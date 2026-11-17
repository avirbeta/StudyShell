using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using StudyShell.Core;
using StudyShell.Desktop;
using StudyShell.Settings;
using StudyShell.StartMenu;
using StudyShell.Taskbar;

namespace StudyShell.Services
{
    /// <summary>Менеджер окон Study Shell.</summary>
    public class WindowManager
    {
        private readonly LogService _log;
        private StartMenuWindow _startMenu;
        private CalendarPopupWindow _calendar;
        private SettingsWindow _settings;
        private TrayService _tray;

        public DesktopWindow Desktop { get; private set; }
        public TaskbarWindow Taskbar { get; private set; }

        public WindowManager(LogService log) => _log = log;

        public string DesktopDir => Path.Combine(App.DataDir, "Desktop");

        public bool IsAnyPopupOpen =>
            (_startMenu?.IsVisible ?? false) ||
            (_calendar?.IsVisible ?? false);

        public void StartShell()
        {
            Desktop = new DesktopWindow();
            Desktop.Show();

            Taskbar = new TaskbarWindow();
            Taskbar.Show();

            _tray = new TrayService(this);
            _tray.Show();

            App.Settings.SettingsChanged += OnSettingsChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _log.Info("Оболочка Study запущена");
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            DispatcherSafe(ApplySettings);
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            DispatcherSafe(() =>
            {
                Desktop?.UpdateBounds();
                Taskbar?.UpdateBounds();
                OnTaskbarLayoutChanged();
            });
        }

        private void DispatcherSafe(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        public void ApplySettings()
        {
            try
            {
                ThemeManager.Apply(App.Settings);
                Taskbar?.UpdateBounds();
                Desktop?.UpdateWallpaper();
                Desktop?.ViewModel.Reload();
                Desktop?.UpdateStudyWidget();
                _startMenu?.UpdatePosition();
                _calendar?.UpdatePosition();
            }
            catch (Exception ex) { _log.Error("ApplySettings", ex); }
        }

        public void OnTaskbarLayoutChanged()
        {
            _startMenu?.UpdatePosition();
            _calendar?.UpdatePosition();
        }

        /// <summary>Пересканировать библиотеку игр.</summary>
        public void RescanGames()
        {
            try
            {
                if (_startMenu != null && _startMenu.IsLoaded)
                    _startMenu.RescanGames();
            }
            catch (Exception ex) { _log.Error("RescanGames", ex); }
        }

        public void ToggleStartMenu()
        {
            if (_startMenu == null || !_startMenu.IsLoaded)
            {
                _startMenu = new StartMenuWindow();
                var w = _startMenu;
                w.Closed += (_, _) => { if (ReferenceEquals(_startMenu, w)) _startMenu = null; };
            }
            if (_startMenu.IsVisible) _startMenu.CloseMenu();
            else
            {
                CloseOtherPopups(_startMenu);
                _startMenu.OpenMenu();
            }
        }

        public void ToggleCalendar()
        {
            if (_calendar == null || !_calendar.IsLoaded)
            {
                _calendar = new CalendarPopupWindow();
                var w = _calendar;
                w.Closed += (_, _) => { if (ReferenceEquals(_calendar, w)) _calendar = null; };
            }
            if (_calendar.IsVisible) _calendar.Hide();
            else
            {
                CloseOtherPopups(_calendar);
                _calendar.OpenMenu();
            }
        }

        private void CloseOtherPopups(Window except)
        {
            if (_startMenu != null && _startMenu != except) _startMenu.CloseMenu();
            if (_calendar != null && _calendar != except) _calendar.Hide();
        }

        public void OpenSettings(string page = null)
        {
            if (_settings == null || !_settings.IsLoaded)
            {
                _settings = new SettingsWindow();
                var w = _settings;
                w.Closed += (_, _) => { if (ReferenceEquals(_settings, w)) _settings = null; };
            }
            if (!_settings.IsVisible) _settings.Show();
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Activate();
            if (!string.IsNullOrEmpty(page)) _settings.ShowPage(page);
        }

        public void OpenFolder(string path)
        {
            if (Directory.Exists(path))
                AppLauncher.OpenPath(path);
        }

        public void ExitShell()
        {
            try
            {
                _log.Info("Выход из Study Shell, восстановление Windows");
                App.Settings.SaveNow();
                _tray?.Dispose();
                _tray = null;
                CloseAllWindows();
                ShellIntegration.RestoreAll();
                App.CrashGuard?.ClearRunning();
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _log.Error("ExitShell", ex);
                Application.Current.Shutdown();
            }
        }

        private void CloseAllWindows()
        {
            try
            {
                _startMenu?.CloseMenu();
                _calendar?.Hide();
                foreach (Window w in Application.Current.Windows)
                {
                    try { w.Close(); }
                    catch { }
                }
            }
            catch { }
        }
    }

    /// <summary>Значок Study в системном трее.</summary>
    public class TrayService : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon _icon;
        private readonly WindowManager _wm;

        public TrayService(WindowManager wm)
        {
            _wm = wm;
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Параметры Study", null, (_, _) => _wm.OpenSettings());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Выйти в Windows (восстановить)", null, (_, _) => _wm.ExitShell());

            System.Drawing.Icon icon = null;
            try { icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath); }
            catch { }

            _icon = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon,
                Text = "Study Shell 1.0",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _icon.DoubleClick += (_, _) => _wm.ToggleStartMenu();
        }

        public void Show() { }

        public void Dispose()
        {
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
            }
            catch { }
        }
    }
}
