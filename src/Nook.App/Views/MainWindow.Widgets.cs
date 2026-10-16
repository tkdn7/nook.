using System.ComponentModel;
using Nook.App.ViewModels;
using Nook.App.Controls;
using Nook.App.Services;
using System.Windows.Data;
using System.Windows.Media.Animation;

namespace Nook.App.Views;

public partial class MainWindow
{
    private string _libraryCategory = "전체";
    private void LibrarySearch_Changed(object sender, TextChangedEventArgs e)
    { if (FeatureLibraryHost is not null) RenderLibrary(); }
    private void BuildFeatureLibrary()
    {
        foreach (string category in new[] { "전체", "시스템", "일상", "도구", "집중", "놀이" })
        {
            var button = new Button { Content = category, Tag = category, BorderThickness = new(0), Padding = new(12, 6, 12, 6), Margin = new(0, 0, 6, 6) };
            button.Click += (_, _) => { _libraryCategory = category; RenderLibrary(); }; LibraryCategoryHost.Children.Add(button);
        }
        RenderLibrary();
    }
    private void RenderLibrary()
    {
        foreach (var button in LibraryCategoryHost.Children.OfType<Button>()) button.SetResourceReference(BackgroundProperty, Equals(button.Tag, _libraryCategory) ? "SoftAccentBrush" : "PageBrush");
        FeatureLibraryHost.Children.Clear();
        foreach (var entry in WidgetCatalog.Search(_libraryCategory, LibrarySearch.Text))
        {
            var preview = new WidgetViewModel(_vm, new() { Kind = entry.Kind });
            var grid = new Grid { Height = 100 }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 6) }; var icon = new FluentIcon { Glyph = preview.Icon, Margin = new(0, 0, 8, 0) }; icon.SetResourceReference(FluentIcon.ForegroundProperty, "TextBrush"); title.Children.Add(icon); title.Children.Add(Text(entry.Name)); grid.Children.Add(title);
            var previewControl = new LibraryPreview { Kind = entry.Kind, Width = 160, Height = 34, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            previewControl.SetResourceReference(ForegroundProperty, "TextBrush");
            string? previewBinding = entry.Kind switch { WidgetKind.Cpu => nameof(AppViewModel.CpuPreviewValue), WidgetKind.Memory => nameof(AppViewModel.MemoryPreviewValue), WidgetKind.Gpu => nameof(AppViewModel.GpuPreviewValue), _ => null };
            if (previewBinding is not null) { previewControl.SetBinding(LibraryPreview.PreviewValueProperty, new Binding(previewBinding)); }
            Grid.SetRow(previewControl, 1); grid.Children.Add(previewControl);
            var bottom = new Grid(); bottom.ColumnDefinitions.Add(new()); bottom.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var detail = Text(entry.Category, 10, true); detail.Margin = new(); detail.TextWrapping = TextWrapping.NoWrap; detail.TextTrimming = TextTrimming.CharacterEllipsis; detail.VerticalAlignment = VerticalAlignment.Center; bottom.Children.Add(detail);
            var add = ActionButton("추가", () => _vm.Add(entry.Kind)); add.Padding = new(10, 5, 10, 5); add.Margin = new(8, 0, 0, 0); Grid.SetColumn(add, 1); bottom.Children.Add(add); Grid.SetRow(bottom, 2); grid.Children.Add(bottom);
            var card = new Border { Child = grid, Padding = new(14), Margin = new(0, 0, 12, 12), Tag = entry.Kind }; card.SetResourceReference(StyleProperty, "Card"); FeatureLibraryHost.Children.Add(card);
        }
        LibraryEmpty.Visibility = FeatureLibraryHost.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void LibraryHost_SizeChanged(object sender, SizeChangedEventArgs e)
    { FeatureLibraryHost.Columns = e.NewSize.Width >= 630 ? 3 : 2; }
    private ComboBox ChoiceControl(string title, string[] choices, int selected, Action<int> change)
    {
        var row = new Grid { Margin = new(0, 16, 0, 0) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.Children.Add(Text(title));
        var combo = new ComboBox { Width = 190, ItemsSource = choices, SelectedIndex = selected }; System.Windows.Automation.AutomationProperties.SetName(combo, title);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex < 0) return; change(combo.SelectedIndex); _vm.Changed(); };
        Grid.SetColumn(combo, 1); row.Children.Add(combo); EditorHost.Children.Add(row);
        return combo;
    }
    private void ToggleControl(string title, bool selected, Action<bool> change)
    {
        var toggle = new System.Windows.Controls.Primitives.ToggleButton { Content = title, IsChecked = selected };
        System.Windows.Automation.AutomationProperties.SetName(toggle, title);
        toggle.Click += (_, _) => { change(toggle.IsChecked == true); _vm.Changed(); };
        EditorHost.Children.Add(toggle);
    }
    private void BuildFeatureSettings(WidgetViewModel widget)
    {
        var model = widget.Model;
        if (model.Kind == WidgetKind.Activity) ToggleControl("오늘 사용 시간 표시", model.ActivityShowScreenTime, value => model.ActivityShowScreenTime = value);
        if (model.Kind == WidgetKind.MediaController) ToggleControl("볼륨 슬라이더 표시", model.MediaControllerShowVolume, value => { model.MediaControllerShowVolume = value; _vm.RefreshDesktopState(); });
        if (model.Kind == WidgetKind.Breathe) ChoiceControl("색상", ["스카이", "라벤더", "민트", "피치"], (int)model.BreatheTone, i => model.BreatheTone = (BreatheTone)i);
        if (model.Kind == WidgetKind.Calculator)
        {
            var memos = _vm.Widgets.Where(w => w.Model.Kind == WidgetKind.Memo).ToArray();
            var choices = new[] { new GroupChoice(null, memos.Length == 0 ? "메모 위젯 없음" : memos.Length == 1 ? "자동 · " + memos[0].Title : "메모 선택") }
                .Concat(memos.Select(m => new GroupChoice(m.Model.Id, m.Title))).ToList();
            var target = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = model.CalculatorMemoId, MinWidth = 190, HorizontalAlignment = HorizontalAlignment.Left };
            if (model.CalculatorMemoId is null) target.SelectedIndex = 0;
            System.Windows.Automation.AutomationProperties.SetName(target, "계산 결과 메모");
            target.SelectionChanged += (_, _) => { model.CalculatorMemoId = (target.SelectedItem as GroupChoice)?.Id; _vm.Changed(); };
            EditorHost.Children.Add(Text("계산 결과를 보낼 메모", 14)); EditorHost.Children.Add(target);
        }
        if (model.Kind == WidgetKind.Activity) ChoiceControl("기간", ["일주일", "한 달", "6개월", "1년"], (int)model.ActivityPeriod, i => model.ActivityPeriod = (ActivityPeriod)i);
        if (model.Kind == WidgetKind.Clock) ChoiceControl("시계 스타일", ["텍스트", "모션"], (int)model.ClockStyle, i => model.ClockStyle = (ClockStyle)i);
        if (model.Kind == WidgetKind.Dice) ChoiceControl("주사위", ["1개", "2개", "3개"], model.DiceCount - 1, i => model.DiceCount = i + 1);
        if (model.Kind is WidgetKind.Timer or WidgetKind.Pomodoro) BuildCountdownSettings(model);
        if (model.Kind == WidgetKind.Bluetooth) EditorHost.Children.Add(ActionButton("장치 새로 고침", async () => { await _vm.Bluetooth.RefreshAsync(true); widget.RefreshMetrics(); }));
        if (widget.IsAir)
        {
            ChoiceControl("스타일", ["Casual", "Modern"], (int)model.AirQuality.Style, index => model.AirQuality.Style = (AirStyle)index);
            ChoiceControl("대기질", Enum.GetValues<AirStage>().Select(s => AirSample.For(s).Label).ToArray(), (int)model.AirQuality.Stage, index => model.AirQuality.Stage = (AirStage)index);
        }
        if (model.Kind == WidgetKind.ScreenTime)
            ChoiceControl("ETC 기준", ["1% 미만", "3% 미만", "5% 미만", "10% 미만"], Array.IndexOf(new double[] { 1, 3, 5, 10 }, model.ScreenTimeEtcThreshold), i => model.ScreenTimeEtcThreshold = new double[] { 1, 3, 5, 10 }[i]);
        if (model.Kind is WidgetKind.Volume or WidgetKind.Brightness)
        {
            var orientation = ChoiceControl("슬라이더 방향", ["자동", "가로", "세로"], (int)model.ControlOrientation, i => model.ControlOrientation = (ControlOrientation)i);
            orientation.SetBinding(IsEnabledProperty, new Binding(nameof(WidgetViewModel.IsCard)) { Source = widget });
        }
        if (model.Kind == WidgetKind.Brightness)
        {
            var displays = new ComboBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left, DisplayMemberPath = "Name", SelectedValuePath = "Id", Margin = new(0, 8, 0, 8) };
            void RefreshDisplays()
            {
                var choices = _vm.BrightnessDisplays.Select(d => new MediaSourceChoice(d.Id, d.Name)).ToList();
                if (!string.IsNullOrEmpty(model.BrightnessTarget) && choices.All(d => d.Id != model.BrightnessTarget)) choices.Add(new(model.BrightnessTarget, "연결되지 않은 디스플레이"));
                choices.Insert(0, new("", "자동 선택")); displays.ItemsSource = choices; displays.SelectedValue = model.BrightnessTarget;
            }
            RefreshDisplays(); System.Windows.Automation.AutomationProperties.SetName(displays, "밝기 디스플레이");
            displays.SelectionChanged += (_, _) => { if (displays.SelectedItem is MediaSourceChoice d && model.BrightnessTarget != d.Id) { model.BrightnessTarget = d.Id; _vm.Changed(); } };
            EditorHost.Children.Add(displays); EditorHost.Children.Add(ActionButton("디스플레이 새로 고침", async () => { await _vm.RefreshDesktopStateAsync(); if (_editingWidget == widget) RefreshDisplays(); }));
        }
        if (widget.IsMedia)
        {
            ChoiceControl("미디어 선택", ["자동", "앱 고정"], (int)model.Media.Mode, i => model.Media.Mode = (MediaMode)i);
            var source = new ComboBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left, DisplayMemberPath = "Name", SelectedValuePath = "Id", Margin = new(0, 8, 0, 8) };
            string lastSources = "";
            void RefreshSources()
            {
                if (source.IsDropDownOpen) return;
                string chosen = model.Media.PinnedApp;
                var choices = _vm.MediaService.Sessions.GroupBy(s => s.AppId).Select(g => new MediaSourceChoice(g.Key, g.First().AppName)).ToList();
                if (!string.IsNullOrEmpty(chosen) && choices.All(x => x.Id != chosen)) choices.Add(new(chosen, MediaSessionService.FriendlyName(chosen) + " · 대기 중"));
                string signature = string.Join("|", choices.Select(x => x.Id)) + ":" + chosen;
                if (signature == lastSources) return; lastSources = signature;
                choices.Insert(0, new("", "앱 선택"));
                source.ItemsSource = choices; source.SelectedValue = chosen;
            }
            _refreshMediaChoices = RefreshSources; RefreshSources(); System.Windows.Automation.AutomationProperties.SetName(source, "고정할 앱");
            source.SelectionChanged += (_, _) => { if (source.SelectedItem is MediaSourceChoice choice && model.Media.PinnedApp != choice.Id) { model.Media.PinnedApp = choice.Id; _vm.Changed(); } };
            EditorHost.Children.Add(source); EditorHost.Children.Add(ActionButton("재생 앱 새로 고침", async () => { await _vm.MediaService.RefreshAsync(); if (_editingWidget == widget) RefreshSources(); }));
            EditorHost.Children.Add(Text("우선순위", 14));
            var input = new TextBox { Text = string.Join(", ", model.Media.Priority), MaxLength = 500 }; System.Windows.Automation.AutomationProperties.SetName(input, "미디어 우선순위"); EditorHost.Children.Add(input);
            EditorHost.Children.Add(ActionButton("적용", () => { model.Media.Priority = input.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); _vm.Changed(); widget.RefreshMetrics(); }));
            _ = _vm.MediaService.RefreshAsync();
        }
        if (model.Kind == WidgetKind.SystemShelf)
        {
            EditorHost.Children.Add(Text("시스템 버튼", 14));
            foreach (var action in model.SystemButtons.Concat(Enum.GetValues<SystemAction>().Except(model.SystemButtons)).ToArray())
            {
                var row = new DockPanel();
                var up = ActionButton("위로", () => { int i = model.SystemButtons.IndexOf(action); if (i <= 0) return; model.SystemButtons.RemoveAt(i); model.SystemButtons.Insert(i - 1, action); _vm.Changed(); OpenWidgetEditor(widget); }); up.Margin = new(4);
                var down = ActionButton("아래로", () => { int i = model.SystemButtons.IndexOf(action); if (i < 0 || i + 1 >= model.SystemButtons.Count) return; model.SystemButtons.RemoveAt(i); model.SystemButtons.Insert(i + 1, action); _vm.Changed(); OpenWidgetEditor(widget); }); down.Margin = new(4);
                DockPanel.SetDock(down, Dock.Right); row.Children.Add(down); DockPanel.SetDock(up, Dock.Right); row.Children.Add(up);
                var check = new System.Windows.Controls.Primitives.ToggleButton { Content = WidgetFeaturesView.ActionName(action), IsChecked = model.SystemButtons.Contains(action), VerticalAlignment = VerticalAlignment.Center };
                check.Click += (_, _) => { if (check.IsChecked == true) { if (!model.SystemButtons.Contains(action)) model.SystemButtons.Add(action); } else model.SystemButtons.Remove(action); _vm.Changed(); }; row.Children.Add(check); EditorHost.Children.Add(row);
            }
        }
        if (model.Kind == WidgetKind.ProgramShelf)
        {
            EditorHost.Children.Add(Text("등록한 항목", 14));
            var add = new StackPanel { Orientation = Orientation.Horizontal };
            add.Children.Add(ActionButton("프로그램 / 파일 추가", () => { var d = new Microsoft.Win32.OpenFileDialog { Multiselect = true }; if (d.ShowDialog(this) == true) AddShelfPaths(widget, d.FileNames); }));
            add.Children.Add(ActionButton("폴더 추가", () => { var d = new Microsoft.Win32.OpenFolderDialog { Multiselect = true }; if (d.ShowDialog(this) == true) AddShelfPaths(widget, d.FolderNames); })); EditorHost.Children.Add(add);
            foreach (var entry in model.Shelf)
            {
                var row = new DockPanel { Margin = new(0, 8, 0, 0) };
                var name = new TextBox { Text = entry.Name, MaxLength = 80, MinWidth = 120, ToolTip = entry.Path };
                var remove = ActionButton("제거", () => { model.Shelf.Remove(entry); _vm.Changed(); OpenWidgetEditor(widget); }); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                var rename = ActionButton("적용", () => { if (string.IsNullOrWhiteSpace(name.Text)) return; entry.Name = name.Text.Trim(); _vm.Changed(); }); DockPanel.SetDock(rename, Dock.Right); row.Children.Add(rename);
                foreach (int delta in new[] { 1, -1 })
                { var move = ActionButton(delta < 0 ? "위로" : "아래로", () => { int i = model.Shelf.IndexOf(entry), next = i + delta; if (next < 0 || next >= model.Shelf.Count) return; model.Shelf.RemoveAt(i); model.Shelf.Insert(next, entry); _vm.Changed(); OpenWidgetEditor(widget); }); DockPanel.SetDock(move, Dock.Right); row.Children.Add(move); }
                row.Children.Add(name); EditorHost.Children.Add(row);
            }
        }
    }
    private void AddShelfPaths(WidgetViewModel widget, IEnumerable<string> paths)
    {
        using var editor = new WidgetFeaturesView(_vm, widget); editor.AddPaths(paths); OpenWidgetEditor(widget);
    }
    private void BuildCountdownSettings(WidgetInstance model)
    {
        var o = model.Countdown;
        var values = new List<(TextBox Input, int Min, int Max, Action<int> Apply)>();
        void Field(string name, int value, int min, int max, Action<int> change)
        {
            var row = new Grid { Margin = new(0, 12, 0, 0) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.Children.Add(Text(name));
            var input = new TextBox { Text = value.ToString(), Width = 100, MaxLength = 5 }; System.Windows.Automation.AutomationProperties.SetName(input, name); Grid.SetColumn(input, 1); row.Children.Add(input); EditorHost.Children.Add(row); values.Add((input, min, max, change));
        }
        if (model.Kind == WidgetKind.Timer) Field("시간 · 초", o.TimerSeconds, 1, 86400, v => o.TimerSeconds = v);
        else { Field("집중 · 분", o.FocusMinutes, 1, 180, v => o.FocusMinutes = v); Field("짧은 휴식 · 분", o.BreakMinutes, 1, 60, v => o.BreakMinutes = v); Field("긴 휴식 · 분", o.LongBreakMinutes, 1, 120, v => o.LongBreakMinutes = v); }
        EditorHost.Children.Add(ActionButton("적용", () =>
        {
            if (values.Any(x => !int.TryParse(x.Input.Text, out int value) || value < x.Min || value > x.Max)) { MessageBox.Show(this, "시간 범위에 맞는 숫자를 입력해 주세요.", "Nook", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            foreach (var x in values) x.Apply(int.Parse(x.Input.Text)); CountdownEngine.Reset(o); _vm.Changed();
        }));
    }
    private sealed record MediaSourceChoice(string Id, string Name);
}
