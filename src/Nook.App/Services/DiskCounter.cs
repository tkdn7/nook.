using System.Runtime.InteropServices;

namespace Nook.App.Services;

public readonly record struct DiskReading(DateTimeOffset Time, double? Read, double? Write, bool Warming);

/// <summary>Total physical disk throughput, shared across all instances.</summary>
public sealed class DiskCounter : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Value { public uint Status; public double Number; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, UIntPtr data, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr data, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out Value value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    private readonly object _gate = new();
    private IntPtr _query, _read, _write;
    private bool _primed, _disposed;
    private DateTimeOffset _retry;
    public DiskReading Sample()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            if (_disposed || now < _retry) return new(now, null, null, false);
            if (_query == IntPtr.Zero && (PdhOpenQueryW(null, UIntPtr.Zero, out _query) != 0
                || PdhAddEnglishCounterW(_query, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", UIntPtr.Zero, out _read) != 0
                || PdhAddEnglishCounterW(_query, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", UIntPtr.Zero, out _write) != 0)) return Failed(now);
            if (PdhCollectQueryData(_query) != 0) return Failed(now);
            if (!_primed) { _primed = true; return new(now, null, null, true); }
            double? read = Read(_read), write = Read(_write);
            if (read is null || write is null) return Failed(now);
            return new(now, read, write, false);
        }
    }
    private static double? Read(IntPtr counter) => PdhGetFormattedCounterValue(counter, 0x200 | 0x8000, out _, out var value) == 0
        && value.Status <= 1 && double.IsFinite(value.Number) && value.Number >= 0 ? value.Number : null;
    public static string Speed(double? bytes) => bytes is not { } b || !double.IsFinite(b) || b < 0 ? "—"
        : b >= 1073741824 ? $"{b / 1073741824:F1} GB/s" : b >= 1048576 ? $"{b / 1048576:F1} MB/s" : b >= 1024 ? $"{b / 1024:F0} KB/s" : $"{b:F0} B/s";
    private DiskReading Failed(DateTimeOffset now) { Close(); _retry = now.AddSeconds(15); return new(now, null, null, false); }
    private void Close() { if (_query != IntPtr.Zero) PdhCloseQuery(_query); _query = _read = _write = IntPtr.Zero; _primed = false; }
    public void Reset() { lock (_gate) { Close(); _retry = default; } }
    public void Dispose() { lock (_gate) { _disposed = true; Close(); } }
}
