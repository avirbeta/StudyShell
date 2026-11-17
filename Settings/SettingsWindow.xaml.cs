using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using StudyShell.Core;
using StudyShell.Services;

namespace StudyShell.Settings
{
    /// <summary>Параметры Study Shell (вкладки).</summary>
    public partial class SettingsWindow : Window
    {
        private static readonly (string name, string c1, string c2)[] Schemes =
        {
            ("Мята", "#39FF88", "#00E5FF"),
            ("Циан", "#00E5FF", "#2E6DFF"),
            ("Синева", "#2E6DFF", "#00C2D4"),
            ("Лаванда", "#B537F2", "#2E6DFF"),
            ("Янтарь", "#FFC233", "#FF2E5B"),
            ("Маджента", "#FF2EA6", "#B537F2"),
        };

        private bool _loading = true;
        private readonly DispatcherTimer _debounce;
        private readonly DispatcherTimer _studyTimer;
        private readonly Dictionary<string, UIElement> _pages = new();

        public SettingsWindow()
        {
            InitializeComponent();
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                App.Settings.Data.DesktopIconSize = (int)IconSizeSlider.Value;
                App.Settings.Data.TaskbarHeight = (int)TaskbarHeightSlider.Value;
                App.Settings.Data.TaskbarOpacity = OpacitySlider.Value / 100.0;
                App.Settings.MarkDirty();
                App.WindowManager?.ApplySettings();
            };
            _studyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _studyTimer.Tick += (_, _) => UpdateStudyPage();

            NavList.ItemsSource = new[]
            {
                new { Id = "appearance", Glyph = "\uE790", Title = "Внешний вид" },
                new { Id = "wallpaper", Glyph = "\uEB9F", Title = "Обои" },
                new { Id = "desktop", Glyph = "\uE7FC", Title = "Рабочий стол" },
                new { Id = "taskbar", Glyph = "\uE7F4", Title = "Панель задач" },
                new { Id = "study", Glyph = "\uE7EF", Title = "Учёба" },
                new { Id = "games", Glyph = "\uE7FC", Title = "Игры" },
                new { Id = "system", Glyph = "\uE713", Title = "Система" },
            };
            _pages["appearance"] = PageAppearance;
            _pages["wallpaper"] = PageWallpaper;
            _pages["desktop"] = PageDesktop;
            _pages["taskbar"] = PageTaskbar;
            _pages["study"] = PageStudy;
            _pages["games"] = PageGames;
            _pages["system"] = PageSystem;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildSchemes();
            BuildWallpapers();
            RefreshAll();
            _loading = false;
            NavList.SelectedIndex = 0;
            _studyTimer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _studyTimer.Stop();
            base.OnClosed(e);
        }

        public void ShowPage(string id)
        {
            foreach (var item in NavList.Items)
            {
                string itemId = item.GetType().GetProperty("Id")?.GetValue(item) as string;
                if (itemId == id)
                {
                    NavList.SelectedItem = item;
                    return;
                }
            }
        }

