using Nook.App.Services;
using Nook.App.ViewModels;
using Nook.App.Views;
using Forms = System.Windows.Forms;

namespace Nook.App;

public partial class App : Application
{
    internal static bool InspectWidgets { get; private set; }
    private SingleInstance? _instance;
    private WindowCoordinator? _windows;
    private AppViewModel? _vm;
    private ThemeService? _theme;
    private SystemMetricsService? _metrics;
    private Forms.NotifyIcon? _tray;
    private MainWindow? _main;
    private string _directory = "";
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InspectWidgets = e.Args.Contains("--inspect-widgets");
        _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nook");
        int index = Array.IndexOf(e.Args, "--settings-dir");
        if (index >= 0 && index + 1 < e.Args.Length) _directory = Path.GetFullPath(e.Args[index + 1]);
        DispatcherUnhandledException += (_, error) =>
        {
            try { Directory.CreateDirectory(_directory); File.AppendAllText(Path.Combine(_directory, "errors.log"), $"{DateTimeOffset.Now:O} {error.Exception}\n"); } catch { }
            MessageBox.Show("Nook에서 오류가 발생했습니다. 설정 폴더의 errors.log를 확인해 주세요.", "Nook", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        _instance = new SingleInstance(_directory);
        if (!_instance.IsPrimary) { await _instance.SignalPrimaryAsync(); Shutdown(); return; }
        var store = new SettingsStore(_directory);
        var settings = store.Load();
        _theme = new ThemeService(); _theme.Apply(settings.Theme);
        _metrics = new SystemMetricsService(Dispatcher);
        _vm = new AppViewModel(settings, store, _metrics, _theme);
        _main = new MainWindow(_vm); MainWindow = _main;
        _main.Show();
        _windows = new WindowCoordinator(_vm, _main);
        _instance.Listen(() => _main.BringForward());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Nook 열기", null, (_, _) => _main.BringForward());
        menu.Items.Add("모든 위젯 표시·숨기기", null, (_, _) => _vm.ToggleAll());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Shutdown());
        _tray = new Forms.NotifyIcon
        {
            Text = "Nook · 바탕화면 위젯", Icon = new System.Drawing.Icon(GetResourceStream(new Uri("pack://application:,,,/Nook;component/Assets/nook.ico")).Stream),
            ContextMenuStrip = menu, Visible = true
        };
        _tray.DoubleClick += (_, _) => _main.BringForward();
        _vm.Save();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _windows?.CaptureAll(); _vm?.Save();
        if (_main is not null) _main.Exiting = true;
        _windows?.Dispose(); _vm?.Dispose(); _metrics?.Dispose(); _theme?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Icon?.Dispose(); _tray.Dispose(); }
        _instance?.Dispose();
        base.OnExit(e);
    }
}
