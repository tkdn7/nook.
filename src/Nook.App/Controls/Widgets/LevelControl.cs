using System.Globalization;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Nook.App.Controls;

internal sealed class LevelControlAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    public LevelControlAutomationPeer(LevelControl owner) : base(owner) { }
    private LevelControl ControlOwner => (LevelControl)base.Owner;
    public double Minimum => 0;
    public double Maximum => 100;
    public double SmallChange => 1;
    public double LargeChange => 10;
    public double Value => ControlOwner.Value;
    public bool IsReadOnly => !ControlOwner.IsEnabled || !ControlOwner.Available || ControlOwner.Battery;
    public void SetValue(double value)
    {
        if (!ControlOwner.IsEnabled) throw new ElementNotEnabledException();
        if (IsReadOnly) throw new InvalidOperationException("Control is read-only");
        if (!double.IsFinite(value) || value < 0 || value > 100) throw new ArgumentException($"Value must be between 0 and 100, got {value}");
        ControlOwner.AdjustFromAutomation(value);
    }
    protected override string GetNameCore()
    {
        string? automationName = System.Windows.Automation.AutomationProperties.GetName(ControlOwner);
        if (!string.IsNullOrWhiteSpace(automationName)) return automationName;
        if (!string.IsNullOrWhiteSpace(ControlOwner.AccessibilityName)) return ControlOwner.AccessibilityName;
        return base.GetNameCore();
    }
    protected override string GetClassNameCore() => nameof(LevelControl);
    protected override AutomationControlType GetAutomationControlTypeCore() => ControlOwner.Battery ? AutomationControlType.ProgressBar : AutomationControlType.Slider;
    protected override string GetHelpTextCore() => ControlOwner.Status;
    public override object? GetPattern(PatternInterface patternInterface)
        => patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);
}

