using Nook.App.Services;
using System.Windows.Controls.Primitives;
namespace Nook.App.Controls;
public sealed partial class WidgetFeaturesView
{
    private FrameworkElement? _deskVisual;
    private Button? _countdownPlay;
    private StackPanel? _countdownControls;
    private void BuildClock() { _deskVisual = new ClockVisual(_vm, true); Children.Add(_deskVisual); }
    private void BuildCountdown()
    {
        _deskVisual = new CountdownVisual(_vm, true); Children.Add(_deskVisual);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = _vm.IsCircular ? VerticalAlignment.Top : VerticalAlignment.Bottom, Margin = new(0, 0, 0, 2) };
        _countdownControls = controls;
        _countdownPlay = IconButton("play", "시작 / 일시정지", () => { CountdownEngine.Toggle(_vm.Model.Countdown, _vm.Model.Kind == WidgetKind.Pomodoro, DateTimeOffset.UtcNow); _app.Changed(); }); controls.Children.Add(_countdownPlay);
        controls.Children.Add(IconButton("arrow_clockwise", "초기화", () => { CountdownEngine.Reset(_vm.Model.Countdown); _app.Changed(); }));
        if (_vm.Model.Kind == WidgetKind.Pomodoro) controls.Children.Add(IconButton("next", "다음 단계", () => { CountdownEngine.Next(_vm.Model.Countdown); _vm.Model.Countdown.Finished = false; _app.Changed(); }));
        foreach (var button in controls.Children.OfType<Button>()) { button.Width = 30; button.Height = 28; button.Padding = new(4); button.BorderThickness = new(0); button.Background = Brushes.Transparent; }
        Children.Add(controls);
        PositionCountdown();
    }
    private void PositionCountdown() { if (_vm.IsCircular && _countdownControls is { } controls) controls.Margin = new(0, ActualHeight * .77, 0, 0); }
    private void BuildNetwork()
    {
        _deskVisual = new NetworkVisual(_app, _vm);
        if (!_vm.IsCircular && _vm.IsExpanded)
        {
            RowDefinitions.Add(new() { Height = new(110) }); RowDefinitions.Add(new()); Children.Add(_deskVisual);
            var chart = new DiskHistoryChart { Read = _app.Network.Download.Points, Write = _app.Network.Upload.Points, Ink = _vm.Ink, Muted = _vm.MutedInk, Margin = new(0, 10, 0, 0) }; SetRow(chart, 1); Children.Add(chart);
        }
        else Children.Add(_deskVisual);
    }
    private void BuildBluetooth()
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new());
        var header = new Grid(); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _status = Label(_app.Bluetooth.Status, 12, true); header.Children.Add(_status);
        var toggle = new ToggleButton { IsChecked = _app.Bluetooth.Enabled, IsEnabled = _app.Bluetooth.Enabled.HasValue && !_app.Bluetooth.Busy, Width = 46, Height = 28, ToolTip = "Bluetooth ON / OFF" };
        toggle.SetResourceReference(StyleProperty, "SwitchToggle"); System.Windows.Automation.AutomationProperties.SetName(toggle, "Bluetooth");
        toggle.Click += async (_, _) => { toggle.IsEnabled = false; try { await _app.Bluetooth.ToggleAsync(); } catch (Exception ex) { Try(() => throw new IOException(ex.Message)); } _signature = ""; Refresh(); };
        SetColumn(toggle, 1); header.Children.Add(toggle); Children.Add(header);
        var list = new StackPanel { Margin = new(0, 8, 0, 0) };
        foreach (var device in _app.Bluetooth.Devices)
        {
            var row = new Grid { Margin = new(0, 0, 0, 8) }; row.ColumnDefinitions.Add(new() { Width = new(26) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(new FluentIcon { Glyph = "bluetooth", Foreground = _vm.MutedInk, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center });
            var names = new StackPanel(); names.Children.Add(Label(device.Name, 12)); if (_vm.IsStandard) names.Children.Add(Label(device.Connected ? "연결됨" : "연결되지 않음", 10, true)); SetColumn(names, 1); row.Children.Add(names);
            var battery = Label(device.Battery is { } b && device.Connected ? $"{b}%" : "—", 11, true); battery.ToolTip = device.Battery is null ? "배터리 정보를 제공하지 않는 장치" : "배터리"; SetColumn(battery, 2); row.Children.Add(battery); row.ToolTip = device.Connected ? "연결됨" : "연결되지 않음"; list.Children.Add(row);
        }
        if (_app.Bluetooth.Devices.Count == 0) list.Children.Add(Label(_app.Bluetooth.Enabled == true ? "등록된 장치 없음" : "", 11, true));
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; scroll.SetResourceReference(StyleProperty, "SoftScrollViewer"); SetRow(scroll, 1); Children.Add(scroll);
    }
    private void BuildKeys()
    {
        _deskVisual = new KeyStatsVisual(_app, _vm); Children.Add(_deskVisual);
    }
    private void BuildDice()
    {
        RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = GridLength.Auto });
        var dice = new DiceVisual { Count = _vm.Model.DiceCount, Portrait = _vm.Model.Height > _vm.Model.Width * 1.1, Ink = _vm.Ink, Surface = _vm.Background }; Children.Add(dice); _deskVisual = dice;
        var button = new Button { Content = "굴리기", HorizontalAlignment = HorizontalAlignment.Center, Padding = new(18, 4, 18, 4), Margin = new(0, 2, 0, 0), ToolTip = "주사위 굴리기", Foreground = _vm.Ink, BorderThickness = new(0) };
        button.Click += (_, _) => { dice.Roll(); button.IsEnabled = !dice.Rolling; };
        dice.Finished += () => { button.IsEnabled = true; button.ToolTip = $"합계 {dice.Values.Take(dice.Count).Sum()}"; }; SetRow(button, 1); Children.Add(button);
    }
    private void UpdateDesk()
    {
        if (_deskVisual is DiceVisual dice) dice.Portrait = _vm.Model.Height > _vm.Model.Width * 1.1;
        if (_deskVisual is ClockVisual clock) clock.Refresh();
        if (_deskVisual is CountdownVisual countdown) countdown.Refresh();
        _deskVisual?.InvalidateVisual();
        if (_countdownPlay?.Content is FluentIcon icon) icon.Glyph = _vm.Model.Countdown.Deadline is null ? "play" : "pause";
        foreach (var chart in Children.OfType<DiskHistoryChart>()) chart.InvalidateVisual();
    }
}
