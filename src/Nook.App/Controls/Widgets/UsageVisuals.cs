using System.Windows.Input;
using Nook.App.Services;
using Nook.App.ViewModels;

namespace Nook.App.Controls;

public sealed class ActivityHeatmap : FrameworkElement
{
    public IReadOnlyList<ActivityCell> Cells { get; set; } = [];
    public bool Dark { get; set; }
    public Brush Ink { get; set; } = Brushes.Black;
    public bool ShowLabels { get; set; }
    public double ViewportWidth { get; set; }
    private readonly List<(Rect Bounds, ActivityCell Cell)> _hits = [];
    private readonly ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
    public ActivityHeatmap() { ToolTip = _tip; ToolTipService.SetInitialShowDelay(this, 100); }
    public ActivityCell? CellAt(Point p) => _hits.FirstOrDefault(x => x.Bounds.Contains(p)).Cell;
    protected override void OnMouseMove(MouseEventArgs e)
    { if (CellAt(e.GetPosition(this)) is { } cell) { _tip.Content = $"{cell.Date:yyyy.MM.dd}\n{(cell.Seconds > 0 ? UsageLedger.Duration(cell.Seconds) : "기록 없음")}"; _tip.IsOpen = true; } else _tip.IsOpen = false; }
    protected override void OnMouseLeave(MouseEventArgs e) => _tip.IsOpen = false;
    protected override Size MeasureOverride(Size available)
    {
        int offset = Cells.Count <= 7 || Cells.Count == 0 ? 0 : ((int)Cells[0].Date.DayOfWeek + 6) % 7;
        int columns = Cells.Count <= 7 ? Cells.Count : (Cells.Count + offset + 6) / 7;
        double height = double.IsFinite(available.Height) ? available.Height : 100;
        double step = Math.Clamp((height - (ShowLabels ? 20 : 0)) / (Cells.Count <= 7 ? 1 : 7), 10, 24);
        // A long period keeps legible cells and scrolls; a week fits the viewport.
        double width = columns * step;
        if (Cells.Count <= 7 && ViewportWidth > 0) width = ViewportWidth;
        return new(Math.Max(1, width), Math.Max(1, height));
    }
    protected override void OnRender(DrawingContext dc)
    {
        _hits.Clear(); if (Cells.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        bool week = Cells.Count <= 7; int offset = week ? 0 : ((int)Cells[0].Date.DayOfWeek + 6) % 7;
        int rows = week ? 1 : 7, columns = week ? Cells.Count : (Cells.Count + offset + 6) / 7;
        double labelLeft = 0, labelTop = ShowLabels && !week ? 20 : 0, legend = 0;
        double maxStep = week ? 28 : 24;
        double step = Math.Clamp(Math.Min((ActualWidth - labelLeft) / Math.Max(1, columns), (ActualHeight - labelTop - legend) / rows), 1, maxStep);
        double gap = Math.Clamp(step * .18, 1, 4), side = Math.Max(1, step - gap);
        bool compress = ActualWidth < 120;
        double left = labelLeft + (columns > 15 && !compress ? 0 : Math.Max(0, (ActualWidth - labelLeft - step * columns) / 2)), top = labelTop + Math.Max(0, (ActualHeight - labelTop - legend - step * rows) / 2);
        string[] colors = Dark ? ["#353936", "#445A4C", "#56745F", "#769781", "#A0BAA7"] : ["#E5E3DC", "#D0DAD0", "#ADBFAD", "#849F88", "#597B61"];
        if (ShowLabels && !week && !compress)
        {
            int previous = -1; double lastX = -100;
            for (int i = 0; i < Cells.Count; i++)
            {
                double x = left + (i + offset) / 7 * step;
                if (Cells[i].Date.Month != previous && x - lastX >= 34) { DeskDrawing.Text(this, dc, Cells[i].Date.ToString("M월"), new(x + 10, 9), 9, 30, Ink); lastX = x; }
                previous = Cells[i].Date.Month;
            }
        }
        for (int i = 0; i < Cells.Count; i++)
        {
            int column = week ? i : (i + offset) / 7, row = week ? 0 : (i + offset) % 7;
            var rect = new Rect(left + column * step, top + row * step, side, side);
            dc.DrawRoundedRectangle(new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[Cells[i].Level])), null, rect, Math.Min(2, side * .2), Math.Min(2, side * .2));
            _hits.Add((new Rect(rect.X, rect.Y, step, step), Cells[i]));
        }
    }
}

