namespace Nook.Core;

public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

public sealed class CpuCalculator
{
    private CpuTimes? _previous;
    public void Reset() => _previous = null;
    public double? Sample(CpuTimes times)
    {
        var previous = _previous;
        _previous = times;
        if (previous is not { } p || times.Idle < p.Idle || times.Kernel < p.Kernel || times.User < p.User)
            return null;
        double total = (double)(times.Kernel - p.Kernel) + (times.User - p.User);
        double idle = times.Idle - p.Idle;
        if (total <= 0 || idle > total) return null;
        return Math.Clamp((total - idle) / total * 100, 0, 100);
    }
}

public readonly record struct MetricPoint(DateTimeOffset Time, double? Value);
public sealed record SystemSample(DateTimeOffset Time, double? Cpu, double? Memory, ulong UsedBytes, ulong TotalBytes, bool CpuWarming);

public sealed class MetricHistory
{
    private readonly List<MetricPoint> _points = [];
    public IReadOnlyList<MetricPoint> Points => _points;
    public void Add(DateTimeOffset time, double? value)
    {
        _points.Add(new(time, value));
        _points.RemoveAll(p => p.Time <= time.AddSeconds(-60));
        if (_points.Count > 60) _points.RemoveRange(0, _points.Count - 60);
    }
    public void Clear() => _points.Clear();
}

public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public static class Placement
{
    public static ScreenRect Clamp(ScreenRect window, ScreenRect work)
    {
        double width = Math.Min(window.Width, work.Width), height = Math.Min(window.Height, work.Height);
        return new(Math.Clamp(window.Left, work.Left, work.Right - width),
            Math.Clamp(window.Top, work.Top, work.Bottom - height), width, height);
    }
}
