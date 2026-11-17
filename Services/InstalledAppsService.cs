using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows.Media;
using StudyShell.Core;
using StudyShell.Core.Helpers;
using StudyShell.Models;

namespace StudyShell.Services
{
    /// <summary>Каталог установленных приложений (ярлыки меню «Пуск» + реестр).</summary>
    public class InstalledAppsService
    {
        private readonly LogService _log;
        private readonly object _lock = new();
        private List<AppEntry> _apps = new();
        private bool _loaded;

        public event EventHandler AppsLoaded;

        public InstalledAppsService(LogService log) => _log = log;

        public bool IsLoaded => _loaded;

        public List<AppEntry> Apps
        {
            get { lock (_lock) return _apps; }
        }

        public IReadOnlyList<AppEntry> BuiltIns { get; } = new List<AppEntry>
        {
            new() { Name = "Параметры Study", BuiltInId = "Settings", IsBuiltIn = true, Category = "Study" },
            new() { Name = "Выйти в Windows", BuiltInId = "Exit", IsBuiltIn = true, Category = "Study" },
            new() { Name = "Перезагрузка", BuiltInId = "Restart", IsBuiltIn = true, Category = "Study" },
            new() { Name = "Выключение", BuiltInId = "Shutdown", IsBuiltIn = true, Category = "Study" },
        };

        public static void LaunchBuiltIn(string id)
        {
            switch (id)
            {
                case "Settings": App.WindowManager?.OpenSettings(); break;
                case "Exit": App.WindowManager?.ExitShell(); break;
                case "Restart": SystemServices.PowerService.Restart(); break;
                case "Shutdown": SystemServices.PowerService.Shutdown(); break;
            }
        }

        public static void Launch(AppEntry entry)
        {
            if (entry == null) return;
            if (entry.IsBuiltIn) { LaunchBuiltIn(entry.BuiltInId); return; }
            AppLauncher.LaunchApp(entry.Path, entry.Args);
        }

        public void LoadAsync()
        {
            Task.Run(() =>
            {
                try
                {
                    var list = new List<AppEntry>();
                    CollectStartMenuShortcuts(list);
                    CollectRegistryApps(list);

                    var unique = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in list)
                    {
                        string key = (e.Path + "|" + e.Name).ToLowerInvariant();
                        if (!unique.ContainsKey(key)) unique[key] = e;
                    }
                    var result = unique.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

                    foreach (var e in result)
                        e.Icon = ResolveIcon(e) ?? IconService.FromGlyph(Glyphs.Apps,
                            System.Windows.Media.Color.FromRgb(0x7E, 0x8A, 0xA0));

                    lock (_lock) { _apps = result; _loaded = true; }
                    _log.Info($"Каталог приложений: {result.Count} записей");
                }
                catch (Exception ex)
                {
                    _log.Error("Каталог приложений", ex);
                    lock (_lock) _loaded = true;
                }
                AppsLoaded?.Invoke(this, EventArgs.Empty);
            });
        }

