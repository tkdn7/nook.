using Nook.App.Services;
using Nook.App.ViewModels;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Threading.Tasks;

namespace Nook.App.Controls;

public sealed partial class WidgetFeaturesView
{
    private void BuildShelf()
    {
        AllowDrop = true;
        // Routed drag/drop is handled at this view, never at a terminal or shell command field.
        var panel = new WrapPanel();
        foreach (var item in _vm.Model.Shelf)
        {
            var content = new StackPanel { Width = _vm.IsStandard ? 66 : 46 };
            var icon = new Grid { Width = _vm.IsExpanded ? 32 : 26, Height = _vm.IsExpanded ? 32 : 26, HorizontalAlignment = HorizontalAlignment.Center };
            var fallback = new FluentIcon { Glyph = Directory.Exists(item.Path) ? "folder" : "apps", Width = icon.Width, Height = icon.Height, Foreground = _vm.Ink };
            var image = new Image { Stretch = Stretch.Uniform }; icon.Children.Add(fallback); icon.Children.Add(image); content.Children.Add(icon);
            _ = SetShelfIcon(item.Path, image, fallback);
            if (_vm.IsStandard) { var label = Label(item.Name, 10); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new(0, 5, 0, 0); content.Children.Add(label); }
            var button = new Button { Content = content, ToolTip = item.Name + "\n" + item.Path, Padding = new(4), Margin = new(2), Foreground = _vm.Ink, BorderThickness = new(0), Background = Brushes.Transparent };
            System.Windows.Automation.AutomationProperties.SetName(button, item.Name);
            button.Click += (_, _) => Try(() => DesktopActions.Open(item.Path));
            var menu = new ContextMenu(); AddMenu(menu, "이름 변경", () => _app.OpenWidgetEditor(_vm));
            AddMenu(menu, "앞으로", () => Move(item, -1)); AddMenu(menu, "뒤로", () => Move(item, 1));
            AddMenu(menu, "제거", () => { _vm.Model.Shelf.Remove(item); _app.Changed(); }); button.ContextMenu = menu; panel.Children.Add(button);
        }
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SetResourceReference(StyleProperty, "SoftScrollViewer"); Children.Add(scroll);
        if (_vm.Model.Shelf.Count == 0)
        { var hint = Label("드래그하여 추가", 11, true); hint.IsHitTestVisible = false; hint.HorizontalAlignment = HorizontalAlignment.Center; hint.VerticalAlignment = VerticalAlignment.Center; Children.Add(hint); }
    }
    private async Task SetShelfIcon(string path, Image image, FluentIcon fallback)
    {
        var source = await AppIconStore.ForPathAsync(path);
        if (_disposed || source is null) return;
        image.Source = source; fallback.Visibility = Visibility.Collapsed;
    }
    protected override void OnDragOver(DragEventArgs e)
    { e.Effects = _vm.Model.Kind == WidgetKind.ProgramShelf && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; base.OnDragOver(e); }
    protected override void OnDrop(DragEventArgs e)
    {
        if (_vm.Model.Kind == WidgetKind.ProgramShelf && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            AddPaths(files);
        e.Handled = true; base.OnDrop(e);
    }
    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            if (_vm.Model.Shelf.Any(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            _vm.Model.Shelf.Add(new() { Path = Path.GetFullPath(path), Name = (Directory.Exists(path) ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetFileNameWithoutExtension(path)) is { Length: > 0 } name ? name : path });
        }
        _app.Changed();
    }
    private void Move(ShelfEntry item, int delta)
    { int i = _vm.Model.Shelf.IndexOf(item), next = i + delta; if (i < 0 || next < 0 || next >= _vm.Model.Shelf.Count) return; _vm.Model.Shelf.RemoveAt(i); _vm.Model.Shelf.Insert(next, item); _app.Changed(); }
    private static void AddMenu(ContextMenu menu, string title, Action action)
    { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    public void AddFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "프로그램과 바로가기|*.exe;*.lnk;*.url;*.appref-ms|모든 파일|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) AddPaths(dialog.FileNames);
    }
    public void AddFolders()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) AddPaths(dialog.FolderNames);
    }
}
