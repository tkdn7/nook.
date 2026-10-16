using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nook.Core;
using Nook.App.Controls;
using Nook.App.Services;
using Nook.App.ViewModels;
using Nook.App.Views;
using ThemeMode = Nook.Core.ThemeMode;

internal static partial class Program
{
    // Third-meeting implementation checks (Opacity None, Activity period removal, mouse
    // horizontal scroll). File owned by DeepSeek; entrypoint wiring in Program.cs and all
    // App/Core implementation are Claude's. Acceptance/evidence matrix:
    // docs/wiki/reports/2026-10-14-meeting3-implementation-deepseek.md.
    // Stable hooks confirmed with Claude: AutomationProperties.Name "ActivityScroll" on the
    // Activity heatmap ScrollViewer and "ActivityPeriodShortcut" on the period affordance.
    // All of these are raised routed/synthetic checks, NOT native physical input: a
    // MouseWheel/Key routed event and IsHitTestVisible prove wiring and the hit map, not
    // that a real device delivered the input. Implemented features may not be downgraded
    // to a skip: a missing capability FAILS.
    private static void RunMeeting3Checks(string output, AppViewModel vm, AppSettings settings, ThemeService theme, ref int passed, ref int failed)
    {
        int good = 0, bad = 0;
        void Check(string name, Action test)
        { try { test(); good++; Console.WriteLine("PASS Meeting3 " + name); } catch (Exception ex) { bad++; Console.WriteLine("FAIL Meeting3 " + name + ": " + ex); } }
        void Require(bool value, string message) { if (!value) throw new Exception(message); }

        static ScrollViewer? FindActivityScroll(FrameworkElement root) =>
            AllChildren(root).OfType<ScrollViewer>()
                .FirstOrDefault(s => s.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto
                    && s.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled);
        static bool HasBodyPeriodCombo(FrameworkElement root) =>
            AllChildren(root).OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == "잔디 기간");
        (WidgetWindow Window, WidgetViewModel Widget) BuildActivity(ActivityPeriod period, double width, double height, ThemeMode mode)
        {
            var widget = new WidgetViewModel(vm, new() { Kind = WidgetKind.Activity, ActivityPeriod = period, Theme = mode, Width = width, Height = height });
            var window = new WidgetWindow(vm, widget);
            var root = (FrameworkElement)window.Content; var size = new Size(width, height);
            widget.SetSize(width, height); root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
            return (window, widget);
        }
        // A live presentation source is required for scroll extent, focus and key routing.
        void LiveLayout(WidgetWindow window)
        {
            window.Show(); Pump(40);
            var root = (FrameworkElement)window.Content; root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        // Core ledger stores UsageDay; capture and restore the real list, never a DateOnly array.
        UsageDay[] FillLedger()
        {
            var today = DateOnly.FromDateTime(DateTime.Now); var oldDays = vm.ScreenTime.Ledger.Days.ToArray();
            vm.ScreenTime.Ledger.Days.Clear();
            for (int i = 0; i < 366; i++) vm.ScreenTime.Ledger.Days.Add(new() { Date = today.AddDays(-i), Apps = [new() { Id = "code", Name = "Code", Color = "#408DC1", Seconds = i % 5 * 4500 }] });
            return oldDays;
        }
        void RestoreLedger(UsageDay[] oldDays) { vm.ScreenTime.Ledger.Days.Clear(); vm.ScreenTime.Ledger.Days.AddRange(oldDays); }

        // 0. Stable automation hooks the App must expose for the Activity scroll/period.
        Check("Activity stable automation hooks", () =>
        {
            var (window, _) = BuildActivity(ActivityPeriod.Year, 300, 200, ThemeMode.Light);
            try
            {
                LiveLayout(window); var root = (FrameworkElement)window.Content;
                var scroll = FindActivityScroll(root);
                Require(scroll is not null, "no horizontal Activity ScrollViewer found");
                Require(AutomationProperties.GetName(scroll!) == "ActivityScroll", $"ActivityScroll hook missing (name='{AutomationProperties.GetName(scroll!)}')");
                Require(AllChildren(root).OfType<FrameworkElement>().Any(e => AutomationProperties.GetName(e) == "ActivityPeriodShortcut"), "ActivityPeriodShortcut hook missing");
            }
            finally { window.Close(); }
        });

        // 1. The body period ComboBox must be gone once the settings selector is the only one.
        Check("Activity body period ComboBox removed", () =>
        {
            foreach (var size in new[] { new Size(300, 200), new Size(420, 310) })
            {
                var (window, _) = BuildActivity(ActivityPeriod.Year, size.Width, size.Height, ThemeMode.Light);
                try { LiveLayout(window); Require(!HasBodyPeriodCombo((FrameworkElement)window.Content), $"body period ComboBox remains at {size.Width}x{size.Height}"); }
                finally { window.Close(); }
            }
        });

        // 2. A synthesized ordinary mouse-wheel routed event must move the horizontal offset.
        //    300x200 is Mid (IsStandard) so the selected Year period is actually laid out.
        Check("ordinary mouse-wheel drives horizontal offset", () =>
        {
            var oldDays = FillLedger();
            var (window, _) = BuildActivity(ActivityPeriod.Year, 300, 200, ThemeMode.Light);
            try
            {
                LiveLayout(window);
                var scroll = FindActivityScroll((FrameworkElement)window.Content);
                Require(scroll is not null, "no horizontal Activity ScrollViewer found");
                Require(scroll!.ScrollableWidth > 0, $"content not wider than viewport (scrollable={scroll.ScrollableWidth})");
                scroll.ScrollToHorizontalOffset(0); Pump(10); double before = scroll.HorizontalOffset;
                scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                Pump(20);
                Require(scroll.HorizontalOffset > before, $"wheel did not scroll horizontally ({before} -> {scroll.HorizontalOffset})");
            }
            finally { window.Close(); RestoreLedger(oldDays); }
        });

        // 3. Keyboard route (PreviewKeyDown End/Home) must reach the horizontal scroll too.
        Check("keyboard route Home/End scrolls horizontally", () =>
        {
            var oldDays = FillLedger();
            var (window, _) = BuildActivity(ActivityPeriod.Year, 300, 200, ThemeMode.Light);
            try
            {
                LiveLayout(window);
                var scroll = FindActivityScroll((FrameworkElement)window.Content);
                Require(scroll is not null, "no horizontal Activity ScrollViewer found");
                Require(scroll!.ScrollableWidth > 0, $"content not wider than viewport (scrollable={scroll.ScrollableWidth})");
                scroll.Focus(); Pump(10);
                void SendKey(Key key) { scroll.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(scroll), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); Pump(15); }
                SendKey(Key.End); Require(scroll.HorizontalOffset > 0, "End did not scroll to the right");
                SendKey(Key.Home); Require(scroll.HorizontalOffset < 1, "Home did not return to the left");
            }
            finally { window.Close(); RestoreLedger(oldDays); }
        });

