using System.Diagnostics;
using Nook.App.Services;
using Nook.App.ViewModels;

namespace Nook.App.Controls;

public sealed class BreatheVisual : FrameworkElement, IDisposable
{
    private readonly WidgetViewModel _vm;
    private readonly Stopwatch _watch = new();
    private bool _attached, _disposed;
    private TimeSpan _lastFrame;
    public double? PreviewSeconds { get; set; }
    public bool Animating => _attached;
    public double Expansion => Motion.Enabled ? BreatheMotion.Expansion(PreviewSeconds ?? _watch.Elapsed.TotalSeconds) : .5;
    public BreatheVisual(WidgetViewModel vm)
    {
        _vm = vm; IsHitTestVisible = false;
        Loaded += (_, _) => Sync(); Unloaded += (_, _) => Detach(); IsVisibleChanged += (_, _) => Sync();
        vm.FeatureUpdated += Refresh;
    }
    private void Refresh() { Sync(); InvalidateVisual(); }
    private void Sync()
    {
        if (_disposed) return;
        if (IsLoaded && IsVisible && Motion.Enabled && !PreviewSeconds.HasValue)
        { if (!_attached) { _watch.Start(); _attached = true; CompositionTarget.Rendering += Frame; } }
        else Detach();
    }
    private void Frame(object? sender, EventArgs e)
    {
        if (!Motion.Enabled || !IsVisible) { Detach(); InvalidateVisual(); return; }
        if (_watch.Elapsed - _lastFrame < TimeSpan.FromMilliseconds(32)) return;
        _lastFrame = _watch.Elapsed; InvalidateVisual();
    }
    private void Detach() { if (_attached) CompositionTarget.Rendering -= Frame; _attached = false; _watch.Stop(); }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, side = Math.Min(w, h);
        if (side <= 0) return;
        var rect = new Rect(RenderSize);
        dc.PushClip(_vm.IsCircular ? new EllipseGeometry(rect) : new RectangleGeometry(rect, _vm.Radius.TopLeft, _vm.Radius.TopLeft));
        var color = (Color)ColorConverter.ConvertFromString(BreatheMotion.Color(_vm.Model.BreatheTone));
        double expansion = Expansion, radius = side * (.34 + .28 * expansion);
        var center = new Point(w / 2, h / 2);
        var glow = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = center, GradientOrigin = center,
            RadiusX = radius, RadiusY = radius, ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation };
        for (int i = 0; i <= 32; i++)
        {
            double t = i / 32.0, fade = 1 - t * t * (3 - 2 * t);
            glow.GradientStops.Add(new(Color.FromArgb((byte)Math.Round((110 + 60 * expansion) * fade), color.R, color.G, color.B), t));
        }
        dc.DrawRectangle(glow, null, rect); dc.Pop();
    }
    public void Dispose() { _disposed = true; Detach(); _vm.FeatureUpdated -= Refresh; }
}
