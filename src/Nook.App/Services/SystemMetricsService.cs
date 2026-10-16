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
    private readonly DiskCounter _disk = new();
    private readonly Func<GpuReading> _sampleGpu;
    private readonly Func<DiskReading> _sampleDisk;
    private bool _gpuRequested, _diskRequested, _gpuReset, _diskReset, _disposed;
    private int _gpuGeneration, _diskGeneration, _gpuTaskGeneration, _diskTaskGeneration;
    public bool GpuRequested => _gpuRequested;
    public bool DiskRequested => _diskRequested;
    public long GpuQueriesScheduled { get; private set; }
    public long DiskQueriesScheduled { get; private set; }
    private Task<DiskReading>? _diskTask;
    public DiskReading Disk { get; private set; } = new(DateTimeOffset.Now, null, null, true);
    public MetricHistory DiskReadHistory { get; } = new();
    public MetricHistory DiskWriteHistory { get; } = new();
    private Task<GpuReading>? _gpuTask;
    private GpuReading _gpuReading = new(DateTimeOffset.Now, null, "", true);
    public MetricHistory GpuHistory { get; } = new();
    public GpuReading Gpu => _gpuReading;
    public MetricHistory CpuHistory { get; } = new();
    public MetricHistory MemoryHistory { get; } = new();
    public SystemSample Current { get; private set; } = new(DateTimeOffset.Now, null, null, 0, 0, true);
    public event Action? Updated;

    public SystemMetricsService(Dispatcher dispatcher, Func<GpuReading>? sampleGpu = null, Func<DiskReading>? sampleDisk = null, bool startTimer = true)
    {
        _dispatcher = dispatcher;
        _sampleGpu = sampleGpu ?? _gpu.Sample; _sampleDisk = sampleDisk ?? _disk.Sample;
        _timer = new(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Tick;
        SystemEvents.PowerModeChanged += OnPower;
        Sample();
        if (startTimer) _timer.Start();
    }
    public void SetDemand(bool gpu, bool disk)
    {
        if (_disposed) return;
        if (_gpuRequested != gpu)
        {
            _gpuRequested = gpu; _gpuGeneration++; _gpuReset = true; GpuHistory.Clear();
            _gpuReading = new(DateTimeOffset.Now, null, "", gpu);
        }
        if (_diskRequested != disk)
        {
            _diskRequested = disk; _diskGeneration++; _diskReset = true;
            DiskReadHistory.Clear(); DiskWriteHistory.Clear(); Disk = new(DateTimeOffset.Now, null, null, disk);
        }
    }
    public void SampleNow() => Sample();
    private void Tick(object? sender, EventArgs e) => Sample();
    private void OnPower(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _dispatcher.BeginInvoke(() =>
        { if (_disposed) return; _cpu.Reset(); _warming = true; CpuHistory.Clear(); MemoryHistory.Clear(); GpuHistory.Clear();
            // Retire an in-flight generation; never create an overlapping query on resume.
            _gpuGeneration++; _gpuReset = true; _diskGeneration++; _diskReset = true;
            _gpuReading = new(DateTimeOffset.Now, null, "", _gpuRequested);
            DiskReadHistory.Clear(); DiskWriteHistory.Clear();
            Disk = new(DateTimeOffset.Now, null, null, _diskRequested); Sample(); });
    }
    private void Sample()
    {
        var now = DateTimeOffset.Now;
        if (_disposed) return;
        if (_diskTask is { IsCompleted: true })
        {
            if (_diskRequested && _diskTaskGeneration == _diskGeneration)
            {
                Disk = _diskTask.IsCompletedSuccessfully ? _diskTask.Result : new(now, null, null, false);
                DiskReadHistory.Add(Disk.Time, Disk.Read); DiskWriteHistory.Add(Disk.Time, Disk.Write);
            }
            _diskTask = null;
        }
        if (_diskTask is null && (_diskRequested || _diskReset))
        {
            bool reset = _diskReset, sample = _diskRequested; _diskReset = false;
            _diskTaskGeneration = sample ? _diskGeneration : -1;
            if (sample) DiskQueriesScheduled++;
            _diskTask = Task.Run(() => { if (reset) _disk.Reset(); return sample ? _sampleDisk() : new(now, null, null, false); });
        }
        if (_gpuTask is { IsCompleted: true })
        {
            if (_gpuRequested && _gpuTaskGeneration == _gpuGeneration)
            {
                _gpuReading = _gpuTask.IsCompletedSuccessfully ? _gpuTask.Result : new(now, null, "", false);
                GpuHistory.Add(_gpuReading.Time, _gpuReading.Usage);
            }
            _gpuTask = null;
        }
        if (_gpuTask is null && (_gpuRequested || _gpuReset))
        {
            bool reset = _gpuReset, sample = _gpuRequested; _gpuReset = false;
            _gpuTaskGeneration = sample ? _gpuGeneration : -1;
            if (sample) GpuQueriesScheduled++;
            _gpuTask = Task.Run(() => { if (reset) _gpu.Reset(); return sample ? _sampleGpu() : new(now, null, "", false); });
        }
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
        _disposed = true;
        _timer.Stop(); _timer.Tick -= Tick;
        SystemEvents.PowerModeChanged -= OnPower;
        _gpu.Dispose();
        _disk.Dispose();
    }
}
