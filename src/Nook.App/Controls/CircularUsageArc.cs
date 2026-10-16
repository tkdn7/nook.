using System.Windows.Media.Animation;
using Nook.App.Services;

namespace Nook.App.Controls;

public sealed class CircularUsageArc : FrameworkElement
{
    public static readonly DependencyProperty PercentageProperty = DependencyProperty.Register(nameof(Percentage), typeof(double), typeof(CircularUsageArc), new PropertyMetadata(0.0, OnPercentage));
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(CircularUsageArc), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(CircularUsageArc), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty DisplayedProperty = DependencyProperty.Register("Displayed", typeof(double), typeof(CircularUsageArc), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Percentage { get => (double)GetValue(PercentageProperty); set => SetValue(PercentageProperty, value); }
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public double DisplayedPercentage => (double)GetValue(DisplayedProperty);
    private static void OnPercentage(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var arc = (CircularUsageArc)sender;
        double value = (double)args.NewValue; value = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
        if (Motion.Enabled && arc.IsLoaded) arc.BeginAnimation(DisplayedProperty, Motion.To(value, 360), HandoffBehavior.SnapshotAndReplace);
        else { arc.BeginAnimation(DisplayedProperty, null); arc.SetValue(DisplayedProperty, value); }
    }
    protected override void OnRender(DrawingContext context)
    {
        double side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = side * .405, thickness = Math.Clamp(side * .024, 5, 10);
        Draw(TrackBrush, 100);
        Draw(Ink, DisplayedPercentage);
        void Draw(Brush brush, double percentage)
        {
            if (percentage <= 0) return;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(At(150), false, false);
                path.ArcTo(At(150 - 120 * percentage / 100), new Size(radius, radius), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            geometry.Freeze();
            var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            context.DrawGeometry(null, pen, geometry);
        }
        Point At(double degrees) => new(center.X + Math.Cos(degrees * Math.PI / 180) * radius, center.Y + Math.Sin(degrees * Math.PI / 180) * radius);
    }
}
