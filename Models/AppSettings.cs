using System.Collections.Generic;
using System;

namespace StudyShell.Models
{
    public class PinnedApp
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Args { get; set; } = "";
        public string BuiltInId { get; set; } = "";
        public bool IsBuiltIn => !string.IsNullOrEmpty(BuiltInId);
    }

    public class DesktopIconPos
    {
        public string Id { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class AppSettings
    {
        // Внешний вид (книжный академический стиль)
        public string AccentHex { get; set; } = "#F0A94B";
        public string AccentSecondaryHex { get; set; } = "#7FB069";
        public bool AnimationsEnabled { get; set; } = true;

        // Рабочий стол
        public string Wallpaper { get; set; } = "neon://grid";
        public int DesktopIconSize { get; set; } = 76;
        public List<DesktopIconPos> DesktopIconPositions { get; set; } = new();
        public List<string> HiddenBuiltinIcons { get; set; } = new();

        // Панель задач
        public string TaskbarPosition { get; set; } = "Bottom";
        public int TaskbarHeight { get; set; } = 56;
        public bool TaskbarAutoHide { get; set; } = false;
        public double TaskbarOpacity { get; set; } = 0.88;
        public bool HideSystemTaskbar { get; set; } = true;
        public bool PinnedInitialized { get; set; } = false;
        public List<PinnedApp> PinnedApps { get; set; } = new();
        public List<string> RecentApps { get; set; } = new();

        // Уведомления
        public bool NotificationsEnabled { get; set; } = true;
        public int NotificationDurationSec { get; set; } = 5;

        // Учёба
        public bool StudySessionActive { get; set; } = false;
        public int StudySessionMinutes { get; set; } = 60;
        public bool StudyScheduleEnabled { get; set; } = false;
        public string StudyStart { get; set; } = "18:00";
        public string StudyEnd { get; set; } = "21:00";
        public bool StudyStrictBlock { get; set; } = true;
        public bool HardBlock { get; set; } = true;
        public DateTime StudySessionEndUtc { get; set; }

        // Свои приложения
        public List<ManualApp> ManualApps { get; set; } = new();

        // Игры (видимы, но заблокированы во время учёбы)
        public List<ManualGame> ManualGames { get; set; } = new();
        public List<string> HiddenGameIds { get; set; } = new();

        // Система
        public bool Autostart { get; set; } = false;
    }

    public class ManualApp
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Args { get; set; } = "";
    }

    public class ManualGame
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Args { get; set; } = "";
    }
}
