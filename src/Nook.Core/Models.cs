using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Nook.Core;

public enum WidgetKind { Cpu, Memory, Clock, Gpu, Weather }
public enum Level { Low, Mid, High }
public enum ThemeMode { System, Light, Dark }
public enum WidgetShape { Card, Circle }
public enum LayoutMode { Small, Mid, Big }

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AppearanceOverride
{
    public Level? Radius { get; set; }
    public Level? Opacity { get; set; }
}

public sealed class WidgetInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WidgetKind Kind { get; set; }
    // Screen coordinates are physical pixels; content sizes are device-independent pixels.
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 200;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public Guid? GroupId { get; set; }
    // Null inherits the app theme; System follows Windows independently.
    public ThemeMode? Theme { get; set; }
    public WidgetShape Shape { get; set; } = WidgetShape.Card;
    public AppearanceOverride Appearance { get; set; } = new();
}

public static class WidgetShapes
{
    public static bool SupportsCircle(WidgetKind kind) => kind is WidgetKind.Cpu or WidgetKind.Memory or WidgetKind.Gpu;
    public static void Normalize(WidgetInstance widget)
    {
        if (!SupportsCircle(widget.Kind)) widget.Shape = WidgetShape.Card;
        if (widget.Shape == WidgetShape.Circle)
            widget.Width = widget.Height = Math.Clamp(widget.Width, 180, 480);
    }
    public static ScreenRect ResizeCircle(ScreenRect rect, string edge, double dx, double dy, double scale)
    {
        double horizontal = edge.Contains('L') ? -dx : edge.Contains('R') ? dx : 0;
        double vertical = edge.Contains('T') ? -dy : edge.Contains('B') ? dy : 0;
        double delta = Math.Abs(horizontal) >= Math.Abs(vertical) ? horizontal : vertical;
        double side = Math.Clamp(rect.Width + delta, 180 * scale, 480 * scale);
        double left = edge.Contains('L') ? rect.Right - side : edge.Contains('R') ? rect.Left : rect.Left + (rect.Width - side) / 2;
        double top = edge.Contains('T') ? rect.Bottom - side : edge.Contains('B') ? rect.Top : rect.Top + (rect.Height - side) / 2;
        return new(left, top, side, side);
    }
}

public sealed class WidgetGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "새 그룹";
    public AppearanceOverride Appearance { get; set; } = new();
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public Level Radius { get; set; } = Level.Mid;
    public Level Opacity { get; set; } = Level.High;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool AlwaysOnTop { get; set; }
    // Keep the v0.2/v0.3 JSON key so existing user preferences migrate.
    [System.Text.Json.Serialization.JsonPropertyName("AutoGrid")]
    public bool MagneticMode { get; set; }
    public List<WidgetInstance> Widgets { get; set; } = [];
    public List<WidgetGroup> Groups { get; set; } = [];
}

public readonly record struct ResolvedProperty(Level Value, string Source);
public readonly record struct ResolvedAppearance(ResolvedProperty Radius, ResolvedProperty Opacity)
{
    public double CornerRadius => Radius.Value switch { Level.Low => 8, Level.Mid => 16, _ => 24 };
    public double BackgroundOpacity => Opacity.Value switch { Level.Low => .65, Level.Mid => .82, _ => .96 };
}

public static class AppearanceResolver
{
    public static ThemeMode ResolveTheme(AppSettings settings, WidgetInstance widget) => widget.Theme ?? settings.Theme;
    public static ResolvedAppearance Resolve(AppSettings settings, WidgetInstance widget)
    {
        var group = settings.Groups.Find(g => g.Id == widget.GroupId);
        return new(ResolveProperty(widget.Appearance.Radius, group?.Appearance.Radius, settings.Radius, group?.Name),
            ResolveProperty(widget.Appearance.Opacity, group?.Appearance.Opacity, settings.Opacity, group?.Name));
    }

    public static ResolvedProperty ResolveProperty(Level? individual, Level? group, Level global, string? groupName) =>
        individual is { } w ? new(w, "개별 위젯") : group is { } g ? new(g, $"그룹: {groupName}") : new(global, "전체 기본값");

    public static void DeleteGroup(AppSettings settings, Guid groupId)
    {
        settings.Groups.RemoveAll(g => g.Id == groupId);
        foreach (var widget in settings.Widgets.Where(w => w.GroupId == groupId)) widget.GroupId = null;
    }

    public static void ResetAll(AppSettings settings)
    {
        foreach (var group in settings.Groups) group.Appearance = new();
        foreach (var widget in settings.Widgets) { widget.Appearance = new(); widget.Theme = null; }
    }
}

public static class ResponsiveLayout
{
    public static LayoutMode Resolve(double width, double height, LayoutMode current)
    {
        if (width >= 360 && height >= 260) return LayoutMode.Big;
        if (current == LayoutMode.Big && width >= 352 && height >= 252) return current;
        if (width >= 280 && height >= 180) return LayoutMode.Mid;
        if (current != LayoutMode.Small && width >= 272 && height >= 172) return LayoutMode.Mid;
        return LayoutMode.Small;
    }
}
