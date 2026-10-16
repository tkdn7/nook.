using System.Collections.ObjectModel;
using Nook.App.Services;

namespace Nook.App.ViewModels;

public sealed partial class AppViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _store;
    public AppSettings Settings { get; }
    public SystemMetricsService Metrics { get; }
    public ThemeService Theme { get; }
    public StartupService Startup { get; }
    public bool CanConfigureStartup => Startup.Available;
    public bool StartupEnabled
    {
        get => Startup.Enabled;
        set
        {
            try { Startup.SetEnabled(value); Status = ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or ArgumentException) { Status = "시작 프로그램 설정을 변경할 수 없습니다. " + ex.Message; }
            Raise();
        }
    }
    public MediaSessionService MediaService { get; } = new();
    public AudioOutputService AudioOutput { get; } = new();
    public ScreenTimeService ScreenTime { get; }
    public NetworkService Network { get; } = new();
    public BluetoothService Bluetooth { get; } = new();
    public KeyStatsService Keys { get; }
    public bool? MicrophoneMuted { get; private set; }
    public BatterySample Battery { get; private set; } = DesktopActions.Battery();
    public bool? Muted { get; private set; }
    public bool? Wifi { get; private set; }
    public bool? DoNotDisturb { get; private set; }
    public int? Volume { get; private set; }
    public IReadOnlyList<BrightnessDisplay> BrightnessDisplays { get; private set; } = [];
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private int _ticks;
    private bool _libraryVisible;
    public void SetLibraryVisible(bool visible)
    { if (_libraryVisible == visible) return; _libraryVisible = visible; UpdateMetricDemand(); }
    private void UpdateMetricDemand() => Metrics.SetDemand(
        _libraryVisible || Widgets.Any(w => w.Model.Visible && w.Model.Kind == WidgetKind.Gpu),
        _libraryVisible || Widgets.Any(w => w.Model.Visible && w.Model.Kind == WidgetKind.Disk));
    private int _desktopRevision, _desktopTaskRevision;
    private readonly Dictionary<SystemAction, bool> _pendingSystem = [];
    internal Func<SystemAction, System.Threading.Tasks.Task<bool?>>? SystemToggleOverride { get; set; }
    public bool SystemBusy(SystemAction action) => _pendingSystem.ContainsKey(action);
    public bool? SystemState(SystemAction action) => _pendingSystem.TryGetValue(action, out bool pending) ? pending : action switch
    { SystemAction.Mute => Muted, SystemAction.Wifi => Wifi, SystemAction.DoNotDisturb => DoNotDisturb, SystemAction.MicrophoneMute => MicrophoneMuted, _ => null };
    public async System.Threading.Tasks.Task ToggleSystemAsync(SystemAction action)
    {
        if (SystemBusy(action) || SystemState(action) is not { } old) return;
        _pendingSystem[action] = !old; _desktopRevision++;
        RefreshFeatures();
        try
        {
            bool? actual = SystemToggleOverride is { } control ? await control(action) : await System.Threading.Tasks.Task.Run(() =>
            {
                switch (action)
                {
                    case SystemAction.Mute: DesktopActions.ToggleMute(); return DesktopActions.Muted();
                    case SystemAction.Wifi: DesktopActions.ToggleWifi(); return DesktopActions.Wifi();
                    case SystemAction.MicrophoneMute: DesktopActions.ToggleMicrophone(); return DesktopActions.MicrophoneMuted();
                    default: DoNotDisturbService.Toggle(); return DoNotDisturbService.Read();
                }
            });
            switch (action) { case SystemAction.Mute: Muted = actual; break; case SystemAction.Wifi: Wifi = actual; break; case SystemAction.MicrophoneMute: MicrophoneMuted = actual; break; default: DoNotDisturb = actual; break; }
        }
        finally { _desktopRevision++; _pendingSystem.Remove(action); RefreshFeatures(); }
    }
    private void RefreshFeatures() { foreach (var widget in Widgets) widget.RefreshMetrics(); }
    private System.Threading.Tasks.Task<(BatterySample Battery, bool? Muted, bool? Wifi, bool? Dnd, int? Volume, IReadOnlyList<BrightnessDisplay> Displays, bool? Mic)>? _desktopTask;
    public ObservableCollection<WidgetViewModel> Widgets { get; } = [];
    public ObservableCollection<WidgetGroup> Groups { get; } = [];
    public event Action<WidgetViewModel>? WidgetAdded;
    public event Action<WidgetViewModel>? WidgetDeleted;
    public event Action<WidgetViewModel>? VisibilityChanged;
    public event Action<WidgetViewModel>? WidgetEditorRequested;
    public event Action? SettingsChanged;
    public event Action? CaptureLayoutRequested;
    public event Action? PresetApplied;
    public Func<(ScreenRect Work, double Scale)>? GetPlacementArea { get; set; }
    public bool MagneticMode
    {
        get => Settings.MagneticMode;
        set
        {
            if (Settings.MagneticMode == value) return;
            Settings.MagneticMode = value; Raise(); Raise(nameof(MagneticModeStateLabel));
            Save(); SettingsChanged?.Invoke();
        }
    }
    public string MagneticModeStateLabel => MagneticMode ? "ON" : "OFF";
    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string WidgetCount => $"{Widgets.Count}개의 위젯";
    public bool HasWidgets => Widgets.Count > 0;
    public string MetricSummary => Metrics.Current.Cpu is { } cpu && Metrics.Current.Memory is { } ram
        ? $"CPU {cpu:F0}%  ·  Memory {ram:F0}%" : "시스템 데이터를 읽는 중…";
    public string CpuPreviewValue => Metrics.Current.Cpu is { } v ? $"{v:F0}%" : "—";
    public string GpuPreviewValue => Metrics.Gpu.Usage is { } v ? $"{v:F0}%" : Metrics.Gpu.Warming ? "측정 중" : "—";
    public double GpuPreviewPercentage => Metrics.Gpu.Usage ?? 0;
    public string GpuPreviewDetail => Metrics.Gpu.Usage.HasValue ? "실시간 사용량" : Metrics.Gpu.Warming ? "측정 준비 중" : "GPU 카운터를 사용할 수 없습니다";
    public string MemoryPreviewValue => Metrics.Current.Memory is { } v ? $"{v:F0}%" : "—";
    public string ClockPreviewValue => Metrics.Current.Time.LocalDateTime.ToString("HH:mm");
    public string ClockPreviewDate => Metrics.Current.Time.LocalDateTime.ToString("M월 d일 · dddd", System.Globalization.CultureInfo.GetCultureInfo("ko-KR"));
    public double CpuPreviewPercentage => Metrics.Current.Cpu ?? 0;
    public double MemoryPreviewPercentage => Metrics.Current.Memory ?? 0;
    public string MemoryPreviewCapacity => Metrics.Current.TotalBytes > 0
        ? $"{Metrics.Current.UsedBytes / 1073741824.0:F1} / {Metrics.Current.TotalBytes / 1073741824.0:F1} GB" : "측정 중";
    public RelayCommand AddCpuCommand { get; }
    public RelayCommand AddMemoryCommand { get; }
    public RelayCommand AddClockCommand { get; }
    public RelayCommand AddGpuCommand { get; }
    public RelayCommand AddWeatherCommand { get; }

    public AppViewModel(AppSettings settings, SettingsStore store, SystemMetricsService metrics, ThemeService theme, StartupService? startup = null)
    {
        Startup = startup ?? new StartupService();
        settings.MainTheme ??= settings.Theme;
        Settings = settings; _store = store; Metrics = metrics; Theme = theme;
        ScreenTime = new(store.DirectoryPath);
        ScreenTime.ExcludeNook = settings.ExcludeNookFromScreenTime;
        Keys = new(store.DirectoryPath);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };
        MediaService.Updated += OnMedia;
        AudioOutput.Updated += OnOutput;
        AddCpuCommand = new(_ => Add(WidgetKind.Cpu));
        AddMemoryCommand = new(_ => Add(WidgetKind.Memory));
        AddClockCommand = new(_ => Add(WidgetKind.Clock));
        AddGpuCommand = new(_ => Add(WidgetKind.Gpu));
        AddWeatherCommand = new(_ => Add(WidgetKind.Weather));
        foreach (var group in settings.Groups) Groups.Add(group);
        foreach (var widget in settings.Widgets) Widgets.Add(new(this, widget));
        if (store.Warning is { } warning) Status = warning;
        Metrics.Updated += OnMetrics;
        Theme.Changed += Refresh;
        UpdateMetricDemand();
    }
    private void OnMetrics()
    {
        Network.Poll(Widgets.Any(w => w.Model.Kind == WidgetKind.Network));
        Keys.Poll(Widgets.Any(w => w.Model.Kind == WidgetKind.KeyStats));
        if (Widgets.Any(w => w.Model.Visible && w.Model.Kind == WidgetKind.Bluetooth)) _ = Bluetooth.RefreshAsync();
        foreach (var widget in Widgets.Where(w => w.Model.Kind is WidgetKind.Pomodoro or WidgetKind.Timer))
            if (CountdownEngine.Tick(widget.Model.Countdown, widget.Model.Kind == WidgetKind.Pomodoro, DateTimeOffset.UtcNow)) { System.Media.SystemSounds.Asterisk.Play(); Save(); }
        ScreenTime.Poll(Widgets.Any(w => w.Model.Kind is WidgetKind.ScreenTime or WidgetKind.Activity));
        if (Widgets.Any(w => w.Model.Visible && w.IsMedia)) _ = MediaService.RefreshAsync();
        if (Widgets.Any(w => w.Model.Visible && w.Model.Kind == WidgetKind.MediaController)) _ = AudioOutput.RefreshAsync();
        ApplyDesktopState();
        if (++_ticks % 3 == 0 && Widgets.Any(w => w.Model.Visible && w.Model.Kind is WidgetKind.Battery or WidgetKind.SystemShelf or WidgetKind.Volume or WidgetKind.Brightness or WidgetKind.MediaController)) RefreshDesktopState();
        foreach (var widget in Widgets.Where(w => w.Model.Visible)) widget.RefreshMetrics();
        if (!_libraryVisible) return;
        Raise(nameof(MetricSummary));
        Raise(nameof(CpuPreviewValue)); Raise(nameof(MemoryPreviewValue)); Raise(nameof(ClockPreviewValue));
        Raise(nameof(ClockPreviewDate)); Raise(nameof(CpuPreviewPercentage)); Raise(nameof(MemoryPreviewPercentage)); Raise(nameof(MemoryPreviewCapacity));
        Raise(nameof(GpuPreviewValue)); Raise(nameof(GpuPreviewPercentage)); Raise(nameof(GpuPreviewDetail));
    }
    private void ApplyDesktopState()
    {
        if (_desktopTask is { IsCompleted: true } task)
        {
            if (task.IsCompletedSuccessfully && _desktopTaskRevision == _desktopRevision) { Battery = task.Result.Battery; Muted = task.Result.Muted; Wifi = task.Result.Wifi; DoNotDisturb = task.Result.Dnd; Volume = task.Result.Volume; BrightnessDisplays = task.Result.Displays; MicrophoneMuted = task.Result.Mic; }
            _desktopTask = null;
        }
    }
    private void OnMedia() { foreach (var widget in Widgets.Where(w => w.Model.Visible && w.IsMedia)) widget.RefreshMetrics(); }
    private void OnOutput() { foreach (var widget in Widgets.Where(w => w.Model.Visible && w.Model.Kind == WidgetKind.MediaController)) widget.RefreshMetrics(); }
    public void ScheduleSave() { _saveTimer.Stop(); _saveTimer.Start(); }
    public void RefreshDesktopState()
    {
        if (_desktopTask is not null) return;
        _desktopTaskRevision = _desktopRevision;
        bool brightness = Widgets.Any(w => w.Model.Kind == WidgetKind.Brightness);
        _desktopTask = System.Threading.Tasks.Task.Run(() => (DesktopActions.Battery(), DesktopActions.Muted(), DesktopActions.Wifi(), DoNotDisturbService.Read(), DesktopActions.Volume(), brightness ? BrightnessService.Read() : (IReadOnlyList<BrightnessDisplay>)[], DesktopActions.MicrophoneMuted()));
    }
    public async System.Threading.Tasks.Task RefreshDesktopStateAsync()
    {
        RefreshDesktopState();
        if (_desktopTask is { } task) await task;
        ApplyDesktopState();
        foreach (var widget in Widgets) widget.RefreshMetrics();
    }
    public WidgetViewModel Add(WidgetKind kind)
    {
        var (work, scale) = GetPlacementArea?.Invoke() ?? (new ScreenRect(0, 0, 1920, 1040), 1.0);
        double w = 300 * scale, h = 200 * scale, gap = 16 * scale;
        int columns = Math.Max(1, (int)((work.Width - gap) / (w + gap)));
        int rows = Math.Max(1, (int)((work.Height - gap) / (h + gap)));
        int index = Widgets.Count;
        int cell = index % (columns * rows);
        double offset = index / (columns * rows) * 24 * scale;
        var rect = Placement.Clamp(new(work.Left + gap + cell % columns * (w + gap) + offset,
            work.Top + gap + cell / columns * (h + gap) + offset, w, h), work);
        var model = new WidgetInstance { Kind = kind, Left = rect.Left, Top = rect.Top };
        if (kind == WidgetKind.Dice) { model.Width = 260; model.Height = 170; }
        if (kind == WidgetKind.KeyStats) { model.Width = 200; model.Height = 150; }
        if (kind == WidgetKind.Calculator) model.Height = 400;
        WidgetShapes.Normalize(model);
        Settings.Widgets.Add(model);
        var vm = new WidgetViewModel(this, model);
        Widgets.Add(vm); WidgetAdded?.Invoke(vm);
        Changed(); return vm;
    }
    public void Toggle(WidgetViewModel widget)
    {
        widget.Model.Visible = !widget.Model.Visible;
        UpdateMetricDemand(); if (widget.Model.Visible) widget.RefreshMetrics();
        VisibilityChanged?.Invoke(widget); Changed();
    }
    public void ToggleAll()
    {
        bool visible = !Widgets.Any(w => w.Model.Visible);
        foreach (var widget in Widgets) { widget.Model.Visible = visible; VisibilityChanged?.Invoke(widget); }
        Changed();
    }
    public void Delete(WidgetViewModel widget)
    {
        WidgetDeleted?.Invoke(widget); Widgets.Remove(widget); Settings.Widgets.Remove(widget.Model);
        Changed();
    }
    public void OpenWidgetEditor(WidgetViewModel widget) => WidgetEditorRequested?.Invoke(widget);
    public void SelectWidget(WidgetViewModel widget, bool toggle)
    {
        if (toggle) widget.Selected = !widget.Selected;
        else { foreach (var item in Widgets) item.Selected = item == widget; }
    }
    public void ClearSelection() { foreach (var item in Widgets) item.Selected = false; }
    public WidgetGroup CreateGroup(string name)
    {
        var group = new WidgetGroup { Name = string.IsNullOrWhiteSpace(name) ? "새 그룹" : name.Trim() };
        Settings.Groups.Add(group); Groups.Add(group); Changed(); return group;
    }
    public void DeleteGroup(WidgetGroup group)
    {
        AppearanceResolver.DeleteGroup(Settings, group.Id); Groups.Remove(group); Changed();
    }
    public bool SendCalculation(WidgetViewModel calculator, string expression, string result)
    {
        var memos = Widgets.Where(w => w.Model.Kind == WidgetKind.Memo).ToArray();
        var target = memos.Length == 1 ? memos[0] : memos.FirstOrDefault(w => w.Model.Id == calculator.Model.CalculatorMemoId);
        if (target is null) { OpenWidgetEditor(calculator); return false; }
        string addition = expression + " = " + result;
        if (target.Model.MemoText.Length + addition.Length + 2 > 100000) return false;
        target.Model.MemoText += (target.Model.MemoText.Length == 0 ? "" : Environment.NewLine) + addition;
        target.RefreshMetrics(); Save(); return true;
    }
    public void Changed()
    {
        UpdateMetricDemand();
        Keys.Poll(Widgets.Any(w => w.Model.Kind == WidgetKind.KeyStats));
        ScreenTime.ExcludeNook = Settings.ExcludeNookFromScreenTime;
        Theme.Apply(Settings.MainTheme ?? Settings.Theme);
        Save(); Raise(nameof(WidgetCount)); Raise(nameof(HasWidgets));
        SettingsChanged?.Invoke();
    }
    public void Refresh() { foreach (var widget in Widgets) widget.RefreshAppearance(); }
    public void Save()
    {
        if (IsApplyingPreset) return;
        LayoutPresets.SyncMemos(Settings);
        if (!_store.Save(Settings) && _store.Warning is { } warning) Status = warning;
    }
    public void Dispose()
    { _saveTimer.Stop(); Save(); Metrics.SetDemand(false, false); Keys.Dispose(); ScreenTime.Dispose(); AudioOutput.Updated -= OnOutput; MediaService.Updated -= OnMedia; MediaService.Dispose(); Metrics.Updated -= OnMetrics; Theme.Changed -= Refresh; }
}
