using System.Text.Json;

namespace Nook.Core;

public sealed class LayoutPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "새 프리셋";
    public List<WidgetInstance> Widgets { get; set; } = [];
    public List<WidgetGroup> Groups { get; set; } = [];
    public Level Radius { get; set; }
    public OpacityLevel Opacity { get; set; }
    public ThemeMode Theme { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool MagneticMode { get; set; }
    public bool ShareMemoContents { get; set; }
}

public static partial class LayoutPresets
{
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    public static LayoutPreset Capture(AppSettings settings, string name)
    {
        SyncMemos(settings);
        var preset = new LayoutPreset { Name = name.Trim(), Widgets = Copy(settings.Widgets), Groups = Copy(settings.Groups),
            Radius = settings.Radius, Opacity = settings.Opacity, Theme = settings.Theme,
            AlwaysOnTop = settings.AlwaysOnTop, MagneticMode = settings.MagneticMode };
        foreach (var widget in preset.Widgets)
        { CountdownEngine.Reset(widget.Countdown); }
        return preset;
    }
    public static void SyncMemos(AppSettings settings)
    {
        var active = settings.Presets.Find(p => p.Id == settings.ActivePresetId);
        foreach (var memo in settings.Widgets.Where(w => w.Kind == WidgetKind.Memo))
        {
            if (active is null || active.ShareMemoContents) settings.MemoArchive[memo.Id] = memo.MemoText;
            if (active?.Widgets.Find(w => w.Id == memo.Id && w.Kind == WidgetKind.Memo) is { } saved) saved.MemoText = memo.MemoText;
        }
    }
    public static void Apply(AppSettings settings, LayoutPreset preset)
    {
        SyncMemos(settings);
        var timers = settings.Widgets.ToDictionary(w => w.Id, w => w.Countdown);
        settings.Widgets = Copy(preset.Widgets); settings.Groups = Copy(preset.Groups);
        settings.Radius = preset.Radius; settings.Opacity = preset.Opacity; settings.Theme = preset.Theme;
        settings.AlwaysOnTop = preset.AlwaysOnTop; settings.MagneticMode = preset.MagneticMode;
        settings.ActivePresetId = preset.Id;
        foreach (var widget in settings.Widgets)
        {
            WidgetShapes.Normalize(widget);
            if (widget.Kind == WidgetKind.Memo && preset.ShareMemoContents) widget.MemoText = settings.MemoArchive.GetValueOrDefault(widget.Id, widget.MemoText);
            if (timers.TryGetValue(widget.Id, out var timer)) widget.Countdown = timer;
        }
    }
}
