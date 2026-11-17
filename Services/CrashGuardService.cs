using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using StudyShell.Core;

namespace StudyShell.Services
{
    public static class AutostartService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "StudyShell";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static bool SetEnabled(bool enable, string exePath, LogService log)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
                if (key == null) return false;
                if (enable) key.SetValue(ValueName, "\"" + exePath + "\"");
                else key.DeleteValue(ValueName, false);
                return true;
            }
            catch (Exception ex)
            {
                log?.Error("Автозапуск", ex);
                return false;
            }
        }
    }

    public enum CrashDecision { Start, Exit }

    public class CrashGuardService
    {
        private readonly string _dataDir;
        private readonly LogService _log;

        public CrashGuardService(string dataDir, LogService log)
        {
            _dataDir = dataDir;
            _log = log;
        }

        public CrashDecision Evaluate(string[] args)
        {
            try
            {
                bool safeMode = args.Contains("--safemode", StringComparer.OrdinalIgnoreCase);
                string marker = Path.Combine(_dataDir, "running.marker");
                string counterFile = Path.Combine(_dataDir, "crashcounter.json");

                var crashes = LoadCrashes(counterFile);
                if (File.Exists(marker))
                {
                    crashes.Add(DateTime.Now);
                    TryDelete(marker);
                    SaveCrashes(counterFile, crashes);
                    _log.Warn($"Аварийное завершение прошлого сеанса (всего за сутки: {crashes.Count})");
                }
                crashes.RemoveAll(d => d < DateTime.Now.AddHours(24));
                int recent = crashes.Count;

                if (recent >= 3)
                {
                    AutostartService.SetEnabled(false, "", _log);
                    if (!safeMode)
                    {
                        var res = System.Windows.MessageBox.Show(
                            $"Neon Shell аварийно завершался {recent} раз(а) за последние сутки.\n\n" +
                            "Автозапуск отключён автоматически.\n\nПродолжить запуск оболочки?",
                            "Neon Shell — восстановление",
                            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                        if (res != System.Windows.MessageBoxResult.Yes) return CrashDecision.Exit;
                    }
                }

                if (safeMode)
                {
                    var res = System.Windows.MessageBox.Show(
                        "Запуск Neon Shell в безопасном режиме.\n\nПродолжить загрузку оболочки?",
                        "Neon Shell — безопасный режим",
                        System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                    if (res != System.Windows.MessageBoxResult.Yes) return CrashDecision.Exit;
                }
                return CrashDecision.Start;
            }
            catch (Exception ex)
            {
                _log.Error("Проверка сбоев", ex);
                return CrashDecision.Start;
            }
        }

        public void MarkRunning()
        {
            try { File.WriteAllText(Path.Combine(_dataDir, "running.marker"), DateTime.Now.ToString("O")); }
            catch { }
        }

        public void ClearRunning()
        {
            try { TryDelete(Path.Combine(_dataDir, "running.marker")); }
            catch { }
        }

        private static List<DateTime> LoadCrashes(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<List<DateTime>>(File.ReadAllText(path)) ?? new List<DateTime>();
            }
            catch { }
            return new List<DateTime>();
        }

        private static void SaveCrashes(string path, List<DateTime> crashes)
        {
            try { File.WriteAllText(path, JsonSerializer.Serialize(crashes)); }
            catch { }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }

    /// <summary>Запуск приложений и системных страниц.</summary>
    public static class AppLauncher
    {
        public static void LaunchApp(string path, string args = "", string workingDir = "")
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;

                // Во время учёбы (или при жёсткой блокировке) игры и лаунчеры
                // не запускаются из оболочки.
                if (StudyService.IsActive || StudyService.HardBlock)
                {
                    string exeName = "";
                    try { exeName = Path.GetFileNameWithoutExtension(path); } catch { }
                    if (StudyService.IsBlockedName(exeName))
                    {
                        App.Notifications?.Show("Время учёбы",
                            "Игра/лаунчер «" + (string.IsNullOrEmpty(exeName) ? path : exeName) +
                            "» заблокирован(а) во время учёбы.", NotificationKind.Warning);
                        return;
                    }
                }

                if (string.IsNullOrEmpty(workingDir))
                {
                    try { workingDir = Path.GetDirectoryName(path) ?? ""; }
                    catch { workingDir = ""; }
                }
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = string.IsNullOrEmpty(workingDir)
                        ? Environment.GetFolderPath(Environment.SpecialFolder.System)
                        : workingDir,
                };
                System.Diagnostics.Process.Start(psi);
                RecordRecent(path);
            }
            catch (Exception ex)
            {
                App.Log?.Error("Не удалось запустить: " + path, ex);
                App.Notifications?.Show("Ошибка запуска", Path.GetFileName(path) + ": " + ex.Message, NotificationKind.Error);
            }
        }

        public static void OpenPath(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Log?.Error("Не удалось открыть: " + path, ex);
                App.Notifications?.Show("Ошибка", "Не удалось открыть: " + path, NotificationKind.Error);
            }
        }

        public static void OpenSystemSettings(string uri) => OpenPath(uri);

        private static void RecordRecent(string path)
        {
            try
            {
                var settings = App.Settings;
                if (settings == null) return;
                var recent = settings.Data.RecentApps;
                recent.RemoveAll(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase));
                recent.Insert(0, path);
                while (recent.Count > 12) recent.RemoveAt(recent.Count - 1);
                settings.MarkDirty();
            }
            catch { }
        }
    }
}
