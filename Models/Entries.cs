using System;
using System.Windows.Media;
using StudyShell.Core;

namespace StudyShell.Models
{
    public class AppEntry : ObservableObject
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Args { get; set; } = "";
        public string Category { get; set; } = "Прочее";
        public bool IsBuiltIn { get; set; }
        public string BuiltInId { get; set; } = "";
        public string IconPath { get; set; } = "";
        public int IconIndex { get; set; }

        private ImageSource _icon;
        public ImageSource Icon { get => _icon; set => Set(ref _icon, value); }
    }

    /// <summary>Игра в меню «Пуск» (Steam/Epic/вручную).</summary>
    public class GameEntry : ObservableObject
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Platform { get; set; } = "Вручную";
        public string LaunchPath { get; set; } = "";
        public string Args { get; set; } = "";
        public string WorkingDir { get; set; } = "";
        public bool IsSteamUri => LaunchPath.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);

        private ImageSource _cover;
        public ImageSource Cover { get => _cover; set => Set(ref _cover, value); }

        private ImageSource _icon;
        public ImageSource Icon { get => _icon; set => Set(ref _icon, value); }

        private Color _dominant = Color.FromRgb(0x00, 0xE5, 0xFF);
        public Color Dominant { get => _dominant; set => Set(ref _dominant, value); }

        private bool _colorReady;
        public bool ColorReady { get => _colorReady; set => Set(ref _colorReady, value); }

        private bool _isLocked;
        public bool IsLocked { get => _isLocked; set => Set(ref _isLocked, value); }
    }

    public class RunningAppInfo : ObservableObject
    {
        public IntPtr Hwnd { get; set; }
        public string Title { get; set; } = "";
        public int Pid { get; set; }
        public string Path { get; set; } = "";
        public ImageSource Icon { get; set; }

        private bool _isMinimized;
        public bool IsMinimized { get => _isMinimized; set => Set(ref _isMinimized, value); }

        private bool _isActive;
        public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    }
}
