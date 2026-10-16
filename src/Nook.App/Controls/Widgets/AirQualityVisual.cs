using System.Globalization;
using System.Windows.Media.Animation;
using Nook.App.Services;

namespace Nook.App.Controls;

/// <summary>Vector face and a low-opacity radial glow; no network or raster assets.</summary>
public sealed class AirQualityVisual : FrameworkElement
{
    private readonly WidgetInstance _model;
    private Brush _ink = Brushes.Black, _muted = Brushes.Gray;
    private AirStage? _previousStage;
    private static readonly DependencyProperty GlowProperty = DependencyProperty.Register("Glow", typeof(Color), typeof(AirQualityVisual), new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty MouthProperty = DependencyProperty.Register("Mouth", typeof(double), typeof(AirQualityVisual), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public AirQualityVisual(WidgetInstance model) { _model = model; IsHitTestVisible = false; }
    public void Refresh(Brush ink, Brush muted)
    {
        _ink = ink; _muted = muted;
        var stage = _model.AirQuality.Stage;
        if (_previousStage != stage)
        {
            var color = (Color)ColorConverter.ConvertFromString(AirSample.For(stage).Color);
            double mouth = stage switch { AirStage.Good => .28, AirStage.Moderate => .15, AirStage.Sensitive => 0, AirStage.Unhealthy => -.12, AirStage.VeryUnhealthy or AirStage.Hazardous => -.22, _ => 0 };
            if (_previousStage.HasValue && Motion.Enabled && IsLoaded)
            {
                BeginAnimation(GlowProperty, new ColorAnimation(color, Motion.Duration(420)));
                BeginAnimation(MouthProperty, Motion.To(mouth, 420));
            }
            else { BeginAnimation(GlowProperty, null); SetValue(GlowProperty, color); BeginAnimation(MouthProperty, null); SetValue(MouthProperty, mouth); }
            _previousStage = stage;
        }
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        bool circle = _model.Shape == WidgetShape.Circle;
        var sample = AirSample.For(_model.AirQuality.Stage);
        double top = circle ? h * .27 : 0;
        double usable = circle ? h * .58 : h;
        dc.PushClip(circle ? new EllipseGeometry(new Rect(0, 0, w, h)) : new RectangleGeometry(new Rect(0, 0, w, h), 8, 8));
        if (_model.AirQuality.Style == AirStyle.Casual)
        {
            double r = Math.Clamp(Math.Min(w * .3, usable * .43), 20, 98);
            var center = new Point(w / 2, top + usable * .36);
            var pen = new Pen(_ink, Math.Clamp(r * .11, 2.5, 6)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            foreach (double x in new[] { -.28, .28 })
                dc.DrawLine(pen, new(center.X + r * x, center.Y - r * .17), new(center.X + r * x, center.Y + r * .02));
            var mouth = new StreamGeometry();
            using (var context = mouth.Open())
            {
                context.BeginFigure(new(center.X - r * .23, center.Y + r * .27), false, false);
                context.QuadraticBezierTo(new(center.X, center.Y + r * (.27 + (double)GetValue(MouthProperty))), new(center.X + r * .23, center.Y + r * .27), true, false);
            }
            dc.DrawGeometry(null, pen, mouth);
            Label(dc, sample.Label, w / 2, top + usable * .70, Math.Clamp(usable * .12, 12, 20), _ink, true);
            if (sample.Aqi is { } a && usable >= 110) Label(dc, $"AQI {a}", w / 2, top + usable * .89, 11, _muted);
        }
        else
        {
            Label(dc, sample.Aqi?.ToString(CultureInfo.InvariantCulture) ?? "—", w / 2, top + usable * .25, Math.Clamp(usable * .38, 28, 72), _ink, true);
            Label(dc, sample.Label, w / 2, top + usable * .60, Math.Clamp(usable * .12, 11, 18), _ink);
            if (usable >= 110)
            {
                double barW = Math.Min(w * .68, 220), x = (w - barW) / 2, y = top + usable * .78;
                for (int i = 0; i < 6; i++)
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AirSample.For((AirStage)i).Color));
                    brush.Opacity = i == (int)_model.AirQuality.Stage ? .95 : .3;
                    dc.DrawRoundedRectangle(brush, null, new(x + i * barW / 6, y, barW / 6 - 3, 5), 2.5, 2.5);
                }
            }
            Label(dc, "AQI", w / 2, top + usable * .93, 10, _muted);
        }
        dc.Pop();
    }
    private void Label(DrawingContext dc, string text, double x, double y, double size, Brush brush, bool bold = false)
    {
        var family = TryFindResource("BodyFont") as FontFamily ?? new FontFamily("Segoe UI");
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        formatted.MaxTextWidth = Math.Max(1, ActualWidth - 16); formatted.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(formatted, new Point(Math.Max(8, x - formatted.Width / 2), Math.Clamp(y - formatted.Height / 2, 0, Math.Max(0, ActualHeight - formatted.Height))));
    }
}
