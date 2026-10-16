using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Nook.Core;
using Nook.App.Services;
using Nook.App.ViewModels;
using Nook.App.Views;
using ThemeMode = Nook.Core.ThemeMode;

internal static partial class Program
{
    // Native (OS-message) checks for the third-meeting fixes. DRAFT owned by DeepSeek; the
    // App/hook contract and the shared runner are Claude's. Every HWND here is created by
    // this test; messages are INJECTED (SendMessage), not physical hardware input, and the
    // user cursor is never moved. WindowFromPoint proves OS hit delivery to the layered
    // HWND, not WPF IsHitTestVisible. All checks are unexecuted drafts; matrix in
    // docs/wiki/reports/2026-10-14-meeting3-native-deepseek.md. Missing capability FAILS.
    [StructLayout(LayoutKind.Sequential)] private struct M3NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct M3NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    // Managed prefixes are kept; EntryPoint names the real exports explicitly (no name mangling).
    [DllImport("user32.dll", EntryPoint = "WindowFromPoint", SetLastError = true)] private static extern IntPtr M3NativeWindowFromPoint(M3NativePoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)] private static extern bool M3NativeGetWindowRect(IntPtr hwnd, out M3NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr M3NativeSendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool M3NativePostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    private const int M3NativeWM_MOUSEWHEEL = 0x020A;
    private const int M3NativeWM_MOUSEHWHEEL = 0x020E;

    private static void RunMeeting3NativeChecks(string output, AppViewModel vm, AppSettings settings, ThemeService theme, ref int passed, ref int failed)
    {
        int good = 0, bad = 0;
        void Check(string name, Action test)
        { try { test(); good++; Console.WriteLine("PASS M3Native " + name); } catch (Exception ex) { bad++; Console.WriteLine("FAIL M3Native " + name + ": " + ex); } }
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        static IntPtr M3NativeLParam(int x, int y) => new(((y & 0xFFFF) << 16) | (x & 0xFFFF));
        static IntPtr M3NativeDelta(int delta) => new(delta << 16); // signed wheel delta in the high word
        static ScrollViewer? M3NativeFindScroll(FrameworkElement root) =>
            AllChildren(root).OfType<ScrollViewer>().FirstOrDefault(s => AutomationProperties.GetName(s) == "ActivityScroll")
            ?? AllChildren(root).OfType<ScrollViewer>().FirstOrDefault(s => s.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto
                && s.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled);

        // 1. OS-level hit delivery: blank title drag region and the resize grip must resolve to the widget HWND.
        Check("native WindowFromPoint reaches widget at title/grip", () =>
        {
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
            foreach (var shape in new[] { WidgetShape.Card, WidgetShape.Circle })
            {
                using var own = new AppViewModel(new(), new SettingsStore(Path.Combine(output, $"m3-native-hit-{mode}-{shape}")), vm.Metrics, theme);
                var model = new WidgetInstance
                {
                    Kind = WidgetKind.Cpu, Shape = shape, Theme = mode,
                    Width = shape == WidgetShape.Circle ? 300 : 300, Height = shape == WidgetShape.Circle ? 300 : 200,
                };
                model.Appearance.Opacity = OpacityLevel.None;
                var window = new WidgetWindow(own, new WidgetViewModel(own, model));
                try
                {
                    window.Show(); Pump(80);
                    var hwnd = new WindowInteropHelper(window).Handle;
                    Require(hwnd != IntPtr.Zero, "no HWND after Show");
                    var root = (FrameworkElement)window.Content;
                    // OS hit-test acceptance candidate (Claude to confirm the exact blank pixel):
                    // card uses the visible TitleArea row off the left title text / right menu;
                    // circle uses the upper Surface interior ABOVE its centred title+value, since
                    // the circle centre lands on the opaque percentage text, not blank space.
                    Point titleScreen;
                    if (shape == WidgetShape.Card)
                    {
                        var titleArea = window.FindName("TitleArea") as Grid;
                        Require(titleArea is not null && titleArea.ActualWidth > 0, "visible TitleArea not found");
                        titleScreen = titleArea!.PointToScreen(new Point(titleArea.ActualWidth * 0.55, titleArea.ActualHeight / 2));
                    }
                    else
                    {
                        var surface = window.FindName("Surface") as Border;
                        Require(surface is not null && surface.ActualWidth > 0, "visible Surface not found");
                        titleScreen = surface!.PointToScreen(new Point(surface.ActualWidth * 0.5, surface.ActualHeight * 0.2));
                    }
                    var title = new M3NativePoint { X = (int)Math.Round(titleScreen.X), Y = (int)Math.Round(titleScreen.Y) };
                    var titleHit = M3NativeWindowFromPoint(title);
                    Require(titleHit == hwnd, $"title {title.X},{title.Y} resolved to {titleHit:X} not widget {hwnd:X}");
                    // Actual visible RB grip centre, not a rectangle-edge guess (circles have no corner).
                    var grip = AllChildren(root).OfType<Thumb>().FirstOrDefault(t => t.IsVisible && Equals(t.Tag, "RB") && t.ActualWidth > 0 && t.ActualHeight > 0);
                    Require(grip is not null, "visible RB resize grip not found");
                    var gripScreen = grip!.PointToScreen(new Point(grip.ActualWidth / 2, grip.ActualHeight / 2));
                    var gripPoint = new M3NativePoint { X = (int)Math.Round(gripScreen.X), Y = (int)Math.Round(gripScreen.Y) };
                    var gripHit = M3NativeWindowFromPoint(gripPoint);
                    Require(gripHit == hwnd, $"grip {gripPoint.X},{gripPoint.Y} resolved to {gripHit:X} not widget {hwnd:X}");
                }
                finally { window.Close(); }
            }
        });

        // 2. Injected native horizontal-wheel message (WM_MOUSEHWHEEL) must move the Activity offset.
        Check("native WM_MOUSEHWHEEL scrolls Activity horizontally", () =>
        {
            using var own = new AppViewModel(new(), new SettingsStore(Path.Combine(output, "m3-native-hwheel")), vm.Metrics, theme);
            SeedLedger(own);
            var window = new WidgetWindow(own, new WidgetViewModel(own, new() { Kind = WidgetKind.Activity, ActivityPeriod = ActivityPeriod.Year, Theme = ThemeMode.Light, Width = 300, Height = 200 }));
            try
            {
                window.Show(); Pump(80);
                var root = (FrameworkElement)window.Content;
                var scroll = M3NativeFindScroll(root);
                Require(scroll is not null, "no horizontal Activity ScrollViewer found");
                Require(scroll!.ScrollableWidth > 0, $"content not wider than viewport ({scroll.ScrollableWidth})");
                scroll.ScrollToHorizontalOffset(0); Pump(20); double before = scroll.HorizontalOffset;
                var p = scroll.PointToScreen(new Point(Math.Max(1, scroll.ActualWidth / 2), Math.Max(1, scroll.ActualHeight / 2)));
                var hwnd = new WindowInteropHelper(window).Handle;
                M3NativeSendMessage(hwnd, M3NativeWM_MOUSEHWHEEL, M3NativeDelta(120), M3NativeLParam((int)Math.Round(p.X), (int)Math.Round(p.Y)));
                Pump(80);
                Require(scroll.HorizontalOffset != before, $"injected WM_MOUSEHWHEEL did not scroll ({before} -> {scroll.HorizontalOffset})");
            }
            finally { window.Close(); ClearLedger(own); }
        });

        // 3. Feasible bounded ordinary native wheel message path (WM_MOUSEWHEEL at the scroll point).
        Check("native WM_MOUSEWHEEL reaches Activity scroll", () =>
        {
            using var own = new AppViewModel(new(), new SettingsStore(Path.Combine(output, "m3-native-wheel")), vm.Metrics, theme);
            SeedLedger(own);
            var window = new WidgetWindow(own, new WidgetViewModel(own, new() { Kind = WidgetKind.Activity, ActivityPeriod = ActivityPeriod.Year, Theme = ThemeMode.Light, Width = 300, Height = 200 }));
            try
            {
                window.Show(); Pump(80);
                var root = (FrameworkElement)window.Content;
                var scroll = M3NativeFindScroll(root);
                Require(scroll is not null, "no horizontal Activity ScrollViewer found");
                Require(scroll!.ScrollableWidth > 0, "content not wider than viewport");
                scroll.ScrollToHorizontalOffset(0); Pump(20); double before = scroll.HorizontalOffset;
                var p = scroll.PointToScreen(new Point(Math.Max(1, scroll.ActualWidth / 2), Math.Max(1, scroll.ActualHeight / 2)));
                var hwnd = new WindowInteropHelper(window).Handle;
                M3NativeSendMessage(hwnd, M3NativeWM_MOUSEWHEEL, M3NativeDelta(-120), M3NativeLParam((int)Math.Round(p.X), (int)Math.Round(p.Y)));
                Pump(80);
                Require(scroll.HorizontalOffset != before, $"injected WM_MOUSEWHEEL did not scroll ({before} -> {scroll.HorizontalOffset})");
            }
            finally { window.Close(); ClearLedger(own); }
        });

        void SeedLedger(AppViewModel target)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            target.ScreenTime.Ledger.Days.Clear();
            for (int i = 0; i < 366; i++) target.ScreenTime.Ledger.Days.Add(new() { Date = today.AddDays(-i), Apps = [new() { Id = "code", Name = "Code", Color = "#408DC1", Seconds = i % 5 * 4500 }] });
        }
        void ClearLedger(AppViewModel target) => target.ScreenTime.Ledger.Days.Clear();

        passed += good; failed += bad;
        Console.WriteLine($"M3Native checks: {good} passed, {bad} failed. Draft; note injected messages are not physical hardware.");
    }
}
