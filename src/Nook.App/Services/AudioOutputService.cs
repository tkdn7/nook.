using System.Runtime.InteropServices;
using System.Threading.Tasks;
namespace Nook.App.Services;

public sealed record AudioOutputDevice(string Id, string Name, bool Default);
public sealed class AudioOutputService
{
    private readonly Func<IReadOnlyList<AudioOutputDevice>> _read;
    private readonly Action<string> _select;
    private DateTimeOffset _last;
    private bool _refreshing;
    private int _revision;
    public IReadOnlyList<AudioOutputDevice> Devices { get; private set; } = [];
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public event Action? Updated;
    public AudioOutputService() : this(AudioOutputNative.Read, AudioOutputNative.Select) { }
    public AudioOutputService(Func<IReadOnlyList<AudioOutputDevice>> read, Action<string> select) { _read = read; _select = select; }
    public async Task RefreshAsync(bool force = false)
    {
        if (_refreshing || Busy || !force && DateTimeOffset.UtcNow - _last < TimeSpan.FromSeconds(3)) return;
        _refreshing = true;
        int revision = _revision;
        try { var devices = await Task.Run(_read); if (revision == _revision) { Devices = devices; Error = null; } }
        catch (Exception ex) when (ex is COMException or InvalidCastException or IOException) { Error = "출력 장치를 읽을 수 없습니다."; }
        finally { _refreshing = false; _last = DateTimeOffset.UtcNow; Updated?.Invoke(); }
    }
    public async Task SelectAsync(string id)
    {
        if (Busy) return;
        Busy = true; _revision++; Error = null; Updated?.Invoke();
        try
        {
            await Task.Run(() => { if (!_read().Any(d => d.Id == id)) throw new IOException("장치 연결을 확인해 주세요."); _select(id); });
            Devices = await Task.Run(_read);
            if (!Devices.Any(d => d.Id == id && d.Default)) Error = "기본 출력 변경을 확인할 수 없습니다.";
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or IOException) { Error = ex is IOException ? ex.Message : "출력 장치를 변경할 수 없습니다."; }
        finally { Busy = false; _last = DateTimeOffset.UtcNow; Updated?.Invoke(); }
    }
}

internal static class AudioOutputNative
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class Enumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IDevices
    { [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IDeviceCollection devices); [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device); }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IDeviceCollection
    { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out IProperties store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IProperties
    { [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out PropertyKey key); [PreserveSig] int GetValue(ref PropertyKey key, out Variant value); }
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] private class PolicyClient { }
    // The setter is an undocumented Windows ABI. Unused slots are never called.
    // References and compatibility notes: docs/AUDIO_OUTPUT.md.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IPolicy
    {
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
        void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode(); void GetPropertyValue(); void SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
    public static IReadOnlyList<AudioOutputDevice> Read()
    {
        object enumerator = new Enumerator(); IDeviceCollection? collection = null;
        try
        {
            var devices = (IDevices)enumerator; string? current = DefaultId(devices, 1);
            Marshal.ThrowExceptionForHR(devices.EnumAudioEndpoints(0, 1, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out uint count)); var result = new List<AudioOutputDevice>();
            for (uint i = 0; i < count; i++)
            {
                IDevice? device = null; IProperties? properties = null; var value = new Variant();
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out device)); Marshal.ThrowExceptionForHR(device.GetId(out string id));
                    Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
                    var key = new PropertyKey { Format = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                    Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out value));
                    string name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "오디오 장치" : "오디오 장치";
                    result.Add(new(id, name, id == current));
                }
                finally { PropVariantClear(ref value); Release(properties); Release(device); }
            }
            return result.OrderByDescending(d => d.Default).ThenBy(d => d.Name).ToArray();
        }
        finally { Release(collection); Release(enumerator); }
    }
    private static string? DefaultId(IDevices devices, int role)
    {
        IDevice? device = null;
        try { if (devices.GetDefaultAudioEndpoint(0, role, out device) < 0) return null; Marshal.ThrowExceptionForHR(device.GetId(out string id)); return id; }
        finally { Release(device); }
    }
    public static void Select(string id)
    {
        object enumerator = new Enumerator(), policy = new PolicyClient(); var previous = new string?[3]; var changed = new List<int>();
        try
        {
            for (int role = 0; role < 3; role++) previous[role] = DefaultId((IDevices)enumerator, role);
            for (int role = 0; role < 3; role++) { Marshal.ThrowExceptionForHR(((IPolicy)policy).SetDefaultEndpoint(id, role)); changed.Add(role); }
        }
        catch { foreach (int role in changed) if (previous[role] is { } old) ((IPolicy)policy).SetDefaultEndpoint(old, role); throw; }
        finally { Release(policy); Release(enumerator); }
    }
    private static void Release(object? value) { if (value is not null) Marshal.ReleaseComObject(value); }
}
