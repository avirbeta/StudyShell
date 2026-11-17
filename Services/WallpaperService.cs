using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StudyShell.Core;
using StudyShell.Core.Helpers;

namespace StudyShell.Services
{
    /// <summary>Неоновые обои рабочего стола (градиенты со свечением).</summary>
    public static class WallpaperService
    {
        public static readonly string[] Presets = { "grid", "cyber", "void", "pulse", "sunset" };

        public static string PresetName(string id)
        {
            switch (id)
            {
                case "grid": return "Кабинет";
                case "cyber": return "Библиотека";
                case "void": return "Тетрадь";
                case "pulse": return "Конспект";
                case "sunset": return "Стол";
                default: return id;
            }
        }

        public static Brush GetPresetBrush(string preset)
        {
            (Color c1, Color c2, Color glow1, Color glow2, double ox, double oy) = preset switch
            {
                "cyber" => (C("#0B1220"), C("#132238"), C("#2E5A8A"), C("#4C6FA0"), 0.25, 0.2),
                "void" => (C("#0A0D10"), C("#0E1418"), C("#3C5A4A"), C("#54705C"), 0.7, 0.8),
                "pulse" => (C("#131007"), C("#1D1710"), C("#8A6A3C"), C("#A98A52"), 0.5, 0.4),
                "sunset" => (C("#141008"), C("#1F1610"), C("#9A6A3A"), C("#B08952"), 0.8, 0.25),
                _ => (C("#0A0D14"), C("#101624"), C("#3C5A8A"), C("#6A7FA0"), 0.3, 0.3),
            };
            return BuildBrush(c1, c2, glow1, glow2, ox, oy);
        }

        public static object Resolve(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (value.StartsWith("neon://"))
                return GetPresetBrush(value.Substring("neon://".Length));
            return IconService.LoadImage(value, 0);
        }

        public static string StoreUserImage(string sourcePath)
        {
            string dir = Path.Combine(App.DataDir, "wallpapers");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, Path.GetFileName(sourcePath));
            try { File.Copy(sourcePath, dest, true); }
            catch { dest = sourcePath; }
            return dest;
        }

        private static Color C(string hex) => ThemeManager.ParseColor(hex, Colors.Black);

        private static DrawingBrush BuildBrush(Color base1, Color base2, Color glow1, Color glow2, double ox, double oy)
        {
            var dg = new DrawingGroup();
            var full = new RectangleGeometry(new Rect(0, 0, 1, 1));
            var linear = new LinearGradientBrush(base1, base2, new Point(0, 0), new Point(1, 1));
            linear.Freeze();
            dg.Children.Add(new GeometryDrawing(linear, null, full));

            var radial1 = new RadialGradientBrush
            {
                GradientOrigin = new Point(ox, oy),
                Center = new Point(ox, oy),
                RadiusX = 0.85,
                RadiusY = 0.85,
            };
            radial1.GradientStops.Add(new GradientStop(Glow(glow1, 0.50), 0));
            radial1.GradientStops.Add(new GradientStop(Glow(glow1, 0.0), 1));
            radial1.Freeze();
            dg.Children.Add(new GeometryDrawing(radial1, null, full));

            var radial2 = new RadialGradientBrush
            {
                GradientOrigin = new Point(1 - ox, 1 - oy),
                Center = new Point(1 - ox, 1 - oy),
                RadiusX = 0.9,
                RadiusY = 0.9,
            };
            radial2.GradientStops.Add(new GradientStop(Glow(glow2, 0.45), 0));
            radial2.GradientStops.Add(new GradientStop(Glow(glow2, 0.0), 1));
            radial2.Freeze();
            dg.Children.Add(new GeometryDrawing(radial2, null, full));

            dg.Freeze();
            var brush = new DrawingBrush(dg) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            return brush;
        }

        private static Color Glow(Color c, double alpha)
            => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);
    }
}
