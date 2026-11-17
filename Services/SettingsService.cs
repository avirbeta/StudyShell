using System;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using StudyShell.Models;

namespace StudyShell.Services
{
    public class SettingsService
    {
        private readonly string _path;
        private readonly LogService _log;
        private readonly DispatcherTimer _saveTimer;
        private bool _loaded;
        private bool _dirty;

        public AppSettings Data { get; private set; } = new();
        public bool AutoTunedPerformance { get; private set; }

        public event EventHandler SettingsChanged;

        public SettingsService(string path, LogService log)
        {
            _path = path;
            _log = log;
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (_, _) => SaveNow();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
                    if (loaded != null) Data = loaded;
                }
                _loaded = true;
            }
            catch (Exception ex)
            {
                _log.Error("Ошибка загрузки настроек", ex);
                Data = new AppSettings();
                _loaded = true;
            }
            Normalize();
        }

        public void MarkDirty()
        {
            if (!_loaded) return;
            _dirty = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void SaveNow()
        {
            _saveTimer.Stop();
            if (!_dirty) return;
            _dirty = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                _log.Error("Не удалось сохранить настройки", ex);
            }
        }

        public void ResetToDefaults()
        {
            Data = new AppSettings();
            _dirty = true;
            SaveNow();
            RaiseChanged();
        }

        public void RaiseChanged() => SettingsChanged?.Invoke(this, EventArgs.Empty);

        private void Normalize()
        {
            if (Data.DesktopIconSize < 48 || Data.DesktopIconSize > 110) Data.DesktopIconSize = 76;
            if (Data.TaskbarHeight < 40 || Data.TaskbarHeight > 80) Data.TaskbarHeight = 56;
            if (Data.TaskbarOpacity < 0.3 || Data.TaskbarOpacity > 1.0) Data.TaskbarOpacity = 0.88;
            if (Data.NotificationDurationSec < 2 || Data.NotificationDurationSec > 60) Data.NotificationDurationSec = 5;
            if (Data.TaskbarPosition != "Top") Data.TaskbarPosition = "Bottom";
            Data.PinnedApps ??= new();
            Data.RecentApps ??= new();
            Data.DesktopIconPositions ??= new();
            Data.HiddenBuiltinIcons ??= new();
            Data.ManualApps ??= new();
            Data.ManualGames ??= new();
            Data.HiddenGameIds ??= new();
        }
    }
}
