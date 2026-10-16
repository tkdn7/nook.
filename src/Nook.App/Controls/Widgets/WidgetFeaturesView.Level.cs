using Nook.App.Services;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Nook.App.Controls;

public sealed partial class WidgetFeaturesView
{
    private LevelControl? _level;
    private readonly DispatcherTimer _adjustTimer = new() { Interval = TimeSpan.FromMilliseconds(75) };
    private double? _pendingLevel;
    private bool _adjusting;
    private string _levelError = "";
    private DateTimeOffset _lastAdjustment;
    private void BuildLevel()
    {
        _level = new LevelControl { Circular = _vm.IsCircular, Orientation = _vm.Model.ControlOrientation, Ink = _vm.Ink, Muted = _vm.MutedInk, AccessibilityName = _vm.Title };
        System.Windows.Automation.AutomationProperties.SetName(_level, _vm.Title + " 조절");
        _level.Adjusted += value => { _pendingLevel = value; _lastAdjustment = DateTimeOffset.UtcNow; if (!_adjustTimer.IsEnabled && !_adjusting) _adjustTimer.Start(); };
        Children.Add(_level); UpdateLevel();
    }
    private void UpdateControlOrientation() { if (_level is not null) { _level.Orientation = _vm.Model.ControlOrientation; _level.InvalidateVisual(); } }
    private void UpdateLevel()
    {
        if (_level is null) return;
        var display = BrightnessService.Choose(_app.BrightnessDisplays, _vm.Model);
        bool volume = _vm.Model.Kind is WidgetKind.Volume or WidgetKind.MediaController;
        int? value = volume ? _app.Volume : display?.Percentage;
        _level.Available = value.HasValue;
        if (!_pendingLevel.HasValue && !_adjusting && DateTimeOffset.UtcNow - _lastAdjustment > TimeSpan.FromSeconds(3)) _level.Value = value ?? 0;
        _level.Status = !string.IsNullOrEmpty(_levelError) ? _levelError : !value.HasValue ? volume ? "오디오 장치 없음" : "밝기 제어 불가"
            : volume ? _app.Muted == true ? "음소거됨" : "" : display!.Name;
        if (_outputPercentage is { } percent) percent.Text = value.HasValue ? $"{_level.Value:F0}%" : "—";
        _level.Cursor = value.HasValue ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        _level.InvalidateVisual();
    }
    private async Task ApplyLevel()
    {
        _adjustTimer.Stop();
        if (_adjusting || !_pendingLevel.HasValue || _disposed) return;
        _adjusting = true;
        try
        {
            while (_pendingLevel is { } value && !_disposed)
            {
                _pendingLevel = null;
                var display = BrightnessService.Choose(_app.BrightnessDisplays, _vm.Model);
                bool volume = _vm.Model.Kind is WidgetKind.Volume or WidgetKind.MediaController;
                await Task.Run(() => { if (volume) DesktopActions.SetVolume(value); else if (display is not null) BrightnessService.Set(display.Id, value); else throw new InvalidOperationException("밝기 제어 불가"); });
                _levelError = "";
            }
        }
        catch (Exception) { _pendingLevel = null; _levelError = "조절할 수 없습니다"; }
        finally { _adjusting = false; if (!_disposed) { _app.RefreshDesktopState(); UpdateLevel(); } }
    }
}
