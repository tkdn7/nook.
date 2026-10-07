using Nook.App.Services;
using System.Globalization;

namespace Nook.App.ViewModels;

public sealed class WidgetViewModel : ObservableObject
{
    private readonly AppViewModel _app;
    private LayoutMode _layout;
    public WidgetInstance Model { get; }
    public WidgetViewModel(AppViewModel app, WidgetInstance model)
    {
        _app = app; Model = model;
        EditCommand = new(_ => _app.OpenWidgetEditor(this));
        ToggleVisibilityCommand = new(_ => _app.Toggle(this));
        DeleteCommand = new(_ => _app.Delete(this));
        RefreshAppearance(); RefreshMetrics();
    }
    public RelayCommand EditCommand { get; }
    public RelayCommand ToggleVisibilityCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public string Title => Model.Kind switch { WidgetKind.Cpu => "CPU", WidgetKind.Memory => "Memory", WidgetKind.Clock => "Clock", WidgetKind.Gpu => "GPU", _ => "날씨 · 데모" };
    public string Subtitle => Model.Kind switch { WidgetKind.Cpu => "프로세서 사용량", WidgetKind.Memory => "물리 메모리", WidgetKind.Clock => "로컬 시간", WidgetKind.Gpu => "GPU 사용량", _ => "서울 · 데모 데이터" };
    public bool IsDark => _app.Theme.DarkFor(AppearanceResolver.ResolveTheme(_app.Settings, Model));
    public string ThemeSource => Model.Theme is { } theme ? theme switch { ThemeMode.System => "개별 · Windows 설정", ThemeMode.Light => "개별 · 라이트", _ => "개별 · 다크" } : "전체 테마 따르기";
    public string Icon => Model.Kind switch { WidgetKind.Cpu or WidgetKind.Gpu => "developer_board", WidgetKind.Memory => "data_usage", WidgetKind.Clock => "clock", _ => "weather_partly_cloudy_day" };
    public string GroupLabel => _app.Settings.Groups.Find(g => g.Id == Model.GroupId)?.Name ?? "그룹 없음";
    public string VisibilityLabel => Model.Visible ? "숨기기" : "표시하기";
    public string StateLabel => $"{(Model.Visible ? "표시 중" : "숨김")} · {GroupLabel}{(Model.Locked ? " · 잠금" : "")}";
    public bool CanResize => !Model.Locked;
    public LayoutMode Layout => _layout;
    public bool IsStandard => _layout != LayoutMode.Small;
    public bool IsExpanded => _layout == LayoutMode.Big;
    public bool IsMetric => Model.Kind is WidgetKind.Cpu or WidgetKind.Memory or WidgetKind.Gpu;
    public bool IsCircular => IsMetric && Model.Shape == WidgetShape.Circle;
    public bool IsCard => !IsCircular;
    public bool ShowCircleDetail => IsCircular && IsStandard && Model.Kind == WidgetKind.Memory;
    public double CircleValueFontSize => Value == "측정 중" ? Math.Min(_circleFontSize, 26) : _circleFontSize;
    private double _circleFontSize = 56;
    public bool IsWeather => Model.Kind == WidgetKind.Weather;
    public bool ShowForecast => IsWeather && IsExpanded;
    public string SizeLabel => _layout.ToString();
    public bool ShowBar => IsMetric && _layout == LayoutMode.Mid;
    public bool ShowGraph => IsMetric && IsExpanded;
    public bool ShowDetails => IsStandard && !string.IsNullOrEmpty(Detail);
    public CornerRadius Radius { get; private set; }
    public Brush Background { get; private set; } = Brushes.White;
    public Brush Ink { get; private set; } = Brushes.Black;
    public Brush MutedInk { get; private set; } = Brushes.Gray;
    public string RadiusSource { get; private set; } = "";
    public string OpacitySource { get; private set; } = "";
    public string Value { get; private set; } = "—";
    public string Detail { get; private set; } = "";
    public string Footer { get; private set; } = "";
    public double Percentage { get; private set; }
    private double _numericFontSize = 48;
    public double ValueFontSize { get => Value == "측정 중" ? Math.Min(_numericFontSize, 28) : _numericFontSize; private set => _numericFontSize = value; }
    public IReadOnlyList<MetricPoint> History => Model.Kind switch { WidgetKind.Cpu => _app.Metrics.CpuHistory.Points, WidgetKind.Gpu => _app.Metrics.GpuHistory.Points, _ => _app.Metrics.MemoryHistory.Points };
    public string WeatherLocation => "서울 · 데모 데이터";
    public IReadOnlyList<WeatherHour> Forecast { get; } = [new("지금", "22°", "weather_partly_cloudy_day"), new("14시", "24°", "weather_sunny"), new("16시", "23°", "weather_sunny"), new("18시", "20°", "weather_partly_cloudy_day")];

