using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using StudyShell.Core;
using StudyShell.Core.Helpers;
using StudyShell.Models;
using StudyShell.Services;

namespace StudyShell.ViewModels
{
    public class DesktopItem : ObservableObject
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsBuiltIn { get; set; }
        public string BuiltInId { get; set; } = "";
        public bool IsFolder { get; set; }
        public ImageSource Icon { get; set; }

        private double _x;
        public double X { get => _x; set => Set(ref _x, value); }

        private double _y;
        public double Y { get => _y; set => Set(ref _y, value); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    }

    /// <summary>Рабочий стол Neon: встроенные значки + папка StudyShell\Desktop.</summary>
    public class DesktopViewModel
    {
        private static readonly (string id, string name, string glyph, string color)[] BuiltIns =
        {
            ("recycle", "Корзина", Glyphs.Delete, "#7E8AA0"),
            ("settings", "Параметры Study", Glyphs.Settings, "#39FF88"),
            ("exit", "Выйти в Windows", Glyphs.Home, "#39FF88"),
        };

        public ObservableCollection<DesktopItem> Items { get; } = new();
        public double IconSize { get; private set; } = 76;
        public string DesktopDir { get; private set; } = "";

        public void Initialize()
        {
            DesktopDir = Path.Combine(App.DataDir, "Desktop");
            Directory.CreateDirectory(DesktopDir);
        }

        public void Reload()
        {
            try
            {
                var settings = App.Settings.Data;
                IconSize = settings.DesktopIconSize;
                Items.Clear();
                AddBuiltIns();
                AddFolderContents();
                ApplyPositions();
                SavePositions();
            }
            catch (Exception ex)
            {
                App.Log?.Error("Загрузка рабочего стола", ex);
            }
        }

        private void AddBuiltIns()
        {
            foreach (var (id, name, glyph, color) in BuiltIns)
            {
                if (App.Settings.Data.HiddenBuiltinIcons.Contains(id)) continue;
                Items.Add(new DesktopItem
                {
                    Id = id,
                    Name = name,
                    IsBuiltIn = true,
                    BuiltInId = id,
                    Icon = IconService.FromGlyph(glyph, ThemeManager.ParseColor(color, Colors.White)),
                });
            }
        }

        private void AddFolderContents()
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(DesktopDir).OrderBy(e => e, StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    bool isFolder = Directory.Exists(entry);
                    string name = Path.GetFileName(entry);
                    string target = entry;
                    if (!isFolder && Path.GetExtension(entry).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        string resolved = ShortcutResolver.Resolve(entry, out _, out _, out _);
                        if (string.IsNullOrEmpty(resolved)) continue;
                        target = resolved;
                    }
                    Items.Add(new DesktopItem
                    {
                        Id = "fs:" + entry.ToLowerInvariant(),
                        Name = name,
                        Path = target,
                        IsFolder = isFolder,
                        Icon = IconService.ForPath(isFolder ? entry : target),
                    });
                }
                catch { }
            }
        }

        private void ApplyPositions()
        {
            var dict = App.Settings.Data.DesktopIconPositions.ToDictionary(p => p.Id, p => p);
            double x = 20, y = 20;
            double step = Math.Max(IconSize + 30, 96);
            foreach (var item in Items)
            {
                if (dict.TryGetValue(item.Id, out var pos))
                {
                    item.X = pos.X;
                    item.Y = pos.Y;
                }
                else
                {
                    item.X = x;
                    item.Y = y;
                    y += step;
                    if (y > SystemParameters.VirtualScreenHeight - step - 70)
                    {
                        y = 20;
                        x += step;
                    }
                }
            }
        }

        public void SavePositions()
        {
            try
            {
                App.Settings.Data.DesktopIconPositions = Items
                    .Select(i => new DesktopIconPos { Id = i.Id, X = i.X, Y = i.Y })
                    .ToList();
                App.Settings.MarkDirty();
            }
            catch { }
        }

        public void ResetPositions()
        {
            App.Settings.Data.DesktopIconPositions.Clear();
            App.Settings.MarkDirty();
            Reload();
        }

        public void Launch(DesktopItem item)
        {
            if (item == null) return;
            if (item.IsBuiltIn)
            {
                switch (item.BuiltInId)
                {
                    case "recycle": AppLauncher.OpenPath("::{645FF040-5081-101B-9F08-00AA002F954E}"); break;
                    case "settings": App.WindowManager?.OpenSettings(); break;
                    case "exit": App.WindowManager?.ExitShell(); break;
                }
                return;
            }
            if (item.IsFolder) { App.WindowManager?.OpenFolder(item.Path); return; }
            AppLauncher.LaunchApp(item.Path);
        }

        public void Delete(List<DesktopItem> items, out string errors)
        {
            var real = items.Where(i => !i.IsBuiltIn)
                .Select(i => Path.Combine(DesktopDir, i.Name)).ToArray();
            var errs = new List<string>();
            foreach (var p in real)
            {
                try
                {
                    if (Directory.Exists(p))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(p,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    else if (File.Exists(p))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                catch (Exception ex) { errs.Add(ex.Message); }
            }
            errors = string.Join(Environment.NewLine, errs);
            Reload();
        }

        public void Rename(DesktopItem item, string newName)
        {
            if (item == null || item.IsBuiltIn) return;
            string src = Path.Combine(DesktopDir, item.Name);
            string dest = Path.Combine(DesktopDir, newName);
            if (Directory.Exists(src)) Directory.Move(src, dest);
            else File.Move(src, dest);
            Reload();
        }

        public void CreateFolder(string name)
        {
            Directory.CreateDirectory(Path.Combine(DesktopDir, Unique(DesktopDir, name, "")));
            Reload();
        }

        public void CreateTextFile(string name)
        {
            File.WriteAllText(Path.Combine(DesktopDir, Unique(DesktopDir, name, ".txt")), "");
            Reload();
        }

        public void CreateShortcut(string name, string targetPath, string args)
        {
            string lnk = Path.Combine(DesktopDir, Unique(DesktopDir, name, ".lnk"));
            ShortcutResolver.Create(lnk, targetPath, args, "");
            Reload();
        }

        public void ImportFiles(string[] paths)
        {
            foreach (var p in paths)
            {
                try
                {
                    string dest = Path.Combine(DesktopDir, Path.GetFileName(p));
                    if (File.Exists(p)) File.Copy(p, dest, true);
                    else if (Directory.Exists(p) && !Directory.Exists(dest)) CopyDir(p, dest);
                }
                catch { }
            }
            Reload();
        }

        private static void CopyDir(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.EnumerateFiles(src))
                File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            foreach (var d in Directory.EnumerateDirectories(src))
                CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
        }

        private static string Unique(string dir, string baseName, string ext)
        {
            string name = baseName + ext;
            string full = Path.Combine(dir, name);
            int i = 2;
            while (File.Exists(full) || Directory.Exists(full))
            {
                name = $"{baseName} ({i}){ext}";
                full = Path.Combine(dir, name);
                i++;
            }
            return name;
        }
    }
}
