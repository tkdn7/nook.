using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Nook.App.Services;

/// <summary>Local foreground-app tracking. No window titles, URLs, or input contents are collected.</summary>
public sealed class ScreenTimeService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Tick; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder text, ref uint length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformationW(IntPtr handle, int index, StringBuilder text, uint length, out uint needed);
    private sealed record Observation(long Tick, DateTimeOffset Time, UsageApp? App, int Generation);
    private sealed class UsageFile { public int Version { get; set; } = 1; public List<UsageDay> Days { get; set; } = []; }
    private readonly string _file;
    private readonly Dictionary<string, UsageApp> _cache = new(StringComparer.OrdinalIgnoreCase);
    private Task<Observation>? _task;
    private bool _enabled, _disposed, _preserveOriginal;
    private volatile bool _locked;
    private int _generation, _saveTicks;
    public UsageLedger Ledger { get; }
    public AppIconStore Icons { get; }
    public IReadOnlyList<UsageDay> VisibleDays => _excludeNook ? Ledger.Days.Select(d => UsageLedger.Excluding(d, "nook")).ToArray() : Ledger.Days;
    public UsageDay Today { get { var day = Ledger.Today(DateOnly.FromDateTime(DateTime.Now)); return _excludeNook ? UsageLedger.Excluding(day, "nook") : day; } }
    public string? Warning { get; private set; }
    private volatile bool _excludeNook;
    public bool ExcludeNook
    {
        get => _excludeNook;
        set { if (_excludeNook == value) return; _excludeNook = value; Ledger.Reset(); System.Threading.Interlocked.Increment(ref _generation); }
    }
    public ScreenTimeService(string directory)
    {
        _file = Path.Combine(directory, "screen-time.json"); Ledger = Load(); Icons = new(directory);
        SystemEvents.SessionSwitch += SessionChanged; SystemEvents.PowerModeChanged += PowerChanged;
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect)
            _locked = true;
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect)
            _locked = false;
        System.Threading.Interlocked.Increment(ref _generation);
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode is PowerModes.Resume or PowerModes.Suspend) System.Threading.Interlocked.Increment(ref _generation); }
    public void Poll(bool enabled)
    {
        if (_disposed) return;
        if (_enabled != enabled) { _enabled = enabled; Ledger.Reset(); System.Threading.Interlocked.Increment(ref _generation); if (!enabled) Save(); }
        if (_task is { IsCompleted: true } task)
        {
            if (enabled && task.IsCompletedSuccessfully && task.Result.Generation == _generation)
                Ledger.Sample(task.Result.Tick, task.Result.Time, task.Result.App);
            else Ledger.Reset();
            _task = null;
        }
        if (!enabled) return;
        if (_task is null) { int generation = _generation; _task = Task.Run(() => Observe(generation)); }
        if (++_saveTicks >= 30) { _saveTicks = 0; Save(); }
    }
    private Observation Observe(int generation)
    {
        long tick = Environment.TickCount64; var now = DateTimeOffset.Now;
        var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (_locked || !GetLastInputInfo(ref input) || unchecked((uint)tick - input.Tick) >= 300000 || !InteractiveDesktop())
            return new(tick, now, null, generation);
        UsageApp app = new("unknown", "ETC", "#91969B");
        try
        {
            var window = GetForegroundWindow();
            GetWindowThreadProcessId(window, out uint pid);
            if (pid != 0)
            {
                using var process = Process.GetProcessById((int)pid);
                string id = process.ProcessName.ToLowerInvariant();
                if (_excludeNook && (pid == Environment.ProcessId || id == "nook")) return new(tick, now, null, generation);
                if (!_cache.TryGetValue(id, out app!))
                {
                    string path = Executable(pid), name = process.ProcessName;
                    if (!string.IsNullOrEmpty(path))
                    { string? description = FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(description)) name = description.Trim(); }
                    app = new(id, name, AppIconColor.Extract(path, id)); _cache[id] = app;
                    if (!string.IsNullOrEmpty(path)) _ = Icons.CaptureAsync(id, path);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        { app = new("unknown", "ETC", "#91969B"); }
        return new(tick, now, app, generation);
    }
    private static bool InteractiveDesktop()
    {
        IntPtr desktop = OpenInputDesktop(0, false, 1); if (desktop == IntPtr.Zero) return false;
        try { var name = new StringBuilder(128); return GetUserObjectInformationW(desktop, 2, name, 256, out _) && name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase); }
        finally { CloseDesktop(desktop); }
    }
    private static string Executable(uint pid)
    {
        var process = OpenProcess(0x1000, false, pid); if (process == IntPtr.Zero) return "";
        try { var path = new StringBuilder(32768); uint length = 32768; return QueryFullProcessImageNameW(process, 0, path, ref length) ? path.ToString() : ""; }
        finally { CloseHandle(process); }
    }
    private UsageLedger Load()
    {
        if (!File.Exists(_file)) return new();
        try
        {
            var data = JsonSerializer.Deserialize<UsageFile>(File.ReadAllText(_file)) ?? throw new JsonException();
            if (data.Version != 1 || data.Days is null || data.Days.Count > 1000
                || data.Days.Any(d => d is null || d.Apps is null || d.Apps.Any(a => a is null || string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Name) || !ValidColor(a.Color) || !double.IsFinite(a.Seconds) || a.Seconds < 0 || a.Seconds > 86400))
                || data.Days.Select(x => x.Date).Distinct().Count() != data.Days.Count
                || data.Days.Any(d => d.Apps.Select(a => a.Id).Distinct().Count() != d.Apps.Count))
                throw new JsonException();
            return new(data.Days);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Warning = "사용 기록을 읽을 수 없습니다.";
            try { File.Copy(_file, _file + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"); }
            catch (Exception backup) when (backup is IOException or UnauthorizedAccessException) { _preserveOriginal = true; }
            return new();
        }
    }
    private static bool ValidColor(string? color) => color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
    public void Save()
    {
        if (_preserveOriginal) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(_file)!); File.WriteAllText(_file + ".tmp", JsonSerializer.Serialize(new UsageFile { Days = Ledger.Days })); File.Move(_file + ".tmp", _file, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warning = "사용 기록을 저장할 수 없습니다."; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Save();
        SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged;
    }
}

public static class AppIconColor
{
    public static string Extract(string path, string id)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return Fallback(id);
            using var original = icon.ToBitmap(); using var bitmap = new System.Drawing.Bitmap(original, 32, 32);
            var bins = new Dictionary<int, (double Weight, double R, double G, double B)>();
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                var c = bitmap.GetPixel(x, y); int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
                if (c.A < 96 || max < 40 || min > 225 || max - min < 24) continue;
                int bin = (c.R >> 5) << 6 | (c.G >> 5) << 3 | c.B >> 5;
                double weight = c.A / 255.0 * (max - min) / 255.0;
                var b = bins.GetValueOrDefault(bin); bins[bin] = (b.Weight + weight, b.R + c.R * weight, b.G + c.G * weight, b.B + c.B * weight);
            }
            if (bins.Count == 0) return Fallback(id);
            var best = bins.OrderByDescending(x => x.Value.Weight).First().Value;
            return $"#{(int)(best.R / best.Weight):X2}{(int)(best.G / best.Weight):X2}{(int)(best.B / best.Weight):X2}";
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or ExternalException) { return Fallback(id); }
    }
    public static string Fallback(string id)
    {
        string[] palette = ["#7296B8", "#A088B7", "#BD8E72", "#70A398", "#A4A06E", "#AF7E94"];
        uint hash = 2166136261; foreach (char c in id.ToLowerInvariant()) hash = unchecked((hash ^ c) * 16777619);
        return palette[hash % (uint)palette.Length];
    }
}
