namespace Nook.App.Controls;
public sealed partial class WidgetFeaturesView
{
    private ActivityHeatmap? _heatmap;
    private TextBlock? _activitySummary;
    private ActivityScrollViewer? _scroll;
    private void BuildActivity()
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { Margin = new(0, 2, 0, 6) }; header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _primary = Label(UsageLedger.Duration(_app.ScreenTime.Today.TotalSeconds), _vm.IsExpanded ? 30 : _vm.IsStandard ? 24 : 20); _primary.FontFamily = (FontFamily)FindResource("NumberFont"); header.Children.Add(_primary);
        _primary.Visibility = _vm.Model.ActivityShowScreenTime ? Visibility.Visible : Visibility.Collapsed;
        if (!_vm.IsStandard && !_vm.Model.ActivityShowScreenTime) header.Visibility = Visibility.Collapsed;
        Children.Add(header);
        header.SizeChanged += (_, _) =>
        {
            if (_primary is { } value) value.FontSize = Math.Clamp(header.ActualWidth / 9, 14, _vm.IsExpanded ? 30 : _vm.IsStandard ? 24 : 20);
        };
        _heatmap = new() { Dark = _vm.IsDark, Ink = _vm.MutedInk, ShowLabels = _vm.IsExpanded }; foreach (var (key, color) in Services.ThemeService.Palette(_vm.IsDark)) _heatmap.Resources[key] = new SolidColorBrush(color);
        _scroll = new ActivityScrollViewer(_vm.Model) { Content = _heatmap };
        var scroll = _scroll;
        _scroll.SetResourceReference(StyleProperty, "SoftScrollViewer");
        var map = new Grid(); map.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); map.ColumnDefinitions.Add(new());
        if (_vm.IsExpanded && _vm.Model.ActivityPeriod != ActivityPeriod.Week)
        {
            var axis = new ActivityDaysAxis { Width = 24, Ink = _vm.MutedInk, VerticalAlignment = VerticalAlignment.Top };
            axis.SetBinding(HeightProperty, new System.Windows.Data.Binding(nameof(ActualHeight)) { Source = _heatmap }); map.Children.Add(axis);
        }
        SetColumn(_scroll, 1); map.Children.Add(_scroll); SetRow(map, 1); Children.Add(map);
        scroll.SizeChanged += (_, _) =>
        { if (_heatmap is { } heat) { heat.ViewportWidth = Math.Max(1, scroll.ActualWidth - 2); heat.MinWidth = heat.ViewportWidth; heat.InvalidateMeasure(); } };
        var footer = new Grid { Margin = new(0, 4, 0, 0) };
        footer.RowDefinitions.Add(new() { Height = GridLength.Auto }); footer.RowDefinitions.Add(new() { Height = GridLength.Auto });
        footer.ColumnDefinitions.Add(new());
        for (int i = 0; i < 3; i++) footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        SetRow(footer, 2); Children.Add(footer);
        _activitySummary = Label("", 10, true); _activitySummary.Margin = new(0);
        _activitySummary.TextWrapping = TextWrapping.NoWrap; _activitySummary.TextTrimming = TextTrimming.CharacterEllipsis;
        _activitySummary.VerticalAlignment = VerticalAlignment.Center;
        _activitySummary.Visibility = _vm.IsStandard ? Visibility.Visible : Visibility.Collapsed;
        footer.Children.Add(_activitySummary);
        if (_vm.IsExpanded)
        {
            var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 4, 0, 0) };
            var less = Label("적음", 9, true); less.Margin = new(0); legend.Children.Add(less);
            string[] colors = _vm.IsDark ? ["#353936", "#445A4C", "#56745F", "#769781", "#A0BAA7"] : ["#E5E3DC", "#D0DAD0", "#ADBFAD", "#849F88", "#597B61"];
            foreach (string color in colors) legend.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new(2), Margin = new(3, 0, 0, 0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) });
            var label = Label("많음", 9, true); label.Margin = new(6, 0, 0, 0); legend.Children.Add(label);
            SetRow(legend, 1); SetColumnSpan(legend, 4); footer.Children.Add(legend);
        }
        Button ScrollHint(bool left)
        {
            var button = new Button { Content = left ? "‹" : "›", Width = 22, MinWidth = 0, MinHeight = 0, Height = 24, Margin = new(0),
                Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(0), FontSize = 14, Foreground = _vm.Ink };
            button.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(left ? nameof(ActivityScrollViewer.CanScrollLeft) : nameof(ActivityScrollViewer.CanScrollRight))
                { Source = scroll, Converter = (System.Windows.Data.IValueConverter)FindResource("BoolVisibility") });
            System.Windows.Automation.AutomationProperties.SetName(button, left ? "이전 기록" : "최근 기록");
            button.Click += (_, _) => scroll.ScrollBy(left ? -40 : 40);
            return button;
        }
        var leftScroll = ScrollHint(true); SetColumn(leftScroll, 1); footer.Children.Add(leftScroll);
        var periodLabel = Label(_vm.IsStandard ? (_vm.Model.ActivityPeriod switch { ActivityPeriod.Week => "7일", ActivityPeriod.Month => "한 달", ActivityPeriod.Year => "1년", ActivityPeriod.HalfYear => "6개월", _ => "7일" }) : "7일", 10, true);
        periodLabel.Margin = new(0);
        var periodButton = new Button { Content = periodLabel, Background = Brushes.Transparent, BorderThickness = new(0), MinHeight = 0, Height = 24, Margin = new(0), Padding = new(6, 0, 6, 0), Foreground = _vm.Ink };
        System.Windows.Automation.AutomationProperties.SetName(periodButton, "ActivityPeriodShortcut");
        periodButton.Click += (_, _) => _app.OpenWidgetEditor(_vm);
        SetColumn(periodButton, 2); footer.Children.Add(periodButton);
        var rightScroll = ScrollHint(false); SetColumn(rightScroll, 3); footer.Children.Add(rightScroll);
    }
    private void UpdateExtras()
    {
        if (_vm.Model.Kind == WidgetKind.Activity && _heatmap is not null)
        {
            var cells = ActivityHistory.Cells(_app.ScreenTime.VisibleDays, DateOnly.FromDateTime(DateTime.Now), _vm.IsStandard ? _vm.Model.ActivityPeriod : ActivityPeriod.Week); _heatmap.Cells = cells; _heatmap.InvalidateMeasure(); _heatmap.InvalidateVisual();
            _primary!.Text = UsageLedger.Duration(_app.ScreenTime.Today.TotalSeconds);
            _activitySummary!.Text = _vm.IsStandard ? $"{cells.Count(x => x.Seconds > 0)}일 · {UsageLedger.Duration(cells.Sum(x => x.Seconds))}" : _vm.Model.ActivityShowScreenTime ? "오늘 · 최근 7일" : "최근 7일";
            _activitySummary.ToolTip = _activitySummary.Text;
        }
    }
    private void DisposeExtras() { _heatmap = null; _activitySummary = null; _scroll = null; }
}
