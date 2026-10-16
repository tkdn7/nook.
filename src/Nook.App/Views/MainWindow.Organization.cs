using Nook.App.Controls;
using Nook.App.Services;
using System.Windows.Data;

namespace Nook.App.Views;

public partial class MainWindow
{
    private Guid? _manageGroup;
    private string? _manageTag;
    private void InitializeManageFilters() { ManageFilter.ItemsSource = new[] { "전체", "그룹별", "태그별" }; ManageFilter.SelectedIndex = 0; }
    private void ManageFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (WidgetsHost is null || ManageTagsHost is null) return;
        CloseEditor(); ApplyManageFilter();
    }
    private void ApplyManageFilter()
    {
        int mode = ManageFilter.SelectedIndex;
        ManageTagsHost.Children.Clear(); ManageTagsHost.Visibility = mode > 0 ? Visibility.Visible : Visibility.Collapsed;
        GroupsHost.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        GroupEmpty.Visibility = mode == 1 && _vm.Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_manageGroup.HasValue && !_vm.Groups.Any(g => g.Id == _manageGroup)) _manageGroup = null;
        void Chip(string name, bool active, Action select)
        {
            var button = ActionButton(name, () => { CloseEditor(); select(); ApplyManageFilter(); }); button.BorderThickness = new(0); button.Padding = new(10, 5, 10, 5); button.Margin = new(0, 0, 6, 6);
            button.SetResourceReference(BackgroundProperty, active ? "SoftAccentBrush" : "PageBrush"); ManageTagsHost.Children.Add(button);
        }
        if (mode == 1)
        {
            Chip("그룹 없음", _manageGroup is null, () => _manageGroup = null);
            foreach (var group in _vm.Groups) Chip(group.Name, _manageGroup == group.Id, () => _manageGroup = group.Id);
        }
        if (mode == 2)
        {
            var tags = _vm.Widgets.Select(w => WidgetCatalog.All.First(e => e.Kind == w.Model.Kind).Category).Distinct().OrderBy(t => t).ToArray();
            if (_manageTag is not null && !tags.Contains(_manageTag, StringComparer.OrdinalIgnoreCase)) _manageTag = null;
            Chip("모든 태그", _manageTag is null, () => _manageTag = null);
            foreach (string tag in tags) Chip(tag, string.Equals(tag, _manageTag, StringComparison.OrdinalIgnoreCase), () => _manageTag = tag);
        }
        foreach (var widget in _vm.Widgets)
            if (_widgetCards.TryGetValue(widget.Model.Id, out var card))
            {
                bool visible = mode == 1 ? widget.Model.GroupId == _manageGroup : mode != 2 || _manageTag is null || WidgetCatalog.All.First(e => e.Kind == widget.Model.Kind).Category == _manageTag;
                card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (!visible && _editingWidget == widget) CloseEditor();
            }
        foreach (var (id, card) in _groupCards) card.Visibility = id == _manageGroup ? Visibility.Visible : Visibility.Collapsed;
    }
    private void BuildPresets()
    {
        PresetHost.Children.Clear();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 16) };
        var save = ActionButton("현재 배치 저장", () => ShowNameDialog("프리셋 저장", "", name => _vm.SavePreset(name)), true);
        save.Margin = new(0, 0, 8, 0);
        header.Children.Add(save);
        var undo = ActionButton("되돌리기", () =>
        {
            bool success = _vm.UndoPreset();
            if (!success && !string.IsNullOrEmpty(_vm.PresetError))
            {
                MessageBox.Show(this, _vm.PresetError, "프리셋 되돌리기", MessageBoxButton.OK);
            }
            else
            {
                BuildPresets();
            }
        });
        undo.SetBinding(IsEnabledProperty, new Binding(nameof(_vm.CanUndoPreset)) { Source = _vm });
        header.Children.Add(undo);
        PresetHost.Children.Add(header);
        if (_vm.Settings.Presets.Count == 0) { var empty = Text("저장된 프리셋이 없습니다.", 13, true); empty.Margin = new(0, 24, 0, 0); PresetHost.Children.Add(empty); }
        foreach (var preset in _vm.Settings.Presets.ToArray())
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var labels = new StackPanel();
            var title = Text(preset.Name + (preset.Id == _vm.Settings.ActivePresetId ? " · 현재" : ""), 14); title.ToolTip = $"{preset.Widgets.Count}개 위젯"; title.Margin = new(0, 0, 0, 12); labels.Children.Add(title);
            var sharePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 12) };
            var shareLabel = new TextBlock { Text = "메모 내용 공유", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            sharePanel.Children.Add(shareLabel);
            var share = new System.Windows.Controls.Primitives.ToggleButton { IsChecked = preset.ShareMemoContents, Width = 44, Height = 28, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "같은 메모 위젯의 내용을 다른 공유 프리셋과 함께 사용합니다." };
            share.SetResourceReference(StyleProperty, "SwitchToggle");
            share.Click += (_, _) => { preset.ShareMemoContents = share.IsChecked == true; _vm.Save(); };
            sharePanel.Children.Add(share);
            labels.Children.Add(sharePanel);
            row.Children.Add(labels);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var apply = ActionButton("적용", () =>
            {
                bool success = _vm.ApplyPreset(preset);
                if (!success && !string.IsNullOrEmpty(_vm.PresetError))
                {
                    MessageBox.Show(this, _vm.PresetError, "프리셋 적용", MessageBoxButton.OK);
                }
            }, true);
            apply.Margin = new(16, 0, 12, 0);
            actions.Children.Add(apply);
            actions.Children.Add(ActionButton("편집", () => ShowPresetMenu(preset)));
            Grid.SetColumn(actions, 1); row.Children.Add(actions); var card = new Border { Child = row, Margin = new(0, 12, 0, 0), Padding = new(20) }; card.SetResourceReference(StyleProperty, "Card"); PresetHost.Children.Add(card);
        }
    }
    private void ShowPresetMenu(LayoutPreset preset)
    {
        var menu = new ContextMenu();
        void Item(string title, Action action) { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("이름 변경", () => ShowNameDialog("프리셋 이름", preset.Name, name => { preset.Name = name; _vm.Changed(); }));
        Item("현재 배치로 덮어쓰기", () => { if (MessageBox.Show(this, $"‘{preset.Name}’을 현재 배치로 바꿀까요?", "프리셋", MessageBoxButton.YesNo) == MessageBoxResult.Yes) _vm.SavePreset(preset.Name, preset); });
        Item("삭제", () => { if (MessageBox.Show(this, $"‘{preset.Name}’을 삭제할까요?", "프리셋", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { _vm.Settings.Presets.Remove(preset); _vm.Changed(); } });
        menu.IsOpen = true;
    }
    private void ShowNameDialog(string title, string initial, Action<string> accepted)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 400, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            FontFamily = (FontFamily)FindResource("BodyFont"), Foreground = (Brush)FindResource("TextBrush") };
        foreach (var key in Resources.Keys) dialog.Resources[key] = Resources[key];
        var body = new StackPanel(); var heading = Text(title, 18); heading.Margin = new(0, 0, 0, 18); body.Children.Add(heading);
        var input = new TextBox { Text = initial, MaxLength = 60, Padding = new(12), VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(input, title + " 이름"); body.Children.Add(input);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0) };
        var cancel = ActionButton("취소", () => dialog.DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel);
        var confirm = ActionButton("저장", () => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; }, true); confirm.IsDefault = true; confirm.IsEnabled = !string.IsNullOrWhiteSpace(initial); input.TextChanged += (_, _) => confirm.IsEnabled = !string.IsNullOrWhiteSpace(input.Text); actions.Children.Add(confirm); body.Children.Add(actions);
        var surface = new Border { Child = body, Padding = new(24), CornerRadius = new(16), BorderThickness = new(1), Margin = new(8), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = .18 } };
        surface.SetResourceReference(Border.BackgroundProperty, "PageBrush"); surface.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); dialog.Content = surface;
        heading.MouseLeftButtonDown += (_, _) => dialog.DragMove(); dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); Motion.Reveal(body, 4); };
        if (dialog.ShowDialog() == true) accepted(input.Text.Trim());
    }
}
