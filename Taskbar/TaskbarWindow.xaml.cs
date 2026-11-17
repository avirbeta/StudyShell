using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StudyShell.Core;
using StudyShell.Core.Helpers;
using StudyShell.Models;
using StudyShell.Services;

namespace StudyShell.Taskbar
{
    /// <summary>Неоновая панель задач (системная appbar).</summary>
    public partial class TaskbarWindow : Window
    {
        public class TaskbarPin
        {
            public PinnedApp App { get; set; }
            public ImageSource Icon { get; set; }
        }

        public ObservableCollection<TaskbarPin> PinnedItems { get; } = new();
        public ObservableCollection<RunningAppInfo> RunningItems { get; } = new();

        private readonly DispatcherTimer _runningTimer;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _trayTimer;
        private readonly DispatcherTimer _hideTimer;
        private readonly SystemServices.VolumeService _volume = new();
        private readonly SystemServices.NetworkService _network = new();
        private readonly Dictionary<int, string> _pidPathCache = new();
        private EdgeTriggerWindow _edgeTrigger;
        private double _visibleTop;
        private bool _slideAnimating;
        private AppBarService _appBar;
        private int _appBarEdge = -1;
        private HwndSource _hwndSource;

        public TaskbarWindow()
        {
            InitializeComponent();
            DataContext = this;

            _runningTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
            _runningTimer.Tick += (_, _) => RefreshRunningWindows();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _clockTimer.Tick += (_, _) => UpdateClock();
            _trayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4000) };
            _trayTimer.Tick += (_, _) => UpdateTray();
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); HideToEdge(); };

            MouseEnter += (_, _) => _hideTimer.Stop();
            MouseLeave += (_, _) => MaybeHideLater();
            Deactivated += (_, _) => MaybeHideLater();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _hwndSource?.AddHook(WndProc);

            if (App.Settings.Data.HideSystemTaskbar)
                ShellIntegration.HideSystemTaskbar(true);

            UpdateBounds();
            LoadPins();
            _runningTimer.Start();
            _clockTimer.Start();
            _trayTimer.Start();
            UpdateClock();
            UpdateTray();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _runningTimer.Stop();
            _clockTimer.Stop();
            _trayTimer.Stop();
            _hideTimer.Stop();
            _edgeTrigger?.Close();
            _edgeTrigger = null;
            try { _appBar?.Unregister(); } catch { }
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_appBar != null && _appBar.IsRegistered && msg == _appBar.CallbackMessage)
            {
                if ((uint)wParam.ToInt64() == NativeMethods.ABN_POSCHANGED)
                    Dispatcher.BeginInvoke(new Action(UpdateBounds));
            }
            return IntPtr.Zero;
        }

        public void UpdateBounds()
        {
            try
            {
                var settings = App.Settings.Data;
                Height = settings.TaskbarHeight;
                // неоновая прозрачность панели
                byte a = (byte)(255 * Math.Clamp(settings.TaskbarOpacity, 0.3, 1.0));
                Background = new SolidColorBrush(Color.FromArgb(a, 0x0A, 0x0C, 0x14));
                bool top = settings.TaskbarPosition == "Top";
                var wa = SystemParameters.WorkArea;
                bool sysHidden = settings.HideSystemTaskbar;

                var hwnd = new WindowInteropHelper(this).Handle;
                if (sysHidden && hwnd != IntPtr.Zero)
                {
                    if (_appBar == null) _appBar = new AppBarService();
                    int edge = top ? 1 : 0;
                    if (!_appBar.IsRegistered || _appBarEdge != edge)
                    {
                        _appBar.Register(hwnd, top, Height);
                        _appBarEdge = edge;
                    }
                    else _appBar.UpdatePosition(top, Height);
                    var b = _appBar.Bounds;
                    _visibleTop = b.Top;
                    Left = b.Left;
                    Width = b.Width;
                    if (!_slideAnimating) Top = b.Top;
                }
                else
                {
                    _appBar?.Unregister();
                    _appBarEdge = -1;
                    double x = wa.Left;
                    double w = wa.Width;
                    double y = top ? wa.Top : wa.Bottom - Height;
                    _visibleTop = y;
                    Left = x;
                    Width = w;
                    if (!_slideAnimating) Top = y;
                }

                AccentLine.VerticalAlignment = top ? VerticalAlignment.Bottom : VerticalAlignment.Top;
                RootBorder.BorderThickness = new Thickness(0, top ? 0 : 1, 0, top ? 1 : 0);
                UpdateEdgeTrigger();
                App.WindowManager?.OnTaskbarLayoutChanged();
            }
            catch (Exception ex) { App.Log?.Error("Taskbar UpdateBounds", ex); }
        }

        private void UpdateEdgeTrigger()
        {
            bool need = App.Settings.Data.TaskbarAutoHide;
            if (need)
            {
                if (_edgeTrigger == null)
                {
                    _edgeTrigger = new EdgeTriggerWindow();
                    _edgeTrigger.Triggered += () => Dispatcher.BeginInvoke(new Action(ShowFromEdge));
                    _edgeTrigger.Show();
                }
                var settings = App.Settings.Data;
                bool top = settings.TaskbarPosition == "Top";
                bool sysHidden = settings.HideSystemTaskbar;
                _edgeTrigger.Width = SystemParameters.PrimaryScreenWidth;
                _edgeTrigger.Left = 0;
                if (top)
                    _edgeTrigger.Top = sysHidden ? 0 : SystemParameters.WorkArea.Top - 2;
                else
                    _edgeTrigger.Top = sysHidden ? SystemParameters.PrimaryScreenHeight - 2 : SystemParameters.WorkArea.Bottom - 2;
            }
            else
            {
                _edgeTrigger?.Close();
                _edgeTrigger = null;
            }
        }

        public void ShowFromEdge()
        {
            if (!App.Settings.Data.TaskbarAutoHide)
            {
                if (!IsVisible) Show();
                return;
            }
            if (!IsVisible) Show();
            _hideTimer.Stop();
            if (!App.AnimationsEnabled) { Top = _visibleTop; return; }
            _slideAnimating = true;
            var anim = new DoubleAnimation(Top, _visibleTop, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            anim.Completed += (_, _) => { _slideAnimating = false; Top = _visibleTop; };
            BeginAnimation(TopProperty, anim);
        }

        private void MaybeHideLater()
        {
            if (!App.Settings.Data.TaskbarAutoHide) return;
            if (App.WindowManager != null && App.WindowManager.IsAnyPopupOpen) return;
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private void HideToEdge()
        {
            if (!App.Settings.Data.TaskbarAutoHide || !IsVisible) return;
            double offscreen = App.Settings.Data.TaskbarPosition == "Top"
                ? -Height - 2
                : SystemParameters.PrimaryScreenHeight + 2;
            if (!App.AnimationsEnabled) { Hide(); return; }
            _slideAnimating = true;
            var anim = new DoubleAnimation(Top, offscreen, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            anim.Completed += (_, _) => { _slideAnimating = false; Hide(); };
            BeginAnimation(TopProperty, anim);
        }

        // ---------- закреплённые ----------

        private void LoadPins()
        {
            PinnedItems.Clear();
            var settings = App.Settings.Data;
            if (!settings.PinnedInitialized)
            {
                settings.PinnedInitialized = true;
                settings.PinnedApps.Clear();
                settings.PinnedApps.Add(new PinnedApp { Name = "Параметры Study", BuiltInId = "Settings" });
                App.Settings.MarkDirty();
            }
            foreach (var p in settings.PinnedApps)
                PinnedItems.Add(new TaskbarPin { App = p, Icon = ResolvePinIcon(p) });
        }

        private static ImageSource ResolvePinIcon(PinnedApp p)
        {
            if (p.IsBuiltIn)
            {
                (string glyph, string color) = p.BuiltInId switch
                {
                    "Settings" => (Glyphs.Settings, "#39FF88"),
                    _ => (Glyphs.Apps, "#7E8AA0"),
                };
                return IconService.FromGlyph(glyph, ThemeManager.ParseColor(color, Colors.White));
            }
            var img = IconService.ForExecutable(p.Path);
            return img ?? IconService.ForPath(p.Path);
        }

        public void RefreshPins() => LoadPins();

        private void OnPinnedClick(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is TaskbarPin pin)
            {
                if (pin.App.IsBuiltIn) InstalledAppsService.LaunchBuiltIn(pin.App.BuiltInId);
                else AppLauncher.LaunchApp(pin.App.Path, pin.App.Args);
            }
        }

        private void OnPinnedRightClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as Button)?.Tag is TaskbarPin pin)
            {
                var menu = new ContextMenu();
                AddItem(menu, "Запустить", Glyphs.Play, () =>
                {
                    if (pin.App.IsBuiltIn) InstalledAppsService.LaunchBuiltIn(pin.App.BuiltInId);
                    else AppLauncher.LaunchApp(pin.App.Path, pin.App.Args);
                });
                menu.Items.Add(new Separator());
                AddItem(menu, "Открепить", Glyphs.Unpin, () =>
                {
                    App.Settings.Data.PinnedApps.RemoveAll(x =>
                        x.BuiltInId == pin.App.BuiltInId && x.Path == pin.App.Path);
                    App.Settings.MarkDirty();
                    PinnedItems.Remove(pin);
                });
                menu.IsOpen = true;
            }
            e.Handled = true;
        }

        // ---------- открытые окна ----------

        private void RefreshRunningWindows()
        {
            try
            {
                IntPtr shell = NativeMethods.GetShellWindow();
                int selfPid = Environment.ProcessId;
                var ownHwnds = new HashSet<IntPtr>();
                foreach (Window w in Application.Current.Windows)
                {
                    var h = new WindowInteropHelper(w).Handle;
                    if (h != IntPtr.Zero) ownHwnds.Add(h);
                }

                var seen = new List<(IntPtr hwnd, string title, uint pid, bool iconic, bool active)>();
                IntPtr fg = NativeMethods.GetForegroundWindow();

                NativeMethods.EnumWindows((h, l) =>
                {
                    if (h == shell || h == IntPtr.Zero || ownHwnds.Contains(h)) return true;
                    if (!NativeMethods.IsWindowVisible(h)) return true;
                    int len = NativeMethods.GetWindowTextLength(h);
                    if (len <= 0) return true;
                    long style = NativeMethods.GetWindowStyle(h);
                    if ((style & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
                    NativeMethods.DwmGetWindowAttribute(h, NativeMethods.DWMWA_CLOAKED, out int cloaked, 4);
                    if (cloaked != 0) return true;
                    NativeMethods.GetWindowThreadProcessId(h, out uint pid);
                    if (pid == selfPid) return true;
                    var sb = new StringBuilder(len + 1);
                    NativeMethods.GetWindowText(h, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (string.IsNullOrWhiteSpace(title)) return true;
                    seen.Add((h, title, pid, NativeMethods.IsIconic(h), h == fg));
                    return true;
                }, IntPtr.Zero);

                var newItems = new List<RunningAppInfo>();
                foreach (var (hwnd, title, pid, iconic, active) in seen)
                {
                    string path = ResolvePidPath(pid);
                    var existing = RunningItems.FirstOrDefault(r => r.Hwnd == hwnd);
                    if (existing == null)
                    {
                        existing = new RunningAppInfo
                        {
                            Hwnd = hwnd,
                            Pid = (int)pid,
                            Path = path,
                            Icon = string.IsNullOrEmpty(path) ? null : IconService.ForExecutable(path),
                        };
                        newItems.Add(existing);
                    }
                    existing.Title = title;
                    existing.IsMinimized = iconic;
                    existing.IsActive = active;
                }
                foreach (var n in newItems) RunningItems.Add(n);
                for (int i = RunningItems.Count - 1; i >= 0; i--)
                {
                    if (!seen.Any(s => s.hwnd == RunningItems[i].Hwnd))
                        RunningItems.RemoveAt(i);
                }
            }
            catch (Exception ex)
            {
                App.Log?.Error("RefreshRunningWindows", ex);
            }
        }

        private string ResolvePidPath(uint pid)
        {
            try
            {
                if (_pidPathCache.TryGetValue((int)pid, out var cached)) return cached;
                using var p = Process.GetProcessById((int)pid);
                string path = p.MainModule?.FileName ?? "";
                _pidPathCache[(int)pid] = path;
                return path;
            }
            catch { return ""; }
        }

        private void OnRunningClick(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is RunningAppInfo info)
                ToggleWindow(info.Hwnd);
        }

        private void OnRunningRightClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as Button)?.Tag is RunningAppInfo info)
            {
                var menu = new ContextMenu();
                AddItem(menu, info.IsMinimized ? "Развернуть" : "Свернуть", Glyphs.Play, () =>
                {
                    if (NativeMethods.IsIconic(info.Hwnd))
                    {
                        NativeMethods.ShowWindow(info.Hwnd, NativeMethods.SW_RESTORE);
                        ForceForeground(info.Hwnd);
                    }
                    else NativeMethods.ShowWindow(info.Hwnd, NativeMethods.SW_MINIMIZE);
                });
                AddItem(menu, "Закрыть окно", Glyphs.Close, () =>
                    NativeMethods.PostMessage(info.Hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
                menu.Items.Add(new Separator());
                if (!string.IsNullOrEmpty(info.Path))
                {
                    AddItem(menu, "Закрепить в Neon", Glyphs.Pin, () =>
                    {
                        App.Settings.Data.PinnedApps.Add(new PinnedApp
                        {
                            Name = System.IO.Path.GetFileNameWithoutExtension(info.Path),
                            Path = info.Path,
                        });
                        App.Settings.MarkDirty();
                        RefreshPins();
                    });
                }
                AddItem(menu, "Диспетчер задач Windows", Glyphs.Monitor, () =>
                    AppLauncher.LaunchApp(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskmgr.exe")));
                menu.IsOpen = true;
            }
            e.Handled = true;
        }

        private void ToggleWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            if (NativeMethods.IsIconic(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                ForceForeground(hwnd);
            }
            else if (NativeMethods.GetForegroundWindow() == hwnd)
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
            else ForceForeground(hwnd);
        }

        private static void ForceForeground(IntPtr hwnd)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint targetThread);
            NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint fgThread);
            bool attached = false;
            try
            {
                if (targetThread != fgThread)
                    attached = NativeMethods.AttachThreadInput(targetThread, fgThread, true);
                NativeMethods.BringWindowToTop(hwnd);
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached) NativeMethods.AttachThreadInput(targetThread, fgThread, false);
            }
        }

        // ---------- трей ----------

        private void UpdateClock()
        {
            var now = DateTime.Now;
            TimeText.Text = now.ToString("HH:mm");
            DateText.Text = now.ToString("dd.MM.yyyy");
            ClockBtn.ToolTip = now.ToString("dddd, d MMMM yyyy");

            // обратный отсчёт сессии учёбы
            if (StudyService.IsActive && StudyService.SessionActive)
            {
                var rem = StudyService.Remaining;
                StudyTimeText.Text = $"{rem.Hours:00}:{rem.Minutes:00}:{rem.Seconds:00}";
                StudyTimeText.Visibility = Visibility.Visible;
                StudyBtn.Visibility = Visibility.Visible;
            }
        }

        private void UpdateTray()
        {
            try
            {
                // индикатор времени учёбы
                bool study = StudyService.IsActive;
                StudyBtn.Visibility = study ? Visibility.Visible : Visibility.Collapsed;
                StudyBtn.ToolTip = "Время учёбы: игры заблокированы\n" + StudyService.StatusText;
                if (study && StudyService.SessionActive)
                {
                    var rem = StudyService.Remaining;
                    StudyTimeText.Text = $"{rem.Hours:00}:{rem.Minutes:00}:{rem.Seconds:00}";
                    StudyTimeText.Visibility = Visibility.Visible;
                }
                else
                {
                    StudyTimeText.Visibility = Visibility.Collapsed;
                }

                if (SystemServices.BatteryService.IsPresent)
                {
                    BatteryBtn.Visibility = Visibility.Visible;
                    int p = SystemServices.BatteryService.Percent;
                    bool charging = SystemServices.BatteryService.IsCharging;
                    int idx = Math.Clamp((int)Math.Round(p / 10.0), 0, 9);
                    BatteryBtn.Content = charging ? "\uE85A" : ((char)(0xE850 + idx)).ToString();
                    BatteryBtn.ToolTip = charging ? $"Батарея: {p}% (заряжается)" : $"Батарея: {p}%";
                }
                else BatteryBtn.Visibility = Visibility.Collapsed;

                _network.Refresh();
                NetworkBtn.Content = !_network.IsConnected ? Glyphs.Globe
                    : _network.IsWifi ? Glyphs.Wifi : Glyphs.Ethernet;
                NetworkBtn.ToolTip = _network.IsConnected
                    ? $"{_network.InterfaceName}\n{_network.IpAddress}"
                    : "Нет сети";

                double v = _volume.GetVolume();
                bool mute = _volume.GetMute();
                VolumeBtn.Content = mute || v <= 0.001 ? Glyphs.VolumeMute
                    : v < 0.34 ? Glyphs.VolumeLow : Glyphs.Volume;
                VolumeBtn.ToolTip = mute ? "Звук выключен" : $"Громкость: {(int)(v * 100)}%";
            }
            catch (Exception ex)
            {
                App.Log?.Error("UpdateTray", ex);
            }
        }

        private void OnBatteryClick(object sender, RoutedEventArgs e)
            => App.WindowManager?.OpenSettings("study");

        private void OnNetworkClick(object sender, RoutedEventArgs e)
            => App.WindowManager?.OpenSettings("study");

        private void OnVolumeClick(object sender, RoutedEventArgs e)
            => App.WindowManager?.OpenSettings("study");

        private void OnClockClick(object sender, RoutedEventArgs e)
            => App.WindowManager?.ToggleCalendar();

        private void OnStudyClick(object sender, RoutedEventArgs e)
            => App.WindowManager?.OpenSettings("study");

        private void OnStartClick(object sender, RoutedEventArgs e)
            => App.WindowManager?.ToggleStartMenu();

        private void OnShowDesktop(object sender, RoutedEventArgs e)
        {
            IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
                NativeMethods.SendMessage(tray, 0x5A9, IntPtr.Zero, IntPtr.Zero);
        }

        // ---------- контекстное меню панели ----------

        private void OnTaskbarRightDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Right) return;
            var src = e.OriginalSource as DependencyObject;
            while (src != null && !(src is Button) && !(src is TextBlock) && !(src is Image))
            {
                src = VisualTreeHelper.GetParent(src) ?? (src as FrameworkContentElement)?.Parent;
            }
            if (src is Button || src is TextBlock || src is Image) return;

            var settings = App.Settings.Data;
            var menu = new ContextMenu();
            AddItem(menu, "Параметры Study", Glyphs.Settings, () => App.WindowManager?.OpenSettings());
            menu.Items.Add(new Separator());

            var autoHide = new MenuItem
            {
                Header = "Автоматически скрывать панель",
                IsCheckable = true,
                IsChecked = settings.TaskbarAutoHide,
            };
            autoHide.Click += (_, _) =>
            {
                settings.TaskbarAutoHide = autoHide.IsChecked;
                App.Settings.MarkDirty();
                UpdateBounds();
            };
            menu.Items.Add(autoHide);

            var pos = new MenuItem { Header = "Положение панели" };
            var bottom = new MenuItem { Header = "Снизу", IsCheckable = true, IsChecked = settings.TaskbarPosition == "Bottom" };
            bottom.Click += (_, _) => { settings.TaskbarPosition = "Bottom"; App.Settings.MarkDirty(); UpdateBounds(); };
            var top = new MenuItem { Header = "Сверху", IsCheckable = true, IsChecked = settings.TaskbarPosition == "Top" };
            top.Click += (_, _) => { settings.TaskbarPosition = "Top"; App.Settings.MarkDirty(); UpdateBounds(); };
            pos.Items.Add(bottom);
            pos.Items.Add(top);
            menu.Items.Add(pos);

            var sysTray = new MenuItem
            {
                Header = "Показать системную панель Windows",
                IsCheckable = true,
                IsChecked = !settings.HideSystemTaskbar,
            };
            sysTray.Click += (_, _) =>
            {
                settings.HideSystemTaskbar = !sysTray.IsChecked;
                App.Settings.MarkDirty();
                ShellIntegration.HideSystemTaskbar(settings.HideSystemTaskbar);
                UpdateBounds();
            };
            menu.Items.Add(sysTray);

            menu.Items.Add(new Separator());
            AddItem(menu, "Выйти в Windows (восстановить)", Glyphs.Home, () => App.WindowManager?.ExitShell());

            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private void AddItem(ContextMenu menu, string header, string glyph, Action action)
        {
            var item = new MenuItem
            {
                Header = header,
                Icon = glyph == null ? null : new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
    }

    /// <summary>Полоска у края экрана для автопоказа панели.</summary>
    public class EdgeTriggerWindow : Window
    {
        public event Action Triggered;

        public EdgeTriggerWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Background = Brushes.Transparent;
            Height = 3;
            AllowsTransparency = false;
            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.SetWindowStyleFlag(hwnd,
                    NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW, true);
            };
            MouseEnter += (_, _) => Triggered?.Invoke();
        }
    }
}
