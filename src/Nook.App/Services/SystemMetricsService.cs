using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Threading.Tasks;

namespace Nook.App.Services;

public sealed class SystemMetricsService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    private readonly CpuCalculator _cpu = new();
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher;
    private bool _warming = true;
    private readonly GpuCounter _gpu = new();
    private Task<GpuReading>? _gpuTask;
    private GpuReading _gpuReading = new(DateTimeOffset.Now, null, "", true);
    public MetricHistory GpuHistory { get; } = new();
    public GpuReading Gpu => _gpuReading;
    public MetricHistory CpuHistory { get; } = new();
    public MetricHistory MemoryHistory { get; } = new();
    public SystemSample Current { get; private set; } = new(DateTimeOffset.Now, null, null, 0, 0, true);
    public event Action? Updated;

    public SystemMetricsService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _timer = new(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Tick;
        SystemEvents.PowerModeChanged += OnPower;
        Sample();
        _timer.Start();
    }
    private void Tick(object? sender, EventArgs e) => Sample();
    private void OnPower(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _dispatcher.BeginInvoke(() =>
        { _cpu.Reset(); _warming = true; CpuHistory.Clear(); MemoryHistory.Clear(); GpuHistory.Clear();
            // Serialize reset with any in-flight query without blocking the UI.
            _gpuTask = Task.Run(() => { _gpu.Reset(); return _gpu.Sample(); });
            _gpuReading = new(DateTimeOffset.Now, null, "", true); Sample(); });
    }
    private void Sample()
    {
        var now = DateTimeOffset.Now;
        if (_gpuTask is { IsCompleted: true })
        {
            _gpuReading = _gpuTask.IsCompletedSuccessfully ? _gpuTask.Result : new(now, null, "", false);
            GpuHistory.Add(_gpuReading.Time, _gpuReading.Usage);
            _gpuTask = null;
        }
        _gpuTask ??= Task.Run(_gpu.Sample);
        double? cpu = null, memory = null;
        bool warming = _warming;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        { cpu = _cpu.Sample(new(idle.Value, kernel.Value, user.Value)); _warming = false; }
        else { _cpu.Reset(); _warming = true; warming = false; }
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        ulong used = 0, total = 0;
        if (GlobalMemoryStatusEx(ref status) && status.TotalPhys > 0 && status.AvailPhys <= status.TotalPhys)
        { total = status.TotalPhys; used = total - status.AvailPhys; memory = used / (double)total * 100; }
        Current = new(now, cpu, memory, used, total, warming && cpu is null);
        CpuHistory.Add(now, cpu); MemoryHistory.Add(now, memory);
        Updated?.Invoke();
    }
    public void Dispose()
    {
        _timer.Stop(); _timer.Tick -= Tick;
        SystemEvents.PowerModeChanged -= OnPower;
        _gpu.Dispose();
    }
}
