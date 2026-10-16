using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nook.App.Controls;

public class LibraryPreview : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(WidgetKind), typeof(LibraryPreview), new PropertyMetadata(WidgetKind.Clock, OnKindChanged));

    public static readonly DependencyProperty PreviewValueProperty = DependencyProperty.Register(
        nameof(PreviewValue), typeof(string), typeof(LibraryPreview), new PropertyMetadata("", OnValueChanged));

    public WidgetKind Kind
    {
        get => (WidgetKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string PreviewValue
    {
        get => (string)GetValue(PreviewValueProperty);
        set => SetValue(PreviewValueProperty, value);
    }

    static LibraryPreview()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LibraryPreview), new FrameworkPropertyMetadata(typeof(LibraryPreview)));
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LibraryPreview preview) preview.InvalidateVisual();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LibraryPreview preview) preview.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return;

        var w = RenderSize.Width;
        var h = RenderSize.Height;
        var cx = w / 2;
        var cy = h / 2;

        {
            var accent = Foreground as SolidColorBrush ?? Brushes.Black;
            var line = (SolidColorBrush)(FindResource("LineBrush") ?? new SolidColorBrush(Colors.LightGray));
            var text = (SolidColorBrush)(FindResource("TextBrush") ?? Brushes.Black);
            var muted = (SolidColorBrush)(FindResource("MutedBrush") ?? new SolidColorBrush(Color.FromArgb(255, 112, 112, 106)));

            switch (Kind)
            {
                case WidgetKind.Clock:
                    DrawClockDial(dc, cx, cy, Math.Min(w, h) * 0.4, accent, line);
                    break;
                case WidgetKind.Cpu:
                case WidgetKind.Gpu:
                case WidgetKind.Memory:
                    DrawValueBar(dc, 8, h/2 - 4, w - 16, 8, accent, line);
                    break;
                case WidgetKind.Disk:
                case WidgetKind.Network:
                    DrawGlyph(dc, Kind == WidgetKind.Disk ? "hard_drive" : "arrow_download", 20, cy - 10, 20, accent);
                    DrawSlider(dc, 54, cy - 7, w - 68, 3, accent, line);
                    DrawSlider(dc, 54, cy + 5, (w - 68) * .64, 3, accent, line);
                    break;
                case WidgetKind.Weather:
                    DrawGlyph(dc, "weather_partly_cloudy_day", cx - 39, cy - 14, 28, accent);
                    DrawText(dc, "22°", cx + 5, cy - 12, 20, accent);
                    break;
                case WidgetKind.Battery:
                    dc.DrawRoundedRectangle(null, new Pen(accent, 1), new Rect(cx - 24, cy - 9, 46, 18), 4, 4);
                    dc.DrawRoundedRectangle(accent, null, new Rect(cx - 20, cy - 5, 29, 10), 1, 1);
                    dc.DrawRoundedRectangle(accent, null, new Rect(cx + 24, cy - 4, 3, 8), 1, 1);
                    break;
                case WidgetKind.Bluetooth:
                    DrawBluetoothIcon(dc, cx, cy, 12, accent);
                    break;
                case WidgetKind.Memo:
                    DrawMemoLines(dc, 8, 8, w - 16, h - 16, line);
                    break;
                case WidgetKind.Breathe:
                    DrawColorCircle(dc, cx, cy, Math.Min(w, h) * 0.4, Color.FromArgb(180, 120, 160, 200));
                    break;
                case WidgetKind.AirQuality:
                    DrawAQISample(dc, cx, cy, Math.Min(w, h) * 0.35, accent);
                    break;
                case WidgetKind.ProgramShelf:
                    DrawGlyph(dc, "folder", cx - 41, cy - 10, 20, accent);
                    DrawGlyph(dc, "apps", cx - 10, cy - 10, 20, accent);
                    DrawGlyph(dc, "note", cx + 21, cy - 10, 20, accent);
                    break;
                case WidgetKind.SystemShelf:
                    DrawGlyph(dc, "speaker_2", cx - 41, cy - 10, 20, accent);
                    DrawGlyph(dc, "power", cx - 10, cy - 10, 20, accent);
                    DrawGlyph(dc, "mic_off", cx + 21, cy - 10, 20, accent);
                    break;
                case WidgetKind.Media:
                    dc.DrawRoundedRectangle(line, null, new Rect(22, cy - 14, 28, 28), 4, 4);
                    DrawGlyph(dc, "music_note_2", 26, cy - 10, 20, accent);
                    DrawGlyph(dc, "previous", 72, cy - 11, 18, accent);
                    DrawGlyph(dc, "play", 98, cy - 11, 18, accent);
                    DrawGlyph(dc, "next", 124, cy - 11, 18, accent);
                    dc.DrawLine(new Pen(line, 2), new Point(72, cy + 13), new Point(142, cy + 13));
                    break;
                case WidgetKind.KeyStats:
                    DrawGlyph(dc, "keyboard", cx - 41, cy - 10, 20, accent);
                    DrawText(dc, "1,204", cx - 9, cy - 11, 18, accent);
                    break;
                case WidgetKind.Volume:
                case WidgetKind.Brightness:
                    DrawSlider(dc, 8, h/2 - 3, w - 16, 6, accent, line);
                    break;
                case WidgetKind.Timer:
                case WidgetKind.Pomodoro:
                    DrawTimerArc(dc, cx, cy, Math.Min(w, h) * 0.35, accent, line);
                    break;
                case WidgetKind.Calculator:
                    DrawCalculatorGrid(dc, 6, 8, w - 12, h - 16, line);
                    break;
                case WidgetKind.Activity:
                    for (int row = 0; row < 3; row++) for (int col = 0; col < 9; col++)
                    { var tile = accent.CloneCurrentValue(); tile.Opacity = .10 + ((row * 7 + col * 3) % 5) * .12; dc.DrawRoundedRectangle(tile, null, new Rect(cx - 44 + col * 10, cy - 14 + row * 10, 7, 7), 1.5, 1.5); }
                    break;
                case WidgetKind.ScreenTime:
                    DrawActivityBars(dc, 6, 8, w - 12, h - 16, accent);
                    break;
                case WidgetKind.MediaController:
                    DrawMediaIcon(dc, cx, cy, 16, accent);
                    break;
                case WidgetKind.Dice:
                    DrawDieIcon(dc, cx, cy, 14, accent);
                    break;
                default:
                    DrawDefaultPreview(dc, w, h, accent, line);
                    break;
            }
        }
    }

    private void DrawClockDial(DrawingContext dc, double cx, double cy, double r, SolidColorBrush accent, SolidColorBrush line)
    {
        var pen = new Pen(line, 1);
        dc.DrawEllipse(null, pen, new Point(cx, cy), r, r);

        var now = DateTime.Now;
        var hAngle = (now.Hour % 12 + now.Minute / 60.0) * 30 * Math.PI / 180;
        var mAngle = now.Minute * 6 * Math.PI / 180;

        var h1 = new Point(cx + Math.Sin(hAngle) * r * 0.5, cy - Math.Cos(hAngle) * r * 0.5);
        var m1 = new Point(cx + Math.Sin(mAngle) * r * 0.65, cy - Math.Cos(mAngle) * r * 0.65);

        dc.DrawLine(pen, new Point(cx, cy), h1);
        dc.DrawLine(new Pen(accent, 1), new Point(cx, cy), m1);
        dc.DrawEllipse(accent, null, new Point(cx, cy), 1.5, 1.5);
    }

    private void DrawValueBar(DrawingContext dc, double x, double y, double w, double h, SolidColorBrush accent, SolidColorBrush line)
    {
        var pen = new Pen(line, 0.5);
        dc.DrawRectangle(null, pen, new Rect(x, y, w, h));

        double val = 0;
        if (double.TryParse(PreviewValue.TrimEnd('%'), out var p)) val = Math.Clamp(p / 100, 0, 1);
        dc.DrawRectangle(accent, null, new Rect(x + 1, y + 1, Math.Max(0, (w - 2) * val), h - 2));
    }

    private void DrawMemoLines(DrawingContext dc, double x, double y, double w, double h, SolidColorBrush line)
    {
        var pen = new Pen(line, 0.5);
        var lineH = h / 4;
        for (int i = 0; i < 3; i++)
        {
            var ly = y + 2 + i * lineH;
            dc.DrawLine(pen, new Point(x + 2, ly), new Point(x + w - 2, ly));
        }
    }

    private void DrawColorCircle(DrawingContext dc, double cx, double cy, double r, Color color)
    {
        var brush = new RadialGradientBrush(); brush.GradientStops.Add(new(color, 0)); brush.GradientStops.Add(new(Color.FromArgb(0, color.R, color.G, color.B), 1));
        dc.DrawEllipse(brush, null, new Point(cx, cy), r * 2, r);
    }

    private void DrawAQISample(DrawingContext dc, double cx, double cy, double r, SolidColorBrush accent)
    {
        var color = Color.FromArgb(160, 100, 150, 100);
        DrawColorCircle(dc, cx, cy, r, color);
        dc.DrawEllipse(accent, null, new Point(cx - 3, cy), 1, 1); dc.DrawEllipse(accent, null, new Point(cx + 3, cy), 1, 1);
    }

    private void DrawShelfGrid(DrawingContext dc, double x, double y, double w, double h, SolidColorBrush text)
    {
        var cols = 4;
        var size = Math.Min(w / cols - 2, h / 2 - 1);
        for (int i = 0; i < 6; i++)
        {
            var ix = x + (i % cols) * (size + 2);
            var iy = y + (i / cols) * (size + 2);
            if (ix + size < x + w) dc.DrawRectangle(text, null, new Rect(ix, iy, size, size));
        }
    }

    private void DrawSlider(DrawingContext dc, double x, double y, double w, double h, SolidColorBrush accent, SolidColorBrush line)
    {
        var pen = new Pen(line, 0.5);
        dc.DrawRectangle(null, pen, new Rect(x, y, w, h));
        double val = 0.6;
        if (double.TryParse(PreviewValue, out var p)) val = Math.Clamp(p / 100, 0, 1);
        dc.DrawRectangle(accent, null, new Rect(x + 1, y + 1, Math.Max(0, (w - 2) * val), h - 2));
    }

    private void DrawTimerArc(DrawingContext dc, double cx, double cy, double r, SolidColorBrush accent, SolidColorBrush line)
    {
        var pen = new Pen(line, 1);
        dc.DrawEllipse(null, pen, new Point(cx, cy), r, r);

        var ang = 240 * Math.PI / 180;
        var start = -90 * Math.PI / 180;
        var p1 = new Point(cx + Math.Cos(start) * r, cy + Math.Sin(start) * r);
        var p2 = new Point(cx + Math.Cos(start + ang) * r, cy + Math.Sin(start + ang) * r);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p1, false, false);
            ctx.ArcTo(p2, new Size(r, r), 0, true, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, new Pen(accent, 1.5), geo);
    }

    private void DrawCalculatorGrid(DrawingContext dc, double x, double y, double w, double h, SolidColorBrush line)
    {
        var pen = new Pen(line, 0.5);
        var kw = w / 3;
        var kh = h / 2;
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 3; c++)
                dc.DrawRectangle(null, pen, new Rect(x + c * kw + 1, y + r * kh + 1, kw - 2, kh - 2));
    }

    private void DrawActivityBars(DrawingContext dc, double x, double y, double w, double h, SolidColorBrush accent)
    {
        var barW = w / 7;
        for (int i = 0; i < 7; i++)
        {
            var val = (Math.Sin(i * Math.PI / 7) + 1) / 2;
            var bx = x + i * barW;
            var by = y + (1 - val) * h;
            dc.DrawRectangle(accent, null, new Rect(bx + 1, by, barW - 2, Math.Max(1, val * h)));
        }
    }

    private void DrawBluetoothIcon(DrawingContext dc, double cx, double cy, double size, SolidColorBrush accent)
    {
        DrawGlyph(dc, "bluetooth", cx - size / 2, cy - size / 2, size, accent);
    }

    private void DrawMediaIcon(DrawingContext dc, double cx, double cy, double size, SolidColorBrush accent)
    {
        DrawGlyph(dc, "speaker_2", cx - 40, cy - 10, 20, accent);
        dc.DrawLine(new Pen(accent, 1), new Point(cx - 4, cy - 5), new Point(cx + 42, cy - 5));
        var quiet = accent.CloneCurrentValue(); quiet.Opacity = .35;
        dc.DrawLine(new Pen(quiet, 1), new Point(cx - 4, cy + 6), new Point(cx + 24, cy + 6));
    }

    private void DrawDieIcon(DrawingContext dc, double cx, double cy, double size, SolidColorBrush accent)
    {
        dc.DrawRectangle(null, new Pen(accent, 1), new Rect(cx - size/2, cy - size/2, size, size));
        var dot = size / 4;
        dc.DrawEllipse(accent, null, new Point(cx - dot, cy - dot), 1, 1);
        dc.DrawEllipse(accent, null, new Point(cx + dot, cy + dot), 1, 1);
    }

    private void DrawDefaultPreview(DrawingContext dc, double w, double h, SolidColorBrush accent, SolidColorBrush line)
    {
        var pen = new Pen(line, 1);
        dc.DrawRectangle(null, pen, new Rect(4, 4, w - 8, h - 8));
    }
    private void DrawGlyph(DrawingContext dc, string name, double x, double y, double size, Brush ink)
    {
        if (TryFindResource("Fluent." + name) is not Geometry geometry) return;
        dc.PushTransform(new TranslateTransform(x, y)); dc.PushTransform(new ScaleTransform(size / 24, size / 24));
        dc.DrawGeometry(ink, null, geometry); dc.Pop(); dc.Pop();
    }
    private void DrawText(DrawingContext dc, string value, double x, double y, double size, Brush ink)
    {
        var font = TryFindResource("NumberFont") as FontFamily ?? new FontFamily("Segoe UI");
        var text = new FormattedText(value, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), size, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(x, y));
    }
}

