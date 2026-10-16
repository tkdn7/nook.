using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Nook.App.Services;

namespace Nook.App.Views;

public partial class MainWindow
{
    private void AddStartupSetting(StackPanel host)
    {
        var toggle = new ToggleButton { Content = "Windows 로그인 시 자동 실행", IsEnabled = _vm.CanConfigureStartup,
            ToolTip = _vm.CanConfigureStartup ? "현재 Nook 실행 위치로 등록합니다." : "별도 설정으로 실행 중입니다." };
        System.Windows.Automation.AutomationProperties.SetName(toggle, "시작 프로그램");
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(_vm.StartupEnabled)) { Source = _vm, Mode = BindingMode.TwoWay });
        host.Children.Add(toggle);
    }
    public void ShowStartupRecommendation()
    {
        if (!_vm.CanConfigureStartup || _vm.Settings.StartupPromptShown) return;
        var dialog = new Window { Owner = this, Title = "Nook 자동 실행", Width = 420, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            FontFamily = (FontFamily)FindResource("BodyFont"), Foreground = (Brush)FindResource("TextBrush") };
        foreach (var key in Resources.Keys) dialog.Resources[key] = Resources[key];
        var body = new StackPanel(); body.Children.Add(Text("로그인할 때 Nook 열기", 20));
        body.Children.Add(Text("Windows 로그인 시 위젯을 자동으로 불러올까요? 설정에서 언제든 변경할 수 있습니다.", 14, true));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) };
        var later = ActionButton("나중에", () => dialog.DialogResult = false); later.IsCancel = true; actions.Children.Add(later);
        actions.Children.Add(ActionButton("자동 실행 켜기", () => dialog.DialogResult = true, true)); body.Children.Add(actions);
        var surface = new Border { Child = body, Padding = new(24), Margin = new(8), CornerRadius = new(16), BorderThickness = new(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = .18 } };
        surface.SetResourceReference(Border.BackgroundProperty, "PageBrush"); surface.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); dialog.Content = surface;
        dialog.Loaded += (_, _) => Motion.Reveal(body, 4);
        bool selected = dialog.ShowDialog() == true;
        _vm.Settings.StartupPromptShown = true;
        if (selected) _vm.StartupEnabled = true;
        _vm.Save();
    }
}
