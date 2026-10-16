using Nook.App.Services;
using Nook.App.ViewModels;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Threading.Tasks;

namespace Nook.App.Controls;

public sealed partial class WidgetFeaturesView
{
    private void BuildBattery()
    {
        if (_vm.IsCircular) { _level = new LevelControl { Circular = true, Battery = true, IsHitTestVisible = false, Ink = _vm.Ink, Muted = _vm.MutedInk }; Children.Add(_level); return; }
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        _primary = Label("—", _vm.IsExpanded ? 62 : _vm.IsStandard ? 42 : 28); _primary.FontWeight = FontWeights.SemiBold; _primary.FontFamily = (FontFamily)FindResource("NumberFont"); Children.Add(_primary);
        _secondary = Label("", 11, true); SetRow(_secondary, 1); Children.Add(_secondary);
        var bottom = new StackPanel { Margin = new(0, 7, 0, 0), Visibility = _vm.IsStandard ? Visibility.Visible : Visibility.Collapsed };
        _progress = new SmoothProgressBar { Maximum = 100, Height = 5, Foreground = _vm.Ink, Background = _vm.MutedInk }; bottom.Children.Add(_progress);
        _status = Label("", 11, true); _status.Margin = new(0, 8, 0, 0); _status.Visibility = _vm.IsExpanded ? Visibility.Visible : Visibility.Collapsed; bottom.Children.Add(_status); SetRow(bottom, 2); Children.Add(bottom);
    }
    private void UpdateBattery()
    {
        var b = _app.Battery;
        if (_level is not null) { _level.Available = b.Percentage.HasValue; _level.Value = b.Percentage ?? 0; _level.Status = !b.Present ? "배터리 없음" : b.Charging ? "충전 중" : b.Plugged ? "전원 연결됨" : ""; _level.InvalidateVisual(); return; }
        if (_primary is null) return;
        _primary.Text = b.Present ? b.Percentage is { } p ? $"{p}%" : "—" : "—";
        _secondary!.Text = !b.Present ? "배터리 없음" : b.Charging ? "충전 중" : b.Plugged ? "전원 연결됨" : b.Percentage.HasValue ? "배터리 사용 중" : "배터리 정보 없음";
        _progress!.TargetValue = b.Percentage ?? 0;
        _status!.Text = !b.Plugged && b.SecondsRemaining is { } seconds ? $"약 {seconds / 3600}시간 {seconds % 3600 / 60}분 남음" : "";
    }
}
