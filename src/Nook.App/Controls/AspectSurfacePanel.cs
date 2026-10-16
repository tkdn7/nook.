namespace Nook.App.Controls;

/// <summary>Keeps a circular surface centered and square even during native size/DPI transitions.</summary>
public sealed class AspectSurfacePanel : Panel
{
    public static readonly DependencyProperty IsSquareProperty = DependencyProperty.Register(nameof(IsSquare), typeof(bool), typeof(AspectSurfacePanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));
    public bool IsSquare { get => (bool)GetValue(IsSquareProperty); set => SetValue(IsSquareProperty, value); }
    public static Rect SurfaceBounds(Size size, bool square)
    {
        if (!square) return new Rect(size);
        double side = Math.Min(size.Width, size.Height);
        return new((size.Width - side) / 2, (size.Height - side) / 2, side, side);
    }
    protected override Size MeasureOverride(Size available)
    {
        var size = new Size(double.IsFinite(available.Width) ? available.Width : 300, double.IsFinite(available.Height) ? available.Height : 200);
        foreach (UIElement child in InternalChildren) child.Measure(SurfaceBounds(size, IsSquare).Size);
        return size;
    }
    protected override Size ArrangeOverride(Size size)
    {
        foreach (UIElement child in InternalChildren) child.Arrange(SurfaceBounds(size, IsSquare));
        return size;
    }
}
