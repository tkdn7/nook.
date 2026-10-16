using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nook.Core;
using Nook.App.Controls;
using Nook.App.Services;
using Nook.App.ViewModels;
using ThemeMode = Nook.Core.ThemeMode;

internal static partial class Program
{
    // Scroll-state checks for Codex-owned ActivityScrollViewer (read-only dependency; src is
    // not edited). Draft owned by DeepSeek; entrypoint wiring and the shared runner are
    // Claude's. Each case uses a fresh WidgetInstance and its own shown host Window, closed in
    // finally; no user cursor/app/settings operations. Real layout and offsets are asserted
    // with Pump, so a missing behavior FAILS instead of mirroring the implementation. All
    // cases are unexecuted drafts.
    private static void RunMeeting3ScrollStateChecks(string output, AppViewModel vm, AppSettings settings, ThemeService theme, ref int passed, ref int failed)
    {
        int good = 0, bad = 0;
        void Check(string name, Action test)
        { try { test(); good++; Console.WriteLine("PASS M3Scroll " + name); } catch (Exception ex) { bad++; Console.WriteLine("FAIL M3Scroll " + name + ": " + ex); } }
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        void Near(double expected, double actual, double tolerance, string what)
        { if (Math.Abs(expected - actual) > tolerance) throw new Exception($"{what}: expected {expected:F1}, got {actual:F1}"); }

        const double Wide = 2000, Viewport = 220, Height = 60;
        (Window Host, ActivityScrollViewer Scroll) Show(WidgetInstance model, double contentWidth)
        {
            var scroll = new ActivityScrollViewer(model) { Width = Viewport, Height = Height, Content = new Border { Width = contentWidth, Height = 40, Background = Brushes.Transparent } };
            var host = new Window { Width = Viewport + 40, Height = Height + 40, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Content = scroll };
            host.Show(); Pump(80);
            return (host, scroll);
        }
        void SetContent(ActivityScrollViewer scroll, double contentWidth) => scroll.Content = new Border { Width = contentWidth, Height = 40, Background = Brushes.Transparent };
        void Resize(ActivityScrollViewer scroll, double contentWidth) { SetContent(scroll, contentWidth); Pump(60); }

        // 1. A fresh control with overflow starts at the latest (right) end.
        Check("default initial offset is the latest right end", () =>
        {
            var (host, scroll) = Show(new WidgetInstance(), Wide);
            try
            {
                Require(scroll.ScrollableWidth > 100, $"expected overflow, scrollable={scroll.ScrollableWidth:F1}");
                Near(scroll.ScrollableWidth, scroll.HorizontalOffset, 2, "initial latest offset");
            }
            finally { host.Close(); }
        });

        // 2. Two ScrollBy(-40) calls made before the pump accumulate 80 back from the right end.
        Check("consecutive ScrollBy(-40) before layout accumulate 80", () =>
        {
            var (host, scroll) = Show(new WidgetInstance(), Wide);
            try
            {
                Require(scroll.ScrollableWidth > 100, "expected overflow");
                double right = scroll.ScrollableWidth;
                scroll.ScrollBy(-40); scroll.ScrollBy(-40);
                Pump(60);
                Near(right - 80, scroll.HorizontalOffset, 2, "accumulated 80 from right end");
            }
            finally { host.Close(); }
        });

        // 3. A manual scroll after an extent change cancels the queued automatic restore.
        Check("manual scroll cancels pending latest restore after extent change", () =>
        {
            var (host, scroll) = Show(new WidgetInstance(), Wide);
            try
            {
                Require(scroll.ScrollableWidth > 700, "expected overflow");
                Near(scroll.ScrollableWidth, scroll.HorizontalOffset, 2, "start at latest right end");
                SetContent(scroll, Wide + 400);   // extent change queues a latest restore, no pump
                scroll.UpdateLayout();            // force the extent-change ScrollChanged; the Render restore stays queued
                double captured = scroll.HorizontalOffset;
                scroll.ScrollBy(-40);             // manual input before the dispatcher runs must win
                Pump(60);
                Near(captured - 40, scroll.HorizontalOffset, 3, "manual scroll after cancelling the latest restore");
            }
            finally { host.Close(); }
        });

        // 4. A new control for the SAME model restores the manual historical offset, not the right end.
        Check("new control same model restores manual offset", () =>
        {
            var model = new WidgetInstance();
            double manual;
            var (first, firstScroll) = Show(model, Wide);
            try
            {
                Require(firstScroll.ScrollableWidth > 700, "expected overflow");
                firstScroll.ScrollBy(-500); Pump(60);
                manual = firstScroll.HorizontalOffset;
                Require(manual > 6 && manual < firstScroll.ScrollableWidth - 6, $"manual offset not historical: {manual:F1}");
            }
            finally { first.Close(); }

            var (second, secondScroll) = Show(model, Wide);
            try
            {
                Require(secondScroll.ScrollableWidth > 700, "expected overflow");
                Near(manual, secondScroll.HorizontalOffset, 3, "restored manual offset");
            }
            finally { second.Close(); }
        });

        // 5. A no-overflow small view then a wide replacement keeps the earlier historical offset.
        Check("no-overflow small then wide replacement preserves historical offset", () =>
        {
            var (host, scroll) = Show(new WidgetInstance(), Wide);
            try
            {
                Require(scroll.ScrollableWidth > 700, "expected overflow");
                scroll.ScrollBy(-700); Pump(60);
                double historical = scroll.HorizontalOffset;
                Resize(scroll, Viewport - 40);    // no overflow: history must not be overwritten
                Near(0, scroll.ScrollableWidth, 3, "small view should not overflow");
                Resize(scroll, Wide);             // wide replacement restores the earlier position
                Require(scroll.ScrollableWidth > 700, "expected overflow after replacement");
                Near(historical, scroll.HorizontalOffset, 3, "historical offset after wide replacement");
            }
            finally { host.Close(); }
        });

        passed += good; failed += bad;
        Console.WriteLine($"M3Scroll checks: {good} passed, {bad} failed. Draft; see docs/wiki/reports/2026-10-14-meeting3-scrollstate-deepseek.md");
    }
}