public sealed class ActivityDaysAxis : FrameworkElement
{
    public Brush Ink { get; init; } = Brushes.Gray;
    protected override void OnRender(DrawingContext dc)
    {
        double step = Math.Clamp((ActualHeight - 20) / 7, 1, 24), gap = Math.Clamp(step * .18, 1, 4);
        double top = 20 + Math.Max(0, (ActualHeight - 20 - step * 7) / 2);
        string[] days = ["월", "화", "수", "목", "금", "토", "일"];
        foreach (int row in new[] { 0, 2, 4, 6 }) DeskDrawing.Text(this, dc, days[row], new(10, top + row * step + (step - gap) / 2), 9, 18, Ink);
    }
}

public sealed class ScreenTimeCircle(AppViewModel app, WidgetViewModel vm) : FrameworkElement
{
    private IReadOnlyList<UsageShare> _shares = [];
    private readonly ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
    private Point Center => new(ActualWidth / 2, ActualHeight * .5);
    private double Radius => Math.Min(ActualWidth, ActualHeight) * .4;
    public UsageShare? ShareAt(Point p)
    {
        if (Math.Abs((p - Center).Length - Radius) > 12) return null;
        double angle = Math.Atan2(p.Y - Center.Y, p.X - Center.X) * 180 / Math.PI; while (angle < 150) angle += 360;
        double fraction = (angle - 150) / 240, end = 0; if (fraction is < 0 or > 1) return null;
        foreach (var share in _shares) { end += share.Percentage / 100; if (fraction <= end) return share; } return null;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    { if (ShareAt(e.GetPosition(this)) is { } share) { _tip.Content = UsageTag.Build(share, vm, app.ScreenTime.Icons); foreach (var (key, color) in ThemeService.Palette(vm.IsDark)) _tip.Resources[key] = new SolidColorBrush(color); ToolTip = _tip; _tip.IsOpen = true; } else _tip.IsOpen = false; }
    protected override void OnMouseLeave(MouseEventArgs e) => _tip.IsOpen = false;
    protected override void OnRender(DrawingContext dc)
    {
        double side = Math.Min(ActualWidth, ActualHeight); if (side <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        _shares = UsageLedger.Shares(app.ScreenTime.Today, vm.Model.ScreenTimeEtcThreshold);
        var track = vm.MutedInk.CloneCurrentValue(); track.Opacity = .15;
        DeskDrawing.Arc(dc, Center, Radius, 1, track, 2.5, 150, 240);
        double start = 150;
        foreach (var share in _shares)
        { double sweep = 240 * share.Percentage / 100; DeskDrawing.Arc(dc, Center, Radius, 1, new SolidColorBrush((Color)ColorConverter.ConvertFromString(share.Color)), 2.5, start, Math.Max(.1, sweep - 1.5)); start += sweep; }
        DeskDrawing.Text(this, dc, UsageLedger.Duration(app.ScreenTime.Today.TotalSeconds), new(ActualWidth / 2, ActualHeight * .53), Math.Clamp(side * .17, 21, 62), side * .67, vm.Ink, true);
        DeskDrawing.Text(this, dc, "오늘", new(ActualWidth / 2, ActualHeight * .67), Math.Clamp(side * .04, 10, 14), side * .6, vm.MutedInk);
    }
}