        private void OnNavChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem == null) return;
            string id = NavList.SelectedItem.GetType().GetProperty("Id")?.GetValue(NavList.SelectedItem) as string;
            foreach (var kv in _pages) kv.Value.Visibility = Visibility.Collapsed;
            if (id != null && _pages.TryGetValue(id, out var page))
                page.Visibility = Visibility.Visible;
        }

        private void RefreshAll()
        {
            _loading = true;
            var s = App.Settings.Data;
            AnimationsChk.IsChecked = s.AnimationsEnabled;
            IconSizeSlider.Value = s.DesktopIconSize;
            TaskbarHeightSlider.Value = s.TaskbarHeight;
            OpacitySlider.Value = s.TaskbarOpacity * 100;
            foreach (ComboBoxItem item in PositionBox.Items)
                if ((string)item.Tag == s.TaskbarPosition) { PositionBox.SelectedItem = item; break; }
            AutoHideChk.IsChecked = s.TaskbarAutoHide;
            SysTrayChk.IsChecked = s.HideSystemTaskbar;
            IconRecycleChk.IsChecked = !s.HiddenBuiltinIcons.Contains("recycle");
            IconSettingsChk.IsChecked = !s.HiddenBuiltinIcons.Contains("settings");
            IconExitChk.IsChecked = !s.HiddenBuiltinIcons.Contains("exit");
            SessionMinutesBox.Text = s.StudySessionMinutes.ToString();
            ScheduleChk.IsChecked = s.StudyScheduleEnabled;
            StudyStartBox.Text = s.StudyStart;
            StudyEndBox.Text = s.StudyEnd;
            StrictChk.IsChecked = s.StudyStrictBlock;
            HardBlockChk.IsChecked = s.HardBlock;
            AutostartChk.IsChecked = AutostartService.IsEnabled();
            UpdateStudyPage();
            _loading = false;
        }

        // ---------- внешний вид ----------

        private void BuildSchemes()
        {
            SchemePanel.Children.Clear();
            foreach (var (name, c1, c2) in Schemes)
            {
                var color1 = ThemeManager.ParseColor(c1, Colors.White);
                var color2 = ThemeManager.ParseColor(c2, Colors.White);
                bool current = App.Settings.Data.AccentHex == c1;

                var card = new Border
                {
                    Width = 116,
                    Height = 58,
                    Margin = new Thickness(0, 0, 10, 10),
                    CornerRadius = new CornerRadius(10),
                    Background = new LinearGradientBrush(color1, color2, new Point(0, 0), new Point(1, 1)),
                    BorderThickness = new Thickness(current ? 3 : 1),
                    BorderBrush = current
                        ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
                        : (Brush)Application.Current.Resources["BorderBrushSoft"],
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = (name, c1, c2),
                };
                card.MouseLeftButtonUp += (_, _) =>
                {
                    App.Settings.Data.AccentHex = c1;
                    App.Settings.Data.AccentSecondaryHex = c2;
                    App.Settings.MarkDirty();
                    App.WindowManager?.ApplySettings();
                    BuildSchemes();
                };
                card.Child = new TextBlock
                {
                    Text = name,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 6),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8, Color = Colors.Black,
                    },
                };
                SchemePanel.Children.Add(card);
            }
        }

        private void OnVisualChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.Data.AnimationsEnabled = AnimationsChk.IsChecked == true;
            App.Settings.MarkDirty();
        }

        // ---------- обои ----------

        private void BuildWallpapers()
        {
            WallpaperPanel.Children.Clear();
            foreach (var preset in WallpaperService.Presets)
            {
                bool current = App.Settings.Data.Wallpaper == "neon://" + preset;
                var card = new Border
                {
                    Width = 140,
                    Height = 84,
                    Margin = new Thickness(0, 0, 10, 10),
                    CornerRadius = new CornerRadius(10),
                    Background = WallpaperService.GetPresetBrush(preset) ?? (Brush)Application.Current.Resources["SurfaceBrush"],
                    BorderThickness = new Thickness(current ? 3 : 1),
                    BorderBrush = current
                        ? (Brush)Application.Current.Resources["AccentBrush"]
                        : (Brush)Application.Current.Resources["BorderBrushSoft"],
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = preset,
                };
                card.MouseLeftButtonUp += (_, _) =>
                {
                    App.Settings.Data.Wallpaper = "neon://" + preset;
                    App.Settings.MarkDirty();
                    App.WindowManager?.ApplySettings();
                    BuildWallpapers();
                };
                card.Child = new TextBlock
                {
                    Text = WallpaperService.PresetName(preset),
                    Foreground = Brushes.White,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 8),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8, Color = Colors.Black,
                    },
                };
                WallpaperPanel.Children.Add(card);
            }
        }

        private void OnBrowseWallpaper(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите изображение",
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Все файлы|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                App.Settings.Data.Wallpaper = WallpaperService.StoreUserImage(dlg.FileName);
                App.Settings.MarkDirty();
                App.WindowManager?.ApplySettings();
                BuildWallpapers();
            }
        }

        // ---------- рабочий стол ----------

        private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            _debounce.Stop();
            _debounce.Start();
        }

        private void OnBuiltinChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var hidden = App.Settings.Data.HiddenBuiltinIcons;
            hidden.Clear();
            if (IconRecycleChk.IsChecked != true) hidden.Add("recycle");
            if (IconSettingsChk.IsChecked != true) hidden.Add("settings");
            if (IconExitChk.IsChecked != true) hidden.Add("exit");
            App.Settings.MarkDirty();
            App.WindowManager?.ApplySettings();
        }

        private void OnResetPositions(object sender, RoutedEventArgs e)
        {
            App.Settings.Data.DesktopIconPositions.Clear();
            App.Settings.MarkDirty();
            App.WindowManager?.ApplySettings();
        }

        private void OnOpenDesktopFolder(object sender, RoutedEventArgs e)
        {
            string dir = App.WindowManager?.DesktopDir
                ?? System.IO.Path.Combine(App.DataDir, "Desktop");
            System.IO.Directory.CreateDirectory(dir);
            AppLauncher.OpenPath(dir);
        }

        // ---------- панель задач ----------

        private void OnPositionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (PositionBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                App.Settings.Data.TaskbarPosition = tag;
                App.Settings.MarkDirty();
                App.WindowManager?.ApplySettings();
            }
        }

        private void OnTaskbarChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.Data.TaskbarAutoHide = AutoHideChk.IsChecked == true;
            App.Settings.MarkDirty();
            App.WindowManager?.ApplySettings();
        }

        private void OnSysTrayChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool hide = SysTrayChk.IsChecked == true;
            if (!hide)
            {
                var res = MessageBox.Show(
                    "Показать системную панель Windows? Панель Study останется поверх неё.",
                    "Study Shell", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes)
                {
                    _loading = true;
                    SysTrayChk.IsChecked = true;
                    _loading = false;
                    return;
                }
            }
            App.Settings.Data.HideSystemTaskbar = hide;
            App.Settings.MarkDirty();
            ShellIntegration.HideSystemTaskbar(hide);
            App.WindowManager?.ApplySettings();
        }

        // ---------- учёба ----------

        private void UpdateStudyPage()
        {
            if (PageStudy.Visibility != Visibility.Visible) return;
            bool active = StudyService.IsActive;
            StudyStatusText.Text = StudyService.StatusText;
            StudyStatusText.Foreground = active
                ? (Brush)Application.Current.Resources["WarningBrush"]
                : (Brush)Application.Current.Resources["TextPrimaryBrush"];
            StudyGlyphText.Foreground = active
                ? (Brush)Application.Current.Resources["WarningBrush"]
                : (Brush)Application.Current.Resources["AccentBrush"];

            var rem = StudyService.Remaining;
            StudyRemainText.Text = StudyService.SessionActive
                ? $"Осталось: {rem.Hours:00}:{rem.Minutes:00}:{rem.Seconds:00}"
                : "";
            StartSessionBtn.Visibility = StudyService.SessionActive ? Visibility.Collapsed : Visibility.Visible;
            StopSessionBtn.Visibility = StudyService.SessionActive ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnStartSession(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(SessionMinutesBox.Text, out int minutes) || minutes < 5)
            {
                App.Notifications.Show("Учёба", "Укажите длительность в минутах (минимум 5).", NotificationKind.Warning);
                return;
            }
            StudyService.StartSession(minutes);
            UpdateStudyPage();
            App.WindowManager?.Desktop?.UpdateStudyWidget();
        }

        private void OnStopSession(object sender, RoutedEventArgs e)
        {
            StudyService.StopSession();
            UpdateStudyPage();
            App.WindowManager?.Desktop?.UpdateStudyWidget();
        }

        private void OnScheduleChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (!TimeSpan.TryParse(StudyStartBox.Text, out _) || !TimeSpan.TryParse(StudyEndBox.Text, out _))
            {
                App.Notifications.Show("Расписание", "Укажите время в формате ЧЧ:ММ (например, 18:00).", NotificationKind.Warning);
                _loading = true;
                ScheduleChk.IsChecked = !ScheduleChk.IsChecked;
                _loading = false;
                return;
            }
            App.Settings.Data.StudyScheduleEnabled = ScheduleChk.IsChecked == true;
            App.Settings.Data.StudyStart = StudyStartBox.Text;
            App.Settings.Data.StudyEnd = StudyEndBox.Text;
            App.Settings.MarkDirty();
            UpdateStudyPage();
        }

        private void OnApplySchedule(object sender, RoutedEventArgs e)
        {
            if (!TimeSpan.TryParse(StudyStartBox.Text, out var start) || !TimeSpan.TryParse(StudyEndBox.Text, out var end))
            {
                App.Notifications.Show("Расписание", "Неверный формат времени. Пример: 18:00", NotificationKind.Warning);
                return;
            }
            if (start == end)
            {
                App.Notifications.Show("Расписание", "Время начала и конца не должно совпадать.", NotificationKind.Warning);
                return;
            }
            App.Settings.Data.StudyScheduleEnabled = ScheduleChk.IsChecked == true;
            App.Settings.Data.StudyStart = StudyStartBox.Text;
            App.Settings.Data.StudyEnd = StudyEndBox.Text;
            App.Settings.MarkDirty();
            UpdateStudyPage();
            App.Notifications.Show("Расписание", "Сохранено: " + StudyService.StatusText, NotificationKind.Success);
        }

        private void OnStrictChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.Data.StudyStrictBlock = StrictChk.IsChecked == true;
            App.Settings.MarkDirty();
        }

        private void OnHardBlockChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.Settings.Data.HardBlock = HardBlockChk.IsChecked == true;
            App.Settings.MarkDirty();
        }

        // ---------- система ----------

        private void OnAutostartChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool enable = AutostartChk.IsChecked == true;
            string exe = Environment.ProcessPath ?? "";
            bool ok = AutostartService.SetEnabled(enable, exe, App.Log);
            App.Settings.Data.Autostart = enable;
            App.Settings.MarkDirty();
            if (!ok)
            {
                _loading = true;
                AutostartChk.IsChecked = AutostartService.IsEnabled();
                _loading = false;
            }
        }

        private void OnExitWindows(object sender, RoutedEventArgs e)
            => App.WindowManager?.ExitShell();

        private void OnOpenLogs(object sender, RoutedEventArgs e)
        {
            if (System.IO.Directory.Exists(App.LogDir))
                AppLauncher.OpenPath(App.LogDir);
        }

        private void OnResetAll(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Сбросить все настройки Study Shell?",
                "Study Shell", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
            App.Settings.ResetToDefaults();
            App.WindowManager?.ApplySettings();
            RefreshAll();
            BuildSchemes();
            BuildWallpapers();
        }

        // ---------- игры ----------

        private void OnRescanGames(object sender, RoutedEventArgs e)
            => App.WindowManager?.RescanGames();

        private void OnRestoreGames(object sender, RoutedEventArgs e)
        {
            App.Settings.Data.HiddenGameIds.Clear();
            App.Settings.MarkDirty();
            App.WindowManager?.RescanGames();
            App.Notifications.Show("Игры", "Скрытые игры возвращены в список.", NotificationKind.Success);
        }
    }
}
