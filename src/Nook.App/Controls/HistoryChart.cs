namespace Nook.App.Controls;

public sealed class HistoryChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(IReadOnlyList<MetricPoint>), typeof(HistoryChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(HistoryChart), new FrameworkPropertyMetadata(Brushes.RoyalBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(HistoryChart), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<MetricPoint>? Points { get => (IReadOnlyList<MetricPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        var gridPen = new Pen(GridBrush, .6);
        for (int i = 0; i <= 2; i++) dc.DrawLine(gridPen, new(0, i * (height - 2) / 2 + 1), new(width, i * (height - 2) / 2 + 1));
        if (Points is not { Count: > 0 } points) { dc.Pop(); return; }
        DateTimeOffset end = points[^1].Time;
        var pen = new Pen(Accent, 2) { LineJoin = PenLineJoin.Round };
        Point? last = null;
        foreach (var p in points)
        {
            if (p.Value is not { } value) { last = null; continue; }
            var point = new Point(width * Math.Clamp(1 - (end - p.Time).TotalSeconds / 60, 0, 1),
                (height - 4) * (1 - Math.Clamp(value, 0, 100) / 100) + 2);
            if (last is { } previous) dc.DrawLine(pen, previous, point);
            else dc.DrawEllipse(Accent, null, point, 1.5, 1.5);
            last = point;
        }
        dc.Pop();
    }
}
