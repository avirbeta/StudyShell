using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StudyShell.Core;
using StudyShell.Core.Helpers;
using StudyShell.Services;

namespace StudyShell.Notifications
{
    /// <summary>Неоновое всплывающее уведомление.</summary>
    public partial class ToastWindow : Window
    {
        public bool Expired { get; private set; }
        private readonly DispatcherTimer _timer;

        public ToastWindow(NotificationItem item)
        {
            InitializeComponent();
            TitleText.Text = item.Title;
            MessageText.Text = item.Message;

            (string glyph, string color) = item.Kind switch
            {
                "Success" => (Glyphs.Check ?? "\uE73A", "#39FF88"),
                "Warning" => (Glyphs.Warning, "#FFC233"),
                "Error" => (Glyphs.ErrorBadge, "#FF2E5B"),
                _ => (Glyphs.Info, App.Settings.Data.AccentHex),
            };
            KindGlyph.Text = glyph;
            try { KindBadge.Background = new SolidColorBrush(ThemeManager.ParseColor(color, Colors.White)); }
            catch { }

            int duration = App.Settings?.Data.NotificationDurationSec ?? 5;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(2, duration)) };
            _timer.Tick += (_, _) => { Expired = true; Close(); };
            _timer.Start();

            MouseLeftButtonUp += (_, _) => Close();
            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.SetWindowStyleFlag(hwnd,
                    NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW, true);
                Position();
                if (App.AnimationsEnabled)
                {
                    Opacity = 0;
                    Slide.X = 40;
                    BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                    Slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                        new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(200))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                        });
                }
            };
        }

        private void Position()
        {
            try
            {
                var settings = App.Settings.Data;
                bool top = settings.TaskbarPosition == "Top";
                double screenW = SystemParameters.PrimaryScreenWidth;
                double screenH = SystemParameters.PrimaryScreenHeight;
                var wa = SystemParameters.WorkArea;
                double right = settings.HideSystemTaskbar ? screenW : wa.Right;
                int n = App.Notifications?.VisibleCount(this) ?? 0;

                Left = right - Width - 12;
                double stackH = (ActualHeight + 8) * (n + 1);
                if (top) Top = (settings.HideSystemTaskbar ? 0 : wa.Top) + settings.TaskbarHeight + 10 + stackH;
                else Top = (settings.HideSystemTaskbar ? screenH : wa.Bottom) - settings.TaskbarHeight - 10 - stackH;
            }
            catch { }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
