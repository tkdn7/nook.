using Nook.App.ViewModels;
using Nook.App.Services;
using System.Windows.Media.Animation;

namespace Nook.App.Controls;

public sealed class AirQualityGlow : FrameworkElement, IDisposable
{
    private readonly WidgetViewModel _vm;
    private AirStage? _stage;
    private static readonly DependencyProperty ColorProperty = DependencyProperty.Register("GlowColor", typeof(Color), typeof(AirQualityGlow), new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public AirQualityGlow(WidgetViewModel vm)
    {
        _vm = vm; IsHitTestVisible = false; vm.FeatureUpdated += Refresh; Refresh();
    }
    private void Refresh()
    {
        Visibility = _vm.IsAir && _vm.Model.AirQuality.Style == AirStyle.Casual ? Visibility.Visible : Visibility.Collapsed;
        var stage = _vm.Model.AirQuality.Stage;
        if (_stage != stage)
        {
            var color = (Color)ColorConverter.ConvertFromString(AirSample.For(stage).Color);
            if (_stage.HasValue && IsLoaded && Motion.Enabled) BeginAnimation(ColorProperty, new ColorAnimation(color, Motion.Duration(420)));
            else { BeginAnimation(ColorProperty, null); SetValue(ColorProperty, color); }
            _stage = stage;
        }
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var c = (Color)GetValue(ColorProperty);
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.PushClip(_vm.IsCircular ? new EllipseGeometry(rect) : new RectangleGeometry(rect, _vm.Radius.TopLeft, _vm.Radius.TopLeft));
        // A physical, circular halo stays round in wide and tall cards. Smoothstep
        // gives both the center and the transparent edge a zero slope.
        double shortSide = Math.Min(ActualWidth, ActualHeight);
        var center = new Point(ActualWidth / 2, _vm.IsCircular ? ActualHeight * .479 : ActualHeight * .36 + 16);
        double radius = shortSide * .69;
        var glow = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute,
            Center = center, GradientOrigin = center, RadiusX = radius, RadiusY = radius,
            ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation };
        for (int i = 0; i <= 32; i++)
        {
            double t = i / 32.0, fade = 1 - t * t * (3 - 2 * t);
            glow.GradientStops.Add(new(Color.FromArgb((byte)Math.Round(76 * fade * fade), c.R, c.G, c.B), t));
        }
        dc.DrawRectangle(glow, null, rect); dc.Pop();
    }
    public void Dispose() => _vm.FeatureUpdated -= Refresh;
}
