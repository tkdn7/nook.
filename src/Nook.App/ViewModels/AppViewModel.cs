using System.Collections.ObjectModel;
using Nook.App.Services;

namespace Nook.App.ViewModels;

public sealed class AppViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    public AppSettings Settings { get; }
    public SystemMetricsService Metrics { get; }
    public ThemeService Theme { get; }
    public ObservableCollection<WidgetViewModel> Widgets { get; } = [];
    public ObservableCollection<WidgetGroup> Groups { get; } = [];
    public event Action<WidgetViewModel>? WidgetAdded;
    public event Action<WidgetViewModel>? WidgetDeleted;
    public event Action<WidgetViewModel>? VisibilityChanged;
    public event Action<WidgetViewModel>? WidgetEditorRequested;
    public event Action? SettingsChanged;
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
    public RelayCommand AddDemoCommand { get; }

    public AppViewModel(AppSettings settings, SettingsStore store, SystemMetricsService metrics, ThemeService theme)
    {
        Settings = settings; _store = store; Metrics = metrics; Theme = theme;
        AddCpuCommand = new(_ => Add(WidgetKind.Cpu));
        AddMemoryCommand = new(_ => Add(WidgetKind.Memory));
        AddClockCommand = new(_ => Add(WidgetKind.Clock));
        AddGpuCommand = new(_ => Add(WidgetKind.Gpu));
        AddWeatherCommand = new(_ => Add(WidgetKind.Weather));
        AddDemoCommand = new(_ => { Add(WidgetKind.Cpu); Add(WidgetKind.Memory); Add(WidgetKind.Clock); });
        foreach (var group in settings.Groups) Groups.Add(group);
        foreach (var widget in settings.Widgets) Widgets.Add(new(this, widget));
        if (store.Warning is { } warning) Status = warning;
        Metrics.Updated += OnMetrics;
        Theme.Changed += Refresh;
    }
    private void OnMetrics()
    {
        foreach (var widget in Widgets) widget.RefreshMetrics();
        Raise(nameof(MetricSummary));
        Raise(nameof(CpuPreviewValue)); Raise(nameof(MemoryPreviewValue)); Raise(nameof(ClockPreviewValue));
        Raise(nameof(ClockPreviewDate)); Raise(nameof(CpuPreviewPercentage)); Raise(nameof(MemoryPreviewPercentage)); Raise(nameof(MemoryPreviewCapacity));
        Raise(nameof(GpuPreviewValue)); Raise(nameof(GpuPreviewPercentage)); Raise(nameof(GpuPreviewDetail));
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
        Settings.Widgets.Add(model);
        var vm = new WidgetViewModel(this, model);
        Widgets.Add(vm); WidgetAdded?.Invoke(vm);
        Changed(); return vm;
    }
    public void Toggle(WidgetViewModel widget)
    {
        widget.Model.Visible = !widget.Model.Visible;
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
    public WidgetGroup CreateGroup(string name)
    {
        var group = new WidgetGroup { Name = string.IsNullOrWhiteSpace(name) ? "새 그룹" : name.Trim() };
        Settings.Groups.Add(group); Groups.Add(group); Changed(); return group;
    }
    public void DeleteGroup(WidgetGroup group)
    {
        AppearanceResolver.DeleteGroup(Settings, group.Id); Groups.Remove(group); Changed();
    }
    public void Changed()
    {
        Theme.Apply(Settings.Theme);
        Save(); Raise(nameof(WidgetCount)); Raise(nameof(HasWidgets));
        SettingsChanged?.Invoke();
    }
    public void Refresh() { foreach (var widget in Widgets) widget.RefreshAppearance(); }
    public void Save()
    {
        if (!_store.Save(Settings) && _store.Warning is { } warning) Status = warning;
    }
    public void Dispose()
    { Metrics.Updated -= OnMetrics; Theme.Changed -= Refresh; }
}
