using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using StudyShell.Models;

namespace StudyShell.Services
{
    /// <summary>
    /// Центр режима учёбы: сессия с таймером, расписание и строгая блокировка
    /// игровых процессов (запущенные игры и лаунчеры закрываются автоматически).
    /// </summary>
    public static class StudyService
    {
        private static readonly HashSet<string> BlockedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // Java-игры (Minecraft и т.д.)
            "java", "javaw", "javaws", "javapath", "javapath_target_1",

            // Лаунчеры
            "steam", "steamwebhelper", "epicgameslauncher", "battle.net", "battlenet",
            "origin", "eadesktop", "ubisoftconnect", "upc", "galaxyclient", "gog galaxy",
            "riotclientservices", "riotclient", "leagueclient", "leagueoflegends",
            "valorant", "minecraft", "minecraftlauncher", "tlauncher", "tl",
            "flauncher", "ll",
            "bethesdanet", "rockstar", "socialclubhelper", "itch", "playnite",
            "gog galaxy", "ea app",

            // RADMIR (CRMP / GTA SA) и его лаунчер
            "radmir launcher", "radmirlauncher", "radmir", "radmirgames",
            "radmir games", "crmp", "samp",

            // Игры и платформы
            "robloxplayerbeta", "robloxplayerlauncher", "roblox",
            "robloxstudiobeta", "robloxstudio", "robloxstudioinstaller",
            "robloxstudiolauncher", "robloxcrashhandler", "studiomcp",
            "fortniteclient-win64-shipping", "fortnitelauncher", "fortnite",
            "valorant-win64-shipping", "league of legends",
            "csgo", "cs2", "dota2", "hl2", "portal2", "left4dead2", "left4dead",
            "tf2", "tf_win64", "rustclient", "rust", "escapefromtarkov", "tarkov",
            "osu!", "geometrydash", "amongus", "among us", "terraria", "tmodloader",
            "stardew valley", "stardewvalley", "gta5", "gtav", "gta_sa",
            "overwatch", "overwatch2", "hearthstone", "diablo", "diablo iv",
            "genshinimpact", "honkai", "starrail", "zenlesszonezero", "pubg", "tslgame",
            "warframe", "warframe.x64", "apex", "r5apex", "fifa", "fc24", "fc25",
            "nba2k", "madden", "call of duty", "cod", "modernwarfare", "cyberpunk2077",
            "witcher3", "reddead2", "rdr2", "eldenring", "dark souls", "darksouls",
            "fallout4", "skyrimse", "skyrim", "factorio", "rimworld", "subnautica",
            "phasmophobia", "garrysmod", "hl2_linux", "satisfactory", "valheim",
            "7daystodie", "deadbydaylight", "dontstarve", "spelunky", "hollowknight",
            "celeste", "cuphead", "undertale", "brawlstars", "clashroyale", "clashofclans",
            "candycrush", "subwaysurfers",
        };

        private static List<string> _gameExeNames = new();
        private static bool _scanned;
        private static DateTime _sessionEnd = DateTime.MinValue;

        public static bool SessionActive => App.Settings?.Data.StudySessionActive ?? false;
        public static bool ScheduleEnabled => App.Settings?.Data.StudyScheduleEnabled ?? false;
        public static bool StrictBlock => App.Settings?.Data.StudyStrictBlock ?? true;

        /// <summary>Сейчас идёт время учёбы?</summary>
        public static bool IsActive
        {
            get
            {
                if (SessionActive) return true;
                if (!ScheduleEnabled) return false;
                return IsWithinSchedule(DateTime.Now);
            }
        }

        public static bool IsWithinSchedule(DateTime now)
        {
            if (!TimeSpan.TryParse(App.Settings.Data.StudyStart, out var start)) return false;
            if (!TimeSpan.TryParse(App.Settings.Data.StudyEnd, out var end)) return false;
            if (start == end) return false;
            var current = now.TimeOfDay;
            if (start < end) return current >= start && current < end;
            // интервал через полночь
            return current >= start || current < end;
        }

        public static string StatusText
        {
            get
            {
                if (SessionActive) return "Сессия учёбы идёт";
                if (ScheduleEnabled)
                    return $"Расписание: {App.Settings.Data.StudyStart}–{App.Settings.Data.StudyEnd}" +
                           (IsWithinSchedule(DateTime.Now) ? " (сейчас учёба)" : "");
                return "Режим учёбы выключен";
            }
        }

