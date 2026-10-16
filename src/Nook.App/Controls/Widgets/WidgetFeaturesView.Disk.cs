using Nook.App.Services;

namespace Nook.App.Controls;

public sealed partial class WidgetFeaturesView
{
    private Grid? _diskValues;
    private TextBlock? _diskRead, _diskWrite;
    private DiskHistoryChart? _diskChart;
    private void BuildDisk()
    {
        if (_vm.IsCircular) { _deskVisual = new NetworkVisual(_app, _vm); Children.Add(_deskVisual); return; }
        RowDefinitions.Add(new() { Height = _vm.IsExpanded ? GridLength.Auto : new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        _diskValues = new Grid();
        foreach (string direction in new[] { "READ", "WRITE" })
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(Label(direction, 10, direction == "WRITE"));
            var value = Label("—", _vm.IsExpanded ? 32 : _vm.IsStandard ? 24 : 15);
            value.FontFamily = (FontFamily)FindResource("NumberFont"); value.FontWeight = FontWeights.SemiBold; value.Margin = new(0, 3, 0, 0);
            if (direction == "READ") _diskRead = value; else _diskWrite = value;
            stack.Children.Add(value); _diskValues.Children.Add(stack);
        }
        Children.Add(_diskValues);
        _status = Label("전체 디스크", 10, true); _status.Margin = new(0, 8, 0, 0);
        _status.Visibility = _vm.IsStandard ? Visibility.Visible : Visibility.Collapsed; SetRow(_status, 1); Children.Add(_status);
        _diskChart = new DiskHistoryChart { Read = _app.Metrics.DiskReadHistory.Points, Write = _app.Metrics.DiskWriteHistory.Points, Ink = _vm.Ink, Muted = _vm.MutedInk, Margin = new(0, 14, 0, 0), Visibility = _vm.IsExpanded ? Visibility.Visible : Visibility.Collapsed };
        SetRow(_diskChart, 2); Children.Add(_diskChart);
        SizeChanged -= DiskSizeChanged; SizeChanged += DiskSizeChanged;
        DiskSizeChanged(this, null!);
    }
    private void DiskSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_diskValues is null || _vm.Model.Kind != WidgetKind.Disk) return;
        bool vertical = ActualWidth < 210 && ActualHeight > 170;
        _diskValues.RowDefinitions.Clear(); _diskValues.ColumnDefinitions.Clear();
        for (int i = 0; i < 2; i++)
        {
            if (vertical) _diskValues.RowDefinitions.Add(new()); else _diskValues.ColumnDefinitions.Add(new());
            var child = (FrameworkElement)_diskValues.Children[i]; SetRow(child, vertical ? i : 0); SetColumn(child, vertical ? 0 : i);
            child.Margin = vertical ? new(0, 4, 0, 4) : new(i == 0 ? 0 : 6, 0, i == 0 ? 6 : 0, 0);
        }
        FitDiskValues();
    }
    private void UpdateDisk()
    {
        if (_vm.IsCircular) { _deskVisual?.InvalidateVisual(); return; }
        var sample = _app.Metrics.Disk;
        _diskRead!.Text = sample.Warming ? "…" : DiskCounter.Speed(sample.Read);
        _diskWrite!.Text = sample.Warming ? "…" : DiskCounter.Speed(sample.Write);
        FitDiskValues();
        _status!.Text = sample.Warming ? "측정 중" : sample.Read is null ? "디스크 측정 불가" : _vm.IsExpanded ? "전체 디스크 · 최근 60초" : "전체 디스크";
        _diskChart?.InvalidateVisual();
    }
    private void FitDiskValues()
    {
        double width = ActualWidth > 0 ? ActualWidth : Math.Max(1, _vm.Model.Width - 46);
        bool vertical = width < 210 && ActualHeight > 170;
        double available = Math.Max(1, vertical ? width : width / 2 - 12);
        double maximum = _vm.IsExpanded ? 32 : _vm.IsStandard ? 24 : 15;
        foreach (var label in new[] { _diskRead, _diskWrite })
        {
            if (label is null) continue;
            var text = new FormattedText(label.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(label.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), maximum, _vm.Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            label.FontSize = Math.Max(10, maximum * Math.Min(1, available / Math.Max(1, text.WidthIncludingTrailingWhitespace)));
        }
    }
}

public sealed class DiskHistoryChart : FrameworkElement
{
    public IReadOnlyList<MetricPoint> Read { get; init; } = [];
    public IReadOnlyList<MetricPoint> Write { get; init; } = [];
    public Brush Ink { get; init; } = Brushes.Black;
    public Brush Muted { get; init; } = Brushes.Gray;
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight; if (w <= 0 || h <= 0) return;
        var grid = Muted.CloneCurrentValue(); grid.Opacity = .18;
        for (int i = 0; i < 3; i++) dc.DrawLine(new Pen(grid, .7), new(0, 2 + (h - 4) * i / 2), new(w, 2 + (h - 4) * i / 2));
        double maximum = Math.Max(1024, Read.Concat(Write).Select(x => x.Value ?? 0).DefaultIfEmpty().Max());
        var end = Read.Concat(Write).Max(x => (DateTimeOffset?)x.Time);
        if (end is null) return;
        Draw(Read, new Pen(Ink, 2)); Draw(Write, new Pen(Muted, 1.5) { DashStyle = DashStyles.Dash });
        void Draw(IReadOnlyList<MetricPoint> points, Pen pen)
        {
            Point? previous = null;
            foreach (var p in points)
            {
                if (p.Value is not { } value) { previous = null; continue; }
                var point = new Point(w * Math.Clamp(1 - (end.Value - p.Time).TotalSeconds / 60, 0, 1), 2 + (h - 4) * (1 - Math.Clamp(value / maximum, 0, 1)));
                if (previous is { } from) dc.DrawLine(pen, from, point); else dc.DrawEllipse(pen.Brush, null, point, 1.5, 1.5);
                previous = point;
            }
        }
    }
}
