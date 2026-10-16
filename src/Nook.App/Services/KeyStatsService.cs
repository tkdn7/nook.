using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
namespace Nook.App.Services;
public sealed class KeyStatsService : IDisposable
{
    public KeyCounts Data { get; private set; } = new();
    public bool Available => _hook != IntPtr.Zero;
    private readonly string _file;
    private readonly HookProc _callback;
    private IntPtr _hook;
    private bool _dirty;
    private DateTimeOffset _lastSave;
    private bool _disposed;
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct KeyData { public uint Key, Scan, Flags, Time; public IntPtr Extra; }
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookExW(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    public KeyStatsService(string directory)
    {
        _file = Path.Combine(directory, "key-counts.json"); _callback = Hook;
        SystemEvents.SessionSwitch += SessionChanged; SystemEvents.PowerModeChanged += PowerChanged;
        try
        {
            if (File.Exists(_file))
            {
                string json = File.ReadAllText(_file);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("Total", out var total))
                {
                    Data.Total = total.GetInt64();
                    if (Data.Total is < 0 or > 254000000000) throw new JsonException("Invalid total");
                }
                else
                {
                    var counts = JsonSerializer.Deserialize<Dictionary<int, long>>(json);
                    if (counts is null || counts.Any(x => x.Key is < 1 or > 254 || x.Value is < 0 or > 1000000000)) throw new JsonException("Invalid counts");
                    Data.Total = counts.Values.Sum();
                    _dirty = true;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { try { if (File.Exists(_file)) File.Copy(_file, _file + ".invalid-" + Guid.NewGuid().ToString("N")); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    private IntPtr Hook(int code, IntPtr message, IntPtr pointer)
    {
        if (code >= 0)
        {
            int kind = message.ToInt32();
            if (kind is 0x0100 or 0x0104 or 0x0101 or 0x0105)
            { var key = Marshal.PtrToStructure<KeyData>(pointer); _dirty |= Data.Record((int)key.Key, kind is 0x0100 or 0x0104, (key.Flags & 0x10) != 0); }
        }
        return CallNextHookEx(_hook, code, message, pointer);
    }
    public void Poll(bool enabled)
    {
        if (enabled && _hook == IntPtr.Zero) _hook = SetWindowsHookExW(13, _callback, GetModuleHandleW(null), 0);
        if (!enabled && _hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; Data.ReleaseAll(); Save(); }
        if (_dirty && DateTimeOffset.UtcNow - _lastSave > TimeSpan.FromSeconds(30)) Save();
    }
    private void ReleasePending() { if (!_disposed) Application.Current.Dispatcher.BeginInvoke(() => Data.ReleaseAll()); }
    private void SessionChanged(object sender, SessionSwitchEventArgs e) => ReleasePending();
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => ReleasePending();
    public void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(_file)!); File.WriteAllText(_file + ".tmp", JsonSerializer.Serialize(new { Version = 2, Data.Total })); File.Move(_file + ".tmp", _file, true); _dirty = false; _lastSave = DateTimeOffset.UtcNow; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    public void Dispose() { _disposed = true; SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged; if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; Save(); }
}
