using System;
using System.Windows;
using StudyShell.Core.Helpers;

namespace StudyShell.Services
{
    /// <summary>
    /// Безопасная замена оболочки Windows: рабочий стол Neon встраивается
    /// в системный слой WorkerW, системные значки и панель задач скрываются
    /// обратимо (explorer.exe остаётся жив). При выходе всё восстанавливается,
    /// а если explorer был завершён — запускается заново.
    /// </summary>
    public static class ShellIntegration
    {
        private static bool _iconsHidden;
        private static bool _trayHidden;

        public static bool AttachDesktop(IntPtr childHwnd)
        {
            try
            {
                IntPtr progman = NativeMethods.FindWindow("Progman", null);
                if (progman != IntPtr.Zero)
                    NativeMethods.SendMessage(progman, NativeMethods.WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero);

                IntPtr host = FindDefViewHost();
                if (host == IntPtr.Zero) host = progman;
                if (host == IntPtr.Zero) return false;

                NativeMethods.SetParent(childHwnd, host);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IntPtr FindDefViewHost()
        {
            IntPtr host = IntPtr.Zero;
            NativeMethods.EnumWindows((h, l) =>
            {
                IntPtr dv = NativeMethods.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero)
                {
                    host = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return host;
        }

        public static bool HideDesktopIcons(bool hide)
        {
            IntPtr host = FindDefViewHost();
            if (host == IntPtr.Zero) return false;
            IntPtr defView = NativeMethods.FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero) return false;
            _iconsHidden = hide;
            return NativeMethods.ShowWindow(defView, hide ? NativeMethods.SW_HIDE : NativeMethods.SW_SHOW);
        }

        public static bool HideSystemTaskbar(bool hide)
        {
            IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return false;
            _trayHidden = hide;
            return NativeMethods.ShowWindow(tray, hide ? NativeMethods.SW_HIDE : NativeMethods.SW_SHOW);
        }

        /// <summary>Полное восстановление стандартного рабочего стола Windows.</summary>
        public static void RestoreAll()
        {
            try
            {
                if (_iconsHidden) { HideDesktopIcons(false); _iconsHidden = false; }
                if (_trayHidden) { HideSystemTaskbar(false); _trayHidden = false; }
                // если explorer.exe был завершён — вернуть его
                bool explorerAlive = false;
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
                {
                    explorerAlive = true;
                    break;
                }
                if (!explorerAlive)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
                    { UseShellExecute = true });
                }
            }
            catch { }
        }

        public static void ReattachDesktop(IntPtr childHwnd)
        {
            if (childHwnd == IntPtr.Zero) return;
            AttachDesktop(childHwnd);
        }
    }

    /// <summary>Регистрация панели Neon как системной панели (appbar).</summary>
    public class AppBarService
    {
        private IntPtr _hwnd;
        private uint _callbackMsg;
        private bool _registered;

        public Rect Bounds { get; private set; }
        public uint CallbackMessage => _callbackMsg;
        public bool IsRegistered => _registered;

        public void Register(IntPtr hwnd, bool top, double heightDip)
        {
            Unregister();
            _hwnd = hwnd;
            _callbackMsg = NativeMethods.RegisterWindowMessage("StudyShellAppBar");
            var data = new NativeMethods.APPBARDATA
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                hWnd = hwnd,
                uCallbackMessage = _callbackMsg,
                uEdge = top ? NativeMethods.ABE_TOP : NativeMethods.ABE_BOTTOM,
            };
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref data);
            _registered = true;
            UpdatePosition(top, heightDip);
        }

        public void UpdatePosition(bool top, double heightDip)
        {
            if (!_registered) return;
            try
            {
                double scale = GetDpiScale();
                double screenW = SystemParameters.PrimaryScreenWidth * scale;
                double screenH = SystemParameters.PrimaryScreenHeight * scale;
                int height = Math.Max(1, (int)Math.Round(heightDip * scale));

                var rc = new NativeMethods.RECT
                {
                    Left = 0,
                    Right = (int)Math.Round(screenW),
                    Top = top ? 0 : (int)Math.Round(screenH) - height,
                    Bottom = top ? height : (int)Math.Round(screenH),
                };
                var data = new NativeMethods.APPBARDATA
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                    hWnd = _hwnd,
                    uCallbackMessage = _callbackMsg,
                    uEdge = top ? NativeMethods.ABE_TOP : NativeMethods.ABE_BOTTOM,
                    rc = rc,
                };
                NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref data);
                NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref data);

                Bounds = new Rect(
                    data.rc.Left / scale,
                    data.rc.Top / scale,
                    (data.rc.Right - data.rc.Left) / scale,
                    (data.rc.Bottom - data.rc.Top) / scale);
            }
            catch
            {
                Bounds = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, heightDip);
            }
        }

        public void Unregister()
        {
            if (!_registered) return;
            try
            {
                var data = new NativeMethods.APPBARDATA
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                    hWnd = _hwnd,
                };
                NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref data);
            }
            catch { }
            _registered = false;
            _hwnd = IntPtr.Zero;
        }

        private double GetDpiScale()
        {
            try
            {
                uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
                if (dpi > 0) return dpi / 96.0;
            }
            catch { }
            return 1.0;
        }
    }
}
