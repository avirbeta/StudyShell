using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StudyShell.Core;

namespace StudyShell.Taskbar
{
    public partial class CalendarPopupWindow : Window
    {
        private DateTime _month;

        public CalendarPopupWindow()
        {
            InitializeComponent();
        }

        public void OpenMenu()
        {
            _month = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            Build();
            Left = -10000;
            Top = -10000;
            Show();
            Position();
            Activate();
            if (App.AnimationsEnabled)
            {
                Opacity = 0;
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
            }
        }

        public void UpdatePosition() => Position();

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
                Left = right - Width - 10;
                if (top) Top = (settings.HideSystemTaskbar ? 0 : wa.Top) + settings.TaskbarHeight + 10;
                else Top = (settings.HideSystemTaskbar ? screenH : wa.Bottom) - settings.TaskbarHeight - ActualHeight - 10;
            }
            catch (Exception ex) { App.Log?.Error("Calendar Position", ex); }
        }

        private void Build()
        {
            MonthTitle.Text = _month.ToString("MMMM yyyy");
            DayNames.Children.Clear();
            DaysGrid.Children.Clear();

            string[] dayNames = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
            foreach (var d in dayNames)
            {
                DayNames.Children.Add(new TextBlock
                {
                    Text = d,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }

            int firstDow = ((int)_month.DayOfWeek + 6) % 7;
            int daysInMonth = DateTime.DaysInMonth(_month.Year, _month.Month);
            var today = DateTime.Today;
            for (int i = 0; i < firstDow; i++) DaysGrid.Children.Add(new Border());
            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(_month.Year, _month.Month, day);
                bool isToday = date == today;
                DaysGrid.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(15),
                    Width = 34,
                    Height = 34,
                    Background = isToday ? (Brush)Application.Current.Resources["AccentBrush"] : Brushes.Transparent,
                    Child = new TextBlock
                    {
                        Text = day.ToString(),
                        FontSize = 12,
                        Foreground = isToday ? Brushes.White : (Brush)Application.Current.Resources["TextPrimaryBrush"],
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                });
            }
            TodayText.Text = DateTime.Now.ToString("dddd, d MMMM yyyy");
        }

        private void OnPrev(object sender, RoutedEventArgs e) { _month = _month.AddMonths(-1); Build(); }
        private void OnNext(object sender, RoutedEventArgs e) { _month = _month.AddMonths(1); Build(); }
        private void OnDeactivated(object sender, EventArgs e) => Hide();

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        }
    }
}