    public void SetSize(double width, double height)
    {
        _layout = ResponsiveLayout.Resolve(width, height, _layout);
        ValueFontSize = Model.Kind == WidgetKind.Clock
            ? Math.Clamp((width - 44) / (IsExpanded ? 5.2 : 3.3), 28, 64)
            : Math.Clamp((width - 44) / 3.5, 36, 64);
        _circleFontSize = Math.Clamp((Math.Min(width, height) - 12) * .23, 36, 76);
        foreach (var name in new[] { nameof(Layout), nameof(IsStandard), nameof(IsExpanded), nameof(ShowBar), nameof(ShowGraph), nameof(ShowDetails), nameof(ValueFontSize), nameof(SizeLabel), nameof(ShowForecast), nameof(CircleValueFontSize), nameof(ShowCircleDetail) }) Raise(name);
        RefreshMetrics();
    }

    public void RefreshAppearance()
    {
        var appearance = AppearanceResolver.Resolve(_app.Settings, Model);
        Radius = new(IsCircular ? 1000 : appearance.CornerRadius);
        bool clock = Model.Kind == WidgetKind.Clock;
        var palette = ThemeService.Palette(IsDark);
        var color = palette[clock ? "ClockSurfaceBrush" : "SurfaceBrush"];
        color.A = (byte)Math.Round(appearance.BackgroundOpacity * 255);
        Background = new SolidColorBrush(color);
        Ink = new SolidColorBrush(palette[clock ? "ClockTextBrush" : "TextBrush"]);
        MutedInk = new SolidColorBrush(palette[clock ? "ClockMutedBrush" : "MutedBrush"]);
        RadiusSource = $"{appearance.Radius.Value} · {appearance.Radius.Source}";
        OpacitySource = $"{appearance.Opacity.Value} · {appearance.Opacity.Source}";
        foreach (var name in new[] { nameof(Radius), nameof(Background), nameof(Ink), nameof(MutedInk), nameof(IsDark), nameof(ThemeSource), nameof(RadiusSource), nameof(OpacitySource), nameof(GroupLabel), nameof(StateLabel), nameof(VisibilityLabel), nameof(CanResize), nameof(IsCircular), nameof(IsCard), nameof(ShowCircleDetail) }) Raise(name);
    }
    public void RefreshMetrics()
    {
        var sample = _app.Metrics.Current;
        if (Model.Kind == WidgetKind.Clock)
        {
            Value = sample.Time.LocalDateTime.ToString(IsExpanded ? "HH:mm:ss" : "HH:mm");
            Detail = sample.Time.LocalDateTime.ToString("M월 d일", CultureInfo.GetCultureInfo("ko-KR"));
            Footer = sample.Time.LocalDateTime.ToString("dddd", CultureInfo.GetCultureInfo("ko-KR"));
        }
        else if (IsWeather)
        {
            Value = "22°";
            Detail = "구름 조금 · 최고 24° / 최저 16°";
            Footer = "습도 48% · 바람 2 m/s · 샘플 예보";
        }
        else
        {
            var value = Model.Kind switch { WidgetKind.Cpu => sample.Cpu, WidgetKind.Memory => sample.Memory, _ => _app.Metrics.Gpu.Usage };
            bool warming = Model.Kind == WidgetKind.Cpu && sample.CpuWarming || Model.Kind == WidgetKind.Gpu && _app.Metrics.Gpu.Warming;
            Value = value is { } v ? $"{v:F0}%" : warming ? "측정 중" : "—";
            Percentage = value ?? 0;
            Detail = Model.Kind == WidgetKind.Memory
                ? sample.TotalBytes > 0 ? $"{sample.UsedBytes / 1073741824.0:F1} / {sample.TotalBytes / 1073741824.0:F1} GB" : "메모리 측정 불가"
                : Model.Kind == WidgetKind.Gpu ? value.HasValue ? "" : warming ? "첫 측정 준비 중" : "GPU 카운터 측정 불가" : "PC 전체 프로세서";
            var points = History.Where(p => p.Value.HasValue).Select(p => p.Value!.Value).ToArray();
            Footer = points.Length > 0 ? $"60초 · 평균 {points.Average():F0}% / 최고 {points.Max():F0}%" : "최근 60초 · 데이터 수집 중";
        }
        foreach (var name in new[] { nameof(Value), nameof(Detail), nameof(Footer), nameof(Percentage), nameof(History), nameof(ValueFontSize), nameof(ShowDetails), nameof(CircleValueFontSize) }) Raise(name);
    }
}

public sealed record WeatherHour(string Time, string Temperature, string Icon);