        // 4. Header sample text must stay inside bounds after the 94 DIP combo allowance is dropped.
        Check("header sample stays inside bounds", () =>
        {
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
            foreach (var size in new[] { new Size(180, 140), new Size(300, 200), new Size(420, 310) })
            {
                var (window, _) = BuildActivity(ActivityPeriod.HalfYear, size.Width, size.Height, mode);
                try { LiveLayout(window); ValidateText((FrameworkElement)window.Content, size); }
                finally { window.Close(); }
            }
        });

        // 5. A user's manual offset must survive data/layout refreshes (the auto-right-scroll must not snap it).
        Check("user scroll offset preserved through updates", () =>
        {
            var oldDays = FillLedger();
            var (window, widget) = BuildActivity(ActivityPeriod.Year, 300, 200, ThemeMode.Light);
            try
            {
                LiveLayout(window);
                var root = (FrameworkElement)window.Content;
                var scroll = FindActivityScroll(root);
                Require(scroll is not null, "no horizontal Activity ScrollViewer found");
                Require(scroll!.ScrollableWidth > 0, "content not wider than viewport");
                double manual = Math.Round(scroll.ScrollableWidth * 0.5); scroll.ScrollToHorizontalOffset(manual); Pump(20);
                widget.RefreshMetrics(); Pump(20);
                root.Measure(new Size(300, 200)); root.Arrange(new Rect(0, 0, 300, 200)); root.UpdateLayout(); Pump(20);
                Require(scroll.HorizontalOffset < scroll.ScrollableWidth - 1, $"manual offset jumped to the right end ({scroll.HorizontalOffset}/{scroll.ScrollableWidth})");
                Require(Math.Abs(scroll.HorizontalOffset - manual) <= Math.Max(2, scroll.ScrollableWidth * 0.25), $"manual offset not preserved ({manual} -> {scroll.HorizontalOffset})");
            }
            finally { window.Close(); RestoreLedger(oldDays); }
        });

