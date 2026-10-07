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
        "title" => new(side * .22, side * .18, side * .56, 28),
        "menu" => new(side * .70, side * .17, 24, 24),
        "value" => new(side * .12, side * .34, side * .76, side * .32),
        "detail" => new(side * .14, side * .63, side * .72, 20),
        _ => new(0, 0, side, side)
    };
}

/// <summary>Eight resize targets follow the circumference; transparent window corners stay empty.</summary>
public sealed class CircularResizePanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren) child.Measure(new(14, 14));
        return new();
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double radius = Math.Max(0, Math.Min(finalSize.Width, finalSize.Height) / 2 - 10);
        foreach (FrameworkElement child in InternalChildren)
        {
            double angle = (child.Tag as string) switch { "R" => 0, "RB" => 45, "B" => 90, "LB" => 135, "L" => 180, "LT" => 225, "T" => 270, _ => 315 };
            child.Arrange(new Rect(finalSize.Width / 2 + Math.Cos(angle * Math.PI / 180) * radius - 7,
                finalSize.Height / 2 + Math.Sin(angle * Math.PI / 180) * radius - 7, 14, 14));
        }
        return finalSize;
    }
}