public sealed class LevelControl : FrameworkElement
{
    private double _value, _angle;
    public double Value
    {
        get => _value;
        set
        {
            if (IsMouseCaptured) return;
            SetCurrent(value, false);
        }
    }
    public bool Available { get; set; }
    public bool Circular { get; init; }
    public bool Battery { get; init; }
    public bool Compact { get; init; }
    public ControlOrientation Orientation { get; set; }
    public Brush Ink { get; init; } = Brushes.Black;
    public Brush Muted { get; init; } = Brushes.Gray;
    public string Status { get; set; } = "";
    public string AccessibilityName { get; set; } = "";
    public event Action<double>? Adjusted;
    public LevelControl() { Focusable = true; Cursor = Cursors.Hand; MinWidth = 1; MinHeight = 1; }
    protected override AutomationPeer OnCreateAutomationPeer() => new LevelControlAutomationPeer(this);
    public override string ToString() => !string.IsNullOrEmpty(AccessibilityName) ? $"{AccessibilityName}: {_value:F0}%" : base.ToString() ?? "";
    internal void AdjustFromAutomation(double value)
        => SetCurrent(value, true);
    private void SetCurrent(double value, bool userAdjustment)
    {
        if (!double.IsFinite(value)) return;
        double old = _value;
        _value = Math.Clamp(value, 0, 100);
        if (old == _value) return;
        InvalidateVisual();
        UIElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, old, _value);
        if (userAdjustment) Adjusted?.Invoke(_value);
    }
    private bool Vertical => LevelMath.Vertical(Orientation, ActualWidth, ActualHeight);
    private Point Center => new(ActualWidth / 2, ActualHeight * .5);
    private double Radius => Math.Min(ActualWidth, ActualHeight) * .4;
    private double Angle(Point point) => Math.Atan2(point.Y - Center.Y, point.X - Center.X) * 180 / Math.PI;
    public bool IsAdjustmentPoint(Point point)
    {
        if (Battery || !Available) return false;
        if (!Circular) return true;
        double distance = (point - Center).Length;
        return Math.Abs(distance - Radius) <= Math.Clamp(Math.Min(ActualWidth, ActualHeight) * .055, 10, 20);
    }
    private void SetPointer(Point point)
    {
        double value = Circular ? LevelMath.DragAngle(_value, _angle, Angle(point), 240) : Vertical
            ? Math.Clamp((ActualHeight - Math.Min(35, ActualHeight * .26) - point.Y) / Math.Max(1, ActualHeight - Math.Min(35, ActualHeight * .26) - Math.Min(58, ActualHeight * .48)), 0, 1) * 100
            : Math.Clamp((point.X - 12) / Math.Max(1, ActualWidth - 24), 0, 1) * 100;
        _angle = Angle(point); SetCurrent(Math.Round(value), true);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this);
        if (!IsAdjustmentPoint(point)) return;
        Focus(); _angle = Angle(point); CaptureMouse();
        if (!Circular) SetPointer(point);
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured) { SetPointer(e.GetPosition(this)); e.Handled = true; } }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if (IsMouseCaptured) { ReleaseMouseCapture(); e.Handled = true; } }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    { if (!Battery && Available) { SetCurrent(_value + Math.Sign(e.Delta) * 2, true); e.Handled = true; } }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Battery || !Available) return;
        double next = e.Key switch { Key.Up or Key.Right => _value + 1, Key.Down or Key.Left => _value - 1, Key.PageUp => _value + 10, Key.PageDown => _value - 10, Key.Home => 0, Key.End => 100, _ => _value };
        if (e.Key is Key.Up or Key.Right or Key.Down or Key.Left or Key.PageUp or Key.PageDown or Key.Home or Key.End) { SetCurrent(next, true); e.Handled = true; }
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight; if (w <= 0 || h <= 0) return;
        if (!Battery) dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var track = Muted.CloneCurrentValue(); track.Opacity = .22;
        var focusColor = Ink.CloneCurrentValue(); focusColor.Opacity = IsKeyboardFocused ? .4 : 0;
        if (Compact)
        {
            var start = new Point(12, h / 2); var end = new Point(Math.Max(12, w - 12), h / 2);
            var thumb = new Point(start.X + (end.X - start.X) * _value / 100, start.Y);
            dc.DrawLine(new Pen(track, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, start, end);
            if (Available) { dc.DrawLine(new Pen(Ink, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, start, thumb); dc.DrawEllipse(Ink, null, thumb, 5, 5); }
            if (IsKeyboardFocused && Available) dc.DrawRoundedRectangle(null, new Pen(focusColor, 1), new Rect(1, h / 2 - 8, Math.Max(1, w - 2), 16), 4, 4);
            return;
        }
        string value = Available ? $"{_value:F0}%" : "—";
        if (Circular)
        {
            double side = Math.Min(w, h), radius = Battery ? side * .405 : Radius;
            var center = Battery ? new Point(w / 2, h / 2) : Center;
            double start = 150, sweep = Battery ? -120 : 240;
            DrawArc(track, 100); if (Available) DrawArc(Ink, _value);
            if (!Battery && Available)
            {
                var point = At(start + sweep * _value / 100);
                double handle = Math.Clamp(side * .012, 2.5, 4);
                dc.DrawEllipse(Ink, null, point, handle, handle);
                if (IsKeyboardFocused) dc.DrawEllipse(null, new Pen(focusColor, 1.5), point, handle + 3, handle + 3);
            }
            Text(dc, value, center.X, h * (Battery ? .52 : .53), Math.Clamp(side * (Battery ? .20 : .17), Battery ? 26 : 21, Battery ? 66 : 62), Ink, true);
            Text(dc, Status, w / 2, h * (Battery ? .70 : .67), Math.Clamp(side * .04, 10, Battery ? 13 : 14), Muted);
            void DrawArc(Brush brush, double percent)
            {
                if (percent <= 0) return;
                var geometry = new StreamGeometry(); using (var path = geometry.Open())
                { path.BeginFigure(At(start), false, false); path.ArcTo(At(start + sweep * percent / 100), new(radius, radius), 0, Math.Abs(sweep * percent / 100) > 180, sweep > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false); }
                dc.DrawGeometry(null, new Pen(brush, Battery ? Math.Clamp(side * .018, 2.5, 6) : 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
            }
            Point At(double degrees, double? r = null) => new(center.X + Math.Cos(degrees * Math.PI / 180) * (r ?? radius), center.Y + Math.Sin(degrees * Math.PI / 180) * (r ?? radius));
        }
        else
        {
            Text(dc, value, w / 2, 24, Math.Clamp(Math.Min(w * .22, h * .24), 24, 48), Ink, true);
            Point from = Vertical ? new(w / 2, h - Math.Min(35, h * .26)) : new(12, Math.Max(54, h * .60));
            Point to = Vertical ? new(w / 2, Math.Min(58, h * .48)) : new(w - 12, from.Y);
            var handle = new Point(from.X + (to.X - from.X) * _value / 100, from.Y + (to.Y - from.Y) * _value / 100);
            dc.DrawLine(new Pen(track, 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, from, to);
            if (Available)
            {
                dc.DrawLine(new Pen(Ink, 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, from, handle);
                dc.DrawEllipse(Ink, null, handle, 7, 7);
                if (IsKeyboardFocused) dc.DrawEllipse(null, new Pen(focusColor, 1.5), handle, 10, 10);
            }
            Text(dc, Status, w / 2, h - 12, 10, Muted);
        }
        if (IsKeyboardFocused && Available && !Battery && !Circular)
            dc.DrawRoundedRectangle(null, new Pen(focusColor, 1.5), new Rect(1, 1, Math.Max(1, w - 2), Math.Max(1, h - 2)), 12, 12);
    }
    private void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush, bool number = false)
    {
        var family = TryFindResource(number ? "NumberFont" : "BodyFont") as FontFamily ?? new FontFamily("Segoe UI");
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyles.Normal, number ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1, ActualWidth * .78), Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(ft, new Point(x - ft.Width / 2, Math.Clamp(y - ft.Height / 2, 0, Math.Max(0, ActualHeight - ft.Height))));
    }
}
