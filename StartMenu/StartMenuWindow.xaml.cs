using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StudyShell.Core;
using StudyShell.Core.Helpers;
using StudyShell.Models;
using StudyShell.Services;

namespace StudyShell.StartMenu
{
    /// <summary>
    /// Меню Study Shell: приложения для учёбы + все игры (видимы,
    /// но полностью заблокированы во время учёбы).
    /// </summary>
    public partial class StartMenuWindow : Window
    {
        public ObservableCollection<AppEntry> AppsView { get; } = new();
        public ObservableCollection<GameEntry> Games { get; } = new();

        private List<AppEntry> _allApps = new();
        private List<GameEntry> _allGames = new();
        private bool _closing;

        public StartMenuWindow()
        {
            InitializeComponent();
            AppsItems.ItemsSource = AppsView;
            GamesItems.ItemsSource = Games;
            App.Apps.AppsLoaded += (_, _) => Dispatcher.BeginInvoke(new Action(RebuildApps));
            RebuildApps();
            ScanGames();
        }

        // ---------- приложения ----------

        private void RebuildApps()
        {
            _allApps = new List<AppEntry>();
            foreach (var b in App.Apps.BuiltIns)
            {
                b.Icon ??= IconService.FromGlyph(BuiltInGlyph(b), BuiltInColor(b));
                _allApps.Add(b);
            }
            if (App.Apps.IsLoaded) _allApps.AddRange(App.Apps.Apps);

            foreach (var m in App.Settings.Data.ManualApps)
            {
                if (string.IsNullOrEmpty(m.Path) || !System.IO.File.Exists(m.Path)) continue;
                _allApps.Add(new AppEntry
                {
                    Name = string.IsNullOrEmpty(m.Name) ? System.IO.Path.GetFileNameWithoutExtension(m.Path) : m.Name,
                    Path = m.Path,
                    Args = m.Args,
                    Category = "Мои приложения",
                    Icon = IconService.ForExecutable(m.Path) ?? IconService.ForPath(m.Path),
                });
            }
            ApplyFilters();
        }

        private static string BuiltInGlyph(AppEntry b) => b.BuiltInId switch
        {
            "Settings" => Glyphs.Settings,
            "Exit" => Glyphs.Home,
            "Restart" => Glyphs.Restart,
            "Shutdown" => Glyphs.Power,
            _ => Glyphs.Apps,
        };

        private static Color BuiltInColor(AppEntry b) => b.BuiltInId switch
        {
            "Settings" => ThemeManager.ParseColor("#F0A94B", Colors.White),
            "Exit" => ThemeManager.ParseColor("#7FB069", Colors.White),
            "Restart" => ThemeManager.ParseColor("#FFC233", Colors.White),
            "Shutdown" => ThemeManager.ParseColor("#FF2E5B", Colors.White),
            _ => ThemeManager.ParseColor("#7E8AA0", Colors.White),
        };

        private void OnAppClick(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is AppEntry app)
            {
                CloseMenu();
                InstalledAppsService.Launch(app);
            }
        }