        public static TimeSpan Remaining
        {
            get
            {
                if (!SessionActive) return TimeSpan.Zero;
                var left = _sessionEnd - DateTime.Now;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        /// <summary>Запустить сессию учёбы на N минут.</summary>
        public static void StartSession(int minutes)
        {
            minutes = Math.Clamp(minutes, 5, 720);
            App.Settings.Data.StudySessionMinutes = minutes;
            _sessionEnd = DateTime.Now.AddMinutes(minutes);
            App.Settings.Data.StudySessionActive = true;
            App.Settings.Data.StudySessionEndUtc = _sessionEnd;
            App.Settings.MarkDirty();
            App.Notifications?.Show("Учёба",
                $"Сессия началась: {minutes} мин. Игры заблокированы!", NotificationKind.Success);
        }

        public static void StopSession()
        {
            App.Settings.Data.StudySessionActive = false;
            App.Settings.Data.StudySessionEndUtc = default;
            App.Settings.MarkDirty();
        }

        /// <summary>Жёсткая блокировка: игры не запускаются вообще
        /// (не только во время учёбы). По умолчанию включена.</summary>
        public static bool HardBlock => App.Settings?.Data.HardBlock ?? true;

        /// <summary>Восстановить сессию учёбы после перезапуска оболочки
        /// (время окончания хранится в настройках).</summary>
        public static void ResumeSession()
        {
            var d = App.Settings?.Data;
            if (d == null || !d.StudySessionActive) return;
            if (d.StudySessionEndUtc > DateTime.Now) _sessionEnd = d.StudySessionEndUtc;
            else { d.StudySessionActive = false; d.StudySessionEndUtc = default; }
        }

        /// <summary>Пересчёт сессии (раз в секунду).</summary>
        public static void Tick()
        {
            if (!SessionActive) return;
            if (_sessionEnd <= DateTime.Now)
            {
                StopSession();
                App.Notifications?.Show("Учёба", "Время учёбы закончилось. Перерыв! 🎉", NotificationKind.Success);
            }
        }

        /// <summary>Строгая блокировка: закрыть запущенные игры и лаунчеры
        /// (по имени процесса; системные процессы не затрагиваются).
        /// </summary>
        public static void WatchdogTick()
        {
            if (!IsActive || !StrictBlock) return;
            try
            {
                ScanGameNames();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        string name = p.ProcessName;
                        if (BlockedNames.Contains(name) || _gameExeNames.Contains(name))
                        {
                            p.Kill();
                            App.Log?.Info($"Режим учёбы: закрыта игра/лаунчер «{name}»");
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                App.Log?.Warn("Watchdog: " + ex.Message);
            }
        }

        /// <summary>
        /// Проверка: является ли имя процесса (без .exe) игрой или лаунчером,
        /// которые блокируются во время учёбы.
        /// </summary>
        public static bool IsBlockedName(string exeName)
        {
            if (string.IsNullOrWhiteSpace(exeName)) return false;
            string name = exeName.Trim();
            if (BlockedNames.Contains(name)) return true;
            ScanGameNames();
            return _gameExeNames.Contains(name);
        }

        private static readonly string[] GameFolderHints =
        {
            "steamapps", "epic games", "riot games", "gog games", "battle.net",
            "ubisoft game launcher", "origin games", "\\games\\", "\\game\\",
        };

        private static void ScanGameNames()
        {
            if (_scanned) return;
            _scanned = true;
            try
            {
                var settings = App.Settings?.Data;
                if (settings == null) return;

                foreach (var game in GameScanner.Scan(settings, App.Log))
                {
                    if (game.IsSteamUri) continue;
                    try
                    {
                        string name = Path.GetFileNameWithoutExtension(game.LaunchPath);
                        if (!string.IsNullOrEmpty(name)) _gameExeNames.Add(name);
                    }
                    catch { }
                }

                // игры из ярлыков рабочего стола и меню «Пуск»
                var shortcutDirs = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                };
                foreach (var dir in shortcutDirs)
                {
                    IEnumerable<string> lnks;
                    try { lnks = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories).ToList(); }
                    catch { continue; }
                    foreach (var lnk in lnks)
                    {
                        try
                        {
                            string target = ShortcutResolver.Resolve(lnk, out _, out _, out _);
                            if (string.IsNullOrEmpty(target) || !File.Exists(target)) continue;
                            if (target.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)) continue;
                            if (!GameFolderHints.Any(h => target.Contains(h, StringComparison.OrdinalIgnoreCase)))
                                continue;
                            string name = Path.GetFileNameWithoutExtension(target);
                            if (!string.IsNullOrEmpty(name)) _gameExeNames.Add(name);
                        }
                        catch { }
                    }
                }
                App.Log?.Info($"Режим учёбы: в блок-листе {_gameExeNames.Count} игровых exe из ярлыков");
            }
            catch { }
        }
    }
}
