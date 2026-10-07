using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Nook.App.Services;

internal static class NativeWindows
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    public static (double X, double Y)? PointerPosition() => GetCursorPos(out var point) ? (point.X, point.Y) : null;
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr hdc, ref Rect bounds, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr rect, MonitorCallback callback, IntPtr data);

    public static IntPtr Handle(Window window) => new WindowInteropHelper(window).EnsureHandle();
    public static (ScreenRect Work, double Scale) WorkArea(Window window) => ReadMonitor(MonitorFromWindow(Handle(window), 2));
    public static (ScreenRect Work, double Scale) WorkArea(Rect rect) => ReadMonitor(MonitorFromRect(ref rect, 2));
    public static ScreenRect? Bounds(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r)
            ? new ScreenRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : null;
    }
    private static (ScreenRect Work, double Scale) ReadMonitor(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        GetDpiForMonitor(monitor, 0, out uint dpi, out _);
        return (new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top),
            (dpi == 0 ? 96 : dpi) / 96.0);
    }

    public static void MakeToolWindow(Window window)
    {
        var hwnd = Handle(window);
        var style = GetWindowLongPtr(hwnd, -20).ToInt64();
        SetWindowLongPtr(hwnd, -20, new IntPtr((style | 0x80) & ~0x40000)); // TOOLWINDOW, no APPWINDOW
    }

    public static void Restore(Window window, WidgetInstance model)
    {
        var rect = new Rect { Left = (int)model.Left, Top = (int)model.Top,
            Right = (int)(model.Left + model.Width), Bottom = (int)(model.Top + model.Height) };
        var (work, scale) = ReadMonitor(MonitorFromRect(ref rect, 2));
        var placed = Placement.Clamp(new(model.Left, model.Top, model.Width * scale, model.Height * scale), work);
        SetWindowPos(Handle(window), IntPtr.Zero, (int)placed.Left, (int)placed.Top, (int)placed.Width, (int)placed.Height, 0x14);
        Capture(window, model);
    }

    public static void Capture(Window window, WidgetInstance model)
    {
        // Shutdown has already destroyed HWNDs by the time Application.OnExit runs.
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return;
        double scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        model.Left = rect.Left; model.Top = rect.Top;
        model.Width = Math.Clamp((rect.Right - rect.Left) / scale, 180, 640);
        model.Height = Math.Clamp((rect.Bottom - rect.Top) / scale, 140, 480);
    }

    public static void Resize(Window window, string edge, double dx, double dy, bool circular = false)
    {
        var hwnd = Handle(window);
        GetWindowRect(hwnd, out var rect);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        if (circular)
        {
            var resized = WidgetShapes.ResizeCircle(new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top), edge, dx * scale, dy * scale, scale);
            SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(resized.Left), (int)Math.Round(resized.Top), (int)Math.Round(resized.Width), (int)Math.Round(resized.Height), 0x14);
            return;
        }
        double width = (rect.Right - rect.Left) / scale, height = (rect.Bottom - rect.Top) / scale;
        double newWidth = Math.Clamp(width + (edge.Contains('L') ? -dx : edge.Contains('R') ? dx : 0), 180, 640);
        double newHeight = Math.Clamp(height + (edge.Contains('T') ? -dy : edge.Contains('B') ? dy : 0), 140, 480);
        int left = edge.Contains('L') ? rect.Right - (int)Math.Round(newWidth * scale) : rect.Left;
        int top = edge.Contains('T') ? rect.Bottom - (int)Math.Round(newHeight * scale) : rect.Top;
        SetWindowPos(hwnd, IntPtr.Zero, left, top, (int)Math.Round(newWidth * scale), (int)Math.Round(newHeight * scale), 0x14);
    }

    public static IReadOnlyList<(ScreenRect Work, double Scale)> Screens()
    {
        var areas = new List<(ScreenRect, double)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Rect bounds, IntPtr data) =>
        { areas.Add(ReadMonitor(monitor)); return true; }, IntPtr.Zero);
        return areas;
    }
}
