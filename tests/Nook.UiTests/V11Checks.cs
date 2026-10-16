using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Interop;
using Nook.Core;
using Nook.App.Services;
using Nook.App.Controls;
using Nook.App.ViewModels;
using Nook.App.Views;
using ThemeMode = Nook.Core.ThemeMode;
internal static partial class Program
{
    private static void RunV11Checks(string output, AppViewModel vm, AppSettings settings, ThemeService theme, ref int passed, ref int failed)
    {
        vm.Keys.Data.Total = 2316;
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        foreach (var kind in new[] { WidgetKind.Clock, WidgetKind.Pomodoro, WidgetKind.Timer, WidgetKind.Network })
        foreach (double side in new[] { 180.0, 220.0, 300.0, 480.0 })
        foreach (var clock in kind == WidgetKind.Clock ? new[] { ClockStyle.Text, ClockStyle.Analog } : new[] { ClockStyle.Text })
        {
            settings.Theme = mode; theme.Apply(mode); var widget = new WidgetViewModel(vm, new() { Kind = kind, Shape = WidgetShape.Circle, ClockStyle = clock, Width = side, Height = side }); var window = new WidgetWindow(vm, widget);
            try
            {
                widget.SetSize(side, side); var root = (FrameworkElement)window.Content; root.Measure(new(side, side)); root.Arrange(new(0, 0, side, side)); root.UpdateLayout(); ValidateText(root, new(side, side));
                var menu = (Button)window.FindName("CircleMenuButton"); var bounds = menu.TransformToAncestor(root).TransformBounds(new Rect(menu.RenderSize));
                if (Math.Abs(bounds.Left + bounds.Width / 2 - side / 2) > 1) throw new Exception("New circle menu not centered");
                SaveV10Image(root, Path.Combine(output, $"V11-Circle-{kind}-{clock}-{mode}-{side}.png"));
                foreach (double scale in new[] { 1.0, 1.5, 2.0 })
                {
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(side * scale), (int)(side * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(root); byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    if (pixels[3] != 0 || pixels[^1] != 0 || pixels.All(b => b == 0)) throw new Exception("New circle paints corners or is empty"); passed++;
                }
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL V11 circle {kind}/{clock}/{mode}/{side}: {ex}"); }
            finally { window.Close(); }
        }
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        foreach (var size in new[] { new Size(180, 140), new Size(300, 200), new Size(420, 310), new Size(640, 140), new Size(180, 480) })
        {
            var widget = new WidgetViewModel(vm, new() { Kind = WidgetKind.Clock, ClockStyle = ClockStyle.Analog, Theme = mode, Width = size.Width, Height = size.Height }); var window = new WidgetWindow(vm, widget);
            try
            { widget.SetSize(size.Width, size.Height); var root = (FrameworkElement)window.Content; root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); SaveV10Image(root, Path.Combine(output, $"V11-Analog-{mode}-{size.Width}x{size.Height}.png")); passed++; }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL analog card {mode}/{size}: {ex}"); }
            finally { window.Close(); }
        }
        try
        {
            var main = new MainWindow(vm); main.Show(); Pump(30);
            var host = (Panel)main.FindName("FeatureLibraryHost"); var search = (TextBox)main.FindName("LibrarySearch"); var categories = (Panel)main.FindName("LibraryCategoryHost");
            if (host.Children.Count != WidgetCatalog.All.Count) throw new Exception("Library omitted widgets");
            search.Text = "인터넷"; if (host.Children.Count != 1 || (WidgetKind)((Border)host.Children[0]).Tag != WidgetKind.Network) throw new Exception("Keyword search failed");
            categories.Children.OfType<Button>().Single(x => Equals(x.Tag, "일상")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (host.Children.Count != 0 || ((TextBlock)main.FindName("LibraryEmpty")).Visibility != Visibility.Visible) throw new Exception("Category and query did not combine");
            search.Text = ""; if (host.Children.Count != WidgetCatalog.Search("일상", "").Count()) throw new Exception("Category selection failed");
            categories.Children.OfType<Button>().Single(x => Equals(x.Tag, "전체")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (var kind in new[] { WidgetKind.Timer, WidgetKind.Pomodoro, WidgetKind.Clock })
            {
                var widget = vm.Add(kind); main.OpenWidgetEditor(widget); Pump(20); var card = ((Panel)main.FindName("WidgetsHost")).Children.OfType<ExpandableCard>().Single(x => x.IsExpanded);
                if (kind == WidgetKind.Clock)
                { var style = AllChildren(card.Editor!).OfType<ComboBox>().Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "시계 스타일"); style.SelectedIndex = 1; if (widget.Model.ClockStyle != ClockStyle.Analog) throw new Exception("Clock style settings failed"); }
                else
                { var field = AllChildren(card.Editor!).OfType<TextBox>().Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == (kind == WidgetKind.Timer ? "시간 · 초" : "집중 · 분")); field.Text = "42"; AllChildren(card.Editor!).OfType<Button>().Single(x => Equals(x.Content, "적용")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (kind == WidgetKind.Timer && widget.Model.Countdown.TimerSeconds != 42 || kind == WidgetKind.Pomodoro && widget.Model.Countdown.FocusMinutes != 42) throw new Exception("Duration settings failed"); }
                vm.Delete(widget);
            }
            ((Button)main.FindName("LibraryNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(30); SaveV10Image((FrameworkElement)main.Content, Path.Combine(output, "V11-Library.png")); main.Exiting = true; main.Close(); passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL categories/search/settings: " + ex); }
        foreach (var shape in new[] { WidgetShape.Card, WidgetShape.Circle })
        foreach (var clockStyle in new[] { ClockStyle.Text, ClockStyle.Analog })
        {
            var widget = new WidgetViewModel(vm, new() { Kind = WidgetKind.Clock, ClockStyle = clockStyle, Shape = shape, Width = 300, Height = 300 }); var window = new WidgetWindow(vm, widget); window.Show(); Pump(20);
            try
            {
                var clock = AllChildren(window).OfType<ClockVisual>().Single(x => x.IsVisible); var root = (FrameworkElement)window.Content;
                byte[] Snapshot()
                { var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(root); var data = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(data, image.PixelWidth * 4, 0); return data; }
                var time = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(9)); clock.Now = () => time; widget.RefreshMetrics(); Pump(30); var before = Snapshot();
                time = time.AddMinutes(30); widget.RefreshMetrics(); Pump(30); if (before.SequenceEqual(Snapshot())) throw new Exception("Clock did not refresh from shared metric tick"); passed++;
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL moving clock hands {shape}: {ex}"); }
            finally { window.Close(); }
        }
        foreach (var kind in new[] { WidgetKind.Timer, WidgetKind.Pomodoro })
        foreach (var shape in new[] { WidgetShape.Card, WidgetShape.Circle })
        {
            var widget = vm.Add(kind); widget.Model.Shape = shape; var window = new WidgetWindow(vm, widget); window.Show(); Pump(20);
            try
            {
                var controls = AllChildren(window).OfType<Button>().Where(x => x.IsVisible).ToArray();
                var play = controls.Single(x => Equals(x.ToolTip, "시작 / 일시정지")); play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (widget.Model.Countdown.Deadline is null) throw new Exception("Start failed");
                play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (widget.Model.Countdown.Deadline is not null || widget.Model.Countdown.RemainingSeconds <= 0) throw new Exception("Pause failed");
                controls.Single(x => Equals(x.ToolTip, "초기화")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (widget.Model.Countdown.RemainingSeconds != -1) throw new Exception("Reset failed");
                if (kind == WidgetKind.Pomodoro) { controls.Single(x => Equals(x.ToolTip, "다음 단계")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (widget.Model.Countdown.Phase != FocusPhase.ShortBreak) throw new Exception("Next phase failed"); }
                passed++;
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL Countdown UI {kind}/{shape}: {ex}"); }
            finally { window.Close(); vm.Delete(widget); }
        }
        try
        {
            var dice = new DiceVisual { Count = 3 }; var holder = new Window { Content = dice, Width = 300, Height = 200 }; holder.Show(); Pump(30); Motion.EnabledOverride = true; dice.Roll();
            if (!dice.Rolling) throw new Exception("Dice did not animate"); Pump(1050);
            if (dice.Rolling || dice.Values.Any(x => x is < 1 or > 6)) throw new Exception("Dice failed to settle");
            for (int i = 1; i <= 6; i++) if (DiceVisual.Pips(i).Count() != i) throw new Exception("Wrong dice face"); dice.Dispose(); holder.Close(); Motion.EnabledOverride = false; passed++;
        }
        catch (Exception ex) { Motion.EnabledOverride = false; failed++; Console.WriteLine("FAIL animated dice: " + ex); }
        try
        {
            // Isolated settings/windows. This test never injects mouse or keyboard input.
            var config = new AppSettings(); var store = new SettingsStore(Path.Combine(output, "multi-move")); using var own = new AppViewModel(config, store, vm.Metrics, theme); var main = new MainWindow(own); main.Show(); using var coordinator = new WindowCoordinator(own, main);
            var lead = own.Add(WidgetKind.Cpu); var peer = own.Add(WidgetKind.Memory); var locked = own.Add(WidgetKind.Clock); locked.Model.Locked = true;
            lead.Selected = peer.Selected = locked.Selected = true; Pump(30);
            var windows = (Dictionary<Guid, WidgetWindow>)typeof(WindowCoordinator).GetField("_windows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(coordinator)!;
            var before = V10Bounds(windows[peer.Model.Id]); var pinned = V10Bounds(windows[locked.Model.Id]); var origin = V10Bounds(windows[lead.Model.Id]);
            var start = (Delegate)typeof(WidgetWindow).GetField("DragStarting", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(windows[lead.Model.Id])!; start.DynamicInvoke();
            var move = (Delegate)typeof(WidgetWindow).GetField("DragUpdated", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(windows[lead.Model.Id])!;
            foreach (var delta in new[] { (31.0, 24.0), (-17.0, -9.0), (0.0, 0.0) })
            { move.DynamicInvoke(new ScreenRect(origin.Left + delta.Item1, origin.Top + delta.Item2, origin.Width, origin.Height)); Pump(15); var actual = V10Bounds(windows[peer.Model.Id]); if (Math.Abs(actual.Left - before.Left - delta.Item1) > 1 || Math.Abs(actual.Top - before.Top - delta.Item2) > 1 || actual.Width != before.Width || V10Bounds(windows[locked.Model.Id]) != pinned) throw new Exception("Multiple movement changed offsets, sizes or locked widget"); }
            ((Delegate)typeof(WidgetWindow).GetField("DragEnding", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(windows[lead.Model.Id])!).DynamicInvoke();
            own.ClearSelection(); if (own.Widgets.Any(x => x.Selected)) throw new Exception("Selection did not clear"); main.Exiting = true; main.Close(); passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL selection movement: " + ex); }
        try
        {
            var network = new NetworkService(); for (int i = 0; i < 5; i++) { network.Poll(true); Pump(550); }
            if (network.Current.Download is { } value && (value < 0 || !double.IsFinite(value))) throw new Exception("Invalid actual network rate");
            File.WriteAllText(Path.Combine(output, "network-live.txt"), network.Current.ToString());
            var read = vm.Bluetooth.RefreshAsync(true); var watch = System.Diagnostics.Stopwatch.StartNew(); while (!read.IsCompleted && watch.Elapsed.TotalSeconds < 15) Pump(20);
            if (!read.IsCompleted) throw new Exception("Bluetooth read did not finish within QA budget"); read.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "bluetooth-live.txt"), $"Radio={vm.Bluetooth.Enabled}; {vm.Bluetooth.Status}; Devices={vm.Bluetooth.Devices.Count}\n");
            File.WriteAllText(Path.Combine(output, "microphone-live.txt"), $"Default communication capture mute={DesktopActions.MicrophoneMuted()}\nRead-only; no state change.\n");
            passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL actual network/Bluetooth/microphone reads: " + ex); }
        foreach (var kind in new[] { WidgetKind.KeyStats, WidgetKind.Dice })
        foreach (var size in new[] { new Size(180, 140), new Size(300, 200), new Size(420, 310), new Size(640, 140), new Size(180, 480) })
        {
            var widget = new WidgetViewModel(vm, new() { Kind = kind, Width = size.Width, Height = size.Height }); var window = new WidgetWindow(vm, widget);
            try { widget.SetSize(size.Width, size.Height); var root = (FrameworkElement)window.Content; root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); ValidateText(root, size); SaveV10Image(root, Path.Combine(output, $"V11-Populated-{kind}-{size.Width}x{size.Height}.png")); passed++; }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL populated {kind}/{size}: {ex}"); }
            finally { window.Close(); }
        }
        var previousDevices = vm.Bluetooth.Devices; var previousState = vm.Bluetooth.Enabled; var previousStatus = vm.Bluetooth.Status;
        try
        {
            typeof(BluetoothService).GetProperty("Devices")!.SetValue(vm.Bluetooth, new BluetoothItem[] { new("a", "Headphones", true, 82), new("b", "아주 긴 이름을 가진 무선 키보드", true, null), new("c", "Mouse", false, null) });
            typeof(BluetoothService).GetProperty("Enabled")!.SetValue(vm.Bluetooth, (bool?)true); typeof(BluetoothService).GetProperty("Status")!.SetValue(vm.Bluetooth, "ON");
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
            foreach (var size in new[] { new Size(180, 140), new Size(300, 200), new Size(420, 310), new Size(640, 140), new Size(180, 480) })
            {
                var widget = new WidgetViewModel(vm, new() { Kind = WidgetKind.Bluetooth, Theme = mode, Width = size.Width, Height = size.Height }); var window = new WidgetWindow(vm, widget);
                try
                {
                    widget.SetSize(size.Width, size.Height); var root = (FrameworkElement)window.Content; root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                    if (!AllText(root).Any(x => x.Text == "82%") || !AllText(root).Any(x => x.Text == "—")) throw new Exception("Bluetooth battery/unknown rendering omitted");
                    var toggle = AllChildren(root).OfType<ToggleButton>().Single(); if (!toggle.IsEnabled || toggle.IsChecked != true) throw new Exception("Bluetooth switch state wrong");
                    SaveV10Image(root, Path.Combine(output, $"V11-BluetoothFixture-{mode}-{size.Width}x{size.Height}.png")); passed++;
                }
                catch (Exception ex) { failed++; Console.WriteLine($"FAIL Bluetooth fixture {mode}/{size}: {ex}"); }
                finally { window.Close(); }
            }
        }
        finally
        { typeof(BluetoothService).GetProperty("Devices")!.SetValue(vm.Bluetooth, previousDevices); typeof(BluetoothService).GetProperty("Enabled")!.SetValue(vm.Bluetooth, previousState); typeof(BluetoothService).GetProperty("Status")!.SetValue(vm.Bluetooth, previousStatus); }
    }
}
