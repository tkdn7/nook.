using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Nook.Core;
using Nook.App.Services;
using Nook.App.ViewModels;
using Nook.App.Views;
using ThemeMode = Nook.Core.ThemeMode;

internal static partial class Program
{
    private sealed class CommitFailure(IPresetWindowChange inner) : IPresetWindowChange
    {
        public void Commit() { inner.Commit(); throw new InvalidOperationException("injected commit failure"); }
        public void Complete() => inner.Complete();
        public void Dispose() => inner.Dispose();
    }
    private static void RunApprovedChecks(string output, ThemeService theme, ref int passed, ref int failed)
    {
        int good = 0, bad = 0;
        void Check(string name, Action action)
        { try { action(); good++; Console.WriteLine("PASS approved " + name); } catch (Exception ex) { bad++; Console.WriteLine("FAIL approved " + name + ": " + ex); } }
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        SystemMetricsService Metrics() => new(Dispatcher.CurrentDispatcher,
            () => new(DateTimeOffset.Now, 25, "", false), () => new(DateTimeOffset.Now, 100, 200, false), false);

        Check("prepared windows, commit rollback, disk state and single Undo", () =>
        {
            using var metrics = Metrics();
            var store = new SettingsStore(Path.Combine(output, "approved-presets"));
            var config = new AppSettings { MainTheme = ThemeMode.Light };
            using var own = new AppViewModel(config, store, metrics, theme);
            var memo = own.Add(WidgetKind.Memo); memo.Model.MemoText = "A contents";
            var timer = own.Add(WidgetKind.Timer); CountdownEngine.Toggle(timer.Model.Countdown, false, DateTimeOffset.UtcNow);
            var a = own.SavePreset("A");
            var b = own.SavePreset("B"); b.Widgets[0].Left += 140; b.Widgets[0].MemoText = "B contents";
            config.ActivePresetId = a.Id; own.Save();
            var main = new MainWindow(own); main.Show();
            using var coordinator = new WindowCoordinator(own, main); Pump(30); coordinator.CaptureAll(); own.Save();
            var window = Application.Current.Windows.OfType<WidgetWindow>().Single(w => ReferenceEquals(w.DataContext, memo));
            var saved = File.ReadAllText(store.FilePath);
            var factory = own.PreparePresetWindows!;
            try
            {
                own.PreparePresetWindows = _ => throw new InvalidOperationException("injected preparation failure");
                Require(!own.ApplyPreset(b), "Preparation unexpectedly succeeded");
                Require(window.IsVisible && ReferenceEquals(own.Widgets[0], memo), "Old window/model was removed during preparation");
                Require(File.ReadAllText(store.FilePath) == saved, "Preparation failure wrote settings");
                own.PreparePresetWindows = next => new CommitFailure(factory(next));
                Require(!own.ApplyPreset(b), "Commit unexpectedly succeeded"); Pump(20);
                Require(window.IsVisible && ReferenceEquals(own.Widgets[0], memo), "Failed commit did not keep old desktop");
                Require(Application.Current.Windows.OfType<WidgetWindow>().Count(w => w.DataContext is WidgetViewModel v && own.Widgets.Contains(v)) == 2, "Window rollback mismatch");
                Require(File.ReadAllText(store.FilePath) == saved && config.ActivePresetId == a.Id, "Commit failure changed persisted layout");
                Require(!own.CanUndoPreset && !string.IsNullOrEmpty(own.PresetError), "Failure feedback/Undo wrong");
                own.PreparePresetWindows = factory;
                Require(own.ApplyPreset(b) && own.CanUndoPreset, "Successful apply did not enable Undo"); Pump(20);
                Require(!Application.Current.Windows.Cast<Window>().Contains(window), "Old window remained after successful commit");
                Require(own.Widgets[0].Model.MemoText == "B contents", "Independent memo lost");
                Require(own.Widgets[1].Model.Countdown.Deadline == timer.Model.Countdown.Deadline, "Running timer reset");
                Require(own.UndoPreset() && !own.CanUndoPreset && !own.UndoPreset(), "Undo must be available once"); Pump(20);
                Require(config.ActivePresetId == a.Id && own.Widgets[0].Model.MemoText == "A contents", "Undo did not restore previous memo/layout");
                Require(store.Load().ActivePresetId == a.Id, "Undo not persisted");
            }
            finally { main.Exiting = true; main.Close(); }
        });
        Check("save failure restores working layout and keeps Undo unavailable", () =>
        {
            using var metrics = Metrics();
            string blocked = Path.Combine(output, "approved-blocked-folder"); File.WriteAllText(blocked, "not a directory");
            var state = new AppSettings { Widgets = [new() { Kind = WidgetKind.Memo, MemoText = "before" }] };
            using var own = new AppViewModel(state, new SettingsStore(blocked), metrics, theme);
            var preset = LayoutPresets.Capture(state, "B"); preset.Widgets[0].MemoText = "after"; state.Presets.Add(preset);
            var model = own.Widgets[0];
            Require(!own.ApplyPreset(preset), "Save failure unexpectedly succeeded");
            Require(ReferenceEquals(model, own.Widgets[0]) && state.Widgets[0].MemoText == "before", "Memory state not restored");
            Require(!own.IsApplyingPreset && !own.CanUndoPreset && own.PresetError.Length > 0, "Failed transaction state leaked");
        });
        Check("zero unused GPU/disk queries; one shared demand for multiple widgets", () =>
        {
            using var metrics = Metrics();
            for (int i = 0; i < 10; i++) { metrics.SampleNow(); Pump(2); }
            Require(metrics.GpuQueriesScheduled == 0 && metrics.DiskQueriesScheduled == 0, "Unused counters queried");
            using var own = new AppViewModel(new(), new SettingsStore(Path.Combine(output, "approved-demand")), metrics, theme);
            var first = own.Add(WidgetKind.Gpu); var second = own.Add(WidgetKind.Gpu);
            metrics.SampleNow(); Require(metrics.GpuQueriesScheduled == 1 && metrics.DiskQueriesScheduled == 0, "Duplicate widget created duplicate query");
            own.Toggle(first); Require(metrics.GpuRequested, "Second GPU consumer lost demand");
            own.Toggle(second); Require(!metrics.GpuRequested, "Hidden consumers kept GPU sampling"); Pump(15); metrics.SampleNow();
            long queries = metrics.GpuQueriesScheduled; for (int i = 0; i < 8; i++) { Pump(2); metrics.SampleNow(); }
            Require(metrics.GpuQueriesScheduled == queries, "Hidden GPU keeps querying");
            own.SetLibraryVisible(true); Require(metrics.GpuRequested && metrics.DiskRequested, "Library demand missing");
            own.SetLibraryVisible(false); Require(!metrics.GpuRequested && !metrics.DiskRequested, "Hidden library demand leaked");
            File.WriteAllText(Path.Combine(output, "approved-query-counts.txt"), "Legacy schedules GPU and Disk on every tick, even without consumers.\nNew: 10 consumer-free pulses scheduled 0 GPU / 0 Disk queries; two GPU widgets scheduled one shared initial query.\nThese are query counts, not a claim about end-user CPU or memory percentages.\n");
        });
        Check("retired in-flight sample cannot leak after hide/re-enable", () =>
        {
            using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            int calls = 0;
            using var metrics = new SystemMetricsService(Dispatcher.CurrentDispatcher,
                () => { int n = Interlocked.Increment(ref calls); if (n == 1) { started.Set(); release.Wait(5000); } return new(DateTimeOffset.Now, n == 1 ? 99 : 17, "", false); }, null, false);
            metrics.SetDemand(true, false); metrics.SampleNow(); Require(started.Wait(2000), "Query did not start");
            metrics.SetDemand(false, false); metrics.SetDemand(true, false); metrics.SampleNow();
            Require(metrics.GpuQueriesScheduled == 1, "Overlapping query launched"); release.Set(); Pump(50); metrics.SampleNow();
            Require(metrics.Gpu.Usage is null && metrics.GpuHistory.Points.Count == 0, "Retired result entered history");
            Pump(30); metrics.SampleNow(); Require(metrics.Gpu.Usage == 17, "Fresh generation missing");
        });
        Check("hidden content skips renders; records and countdown remain active", () =>
        {
            using var metrics = Metrics();
            var state = new AppSettings { Widgets = [new() { Kind = WidgetKind.ScreenTime, Visible = false }, new() { Kind = WidgetKind.KeyStats, Visible = false }, new() { Kind = WidgetKind.Timer, Visible = false }] };
            using var own = new AppViewModel(state, new SettingsStore(Path.Combine(output, "approved-hidden")), metrics, theme);
            var timer = own.Widgets.Last(); CountdownEngine.Toggle(timer.Model.Countdown, false, DateTimeOffset.UtcNow); var deadline = timer.Model.Countdown.Deadline;
            int refreshes = 0; foreach (var widget in own.Widgets) widget.FeatureUpdated += () => refreshes++;
            metrics.SampleNow(); Pump(5);
            Require(refreshes == 0 && timer.Model.Countdown.Deadline == deadline, "Hidden render or timer reset");
            var enabled = typeof(ScreenTimeService).GetField("_enabled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Require((bool)enabled.GetValue(own.ScreenTime)!, "Hidden screen-time tracker disabled");
            var hook = typeof(KeyStatsService).GetField("_hook", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Require((IntPtr)hook.GetValue(own.Keys)! != IntPtr.Zero, "Hidden key tracker disabled");
            own.Toggle(timer); Require(refreshes > 0, "Reveal did not refresh latest state");
        });
        Check("compact titles preserve full/custom names and Clock palette", () =>
        {
            using var metrics = Metrics(); using var own = new AppViewModel(new() { Theme = ThemeMode.Dark }, new SettingsStore(Path.Combine(output, "approved-titles")), metrics, theme);
            var widget = new WidgetViewModel(own, new() { Kind = WidgetKind.MediaController }); widget.SetSize(180, 180);
            Require(widget.CompactDisplayTitle == "출력" && widget.Title == "Media Controller", "Compact/full title wrong");
            widget.Model.Name = "내 스피커"; widget.RefreshAppearance(); Require(widget.CompactDisplayTitle == "내 스피커", "Custom title lost");
            var clock = new WidgetViewModel(own, new() { Kind = WidgetKind.Clock });
            Require(((SolidColorBrush)clock.Background).Color.R > 200, "Clock card inversion changed");
            clock.Model.Shape = WidgetShape.Circle; clock.Model.ClockStyle = ClockStyle.Analog; clock.RefreshAppearance();
            Require(((SolidColorBrush)clock.Background).Color.R < 100, "Analog circle palette changed");
        });
        passed += good; failed += bad;
    }
}
