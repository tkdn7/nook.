namespace Nook.App.Controls;

/// <summary>Positions content relative to the circular surface without scaling text or controls.</summary>
public sealed class CircularLayoutPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double side = double.IsFinite(availableSize.Width) && double.IsFinite(availableSize.Height)
            ? Math.Min(availableSize.Width, availableSize.Height) : 288;
        foreach (FrameworkElement child in InternalChildren) child.Measure(Bounds(child, side).Size);
        return new(side, side);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double side = Math.Min(finalSize.Width, finalSize.Height);
        foreach (FrameworkElement child in InternalChildren) child.Arrange(Bounds(child, side));
        return finalSize;
    }
    private static Rect Bounds(FrameworkElement child, double side) => (child.Tag as string) switch
    {
        "title" => new(side * .25, Math.Max(40, side * .28), side * .50, 18),
        "menu" => new((side - 20) / 2, Math.Max(18, side * .04), 20, 20),
        "value" => new(side * .12, Math.Max(64, side * .34), side * .76, side * .32),
        "detail" => new(side * .14, side * .63, side * .72, 20),
        _ => new(0, 0, side, side)
    };
}

/// <summary>Eight resize targets follow the circumference; transparent window corners stay empty.</summary>
public sealed class CircularResizePanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren) child.Measure(availableSize);
        return new();
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double outer = Math.Max(1, Math.Min(finalSize.Width, finalSize.Height) / 2 - 2), inner = Math.Max(0, outer - 14);
        var center = new Point(finalSize.Width / 2, finalSize.Height / 2);
        foreach (FrameworkElement child in InternalChildren)
        {
            double angle = (child.Tag as string) switch { "R" => 0, "RB" => 45, "B" => 90, "LB" => 135, "L" => 180, "LT" => 225, "T" => 270, _ => 315 };
            Point At(double degrees, double radius) => new(center.X + Math.Cos(degrees * Math.PI / 180) * radius, center.Y + Math.Sin(degrees * Math.PI / 180) * radius);
            var band = new StreamGeometry(); using (var path = band.Open())
            { path.BeginFigure(At(angle - 22.5, outer), true, true); path.ArcTo(At(angle + 22.5, outer), new(outer, outer), 0, false, SweepDirection.Clockwise, true, false); path.LineTo(At(angle + 22.5, inner), true, false); path.ArcTo(At(angle - 22.5, inner), new(inner, inner), 0, false, SweepDirection.Counterclockwise, true, false); }
            band.Freeze(); child.Clip = band; child.Arrange(new Rect(finalSize));
        }
        return finalSize;
    }
}
