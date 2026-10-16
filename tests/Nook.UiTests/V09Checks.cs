using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using Nook.Core;
using Nook.App.Controls;
using Nook.App.Services;
using Nook.App.ViewModels;
using Nook.App.Views;
using ThemeMode = Nook.Core.ThemeMode;

internal static partial class Program
{
    private static void RunV09Checks(string output, AppViewModel vm, AppSettings settings, ThemeService theme, ref int passed, ref int failed)
    {
        // Populate only this test's isolated local ledger. Shipping widgets start empty.
        var day = new UsageDay { Date = DateOnly.FromDateTime(DateTime.Now), Apps = [
            new() { Id = "chrome", Name = "Google Chrome", Color = "#DBB641", Seconds = 7200 },
            new() { Id = "code", Name = "Visual Studio Code", Color = "#408DC1", Seconds = 5400 },
            new() { Id = "spotify", Name = "Spotify", Color = "#62A475", Seconds = 2700 },
            new() { Id = "a", Name = "작은 앱", Color = "#BA927C", Seconds = 300 },
            new() { Id = "b", Name = "다른 앱", Color = "#BA927C", Seconds = 200 }] };
        vm.ScreenTime.Ledger.Days.Clear(); vm.ScreenTime.Ledger.Days.Add(day);
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        foreach (var kind in new[] { WidgetKind.ScreenTime, WidgetKind.Disk })
        foreach (var size in new[] { new Size(180, 140), new Size(300, 200), new Size(360, 260), new Size(420, 310), new Size(640, 140), new Size(180, 480) })
        {
            settings.Theme = mode; theme.Apply(mode);
            var widget = new WidgetViewModel(vm, new() { Kind = kind, Width = size.Width, Height = size.Height });
            var window = new WidgetWindow(vm, widget); var root = (FrameworkElement)window.Content;
            try
            {
                widget.SetSize(size.Width, size.Height); root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                ValidateText(root, size);
                if (kind == WidgetKind.ScreenTime)
                {
                    var bar = AllChildren((FrameworkElement)((Border)window.FindName("FeatureHost")).Child).OfType<UsageBar>().Single();
                    if (bar.Shares.Count != 4 || bar.Shares[^1].Name != "ETC") throw new Exception("App aggregation missing from bar");
                    if (bar.ShareAt(bar.ActualWidth * .15)?.Id != "chrome" || bar.ShareAt(bar.ActualWidth * .99)?.Id != "etc") throw new Exception("Hover did not identify painted segment");
                }
                foreach (double scale in new[] { 1.0, 1.5, 2.0 })
                {
                    var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(root);
                    if (scale == 1) { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, $"V09-{kind}-{mode}-{size.Width}x{size.Height}.png")); png.Save(stream); }
                    passed++;
                }
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL new widget {kind}/{mode}/{size}: {ex}"); }
            finally { window.Close(); }
        }
        foreach (var kind in Enum.GetValues<WidgetKind>().Where(WidgetShapes.SupportsCircle))
        foreach (double side in new[] { 180.0, 300.0, 480.0 })
        {
            var widget = new WidgetViewModel(vm, new() { Kind = kind, Shape = WidgetShape.Circle, Width = side, Height = side });
            var window = new WidgetWindow(vm, widget); var root = (FrameworkElement)window.Content;
            try
            {
                widget.SetSize(side, side); root.Measure(new(side, side)); root.Arrange(new Rect(0, 0, side, side)); root.UpdateLayout();
                var menu = (Button)window.FindName("CircleMenuButton"); menu.Opacity = 1;
                var bounds = menu.TransformToAncestor(root).TransformBounds(new Rect(menu.RenderSize));
                var title = AllChildren(root).OfType<StackPanel>().Single(x => Equals(x.Tag, "title"));
                var titleBounds = title.TransformToAncestor(root).TransformBounds(new Rect(title.RenderSize));
                if (Math.Abs(bounds.Left + bounds.Width / 2 - side / 2) > 1 || bounds.Top < 18 || title.Visibility == Visibility.Visible && bounds.Bottom > titleBounds.Top + 1) throw new Exception("Menu is not centered above title or overlaps resize grip");
                passed++;
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL circle menu {kind}/{side}: {ex}"); }
            finally { window.Close(); }
        }
        try
        {
            var main = new MainWindow(vm);
            foreach (var kind in new[] { WidgetKind.Volume, WidgetKind.Brightness })
            {
                var widget = vm.Add(kind); main.OpenWidgetEditor(widget); Pump(20);
                var editor = ((StackPanel)main.FindName("WidgetsHost")).Children.OfType<ExpandableCard>().Single(x => x.IsExpanded).Editor!;
                var shape = AllChildren(editor).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "모양");
                var orientation = AllChildren(editor).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "슬라이더 방향");
                orientation.SelectedIndex = 2; shape.SelectedIndex = 1; Pump(20);
                if (orientation.IsEnabled || widget.Model.ControlOrientation != ControlOrientation.Vertical) throw new Exception("Circle orientation not disabled or previous card direction lost");
                shape.SelectedIndex = 0; Pump(20);
                if (!orientation.IsEnabled || orientation.SelectedIndex != 2) throw new Exception("Card did not restore direction");
            }
            var usage = vm.Add(WidgetKind.ScreenTime); main.OpenWidgetEditor(usage); Pump(20);
            var card = ((StackPanel)main.FindName("WidgetsHost")).Children.OfType<ExpandableCard>().Single(x => x.IsExpanded);
            var threshold = AllChildren(card.Editor!).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "ETC 기준"); threshold.SelectedIndex = 2;
            if (usage.Model.ScreenTimeEtcThreshold != 5) throw new Exception("ETC preference not applied");
            main.Exiting = true; main.Close(); passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL knob and usage settings: " + ex); }
        try
        {
            string directory = Path.Combine(output, "usage-store");
            using (var tracker = new ScreenTimeService(directory)) { tracker.Ledger.Days.Add(day); tracker.Save(); }
            using (var restored = new ScreenTimeService(directory)) if (restored.Today.TotalSeconds != day.TotalSeconds) throw new Exception("Usage did not restore");
            File.WriteAllText(Path.Combine(directory, "screen-time.json"), "{\"Version\":1,\"Days\":[null]}");
            using (var corrupted = new ScreenTimeService(directory)) { if (corrupted.Today.TotalSeconds != 0 || corrupted.Warning is null) throw new Exception("Corrupt usage not isolated"); corrupted.Save(); }
            if (!Directory.GetFiles(directory, "*.invalid-*").Any()) throw new Exception("Corrupt usage not backed up");
            if (AppIconColor.Fallback("chrome") != AppIconColor.Extract(Path.Combine(output, "missing.exe"), "chrome")) throw new Exception("Icon fallback unstable");
            passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL usage persistence/color: " + ex); }
        try
        {
            using var counter = new DiskCounter(); var first = counter.Sample();
            File.WriteAllBytes(Path.Combine(output, "disk-probe.bin"), new byte[4 * 1024 * 1024]); Thread.Sleep(1050); var reading = counter.Sample();
            if (!first.Warming || reading.Read is null || reading.Write is null || reading.Read < 0 || reading.Write < 0) throw new Exception("Real PhysicalDisk counter did not return valid speeds");
            if (DiskCounter.Speed(1048576) != "1.0 MB/s" || DiskCounter.Speed(null) != "—") throw new Exception("Speed formatting failed");
            File.WriteAllText(Path.Combine(output, "disk-live.txt"), $"First: {first}\nSecond: {reading}\n"); passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL disk native measurement: " + ex); }
        vm.ScreenTime.Ledger.Days.Clear();
    }
}
