using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using StudyShell.Services;

namespace StudyShell.Core
{
    /// <summary>
    /// Управление неоновыми акцентами оболочки.
    /// Кисти в ресурсах XAML заморожены WPF, поэтому цвет меняется
    /// заменой ресурса новой кистью (DynamicResource подхватывает обновление).
    /// </summary>
    public static class ThemeManager
    {
        private static readonly Color DefaultCyan = Color.FromRgb(0x00, 0xE5, 0xFF);
        private static readonly Color DefaultMagenta = Color.FromRgb(0xFF, 0x2E, 0xA6);

        public static void Apply(SettingsService settings)
        {
            if (Application.Current == null) return;
            SetAccent(
                ParseColor(settings.Data.AccentHex, DefaultCyan),
                ParseColor(settings.Data.AccentSecondaryHex, DefaultMagenta));
        }

        /// <summary>Плавно сменить неоновый акцент (адаптация под игру).</summary>
        public static void AnimateAccent(Color accent, Color accent2)
        {
            if (Application.Current == null) return;
            ReplaceBrush("AccentBrush", accent, animate: true);
            ReplaceBrush("AccentBrush2", accent2, animate: true);
            ReplaceBrush("AccentBrushSoft", Color.FromArgb(0x26, accent.R, accent.G, accent.B), animate: true);
            ReplaceGradient(accent, accent2);
            ReplaceEffect("NeonGlow", accent);
            ReplaceEffect("NeonGlowSoft", accent);
        }

        private static void SetAccent(Color accent, Color accent2)
        {
            ReplaceBrush("AccentBrush", accent, animate: false);
            ReplaceBrush("AccentBrush2", accent2, animate: false);
            ReplaceBrush("AccentBrushSoft", Color.FromArgb(0x26, accent.R, accent.G, accent.B), animate: false);
            ReplaceGradient(accent, accent2);
            ReplaceEffect("NeonGlow", accent);
            ReplaceEffect("NeonGlowSoft", accent);
        }

        private static void ReplaceBrush(string key, Color color, bool animate)
        {
            try
            {
                var res = Application.Current.Resources;
                var old = res[key] as SolidColorBrush;
                Color start = animate && old != null ? old.Color : color;
                var brush = new SolidColorBrush(start);
                res[key] = brush;
                if (animate && start != color)
                {
                    brush.BeginAnimation(SolidColorBrush.ColorProperty,
                        new ColorAnimation(color, TimeSpan.FromMilliseconds(350))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                        });
                }
            }
            catch { }
        }

        private static void ReplaceGradient(Color c1, Color c2)
        {
            try
            {
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                };
                grad.GradientStops.Add(new GradientStop(c1, 0));
                grad.GradientStops.Add(new GradientStop(c2, 1));
                Application.Current.Resources["AccentGradientBrush"] = grad;
            }
            catch { }
        }

        private static void ReplaceEffect(string key, Color color)
        {
            try
            {
                var res = Application.Current.Resources;
                var old = res[key] as DropShadowEffect;
                if (old == null) return;
                res[key] = new DropShadowEffect
                {
                    Color = color,
                    BlurRadius = old.BlurRadius,
                    ShadowDepth = old.ShadowDepth,
                    Opacity = old.Opacity,
                };
            }
            catch { }
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                hex = hex.Trim().TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    return Color.FromRgb(r, g, b);
                }
            }
            catch { }
            return fallback;
        }
    }
}
