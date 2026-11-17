using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StudyShell.Core.Helpers
{
    public static class IconService
    {
        private const int MaxCache = 800;
        private static readonly Dictionary<string, ImageSource> Cache = new();

        public static ImageSource ForPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var key = "p:" + path.ToLowerInvariant();
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }
            ImageSource src = null;
            try
            {
                var info = new NativeMethods.SHFILEINFO();
                IntPtr res = NativeMethods.SHGetFileInfo(path, 0, ref info,
                    (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                    NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
                if (res != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    src = FromHIcon(info.hIcon);
                    NativeMethods.DestroyIcon(info.hIcon);
                }
            }
            catch { }
            lock (Cache)
            {
                if (Cache.Count > MaxCache) Cache.Clear();
                Cache[key] = src;
            }
            return src;
        }

        public static ImageSource ForExecutable(string exePath, int index = 0)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(exePath);
                if (!string.IsNullOrEmpty(expanded) && expanded != exePath) exePath = expanded;
            }
            catch { }
            if (!File.Exists(exePath)) return null;
            var key = "e:" + exePath.ToLowerInvariant() + "#" + index;
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }
            ImageSource src = null;
            try
            {
                var large = new IntPtr[1];
                var small = new IntPtr[1];
                uint n = NativeMethods.ExtractIconEx(exePath, index, large, small, 1);
                IntPtr h = n > 0 ? large[0] : IntPtr.Zero;
                if (h != IntPtr.Zero)
                {
                    src = FromHIcon(h);
                    NativeMethods.DestroyIcon(h);
                }
            }
            catch { }
            if (src == null) src = ForPath(exePath);
            lock (Cache)
            {
                if (Cache.Count > MaxCache) Cache.Clear();
                Cache[key] = src;
            }
            return src;
        }

        public static ImageSource LoadImage(string path, int maxWidth = 640)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var key = "img:" + path.ToLowerInvariant() + "#" + maxWidth;
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }
            ImageSource src = null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                if (maxWidth > 0) bmp.DecodePixelWidth = maxWidth;
                bmp.EndInit();
                bmp.Freeze();
                src = bmp;
            }
            catch { }
            lock (Cache)
            {
                if (Cache.Count > MaxCache) Cache.Clear();
                Cache[key] = src;
            }
            return src;
        }

        public static ImageSource FromHIcon(IntPtr hIcon)
        {
            if (hIcon == IntPtr.Zero) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }

        /// <summary>Векторный глиф-значок (Segoe MDL2).</summary>
        public static ImageSource FromGlyph(string glyph, Color color)
        {
            var key = "g:" + glyph + "#" + color;
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }
            var typeface = new Typeface(new FontFamily("Segoe MDL2 Assets"),
                FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var text = new FormattedText(glyph,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 48, brush, 1.0);
            var geometry = text.BuildGeometry(new Point(0, 0));
            geometry.Freeze();
            var drawing = new GeometryDrawing(brush, null, geometry);
            drawing.Freeze();
            var image = new DrawingImage(drawing);
            image.Freeze();
            lock (Cache)
            {
                if (Cache.Count > MaxCache) Cache.Clear();
                Cache[key] = image;
            }
            return image;
        }
    }
}
