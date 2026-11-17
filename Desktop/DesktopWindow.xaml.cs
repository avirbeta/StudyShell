using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using StudyShell.Core;
using StudyShell.Core.Helpers;
using StudyShell.Services;
using StudyShell.ViewModels;

namespace StudyShell.Desktop
{
    /// <summary>
    /// Неоновый рабочий стол Neon Shell. Встраивается в системный слой WorkerW —
    /// рабочий стол Windows полностью скрывается, при выходе восстанавливается.
    /// </summary>
    public partial class DesktopWindow : Window
    {
        public DesktopViewModel ViewModel { get; } = new();

        private HwndSource _hwndSource;
        private uint _taskbarCreatedMsg;
        private Point _rubberStart;
        private bool _rubberActive;
        private readonly Dictionary<DesktopItem, Point> _dragOrigins = new();
        private bool _iconDragged;

        public DesktopWindow()
        {
            InitializeComponent();
            DataContext = ViewModel;
            ViewModel.Initialize();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateBounds();
            AttachToSystemDesktop();
            ViewModel.Reload();
            UpdateWallpaper();
            UpdateStudyWidget();
            _studyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _studyTimer.Tick += (_, _) => UpdateStudyWidget();
            _studyTimer.Start();
        }

        private DispatcherTimer _studyTimer;

        private void OnClosed(object sender, EventArgs e)
        {
            _studyTimer?.Stop();
            _hwndSource?.RemoveHook(WndProc);
        }

