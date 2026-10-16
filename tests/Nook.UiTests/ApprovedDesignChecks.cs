using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nook.App.Controls;
using Nook.App.Services;
using Nook.App.ViewModels;
using Nook.App.Views;
using Nook.Core;
using ThemeMode = Nook.Core.ThemeMode;

internal static partial class Program
{
    private static void RunApprovedDesignChecks(string output, ThemeService theme, ref int passed, ref int failed)
    {
        int good = 0, bad = 0;
        void Check(string name, Action action)
        { try { action(); good++; Console.WriteLine("PASS approved design " + name); } catch (Exception ex) { bad++; Console.WriteLine("FAIL approved design " + name + ": " + ex); } }
        void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        using var metrics = new SystemMetricsService(Dispatcher.CurrentDispatcher, null, null, false);
        var state = new AppSettings { MainTheme = ThemeMode.Light };
        using var own = new AppViewModel(state, new SettingsStore(Path.Combine(output, "approved-design-settings")), metrics, theme);

        Check("media stays contained without overlapping artwork, text or transport", () =>
        {
            foreach (var size in new[] { new Size(132, 72), new Size(252, 132), new Size(372, 242), new Size(592, 72), new Size(132, 412), new Size(300, 160) })
            {
                var panel = new MediaLayoutPanel();
                foreach (string tag in new[] { "cover", "title", "artist", "controls", "progress", "time" })
                    panel.Children.Add(new Border { Tag = tag });
                panel.Measure(size); panel.Arrange(new Rect(size)); panel.UpdateLayout();
                var children = panel.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible).ToArray();
                foreach (var child in children)
                {
                    var bounds = new Rect(child.TranslatePoint(new Point(), panel), child.RenderSize);
                    Require(bounds.Left >= -.1 && bounds.Top >= -.1 && bounds.Right <= size.Width + .1 && bounds.Bottom <= size.Height + .1, $"{child.Tag} escapes {size}: {bounds}");
                    foreach (var other in children.Where(c => !ReferenceEquals(c, child)))
                    {
                        var overlap = Rect.Intersect(bounds, new Rect(other.TranslatePoint(new Point(), panel), other.RenderSize));
                        Require(overlap.IsEmpty || overlap.Width <= .1 || overlap.Height <= .1, $"{child.Tag}/{other.Tag} overlap at {size}: {overlap}");
                    }
                }
                Require(children.Any(c => Equals(c.Tag, "cover")) && children.Any(c => Equals(c.Tag, "controls")), "Small lost cover or transport");
            }
        });
        Check("level adjustment exposes a functional read-only aware UIA range", () =>
        {
            var control = new LevelControl { Available = true, Value = 42, AccessibilityName = "시험 볼륨" };
            var peer = UIElementAutomationPeer.CreatePeerForElement(control)!;
            Require(peer.GetName().Contains("시험 볼륨"), "Accessible name missing");
            var range = peer.GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
            Require(range is not null && range.Minimum == 0 && range.Maximum == 100 && range.Value == 42 && !range.IsReadOnly, "RangeValue pattern missing or wrong");
            int events = 0; double adjusted = 0; control.Adjusted += value => { events++; adjusted = value; };
            range!.SetValue(67); Require(control.Value == 67 && adjusted == 67 && events == 1, "UIA did not adjust exactly once");
            bool invalid = false; try { range.SetValue(101); } catch (ArgumentException) { invalid = true; }
            Require(invalid && control.Value == 67, "Invalid UIA input changed state");
            control.Available = false; Require(range.IsReadOnly, "Unavailable hardware still writable");
            var battery = new LevelControl { Battery = true, Available = true, Value = 70 };
            var batteryRange = UIElementAutomationPeer.CreatePeerForElement(battery)!.GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
            Require(batteryRange is null || batteryRange.IsReadOnly, "Battery is writable through UIA");
        });
        Check("library previews all kinds; selection and memo switches fit; navigation accepts focus", () =>
        {
            own.Add(WidgetKind.Cpu); own.SavePreset("검토 배치");
            var main = new MainWindow(own); main.Show(); Pump(30);
            try
            {
                var library = (FrameworkElement)main.FindName("FeatureLibraryHost");
                var previews = AllChildren(library).OfType<LibraryPreview>().ToArray();
                Require(previews.Length == WidgetCatalog.All.Count, "Library preview not wired for every entry");
                foreach (var preview in previews)
                {
                    var bitmap = new RenderTargetBitmap(160, 60, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(preview); byte[] pixels = new byte[160 * 60 * 4]; bitmap.CopyPixels(pixels, 160 * 4, 0);
                    int visible = Enumerable.Range(0, 160 * 60).Count(i => pixels[i * 4 + 3] > 16);
                    Require(visible >= 20, $"{preview.Kind} preview invisible ({visible} pixels)");
                }
                SaveV10Image((FrameworkElement)main.Content, Path.Combine(output, "Approved-Library-Light.png"));
                Require(metrics.GpuRequested && metrics.DiskRequested, "Visible library demand missing");
                main.WindowState = WindowState.Minimized; Pump(20);
                Require(!metrics.GpuRequested && !metrics.DiskRequested, "Minimized library kept live preview sampling");
                main.WindowState = WindowState.Normal; Pump(20);
                Require(metrics.GpuRequested && metrics.DiskRequested, "Restored library did not request data");
                var settingsNav = (Button)main.FindName("SettingsNav"); settingsNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(20);
                Require(settingsNav.Focus(), "Settings navigation cannot focus");
                Require(settingsNav.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && Keyboard.FocusedElement is Control, "Forward Tab traversal failed");
                Require(((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)), "Reverse Tab traversal failed");
                foreach (var button in AllChildren(main).OfType<Button>().Where(b => b.IsVisible && b.IsEnabled))
                { Require(button.Focusable && button.Focus(), "Button cannot focus: " + button.Content); Require(!string.IsNullOrWhiteSpace(UIElementAutomationPeer.CreatePeerForElement(button)!.GetName()), "Button has no accessible name: " + button.Content); }
                var nav = (Button)main.FindName("ManageNav"); Require(nav.Focusable && nav.Focus(), "Navigation cannot receive keyboard focus");
                nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(20);
                var toggle = AllChildren(main).OfType<ToggleButton>().Single(t => t.IsVisible && t.GetBindingExpression(ToggleButton.IsCheckedProperty)?.ParentBinding.Path?.Path == nameof(WidgetViewModel.Selected));
                FitTrack(toggle); Require(toggle.Focusable && toggle.Focus(), "Selection cannot receive focus");
                var selectionPattern = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle)!.GetPattern(PatternInterface.Toggle)!;
                bool before = own.Widgets[0].Selected; selectionPattern.Toggle(); Require(own.Widgets[0].Selected != before, "Selection automation did not update multi-move state"); selectionPattern.Toggle();
                var settingsButton = AllChildren(main).OfType<Button>().Single(b => b.IsVisible && Equals(b.Content, "설정"));
                ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(settingsButton)!.GetPattern(PatternInterface.Invoke)!).Invoke(); Pump(20);
                Require(AllText(main).Any(t => t.Text.Contains("개별 설정")), "Settings invoke did not open editor");
                Require(AllText(main).Any(t => t.Text.Contains("이동") && t.IsVisible), "Multi-move purpose label missing");
                SaveV10Image((FrameworkElement)main.Content, Path.Combine(output, "Approved-Manage-Light.png"));
                ((Button)main.FindName("PresetsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(20);
                var host = (FrameworkElement)main.FindName("PresetHost");
                var share = AllChildren(host).OfType<ToggleButton>().Single(t => t.IsVisible); FitTrack(share);
                var label = AllText(host).Single(t => t.Text == "메모 내용 공유");
                double gap = share.TranslatePoint(new Point(), host).X - label.TranslatePoint(new Point(label.ActualWidth, 0), host).X;
                Require(gap >= 8 && gap <= 24, "Share label and switch separated: " + gap);
                SaveV10Image((FrameworkElement)main.Content, Path.Combine(output, "Approved-Presets-Light.png"));
                state.MainTheme = ThemeMode.Dark; own.Changed(); Pump(20);
                ((Button)main.FindName("LibraryNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(20);
                SaveV10Image((FrameworkElement)main.Content, Path.Combine(output, "Approved-Library-Dark.png"));
                void FitTrack(ToggleButton item)
                {
                    item.ApplyTemplate(); var track = AllChildren(item).OfType<Border>().Single(b => b.Name == "SwitchTrack");
                    var rect = new Rect(track.TranslatePoint(new Point(), item), track.RenderSize);
                    Require(rect.Width > 30 && rect.Left >= -.1 && rect.Top >= -.1 && rect.Right <= item.ActualWidth + .1 && rect.Bottom <= item.ActualHeight + .1, "Switch track clipped: " + rect + " within " + item.RenderSize);
                }
            }
            finally { main.Exiting = true; main.Close(); }
        });
        Check("media and Activity readable across Small, Mid, Big and long cards", () =>
        {
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
            foreach (var kind in new[] { WidgetKind.Media, WidgetKind.Activity })
            foreach (var requested in new[] { new Size(180, 140), new Size(300, 200), new Size(420, 310), new Size(640, 140), new Size(180, 480) })
            {
                state.Theme = mode; theme.Apply(mode);
                var model = new WidgetInstance { Kind = kind, Width = requested.Width, Height = requested.Height, ActivityPeriod = ActivityPeriod.Year };
                var widget = new WidgetViewModel(own, model); var window = new WidgetWindow(own, widget);
                try
                {
                    var root = (FrameworkElement)window.Content; var size = new Size(model.Width, model.Height);
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); ValidateText(root, size);
                    foreach (double scale in new[] { 1.0, 1.5, 2.0 })
                    { var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(root); }
                    SaveV10Image(root, Path.Combine(output, $"Approved-{kind}-{mode}-{size.Width}x{size.Height}.png"));
                }
                finally { window.Close(); }
            }
        });
        Check("hidden and paused visuals detach; resume uses the same visual", () =>
        {
            Motion.EnabledOverride = true;
            try
            {
                foreach (var kind in new[] { WidgetKind.Clock, WidgetKind.Timer })
                {
                    var model = new WidgetInstance { Kind = kind, ClockStyle = ClockStyle.Analog };
                    if (kind == WidgetKind.Timer) CountdownEngine.Toggle(model.Countdown, false, DateTimeOffset.UtcNow);
                    var widget = new WidgetViewModel(own, model); var window = new WidgetWindow(own, widget); window.Show(); Pump(50);
                    try
                    {
                        var visual = AllChildren(window).OfType<FrameworkElement>().Single(v => v.IsVisible && (kind == WidgetKind.Clock ? v is ClockVisual : v is CountdownVisual));
                        var attached = visual.GetType().GetField("_attached", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        Require((bool)attached.GetValue(visual)!, kind + " not animating on display");
                        window.Hide(); Pump(20); Require(!(bool)attached.GetValue(visual)!, kind + " stayed subscribed while hidden");
                        window.Show(); Pump(40); Require((bool)attached.GetValue(visual)!, kind + " failed to resume after show");
                        if (kind == WidgetKind.Timer)
                        {
                            CountdownEngine.Toggle(model.Countdown, false, DateTimeOffset.UtcNow); widget.RefreshAppearance(); Pump(60);
                            Require(!(bool)attached.GetValue(visual)!, "Paused timer stayed subscribed");
                            CountdownEngine.Toggle(model.Countdown, false, DateTimeOffset.UtcNow); widget.RefreshAppearance(); Pump(60);
                            Require((bool)attached.GetValue(visual)!, "Started timer did not reattach");
                            Require(AllChildren(window).OfType<CountdownVisual>().Single(v => v.IsVisible) == visual, "Resume recreated the countdown visual");
                        }
                        ((IDisposable)visual).Dispose(); Pump(20); Require(!(bool)attached.GetValue(visual)!, "Disposed visual stayed subscribed");
                    }
                    finally { window.Close(); }
                }
                var digital = new WidgetViewModel(own, new() { Kind = WidgetKind.Clock, ClockStyle = ClockStyle.Text });
                var textWindow = new WidgetWindow(own, digital); textWindow.Show(); Pump(40);
                try
                { var visual = AllChildren(textWindow).OfType<ClockVisual>().Single(v => v.IsVisible); Require(!(bool)typeof(ClockVisual).GetField("_attached", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(visual)!, "Digital clock subscribed to continuous frames"); }
                finally { textWindow.Close(); }
            }
            finally { Motion.EnabledOverride = false; }
        });
        passed += good; failed += bad;
    }
}
