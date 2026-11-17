using System;
using System.Runtime.InteropServices;
using StudyShell.Core.Helpers;

namespace StudyShell.Services
{
    /// <summary>Лёгкая системная информация (CPU/RAM — один системный вызов).</summary>
    public static class SystemServices
    {
        private static readonly object CpuLock = new();
        private static ulong _lastIdle, _lastKernel, _lastUser;
        private static bool _hasCpuSample;

        public static double SystemCpuPercent
        {
            get
            {
                try
                {
                    if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
                    ulong idleT = idle.ToUlong(), kernelT = kernel.ToUlong(), userT = user.ToUlong();
                    lock (CpuLock)
                    {
                        if (!_hasCpuSample)
                        {
                            _lastIdle = idleT; _lastKernel = kernelT; _lastUser = userT;
                            _hasCpuSample = true;
                            return 0;
                        }
                        double idleDelta = idleT - _lastIdle;
                        double totalDelta = (kernelT + userT) - (_lastKernel + _lastUser);
                        _lastIdle = idleT; _lastKernel = kernelT; _lastUser = userT;
                        if (totalDelta <= 0) return 0;
                        return Math.Round(Math.Clamp(1.0 - idleDelta / totalDelta, 0, 1) * 100.0, 1);
                    }
                }
                catch { return 0; }
            }
        }

        public static double RamUsedPercent
        {
            get
            {
                try
                {
                    var status = new NativeMethods.MEMORYSTATUSEX
                    {
                        dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>(),
                    };
                    if (!NativeMethods.GlobalMemoryStatusEx(ref status)) return 0;
                    if (status.ullTotalPhys == 0) return status.dwMemoryLoad;
                    return Math.Round((status.ullTotalPhys - status.ullAvailPhys) * 100.0 / status.ullTotalPhys, 1);
                }
                catch { return 0; }
            }
        }

        // ---------- питание ----------

        public static class PowerService
        {
            public static void Shutdown() => RunShutdown("/s /t 0");
            public static void Restart() => RunShutdown("/r /t 0");
            public static void SignOut() => RunShutdown("/l");
            public static void Lock() => NativeMethods.LockWorkStation();

            public static void Sleep()
            {
                if (!NativeMethods.SetSuspendState(false, false, false))
                    AppLauncher.OpenSystemSettings("ms-settings:powersleep");
            }

            private static void RunShutdown(string args)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                }
                catch { }
            }
        }

        // ---------- громкость ----------

        public class VolumeService
        {
            [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
            private class MMDeviceEnumeratorComObject { }

            [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IMMDeviceEnumerator
            {
                int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
                int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
                int GetDevice(string id, out IMMDevice device);
                int RegisterEndpointNotificationCallback(IntPtr client);
                int UnregisterEndpointNotificationCallback(IntPtr client);
            }

            [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IMMDevice
            {
                int Activate(ref Guid id, int clsCtx, IntPtr activationParams,
                    [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
                int OpenPropertyStore(int storageAccess, out IntPtr properties);
                int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
                int GetState(out int state);
            }

            [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IAudioEndpointVolume
            {
                int RegisterControlChangeNotify(IntPtr notify);
                int UnregisterControlChangeNotify(IntPtr notify);
                int GetChannelCount(out int count);
                int SetMasterVolumeLevel(float level, ref Guid context);
                int SetMasterVolumeLevelScalar(float level, ref Guid context);
                int GetMasterVolumeLevel(out float level);
                int GetMasterVolumeLevelScalar(out float level);
                int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
                int GetChannelVolumeLevel(uint channel, out float level);
                int SetMute(bool mute, ref Guid context);
                int GetMute(out bool mute);
            }

            private readonly IAudioEndpointVolume _volume;
            private readonly bool _available;
            private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

            public VolumeService()
            {
                try
                {
                    var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                    if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) == 0)
                    {
                        var guid = IID_IAudioEndpointVolume;
                        if (device.Activate(ref guid, 1, IntPtr.Zero, out var obj) == 0)
                        {
                            _volume = (IAudioEndpointVolume)obj;
                            _available = true;
                        }
                    }
                }
                catch { _available = false; }
            }

            public bool IsAvailable => _available;

            public double GetVolume()
            {
                if (!_available) return -1;
                try
                {
                    var guid = Guid.Empty;
                    _volume.GetMasterVolumeLevelScalar(out float v);
                    return Math.Round(v, 3);
                }
                catch { return -1; }
            }

            public void SetVolume(double value)
            {
                if (!_available) return;
                try
                {
                    var guid = Guid.Empty;
                    _volume.SetMasterVolumeLevelScalar((float)Math.Clamp(value, 0, 1), ref guid);
                }
                catch { }
            }

            public bool GetMute()
            {
                if (!_available) return false;
                try { _volume.GetMute(out bool m); return m; }
                catch { return false; }
            }

            public void SetMute(bool mute)
            {
                if (!_available) return;
                try { var guid = Guid.Empty; _volume.SetMute(mute, ref guid); }
                catch { }
            }
        }

        // ---------- батарея ----------

        public static class BatteryService
        {
            public static bool IsPresent
            {
                get
                {
                    try { return System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery; }
                    catch { return false; }
                }
            }

            public static int Percent
            {
                get
                {
                    try { return (int)Math.Round(System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent * 100); }
                    catch { return -1; }
                }
            }

            public static bool IsCharging
            {
                get
                {
                    try { return System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.Charging; }
                    catch { return false; }
                }
            }
        }

        // ---------- сеть ----------

        public class NetworkService
        {
            private long _lastBytesIn;
            private long _lastBytesOut;
            private long _lastMs;
            private bool _hasSample;

            public string InterfaceName { get; private set; } = "Нет сети";
            public string IpAddress { get; private set; } = "—";
            public bool IsConnected { get; private set; }
            public bool IsWifi { get; private set; }
            public double DownMbps { get; private set; }
            public double UpMbps { get; private set; }

            public void Refresh()
            {
                try
                {
                    var active = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                                             n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);
                    IsConnected = active != null;
                    if (active == null)
                    {
                        InterfaceName = "Нет сети";
                        IpAddress = "—";
                        DownMbps = 0; UpMbps = 0;
                        _hasSample = false;
                        return;
                    }
                    InterfaceName = active.Name;
                    IsWifi = active.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ||
                             active.Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase);
                    var ip = active.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    IpAddress = ip?.Address.ToString() ?? "—";
                    var stats = active.GetIPv4Statistics();
                    long now = Environment.TickCount64;
                    if (_hasSample)
                    {
                        long ms = Math.Max(1, now - _lastMs);
                        DownMbps = Math.Round((stats.BytesReceived - _lastBytesIn) * 8.0 / 1000.0 / ms, 2);
                        UpMbps = Math.Round((stats.BytesSent - _lastBytesOut) * 8.0 / 1000.0 / ms, 2);
                    }
                    _lastBytesIn = stats.BytesReceived;
                    _lastBytesOut = stats.BytesSent;
                    _lastMs = now;
                    _hasSample = true;
                }
                catch { IsConnected = false; }
            }
        }
    }
}
