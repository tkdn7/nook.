using Nook.App.Services;
using Nook.App.ViewModels;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Threading.Tasks;

namespace Nook.App.Controls;

public sealed partial class WidgetFeaturesView
{
    private void BuildMemo()
    {
        _memo = new TextBox { Text = _vm.Model.MemoText, AcceptsReturn = true, AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.Transparent,
            Foreground = _vm.Ink, CaretBrush = _vm.Ink, BorderThickness = new(0), Padding = new(2, 6, 2, 2),
            HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top,
            FontSize = _vm.IsExpanded ? 15 : 13, MaxLength = 100000 };
        System.Windows.Automation.AutomationProperties.SetName(_memo, "메모 내용");
        _memo.SetResourceReference(StyleProperty, "MemoInput");
        _memo.TextChanged += (_, _) => { _vm.Model.MemoText = _memo.Text; _app.ScheduleSave(); };
        _memo.LostKeyboardFocus += (_, _) => _app.Save();
        Children.Add(_memo);
    }
}
