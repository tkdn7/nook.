using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Nook.Core;
using Nook.App.Services;
using Nook.App.ViewModels;
using Nook.App.Views;
using Nook.App.Controls;
using System.Diagnostics;
using ThemeMode = Nook.Core.ThemeMode;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/qa");
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Motion.EnabledOverride = false;
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Nook;component/Themes/Controls.xaml", UriKind.Relative) });
        app.Resources.Add("BoolVisibility", new BooleanToVisibilityConverter());
        using var theme = new ThemeService();
        using var metrics = new SystemMetricsService(Dispatcher.CurrentDispatcher);
        var settings = new AppSettings();
        var vm = new AppViewModel(settings, new SettingsStore(Path.Combine(output, "settings")), metrics, theme);
        int passed = 0, failed = 0;
        try
        {
            foreach (string key in new[] { "BodyFont", "NumberFont" })
            {
                var family = (FontFamily)app.FindResource(key);
                var typeface = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                if (!typeface.TryGetGlyphTypeface(out var glyph) || !glyph.FontUri.OriginalString.Contains("Nook;component"))
                    throw new Exception($"Bundled font unavailable: {key}; " + string.Join("; ", Fonts.GetFontFamilies(new Uri("pack://application:,,,/Nook;component/Assets/Fonts/")).Select(f => f.Source + " / " + string.Join(",", f.FamilyNames.Values))));
            }
            passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL bundled fonts: " + ex); }
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        foreach (var kind in Enum.GetValues<WidgetKind>())
        foreach (var size in new[] { new Size(180, 140), new Size(300, 200), new Size(420, 310), new Size(640, 140), new Size(180, 480) })
        {
            settings.Theme = mode; theme.Apply(mode);
            var model = new WidgetInstance { Kind = kind, Width = size.Width, Height = size.Height };
            var widget = new WidgetViewModel(vm, model);
            var window = new WidgetWindow(vm, widget);
            var root = (FrameworkElement)window.Content;
            try
            {
                widget.SetSize(size.Width, size.Height);
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                if (widget.Layout != ResponsiveLayout.Resolve(size.Width, size.Height, LayoutMode.Small)) throw new Exception("Wrong responsive layout");
                bool isMetric = kind is WidgetKind.Cpu or WidgetKind.Memory or WidgetKind.Gpu;
                if (widget.ShowBar != (isMetric && widget.Layout == LayoutMode.Mid)
                    || widget.ShowGraph != (isMetric && widget.Layout == LayoutMode.Big)
                    || widget.ShowForecast != (kind == WidgetKind.Weather && widget.Layout == LayoutMode.Big))
                    throw new Exception("Wrong information density for Small/Mid/Big");
                if (kind == WidgetKind.Weather && (!widget.Title.Contains("데모") || !widget.WeatherLocation.Contains("데모")))
                    throw new Exception("Weather must always identify demo data");
                if (AllText(root).Any(t => t.Text is "Mid" or "Big" || t.Text.Contains("가장 바쁜 엔진")))
                    throw new Exception("Size badge or GPU engine text is still visible");
                foreach (double scale in new[] { 1.0, 1.5, 2.0 })
                {
                    var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    if (pixels.All(b => b == 0)) throw new Exception("Empty render");
                    ValidateText(root, size);
                    if (scale == 1.0)
                    {
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(output, $"{mode}-{kind}-{size.Width}x{size.Height}.png")); png.Save(stream);
                    }
                    passed++;
                }
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL {mode} {kind} {size}: {ex}"); }
            finally { window.Close(); }
        }
        // Exercise dynamic editor construction and all three main pages through actual routed events.
        try
        {
            var main = new MainWindow(vm); var root = (FrameworkElement)main.Content;
            root.Measure(new Size(1084, 741)); root.Arrange(new Rect(0, 0, 1084, 741)); root.UpdateLayout();
            foreach (var name in new[] { "ManageNav", "SettingsNav", "LibraryNav" })
                ((Button)main.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            passed++;
            var toggle = (ToggleButton)main.FindName("MagneticModeToggle");
            if (!((FrameworkElement)main.FindName("SettingsPage")).IsAncestorOf(toggle)
                || ((FrameworkElement)main.FindName("LibraryPage")).IsAncestorOf(toggle))
                throw new Exception("Grid switch must belong exclusively to settings");
            ((Button)main.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            toggle.IsChecked = true;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (!vm.MagneticMode || !settings.MagneticMode) throw new Exception("Settings switch did not enable snapping");
            vm.MagneticMode = false;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (toggle.IsChecked != false) throw new Exception("Grid toggle did not update from view model");
            passed++;
            foreach (var themeMode in new[] { ThemeMode.Light, ThemeMode.Dark })
            foreach (var mainSize in new[] { new Size(884, 601), new Size(1084, 741) })
            {
                theme.Apply(themeMode);
                ((Button)main.FindName("LibraryNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                root.Measure(mainSize); root.Arrange(new Rect(mainSize)); root.UpdateLayout();
                var addButton = (Button)main.FindName("AddDemoButton");
                var buttonText = FindText(addButton);
                if (buttonText is null || ((SolidColorBrush)buttonText.Foreground).Color != ((SolidColorBrush)app.FindResource("AccentTextBrush")).Color)
                    throw new Exception("Primary button text lost inverse contrast");
                var bitmap = new RenderTargetBitmap((int)mainSize.Width, (int)mainSize.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, $"Main-{themeMode}-{mainSize.Width}x{mainSize.Height}.png")); png.Save(stream);
                passed++;
            }
            main.Exiting = true; main.Close();
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL main pages: " + ex); }
        try
        {
            var model = new WidgetInstance { Left = 100, Top = 100 };
            var window = new WidgetWindow(vm, new WidgetViewModel(vm, model));
            window.Show();
            var hwnd = new WindowInteropHelper(window).Handle;
            var area = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
            // Use a synthetic neighbor relative to the actual monitor's physical origin.
            int scale = (int)Math.Round(VisualTreeHelper.GetDpi(window).DpiScaleX * 96);
            double dpiScale = scale / 96.0;
            int w = (int)Math.Round(300 * dpiScale), h = (int)Math.Round(200 * dpiScale);
            int px = area.Left + (int)Math.Round(200 * dpiScale), py = area.Top + (int)Math.Round(200 * dpiScale);
            window.GetSnapPeers = () => [new ScreenRect(px, py, w, h)];
            var input = new NativeRect { Left = px + w + (int)Math.Round(21 * dpiScale), Top = py + 4,
                Right = px + 2 * w + (int)Math.Round(21 * dpiScale), Bottom = py + 4 + h };
            var off = SendMoving(hwnd, input); if (off.Left != input.Left) throw new Exception("OFF snapped native drag");
            vm.MagneticMode = true;
            var on = SendMoving(hwnd, input);
            if (on.Left != px + w + (int)Math.Round(16 * dpiScale) || on.Top != py) throw new Exception("WM_MOVING did not magnetically align");
            if (on.Right - on.Left != w || on.Bottom - on.Top != h) throw new Exception("Drag changed size");
            window.GetSnapPeers = () => [];
            var free = new NativeRect { Left = px + 47, Top = py + 41, Right = px + 47 + w, Bottom = py + 71 + h };
            var freelyMoved = SendMoving(hwnd, free);
            if (freelyMoved.Left != free.Left || freelyMoved.Top != free.Top) throw new Exception("Magnetic Mode snapped empty space to a grid");
            model.Locked = true;
            var locked = SendMoving(hwnd, input); if (locked.Left != input.Left) throw new Exception("Locked widget snapped");
            model.Locked = false; vm.MagneticMode = true;
            var origin = new ScreenRect(px + w + 16 * dpiScale, py, w, h);
            var drag = (MagneticDragSession)typeof(WidgetWindow).GetField("_drag", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            double cursorX = 1000, cursorY = 1000;
            Func<(double X, double Y)?> cursor = () => (cursorX, cursorY);
            typeof(WidgetWindow).GetProperty("GetPointerPosition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, cursor);
            window.GetSnapPeers = () => [new ScreenRect(px, py, w, h)];
            drag.Begin(origin, cursorX, cursorY);
            var corrected = new NativeRect { Left = (int)origin.Left, Top = py, Right = (int)origin.Left + w, Bottom = py + h };
            for (int step = 1; step <= 30; step++)
            {
                cursorX = 1000 + step * dpiScale;
                // Reproduce native feedback: the next proposal moves only one pixel from the last snapped position.
                corrected.Left++; corrected.Right++;
                corrected = SendMoving(hwnd, corrected);
                int expected = (int)Math.Round(origin.Left + (step <= 6 ? 0 : step * dpiScale));
                if (corrected.Left != expected || corrected.Top != py) throw new Exception($"Pointer drag remained stuck at step {step}: {corrected.Left}, expected {expected}");
            }
            SendMessage(hwnd, 0x0232, IntPtr.Zero, IntPtr.Zero);
            if (drag.IsActive) throw new Exception("Native move exit did not clear pointer origin");
            vm.MagneticMode = false; window.Close(); passed += 2;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL native magnetic drag: " + ex); }
        try
        {
            using var gpu = new GpuCounter();
            var first = gpu.Sample();
            Thread.Sleep(1100);
            var readings = new[] { first, gpu.Sample() };
            Thread.Sleep(1100);
            readings = readings.Append(gpu.Sample()).ToArray();
            if (readings.Any(r => r.Usage.HasValue && (!double.IsFinite(r.Usage.Value) || r.Usage < 0 || r.Usage > 100 || string.IsNullOrWhiteSpace(r.Engine))))
                throw new Exception("Invalid live GPU sample");
            File.WriteAllText(Path.Combine(output, "gpu-live.txt"), string.Join("\n", readings.Select(r => $"{r.Time:O} · Usage={r.Usage?.ToString("F2") ?? "unavailable"} · Engine={r.Engine} · Warming={r.Warming}")));
            Console.WriteLine("GPU live probe: " + (readings.Last().Usage is { } usage ? $"{usage:F2}% · {readings.Last().Engine}" : "Unavailable on this machine; fallback checked"));
            passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL GPU live probe: " + ex); }
        foreach (var global in new[] { ThemeMode.Light, ThemeMode.Dark })
        foreach (var individual in new ThemeMode?[] { null, ThemeMode.System, ThemeMode.Light, ThemeMode.Dark })
        foreach (var kind in Enum.GetValues<WidgetKind>())
        {
            try
            {
                settings.Theme = global; theme.Apply(global);
                var widget = new WidgetViewModel(vm, new WidgetInstance { Kind = kind, Theme = individual });
                var window = new WidgetWindow(vm, widget);
                bool expectedDark = theme.DarkFor(individual ?? global);
                var palette = ThemeService.Palette(expectedDark);
                if (widget.IsDark != expectedDark || ((SolidColorBrush)window.FindResource("LineBrush")).Color != palette["LineBrush"])
                    throw new Exception("Per-widget resources do not match theme");
                string ink = kind == WidgetKind.Clock ? "ClockTextBrush" : "TextBrush";
                if (((SolidColorBrush)widget.Ink).Color != palette[ink]) throw new Exception("Per-widget text contrast incorrect");
                window.Close(); passed++;
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL independent theme {global}/{individual}/{kind}: {ex}"); }
        }
        try
        {
            settings.Theme = ThemeMode.Light; theme.Apply(settings.Theme);
            var first = vm.Add(WidgetKind.Cpu); var second = vm.Add(WidgetKind.Gpu); var group = vm.CreateGroup("모션 검증");
            var main = new MainWindow(vm); main.Show(); Pump(40);
            main.OpenWidgetEditor(first); Pump(10);
            var widgetHost = (StackPanel)main.FindName("WidgetsHost"); var groupHost = (StackPanel)main.FindName("GroupsHost");
            var firstCard = widgetHost.Children.OfType<ExpandableCard>().First();
            if (!firstCard.IsExpanded || firstCard.Editor is null || firstCard.EditorHeight <= 0) throw new Exception("Inline widget editor did not open");
            if (main.FindName("EditorBorder") is not null) throw new Exception("Global bottom editor remains");
            var themeCombo = AllChildren(firstCard.Editor).OfType<ComboBox>().First(c => System.Windows.Automation.AutomationProperties.GetName(c) == "위젯 테마");
            themeCombo.SelectedIndex = 3; Pump(10);
            if (first.Model.Theme != ThemeMode.Dark || !first.IsDark || second.IsDark) throw new Exception("Individual theme leaked into another widget");
            if (!ReferenceEquals(firstCard, widgetHost.Children[0]) || !firstCard.IsExpanded) throw new Exception("Settings update recreated the expanded card");
            main.OpenWidgetEditor(second); Pump(10);
            if (firstCard.IsExpanded || widgetHost.Children.OfType<ExpandableCard>().Count(c => c.IsExpanded) != 1) throw new Exception("Another widget did not collapse previous editor");
            main.OpenGroupEditor(group); Pump(10);
            if (widgetHost.Children.OfType<ExpandableCard>().Any(c => c.IsExpanded) || groupHost.Children.OfType<ExpandableCard>().Count(c => c.IsExpanded) != 1) throw new Exception("Group/widget accordion did not share one active editor");
            passed++;
            // Real clocks must interpolate and be safe when the user reverses direction.
            Motion.EnabledOverride = true;
            main.OpenWidgetEditor(first); Pump(75);
            double opening = firstCard.EditorHeight;
            if (opening <= 0) throw new Exception("Accordion animation did not begin");
            main.OpenWidgetEditor(second); Pump(30); main.OpenWidgetEditor(first); Pump(350);
            if (!firstCard.IsExpanded || firstCard.Editor is null || firstCard.EditorHeight <= opening
                || widgetHost.Children.OfType<ExpandableCard>().Count(c => c.IsExpanded) != 1)
                throw new Exception("Rapid editor reversal left stale state");
            var bitmap = new RenderTargetBitmap((int)((FrameworkElement)main.Content).ActualWidth, (int)((FrameworkElement)main.Content).ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render((FrameworkElement)main.Content); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, "Inline-Widget-Editor.png"))) png.Save(stream);
            main.OpenGroupEditor(group); Pump(350);
            bitmap = new RenderTargetBitmap((int)((FrameworkElement)main.Content).ActualWidth, (int)((FrameworkElement)main.Content).ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render((FrameworkElement)main.Content); png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, "Inline-Group-Editor.png"))) png.Save(stream);
            passed++;
            var widgetWindow = new WidgetWindow(vm, second); widgetWindow.ShowAnimated(); Pump(250);
            widgetWindow.ResizeAnimated(420, 310); Pump(70); double intermediate = widgetWindow.ActualWidth;
            if (intermediate <= 300 || intermediate >= 420) throw new Exception($"Preset size jumped without interpolation: {intermediate}");
            Pump(300);
            if (Math.Abs(widgetWindow.ActualWidth - 420) > 1 || Math.Abs(second.Model.Width - 420) > 1) throw new Exception("Animated size did not settle/save");
            widgetWindow.HideAnimated(); Pump(30); widgetWindow.ShowAnimated(); Pump(220);
            if (!widgetWindow.IsVisible || !widgetWindow.IsHitTestVisible || widgetWindow.Opacity < .99) throw new Exception("Show/hide reversal left ghost widget");
            widgetWindow.HideAnimated(); Pump(200);
            if (widgetWindow.IsVisible) throw new Exception("Animated hide did not finish");
            widgetWindow.Close(); passed++;
            main.Close(); Pump(30); main.BringForward(); Pump(200);
            if (!main.IsVisible || main.Opacity < .99) throw new Exception("Main close/reopen reversal left a hidden main window");
            main.Close(); Pump(200);
            if (main.IsVisible) throw new Exception("Main close did not hide to tray");
            main.BringForward(); Pump(220);
            if (!main.IsVisible) throw new Exception("Main did not reopen after animated hide");
            passed++;
            Motion.EnabledOverride = false; main.Exiting = true; main.Close();
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL inline editors / motion: " + ex); Motion.EnabledOverride = false; }
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        foreach (var dimensions in new[] { new Size(900, 640), new Size(1100, 780) })
        {
            MainWindow? main = null;
            try
            {
                settings.Theme = mode; theme.Apply(mode);
                main = new MainWindow(vm) { Width = dimensions.Width, Height = dimensions.Height }; main.Show(); Pump(40);
                main.OpenWidgetEditor(vm.Widgets.First()); Pump(30);
                var scroll = (ScrollViewer)main.FindName("PageScroll"); scroll.UpdateLayout();
                var bar = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
                bar.ApplyTemplate();
                var track = (Track)bar.Template.FindName("PART_Track", bar);
                if (bar.ActualWidth != 12 || track is null || AllChildren(bar).OfType<RepeatButton>().Count() != 2)
                    throw new Exception($"Scroll bar still has native arrows/chrome: {bar.Name}, width={bar.ActualWidth}, track={track is not null}, buttons={AllChildren(bar).OfType<RepeatButton>().Count()}");
                if (scroll.ScrollableHeight <= 0) throw new Exception("Editor scroll validation requires overflowing content");
                var thumb = track.Thumb; thumb.ApplyTemplate();
                var ink = (Border)thumb.Template.FindName("ThumbInk", thumb);
                if (ink.Width != 6 || ink.CornerRadius.TopLeft != 3 || ((SolidColorBrush)ink.Background).Color != ThemeService.Palette(mode == ThemeMode.Dark)["MutedBrush"])
                    throw new Exception("Scroll thumb style or theme incorrect");
                thumb.RaiseEvent(new DragStartedEventArgs(0, 0)); thumb.RaiseEvent(new DragDeltaEventArgs(0, 40)); Pump(20);
                if (scroll.VerticalOffset <= 0) throw new Exception("Thumb drag did not scroll editor");
                thumb.RaiseEvent(new DragCompletedEventArgs(0, 40, false));
                scroll.ScrollToVerticalOffset(0); Pump(20);
                var command = (System.Windows.Input.RoutedCommand)track.IncreaseRepeatButton.Command;
                command.Execute(null, track.IncreaseRepeatButton); Pump(20);
                if (scroll.VerticalOffset <= 0) throw new Exception("Track page click did not scroll editor");
                scroll.ScrollToVerticalOffset(0); Pump(20);
                var bitmap = new RenderTargetBitmap((int)((FrameworkElement)main.Content).ActualWidth, (int)((FrameworkElement)main.Content).ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render((FrameworkElement)main.Content); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(output, $"Scroll-Editor-{mode}-{dimensions.Width}x{dimensions.Height}.png"))) png.Save(stream);
                passed++;
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL scroll editor {mode}/{dimensions}: {ex}"); }
            finally { if (main is not null) { main.Exiting = true; main.Close(); } }
        }
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        foreach (var kind in new[] { WidgetKind.Cpu, WidgetKind.Memory, WidgetKind.Gpu })
        foreach (double diameter in new[] { 180.0, 220.0, 300.0, 420.0, 480.0 })
        {
            WidgetWindow? window = null;
            try
            {
                settings.Theme = mode; theme.Apply(mode);
                var model = new WidgetInstance { Kind = kind, Shape = WidgetShape.Circle, Width = diameter, Height = diameter };
                var widget = new WidgetViewModel(vm, model); window = new WidgetWindow(vm, widget);
                var root = (FrameworkElement)window.Content; var dimensions = new Size(diameter, diameter);
                widget.SetSize(diameter, diameter); root.Measure(dimensions); root.Arrange(new Rect(dimensions)); root.UpdateLayout();
                if (((FrameworkElement)window.FindName("CardContent")).Visibility != Visibility.Collapsed || ((FrameworkElement)window.FindName("CircleContent")).Visibility != Visibility.Visible)
                    throw new Exception("Circle did not replace card contents");
                if (AllText((FrameworkElement)window.FindName("CircleContent")).Count(t => t.Text == widget.Value) != 1) throw new Exception("Circle must show one centered percentage");
                foreach (double scale in new[] { 1.0, 1.5, 2.0 })
                {
                    ValidateText(root, dimensions);
                    var bitmap = new RenderTargetBitmap((int)(diameter * scale), (int)(diameter * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(root);
                    byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    if (pixels.All(b => b == 0) || pixels[3] > 24) throw new Exception("Circle empty or rectangular corner filled");
                    if (scale == 1)
                    {
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(output, $"Circle-{mode}-{kind}-{diameter}.png")); png.Save(stream);
                    }
                    passed++;
                }
            }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL circle render {mode}/{kind}/{diameter}: {ex}"); }
            finally { window?.Close(); }
        }
        try
        {
            var arc = new CircularUsageArc { Width = 240, Height = 240, Ink = Brushes.Black, TrackBrush = Brushes.LightGray };
            foreach (double percentage in new[] { 0.0, 25, 50, 100 })
            {
                arc.Percentage = percentage; arc.Measure(new Size(240, 240)); arc.Arrange(new Rect(0, 0, 240, 240)); arc.UpdateLayout();
                var bitmap = new RenderTargetBitmap(240, 240, 96, 96, PixelFormats.Pbgra32); bitmap.Render(arc);
                byte[] pixels = new byte[240 * 240 * 4]; bitmap.CopyPixels(pixels, 240 * 4, 0);
                foreach (double fraction in new[] { .125, .375, .625, .875 })
                {
                    double angle = (150 - 120 * fraction) * Math.PI / 180;
                    int x = (int)Math.Round(120 + Math.Cos(angle) * 240 * .405), y = (int)Math.Round(120 + Math.Sin(angle) * 240 * .405);
                    bool black = pixels[(y * 240 + x) * 4] < 80;
                    if (black != (fraction < percentage / 100)) throw new Exception($"Arc fill wrong for {percentage}% at fraction {fraction}");
                }
            }
            arc.Percentage = double.NaN; if (arc.DisplayedPercentage != 0) throw new Exception("Arc accepted NaN");
            passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL circular arc proportions: " + ex); }
        try
        {
            Motion.EnabledOverride = true;
            var arc = new CircularUsageArc { Percentage = 0 }; var host = new Window { Content = arc, Width = 240, Height = 240 }; host.Show(); Pump(30);
            arc.Percentage = 100; Pump(75);
            if (arc.DisplayedPercentage <= 0 || arc.DisplayedPercentage >= 100) throw new Exception("Arc jumped without interpolation");
            arc.Percentage = 25; Pump(420); if (Math.Abs(arc.DisplayedPercentage - 25) > .1) throw new Exception("Arc motion reversal retained stale value");
            host.Close(); Motion.EnabledOverride = false; passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL circular arc motion: " + ex); Motion.EnabledOverride = false; }
        try
        {
            settings.Theme = ThemeMode.Light; theme.Apply(settings.Theme);
            var metric = vm.Widgets.First(); var main = new MainWindow(vm); main.Show(); Pump(30); main.OpenWidgetEditor(metric); Pump(30);
            var card = ((StackPanel)main.FindName("WidgetsHost")).Children.OfType<ExpandableCard>().First();
            var shape = AllChildren(card.Editor!).OfType<ComboBox>().First(c => System.Windows.Automation.AutomationProperties.GetName(c) == "모양");
            var window = new WidgetWindow(vm, metric); window.Show(); Pump(30);
            shape.SelectedIndex = 1; Pump(30);
            if (metric.Model.Shape != WidgetShape.Circle || Math.Abs(window.ActualWidth - window.ActualHeight) > 1) throw new Exception("Shape editor did not make a square window");
            var radius = AllChildren(card.Editor!).OfType<ComboBox>().First(c => System.Windows.Automation.AutomationProperties.GetName(c) == "모서리");
            if (radius.IsEnabled) throw new Exception("Corner control still enabled for circle");
            window.ResizeAnimated(420, 310); Pump(30); if (Math.Abs(window.ActualHeight - 420) > 1) throw new Exception("Circle preset lost ratio");
            var grip = AllChildren((FrameworkElement)window.Content).OfType<Thumb>().First(t => t.IsVisible && (string)t.Tag == "R");
            grip.RaiseEvent(new DragDeltaEventArgs(20, 0)); grip.RaiseEvent(new DragCompletedEventArgs(20, 0, false)); Pump(30);
            if (Math.Abs(window.ActualWidth - window.ActualHeight) > 1 || Math.Abs(window.ActualWidth - 440) > 1) throw new Exception("Native circle resize lost ratio");
            metric.Model.Locked = true; vm.Changed(); grip.RaiseEvent(new DragDeltaEventArgs(20, 0)); Pump(20);
            if (Math.Abs(window.ActualWidth - 440) > 1) throw new Exception("Locked circle resized");
            metric.Model.Locked = false; shape.SelectedIndex = 0; Pump(20);
            if (metric.IsCircular || !radius.IsEnabled) throw new Exception("Card restoration did not restore radius option");
            main.OpenWidgetEditor(vm.Add(WidgetKind.Clock)); Pump(20);
            var clockCard = ((StackPanel)main.FindName("WidgetsHost")).Children.OfType<ExpandableCard>().Last();
            if (AllChildren(clockCard.Editor!).OfType<ComboBox>().Any(c => System.Windows.Automation.AutomationProperties.GetName(c) == "모양")) throw new Exception("Clock received circle setting");
            window.Close(); main.Exiting = true; main.Close(); passed++;
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL circle editor / resize: " + ex); }
        vm.Dispose();
        Console.WriteLine($"UI renders/pages: {passed} passed, {failed} failed. PNGs: {output}");
        File.WriteAllText(Path.Combine(output, "ui-results.txt"), $"{passed} passed, {failed} failed\nRendered themes: Light, Dark\nSizes: 180x140, 300x200, 420x310, 640x140, 180x480\nRender scales: 100%, 150%, 200%\nThis validates offscreen rendering, not physical monitor DPI switching.\n");
        return failed == 0 ? 0 : 1;
    }
    private static TextBlock? FindText(DependencyObject parent)
    {
        if (parent is TextBlock text) return text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindText(VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
        return null;
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> AllChildren(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in AllChildren(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static System.Collections.Generic.IEnumerable<TextBlock> AllText(DependencyObject parent) => AllChildren(parent).OfType<TextBlock>().Where(t => t.IsVisible || !t.IsLoaded && t.Visibility == Visibility.Visible);
    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(8); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    private static NativeRect SendMoving(IntPtr hwnd, NativeRect rect)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.StructureToPtr(rect, pointer, false);
            SendMessage(hwnd, 0x0216, IntPtr.Zero, pointer);
            return Marshal.PtrToStructure<NativeRect>(pointer);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    private static void ValidateText(FrameworkElement root, Size size)
    {
        void Visit(DependencyObject obj)
        {
            if (obj is UIElement element && element.Visibility != Visibility.Visible) return;
            if (obj is TextBlock text && text.ActualWidth > 0 && text.ActualHeight > 0)
            {
                var bounds = text.TransformToAncestor(root).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight));
                if (bounds.Left < -1 || bounds.Top < -1 || bounds.Right > size.Width + 1 || bounds.Bottom > size.Height + 1)
                    throw new Exception($"Text out of bounds: {text.Text}, {bounds}");
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++) Visit(VisualTreeHelper.GetChild(obj, i));
        }
        Visit(root);
    }
}
