using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StudyShell.Core
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }

    public static class Glyphs
    {
        public const string Search = "\uE721";
        public const string Settings = "\uE713";
        public const string Power = "\uE7E8";
        public const string Wifi = "\uE701";
        public const string Ethernet = "\uE839";
        public const string Globe = "\uE909";
        public const string Volume = "\uE767";
        public const string VolumeMute = "\uE74F";
        public const string VolumeLow = "\uE992";
        public const string Brightness = "\uE706";
        public const string Lock = "\uE72E";
        public const string Add = "\uE710";
        public const string Delete = "\uE74E";
        public const string Refresh = "\uE72C";
        public const string Folder = "\uE838";
        public const string Close = "\uE711";
        public const string Check = "\uE73A";
        public const string ChevronDown = "\uE70D";
        public const string Info = "\uE946";
        public const string Warning = "\uE7BA";
        public const string ErrorBadge = "\uEA39";
        public const string Notification = "\uE7ED";
        public const string Calendar = "\uE787";
        public const string Clock = "\uE823";
        public const string User = "\uE77B";
        public const string Apps = "\uE71D";
        public const string Restart = "\uE777";
        public const string Monitor = "\uE7F4";
        public const string Pin = "\uE718";
        public const string Unpin = "\uE77A";
        public const string More = "\uE712";
        public const string Play = "\uE768";
        public const string Gamepad = "\uE7FC";
        public const string Home = "\uE80F";
        public const string Calculator = "\uE8EF";
        public const string Picture = "\uEB9F";
        public const string Color = "\uE790";
        public const string Edit = "\uE70F";
    }
}
