namespace Nook.App.Controls;
public sealed partial class WidgetFeaturesView
{
    private TextBlock? _outputPercentage;
    private void BuildOutput()
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new());
        if (_vm.Model.MediaControllerShowVolume) RowDefinitions.Add(new() { Height = GridLength.Auto });
        var top = new DockPanel(); var refresh = IconButton("arrow_clockwise", "출력 장치 새로 고침", () => _ = _app.AudioOutput.RefreshAsync(true)); DockPanel.SetDock(refresh, Dock.Right); top.Children.Add(refresh); top.Children.Add(Label("음향 출력", 11, true)); Children.Add(top);
        top.Visibility = _vm.IsStandard ? Visibility.Visible : Visibility.Collapsed;
        var devices = new StackPanel { Margin = new(0, 6, 0, 0) };
        foreach (var device in _app.AudioOutput.Devices)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new(28) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new(20) });
            row.Children.Add(new FluentIcon { Glyph = "speaker_2", Foreground = _vm.Ink, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center });
            var name = Label(device.Name, _vm.IsStandard ? 12 : 11); SetColumn(name, 1); row.Children.Add(name);
            if (device.Default) { var check = new FluentIcon { Glyph = "checkmark", Foreground = _vm.Ink, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }; SetColumn(check, 2); row.Children.Add(check); }
            var button = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(8), Margin = new(0, 0, 0, 4), BorderThickness = new(0), IsEnabled = !_app.AudioOutput.Busy, ToolTip = device.Name };
            var background = _vm.MutedInk.CloneCurrentValue(); background.Opacity = device.Default ? .12 : .02; button.Background = background;
            System.Windows.Automation.AutomationProperties.SetName(button, "출력 · " + device.Name);
            button.Click += async (_, _) => { await _app.AudioOutput.SelectAsync(device.Id); _app.RefreshDesktopState(); }; devices.Children.Add(button);
        }
        if (_app.AudioOutput.Error is { } error) devices.Children.Add(Label(error, 11, true));
        else if (_app.AudioOutput.Devices.Count == 0) devices.Children.Add(Label("연결된 출력 장치 없음", 11, true));
        var scroll = new ScrollViewer { Content = devices, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; scroll.SetResourceReference(StyleProperty, "SoftScrollViewer"); SetRow(scroll, 1); Children.Add(scroll);
        if (_vm.Model.MediaControllerShowVolume)
        {
            var row = new Grid { Margin = new(0, 6, 0, 0), Height = 28 }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new(38) });
            _level = new LevelControl { Compact = true, Ink = _vm.Ink, Muted = _vm.MutedInk, Orientation = ControlOrientation.Horizontal, Value = _app.Volume ?? 0 };
            System.Windows.Automation.AutomationProperties.SetName(_level, "출력 볼륨");
            _level.Adjusted += value => { _pendingLevel = value; _lastAdjustment = DateTimeOffset.UtcNow; if (_outputPercentage is { } label) label.Text = $"{value:F0}%"; if (!_adjustTimer.IsEnabled && !_adjusting) _adjustTimer.Start(); };
            row.Children.Add(_level); _outputPercentage = Label("", 10, true); _outputPercentage.HorizontalAlignment = HorizontalAlignment.Right; SetColumn(_outputPercentage, 1); row.Children.Add(_outputPercentage);
            SetRow(row, 2); Children.Add(row); UpdateLevel();
        }
    }
}
