namespace Nook.Core;

public static class WidgetLimits
{
    public static double MaxWidth(WidgetKind kind) => kind switch { WidgetKind.Memo => 1200, WidgetKind.Dice => 420, WidgetKind.KeyStats => 220, _ => 640 };
    public static double MaxHeight(WidgetKind kind) => kind switch { WidgetKind.Memo => 1000, WidgetKind.Dice => 420, WidgetKind.KeyStats => 160, _ => 480 };
    public static double MinWidth(WidgetKind kind) => kind == WidgetKind.Calculator ? 240 : 180;
    public static double MinHeight(WidgetKind kind) => kind == WidgetKind.Calculator ? 300 : 140;
    public static void Normalize(WidgetInstance widget)
    {
        widget.Width = Math.Clamp(widget.Width, MinWidth(widget.Kind), MaxWidth(widget.Kind));
        widget.Height = Math.Clamp(widget.Height, MinHeight(widget.Kind), MaxHeight(widget.Kind));
        if (widget.Kind == WidgetKind.Dice)
        { var size = DiceSize(widget.Width, widget.Height, 1); widget.Width = size.Width; widget.Height = size.Height; }
    }
    private static (double Width, double Height) DiceSize(double width, double height, double scale)
    {
        width = Math.Clamp(width / scale, 180, 420); height = Math.Clamp(height / scale, 140, 420);
        if (width >= height) height = Math.Clamp(height, width / 2, 280);
        else width = Math.Clamp(width, height / 2, 280);
        return (width * scale, height * scale);
    }
    public static ScreenRect ConstrainDice(ScreenRect rect, string edge, double scale)
    {
        var (width, height) = DiceSize(rect.Width, rect.Height, scale);
        return new(edge.Contains('L') ? rect.Right - width : edge.Contains('R') ? rect.Left : rect.Left + (rect.Width - width) / 2,
            edge.Contains('T') ? rect.Bottom - height : edge.Contains('B') ? rect.Top : rect.Top + (rect.Height - height) / 2, width, height);
    }
    public static ScreenRect ResizeDice(ScreenRect origin, string edge, double dx, double dy, double scale)
    {
        double width = origin.Width + (edge.Contains('L') ? -dx : edge.Contains('R') ? dx : 0);
        double height = origin.Height + (edge.Contains('T') ? -dy : edge.Contains('B') ? dy : 0);
        var size = DiceSize(width, height, scale);
        return new(edge.Contains('L') ? origin.Right - size.Width : edge.Contains('R') ? origin.Left : origin.Left + (origin.Width - size.Width) / 2,
            edge.Contains('T') ? origin.Bottom - size.Height : edge.Contains('B') ? origin.Top : origin.Top + (origin.Height - size.Height) / 2, size.Width, size.Height);
    }
}
