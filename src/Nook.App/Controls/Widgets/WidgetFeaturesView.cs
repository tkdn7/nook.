using Nook.App.Services;
using Nook.App.ViewModels;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Threading.Tasks;

namespace Nook.App.Controls;

public sealed partial class WidgetFeaturesView : Grid, IDisposable
{
    private readonly AppViewModel _app;
    private readonly WidgetViewModel _vm;
    private string _signature = "";
    private AirQualityVisual? _air;
    private TextBox? _memo;
    private TextBlock? _primary, _secondary, _time, _status;
    private Button? _play, _next, _previous;
    private FluentIcon? _playIcon;
    private SmoothProgressBar? _progress;
    private Border? _cover;
    private FluentIcon? _coverFallback;
    private readonly Dictionary<SystemAction, Button> _system = [];
    private bool _disposed, _controlBusy;
    public WidgetFeaturesView(AppViewModel app, WidgetViewModel vm)
    {
        _app = app; _vm = vm; ClipToBounds = true; Background = Brushes.Transparent;
        _adjustTimer.Tick += (_, _) => _ = ApplyLevel();
        vm.FeatureUpdated += Refresh;
        SizeChanged += (_, _) => { _air?.InvalidateVisual(); UpdateControlOrientation(); PositionCountdown(); };
        Refresh();
    }
    private TextBlock Label(string text, double size = 12, bool muted = false)
        => new() { Text = text, FontSize = size, Foreground = muted ? _vm.MutedInk : _vm.Ink,
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
    private Button IconButton(string glyph, string name, Action action)
    {
        var button = new Button { Content = new FluentIcon { Glyph = glyph, Width = 18, Height = 18, Foreground = _vm.Ink },
            ToolTip = name, Padding = new(7), Margin = new(3, 0, 3, 0), MinWidth = 30, Height = 32, Foreground = _vm.Ink };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action(); return button;
    }
    public void Refresh()
    {
        if (_disposed) return;
        var m = _vm.Model;
        string signature = $"{m.Kind}/{_vm.Layout}/{m.Shape}/{_vm.IsDark}/{m.AirQuality.Style}/{m.ControlOrientation}/" +
            string.Join("|", m.Shelf.Select(x => $"{x.Id}:{x.Name}:{x.Path}")) + "/" + string.Join(",", m.SystemButtons) + $"/{m.ClockStyle}/{m.DiceCount}/{m.ActivityPeriod}";
        if (m.Kind == WidgetKind.Bluetooth) signature += $"/{_app.Bluetooth.Enabled}/{_app.Bluetooth.Status}/{_app.Bluetooth.Busy}/" + string.Join("|", _app.Bluetooth.Devices.Select(x => $"{x.Id}:{x.Connected}:{x.Battery}"));
        signature += $"/{m.ActivityShowScreenTime}/{m.MediaControllerShowVolume}";
        if (m.Kind == WidgetKind.MediaController) signature += $"/{_app.AudioOutput.Busy}/{_app.AudioOutput.Error}/" + string.Join("|", _app.AudioOutput.Devices.Select(d => $"{d.Id}:{d.Name}:{d.Default}"));
        if (_signature != signature)
        {
            DisposeExtras(); (_deskVisual as IDisposable)?.Dispose(); _deskVisual = null; _countdownPlay = null; _countdownControls = null;
            _signature = signature; Children.Clear(); RowDefinitions.Clear(); ColumnDefinitions.Clear();
            _air = null; _memo = null; _level = null; _outputPercentage = null; _primary = _secondary = _time = _status = null; _system.Clear();
            switch (m.Kind)
            {
                case WidgetKind.AirQuality: _air = new(m); Children.Add(_air); break;
                case WidgetKind.Media: BuildMedia(); break;
                case WidgetKind.ProgramShelf: BuildShelf(); break;
                case WidgetKind.SystemShelf: BuildSystem(); break;
                case WidgetKind.Memo: BuildMemo(); break;
                case WidgetKind.Battery: BuildBattery(); break;
                case WidgetKind.Volume or WidgetKind.Brightness: BuildLevel(); break;
                case WidgetKind.Disk: BuildDisk(); break;
                case WidgetKind.ScreenTime: BuildScreenTime(); break;
                case WidgetKind.Clock: BuildClock(); break;
                case WidgetKind.Timer or WidgetKind.Pomodoro: BuildCountdown(); break;
                case WidgetKind.Network: BuildNetwork(); break;
                case WidgetKind.Bluetooth: BuildBluetooth(); break;
                case WidgetKind.KeyStats: BuildKeys(); break;
                case WidgetKind.Dice: BuildDice(); break;
                case WidgetKind.Activity: BuildActivity(); break;
                case WidgetKind.Calculator: _deskVisual = new CalculatorView(_app, _vm); Children.Add(_deskVisual); break;
                case WidgetKind.MediaController: BuildOutput(); break;
            }
        }
        _air?.Refresh(_vm.Ink, _vm.MutedInk);
        if (m.Kind == WidgetKind.Media) UpdateMedia();
        if (m.Kind == WidgetKind.Battery) UpdateBattery();
        if (m.Kind == WidgetKind.SystemShelf) UpdateSystem();
        if (m.Kind is WidgetKind.Volume or WidgetKind.Brightness) UpdateLevel();
        if (m.Kind == WidgetKind.MediaController && m.MediaControllerShowVolume) UpdateLevel();
        if (m.Kind == WidgetKind.Disk) UpdateDisk();
        if (m.Kind == WidgetKind.ScreenTime) UpdateScreenTime();
        if (m.Kind == WidgetKind.KeyStats) _deskVisual?.InvalidateVisual();
        UpdateExtras(); UpdateDesk();
        if (m.Kind == WidgetKind.Memo && _memo is not null && _memo.Text != m.MemoText)
        { int caret = _memo.CaretIndex; _memo.Text = m.MemoText; _memo.CaretIndex = Math.Min(caret, _memo.Text.Length); }
    }
    private void Try(Action action)
    { try { action(); } catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Nook", MessageBoxButton.OK, MessageBoxImage.Information); } }
    public void Dispose() { _disposed = true; DisposeExtras(); (_deskVisual as IDisposable)?.Dispose(); _adjustTimer.Stop(); _pendingLevel = null; _vm.FeatureUpdated -= Refresh; }
}
