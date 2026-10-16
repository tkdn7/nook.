using System.Windows.Input;
using System.Windows.Controls.Primitives;
using Nook.App.ViewModels;

namespace Nook.App.Controls;

public sealed class CalculatorView : Grid, IDisposable
{
    private readonly AppViewModel _app;
    private readonly WidgetViewModel _vm;
    private readonly TextBox _expression;
    private readonly TextBlock _result;
    private readonly UniformGrid _keys = new() { Rows = 5 };
    private readonly Button _send, _angle;
    private bool _scientific, _syncing;
    private double? _value;
    public bool Scientific => _scientific;
    public CalculatorView(AppViewModel app, WidgetViewModel vm)
    {
        _app = app; _vm = vm;
        RowDefinitions.Add(new() { Height = new(34) }); RowDefinitions.Add(new() { Height = new(34) }); RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = new(30) });
        _expression = new TextBox { Text = vm.Calculator.Expression, Foreground = vm.Ink, CaretBrush = vm.Ink, FontSize = 14, MaxLength = 512,
            Background = Brushes.Transparent, Padding = new(2, 4, 2, 2), VerticalContentAlignment = VerticalAlignment.Top, HorizontalContentAlignment = HorizontalAlignment.Right };
        _expression.SetResourceReference(StyleProperty, "MemoInput"); System.Windows.Automation.AutomationProperties.SetName(_expression, "계산식"); Children.Add(_expression);
        _result = new TextBlock { Foreground = vm.Ink, FontFamily = (FontFamily)FindResource("NumberFont"), FontSize = 25, FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        SetRow(_result, 1); Children.Add(_result); SetRow(_keys, 2); Children.Add(_keys);
        var footer = new DockPanel { Margin = new(0, 4, 0, 0) }; SetRow(footer, 3); Children.Add(footer);
        _send = new Button { Content = "메모로 보내기", Padding = new(8, 2, 8, 2), FontSize = 11, Margin = new(), Foreground = vm.Ink, BorderThickness = new(0), Background = Brushes.Transparent };
        System.Windows.Automation.AutomationProperties.SetName(_send, "메모로 보내기"); DockPanel.SetDock(_send, Dock.Right); footer.Children.Add(_send);
        _angle = new Button { Content = vm.Calculator.Degrees ? "DEG" : "RAD", Padding = new(6, 2, 6, 2), FontSize = 10, Margin = new(), BorderThickness = new(0), Background = Brushes.Transparent, Foreground = vm.MutedInk, HorizontalAlignment = HorizontalAlignment.Left }; footer.Children.Add(_angle);
        _expression.TextChanged += (_, _) => { if (!_syncing) { vm.Calculator.Expression = _expression.Text; vm.Calculator.CompletedExpression = null; vm.Calculator.CompletedResult = null; vm.Calculator.Notify(); } UpdateResult(); };
        _expression.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Execute("="); e.Handled = true; } else if (e.Key == Key.Escape) { Execute("C"); e.Handled = true; } };
        _angle.Click += (_, _) => { vm.Calculator.Degrees = !vm.Calculator.Degrees; vm.Calculator.CompletedExpression = null; vm.Calculator.CompletedResult = null; vm.Calculator.Notify(); };
        _send.Click += (_, _) => { if (_value is { } value && app.SendCalculation(vm, vm.Calculator.CompletedExpression ?? vm.Calculator.Expression, CalculatorEngine.Display(value))) { _send.ToolTip = "메모에 저장했습니다."; } };
        vm.Calculator.Changed += Sync;
        SizeChanged += (_, _) => UpdateLayoutMode();
        BuildKeys(false); UpdateResult();
    }
    private void Sync()
    {
        if (_expression.Text != _vm.Calculator.Expression)
        { _syncing = true; _expression.Text = _vm.Calculator.Expression; _syncing = false; }
        _angle.Content = _vm.Calculator.Degrees ? "DEG" : "RAD"; UpdateResult();
    }
    private void UpdateLayoutMode()
    {
        bool scientific = ActualWidth >= 360 && ActualHeight >= 295;
        if (scientific != _scientific) BuildKeys(scientific);
        _result.FontSize = Math.Clamp(ActualWidth * .09, 22, 36);
    }
    private void BuildKeys(bool scientific)
    {
        _scientific = scientific; _keys.Columns = scientific ? 6 : 4; _keys.Children.Clear();
        string[][] basic = [["C", "⌫", "%", "÷"], ["7", "8", "9", "×"], ["4", "5", "6", "−"], ["1", "2", "3", "+"], ["()", "0", ".", "="]];
        string[][] extra = [["sin", "cos"], ["tan", "√"], ["ln", "log"], ["x²", "^"], ["π", "Ans"]];
        for (int row = 0; row < 5; row++)
        foreach (string key in scientific ? extra[row].Concat(basic[row]) : basic[row])
        {
            var button = new Button { Content = key, FontFamily = (FontFamily)FindResource("NumberFont"), FontSize = key.Length > 2 ? 12 : 16,
                Padding = new(2), Margin = new(2), MinWidth = 0, MinHeight = 0, Foreground = _vm.Ink, BorderThickness = new(0) };
            var surface = _vm.MutedInk.CloneCurrentValue(); surface.Opacity = key == "=" ? .22 : .08; button.Background = surface;
            System.Windows.Automation.AutomationProperties.SetName(button, key);
            button.Click += (_, _) => Execute(key); _keys.Children.Add(button);
        }
        _angle.Visibility = scientific ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Execute(string key)
    {
        if (key == "=")
        {
            if (_value is { } value)
            {
                _vm.Calculator.CompletedExpression ??= _expression.Text; _vm.Calculator.CompletedResult = value;
                _vm.Calculator.Answer = value; _vm.Calculator.Notify(); _expression.Focus(); _expression.SelectAll();
            }
            return;
        }
        if (key == "C") { _expression.Clear(); return; }
        if (_vm.Calculator.CompletedExpression is not null && key is "+" or "−" or "×" or "÷" or "^" or "x²" or "%")
        { _expression.Text = CalculatorEngine.Display(_vm.Calculator.CompletedResult ?? _vm.Calculator.Answer); _expression.CaretIndex = _expression.Text.Length; _expression.SelectionLength = 0; }
        int caret = _expression.CaretIndex, length = _expression.SelectionLength;
        if (key == "⌫") { if (length > 0) _expression.SelectedText = ""; else if (caret > 0) { _expression.Select(caret - 1, 1); _expression.SelectedText = ""; } _expression.Focus(); return; }
        string text = key switch { "−" => "-", "×" => "*", "÷" => "/", "π" => "pi", "√" => "sqrt(", "x²" => "^2", "()" => "()", "sin" or "cos" or "tan" or "ln" or "log" => key + "(", _ => key };
        if (_expression.Text.Length - length + text.Length > 512) return;
        _expression.SelectedText = text; _expression.CaretIndex = caret + text.Length - (key == "()" ? 1 : 0); _expression.Focus();
    }
    private void UpdateResult()
    {
        _value = null;
        try { if (!string.IsNullOrWhiteSpace(_expression.Text)) _value = _vm.Calculator.CompletedExpression == _expression.Text && _vm.Calculator.CompletedResult is { } completed ? completed : CalculatorEngine.Evaluate(_expression.Text, _vm.Calculator.Degrees, _vm.Calculator.Answer); _result.Text = _value is { } value ? CalculatorEngine.Display(value) : "0"; _result.ToolTip = null; }
        catch (FormatException ex) { _result.Text = "—"; _result.ToolTip = ex.Message; }
        _send.IsEnabled = _value.HasValue;
    }
    public void Dispose() => _vm.Calculator.Changed -= Sync;
}
