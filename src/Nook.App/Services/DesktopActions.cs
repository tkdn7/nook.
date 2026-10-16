using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nook.App.Services;

public static class DesktopActions
{
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus
    { public byte Ac, Flags, Percent, Reserved; public uint Remaining, Full; }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    public static BatterySample Battery()
    { return GetSystemPowerStatus(out var p) ? BatterySample.Decode(p.Ac, p.Flags, p.Percent, p.Remaining) : new(null, true, false, false, null); }
    public static void Open(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new IOException("항목을 찾을 수 없습니다.");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    public static void OpenSettings(string page) => Process.Start(new ProcessStartInfo("ms-settings:" + page) { UseShellExecute = true });
    public static void Power(bool restart)
    { Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), restart ? "/r /t 0" : "/s /t 0") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }); }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class Enumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IDevices
    {
        int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IDevice
    { [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object value); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IVolume
    {
        void RegisterControlChangeNotify(IntPtr notify); void UnregisterControlChangeNotify(IntPtr notify); void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float level, ref Guid context); void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float level); void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float level, ref Guid context); void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        void GetChannelVolumeLevel(uint channel, out float level); void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    private static T WithVolume<T>(Func<IVolume, T> action, bool microphone = false)
    {
        object enumerator = new Enumerator(); IDevice? device = null; object? volume = null;
        try
        {
            Marshal.ThrowExceptionForHR(((IDevices)enumerator).GetDefaultAudioEndpoint(microphone ? 1 : 0, microphone ? 2 : 1, out device));
            var iid = typeof(IVolume).GUID; Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out volume));
            return action((IVolume)volume);
        }
        finally { if (volume is not null) Marshal.ReleaseComObject(volume); if (device is not null) Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(enumerator); }
    }
    public static bool? Muted()
    { try { return WithVolume(v => { v.GetMute(out bool mute); return mute; }); } catch (Exception) { return null; } }
    public static void ToggleMute() => WithVolume(v => { v.GetMute(out bool mute); var context = Guid.Empty; v.SetMute(!mute, ref context); return true; });
    public static bool? MicrophoneMuted() { try { return WithVolume<bool?>(v => { v.GetMute(out bool mute); return mute; }, true); } catch (Exception) { return null; } }
    public static void ToggleMicrophone() => WithVolume(v => { v.GetMute(out bool mute); var context = Guid.Empty; v.SetMute(!mute, ref context); return true; }, true);
    public static void Capture() => Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
    public static int? Volume()
    { try { return WithVolume<int?>(v => { v.GetMasterVolumeLevelScalar(out float level); return (int)Math.Round(level * 100); }); } catch (Exception) { return null; } }
    public static void SetVolume(double percentage) => WithVolume(v => { var context = Guid.Empty; v.SetMasterVolumeLevelScalar((float)Math.Clamp(percentage / 100, 0, 1), ref context); return true; });

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WlanInfo
    { public Guid Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description; public uint State; }
    [StructLayout(LayoutKind.Sequential)] private struct RadioState { public uint Index, Software, Hardware; }
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, uint opcode, IntPtr reserved, out uint size, out IntPtr data, out uint type);
    [DllImport("wlanapi.dll")] private static extern uint WlanSetInterface(IntPtr handle, ref Guid id, uint opcode, uint size, ref RadioState data, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr value);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    private static bool? WifiCore(bool toggle)
    {
        uint opened = WlanOpenHandle(2, IntPtr.Zero, out _, out var handle);
        if (opened != 0) { if (toggle) throw new System.ComponentModel.Win32Exception((int)opened); return null; }
        IntPtr list = IntPtr.Zero;
        try
        {
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0) return null;
            int count = Marshal.ReadInt32(list); bool any = false, on = false;
            var radiosToSet = new List<(Guid Id, RadioState State)>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<WlanInfo>(IntPtr.Add(list, 8 + i * Marshal.SizeOf<WlanInfo>())); IntPtr data = IntPtr.Zero;
                try
                {
                    if (WlanQueryInterface(handle, ref item.Id, 4, IntPtr.Zero, out _, out data, out _) != 0) continue;
                    int radios = Math.Min(64, Marshal.ReadInt32(data));
                    for (int j = 0; j < radios; j++)
                    {
                        var radio = Marshal.PtrToStructure<RadioState>(IntPtr.Add(data, 4 + j * 12)); any = true;
                        bool enabled = radio.Software == 1 && radio.Hardware == 1; on |= enabled;
                        if (toggle) radiosToSet.Add((item.Id, radio));
                    }
                }
                finally { if (data != IntPtr.Zero) WlanFreeMemory(data); }
            }
            if (toggle && radiosToSet.Count == 0) throw new IOException("Wi-Fi 어댑터를 사용할 수 없습니다.");
            foreach (var target in radiosToSet)
            {
                var id = target.Id; var radio = target.State; radio.Software = on ? 2u : 1u;
                uint result = WlanSetInterface(handle, ref id, 4, 12, ref radio, IntPtr.Zero);
                if (result != 0) throw new System.ComponentModel.Win32Exception((int)result);
                if (radio.Software == 1 && radio.Hardware != 1) throw new IOException("장치의 무선 스위치를 확인해 주세요.");
            }
            return any ? on : null;
        }
        finally { if (list != IntPtr.Zero) WlanFreeMemory(list); WlanCloseHandle(handle, IntPtr.Zero); }
    }
    public static bool? Wifi() { try { return WifiCore(false); } catch (Exception) { return null; } }
    public static void ToggleWifi() => WifiCore(true);
}
