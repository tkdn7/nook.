using System.ComponentModel;
using Nook.App.ViewModels;
using Nook.App.Controls;
using Nook.App.Services;
using System.Windows.Data;
using System.Windows.Media.Animation;

namespace Nook.App.Views;

public partial class MainWindow : Window
{
    private readonly AppViewModel _vm;
    private readonly List<(TextBlock Label, Func<string> Text)> _sourceLabels = [];
    private WidgetViewModel? _editingWidget;
    private WidgetGroup? _editingGroup;
    private ComboBox? _groupCombo;
    private bool _updatingGroups;
    private StackPanel EditorHost = new();
    private ExpandableCard? _activeCard;
    private readonly Dictionary<Guid, ExpandableCard> _widgetCards = [];
    private readonly Dictionary<Guid, ExpandableCard> _groupCards = [];
    private readonly Dictionary<Guid, (TextBlock Name, TextBlock Count)> _groupLabels = [];
    private string _page = "library";
    private int _visibilityRevision;
    public bool Exiting { get; set; }
    public MainWindow(AppViewModel vm)
    {
        _vm = vm; InitializeComponent(); DataContext = vm;
        Closing += OnClosing;
        vm.WidgetEditorRequested += OpenWidgetEditor;
        vm.SettingsChanged += OnSettingsChanged;
        BuildGlobalSettings(); RenderManageCards(); SelectPage("library");
        Loaded += (_, _) => Motion.Reveal((FrameworkElement)Content, 6);
        Closed += (_, _) => { vm.WidgetEditorRequested -= OpenWidgetEditor; vm.SettingsChanged -= OnSettingsChanged; };
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        int revision = ++_visibilityRevision;
        if (Exiting) return;
        e.Cancel = true;
        if (!Motion.Enabled) { Hide(); return; }
        var fade = Motion.To(0, 140);
        fade.Completed += (_, _) =>
        { if (revision != _visibilityRevision || Exiting) return; Hide(); BeginAnimation(OpacityProperty, null); Opacity = 1; };
        BeginAnimation(OpacityProperty, fade);
    }
    public void BringForward()
    { _visibilityRevision++; BeginAnimation(OpacityProperty, null); Opacity = 1; bool hidden = !IsVisible; Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); if (hidden) Motion.Reveal((FrameworkElement)Content, 6); }
    private void Library_Click(object sender, RoutedEventArgs e) => SelectPage("library");
    private void Manage_Click(object sender, RoutedEventArgs e) => SelectPage("manage");
    private void Settings_Click(object sender, RoutedEventArgs e) => SelectPage("settings");
    private void SelectPage(string page)
    {
        _page = page;
        LibraryPage.Visibility = page == "library" ? Visibility.Visible : Visibility.Collapsed;
        ManagePage.Visibility = page == "manage" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (nav, name) in new[] { (LibraryNav, "library"), (ManageNav, "manage"), (SettingsNav, "settings") })
            nav.SetResourceReference(BackgroundProperty, page == name ? "SoftAccentBrush" : "SidebarBrush");
        PageTitle.Text = page switch { "manage" => "내 위젯", "settings" => "설정", _ => "위젯 라이브러리" };
        CloseEditor();
        Motion.Reveal(page switch { "manage" => ManagePage, "settings" => SettingsPage, _ => LibraryPage });
    }
    private void OnSettingsChanged()
    {
        EmptyWidgets.Visibility = _vm.HasWidgets ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (label, text) in _sourceLabels) label.Text = text();
        if (_editingWidget is { } w && !_vm.Widgets.Contains(w) || _editingGroup is { } g && !_vm.Groups.Contains(g)) CloseEditor();
        if (_editingWidget is { } editing && _groupCombo is { } selector)
        {
            var choices = GroupChoices();
            if (selector.ItemsSource is not List<GroupChoice> existing || !existing.SequenceEqual(choices))
            {
                _updatingGroups = true;
                selector.ItemsSource = choices;
                selector.SelectedIndex = Math.Max(0, choices.FindIndex(c => c.Id == editing.Model.GroupId));
                _updatingGroups = false;
            }
        }
        RenderManageCards();
    }
    private static TextBlock Text(string text, double size = 14, bool muted = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
        if (muted) label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return label;
    }
    private static Button ActionButton(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label };
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        button.Click += (_, _) => action(); return button;
    }
    private ComboBox LevelControl(StackPanel host, string title, Level? value, bool inherit, Action<Level?> change)
    {
        var row = new Grid { Margin = new(0, 16, 0, 0) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = Text(title); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
        var combo = new ComboBox { Width = 190, Margin = new(0), ItemsSource = inherit ? new[] { "상위 설정 따르기", "Low", "Mid", "High" } : new[] { "Low", "Mid", "High" } };
        combo.SelectedIndex = value is { } v ? (int)v + (inherit ? 1 : 0) : 0;
        System.Windows.Automation.AutomationProperties.SetName(combo, title);
        combo.SelectionChanged += (_, _) =>
        { change(inherit && combo.SelectedIndex == 0 ? null : (Level)(combo.SelectedIndex - (inherit ? 1 : 0))); _vm.Changed(); };
        Grid.SetColumn(combo, 1); row.Children.Add(combo); host.Children.Add(row); return combo;
    }
    private void BuildGlobalSettings()
    {
        var host = GlobalSettingsHost;
        host.Children.Clear(); host.Children.Add(Text("전체 기본값", 18));
        LevelControl(host, "모서리", _vm.Settings.Radius, false, v => _vm.Settings.Radius = v!.Value);
        LevelControl(host, "배경 불투명도", _vm.Settings.Opacity, false, v => _vm.Settings.Opacity = v!.Value);
        var themeRow = new Grid { Margin = new(0, 20, 0, 12) };
        themeRow.ColumnDefinitions.Add(new()); themeRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        themeRow.Children.Add(Text("테마"));
        var theme = new ComboBox { Width = 190, ItemsSource = new[] { "시스템 설정 따르기", "라이트", "다크" }, SelectedIndex = (int)_vm.Settings.Theme };
        System.Windows.Automation.AutomationProperties.SetName(theme, "테마");
        theme.SelectionChanged += (_, _) => { _vm.Settings.Theme = (ThemeMode)theme.SelectedIndex; _vm.Changed(); };
        Grid.SetColumn(theme, 1); themeRow.Children.Add(theme); host.Children.Add(themeRow);
        var top = new CheckBox { Content = "모든 위젯을 항상 위에 표시", IsChecked = _vm.Settings.AlwaysOnTop };
        top.Click += (_, _) => { _vm.Settings.AlwaysOnTop = top.IsChecked == true; _vm.Changed(); }; host.Children.Add(top);
        var reset = ActionButton("모든 위젯이 전체 설정 따르기", () =>
        {
            if (MessageBox.Show(this, "그룹과 위젯의 Radius·Opacity와 개별 테마 지정값을 모두 지울까요? 위치와 그룹은 유지됩니다.", "전체 외형 초기화", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            AppearanceResolver.ResetAll(_vm.Settings); _vm.Changed();
        });
        reset.HorizontalAlignment = HorizontalAlignment.Left; reset.Margin = new(0, 20, 0, 0); host.Children.Add(reset);
        var exit = ActionButton("Nook 종료", () => Application.Current.Shutdown()); exit.Margin = new(0, 12, 0, 0); host.Children.Add(exit);
    }
    private void CreateGroup_Click(object sender, RoutedEventArgs e)
    {
        var group = _vm.CreateGroup(GroupNameInput.Text); GroupNameInput.Text = ""; OpenGroupEditor(group);
    }
    private static TextBlock BoundText(string property, double size, bool muted = false)
    {
        var text = Text("", size, muted); text.SetBinding(TextBlock.TextProperty, new Binding(property)); text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; return text;
    }
    private void RenderManageCards()
    {
        SyncRemoved(_widgetCards, WidgetsHost, _vm.Widgets.Select(w => w.Model.Id));
        foreach (var widget in _vm.Widgets)
        {
            if (_widgetCards.ContainsKey(widget.Model.Id)) continue;
            var row = new Grid { DataContext = widget };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(44) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var icon = new FluentIcon { Glyph = widget.Icon, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            icon.SetResourceReference(FluentIcon.ForegroundProperty, "TextBrush"); row.Children.Add(icon);
            var labels = new StackPanel(); labels.Children.Add(BoundText(nameof(widget.Title), 16)); labels.Children.Add(BoundText(nameof(widget.StateLabel), 12, true)); Grid.SetColumn(labels, 1); row.Children.Add(labels);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(ActionButton("설정", () => { if (_editingWidget == widget) CloseEditor(); else OpenWidgetEditor(widget); }));
            var visibility = new Button { Command = widget.ToggleVisibilityCommand }; visibility.SetBinding(ContentProperty, new Binding(nameof(widget.VisibilityLabel))); buttons.Children.Add(visibility);
            buttons.Children.Add(new Button { Content = "삭제", Command = widget.DeleteCommand, Margin = new(0) }); Grid.SetColumn(buttons, 2); row.Children.Add(buttons);
            var card = new ExpandableCard(row); _widgetCards.Add(widget.Model.Id, card); WidgetsHost.Children.Add(card);
            Motion.Reveal(card, 4);
        }
        SyncRemoved(_groupCards, GroupsHost, _vm.Groups.Select(g => g.Id));
        foreach (var id in _groupLabels.Keys.Where(id => !_groupCards.ContainsKey(id)).ToArray()) _groupLabels.Remove(id);
        foreach (var group in _vm.Groups)
        {
            if (_groupCards.ContainsKey(group.Id))
            {
                var existing = _groupLabels[group.Id]; existing.Name.Text = group.Name; existing.Count.Text = GroupCount(group); continue;
            }
            var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var label = new StackPanel(); var name = Text(group.Name, 15); var count = Text(GroupCount(group), 11, true); label.Children.Add(name); label.Children.Add(count); row.Children.Add(label);
            _groupLabels[group.Id] = (name, count);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(ActionButton("그룹 설정", () => { if (_editingGroup == group) CloseEditor(); else OpenGroupEditor(group); }));
            buttons.Children.Add(ActionButton("그룹 삭제", () => _vm.DeleteGroup(group)));
            Grid.SetColumn(buttons, 1); row.Children.Add(buttons);
            var card = new ExpandableCard(row); _groupCards.Add(group.Id, card); GroupsHost.Children.Add(card);
        }
        GroupEmpty.Visibility = _vm.Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyWidgets.Visibility = _vm.HasWidgets ? Visibility.Collapsed : Visibility.Visible;
    }
    private string GroupCount(WidgetGroup group) => $"{_vm.Widgets.Count(w => w.Model.GroupId == group.Id)}개 위젯";
    private static void SyncRemoved(Dictionary<Guid, ExpandableCard> cards, Panel host, IEnumerable<Guid> existing)
    { var ids = existing.ToHashSet(); foreach (var id in cards.Keys.Where(id => !ids.Contains(id)).ToArray()) { var card = cards[id]; cards.Remove(id); card.Dismiss(() => host.Children.Remove(card)); } }
    private void CloseEditor()
    { _activeCard?.Collapse(); _activeCard = null; _sourceLabels.Clear(); _editingWidget = null; _editingGroup = null; _groupCombo = null; EditorHost = new(); }
    private void StartEditor(string title, ExpandableCard card)
    {
        if (_activeCard != card) CloseEditor();
        _sourceLabels.Clear(); _editingWidget = null; _editingGroup = null; _groupCombo = null; _activeCard = card; EditorHost = new();
        var head = new DockPanel(); var close = ActionButton("닫기", CloseEditor); DockPanel.SetDock(close, Dock.Right); head.Children.Add(close); head.Children.Add(Text(title, 18)); EditorHost.Children.Add(head);
    }
    private void AddSource(Func<string> text)
    { var label = Text(text(), 11, true); label.Margin = new(0, 4, 0, 0); EditorHost.Children.Add(label); _sourceLabels.Add((label, text)); }
    public void OpenWidgetEditor(WidgetViewModel widget)
    {
        BringForward(); if (_page != "manage") SelectPage("manage");
        RenderManageCards(); StartEditor($"{widget.Title} · 개별 설정", _widgetCards[widget.Model.Id]); _editingWidget = widget;
        if (widget.IsMetric)
        {
            var shapeRow = new Grid { Margin = new(0, 16, 0, 0) }; shapeRow.ColumnDefinitions.Add(new()); shapeRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); shapeRow.Children.Add(Text("모양"));
            var shape = new ComboBox { Width = 190, ItemsSource = new[] { "카드", "원형" }, SelectedIndex = widget.IsCircular ? 1 : 0 };
            System.Windows.Automation.AutomationProperties.SetName(shape, "모양");
            shape.SelectionChanged += (_, _) => { widget.Model.Shape = shape.SelectedIndex == 1 ? WidgetShape.Circle : WidgetShape.Card; _vm.Changed(); };
            Grid.SetColumn(shape, 1); shapeRow.Children.Add(shape); EditorHost.Children.Add(shapeRow);
        }
        var radius = LevelControl(EditorHost, "모서리", widget.Model.Appearance.Radius, true, v => widget.Model.Appearance.Radius = v);
        radius.SetBinding(IsEnabledProperty, new Binding(nameof(widget.IsCard)) { Source = widget });
        AddSource(() => widget.IsCircular ? "현재 적용: 원형" : "현재 적용: " + widget.RadiusSource);
        LevelControl(EditorHost, "배경 불투명도", widget.Model.Appearance.Opacity, true, v => widget.Model.Appearance.Opacity = v);
        AddSource(() => "현재 적용: " + widget.OpacitySource);
        var themeRow = new Grid { Margin = new(0, 16, 0, 12) }; themeRow.ColumnDefinitions.Add(new()); themeRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); themeRow.Children.Add(Text("위젯 테마"));
        var theme = new ComboBox { Width = 190, ItemsSource = new[] { "전체 테마 따르기", "Windows 설정", "라이트", "다크" }, SelectedIndex = widget.Model.Theme is { } mode ? (int)mode + 1 : 0 };
        System.Windows.Automation.AutomationProperties.SetName(theme, "위젯 테마");
        theme.SelectionChanged += (_, _) => { widget.Model.Theme = theme.SelectedIndex == 0 ? null : (ThemeMode)(theme.SelectedIndex - 1); _vm.Changed(); };
        Grid.SetColumn(theme, 1); themeRow.Children.Add(theme); EditorHost.Children.Add(themeRow); AddSource(() => "현재 적용: " + widget.ThemeSource);
        EditorHost.Children.Add(Text("소속 그룹", 14));
        var groups = GroupChoices();
        var combo = new ComboBox { ItemsSource = groups, DisplayMemberPath = nameof(GroupChoice.Name), SelectedValuePath = nameof(GroupChoice.Id), SelectedValue = widget.Model.GroupId, Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        if (widget.Model.GroupId is null) combo.SelectedIndex = 0;
        System.Windows.Automation.AutomationProperties.SetName(combo, "소속 그룹");
        _groupCombo = combo;
        combo.SelectionChanged += (_, _) => { if (_updatingGroups) return; widget.Model.GroupId = (combo.SelectedItem as GroupChoice)?.Id; _vm.Changed(); };
        EditorHost.Children.Add(combo);
        var locked = new CheckBox { Content = "위치·크기 잠금", IsChecked = widget.Model.Locked };
        locked.Click += (_, _) => { widget.Model.Locked = locked.IsChecked == true; _vm.Changed(); }; EditorHost.Children.Add(locked);
        EditorHost.Children.Add(ActionButton("상위 설정으로 초기화", () => { widget.Model.Appearance = new(); widget.Model.Theme = null; _vm.Changed(); OpenWidgetEditor(widget); }));
        _activeCard!.Expand(EditorHost);
    }
    public void OpenGroupEditor(WidgetGroup group)
    {
        if (_page != "manage") SelectPage("manage");
        RenderManageCards(); StartEditor($"{group.Name} · 그룹 설정", _groupCards[group.Id]); _editingGroup = group;
        var nameRow = new DockPanel { Margin = new(0, 12, 0, 8) };
        var input = new TextBox { Text = group.Name, MaxLength = 60 };
        var rename = ActionButton("이름 변경", () => { if (!string.IsNullOrWhiteSpace(input.Text)) { group.Name = input.Text.Trim(); _vm.Changed(); OpenGroupEditor(group); } });
        DockPanel.SetDock(rename, Dock.Right); nameRow.Children.Add(rename); nameRow.Children.Add(input); EditorHost.Children.Add(nameRow);
        LevelControl(EditorHost, "모서리", group.Appearance.Radius, true, v => group.Appearance.Radius = v);
        AddSource(() => Describe(AppearanceResolver.ResolveProperty(null, group.Appearance.Radius, _vm.Settings.Radius, group.Name)));
        LevelControl(EditorHost, "배경 불투명도", group.Appearance.Opacity, true, v => group.Appearance.Opacity = v);
        AddSource(() => Describe(AppearanceResolver.ResolveProperty(null, group.Appearance.Opacity, _vm.Settings.Opacity, group.Name)));
        EditorHost.Children.Add(ActionButton("상위 설정으로 초기화", () => { group.Appearance = new(); _vm.Changed(); OpenGroupEditor(group); }));
        _activeCard!.Expand(EditorHost);
    }
    private sealed record GroupChoice(Guid? Id, string Name)
    { public override string ToString() => Name; }
    private List<GroupChoice> GroupChoices() => new[] { new GroupChoice(null, "그룹 없음") }.Concat(_vm.Groups.Select(g => new GroupChoice(g.Id, g.Name))).ToList();
    private static string Describe(ResolvedProperty property) => $"현재 적용: {property.Value} · {property.Source}";
}