        private void OnAppRightClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as Button)?.Tag is AppEntry app)
            {
                bool isManual = app.Category == "Мои приложения";
                var menu = new ContextMenu();
                AddItem(menu, "Запустить", Glyphs.Play, () => { CloseMenu(); InstalledAppsService.Launch(app); });
                if (isManual)
                {
                    menu.Items.Add(new Separator());
                    AddItem(menu, "Удалить из списка", Glyphs.Close, () =>
                    {
                        App.Settings.Data.ManualApps.RemoveAll(m =>
                            string.Equals(m.Path, app.Path, StringComparison.OrdinalIgnoreCase));
                        App.Settings.MarkDirty();
                        RebuildApps();
                    });
                }
                menu.IsOpen = true;
            }
            e.Handled = true;
        }

        private void OnAddApp(object sender, RoutedEventArgs e)
        {
            var dlg = new Dialogs.AddAppDialog { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.AppPath))
            {
                App.Settings.Data.ManualApps.RemoveAll(m =>
                    string.Equals(m.Path, dlg.AppPath, StringComparison.OrdinalIgnoreCase));
                App.Settings.Data.ManualApps.Add(new ManualApp
                {
                    Name = dlg.AppName,
                    Path = dlg.AppPath,
                    Args = dlg.Arguments,
                });
                App.Settings.MarkDirty();
                RebuildApps();
                App.Notifications.Show("Приложения", "Добавлено: " + dlg.AppName, NotificationKind.Success);
            }
        }

        // ---------- игры ----------

        public void RescanGames() => ScanGames();

        private void ScanGames()
        {
            try
            {
                _allGames = GameScanner.Scan(App.Settings.Data, App.Log)
                    .Where(g => !App.Settings.Data.HiddenGameIds.Contains(g.Id))
                    .ToList();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                App.Log?.Error("Сканирование игр", ex);
            }
        }

        private void OnGameClick(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is GameEntry game)
            {
                if (game.IsLocked)
                {
                    App.Notifications.Show("Время учёбы",
                        "Игра «" + game.Name + "» заблокирована. Управление — Параметры → Учёба.",
                        NotificationKind.Warning);
                    return;
                }
                CloseMenu();
                GameScanner.LaunchGame(game, App.Log);
            }
        }

        private void OnGameRightClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as Button)?.Tag is GameEntry game)
            {
                var menu = new ContextMenu();
                AddItem(menu, "Запустить", Glyphs.Play, () =>
                {
                    if (game.IsLocked)
                    {
                        App.Notifications.Show("Время учёбы", "Игра заблокирована во время учёбы.", NotificationKind.Warning);
                        return;
                    }
                    CloseMenu();
                    GameScanner.LaunchGame(game, App.Log);
                });
                if (!game.IsSteamUri && !string.IsNullOrEmpty(game.WorkingDir) &&
                    System.IO.Directory.Exists(game.WorkingDir))
                {
                    AddItem(menu, "Папка игры", Glyphs.Folder, () => AppLauncher.OpenPath(game.WorkingDir));
                }
                menu.Items.Add(new Separator());
                AddItem(menu, "Скрыть из списка", Glyphs.Close, () =>
                {
                    if (!App.Settings.Data.HiddenGameIds.Contains(game.Id))
                        App.Settings.Data.HiddenGameIds.Add(game.Id);
                    App.Settings.MarkDirty();
                    ScanGames();
                });
                menu.IsOpen = true;
            }
            e.Handled = true;
        }

        private void OnAddGame(object sender, RoutedEventArgs e)
        {
            var dlg = new Dialogs.AddAppDialog { Owner = this, Title = "Добавить игру" };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.AppPath))
            {
                App.Settings.Data.ManualGames.RemoveAll(m =>
                    string.Equals(m.Path, dlg.AppPath, StringComparison.OrdinalIgnoreCase));
                App.Settings.Data.ManualGames.Add(new ManualGame
                {
                    Name = dlg.AppName,
                    Path = dlg.AppPath,
                    Args = dlg.Arguments,
                });
                App.Settings.Data.HiddenGameIds.Remove("manual:" + dlg.AppPath.ToLowerInvariant());
                App.Settings.MarkDirty();
                ScanGames();
            }
        }

        private void OnRescanGames(object sender, RoutedEventArgs e) => ScanGames();

        // ---------- фильтры и вкладки ----------

        private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            string q = SearchBox.Text?.Trim() ?? "";
            bool study = StudyService.IsActive;

            IEnumerable<AppEntry> apps = _allApps;
            if (!string.IsNullOrEmpty(q))
                apps = apps.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            AppsView.Clear();
            foreach (var a in apps) AppsView.Add(a);
            AppsEmptyText.Visibility = AppsView.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var games = string.IsNullOrEmpty(q)
                ? _allGames
                : _allGames.Where(g => g.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            Games.Clear();
            foreach (var g in games)
            {
                g.IsLocked = study;
                Games.Add(g);
            }

            StatusLine.Text = study ? "Время учёбы · игры заблокированы" : StudyService.StatusText;
            GamesLockNote.Text = study ? "Сейчас время учёбы — все игры заблокированы" : "";
        }

        private void OnAppsTab(object sender, RoutedEventArgs e)
        {
            AppsPanel.Visibility = Visibility.Visible;
            GamesPanel.Visibility = Visibility.Collapsed;
            AppsTab.Background = (Brush)Application.Current.Resources["AccentBrushSoft"];
            GamesTab.Background = (Brush)Application.Current.Resources["SurfaceBrush"];
        }

        private void OnGamesTab(object sender, RoutedEventArgs e)
        {
            AppsPanel.Visibility = Visibility.Collapsed;
            GamesPanel.Visibility = Visibility.Visible;
            GamesTab.Background = (Brush)Application.Current.Resources["AccentBrushSoft"];
            AppsTab.Background = (Brush)Application.Current.Resources["SurfaceBrush"];
        }

        // ---------- низ ----------

        private void OnOpenSettings(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            App.WindowManager?.OpenSettings();
        }

        private void OnExitWindows(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            App.WindowManager?.ExitShell();
        }

        private void OnPowerClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                PlacementTarget = PowerBtn,
            };
            AddItem(menu, "Завершение работы", Glyphs.Power, () => { CloseMenu(); SystemServices.PowerService.Shutdown(); });
            AddItem(menu, "Перезагрузка", Glyphs.Restart, () => { CloseMenu(); SystemServices.PowerService.Restart(); });
            AddItem(menu, "Сон", Glyphs.Clock, () => { CloseMenu(); SystemServices.PowerService.Sleep(); });
            menu.Items.Add(new Separator());
            AddItem(menu, "Блокировка", Glyphs.Lock, () => { CloseMenu(); SystemServices.PowerService.Lock(); });
            menu.Items.Add(new Separator());
            AddItem(menu, "Выйти в Windows", Glyphs.Home, () => { CloseMenu(); App.WindowManager?.ExitShell(); });
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

        // ---------- показ/скрытие ----------

        public void OpenMenu()
        {
            _closing = false;
            Position();
            Show();
            Activate();
            SearchBox.Text = "";
            SearchBox.Focus();
            ApplyFilters();
            if (!App.AnimationsEnabled)
            {
                Opacity = 1;
                return;
            }
            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }

        public void CloseMenu()
        {
            if (_closing || !IsVisible) { Hide(); return; }
            _closing = true;
            if (!App.AnimationsEnabled) { Hide(); _closing = false; return; }
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(90));
            fade.Completed += (_, _) => { Hide(); _closing = false; Opacity = 1; };
            BeginAnimation(OpacityProperty, fade);
        }

        public void UpdatePosition() => Position();

        private void Position()
        {
            try
            {
                var settings = App.Settings.Data;
                bool top = settings.TaskbarPosition == "Top";
                double screenH = SystemParameters.PrimaryScreenHeight;
                var wa = SystemParameters.WorkArea;
                double taskbarTop = top ? (settings.HideSystemTaskbar ? 0 : wa.Top)
                    : (settings.HideSystemTaskbar ? screenH - settings.TaskbarHeight : wa.Bottom - settings.TaskbarHeight);

                Left = (settings.HideSystemTaskbar ? 0 : wa.Left) + 8;
                if (top) Top = taskbarTop + settings.TaskbarHeight + 8;
                else Top = Math.Max(4, taskbarTop - Height - 8);

                double maxH = screenH - settings.TaskbarHeight - 16;
                if (Height > maxH) Height = maxH;
            }
            catch (Exception ex) { App.Log?.Error("StartMenu Position", ex); }
        }

        private void OnDeactivated(object sender, EventArgs e)
        {
            if (!_closing) CloseMenu();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    CloseMenu();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (Games.Count > 0 && GamesPanel.Visibility == Visibility.Visible)
                    {
                        if (Games[0].IsLocked)
                        {
                            App.Notifications.Show("Время учёбы", "Игры заблокированы во время учёбы.", NotificationKind.Warning);
                        }
                        else
                        {
                            CloseMenu();
                            GameScanner.LaunchGame(Games[0], App.Log);
                        }
                        e.Handled = true;
                    }
                    else if (AppsView.Count > 0)
                    {
                        CloseMenu();
                        InstalledAppsService.Launch(AppsView[0]);
                        e.Handled = true;
                    }
                    break;
            }
        }
    }
}
