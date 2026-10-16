using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nook.App.Controls;
using Nook.App.Services;
using Nook.App.ViewModels;
using Nook.App.Views;
using Nook.Core;
using ThemeMode = Nook.Core.ThemeMode;

internal static partial class Program
{
    private static void RunV10Checks(string output, AppViewModel vm, AppSettings settings, ThemeService theme, ref int passed, ref int failed)
    {
        foreach (double side in new[] { 180.0, 300.0, 480.0 })
        {
            try
            {
                var level = new LevelControl { Circular = true, Available = true };
                level.Measure(new(side, side)); level.Arrange(new(0, 0, side, side));
                var center = new Point(side / 2, side * .5);
                double radius = side * .4;
                if (level.IsAdjustmentPoint(center) || level.IsAdjustmentPoint(new(1, 1)) || !level.IsAdjustmentPoint(new(center.X + radius, center.Y)))
                    throw new Exception("Center/background must permit drag; ring must permit adjustment");
                level.Available = false;
                if (level.IsAdjustmentPoint(new(center.X + radius, center.Y))) throw new Exception("Unavailable control intercepted movement");
                passed++;
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL circular input {side}: {ex}"); }
        }
        foreach (var kind in new[] { WidgetKind.Volume, WidgetKind.Brightness, WidgetKind.Cpu, WidgetKind.AirQuality })
        {
            var widget = new WidgetViewModel(vm, new() { Kind = kind, Shape = WidgetShape.Circle, Width = 300, Height = 300 });
            var window = new WidgetWindow(vm, widget);
            try
            {
                Motion.EnabledOverride = true; window.ShowAnimated(); Pump(50);
                var root = (FrameworkElement)window.Content; var surface = (Border)window.FindName("Surface");
                foreach (bool magnetic in new[] { true, false, true, false })
                {
                    vm.MagneticMode = magnetic; window.ResizeAnimated(420, 420); Pump(45);
                    var bounds = surface.TransformToAncestor(root).TransformBounds(new Rect(surface.RenderSize));
                    if (Math.Abs(bounds.Width - bounds.Height) > .1 || bounds.Bottom > root.ActualHeight - 4 || bounds.Top < 4)
                        throw new Exception($"Circle escaped native bounds during motion: {bounds}/{root.RenderSize}");
                    if (Math.Abs(surface.RenderTransform.Value.OffsetY) > .01) throw new Exception("Entrance translated whole circle outside HWND");
                    var native = V10Bounds(window);
                    var moved = SendMoving(new WindowInteropHelper(window).Handle, new NativeRect { Left = (int)native.Left + 1, Top = (int)native.Top + 1, Right = (int)(native.Left + native.Width) + 1, Bottom = (int)(native.Top + native.Height) + 1 });
                    if (moved.Right - moved.Left != (int)native.Width || moved.Bottom - moved.Top != (int)native.Height) throw new Exception("Magnetic mode changed dimensions");
                    passed++;
                }
                Pump(300); window.Width = 300; window.Height = 220; Pump(30);
                if (Math.Abs(surface.ActualWidth - surface.ActualHeight) > .1) throw new Exception("Intermediate rectangular HWND distorted circle");
                passed++;
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL circle motion {kind}: {ex}"); }
            finally { window.Close(); Motion.EnabledOverride = false; vm.MagneticMode = false; }
        }
        string executable = Path.GetFullPath(".tools/dotnet/dotnet.exe");
        var icons = new AppIconStore(Path.Combine(output, "icon-store"));
        try
        {
            var capture = icons.CaptureAsync("dotnet", executable);
            while (!capture.IsCompleted) Pump(10);
            capture.GetAwaiter().GetResult();
            if (icons.Get("dotnet") is not BitmapSource { IsFrozen: true } || icons.Version < 1) throw new Exception("Actual Shell icon unavailable");
            var restored = new AppIconStore(Path.Combine(output, "icon-store"));
            restored.Get("dotnet"); int attempts = 0;
            while (restored.Get("dotnet") is null && attempts++ < 100) Pump(10);
            if (restored.Get("dotnet") is not BitmapSource { IsFrozen: true }) throw new Exception("Icon cache did not restore");
            if (Directory.GetFiles(Path.Combine(output, "icon-store", "icons")).Any(x => !Path.GetFileName(x).EndsWith(".png") || Path.GetFileNameWithoutExtension(x).Length != 32)) throw new Exception("Cache leaked paths or temporary file");
            passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL Windows icon cache: " + ex); }
        try
        {
            var shelfVm = vm.Add(WidgetKind.ProgramShelf); var model = shelfVm.Model;
            var window = new WidgetWindow(vm, shelfVm); window.Show(); Pump(20);
            var view = AllChildren(window).OfType<WidgetFeaturesView>().Single();
            if (AllChildren(view).OfType<Button>().Any()) throw new Exception("Inline add button remains");
            view.AddPaths([executable, Path.GetDirectoryName(executable)!, executable]); Pump(150);
            // Feature views rebuild after model changes.
            view = AllChildren(window).OfType<WidgetFeaturesView>().Single();
            int attempts = 0; while (!AllChildren(view).OfType<Image>().Any(x => x.Source is not null) && attempts++ < 100) Pump(10);
            var appButton = AllChildren(view).OfType<Button>().Single(x => Equals(x.ToolTip, "dotnet\n" + executable));
            if (model.Shelf.Count != 2 || !view.AllowDrop || !AllChildren(appButton).OfType<Image>().Any(x => x.Source is BitmapSource { IsFrozen: true })) throw new Exception("Program Shelf omitted actual app icon or duplicate/drop rules");
            if (AllChildren(view).OfType<Button>().Any(x => x.BorderThickness != new Thickness(0))) throw new Exception("Program Shelf app border remains");
            var menu = (Button)window.FindName("MenuButton"); menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(20);
            var headers = menu.ContextMenu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).ToArray();
            if (!headers.Contains("프로그램 / 파일 추가") || !headers.Contains("폴더 추가")) throw new Exception("Add entries absent from ellipsis menu");
            menu.ContextMenu.IsOpen = false; SaveV10Image((FrameworkElement)window.Content, Path.Combine(output, "V10-Shelf.png"));
            window.Close(); vm.Delete(shelfVm); passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL Shelf icons/menu: " + ex); }
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            try
            {
                theme.Apply(mode); var widget = new WidgetViewModel(vm, new() { Kind = WidgetKind.ScreenTime, Theme = mode });
                var share = new UsageShare("dotnet", ".NET", "#512BD4", 3600, 82);
                var etc = new UsageShare("etc", "ETC", "#91969B", 900, 18, Members: [share with { Percentage = 12 }]);
                var tag = UsageTag.Build(etc, widget, icons);
                var tip = new ToolTip { Content = tag };
                foreach (var (key, value) in ThemeService.Palette(widget.IsDark)) tip.Resources[key] = new SolidColorBrush(value);
                tip.Measure(new(330, 300)); tip.Arrange(new Rect(tip.DesiredSize)); tip.UpdateLayout();
                if (!AllChildren(tag).OfType<Image>().Any(x => x.Source is not null) || !AllText(tag).Any(x => x.Text == ".NET")) throw new Exception("ETC tag omitted real app icon/name");
                if (!AllChildren(tip).OfType<Border>().Any(x => x.CornerRadius.TopLeft == 12 && x.BorderThickness.Left == 1)) throw new Exception("Tag lost app tooltip style");
                SaveV10Image(tip, Path.Combine(output, $"V10-UsageTag-{mode}.png")); passed++;
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL Usage tag {mode}: {ex}"); }
        }
        foreach (var size in new[] { new Size(132, 86), new Size(230, 128), new Size(264, 146), new Size(384, 256), new Size(132, 426), new Size(604, 86), new Size(250, 205), new Size(410, 205) })
        {
            try
            {
                var panel = new MediaLayoutPanel();
                foreach (string name in new[] { "cover", "title", "artist", "controls", "progress", "time" }) panel.Children.Add(new Border { Tag = name });
                panel.Measure(size); panel.Arrange(new Rect(size)); panel.UpdateLayout();
                Rect Bounds(string name) { var e = panel.Children.Cast<FrameworkElement>().Single(x => Equals(x.Tag, name)); return e.TransformToAncestor(panel).TransformBounds(new Rect(e.RenderSize)); }
                var controls = Bounds("controls"); var progress = Bounds("progress"); var artist = Bounds("artist");
                if (controls.Bottom > size.Height || controls.Top < 0 || artist.Height > 0 && controls.Top < artist.Bottom) throw new Exception("Controls overlap metadata or exceed panel");
                if (progress.Height > 0 && Math.Abs(progress.Top - controls.Bottom - 10) > .1) throw new Exception("Controls are detached from progress bar");
                passed++;
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL Media controls {size}: {ex}"); }
        }
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            MainWindow? main = null;
            try
            {
                theme.Apply(mode); main = new MainWindow(vm); main.Show(); Pump(40);
                if (((SolidColorBrush)main.FindResource("PageBrush")).Color.A >= 255 || ((SolidColorBrush)main.FindResource("TextBrush")).Color.A != 255 || !main.AllowsTransparency) throw new Exception("Frame lacks background-only transparency");
                main.Width = 960; main.Height = 700; Pump(40);
                var shell = (Grid)main.FindName("MainShell");
                if (shell.Clip is not RectangleGeometry clip || Math.Abs(clip.Rect.Width - shell.ActualWidth) > .1 || Math.Abs(clip.Rect.Height - shell.ActualHeight) > .1) throw new Exception("Rounded clip did not follow resize");
                ((Button)main.FindName("MaximizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(80);
                if (main.WindowState != WindowState.Maximized || ((FluentIcon)main.FindName("MaximizeIcon")).Glyph != "square_multiple") throw new Exception("Maximize failed");
                var bounds = V10Bounds(main); var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(main).Handle).WorkingArea;
                var work = new ScreenRect(area.Left, area.Top, area.Width, area.Height);
                if (Math.Abs(bounds.Left - work.Left) > 2 || Math.Abs(bounds.Top - work.Top) > 2 || Math.Abs(bounds.Height - work.Height) > 2) throw new Exception($"Maximized frame cropped or covered taskbar: {bounds}/{work}");
                ((Button)main.FindName("MaximizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(60);
                if (main.WindowState != WindowState.Normal) throw new Exception("Restore failed");
                ((Button)main.FindName("MinimizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(40);
                if (main.WindowState != WindowState.Minimized) throw new Exception("Minimize failed");
                main.BringForward(); Pump(30); ((Button)main.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(220);
                if (main.IsVisible) throw new Exception("Close did not hide to tray");
                main.BringForward(); Pump(40); if (!main.IsVisible) throw new Exception("Tray restore failed");
                SaveV10Image((FrameworkElement)main.Content, Path.Combine(output, $"V10-Main-{mode}.png")); passed++;
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL main chrome {mode}: {ex}"); }
            finally { if (main is not null) { main.Exiting = true; main.Close(); } }
        }
    }
    private static void SaveV10Image(FrameworkElement root, string path)
    {
        var image = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(root.ActualWidth)), Math.Max(1, (int)Math.Ceiling(root.ActualHeight)), 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); png.Save(stream);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    private static ScreenRect V10Bounds(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var r)) throw new Exception("HWND unavailable");
        return new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}