        // 6. Opacity None: transparent surface, readable foreground, no border/shadow, edits reachable.
        //    IsHitTestVisible only proves the synthesized hit map; it does not prove native input.
        Check("Opacity None transparent surface with kept foreground and reachable edits", () =>
        {
            var (window, widget) = BuildActivity(ActivityPeriod.Month, 420, 310, ThemeMode.Light);
            try
            {
                widget.Model.Appearance.Opacity = OpacityLevel.None; widget.RefreshAppearance();
                LiveLayout(window); // grips/menu visibility needs a shown window.
                var root = (FrameworkElement)window.Content;
                var background = (SolidColorBrush)widget.Background;
                Require(background.Color.A == 0, $"None background alpha is {background.Color.A}, expected 0");
                Require(((SolidColorBrush)widget.Ink).Color.A == 255 && ((SolidColorBrush)widget.MutedInk).Color.A == 255, "None must keep opaque foreground text/icon ink");
                var surface = (Border)window.FindName("Surface")!;
                Require(surface.BorderThickness == new Thickness(0), $"None surface border remains: {surface.BorderThickness}");
                Require(surface.Effect is null, "None surface drop shadow remains");
                var grip = AllChildren(root).OfType<Thumb>().FirstOrDefault(t => t.IsVisible && Equals(t.Tag, "RB"));
                Require(grip is not null && grip.IsHitTestVisible, "None widget resize grip not hit-testable (synthetic hit map)");
                var menu = (Button)window.FindName("MenuButton")!;
                Require(menu.IsHitTestVisible, "None widget menu button not hit-testable (synthetic hit map)");
                SaveV10Image(root, Path.Combine(output, "Meeting3-Activity-None.png"));
                // Routed synthetic limit: this does not prove a native click/drag on the layered window.
            }
            finally { window.Close(); }
        });

        // 7. Existing Low opacity must not regress while None is added.
        Check("Low opacity unchanged after None addition", () =>
        {
            var (window, widget) = BuildActivity(ActivityPeriod.Month, 300, 200, ThemeMode.Light);
            try
            {
                widget.Model.Appearance.Opacity = OpacityLevel.Low; widget.RefreshAppearance(); LiveLayout(window);
                Require(((SolidColorBrush)widget.Background).Color.A == (byte)Math.Round(.65 * 255), "Low background opacity changed");
            }
            finally { window.Close(); }
        });

        // 8. Small/normal Activity keeps the recent-week rule and shows no body selector.
        Check("small/normal activity has no body selector", () =>
        {
            foreach (var size in new[] { new Size(180, 140), new Size(300, 200) })
            {
                var (window, _) = BuildActivity(ActivityPeriod.Week, size.Width, size.Height, ThemeMode.Light);
                try { LiveLayout(window); Require(!HasBodyPeriodCombo((FrameworkElement)window.Content), "small/normal Activity still shows a body period ComboBox"); ValidateText((FrameworkElement)window.Content, size); }
                finally { window.Close(); }
            }
        });

        passed += good; failed += bad;
        Console.WriteLine($"Meeting3 checks: {good} passed, {bad} failed. Draft; see docs/wiki/reports/2026-10-14-meeting3-implementation-deepseek.md");
    }
}
