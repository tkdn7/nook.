namespace Nook.Core;

/// <summary>Physical screen rectangles; spacing and attraction distances scale from DIP.</summary>
public static class MagneticPlacement
{
    public const double GapDip = 16;
    public const double ThresholdDip = 6;

    public static ScreenRect Snap(ScreenRect moving, ScreenRect work, IEnumerable<ScreenRect> peers,
        double scale, bool enabled = true)
    {
        if (!enabled) return moving;
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        double threshold = ThresholdDip * scale, gap = GapDip * scale;
        var xs = new List<double> { work.Left, work.Right - moving.Width };
        var ys = new List<double> { work.Top, work.Bottom - moving.Height };
        var nearby = peers.Where(p => p.Right > work.Left && p.Left < work.Right && p.Bottom > work.Top && p.Top < work.Bottom).ToList();
        foreach (var peer in nearby)
        {
            // Other monitors and distant rows/columns must not attract the dragged widget.
            if (peer.Right <= work.Left || peer.Left >= work.Right || peer.Bottom <= work.Top || peer.Top >= work.Bottom) continue;
            if (IntervalDistance(moving.Top, moving.Bottom, peer.Top, peer.Bottom) <= gap + threshold)
                xs.AddRange([peer.Left, peer.Right - moving.Width, peer.Right + gap, peer.Left - moving.Width - gap]);
            if (IntervalDistance(moving.Left, moving.Right, peer.Left, peer.Right) <= gap + threshold)
                ys.AddRange([peer.Top, peer.Bottom - moving.Height, peer.Bottom + gap, peer.Top - moving.Height - gap]);
        }
        // Between two cards, also attract to the position with equal opposing gaps.
        foreach (var a in nearby)
        foreach (var b in nearby)
        {
            if (a.Right <= moving.Left + threshold && b.Left >= moving.Right - threshold
                && IntervalDistance(moving.Top, moving.Bottom, a.Top, a.Bottom) == 0
                && IntervalDistance(moving.Top, moving.Bottom, b.Top, b.Bottom) == 0
                && b.Left - a.Right >= moving.Width)
                xs.Add((a.Right + b.Left - moving.Width) / 2);
            if (a.Bottom <= moving.Top + threshold && b.Top >= moving.Bottom - threshold
                && IntervalDistance(moving.Left, moving.Right, a.Left, a.Right) == 0
                && IntervalDistance(moving.Left, moving.Right, b.Left, b.Right) == 0
                && b.Top - a.Bottom >= moving.Height)
                ys.Add((a.Bottom + b.Top - moving.Height) / 2);
        }
        // Empty space is free movement. No lattice, no lock, no size changes.
        double x = Nearest(moving.Left, xs, threshold) ?? moving.Left;
        double y = Nearest(moving.Top, ys, threshold) ?? moving.Top;
        return moving with { Left = x, Top = y };
    }

    private static double IntervalDistance(double a0, double a1, double b0, double b1) => Math.Max(0, Math.Max(a0 - b1, b0 - a1));
    private static double? Nearest(double value, List<double> candidates, double threshold)
    {
        double? best = null;
        double distance = threshold + .001;
        foreach (double candidate in candidates)
        {
            double next = Math.Abs(candidate - value);
            if (next <= threshold && next < distance) { best = candidate; distance = next; }
        }
        return best;
    }
}

/// <summary>Tracks the pointer's unsnapped path, never the previous corrected window position.</summary>
public sealed class MagneticDragSession
{
    private ScreenRect _origin;
    private double _pointerX, _pointerY;
    public bool IsActive { get; private set; }
    public void Begin(ScreenRect origin, double pointerX, double pointerY)
    { _origin = origin; _pointerX = pointerX; _pointerY = pointerY; IsActive = true; }
    public void End() => IsActive = false;
    public ScreenRect Proposed(double pointerX, double pointerY) => _origin with
    { Left = _origin.Left + pointerX - _pointerX, Top = _origin.Top + pointerY - _pointerY };
}
