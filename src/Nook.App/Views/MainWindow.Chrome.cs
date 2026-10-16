using System.Windows.Shell;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Nook.App.Views;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct CaptionPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CaptionLimits { public CaptionPoint Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    private HwndSource? _chromeSource;
    private void InitializeChrome()
    {
        _chromeSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _chromeSource?.AddHook(ChromeMessage);
    }
    private IntPtr ChromeMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0084 && WindowState == WindowState.Normal)
        {
            long packed = lParam.ToInt64(); var point = PointFromScreen(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
            int hit = ChromeHitTest(new Size(ActualWidth, ActualHeight), point);
            if (hit != 0) { handled = true; return new IntPtr(hit); }
        }
        if (message != 0x0024 || lParam == IntPtr.Zero) return IntPtr.Zero;
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        var monitor = screen.Bounds; var work = screen.WorkingArea;
        var limits = Marshal.PtrToStructure<CaptionLimits>(lParam);
        limits.MaxPosition = new() { X = work.Left - monitor.Left, Y = work.Top - monitor.Top };
        limits.MaxSize = new() { X = work.Width, Y = work.Height };
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        limits.MinTrack = new() { X = (int)Math.Ceiling(MinWidth * scale), Y = (int)Math.Ceiling(MinHeight * scale) };
        Marshal.StructureToPtr(limits, lParam, false); handled = true; return IntPtr.Zero;
    }
    public static int ChromeHitTest(Size size, Point point)
    {
        double left = point.X, top = point.Y, right = size.Width - point.X, bottom = size.Height - point.Y;
        if (left < 0 || top < 0 || right < 0 || bottom < 0) return 0;
        const double edge = 9, corner = 28;
        bool l = left <= edge, r = right <= edge, t = top <= edge, b = bottom <= edge;
        if (l && top <= corner || t && left <= corner) return 13;
        if (r && top <= corner || t && right <= corner) return 14;
        if (l && bottom <= corner || b && left <= corner) return 16;
        if (r && bottom <= corner || b && right <= corner) return 17;
        return l ? 10 : r ? 11 : t ? 12 : b ? 15 : 0;
    }
    private void ApplyMainPalette()
    {
        foreach (var (key, value) in Services.ThemeService.Palette(_vm.Theme.IsDark))
        {
            var color = value;
            color.A = key switch { "PageBrush" => 237, "SidebarBrush" => 212, "SurfaceBrush" => 220, "SoftAccentBrush" => 230, _ => 255 };
            Resources[key] = new SolidColorBrush(color);
        }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e)
    { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Frame_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateFrame();
    private void UpdateFrame()
    {
        if (MainShell is null || MainFrame is null) return;
        bool maximized = WindowState == WindowState.Maximized;
        MainFrame.Margin = maximized ? new(0) : new(6); MainFrame.CornerRadius = new(maximized ? 0 : 14);
        MainFrame.Effect = maximized ? null : new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = .14, BlurRadius = 8, ShadowDepth = 2 };
        MainShell.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, MainShell.ActualWidth), Math.Max(0, MainShell.ActualHeight)), maximized ? 0 : 13, maximized ? 0 : 13);
        MaximizeIcon.Glyph = maximized ? "square_multiple" : "square";
        MaximizeButton.ToolTip = maximized ? "이전 크기로 복원" : "최대화";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "이전 크기로 복원" : "최대화");
    }
}
