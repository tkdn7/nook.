using System.Globalization;
using Nook.App.Services;
using Nook.App.ViewModels;
namespace Nook.App.Controls;

internal static class DeskDrawing
{
    public static void Text(FrameworkElement host, DrawingContext dc, string value, Point center, double size, double width, Brush ink, bool numeric = false)
    {
        var font = (FontFamily)host.FindResource(numeric ? "NumberFont" : "BodyFont");
        var typeface = new Typeface(font, FontStyles.Normal, numeric ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, ink, VisualTreeHelper.GetDpi(host).PixelsPerDip);
        if (text.Width > width) text.SetFontSize(Math.Max(8, size * width / text.Width));
        dc.DrawText(text, new(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
    public static void Arc(DrawingContext dc, Point center, double radius, double fraction, Brush brush, double thickness, double start = -90, double sweep = 360)
    {
        if (fraction <= 0 || radius <= 0) return;
        fraction = Math.Min(.9999, fraction); var path = new StreamGeometry();
        using (var ctx = path.Open()) { double begin = start * Math.PI / 180; ctx.BeginFigure(new(center.X + Math.Cos(begin) * radius, center.Y + Math.Sin(begin) * radius), false, false); double angle = (fraction * sweep + start) * Math.PI / 180; ctx.ArcTo(new(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius), new(radius, radius), 0, fraction * sweep > 180, SweepDirection.Clockwise, true, false); }
        dc.DrawGeometry(null, new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, path);
    }
}
public sealed class ClockVisual(WidgetViewModel vm) : FrameworkElement, IDisposable
{
    private bool _attached, _disposed;
    private long _lastFrameTicks;
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;
    public double FaceRadius => vm.IsCircular ? Math.Min(ActualWidth, ActualHeight) * .465 : Math.Min(ActualWidth * .43, ActualHeight * (ActualHeight >= 110 ? .36 : .44));
    public ClockVisual(WidgetViewModel vm, bool initialize = true) : this(vm)
    {
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) => Refresh();
    }
    public void Refresh()
    {
        bool shouldAttach = !_disposed && IsLoaded && IsVisible && Motion.Enabled && vm.Model.ClockStyle == ClockStyle.Analog;
        if (shouldAttach && !_attached) { CompositionTarget.Rendering += Frame; _attached = true; }
        else if (!shouldAttach && _attached) Detach();
    }
    private void Frame(object? sender, EventArgs e)
    {
        if (!IsVisible || vm.Model.ClockStyle != ClockStyle.Analog || !Motion.Enabled) { Detach(); return; }
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (now - _lastFrameTicks >= System.Diagnostics.Stopwatch.Frequency / 30) { _lastFrameTicks = now; InvalidateVisual(); }
    }
    private void Detach() { if (_attached) { CompositionTarget.Rendering -= Frame; _attached = false; } }
    public void Dispose() { _disposed = true; Detach(); }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, side = Math.Min(w, h); if (side <= 0) return;
        var time = Now().LocalDateTime; bool circle = vm.IsCircular;
        if (vm.Model.ClockStyle == ClockStyle.Text)
        {
            string value = time.ToString(vm.IsExpanded ? "HH:mm:ss" : "HH:mm");
            DeskDrawing.Text(this, dc, value, new(w / 2, h * (circle ? .52 : .42)), Math.Clamp(side * .26, 26, 72), w * (circle ? .76 : .94), vm.Ink, true);
            if (vm.IsStandard) DeskDrawing.Text(this, dc, time.ToString("M월 d일 · dddd", CultureInfo.GetCultureInfo("ko-KR")), new(w / 2, h * (circle ? .73 : .78)), 12, w * .72, vm.MutedInk);
            return;
        }
        double cy = circle ? h / 2 : h * (h >= 110 ? .43 : .5);
        double radius = Math.Max(12, FaceRadius); var center = new Point(w / 2, cy);
        var track = vm.Ink.CloneCurrentValue(); track.Opacity = circle ? .38 : .18;
        dc.DrawEllipse(null, new Pen(track, 1), center, radius, radius);
        int count = circle ? 12 : 60;
        for (int i = 0; i < count; i++)
        {
            if (circle && i == 0 && Window.GetWindow(this)?.IsMouseOver == true) continue;
            bool major = circle || i % 5 == 0;
            double angle = (i * 360.0 / count - 90) * Math.PI / 180;
            double length = major ? Math.Clamp(radius * (circle && i % 3 == 0 ? .09 : .065), 3, 9) : 2;
            double inset = circle ? Math.Clamp(radius * .07, 8, 15) : 5;
            dc.DrawLine(new Pen(major ? vm.MutedInk : track, major ? 1.4 : .7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, At(angle, radius - inset), At(angle, radius - inset - length));
        }
        double seconds = time.Second + (Motion.Enabled ? time.Millisecond / 1000.0 : 0);
        var minuteInk = vm.Ink.CloneCurrentValue(); minuteInk.Opacity = .72;
        Hand((time.Hour % 12 + time.Minute / 60.0 + seconds / 3600) * 30, radius * .56, Math.Clamp(radius * .07, 4, 12), vm.Ink);
        Hand((time.Minute + seconds / 60) * 6, radius * .78, Math.Clamp(radius * .045, 3, 8), minuteInk);
        if (!circle && vm.IsStandard) { double angle = (seconds * 6 - 90) * Math.PI / 180; dc.DrawLine(new Pen(vm.MutedInk, 1), At(angle, -radius * .08), At(angle, radius * .82)); }
        dc.DrawEllipse(vm.Ink, null, center, Math.Clamp(radius * .018, 1.5, 3), Math.Clamp(radius * .018, 1.5, 3));
        if (vm.IsStandard && !circle) DeskDrawing.Text(this, dc, time.ToString("M월 d일 · ddd", CultureInfo.GetCultureInfo("ko-KR")), new(w / 2, h - 12), 11, w * .68, vm.MutedInk);
        Point At(double angle, double r) => new(center.X + Math.Cos(angle) * r, center.Y + Math.Sin(angle) * r);
        void Hand(double degrees, double length, double width, Brush ink)
        {
            double angle = (degrees - 90) * Math.PI / 180;
            var direction = new Vector(Math.Cos(angle), Math.Sin(angle)); var normal = new Vector(-direction.Y, direction.X);
            var root = center - direction * (radius * .055); var tip = center + direction * length; var middle = center + direction * (length * .46);
            var geometry = new StreamGeometry(); using (var path = geometry.Open())
            {
                path.BeginFigure(root, true, true);
                path.BezierTo(root + normal * (width * .35), middle + normal * (width / 2), tip, true, false);
                path.BezierTo(middle - normal * (width / 2), root - normal * (width * .35), root, true, false);
            }
            dc.DrawGeometry(ink, null, geometry);
        }
    }
}
public sealed class CountdownVisual(WidgetViewModel vm) : FrameworkElement, IDisposable
{
    private bool _attached, _disposed;
    private long _lastFrameTicks;
    public CountdownVisual(WidgetViewModel vm, bool initialize = true) : this(vm)
    {
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) => Refresh();
    }
    public void Refresh()
    {
        bool shouldAttach = !_disposed && IsLoaded && IsVisible && Motion.Enabled && vm.Model.Countdown.Deadline is not null;
        if (shouldAttach && !_attached) { CompositionTarget.Rendering += Frame; _attached = true; }
        else if (!shouldAttach && _attached) Detach();
    }
    private void Frame(object? sender, EventArgs e)
    {
        if (!IsVisible || vm.Model.Countdown.Deadline is null || !Motion.Enabled) { Detach(); return; }
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (now - _lastFrameTicks >= System.Diagnostics.Stopwatch.Frequency / 20) { _lastFrameTicks = now; InvalidateVisual(); }
    }
    private void Detach() { if (_attached) { CompositionTarget.Rendering -= Frame; _attached = false; } }
    public void Dispose() { _disposed = true; Detach(); }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, side = Math.Min(w, h); if (side <= 0) return;
        var options = vm.Model.Countdown; bool focus = vm.Model.Kind == WidgetKind.Pomodoro;
        double remaining = CountdownEngine.Remaining(options, focus, DateTimeOffset.UtcNow), duration = CountdownEngine.Duration(options, focus);
        string phase = focus ? options.Phase switch { FocusPhase.Focus => "집중", FocusPhase.ShortBreak => "짧은 휴식", _ => "긴 휴식" } : options.Finished ? "완료" : "남은 시간";
        if (vm.IsCircular)
        {
            var center = new Point(w / 2, h * .5); double radius = side * .4;
            var track = vm.MutedInk.CloneCurrentValue(); track.Opacity = .18;
            DeskDrawing.Arc(dc, center, radius, 1, track, 2.5, 150, 240); DeskDrawing.Arc(dc, center, radius, CountdownEngine.Progress(options, focus, DateTimeOffset.UtcNow), vm.Ink, 2.5, 150, 240);
            DeskDrawing.Text(this, dc, CountdownEngine.Display(remaining), new(w / 2, h * .53), Math.Clamp(side * .17, 21, 62), side * .67, vm.Ink, true);
            DeskDrawing.Text(this, dc, phase, new(w / 2, h * .67), Math.Clamp(side * .04, 10, 14), side * .6, vm.MutedInk);
        }
        else
        {
            DeskDrawing.Text(this, dc, CountdownEngine.Display(remaining), new(w / 2, Math.Max(20, h * .31)), Math.Clamp(side * .30, 25, 72), w * .9, vm.Ink, true);
            var gauge = vm.MutedInk.CloneCurrentValue(); gauge.Opacity = .18; double gy = h - 42; dc.DrawRoundedRectangle(gauge, null, new Rect(4, Math.Max(4, gy), Math.Max(1, w - 8), 3), 1.5, 1.5); double progress = CountdownEngine.Progress(options, focus, DateTimeOffset.UtcNow); if (progress > 0) dc.DrawRoundedRectangle(vm.Ink, null, new Rect(4, Math.Max(4, gy), Math.Max(1, (w - 8) * progress), 3), 1.5, 1.5);
            if (h >= 140) DeskDrawing.Text(this, dc, phase + (focus ? $" · {options.CompletedFocus}회" : ""), new(w / 2, h * .58), 12, w * .85, vm.MutedInk);
        }
    }
}
public sealed class NetworkVisual(AppViewModel app, WidgetViewModel vm) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, side = Math.Min(w, h); if (side <= 0) return;
        var sample = vm.Model.Kind == WidgetKind.Disk ? new NetworkRate(app.Metrics.Disk.Read, app.Metrics.Disk.Write, "전체 디스크") : app.Network.Current; bool circle = vm.IsCircular;
        bool vertical = circle || w < 200 && h > 120;
        foreach (bool download in new[] { true, false })
        {
            double x = vertical ? w / 2 : w * (download ? .25 : .75), y = circle ? h * (download ? .43 : .66) : vertical ? h * (download ? .23 : .67) : h * .30;
            double valueY = y + (circle ? side * .09 : Math.Min(27, h * .22));
            double available = circle ? w * .64 : vertical ? w * .9 : w * .44;
            DeskDrawing.Text(this, dc, vm.Model.Kind == WidgetKind.Disk ? download ? "READ" : "WRITE" : download ? "↓ DOWNLOAD" : "↑ UPLOAD", new(x, y), 10, available, vm.MutedInk);
            DeskDrawing.Text(this, dc, DiskCounter.Speed(download ? sample.Download : sample.Upload), new(x, valueY), Math.Clamp(side * (circle ? .085 : .18), 14, 34), available, vm.Ink, true);
        }
        if (vm.IsStandard && !vertical) DeskDrawing.Text(this, dc, sample.Name, new(w / 2, h - 12), 10, w * .9, vm.MutedInk);
    }
}
