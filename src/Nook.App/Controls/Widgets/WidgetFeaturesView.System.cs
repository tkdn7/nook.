using Nook.App.Services;
using Nook.App.ViewModels;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Threading.Tasks;

namespace Nook.App.Controls;

public sealed partial class WidgetFeaturesView
{
    private void BuildSystem()
    {
        var panel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in _vm.Model.SystemButtons)
        {
            var stack = new StackPanel { Width = _vm.IsStandard ? 70 : 38 };
            stack.Children.Add(new FluentIcon { Glyph = Glyph(action), Foreground = _vm.Ink, Width = 21, Height = 21, HorizontalAlignment = HorizontalAlignment.Center });
            if (_vm.IsStandard) { var label = Label(ActionName(action), 10); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new(0, 6, 0, 0); stack.Children.Add(label); }
            var button = new Button { Content = stack, ToolTip = ActionName(action), Margin = new(3), Padding = new(6), Foreground = _vm.Ink };
            System.Windows.Automation.AutomationProperties.SetName(button, ActionName(action));
            button.Click += (_, _) => Run(action); panel.Children.Add(button); _system[action] = button;
        }
        if (_vm.Model.SystemButtons.Count == 0) panel.Children.Add(IconButton("settings", "버튼 설정", () => _app.OpenWidgetEditor(_vm)));
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SetResourceReference(StyleProperty, "SoftScrollViewer"); Children.Add(scroll);
    }
    public static string ActionName(SystemAction action) => action switch { SystemAction.Mute => "음소거", SystemAction.Shutdown => "전원 종료", SystemAction.Restart => "다시 시작", SystemAction.Wifi => "Wi-Fi", SystemAction.MicrophoneMute => "마이크 음소거", SystemAction.Capture => "캡처 도구", _ => "방해 금지" };
    public static string Glyph(SystemAction action) => action switch { SystemAction.Mute => "speaker_mute", SystemAction.Shutdown => "power", SystemAction.Restart => "arrow_clockwise", SystemAction.Wifi => "wifi_1", SystemAction.MicrophoneMute => "mic_off", SystemAction.Capture => "screenshot", _ => "alert_off" };
    private void UpdateSystem()
    {
        foreach (var (action, button) in _system)
        {
            bool? active = _app.SystemState(action);
            button.Background = active == true ? _vm.MutedInk : Brushes.Transparent;
            var stack = (StackPanel)button.Content;
            ((FluentIcon)stack.Children[0]).Foreground = active == true ? _vm.Background : _vm.Ink;
            foreach (var label in stack.Children.OfType<TextBlock>()) label.Foreground = active == true ? _vm.Background : _vm.Ink;
            button.ToolTip = ActionName(action) + (action is SystemAction.Mute or SystemAction.Wifi or SystemAction.DoNotDisturb or SystemAction.MicrophoneMute ? active.HasValue ? active.Value ? " · ON" : " · OFF" : " · 사용 불가" : "");
            if (action is SystemAction.Wifi or SystemAction.Mute or SystemAction.DoNotDisturb or SystemAction.MicrophoneMute) button.IsEnabled = active.HasValue && !_app.SystemBusy(action);
        }
    }
    private async void Run(SystemAction action)
    {
        if (action is SystemAction.Mute or SystemAction.Wifi or SystemAction.MicrophoneMute or SystemAction.DoNotDisturb)
        {
            try { await _app.ToggleSystemAsync(action); }
            catch (Exception ex) { Try(() => throw new IOException(ex.Message)); }
            return;
        }
        Try(() =>
        {
            if (action is SystemAction.Shutdown or SystemAction.Restart)
            {
                if (MessageBox.Show(Window.GetWindow(this), action == SystemAction.Restart ? "컴퓨터를 다시 시작할까요?" : "컴퓨터를 종료할까요?", ActionName(action), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                _app.Save(); DesktopActions.Power(action == SystemAction.Restart);
            }
            else if (action == SystemAction.Mute) DesktopActions.ToggleMute();
            else if (action == SystemAction.Wifi) DesktopActions.ToggleWifi();
            else if (action == SystemAction.MicrophoneMute) DesktopActions.ToggleMicrophone();
            else if (action == SystemAction.Capture) DesktopActions.Capture();
            else DoNotDisturbService.Toggle();
            _app.RefreshDesktopState();
        });
    }
}
