using System.Runtime.InteropServices;

namespace Nook.App.Services;

public readonly record struct GpuReading(DateTimeOffset Time, double? Usage, string Engine, bool Warming);

/// <summary>WDDM GPU Engine counters. One query shared by all GPU widgets.</summary>
public sealed class GpuCounter : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status; public double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, UIntPtr data, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr data, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    private readonly object _gate = new();
    private IntPtr _query, _counter;
    private bool _primed, _disposed;
    private DateTimeOffset _retry;
    public void Reset() { lock (_gate) { Close(); _retry = default; } }
    public GpuReading Sample()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            if (_disposed || now < _retry) return new(now, null, "", false);
            if (_query == IntPtr.Zero && (PdhOpenQueryW(null, UIntPtr.Zero, out _query) != 0
                || PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", UIntPtr.Zero, out _counter) != 0))
                return Failed(now);
            if (PdhCollectQueryData(_query) != 0) return Failed(now);
            if (!_primed) { _primed = true; return new(now, null, "", true); }
            uint bytes = 0;
            const uint format = 0x200 | 0x8000; // DOUBLE | NOCAP100; cap after per-engine process aggregation.
            uint status = PdhGetFormattedCounterArrayW(_counter, format, ref bytes, out _, IntPtr.Zero);
            if (status != 0x800007D2 || bytes == 0 || bytes > 32 * 1024 * 1024) return Failed(now);
            var buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                if (PdhGetFormattedCounterArrayW(_counter, format, ref bytes, out uint count, buffer) != 0) return Failed(now);
                int stride = Marshal.SizeOf<CounterItem>();
                if ((ulong)count * (uint)stride > bytes) return Failed(now);
                var values = new List<GpuEngineSample>();
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, i * stride));
                    if (item.Value.Status <= 1 && item.Name != IntPtr.Zero)
                        values.Add(new(Marshal.PtrToStringUni(item.Name) ?? "", item.Value.Value));
                }
                var usage = GpuAggregation.Resolve(values);
                return new(now, usage.Percentage, usage.Engine, false);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
    private GpuReading Failed(DateTimeOffset now) { Close(); _retry = now.AddSeconds(15); return new(now, null, "", false); }
    private void Close() { if (_query != IntPtr.Zero) PdhCloseQuery(_query); _query = _counter = IntPtr.Zero; _primed = false; }
    public void Dispose() { lock (_gate) { _disposed = true; Close(); } }
}
