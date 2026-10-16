namespace Nook.Core;

/// <summary>Only the dragged edges attract. The unsnapped pointer path remains the resize source.</summary>
public static class MagneticResize
{
    public static ScreenRect Propose(ScreenRect origin, string edge, double dx, double dy, double scale, bool circle, double maxWidth = 640, double maxHeight = 480)
    {
        if (circle) return WidgetShapes.ResizeCircle(origin, edge, dx, dy, scale);
        double width = Math.Clamp(origin.Width + (edge.Contains('L') ? -dx : edge.Contains('R') ? dx : 0), 180 * scale, maxWidth * scale);
        double height = Math.Clamp(origin.Height + (edge.Contains('T') ? -dy : edge.Contains('B') ? dy : 0), 140 * scale, maxHeight * scale);
        return new(edge.Contains('L') ? origin.Right - width : origin.Left, edge.Contains('T') ? origin.Bottom - height : origin.Top, width, height);
    }
    public static ScreenRect Snap(ScreenRect raw, string edge, ScreenRect work, IEnumerable<ScreenRect> peers, double scale, bool circle, bool enabled, double maxWidth = 640, double maxHeight = 480)
    {
        if (!enabled) return raw;
        double threshold = MagneticPlacement.ThresholdDip * scale, gap = MagneticPlacement.GapDip * scale;
        var xs = new List<double> { work.Left, work.Right }; var ys = new List<double> { work.Top, work.Bottom };
        foreach (var p in peers.Where(p => p.Right > work.Left && p.Left < work.Right && p.Bottom > work.Top && p.Top < work.Bottom))
        {
            if (Math.Max(0, Math.Max(raw.Top - p.Bottom, p.Top - raw.Bottom)) <= gap + threshold) xs.AddRange([p.Left, p.Right, p.Left - gap, p.Right + gap]);
            if (Math.Max(0, Math.Max(raw.Left - p.Right, p.Left - raw.Right)) <= gap + threshold) ys.AddRange([p.Top, p.Bottom, p.Top - gap, p.Bottom + gap]);
        }
        double? x = edge.Contains('L') ? Nearest(raw.Left, xs) : edge.Contains('R') ? Nearest(raw.Right, xs) : null;
        double? y = edge.Contains('T') ? Nearest(raw.Top, ys) : edge.Contains('B') ? Nearest(raw.Bottom, ys) : null;
        double dw = x is { } nx ? edge.Contains('L') ? raw.Left - nx : nx - raw.Right : 0;
        double dh = y is { } ny ? edge.Contains('T') ? raw.Top - ny : ny - raw.Bottom : 0;
        if (circle)
        {
            double delta = x.HasValue && y.HasValue ? Math.Abs(dw) <= Math.Abs(dh) ? dw : dh : x.HasValue ? dw : dh;
            double side = Math.Clamp(raw.Width + delta, 180 * scale, 480 * scale);
            return new(edge.Contains('L') ? raw.Right - side : edge.Contains('R') ? raw.Left : raw.Left + (raw.Width - side) / 2,
                edge.Contains('T') ? raw.Bottom - side : edge.Contains('B') ? raw.Top : raw.Top + (raw.Height - side) / 2, side, side);
        }
        double width = Math.Clamp(raw.Width + dw, 180 * scale, maxWidth * scale), height = Math.Clamp(raw.Height + dh, 140 * scale, maxHeight * scale);
        return new(edge.Contains('L') ? raw.Right - width : raw.Left, edge.Contains('T') ? raw.Bottom - height : raw.Top, width, height);
        double? Nearest(double value, List<double> candidates) => candidates.Where(x => Math.Abs(x - value) <= threshold).OrderBy(x => Math.Abs(x - value)).Select(x => (double?)x).FirstOrDefault();
    }
}
