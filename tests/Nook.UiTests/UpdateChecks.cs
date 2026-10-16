using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    private static void RunUpdateChecks(string output, AppViewModel vm, AppSettings settings, ThemeService theme, ref int passed, ref int failed)
    {
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        foreach (var kind in new[] { WidgetKind.Battery, WidgetKind.Volume, WidgetKind.Brightness })
        foreach (var shape in Enum.GetValues<WidgetShape>())
        foreach (double side in new[] { 180.0, 300.0, 480.0 })
        {
            settings.Theme = mode; theme.Apply(mode);
            var model = new WidgetInstance { Kind = kind, Shape = shape, Width = side, Height = shape == WidgetShape.Circle ? side : 200 };
            var widget = new WidgetViewModel(vm, model); var window = new WidgetWindow(vm, widget);
            var root = (FrameworkElement)window.Content;
            try
            {
                var size = new Size(model.Width, model.Height); widget.SetSize(size.Width, size.Height); root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var level = AllChildren(root).OfType<LevelControl>().LastOrDefault(l => l.ActualWidth > 0);
                if (shape == WidgetShape.Circle && level is null) throw new Exception("Circular feature control missing");
                if (level is not null) { level.Available = true; level.Value = 63; level.Status = kind == WidgetKind.Battery ? "충전 중" : ""; }
                foreach (double scale in new[] { 1.0, 1.5, 2.0 })
                {
                    foreach (var control in AllChildren(root).OfType<LevelControl>()) { control.Available = true; control.Value = 63; control.Status = kind == WidgetKind.Battery ? "충전 중" : ""; control.InvalidateVisual(); }
                    root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(root);
                    byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    if (shape == WidgetShape.Circle && pixels[3] != 0) throw new Exception("Circle control painted a corner");
                    ValidateText(root, size);
                    if (scale == 1) { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, $"Control-{kind}-{mode}-{shape}-{side}.png")); png.Save(stream); }
                    passed++;
                }
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL control render {kind}/{mode}/{shape}/{side}: {ex}"); }
            finally { window.Close(); }
        }
        try
        {
            var knob = new LevelControl { Circular = true, Available = true, Value = 50 };
            var host = new Window { Width = 300, Height = 300, Content = knob }; host.Show(); Pump(10);
            double written = -1; knob.Adjusted += v => written = v;
            knob.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(knob), 0, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
            host.Close(); if (written != 51) throw new Exception("Knob keyboard input did not publish level"); passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL knob input: " + ex); }
        try
        {
            settings.Theme = ThemeMode.Light; theme.Apply(settings.Theme);
            var main = new MainWindow(vm); var root = (FrameworkElement)main.Content;
            ((Button)main.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            root.Measure(new Size(1084, 741)); root.Arrange(new Rect(0, 0, 1084, 741)); root.UpdateLayout();
            foreach (var check in AllChildren(root).OfType<ToggleButton>().Where(c => (c.IsVisible || c.ActualWidth > 0) && (c.Content is string || Equals(c.Style,main.FindResource("SwitchToggle")))))
            {
                check.IsChecked = true; check.ApplyTemplate(); check.UpdateLayout();
                if (check.Template.FindName("SwitchTrack",check) is not Border border || check.Template.FindName("SwitchThumb",check) is not Border thumb || border.ActualWidth < 45 || thumb.ActualWidth != 20)
                    throw new Exception("Toggle lost common switch proportions");
            }
            var png = new PngBitmapEncoder(); var bitmap = new RenderTargetBitmap(1084, 741, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, "Settings-Checks.png"))) png.Save(stream);
            main.Exiting = true; main.Close(); passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL checkbox bounds: " + ex); }
        try
        {
            int? volume = DesktopActions.Volume(); if (volume.HasValue) { DesktopActions.SetVolume(volume.Value); if (DesktopActions.Volume() != volume) throw new Exception("Master volume did not round-trip"); }
            var sharedRefresh = vm.RefreshDesktopStateAsync(); while (!sharedRefresh.IsCompleted) Pump(10); sharedRefresh.GetAwaiter().GetResult();
            if (vm.Volume != volume) throw new Exception("Shared desktop state did not refresh volume");
            var displays = BrightnessService.Read();
            File.WriteAllText(Path.Combine(output, "controls-live.txt"), $"Volume: {volume}\nBrightness displays: {string.Join("; ", displays.Select(d => d.Name + ":" + d.Percentage))}\nDND: {DoNotDisturbService.Read()}\nVolume written at its original value. Brightness discovery only.\n");
            passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL native level status: " + ex); }
    }
}
