using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;
using StudyShell.Core.Helpers;
using StudyShell.Models;

namespace StudyShell.Services
{
    /// <summary>Поиск установленных игр: Steam, Epic, вручную.</summary>
    public static class GameScanner
    {
        public static List<GameEntry> Scan(AppSettings settings, LogService log)
        {
            var list = new List<GameEntry>();
            try { ScanSteam(list, log); }
            catch (Exception ex) { log.Warn("Steam scan: " + ex.Message); }
            try { ScanEpic(list, log); }
            catch (Exception ex) { log.Warn("Epic scan: " + ex.Message); }

            foreach (var m in settings.ManualGames)
            {
                if (string.IsNullOrEmpty(m.Path) || !File.Exists(m.Path)) continue;
                list.Add(new GameEntry
                {
                    Id = "manual:" + m.Path.ToLowerInvariant(),
                    Name = string.IsNullOrEmpty(m.Name) ? Path.GetFileNameWithoutExtension(m.Path) : m.Name,
                    Platform = "Вручную",
                    LaunchPath = m.Path,
                    Args = m.Args,
                    WorkingDir = Path.GetDirectoryName(m.Path) ?? "",
                });
            }
            return list;
        }

        private static void ScanSteam(List<GameEntry> list, LogService log)
        {
            string steamPath = GetSteamPath();
            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return;

            var libraries = new List<string> { Path.Combine(steamPath, "steamapps") };
            string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string steamapps = Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps");
                    if (Directory.Exists(steamapps) && !libraries.Contains(steamapps))
                        libraries.Add(steamapps);
                }
            }

            foreach (var steamapps in libraries)
            {
                foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    try
                    {
                        string appId = Path.GetFileNameWithoutExtension(manifest).Substring("appmanifest_".Length);
                        string content = File.ReadAllText(manifest);
                        string name = ReadVdfValue(content, "name");
                        string installDir = ReadVdfValue(content, "installdir");
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installDir)) continue;

                        string gameDir = Path.Combine(steamapps, "common", installDir);
                        string exe = FindMainExe(gameDir);