        private void CollectStartMenuShortcuts(List<AppEntry> list)
        {
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            };
            foreach (var dir in roots)
            {
                if (!Directory.Exists(dir)) continue;
                IEnumerable<string> lnks;
                try { lnks = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories).ToList(); }
                catch { continue; }
                foreach (var lnk in lnks)
                {
                    try
                    {
                        string target = ShortcutResolver.Resolve(lnk, out string args, out string iconPath, out int iconIndex);
                        if (string.IsNullOrEmpty(target)) continue;
                        string rel = Path.GetRelativePath(dir, lnk);
                        string category = Path.GetDirectoryName(rel);
                        category = string.IsNullOrEmpty(category) ? "Главная" : category.Split(Path.DirectorySeparatorChar)[0];
                        list.Add(new AppEntry
                        {
                            Name = Path.GetFileNameWithoutExtension(lnk),
                            Path = target,
                            Args = args,
                            Category = category,
                            IconPath = string.IsNullOrEmpty(iconPath) ? target : iconPath,
                            IconIndex = iconIndex,
                        });
                    }
                    catch { }
                }
            }
        }

        private void CollectRegistryApps(List<AppEntry> list)
        {
            var roots = new[]
            {
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                if (root == null) continue;
                try
                {
                    foreach (var subName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var sub = root.OpenSubKey(subName);
                            if (sub == null) continue;
                            string name = sub.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            if (sub.GetValue("SystemComponent") is int sc && sc == 1) continue;
                            if (sub.GetValue("ParentKeyName") != null) continue;
                            if (!seen.Add(name.Trim().ToLowerInvariant())) continue;

                            string icon = sub.GetValue("DisplayIcon") as string ?? "";
                            string installLoc = sub.GetValue("InstallLocation") as string ?? "";
                            string exe = FindLaunchableExe(icon, installLoc);
                            string iconPath = exe;
                            int iconIndex = 0;
                            if (!string.IsNullOrEmpty(icon))
                            {
                                var parts = icon.Split(',');
                                if (parts.Length > 1 && int.TryParse(parts[1], out var idx)) { iconPath = parts[0]; iconIndex = idx; }
                                else iconPath = icon;
                            }
                            if (string.IsNullOrEmpty(iconPath)) iconPath = exe;

                            list.Add(new AppEntry
                            {
                                Name = name.Trim(),
                                Path = exe,
                                Category = "Установленные",
                                IconPath = iconPath,
                                IconIndex = iconIndex,
                            });
                        }
                        catch { }
                    }
                }
                finally { root.Close(); }
            }
        }

        private static string FindLaunchableExe(string displayIcon, string installLocation)
        {
            if (!string.IsNullOrEmpty(displayIcon))
            {
                string p = displayIcon.Split(',')[0];
                if (File.Exists(p) && Path.GetExtension(p).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.AllDirectories).Take(50))
                        return f;
                }
                catch { }
            }
            return "";
        }

        private static ImageSource ResolveIcon(AppEntry e)
        {
            try
            {
                if (!string.IsNullOrEmpty(e.IconPath))
                {
                    var img = IconService.ForExecutable(e.IconPath, e.IconIndex);
                    if (img != null) return img;
                }
                if (!string.IsNullOrEmpty(e.Path))
                {
                    var img = IconService.ForExecutable(e.Path, 0);
                    if (img != null) return img;
                }
            }
            catch { }
            return null;
        }
    }

    /// <summary>Чтение ярлыков .lnk через WScript.Shell.</summary>
    public static class ShortcutResolver
    {
        public static string Resolve(string lnkPath, out string args, out string iconPath, out int iconIndex)
        {
            args = ""; iconPath = ""; iconIndex = 0;
            object shell = null, sc = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                shell = Activator.CreateInstance(type);
                sc = shell.GetType().InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                if (sc == null) return null;
                var t = sc.GetType();
                string target = t.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null) as string;
                string argsStr = t.InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, sc, null) as string;
                string iconLoc = t.InvokeMember("IconLocation", System.Reflection.BindingFlags.GetProperty, null, sc, null) as string;
                args = argsStr ?? "";
                if (!string.IsNullOrEmpty(iconLoc))
                {
                    var parts = iconLoc.Split(',');
                    iconPath = parts[0];
                    if (parts.Length > 1) int.TryParse(parts[1], out iconIndex);
                }
                return string.IsNullOrEmpty(target) ? null : target;
            }
            catch { return null; }
            finally
            {
                if (sc != null) Marshal.FinalReleaseComObject(sc);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }

        public static bool Create(string lnkPath, string targetPath, string args, string iconLocation)
        {
            object shell = null, sc = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return false;
                shell = Activator.CreateInstance(type);
                sc = shell.GetType().InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                if (sc == null) return false;
                var t = sc.GetType();
                t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { targetPath });
                t.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { args });
                t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc,
                    new object[] { Path.GetDirectoryName(targetPath) ?? "" });
                if (!string.IsNullOrEmpty(iconLocation))
                    t.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { iconLocation });
                t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
                return true;
            }
            catch { return false; }
            finally
            {
                if (sc != null) Marshal.FinalReleaseComObject(sc);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