        private void AttachToSystemDesktop()
        {
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (_hwndSource != null)
            {
                _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");
                _hwndSource.AddHook(WndProc);
            }
            bool attached = ShellIntegration.AttachDesktop(new WindowInteropHelper(this).Handle);
            if (attached) ShellIntegration.HideDesktopIcons(true);
            else App.Log?.Warn("Не удалось встроить рабочий стол в системный слой");
            UpdateBounds();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        ShellIntegration.ReattachDesktop(new WindowInteropHelper(this).Handle);
                        ShellIntegration.HideDesktopIcons(true);
                        UpdateBounds();
                        ViewModel.Reload();
                    }
                    catch (Exception ex) { App.Log?.Error("Reattach desktop", ex); }
                }));
            }
            else if (msg == NativeMethods.WM_DISPLAYCHANGE)
            {
                Dispatcher.BeginInvoke(new Action(UpdateBounds));
            }
            return IntPtr.Zero;
        }

        public void UpdateBounds()
        {
            try
            {
                Left = SystemParameters.VirtualScreenLeft;
                Top = SystemParameters.VirtualScreenTop;
                Width = SystemParameters.VirtualScreenWidth;
                Height = SystemParameters.VirtualScreenHeight;
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    int w = (int)(Width * dpi.DpiScaleX);
                    int h = (int)(Height * dpi.DpiScaleY);
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, w, h,
                        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                }
            }
            catch (Exception ex) { App.Log?.Error("UpdateBounds", ex); }
        }

        public void UpdateWallpaper()
        {
            try
            {
                object resolved = WallpaperService.Resolve(App.Settings.Data.Wallpaper);
                if (resolved is Brush brush)
                {
                    RootGrid.Background = brush;
                    WallpaperImage.Source = null;
                }
                else if (resolved is ImageSource img)
                {
                    RootGrid.Background = new SolidColorBrush(Color.FromRgb(0x05, 0x07, 0x0D));
                    WallpaperImage.Source = img;
                }
            }
            catch (Exception ex) { App.Log?.Error("UpdateWallpaper", ex); }
        }

        // ---------- виджет времени учёбы ----------

        public void UpdateStudyWidget()
        {
            bool active = StudyService.IsActive;
            StudyCard.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (!active) return;

            if (StudyService.SessionActive)
            {
                var rem = StudyService.Remaining;
                StudyCountdown.Text = $"{rem.Hours:00}:{rem.Minutes:00}:{rem.Seconds:00}";
                StudyNote.Text = "Сессия учёбы · игры заблокированы";
            }
            else
            {
                StudyCountdown.Text = "УЧЁБА";
                StudyNote.Text = StudyService.StatusText + " · игры заблокированы";
            }
        }

        // ---------- выбор и перетаскивание ----------

        public void OnIconPressed(DesktopIconControl icon, MouseButtonEventArgs e)
        {
            _iconDragged = false;
            var item = icon.DataContext as DesktopItem;
            if (item == null) return;

            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (!ctrl)
            {
                foreach (var i in ViewModel.Items) i.IsSelected = false;
                item.IsSelected = true;
            }
            else item.IsSelected = !item.IsSelected;
            if (!item.IsSelected) item.IsSelected = true;

            _dragOrigins.Clear();
            foreach (var i in ViewModel.Items.Where(x => x.IsSelected))
                _dragOrigins[i] = new Point(i.X, i.Y);

            Focus();
            icon.CaptureMouse();
        }

        public void OnIconDragged(DesktopIconControl icon, Vector delta)
        {
            _iconDragged = true;
            foreach (var kv in _dragOrigins)
            {
                kv.Key.X = kv.Value.X + delta.X;
                kv.Key.Y = kv.Value.Y + delta.Y;
            }
        }

        public void OnIconReleased(DesktopIconControl icon, bool wasDragging, MouseButtonEventArgs e)
        {
            icon.ReleaseMouseCapture();
            if (wasDragging && _iconDragged) ViewModel.SavePositions();
            else
            {
                var item = icon.DataContext as DesktopItem;
                if (item != null && e.ClickCount == 2) ViewModel.Launch(item);
            }
            _iconDragged = false;
        }

        // ---------- резиновое выделение ----------

        private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (IsIconSource(e.OriginalSource)) return;
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                foreach (var i in ViewModel.Items) i.IsSelected = false;

            _rubberActive = true;
            _rubberStart = e.GetPosition(RootGrid);
            SelectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRect, _rubberStart.X);
            Canvas.SetTop(SelectionRect, _rubberStart.Y);
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            Focus();
        }

        private void OnWindowMouseMove(object sender, MouseEventArgs e)
        {
            if (!_rubberActive) return;
            var p = e.GetPosition(RootGrid);
            double x = Math.Min(p.X, _rubberStart.X);
            double y = Math.Min(p.Y, _rubberStart.Y);
            double w = Math.Abs(p.X - _rubberStart.X);
            double h = Math.Abs(p.Y - _rubberStart.Y);
            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = w;
            SelectionRect.Height = h;

            if (w > 4 || h > 4)
            {
                var rect = new Rect(x, y, w, h);
                foreach (var i in ViewModel.Items)
                {
                    var bounds = new Rect(i.X, i.Y, Math.Max(72, ViewModel.IconSize + 16), ViewModel.IconSize + 40);
                    i.IsSelected = bounds.IntersectsWith(rect);
                }
            }
        }

        private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_rubberActive) return;
            _rubberActive = false;
            SelectionRect.Visibility = Visibility.Collapsed;
        }

        private bool IsIconSource(object source)
        {
            var d = source as DependencyObject;
            while (d != null)
            {
                if (d is DesktopIconControl) return true;
                d = VisualTreeHelper.GetParent(d) ?? (d as FrameworkContentElement)?.Parent;
            }
            return false;
        }

        // ---------- контекстные меню ----------

        public void ShowIconContextMenu(DesktopItem item)
        {
            if (!item.IsSelected)
            {
                foreach (var i in ViewModel.Items) i.IsSelected = false;
                item.IsSelected = true;
            }
            var menu = new ContextMenu();
            AddItem(menu, "Открыть", Glyphs.Play, () => ViewModel.Launch(item));
            if (!item.IsBuiltIn)
            {
                menu.Items.Add(new Separator());
                AddItem(menu, "Переименовать", Glyphs.Edit, () => BeginRename(item));
                AddItem(menu, "Удалить в корзину", Glyphs.Delete, () => DeleteSelected());
            }
            menu.IsOpen = true;
        }

        private void OnWindowRightDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Right) return;
            if (IsIconSource(e.OriginalSource)) return;

            var menu = new ContextMenu();
            var newMenu = new MenuItem { Header = "Создать" };
            newMenu.Items.Add(CreateItem("Папку", Glyphs.Folder, () =>
                PromptAndRun("Новая папка", "Имя папки:", name => ViewModel.CreateFolder(name))));
            newMenu.Items.Add(CreateItem("Текстовый документ", Glyphs.Edit, () =>
                PromptAndRun("Новый документ", "Имя файла:", name => ViewModel.CreateTextFile(name))));
            newMenu.Items.Add(CreateItem("Ярлык...", Glyphs.Add, ShowShortcutDialog));
            menu.Items.Add(newMenu);

            menu.Items.Add(new Separator());
            var sizes = new MenuItem { Header = "Размер значков" };
            sizes.Items.Add(CreateItem("Мелкие", null, () => SetIconSize(60)));
            sizes.Items.Add(CreateItem("Средние", null, () => SetIconSize(76)));
            sizes.Items.Add(CreateItem("Крупные", null, () => SetIconSize(92)));
            menu.Items.Add(sizes);

            AddItem(menu, "Сортировать значки", Glyphs.Refresh, ViewModel.ResetPositions);
            AddItem(menu, "Обновить", Glyphs.Refresh, ViewModel.Reload);
            menu.Items.Add(new Separator());
            AddItem(menu, "Сменить обои", Glyphs.Color, ChangeWallpaper);
            AddItem(menu, "Параметры Neon", Glyphs.Settings, () => App.WindowManager?.OpenSettings());
            menu.Items.Add(new Separator());
            AddItem(menu, "Выйти в Windows", Glyphs.Home, () => App.WindowManager?.ExitShell());

            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private MenuItem CreateItem(string header, string glyph, Action action)
        {
            var item = new MenuItem { Header = header };
            if (glyph != null)
            {
                item.Icon = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            item.Click += (_, _) => action();
            return item;
        }

        private void AddItem(ContextMenu menu, string header, string glyph, Action action)
        {
            var item = CreateItem(header, glyph, action);
            menu.Items.Add(item);
        }

        private void SetIconSize(double size)
        {
            App.Settings.Data.DesktopIconSize = (int)size;
            App.Settings.MarkDirty();
            ViewModel.Reload();
        }

        private void ChangeWallpaper()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите изображение для рабочего стола",
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Все файлы|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                App.Settings.Data.Wallpaper = WallpaperService.StoreUserImage(dlg.FileName);
                App.Settings.MarkDirty();
                UpdateWallpaper();
            }
        }

        private void ShowShortcutDialog()
        {
            var dlg = new Dialogs.ShortcutDialog();
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.TargetPath))
                ViewModel.CreateShortcut(dlg.ShortcutName, dlg.TargetPath, dlg.Arguments);
        }

        private void BeginRename(DesktopItem item)
        {
            var dlg = new Dialogs.InputDialog("Переименовать", "Новое имя:", item.Name);
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Value) && dlg.Value != item.Name)
            {
                try { ViewModel.Rename(item, dlg.Value); }
                catch (Exception ex) { App.Notifications.Show("Ошибка", ex.Message, NotificationKind.Error); }
            }
        }

        // ---------- клавиатура ----------

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            var selected = ViewModel.Items.Where(i => i.IsSelected).ToList();
            switch (e.Key)
            {
                case Key.F5:
                    ViewModel.Reload();
                    e.Handled = true;
                    break;
                case Key.Delete:
                    if (selected.Count > 0) DeleteSelected();
                    e.Handled = true;
                    break;
                case Key.F2:
                    if (selected.Count == 1) BeginRename(selected[0]);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (selected.Count == 1) ViewModel.Launch(selected[0]);
                    e.Handled = true;
                    break;
            }
        }

        private void DeleteSelected()
        {
            var selected = ViewModel.Items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;
            var res = MessageBox.Show(
                selected.Count == 1
                    ? $"Удалить «{selected[0].Name}» в корзину?"
                    : $"Удалить {selected.Count} элементов в корзину?",
                "Neon Shell", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            ViewModel.Delete(selected, out string errors);
            if (!string.IsNullOrEmpty(errors))
                App.Notifications.Show("Удаление", errors, NotificationKind.Warning);
        }

        private void PromptAndRun(string title, string prompt, Action<string> run)
        {
            var dlg = new Dialogs.InputDialog(title, prompt, "");
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Value))
            {
                try { run(dlg.Value); }
                catch (Exception ex) { App.Notifications.Show("Ошибка", ex.Message, NotificationKind.Error); }
            }
        }

        // ---------- drag&drop ----------

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                    ViewModel.ImportFiles(files);
            }
        }
    }
}