                        var entry = new GameEntry
                        {
                            Id = "steam:" + appId,
                            Name = name,
                            Platform = "Steam",
                            LaunchPath = "steam://rungameid/" + appId,
                            WorkingDir = gameDir,
                        };
                        string cache = Path.Combine(steamPath, "appcache", "librarycache");
                        string cover = Path.Combine(cache, appId + "_library_600x900.jpg");
                        if (!File.Exists(cover)) cover = Path.Combine(cache, appId + "_header.jpg");
                        if (File.Exists(cover)) entry.Cover = IconService.LoadImage(cover, 420);
                        if (!string.IsNullOrEmpty(exe)) entry.Icon = IconService.ForExecutable(exe);
                        list.Add(entry);
                    }
                    catch { }
                }
            }
            log.Info($"Steam: {list.Count(g => g.Platform == "Steam")} игр");
        }

        private static string GetSteamPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                return key?.GetValue("SteamPath") as string;
            }
            catch { return null; }
        }

        private static string ReadVdfValue(string content, string key)
        {
            var m = Regex.Match(content, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string FindMainExe(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                var exes = Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories)
                    .Where(f => !Regex.IsMatch(Path.GetFileName(f),
                        "(?i)(unins|crash|redist|setup|dxsetup|vcredist|dotnet|report|helper)") &&
                        !f.Contains("\\_CommonRedist\\", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .Take(10)
                    .ToList();
                if (exes.Count == 0) return null;
                string folder = Path.GetFileName(dir);
                string match = exes.FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).Contains(folder, StringComparison.OrdinalIgnoreCase));
                return match ?? exes[0];
            }
            catch { return null; }
        }

        private static void ScanEpic(List<GameEntry> list, LogService log)
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var root = doc.RootElement;
                    bool isGame = false;
                    if (root.TryGetProperty("AppCategories", out var cats))
                        foreach (var c in cats.EnumerateArray())
                            if (c.GetString() == "games") { isGame = true; break; }
                    if (!isGame) continue;

                    string name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
                    string exe = root.TryGetProperty("LaunchExecutable", out var e) ? e.GetString() : null;
                    string location = root.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(exe)) continue;

                    string exePath = exe;
                    if (!Path.IsPathRooted(exePath) && !string.IsNullOrEmpty(location))
                        exePath = Path.Combine(location, exe);
                    if (!File.Exists(exePath)) continue;

                    list.Add(new GameEntry
                    {
                        Id = "epic:" + exePath.ToLowerInvariant(),
                        Name = name,
                        Platform = "Epic",
                        LaunchPath = exePath,
                        WorkingDir = Path.GetDirectoryName(exePath) ?? "",
                        Icon = IconService.ForExecutable(exePath),
                    });
                }
                catch { }
            }
            log.Info($"Epic: {list.Count(g => g.Platform == "Epic")} игр");
        }

        public static void LaunchGame(GameEntry game, LogService log)
        {
            try
            {
                if (StudyService.IsActive)
                {
                    System.Windows.MessageBox.Show(
                        "Сейчас время учёбы — игры полностью заблокированы.\n\n" +
                        StudyService.StatusText + "\n\n" +
                        "Управление: Параметры → Учёба.",
                        "Study Shell — время учёбы",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }
                if (game.IsSteamUri)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(game.LaunchPath)
                    { UseShellExecute = true });
                    log.Info($"Запуск через Steam: {game.Name}");
                    return;
                }
                if (!File.Exists(game.LaunchPath))
                {
                    System.Windows.MessageBox.Show("Файл игры не найден:\n" + game.LaunchPath,
                        "Neon Shell", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = game.LaunchPath,
                    Arguments = game.Args ?? "",
                    UseShellExecute = true,
                    WorkingDirectory = string.IsNullOrEmpty(game.WorkingDir)
                        ? Path.GetDirectoryName(game.LaunchPath) ?? ""
                        : game.WorkingDir,
                };
                System.Diagnostics.Process.Start(psi);
                log.Info($"Запуск игры: {game.Name}");
            }
            catch (Exception ex)
            {
                log.Error("Не удалось запустить игру", ex);
                System.Windows.MessageBox.Show("Не удалось запустить игру:\n" + ex.Message,
                    "Neon Shell", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    /// <summary>Доминирующий цвет обложки игры (для неоновой адаптации).</summary>
    public static class ColorService
    {
        private const int SampleSize = 20;

        public static Color DominantColor(System.Windows.Media.ImageSource image)
        {
            try
            {
                var bmp = image as System.Windows.Media.Imaging.BitmapSource;
                if (bmp == null) return Fallback();
                if (bmp.Format != System.Windows.Media.PixelFormats.Bgra32)
                    bmp = new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp,
                        System.Windows.Media.PixelFormats.Bgra32, null, 0);

                int w = SampleSize;
                int h = Math.Max(1, (int)Math.Round(bmp.PixelHeight * (double)w / bmp.PixelWidth));
                h = Math.Min(h, SampleSize);

                var scaled = new System.Windows.Media.Imaging.TransformedBitmap(bmp,
                    new System.Windows.Media.ScaleTransform(w / (double)bmp.PixelWidth, h / (double)bmp.PixelHeight));
                var rt = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                rt.Render(new System.Windows.Controls.Image { Source = scaled });

                var pixels = new byte[w * h * 4];
                rt.CopyPixels(pixels, w * 4, 0);

                double r = 0, g = 0, b = 0, weightSum = 0;
                for (int i = 0; i < w * h; i++)
                {
                    double pr = pixels[i * 4 + 2] / 255.0;
                    double pg = pixels[i * 4 + 1] / 255.0;
                    double pb = pixels[i * 4 + 0] / 255.0;
                    double max = Math.Max(pr, Math.Max(pg, pb));
                    double min = Math.Min(pr, Math.Min(pg, pb));
                    double sat = max <= 0 ? 0 : (max - min) / max;
                    if (max < 0.16 || sat < 0.18) continue;
                    double weight = sat * sat;
                    r += pr * weight;
                    g += pg * weight;
                    b += pb * weight;
                    weightSum += weight;
                }
                if (weightSum <= 0) return Fallback();

                double cr = r / weightSum, cg = g / weightSum, cb = b / weightSum;
                double m = Math.Max(cr, Math.Max(cg, cb));
                if (m > 0)
                {
                    cr = m + (cr - m) * 0.35;
                    cg = m + (cg - m) * 0.35;
                    cb = m + (cb - m) * 0.35;
                }
                return System.Windows.Media.Color.FromRgb(
                    (byte)Math.Clamp(cr * 255, 0, 255),
                    (byte)Math.Clamp(cg * 255, 0, 255),
                    (byte)Math.Clamp(cb * 255, 0, 255));
            }
            catch { return Fallback(); }
        }

        private static Color Fallback() => System.Windows.Media.Color.FromRgb(0x00, 0xE5, 0xFF);

        public static Color ShiftHue(Color c, double degrees)
        {
            double angle = degrees * Math.PI / 180.0;
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2.0;
            double d = max - min;
            double h;
            if (d == 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            h = (h + degrees) % 360;
            if (h < 0) h += 360;
            double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
            s = Math.Clamp(s, 0.25, 1);
            double c2 = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c2 * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m2 = l - c2 / 2.0;
            double rr, gg, bb;
            if (h < 60) { rr = c2; gg = x; bb = 0; }
            else if (h < 120) { rr = x; gg = c2; bb = 0; }
            else if (h < 180) { rr = 0; gg = c2; bb = x; }
            else if (h < 240) { rr = 0; gg = x; bb = c2; }
            else if (h < 300) { rr = x; gg = 0; bb = c2; }
            else { rr = c2; gg = 0; bb = x; }
            return System.Windows.Media.Color.FromRgb(
                (byte)Math.Clamp((rr + m2) * 255, 0, 255),
                (byte)Math.Clamp((gg + m2) * 255, 0, 255),
                (byte)Math.Clamp((bb + m2) * 255, 0, 255));
        }
    }
}
