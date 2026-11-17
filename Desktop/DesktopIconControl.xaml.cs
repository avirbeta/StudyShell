using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StudyShell.ViewModels;

namespace StudyShell.Desktop
{
    /// <summary>Неоновый значок рабочего стола: выбор, перетаскивание, запуск.</summary>
    public partial class DesktopIconControl : UserControl
    {
        private DesktopItem _item;
        private Point _pressPoint;
        private bool _dragging;
        private bool _hovered;

        public DesktopIconControl()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            MouseEnter += (_, _) => { _hovered = true; UpdateSelection(); };
            MouseLeave += (_, _) => { _hovered = false; UpdateSelection(); };
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_item != null) _item.PropertyChanged -= OnItemChanged;
            _item = e.NewValue as DesktopItem;
            if (_item != null)
            {
                _item.PropertyChanged += OnItemChanged;
                UpdateVisuals();
            }
        }

        private void OnItemChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DesktopItem.IsSelected)) UpdateSelection();
            if (e.PropertyName == nameof(DesktopItem.Name)) NameText.Text = _item.Name;
        }

        private void UpdateVisuals()
        {
            NameText.Text = _item.Name;
            IconImage.Source = _item.Icon;
            RefreshSize();
            UpdateSelection();
        }

        public void RefreshSize()
        {
            double size = (Window.GetWindow(this) is DesktopWindow w) ? w.ViewModel.IconSize : 76;
            IconImage.Width = size;
            IconImage.Height = size;
        }

        private void UpdateSelection()
        {
            if (_item != null && _item.IsSelected)
            {
                SelBorder.Background = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xE5, 0xFF));
                SelBorder.BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
                SelBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.7,
                };
            }
            else if (_hovered)
            {
                SelBorder.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
                SelBorder.BorderBrush = Brushes.Transparent;
                SelBorder.Effect = null;
            }
            else
            {
                SelBorder.Background = Brushes.Transparent;
                SelBorder.BorderBrush = Brushes.Transparent;
                SelBorder.Effect = null;
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_item == null) return;
            e.Handled = true;
            _pressPoint = e.GetPosition(null);
            _dragging = false;
            (Window.GetWindow(this) as DesktopWindow)?.OnIconPressed(this, e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.LeftButton != MouseButtonState.Pressed || _item == null) return;
            var p = e.GetPosition(null);
            if (!_dragging && (Math.Abs(p.X - _pressPoint.X) > 3 || Math.Abs(p.Y - _pressPoint.Y) > 3))
                _dragging = true;
            if (_dragging)
                (Window.GetWindow(this) as DesktopWindow)?.OnIconDragged(this, p - _pressPoint);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_item == null) return;
            e.Handled = true;
            bool wasDragging = _dragging;
            _dragging = false;
            (Window.GetWindow(this) as DesktopWindow)?.OnIconReleased(this, wasDragging, e);
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (_item == null) return;
            e.Handled = true;
            (Window.GetWindow(this) as DesktopWindow)?.ShowIconContextMenu(_item);
        }
    }
}
