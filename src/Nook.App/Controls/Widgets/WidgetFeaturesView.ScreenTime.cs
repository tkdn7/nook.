namespace Nook.App.Controls;

public sealed partial class WidgetFeaturesView
{
    private UsageBar? _usageBar;
    private StackPanel? _usageLegend;
    private string _legendSignature = "";
    private void BuildScreenTime()
    {
        _legendSignature = "";
        if (_vm.IsCircular) { _deskVisual = new ScreenTimeCircle(_app, _vm); Children.Add(_deskVisual); return; }
        RowDefinitions.Add(new() { Height = _vm.IsExpanded ? GridLength.Auto : new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        _primary = Label("0분", _vm.IsExpanded ? 40 : _vm.IsStandard ? 32 : 22);
        _primary.FontWeight = FontWeights.SemiBold; _primary.FontFamily = (FontFamily)FindResource("NumberFont"); Children.Add(_primary);
        _usageBar = new() { Empty = _vm.MutedInk, Height = _vm.IsExpanded ? 16 : 12, Margin = new(0, 8, 0, 0), TipFactory = share => UsageTag.Build(share, _vm, _app.ScreenTime.Icons) };
        if (_usageBar.ToolTip is ToolTip tooltip)
            foreach (var (key, color) in Services.ThemeService.Palette(_vm.IsDark)) tooltip.Resources[key] = new SolidColorBrush(color);
        SetRow(_usageBar, 1); Children.Add(_usageBar);
        _secondary = Label("오늘", 10, true); _secondary.Margin = new(0, 8, 0, 0); SetRow(_secondary, 2); Children.Add(_secondary);
        _usageLegend = new() { Margin = new(0, 14, 0, 0), Visibility = _vm.IsExpanded ? Visibility.Visible : Visibility.Collapsed };
        SetRow(_usageLegend, 3); Children.Add(_usageLegend);
    }
    private void UpdateScreenTime()
    {
        if (_vm.IsCircular) { _deskVisual?.InvalidateVisual(); return; }
        var day = _app.ScreenTime.Today; var shares = UsageLedger.Shares(day, _vm.Model.ScreenTimeEtcThreshold);
        _primary!.Text = UsageLedger.Duration(day.TotalSeconds); _usageBar!.Shares = shares;
        _secondary!.Text = _app.ScreenTime.Warning ?? (day.TotalSeconds <= 0 ? "오늘 · 기록 시작" : _vm.IsStandard ? $"오늘 · {day.Apps.Count}개 앱" : "오늘");
        if (!_vm.IsExpanded) return;
        // Up to four rows fit at the Big threshold. Full names and all remaining
        // shares stay available in the bar's tooltips, including very small segments.
        string signature = _app.ScreenTime.Icons.Version + ":" + string.Join("|", shares.Take(4).Select(x => $"{x.Id}:{x.Percentage:F0}:{(int)x.Seconds / 60}"));
        if (signature == _legendSignature) return; _legendSignature = signature;
        _usageLegend!.Children.Clear();
        foreach (var share in shares.Take(4))
        {
            var row = new Grid { Margin = new(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new() { Width = new(24) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var icon = UsageTag.Icon(share.Id, _app.ScreenTime.Icons, _vm.Ink, 16); icon.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(icon);
            var name = Label(share.Name, 11); SetColumn(name, 1); row.Children.Add(name);
            var percent = Label($"{share.Percentage:F0}%", 11, true); percent.Margin = new(8, 0, 0, 0); SetColumn(percent, 2); row.Children.Add(percent);
            var tooltip = new ToolTip { Content = UsageTag.Build(share, _vm, _app.ScreenTime.Icons) };
            foreach (var (key, color) in Services.ThemeService.Palette(_vm.IsDark)) tooltip.Resources[key] = new SolidColorBrush(color);
            row.ToolTip = tooltip; _usageLegend.Children.Add(row);
        }
    }
}
