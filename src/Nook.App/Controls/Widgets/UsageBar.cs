using System.Windows.Input;
using System.Windows.Automation;

namespace Nook.App.Controls;

/// <summary>One proportional bar; segment hit testing uses the same cumulative fractions as drawing.</summary>
public sealed class UsageBar : FrameworkElement
{
    private IReadOnlyList<UsageShare> _shares = [];
    public IReadOnlyList<UsageShare> Shares { get => _shares; set { _shares = value; InvalidateVisual(); AutomationProperties.SetName(this, value.Count == 0 ? "사용 기록 없음" : string.Join(", ", value.Select(x => $"{x.Name} {x.Percentage:F1}%"))); if (_tooltip.IsOpen && value.FirstOrDefault(x => x.Id == _hovered) is { } hovered) SetTip(hovered); } }
    public Func<UsageShare, FrameworkElement>? TipFactory { get; init; }
    public Brush Empty { get; init; } = Brushes.LightGray;
    private string? _hovered;
    private readonly ToolTip _tooltip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
    public UsageBar() { Height = 14; ToolTip = _tooltip; ToolTipService.SetInitialShowDelay(this, 150); ToolTipService.SetShowDuration(this, 15000); }
    public UsageShare? ShareAt(double x)
    {
        if (ActualWidth <= 0 || x < 0 || x >= ActualWidth) return null;
        double end = 0; foreach (var share in _shares) { end += share.Percentage / 100 * ActualWidth; if (x < end) return share; }
        return _shares.LastOrDefault();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var share = ShareAt(e.GetPosition(this).X);
        if (share?.Id == _hovered) return;
        _hovered = share?.Id;
        SetTip(share);
    }
    public void SetTip(UsageShare? share) => _tooltip.Content = share is null ? "사용 기록 없음" : TipFactory is { } factory ? factory(share) : $"{share.Name} · {share.Percentage:F1}% · {UsageLedger.Duration(share.Seconds)}";
    protected override void OnMouseLeave(MouseEventArgs e) { _hovered = null; _tooltip.IsOpen = false; }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight; if (w <= 0 || h <= 0) return;
        var rect = new Rect(0, 0, w, h); dc.PushClip(new RectangleGeometry(rect, h / 2, h / 2));
        var empty = Empty.CloneCurrentValue(); empty.Opacity = .16; dc.DrawRectangle(empty, null, rect);
        double x = 0;
        for (int i = 0; i < _shares.Count; i++)
        {
            var share = _shares[i]; double width = i == _shares.Count - 1 ? w - x : w * share.Percentage / 100;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(share.Color));
            dc.DrawRectangle(brush, null, new Rect(x, 0, Math.Max(0, width), h)); x += width;
        }
        dc.Pop();
    }
}
