using System.Runtime.InteropServices;

namespace Nook.App.Services;

public sealed record BrightnessDisplay(string Id, string Name, int Percentage);

public static class BrightnessService
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    { public uint Size; public NativeRect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct PhysicalMonitor
    { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("dxva2.dll")] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll")] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] physical);
    [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] physical);
    [DllImport("dxva2.dll")] private static extern bool GetMonitorCapabilities(IntPtr monitor, out uint capabilities, out uint temperature);
    [DllImport("dxva2.dll")] private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);
    [DllImport("dxva2.dll")] private static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private static void Wmi(string query, Action<dynamic, dynamic> action)
    {
        object? locator = null, service = null, collection = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!);
            service = ((dynamic)locator!).ConnectServer(".", "root\\wmi");
            collection = ((dynamic)service).ExecQuery(query);
            foreach (object item in (System.Collections.IEnumerable)collection)
                try { action((dynamic)service, (dynamic)item); } finally { Release(item); }
        }
        finally { Release(collection); Release(service); Release(locator); }
    }
    private static void Physical(Action<string, string, IntPtr, uint, uint, uint> action)
    {
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info) || !GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count == 0 || count > 16) return true;
            var physical = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) return true;
            try
            {
                for (int i = 0; i < physical.Length; i++)
                {
                    var p = physical[i];
                    if (GetMonitorCapabilities(p.Handle, out uint caps, out _) && (caps & 2) != 0
                        && GetMonitorBrightness(p.Handle, out uint min, out uint value, out uint max) && max > min)
                        action($"ddc:{info.Device}:{i}", string.IsNullOrWhiteSpace(p.Description) ? info.Device : p.Description, p.Handle, min, value, max);
                }
            }
            finally { DestroyPhysicalMonitors(count, physical); }
            return true;
        }, IntPtr.Zero);
    }
    public static IReadOnlyList<BrightnessDisplay> Read()
    {
        var result = new List<BrightnessDisplay>();
        try { Wmi("SELECT * FROM WmiMonitorBrightness WHERE Active=TRUE", (_, item) => result.Add(new("wmi:" + (string)item.InstanceName, "내장 디스플레이", Convert.ToInt32(item.CurrentBrightness)))); } catch (Exception) { }
        try { Physical((id, name, handle, min, value, max) => result.Add(new(id, name, (int)Math.Round((value - min) * 100.0 / (max - min))))); } catch (Exception) { }
        return result;
    }
    public static BrightnessDisplay? Choose(IReadOnlyList<BrightnessDisplay> displays, WidgetInstance widget)
    {
        if (!string.IsNullOrEmpty(widget.BrightnessTarget)) return displays.FirstOrDefault(d => d.Id == widget.BrightnessTarget);
        string device = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)widget.Left, (int)widget.Top)).DeviceName;
        return displays.FirstOrDefault(d => d.Id.StartsWith("ddc:" + device + ":", StringComparison.Ordinal)) ?? displays.FirstOrDefault();
    }
    public static void Set(string id, double percentage)
    {
        int value = (int)Math.Round(Math.Clamp(percentage, 0, 100)); bool changed = false;
        if (id.StartsWith("wmi:", StringComparison.Ordinal))
            Wmi("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE", (_, item) =>
            {
                if ("wmi:" + (string)item.InstanceName != id) return;
                object? methods = null, method = null, parameters = null, output = null;
                try
                {
                    methods = item.Methods_; method = ((dynamic)methods).Item("WmiSetBrightness");
                    parameters = ((dynamic)method).InParameters.SpawnInstance_();
                    ((dynamic)parameters).Timeout = 0u; ((dynamic)parameters).Brightness = (byte)value;
                    output = item.ExecMethod_("WmiSetBrightness", parameters);
                    changed = Convert.ToUInt32(((dynamic)output).ReturnValue) == 0;
                }
                finally { Release(output); Release(parameters); Release(method); Release(methods); }
            });
        else Physical((target, name, handle, min, current, max) =>
        { if (target == id) changed = SetMonitorBrightness(handle, min + (uint)Math.Round((max - min) * value / 100.0)); });
        if (!changed) throw new InvalidOperationException("디스플레이가 밝기 조절을 지원하지 않거나 연결이 끊겼습니다.");
    }
}
