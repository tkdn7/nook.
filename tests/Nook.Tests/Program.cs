using Nook.Core;

int passed = 0, failed = 0;
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
void Equal<T>(T expected, T actual)
{ if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
void True(bool value) { if (!value) throw new Exception("Expected true"); }

Check("Screen time: app switching, idle, suspend, restart do not inflate time", () =>
{
    var ledger = new UsageLedger(); var time = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(9));
    var a = new UsageApp("a", "App A", "#7296B8"); var b = new UsageApp("b", "App B", "#BD8E72");
    ledger.Sample(0, time, a); ledger.Sample(1000, time.AddSeconds(1), a);
    ledger.Sample(2000, time.AddSeconds(2), b); ledger.Sample(3000, time.AddSeconds(3), b);
    ledger.Sample(4000, time.AddSeconds(4), null); ledger.Sample(5000, time.AddSeconds(5), b);
    ledger.Sample(6000, time.AddSeconds(6), b); ledger.Sample(606000, time.AddSeconds(606), b);
    ledger.Sample(607000, time.AddSeconds(607), b); ledger.Reset(); ledger.Sample(608000, time.AddSeconds(608), a);
    var day = ledger.Today(DateOnly.FromDateTime(time.DateTime));
    Equal(5.0, day.TotalSeconds); Equal(2.0, day.Apps.Single(x => x.Id == "a").Seconds); Equal(3.0, day.Apps.Single(x => x.Id == "b").Seconds);
});
Check("Countdown: pause, restore, finish once and restart", () =>
{
    var now = DateTimeOffset.UtcNow; var o = new CountdownOptions { TimerSeconds = 60 };
    CountdownEngine.Toggle(o, false, now); Equal(60.0, CountdownEngine.Remaining(o, false, now));
    CountdownEngine.Toggle(o, false, now.AddSeconds(20)); Equal(40.0, o.RemainingSeconds); True(o.Deadline is null);
    CountdownEngine.Toggle(o, false, now.AddHours(1)); Equal(40.0, CountdownEngine.Remaining(o, false, now.AddHours(1)));
    True(CountdownEngine.Tick(o, false, now.AddHours(2))); True(o.Finished); True(!CountdownEngine.Tick(o, false, now.AddHours(3)));
    CountdownEngine.Toggle(o, false, now.AddHours(3)); Equal(60.0, CountdownEngine.Remaining(o, false, now.AddHours(3))); True(!o.Finished);
    CountdownEngine.Reset(o); True(o.Deadline is null); Equal("01:00", CountdownEngine.Display(CountdownEngine.Remaining(o, false, now))); Equal("1:00:00", CountdownEngine.Display(3600));
});
Check("Pomodoro: four focus sessions and long break, expired restart stops", () =>
{
    var o = new CountdownOptions { FocusMinutes = 1, BreakMinutes = 1 }; var now = DateTimeOffset.UtcNow;
    for (int i = 1; i <= 4; i++)
    { CountdownEngine.Toggle(o, true, now); True(CountdownEngine.Tick(o, true, now.AddMinutes(2))); Equal(i, o.CompletedFocus); Equal(i == 4 ? FocusPhase.LongBreak : FocusPhase.ShortBreak, o.Phase); True(o.Deadline is null); CountdownEngine.Next(o); Equal(FocusPhase.Focus, o.Phase); }
});
Check("Network: measured rates, reset counters, resume gap, timestamp reversal", () =>
{
    var d = new RateDelta(); var t = DateTimeOffset.UtcNow;
    True(d.Sample(100, 100, t, "adapter").Download is null);
    var sample = d.Sample(2148, 1124, t.AddSeconds(2), "adapter"); Equal(1024.0, sample.Download!.Value); Equal(512.0, sample.Upload!.Value);
    True(d.Sample(1, 1, t.AddSeconds(3), "adapter").Upload is null); True(d.Sample(20, 20, t.AddSeconds(20), "adapter").Download is null); True(d.Sample(30, 30, t.AddSeconds(19), "adapter").Download is null);
});
Check("Key counts: total only, ignore repeats, modifiers and injected keys", () =>
{
    var c = new KeyCounts(); True(c.Record(65, true)); True(!c.Record(65, true)); c.Record(65, false); True(c.Record(65, true));
    True(!c.Record(16, true)); True(!c.Record(66, true, true)); c.Record(66, true); c.Record(67, true); c.Record(68, true);
    Equal(5L, c.Total); c.ReleaseAll(); True(c.Record(65, true)); Equal(6L, c.Total);
});
Check("Catalog: every widget searchable by category and Korean keywords", () =>
{
    Equal(Enum.GetValues<WidgetKind>().Length, WidgetCatalog.All.Count); Equal(WidgetKind.Network, WidgetCatalog.Search("시스템", "인터넷").Single().Kind);
    True(!WidgetCatalog.Search("일상", "인터넷").Any()); Equal(WidgetKind.Dice, WidgetCatalog.Search("놀이", "DICE").Single().Kind); Equal(WidgetKind.KeyStats, WidgetCatalog.Search("전체", "타자").Single().Kind);
});
Check("Screen time: midnight splits the observed interval", () =>
{
    var ledger = new UsageLedger(); var time = new DateTimeOffset(2026, 10, 11, 23, 59, 59, 500, TimeSpan.FromHours(9));
    var a = new UsageApp("a", "App A", "#7296B8"); ledger.Sample(0, time, a); ledger.Sample(1000, time.AddSeconds(1), a);
    Equal(.5, ledger.Today(new(2026, 10, 11)).TotalSeconds); Equal(.5, ledger.Today(new(2026, 10, 12)).TotalSeconds);
});
Check("Screen time: ETC threshold, exact boundary, hover members and percentages", () =>
{
    var day = new UsageDay { Apps = [new() { Id = "a", Name = "A", Seconds = 90 }, new() { Id = "b", Name = "B", Seconds = 3 }, new() { Id = "c", Name = "C", Seconds = 2 }, new() { Id = "unknown", Name = "ETC", Seconds = 5 }] };
    var shares = UsageLedger.Shares(day, 3); Equal(3, shares.Count); Equal("b", shares[1].Id); True(Math.Abs(7 - shares[^1].Percentage) < .00001);
    True(shares[^1].Detail.Contains("C")); True(Math.Abs(100 - shares.Sum(x => x.Percentage)) < .00001);
    Equal(2, UsageLedger.Shares(day, 5).Count); Equal(0, UsageLedger.Shares(new()).Count);
});
Check("Screen time: clock reversal and long gaps are ignored", () =>
{
    var ledger = new UsageLedger(); var time = DateTimeOffset.Now; var app = new UsageApp("a", "A", "#7296B8");
    ledger.Sample(1000, time, app); ledger.Sample(2000, time.AddMinutes(-1), app); ledger.Sample(3000, time.AddSeconds(2), app);
    Equal(0.0, ledger.Today(DateOnly.FromDateTime(time.DateTime)).TotalSeconds);
    ledger.Sample(4000, time.AddSeconds(3), app); Equal(1.0, ledger.Today(DateOnly.FromDateTime(time.DateTime)).TotalSeconds);
});

Check("Level controls: angle wrap, dead zone and bounds", () =>
{
    Equal(0.0, LevelMath.FromAngle(135)); Equal(100.0, LevelMath.FromAngle(45));
    Equal(50.0, LevelMath.FromAngle(270));
    True(LevelMath.DragAngle(50, 179, -179) > 50 && LevelMath.DragAngle(50, 179, -179) < 51);
    Equal(100.0, LevelMath.DragAngle(99, 10, 40)); Equal(0.0, LevelMath.DragAngle(1, 40, 10));
    True(LevelMath.Vertical(ControlOrientation.Automatic, 180, 480));
    True(!LevelMath.Vertical(ControlOrientation.Horizontal, 180, 480));
    True(LevelMath.Vertical(ControlOrientation.Vertical, 640, 140));
});

Check("Appearance: per-property inheritance and global changes", () =>
{
    var s = new AppSettings(); var g = new WidgetGroup { Name = "시스템", Appearance = new() { Radius = Level.High } }; s.Groups.Add(g);
    var w = new WidgetInstance { GroupId = g.Id, Appearance = new() { Opacity = OpacityLevel.Low } }; s.Widgets.Add(w);
    var r = AppearanceResolver.Resolve(s, w);
    Equal(Level.High, r.Radius.Value); Equal("그룹: 시스템", r.Radius.Source); Equal(OpacityLevel.Low, r.Opacity.Value);
    s.Radius = Level.Low; s.Opacity = OpacityLevel.Mid;
    r = AppearanceResolver.Resolve(s, w); Equal(Level.High, r.Radius.Value); Equal(OpacityLevel.Low, r.Opacity.Value);
    w.Appearance.Radius = Level.Mid; Equal("개별 위젯", AppearanceResolver.Resolve(s, w).Radius.Source);
    w.Appearance = new(); Equal(OpacityLevel.Mid, AppearanceResolver.Resolve(s, w).Opacity.Value);
});
Check("Appearance: reassignment, deletion preserves individual values, reset", () =>
{
    var s = new AppSettings(); var a = new WidgetGroup { Appearance = new() { Radius = Level.High } };
    var b = new WidgetGroup { Appearance = new() { Radius = Level.Low } }; s.Groups.AddRange([a, b]);
    var w = new WidgetInstance { GroupId = a.Id, Appearance = new() { Opacity = OpacityLevel.Low } }; s.Widgets.Add(w);
    w.GroupId = b.Id; Equal(Level.Low, AppearanceResolver.Resolve(s, w).Radius.Value);
    AppearanceResolver.DeleteGroup(s, b.Id); Equal<Guid?>(null, w.GroupId); Equal(OpacityLevel.Low, w.Appearance.Opacity);
    Equal(s.Radius, AppearanceResolver.Resolve(s, w).Radius.Value);
    AppearanceResolver.ResetAll(s); Equal<OpacityLevel?>(null, w.Appearance.Opacity); Equal<Level?>(null, a.Appearance.Radius);
});
Check("Layout: both axes, thresholds and hysteresis", () =>
{
    Equal(LayoutMode.Small, ResponsiveLayout.Resolve(640, 140, LayoutMode.Small));
    Equal(LayoutMode.Small, ResponsiveLayout.Resolve(180, 480, LayoutMode.Small));
    Equal(LayoutMode.Mid, ResponsiveLayout.Resolve(280, 180, LayoutMode.Small));
    Equal(LayoutMode.Big, ResponsiveLayout.Resolve(360, 260, LayoutMode.Mid));
    Equal(LayoutMode.Big, ResponsiveLayout.Resolve(352, 252, LayoutMode.Big));
    Equal(LayoutMode.Mid, ResponsiveLayout.Resolve(351, 251, LayoutMode.Big));
    Equal(LayoutMode.Mid, ResponsiveLayout.Resolve(272, 172, LayoutMode.Mid));
    Equal(LayoutMode.Small, ResponsiveLayout.Resolve(271, 172, LayoutMode.Mid));
});
Check("CPU: initial, idle, load and invalid deltas", () =>
{
    var cpu = new CpuCalculator(); Equal<double?>(null, cpu.Sample(new(100, 200, 100)));
    Equal<double?>(0, cpu.Sample(new(200, 300, 100)));
    Equal<double?>(75, cpu.Sample(new(225, 350, 150)));
    Equal<double?>(100, cpu.Sample(new(225, 400, 200)));
    Equal<double?>(null, cpu.Sample(new(225, 400, 200)));
    Equal<double?>(null, cpu.Sample(new(1, 1, 1)));
    Equal<double?>(null, cpu.Sample(new(100, 2, 2)));
    cpu.Reset(); Equal<double?>(null, cpu.Sample(new(100, 200, 300)));
});
Check("History: no synthetic samples, time-based expiry and gaps", () =>
{
    var h = new MetricHistory(); var time = DateTimeOffset.Now; Equal(0, h.Points.Count);
    for (int i = 0; i < 70; i++) h.Add(time.AddSeconds(i), i == 68 ? null : 30);
    Equal(60, h.Points.Count); Equal<double?>(null, h.Points[^2].Value);
    h.Add(time.AddSeconds(200), 10); Equal(1, h.Points.Count); h.Clear(); Equal(0, h.Points.Count);
});
Check("Placement: negative monitor coordinates and disconnected monitor", () =>
{
    var work = new ScreenRect(-1920, 0, 1920, 1040);
    var inside = Placement.Clamp(new(-1800, 30, 300, 200), work); Equal(-1800.0, inside.Left);
    var offscreen = Placement.Clamp(new(2500, -500, 450, 300), work); Equal(-450.0, offscreen.Left); Equal(0.0, offscreen.Top);
    var big = Placement.Clamp(new(0, 0, 1200, 800), new(0, 0, 800, 600)); Equal(800.0, big.Width); Equal(600.0, big.Height);
});

Check("Air quality: all six categories and missing/loading states", () =>
{
    var states = Enum.GetValues<AirStage>().Select(AirSample.For).ToArray();
    Equal(8, states.Length); Equal(6, states.Count(x => x.Aqi.HasValue));
    True(states.Select(x => x.Label).Distinct().Count() == 8);
    True(states.Take(6).Select(x => x.Aqi!.Value).SequenceEqual(states.Take(6).Select(x => x.Aqi!.Value).Order()));
});
Check("Media: playing, priority, stable pauses and pinned waiting", () =>
{
    MediaCandidate[] sessions = [new("s", "Spotify.exe", false, false), new("c", "chrome.exe", true, true), new("e", "msedge.exe", true, false)];
    var options = new MediaOptions();
    Equal("c", MediaSelection.Choose(sessions, options)!.Id);
    options.Priority = ["Edge", "Chrome", "Spotify"]; Equal("e", MediaSelection.Choose(sessions, options)!.Id);
    options.Mode = MediaMode.Pinned; options.PinnedApp = "Spotify.exe"; Equal("s", MediaSelection.Choose(sessions, options)!.Id);
    Equal<MediaCandidate?>(null, MediaSelection.Choose(sessions.Skip(1), options));
    options.Mode = MediaMode.Automatic; options.Priority = ["Spotify", "Chrome", "Edge"];
    Equal("c", MediaSelection.Choose(sessions.Select(x => x with { Playing = false }), options, "c")!.Id);
    options.Priority = [];
    Equal("e", MediaSelection.Choose(sessions.Select(x => x with { Playing = false }), options, "e")!.Id);
    sessions = [new("c1", "chrome.exe", true, true), new("c2", "chrome.exe", true, false)];
    Equal("c2", MediaSelection.Choose(sessions, options, "c2")!.Id);
});
Check("Battery: charging, absent, unknown and unsupported remaining time", () =>
{
    var battery = BatterySample.Decode(1, 8, 72, uint.MaxValue);
    Equal<int?>(72, battery.Percentage); True(battery.Charging && battery.Plugged); Equal<int?>(null, battery.SecondsRemaining);
    True(!BatterySample.Decode(1, 128, 255, uint.MaxValue).Present);
    battery = BatterySample.Decode(255, 255, 255, uint.MaxValue);
    True(battery.Present && !battery.Charging); Equal<int?>(null, battery.Percentage);
    Equal<int?>(1800, BatterySample.Decode(0, 0, 35, 1800).SecondsRemaining);
});

var testRoot = Path.Combine(Path.GetTempPath(), "Nook-tests-" + Guid.NewGuid().ToString("N"));
Check("Shape: metric-only circle and square normalization", () =>
{
    foreach (var kind in Enum.GetValues<WidgetKind>())
    {
        var widget = new WidgetInstance { Kind = kind, Shape = WidgetShape.Circle, Width = 300, Height = 200 };
        WidgetShapes.Normalize(widget);
        bool metric = WidgetShapes.SupportsCircle(kind);
        Equal(metric ? WidgetShape.Circle : WidgetShape.Card, widget.Shape);
        Equal(metric ? 300.0 : kind == WidgetKind.Calculator ? 300.0 : kind == WidgetKind.KeyStats ? 160.0 : 200.0, widget.Height);
    }
    var large = new WidgetInstance { Shape = WidgetShape.Circle, Width = 640 }; WidgetShapes.Normalize(large);
    Equal(480.0, large.Width); Equal(480.0, large.Height);
});
Check("Shape: circular resize preserves ratio and opposite anchors at every scale", () =>
{
    foreach (double scale in new[] { 1.0, 1.5, 2.0 })
    {
        var origin = new ScreenRect(-1000, -500, 300 * scale, 300 * scale);
        foreach (var edge in new[] { "T", "B", "L", "R", "LT", "RT", "LB", "RB" })
        {
            var resized = WidgetShapes.ResizeCircle(origin, edge, (edge.Contains('L') ? -20 : 20) * scale, (edge.Contains('T') ? -20 : 20) * scale, scale);
            Equal(320 * scale, resized.Width); Equal(resized.Width, resized.Height);
            if (edge.Contains('L')) Equal(origin.Right, resized.Right);
            if (edge.Contains('T')) Equal(origin.Bottom, resized.Bottom);
        }
        Equal(180 * scale, WidgetShapes.ResizeCircle(origin, "R", -1000 * scale, 0, scale).Width);
        Equal(480 * scale, WidgetShapes.ResizeCircle(origin, "B", 0, 1000 * scale, scale).Height);
    }
});
Check("Theme: per-widget selection survives global/group changes and reset", () =>
{
    var settings = new AppSettings { Theme = ThemeMode.Light };
    var group = new WidgetGroup(); settings.Groups.Add(group);
    var widget = new WidgetInstance { Theme = ThemeMode.Dark, GroupId = group.Id }; settings.Widgets.Add(widget);
    Equal(ThemeMode.Dark, AppearanceResolver.ResolveTheme(settings, widget));
    settings.Theme = ThemeMode.System; AppearanceResolver.DeleteGroup(settings, group.Id);
    Equal(ThemeMode.Dark, AppearanceResolver.ResolveTheme(settings, widget));
    widget.Theme = ThemeMode.System; settings.Theme = ThemeMode.Light;
    Equal(ThemeMode.System, AppearanceResolver.ResolveTheme(settings, widget));
    AppearanceResolver.ResetAll(settings); Equal<ThemeMode?>(null, widget.Theme);
    Equal(ThemeMode.Light, AppearanceResolver.ResolveTheme(settings, widget));
});
Check("Magnetic placement: empty space is free, OFF and unchanged size", () =>
{
    var work = new ScreenRect(0, 0, 1920, 1040); var moving = new ScreenRect(47, 71, 317, 209);
    Equal(moving, MagneticPlacement.Snap(moving, work, [], 1));
    Equal(moving, MagneticPlacement.Snap(moving, work, [], 1, false));
    var small = MagneticPlacement.Snap(moving, new(0, 0, 200, 100), [], 1);
    Equal(moving, small);
});
Check("Magnetic placement: peer edges and 16 DIP gaps override grid", () =>
{
    var work = new ScreenRect(0, 0, 1920, 1040); var peer = new ScreenRect(200, 200, 300, 200);
    Equal(new ScreenRect(516, 200, 300, 200), MagneticPlacement.Snap(new(521, 206, 300, 200), work, [peer], 1));
    Equal(new ScreenRect(200, 416, 300, 200), MagneticPlacement.Snap(new(206, 421, 300, 200), work, [peer], 1));
    Equal(new ScreenRect(200, 200, 300, 200), MagneticPlacement.Snap(new(205, 206, 300, 200), work, [peer], 1));
});
Check("Magnetic placement: DPI scaling and negative monitor coordinates", () =>
{
    var work = new ScreenRect(-1920, -1080, 1920, 1080); var peer = new ScreenRect(-1800, -900, 450, 300);
    Equal(new ScreenRect(-1326, -900, 450, 300), MagneticPlacement.Snap(new(-1319, -892, 450, 300), work, [peer], 1.5));
    Equal(new ScreenRect(-1849, -971, 300, 200), MagneticPlacement.Snap(new(-1849, -971, 300, 200), work, [], 1.5));
});
Check("Magnetic placement: distant peers, other monitors and work boundaries", () =>
{
    var work = new ScreenRect(0, 0, 1920, 1040);
    var peers = new[] { new ScreenRect(203, 700, 300, 200), new ScreenRect(-315, 205, 300, 200) };
    Equal(new ScreenRect(203, 205, 300, 200), MagneticPlacement.Snap(new(203, 205, 300, 200), work, peers, 1));
    Equal(new ScreenRect(1900, 1030, 300, 200), MagneticPlacement.Snap(new(1900, 1030, 300, 200), work, [], 1));
    var nearest = MagneticPlacement.Snap(new(520, 200, 300, 200), work,
        [new(200, 200, 300, 200), new(198, 200, 300, 200)], 1);
    Equal(516.0, nearest.Left);
});
Directory.CreateDirectory(testRoot);
Check("Magnetic Mode: equal horizontal/vertical gaps and release", () =>
{
    var work = new ScreenRect(0, 0, 1920, 1040);
    Equal(500.0, MagneticPlacement.Snap(new(505, 100, 200, 150), work,
        [new(100, 100, 200, 150), new(900, 100, 200, 150)], 1).Left);
    Equal(400.0, MagneticPlacement.Snap(new(100, 406, 200, 150), work,
        [new(100, 50, 200, 150), new(100, 750, 200, 150)], 1).Top);
    var moving = new ScreenRect(555, 121, 300, 200);
    Equal(moving, MagneticPlacement.Snap(moving, work, [new(200, 200, 300, 200)], 1));
    Equal(0.0, MagneticPlacement.Snap(new(5, 42, 300, 200), work, [], 1).Left);
});
Check("Magnetic drag: small pointer steps escape on all axes at every scale", () =>
{
    foreach (double scale in new[] { 1.0, 1.5, 2.0 })
    foreach (var direction in new[] { (X: 1, Y: 0), (X: -1, Y: 0), (X: 0, Y: 1), (X: 0, Y: -1) })
    {
        var origin = new ScreenRect(-1400, -700, 300 * scale, 200 * scale);
        var work = new ScreenRect(-1920, -1080, 3840, 2160);
        var drag = new MagneticDragSession(); drag.Begin(origin, -1300, -670);
        for (int step = 1; step <= 30; step++)
        {
            var raw = drag.Proposed(-1300 + direction.X * step * scale, -670 + direction.Y * step * scale);
            var snapped = MagneticPlacement.Snap(raw, work, [origin], scale);
            double expectedX = origin.Left + (step <= 6 ? 0 : direction.X * step * scale);
            double expectedY = origin.Top + (step <= 6 ? 0 : direction.Y * step * scale);
            Equal(expectedX, snapped.Left); Equal(expectedY, snapped.Top); Equal(origin.Width, snapped.Width);
        }
        drag.End(); True(!drag.IsActive);
    }
});
Check("Magnetic drag: re-grab, approach, reversal and bypass use pointer origin", () =>
{
    var drag = new MagneticDragSession(); var work = new ScreenRect(0, 0, 1920, 1040);
    drag.Begin(new(550, 200, 300, 200), 600, 230);
    Equal(516.0, MagneticPlacement.Snap(drag.Proposed(570, 230), work, [new(200, 200, 300, 200)], 1).Left);
    Equal(523.0, MagneticPlacement.Snap(drag.Proposed(573, 230), work, [new(200, 200, 300, 200)], 1).Left);
    Equal(509.0, MagneticPlacement.Snap(drag.Proposed(559, 230), work, [new(200, 200, 300, 200)], 1).Left);
    Equal(520.0, MagneticPlacement.Snap(drag.Proposed(570, 230), work, [], 1, false).Left);
    drag.End(); drag.Begin(new(516, 200, 300, 200), 550, 230);
    Equal(523.0, drag.Proposed(557, 230).Left);
});
Check("GPU: aggregate processes per engine, busiest engine, invalid and missing data", () =>
{
    var result = GpuAggregation.Resolve([new("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 20),
        new("pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 30),
        new("pid_2_luid_0x0_0x1_phys_0_eng_1_engtype_Copy", 40),
        new("pid_3_luid_0x0_0x2_phys_0_eng_0_engtype_3D", 10)]);
    Equal<double?>(50, result.Percentage); Equal("3D", result.Engine);
    Equal<double?>(null, GpuAggregation.Resolve([]).Percentage);
    Equal<double?>(null, GpuAggregation.Resolve([new("bad", 50), new("pid_1_luid_x", double.NaN), new("pid_1_luid_x", -1)]).Percentage);
    Equal<double?>(100, GpuAggregation.Resolve([new("pid_1_luid_x", 80), new("pid_2_luid_x", 80)]).Percentage);
    Equal<double?>(0, GpuAggregation.Resolve([new("pid_1_luid_x", 0)]).Percentage);
});
try
{
    Check("Shape: persistence, legacy default, unsupported fallback and invalid value", () =>
    {
        var store = new SettingsStore(testRoot); var settings = new AppSettings();
        settings.Widgets.Add(new() { Shape = WidgetShape.Circle, Theme = ThemeMode.Dark }); True(store.Save(settings));
        var restored = store.Load().Widgets[0]; Equal(WidgetShape.Circle, restored.Shape); Equal(restored.Width, restored.Height); Equal<ThemeMode?>(ThemeMode.Dark, restored.Theme);
        File.WriteAllText(store.FilePath, "{\"Version\":1,\"Widgets\":[{\"Kind\":\"Memory\"}]}"); Equal(WidgetShape.Card, store.Load().Widgets[0].Shape);
        settings.Widgets[0].Kind = WidgetKind.Weather; True(store.Save(settings)); Equal(WidgetShape.Card, store.Load().Widgets[0].Shape);
        File.WriteAllText(store.FilePath, "{\"Version\":1,\"Widgets\":[{\"Shape\":99}]}"); Equal(0, store.Load().Widgets.Count);
    });
    Check("Theme: persistence, missing legacy field and invalid theme", () =>
    {
        var store = new SettingsStore(testRoot); var settings = new AppSettings();
        var widget = new WidgetInstance { Theme = ThemeMode.Dark }; settings.Widgets.Add(widget);
        True(store.Save(settings)); Equal<ThemeMode?>(ThemeMode.Dark, store.Load().Widgets[0].Theme);
        widget.Theme = null; True(store.Save(settings)); Equal<ThemeMode?>(null, store.Load().Widgets[0].Theme);
        File.WriteAllText(store.FilePath, $"{{\"Version\":1,\"Widgets\":[{{\"Id\":\"{widget.Id}\",\"Theme\":99}}]}}");
        Equal(0, store.Load().Widgets.Count); True(store.Warning is not null);
    });
    Check("Settings: round-trip of instances, groups and appearance", () =>
    {
        var store = new SettingsStore(testRoot); var s = new AppSettings { Theme = ThemeMode.Dark, AlwaysOnTop = true, MagneticMode = true };
        var group = new WidgetGroup { Name = "시스템" }; s.Groups.Add(group);
        var w = new WidgetInstance { Kind = WidgetKind.Memory, Left = -1000, Top = 80, Width = 420, Height = 310,
            GroupId = group.Id, Visible = false, Locked = true, Appearance = new() { Radius = Level.High } }; s.Widgets.Add(w);
        True(store.Save(s)); var loaded = store.Load(); Equal(ThemeMode.Dark, loaded.Theme); True(loaded.AlwaysOnTop);
        True(loaded.MagneticMode);
        Equal(w.Id, loaded.Widgets[0].Id); Equal(group.Id, loaded.Widgets[0].GroupId); Equal(420.0, loaded.Widgets[0].Width);
        Equal(-1000.0, loaded.Widgets[0].Left); Equal(false, loaded.Widgets[0].Visible); True(loaded.Widgets[0].Locked);
        Equal(Level.High, loaded.Widgets[0].Appearance.Radius); True(!File.Exists(store.FilePath + ".tmp"));
    });
    Check("Settings: corruption backup, unknown version and null fields", () =>
    {
        var store = new SettingsStore(testRoot); File.WriteAllText(store.FilePath, "{ broken");
        Equal(0, store.Load().Widgets.Count); True(store.Warning is not null);
        True(Directory.GetFiles(testRoot, "settings.invalid-*.json").Length > 0);
        File.WriteAllText(store.FilePath, "{\"Version\":99}"); Equal(1, store.Load().Version);
        File.WriteAllText(store.FilePath, "{\"Widgets\":null}"); Equal(0, store.Load().Widgets.Count);
        File.WriteAllText(store.FilePath, "{\"Widgets\":[null]}"); Equal(0, store.Load().Widgets.Count);
    });
    Check("Settings: dangling group and sizes normalized", () =>
    {
        var store = new SettingsStore(testRoot); var s = new AppSettings();
        s.Widgets.Add(new() { GroupId = Guid.NewGuid(), Width = 9999, Height = 1 }); True(store.Save(s));
        var loaded = store.Load(); Equal<Guid?>(null, loaded.Widgets[0].GroupId); Equal(640.0, loaded.Widgets[0].Width); Equal(140.0, loaded.Widgets[0].Height);
    });
    Check("Settings: older files default magnetic grid to OFF", () =>
    {
        var store = new SettingsStore(testRoot); File.WriteAllText(store.FilePath, "{\"Version\":1}");
        Equal(false, store.Load().MagneticMode);
    });
    Check("Settings: legacy ON preference, GPU and demo weather round-trip", () =>
    {
        var store = new SettingsStore(testRoot);
        File.WriteAllText(store.FilePath, "{\"Version\":1,\"AutoGrid\":true}");
        True(store.Load().MagneticMode);
        var s = new AppSettings { MagneticMode = true };
        s.Widgets.AddRange([new() { Kind = WidgetKind.Gpu }, new() { Kind = WidgetKind.Weather }]);
        True(store.Save(s)); var loaded = store.Load();
        True(loaded.MagneticMode); Equal(WidgetKind.Gpu, loaded.Widgets[0].Kind); Equal(WidgetKind.Weather, loaded.Widgets[1].Kind);
    });
    Check("Settings: new widgets, Unicode memo, source preferences and ordered shelves round-trip", () =>
    {
        var store = new SettingsStore(testRoot); var s = new AppSettings();
        foreach (var kind in Enum.GetValues<WidgetKind>().Where(k => k >= WidgetKind.AirQuality))
            s.Widgets.Add(new() { Kind = kind, MemoText = "기억할 내용 😀\n두 번째 줄", AirQuality = new() { Style = AirStyle.Modern, Stage = AirStage.Hazardous },
                Media = new() { Mode = MediaMode.Pinned, PinnedApp = "Spotify.exe", Priority = ["Edge", "Spotify"] },
                Shelf = [new() { Name = "문서", Path = @"C:\문서" }, new() { Name = "앱", Path = @"C:\App.exe" }],
                SystemButtons = [SystemAction.Wifi, SystemAction.Restart, SystemAction.Mute], ControlOrientation = ControlOrientation.Vertical, BrightnessTarget = "display:stable-id",
                Shape = WidgetShapes.SupportsCircle(kind) ? WidgetShape.Circle : WidgetShape.Card });
        True(store.Save(s)); var loaded = store.Load(); Equal(Enum.GetValues<WidgetKind>().Count(k => k >= WidgetKind.AirQuality), loaded.Widgets.Count);
        foreach (var w in loaded.Widgets)
        {
            Equal("기억할 내용 😀\n두 번째 줄", w.MemoText); Equal(AirStage.Hazardous, w.AirQuality.Stage);
            Equal(MediaMode.Pinned, w.Media.Mode); Equal("Edge", w.Media.Priority[0]); Equal("문서", w.Shelf[0].Name);
            Equal(SystemAction.Restart, w.SystemButtons[1]);
            Equal(ControlOrientation.Vertical, w.ControlOrientation); Equal("display:stable-id", w.BrightnessTarget);
            Equal(WidgetShapes.SupportsCircle(w.Kind) ? WidgetShape.Circle : WidgetShape.Card, w.Shape);
        }
    });
    Check("Settings: invalid feature options are quarantined", () =>
    {
        var store = new SettingsStore(testRoot); var s = new AppSettings();
        s.Widgets.Add(new() { Kind = WidgetKind.AirQuality, AirQuality = new() { Stage = (AirStage)99 } });
        True(store.Save(s)); Equal(0, store.Load().Widgets.Count); True(store.Warning != null);
    });
    Check("Settings: new clock/countdowns restore and invalid values are quarantined", () =>
    {
        var store = new SettingsStore(testRoot); var end = DateTimeOffset.UtcNow.AddMinutes(3); var s = new AppSettings { Widgets = [new() { Kind = WidgetKind.Clock, Shape = WidgetShape.Circle, ClockStyle = ClockStyle.Analog }, new() { Kind = WidgetKind.Pomodoro, Countdown = new() { Phase = FocusPhase.LongBreak, CompletedFocus = 4, Deadline = end } }] };
        True(store.Save(s)); var loaded = store.Load(); Equal(ClockStyle.Analog, loaded.Widgets[0].ClockStyle); Equal(WidgetShape.Circle, loaded.Widgets[0].Shape); Equal(end, loaded.Widgets[1].Countdown.Deadline!.Value);
        s.Widgets[0].ClockStyle = (ClockStyle)99; True(store.Save(s)); Equal(0, store.Load().Widgets.Count);
    });

}
finally { Directory.Delete(testRoot, true); }
Check("Activity: calendar periods, leap year, fixed intensity and empty dates", () =>
{
    var today = new DateOnly(2024, 3, 1); var days = new List<UsageDay> { new() { Date = today, Apps = [new() { Seconds = 14400 }] } };
    Equal(7, ActivityHistory.Cells(days, today, ActivityPeriod.Week).Count); Equal(366, ActivityHistory.Cells(days, today, ActivityPeriod.Year).Count);
    Equal(new DateOnly(2024, 2, 2), ActivityHistory.Start(today, ActivityPeriod.Month));
    Equal(0, ActivityHistory.Level(0)); Equal(1, ActivityHistory.Level(1)); Equal(2, ActivityHistory.Level(1800)); Equal(3, ActivityHistory.Level(7200)); Equal(4, ActivityHistory.Level(14400));
    var cells = ActivityHistory.Cells(days, today, ActivityPeriod.HalfYear); Equal(1, cells.Count(x => x.Level > 0)); Equal(14400.0, cells.Sum(x => x.Seconds));
});
Check("Activity: retain one year without backfilling unobserved usage", () =>
{
    var now = DateTimeOffset.Now; var date = DateOnly.FromDateTime(now.DateTime);
    var ledger = new UsageLedger([new() { Date = date.AddDays(-364), Apps = [new() { Seconds = 100 }] }, new() { Date = date.AddDays(-366), Apps = [new() { Seconds = 100 }] }]);
    ledger.Sample(1000, now, null); Equal(1, ledger.Days.Count); Equal(100.0, ledger.Days[0].TotalSeconds);
});
Check("Magnetic resize: edge attraction, release, OFF and opposite anchors", () =>
{
    foreach (double scale in new[] { 1.0, 1.5, 2.0 })
    {
        var work = new ScreenRect(-1200 * scale, 0, 2400 * scale, 1100 * scale); var origin = new ScreenRect(100 * scale, 100 * scale, 300 * scale, 200 * scale); var peer = new ScreenRect(450 * scale, 100 * scale, 300 * scale, 200 * scale);
        var raw = MagneticResize.Propose(origin, "R", 30 * scale, 0, scale, false); var snap = MagneticResize.Snap(raw, "R", work, [peer], scale, false, true);
        Equal(434 * scale, snap.Right); Equal(origin.Left, snap.Left); Equal(origin.Height, snap.Height);
        Equal(raw, MagneticResize.Snap(raw, "R", work, [peer], scale, false, false));
        raw = MagneticResize.Propose(origin, "R", 50 * scale, 0, scale, false); Equal(raw, MagneticResize.Snap(raw, "R", work, [peer with { Left = 800 * scale }], scale, false, true));
        var left = MagneticResize.Propose(origin, "L", -50 * scale, 0, scale, false); Equal(origin.Right, left.Right);
        var circle = MagneticResize.Propose(origin with { Height = origin.Width }, "RB", 30 * scale, 30 * scale, scale, true); snap = MagneticResize.Snap(circle, "RB", work, [peer], scale, true, true); Equal(snap.Width, snap.Height); Equal(origin.Left, snap.Left); Equal(origin.Top, snap.Top);
    }
});
Check("Countdown: elapsed gauge fills and paused/reset values stay consistent", () =>
{
    var time = DateTimeOffset.UtcNow; var o = new CountdownOptions { TimerSeconds = 100 }; Equal(0.0, CountdownEngine.Progress(o, false, time));
    CountdownEngine.Toggle(o, false, time); Equal(.5, CountdownEngine.Progress(o, false, time.AddSeconds(50))); CountdownEngine.Toggle(o, false, time.AddSeconds(50)); Equal(.5, CountdownEngine.Progress(o, false, time.AddHours(1)));
    CountdownEngine.Toggle(o, false, time); CountdownEngine.Tick(o, false, time.AddSeconds(50)); Equal(1.0, CountdownEngine.Progress(o, false, time.AddSeconds(50))); CountdownEngine.Reset(o); Equal(0.0, CountdownEngine.Progress(o, false, time));
});
Check("Settings: separate themes, names, activity, retired visualizer and large memo restore", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "nook-v12-" + Guid.NewGuid());
    try { var store = new SettingsStore(directory); var s = new AppSettings { Theme = ThemeMode.Dark, MainTheme = ThemeMode.Light, Widgets = [new() { Kind = WidgetKind.Memo, Name = "나의 계획", Width = 1100, Height = 850 }, new() { Kind = WidgetKind.Activity, ActivityPeriod = ActivityPeriod.Year }, new() { Kind = WidgetKind.Media, Media = new() }] }; True(store.Save(s)); var loaded = store.Load(); Equal(ThemeMode.Light, loaded.MainTheme!.Value); Equal(ThemeMode.Dark, loaded.Theme); Equal("나의 계획", loaded.Widgets[0].Name); Equal(1100.0, loaded.Widgets[0].Width); Equal(850.0, loaded.Widgets[0].Height); Equal(ActivityPeriod.Year, loaded.Widgets[1].ActivityPeriod); True(typeof(MediaOptions).GetProperty("Visualizer") is null); s.MainTheme = (ThemeMode)99; True(store.Save(s)); Equal(0, store.Load().Widgets.Count); }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
});

Check("Removed widget: catalog, model and numeric ids", () =>
{
    True(!Enum.GetNames<WidgetKind>().Contains("Cat")); True(!Enum.IsDefined(typeof(WidgetKind),22)); Equal(21,(int)WidgetKind.Activity);
    True(typeof(WidgetInstance).GetProperty("Cat") is null); Equal(25,WidgetCatalog.All.Count);
    Equal(0,WidgetCatalog.Search("전체","고양이").Count()); Equal(0,WidgetCatalog.Search("전체","Cat").Count());
});
Check("Removed widget: legacy string/numeric entries are filtered without losing other data", () =>
{
    var directory=Path.Combine(Path.GetTempPath(),"nook-removed-"+Guid.NewGuid());
    try
    {
        var group=new WidgetGroup{Name="시스템"};var memo=new WidgetInstance{Kind=WidgetKind.Memo,Name="내 메모",MemoText="계획 ✅\n다음 줄",GroupId=group.Id,Theme=ThemeMode.Light,Width=1100,Height=850};
        var settings=new AppSettings{Theme=ThemeMode.Dark,MainTheme=ThemeMode.Light,Groups=[group],Widgets=[memo,new(){Kind=WidgetKind.Cpu,Left=-340,Top=120},new(){Kind=WidgetKind.Activity,ActivityPeriod=ActivityPeriod.Year}]};
        var store=new SettingsStore(directory);True(store.Save(settings));
        var root=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(store.FilePath))!;var list=(System.Text.Json.Nodes.JsonArray)root["Widgets"]!;
        list[2]!["Kind"]=21;
        foreach(var entry in list)entry!["Cat"]=new System.Text.Json.Nodes.JsonObject{["Meals"]=7,["LastFed"]="legacy"};
        foreach(var kind in new System.Text.Json.Nodes.JsonNode[]{System.Text.Json.Nodes.JsonValue.Create("Cat")!,System.Text.Json.Nodes.JsonValue.Create("cat")!,System.Text.Json.Nodes.JsonValue.Create(" Cat ")!,System.Text.Json.Nodes.JsonValue.Create("22")!,System.Text.Json.Nodes.JsonValue.Create(22)!})
            list.Insert(1,new System.Text.Json.Nodes.JsonObject{["Id"]=Guid.NewGuid().ToString(),["Kind"]=kind,["Shape"]="Circle",["Cat"]=new System.Text.Json.Nodes.JsonObject{["Meals"]=10}});
        File.WriteAllText(store.FilePath,root.ToJsonString());var loaded=store.Load();
        True(store.Warning is null);Equal(3,loaded.Widgets.Count);Equal(group.Id,loaded.Groups.Single().Id);Equal(memo.Id,loaded.Widgets[0].Id);Equal(memo.MemoText,loaded.Widgets[0].MemoText);Equal(memo.Name,loaded.Widgets[0].Name);Equal(group.Id,loaded.Widgets[0].GroupId!.Value);Equal(1100.0,loaded.Widgets[0].Width);Equal(850.0,loaded.Widgets[0].Height);Equal(-340.0,loaded.Widgets[1].Left);Equal(WidgetKind.Activity,loaded.Widgets[2].Kind);Equal(ActivityPeriod.Year,loaded.Widgets[2].ActivityPeriod);Equal(ThemeMode.Dark,loaded.Theme);
        True(store.Save(loaded));True(!File.ReadAllText(store.FilePath).Contains("\"Cat\""));Equal(0,Directory.GetFiles(directory,"settings.invalid-*").Length);
    }
    finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
});
Check("Removed widget: old widget-only config retains preferences; unknown kinds still fail", () =>
{
    var directory=Path.Combine(Path.GetTempPath(),"nook-retired-only-"+Guid.NewGuid());
    try
    {
        Directory.CreateDirectory(directory);var store=new SettingsStore(directory);
        File.WriteAllText(store.FilePath,"{\"Version\":1,\"Theme\":\"Dark\",\"Widgets\":[{\"Kind\":\"Cat\",\"Id\":\"bad-old-id\"}]}");
        var loaded=store.Load();Equal(0,loaded.Widgets.Count);Equal(ThemeMode.Dark,loaded.Theme);True(store.Warning is null);
        File.WriteAllText(store.FilePath,"{\"Version\":1,\"Widgets\":[{\"Kind\":\"UnrecognizedWidget\"}]}");
        Equal(0,store.Load().Widgets.Count);True(store.Warning is not null);Equal(1,Directory.GetFiles(directory,"settings.invalid-*").Length);
    }
    finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
});
Check("Calculator: precedence, powers, unary, percentages and parentheses", () =>
{
    Equal(14.0, CalculatorEngine.Evaluate("2+3*4")); Equal(512.0, CalculatorEngine.Evaluate("2^3^2"));
    Equal(-4.0, CalculatorEngine.Evaluate("-2^2")); Equal(.25, CalculatorEngine.Evaluate("2^-2"));
    Equal(120.0, CalculatorEngine.Evaluate("5!")); Equal(20.0, CalculatorEngine.Evaluate("200*10%"));
    Equal(4.0, CalculatorEngine.Evaluate("(2+6)/2")); Equal(1002.0, CalculatorEngine.Evaluate("1e3 + 2")); Equal(9.0, CalculatorEngine.Evaluate("Ans+3", answer: 6));
});
Check("Calculator: scientific units and invalid expressions", () =>
{
    True(Math.Abs(CalculatorEngine.Evaluate("sin(30)") - .5) < 1e-12);
    True(Math.Abs(CalculatorEngine.Evaluate("cos(pi)", false) + 1) < 1e-12);
    Equal(5.0, CalculatorEngine.Evaluate("sqrt(9)+log(100)"));
    foreach (string expression in new[] { "1/0", "sqrt(-1)", "ln(0)", "tan(90)", "171!", "1.5!", "2+", "(1+2", "2..3", "run(1)", new string('(',40)+"1"+new string(')',40), new string('1',513) })
    { bool invalid = false; try { CalculatorEngine.Evaluate(expression); } catch (FormatException) { invalid = true; } True(invalid); }
});
Check("Dice: constrained proportions, anchors and magnetic resize at all scales", () =>
{
    foreach (double scale in new[] { 1.0, 1.5, 2.0 }) foreach (string edge in new[] { "L", "R", "T", "B", "LT", "RT", "LB", "RB" })
    {
        var origin = new ScreenRect(-100,50,320*scale,200*scale);
        var rect = WidgetLimits.ResizeDice(origin, edge, -60*scale, 70*scale, scale);
        True(rect.Width/rect.Height is >= .5 and <= 2); True(rect.Width >=180*scale && rect.Width<=420*scale); True(Math.Min(rect.Width,rect.Height)<=280*scale);
        if (edge.Contains('L')) Equal(origin.Right,rect.Right); if (edge.Contains('T')) Equal(origin.Bottom,rect.Bottom);
        var snap = MagneticResize.Snap(rect, edge, new(-1000,-1000,3000,3000), [new(rect.Right+3,rect.Top,320,200)],scale,false,true);
        snap = WidgetLimits.ConstrainDice(snap,edge,scale); True(snap.Width/snap.Height is >= .5 and <= 2);
    }
    var widget = new WidgetInstance { Kind=WidgetKind.Dice, Width=999, Height=999 }; WidgetShapes.Normalize(widget); Equal(420.0,widget.Width); Equal(280.0,widget.Height);
});
Check("Screen time: exclusion is reversible and percentages renormalize", () =>
{
    var day = new UsageDay { Apps=[new(){Id="Nook",Seconds=60},new(){Id="code",Seconds=40}] };
    var visible = UsageLedger.Excluding(day,"nook"); Equal(40.0,visible.TotalSeconds); Equal(100.0,UsageLedger.Shares(visible).Single().Percentage); Equal(100.0,day.TotalSeconds);
});
Check("Presets: independent memos, shared memos, stable ids and deep copies", () =>
{
    var memo = new WidgetInstance {Kind=WidgetKind.Memo,MemoText="first"}; var cpu=new WidgetInstance{Kind=WidgetKind.Cpu,Left=-500,Theme=ThemeMode.Dark};
    var group=new WidgetGroup{Name="업무"}; cpu.GroupId=group.Id;
    var s = new AppSettings {Widgets=[memo,cpu],Groups=[group],MainTheme=ThemeMode.Light};
    var a=LayoutPresets.Capture(s,"A");s.Presets.Add(a);s.ActivePresetId=a.Id;
    memo.MemoText="A changed";cpu.Left=50;group.Name="changed";
    var b=LayoutPresets.Capture(s,"B");s.Presets.Add(b);s.ActivePresetId=b.Id;
    memo.MemoText="B changed"; LayoutPresets.Apply(s,a); Equal("A changed",s.Widgets[0].MemoText);Equal(-500.0,s.Widgets[1].Left);Equal("업무",s.Groups[0].Name);Equal(memo.Id,s.Widgets[0].Id);
    LayoutPresets.Apply(s,b);Equal("B changed",s.Widgets[0].MemoText);Equal(ThemeMode.Light,s.MainTheme!.Value);
    a.ShareMemoContents=b.ShareMemoContents=true;s.Widgets[0].MemoText="shared";LayoutPresets.Apply(s,a);Equal("shared",s.Widgets[0].MemoText);
    s.Widgets[0].MemoText="new shared";LayoutPresets.Apply(s,b);Equal("new shared",s.Widgets[0].MemoText);
    True(!ReferenceEquals(a.Widgets[1].Appearance,s.Widgets[1].Appearance));
});
Check("Presets: persistence includes independent memo contents and validates snapshots", () =>
{
    string directory=Path.Combine(Path.GetTempPath(),"nook-preset-"+Guid.NewGuid());
    try
    {
        var store=new SettingsStore(directory); var s=new AppSettings{ExcludeNookFromScreenTime=true,Widgets=[new(){Kind=WidgetKind.Memo,MemoText="한글 메모"},new(){Kind=WidgetKind.Calculator}]};
        var p=LayoutPresets.Capture(s,"작업");s.Presets.Add(p);s.ActivePresetId=p.Id;True(store.Save(s));var loaded=store.Load();
        True(loaded.ExcludeNookFromScreenTime);Equal(p.Id,loaded.ActivePresetId!.Value);Equal("한글 메모",loaded.Presets[0].Widgets[0].MemoText);True(!loaded.Presets[0].ShareMemoContents);
        loaded.Presets[0].Widgets[0].Kind=(WidgetKind)99;True(store.Save(loaded));Equal(0,store.Load().Widgets.Count);True(store.Warning is not null);
    }
    finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
});
Check("Knob: 240-degree tracking matches the countdown ring", () =>
{ Equal(60.0,LevelMath.DragAngle(50,0,24,240));Equal(51.25,LevelMath.DragAngle(50,179,-178,240)); });
Check("Breathe: smooth cycle, finite fallback and palettes", () =>
{
    Equal(0.0,BreatheMotion.Expansion(0)); Equal(1.0,BreatheMotion.Expansion(2)); Equal(0.0,BreatheMotion.Expansion(4.5));
    Equal(.5,BreatheMotion.Expansion(double.NaN)); Equal(.5,BreatheMotion.Expansion(double.PositiveInfinity));
    for(int i=0;i<450;i++) { double t=i/100.0; True(BreatheMotion.Expansion(t) is >=0 and <=1); True(Math.Abs(BreatheMotion.Expansion(t)-BreatheMotion.Expansion(t+4.5))<1e-12); }
    for(int i=1;i<=200;i++) True(BreatheMotion.Expansion(i*.01)>=BreatheMotion.Expansion((i-1)*.01));
    for(int i=201;i<450;i++) True(BreatheMotion.Expansion(i*.01)<=BreatheMotion.Expansion((i-1)*.01));
    True(Math.Abs(BreatheMotion.Expansion(2.001)-BreatheMotion.Expansion(1.999))<1e-6);
    Equal(4,Enum.GetValues<BreatheTone>().Select(BreatheMotion.Color).Distinct().Count());
});
Check("V14: compact limits and portrait Dice survive settings and preset restoration", () =>
{
    string directory=Path.Combine(Path.GetTempPath(),"nook-v14-"+Guid.NewGuid());
    try
    {
        var config=new AppSettings { StartupPromptShown=true,Widgets=[new(){Kind=WidgetKind.KeyStats,Width=999,Height=999},new(){Kind=WidgetKind.Dice,Width=280,Height=420},new(){Kind=WidgetKind.Activity,ActivityShowScreenTime=false},new(){Kind=WidgetKind.MediaController,MediaControllerShowVolume=true},new(){Kind=WidgetKind.Breathe,Shape=WidgetShape.Circle,BreatheTone=BreatheTone.Mint}] };
        config.Presets.Add(LayoutPresets.Capture(config,"test"));var store=new SettingsStore(directory);True(store.Save(config));var loaded=store.Load();
        Equal(220.0,loaded.Widgets[0].Width);Equal(160.0,loaded.Widgets[0].Height);Equal(420.0,loaded.Widgets[1].Height);Equal(280.0,loaded.Widgets[1].Width);
        True(!loaded.Widgets[2].ActivityShowScreenTime);True(loaded.Widgets[3].MediaControllerShowVolume);Equal(BreatheTone.Mint,loaded.Widgets[4].BreatheTone);True(loaded.StartupPromptShown);
        LayoutPresets.Apply(loaded,loaded.Presets[0]);Equal(220.0,loaded.Widgets[0].Width);Equal(160.0,loaded.Widgets[0].Height);True(!loaded.Widgets[2].ActivityShowScreenTime);
        Equal(640.0,WidgetLimits.MaxWidth(WidgetKind.Cpu));Equal(480.0,WidgetLimits.MaxHeight(WidgetKind.Cpu));
        var bad=loaded.Widgets[4];bad.BreatheTone=(BreatheTone)99;True(store.Save(loaded));Equal(0,store.Load().Widgets.Count);True(store.Warning is not null);
        File.WriteAllText(store.FilePath,"{\"Version\":1,\"Widgets\":[{\"Kind\":\"Activity\"},{\"Kind\":\"MediaController\"}]}");var old=store.Load();True(old.Widgets[0].ActivityShowScreenTime);True(!old.Widgets[1].MediaControllerShowVolume);
    }
    finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
});
Check("Preset preparation: invalid snapshot leaves live layout and memo archive untouched", () =>
{
    var memo = new WidgetInstance { Kind = WidgetKind.Memo, MemoText = "saved" };
    var state = new AppSettings { Widgets = [memo] };
    var original = LayoutPresets.Capture(state, "Original"); state.Presets.Add(original); state.ActivePresetId = original.Id;
    memo.MemoText = "unsaved edit";
    var invalid = LayoutPresets.Capture(new AppSettings { Widgets = [new() { Kind = WidgetKind.Cpu }] }, "Invalid");
    invalid.Widgets.Add(invalid.Widgets[0]); state.Presets.Add(invalid);
    bool rejected = false;
    try { LayoutPresets.Prepare(state, invalid); } catch (System.Text.Json.JsonException) { rejected = true; }
    True(rejected); True(ReferenceEquals(memo, state.Widgets[0])); Equal("unsaved edit", memo.MemoText);
    Equal("saved", original.Widgets[0].MemoText); Equal(original.Id, state.ActivePresetId!.Value);
});
Check("Preset preparation: independent copies preserve running countdown and app preferences", () =>
{
    var timer = new WidgetInstance { Kind = WidgetKind.Timer };
    CountdownEngine.Toggle(timer.Countdown, false, DateTimeOffset.UtcNow);
    var state = new AppSettings { MainTheme = ThemeMode.Light, ExcludeNookFromScreenTime = true, Widgets = [timer] };
    var preset = LayoutPresets.Capture(state, "Timer"); state.Presets.Add(preset); preset.Theme = ThemeMode.Dark;
    var candidate = LayoutPresets.Prepare(state, preset);
    Equal(timer.Countdown.Deadline, candidate.Widgets[0].Countdown.Deadline);
    True(!ReferenceEquals(timer, candidate.Widgets[0])); True(!ReferenceEquals(timer.Countdown, candidate.Widgets[0].Countdown));
    Equal(ThemeMode.Light, candidate.MainTheme!.Value); True(candidate.ExcludeNookFromScreenTime);
    Equal(ThemeMode.System, state.Theme); candidate.Widgets[0].Width = 420; Equal(300.0, timer.Width);
});
Check("Preset Undo: shared edits and unrelated preferences survive; snapshot stays independent", () =>
{
    var memo = new WidgetInstance { Kind = WidgetKind.Memo, MemoText = "before" };
    var state = new AppSettings { Widgets = [memo], MainTheme = ThemeMode.Dark };
    var a = LayoutPresets.Capture(state, "A"); a.ShareMemoContents = true; state.Presets.Add(a); state.ActivePresetId = a.Id;
    var before = LayoutPresets.Snapshot(state);
    var b = LayoutPresets.Capture(state, "B"); b.ShareMemoContents = true; b.Widgets[0].Left = 100; state.Presets.Add(b);
    LayoutPresets.RestoreState(state, LayoutPresets.Prepare(state, b));
    state.Widgets[0].MemoText = "shared edit after switching"; state.MainTheme = ThemeMode.Light;
    var undone = LayoutPresets.PrepareUndo(state, before); LayoutPresets.RestoreState(state, undone);
    Equal(a.Id, state.ActivePresetId!.Value); Equal(0.0, state.Widgets[0].Left);
    Equal("shared edit after switching", state.Widgets[0].MemoText); Equal(ThemeMode.Light, state.MainTheme!.Value);
    Equal(2, state.Presets.Count); Equal("before", before.Widgets[0].MemoText);
});
Check("Preset Undo: independent memo returns to its own contents", () =>
{
    var state = new AppSettings { Widgets = [new() { Kind = WidgetKind.Memo, MemoText = "A" }] };
    var a = LayoutPresets.Capture(state, "A"); state.Presets.Add(a); state.ActivePresetId = a.Id;
    var before = LayoutPresets.Snapshot(state);
    var b = LayoutPresets.Capture(state, "B"); state.Presets.Add(b); b.Widgets[0].MemoText = "B";
    LayoutPresets.RestoreState(state, LayoutPresets.Prepare(state, b)); state.Widgets[0].MemoText = "B edit";
    LayoutPresets.RestoreState(state, LayoutPresets.PrepareUndo(state, before));
    Equal("A", state.Widgets[0].MemoText); Equal("B edit", b.Widgets[0].MemoText);
});
Check("Opacity None: each scope is explicit and radius inherits independently", () =>
{
    var group = new WidgetGroup { Name = "Desk", Appearance = new() { Radius = Level.Low, Opacity = OpacityLevel.None } };
    var widget = new WidgetInstance { GroupId = group.Id };
    var state = new AppSettings { Groups = [group], Widgets = [widget], Opacity = OpacityLevel.High };
    var resolved = AppearanceResolver.Resolve(state, widget);
    Equal(0.0, resolved.BackgroundOpacity); Equal("그룹: Desk", resolved.Opacity.Source); Equal(8.0, resolved.CornerRadius);
    widget.Appearance.Opacity = OpacityLevel.Mid;
    Equal(.82, AppearanceResolver.Resolve(state, widget).BackgroundOpacity);
    widget.Appearance.Opacity = OpacityLevel.None; state.Opacity = OpacityLevel.Low;
    Equal("개별 위젯", AppearanceResolver.Resolve(state, widget).Opacity.Source);
    Equal(0.0, AppearanceResolver.Resolve(state, widget).BackgroundOpacity);
    widget.Appearance.Opacity = null; group.Appearance.Opacity = null; state.Opacity = OpacityLevel.None;
    Equal("전체 기본값", AppearanceResolver.Resolve(state, widget).Opacity.Source);
    Equal(0.0, AppearanceResolver.Resolve(state, widget).BackgroundOpacity);
});
Check("Opacity: legacy alpha levels and radius values do not change", () =>
{
    foreach (var (opacity, alpha) in new[] { (OpacityLevel.Low, .65), (OpacityLevel.Mid, .82), (OpacityLevel.High, .96) })
        foreach (var (radius, dip) in new[] { (Level.Low, 8.0), (Level.Mid, 16.0), (Level.High, 24.0) })
        {
            var state = new AppSettings { Opacity = opacity, Radius = radius };
            var result = AppearanceResolver.Resolve(state, new()); Equal(alpha, result.BackgroundOpacity); Equal(dip, result.CornerRadius);
        }
    Equal(3, Enum.GetValues<Level>().Length); Equal(0, (int)OpacityLevel.Low); Equal(1, (int)OpacityLevel.Mid); Equal(2, (int)OpacityLevel.High);
});
Check("Opacity None: group deletion and reset preserve the global preference", () =>
{
    var group = new WidgetGroup { Appearance = new() { Opacity = OpacityLevel.None } };
    var widget = new WidgetInstance { GroupId = group.Id, Appearance = new() { Opacity = OpacityLevel.High } };
    var state = new AppSettings { Groups = [group], Widgets = [widget], Opacity = OpacityLevel.None };
    AppearanceResolver.DeleteGroup(state, group.Id);
    Equal<Guid?>(null, widget.GroupId); Equal(OpacityLevel.High, AppearanceResolver.Resolve(state, widget).Opacity.Value);
    AppearanceResolver.ResetAll(state);
    Equal<OpacityLevel?>(null, widget.Appearance.Opacity); Equal(0.0, AppearanceResolver.Resolve(state, widget).BackgroundOpacity);
});
void WithOpacityStore(Action<SettingsStore> test)
{
    var directory = Path.Combine(Path.GetTempPath(), "Nook-opacity-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { test(new SettingsStore(directory)); }
    finally
    {
        // A freshly allocated flat test directory; no user settings or recursive removal.
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }
}
Check("Opacity: old named and numeric settings survive without migration", () =>
{
    foreach (bool numeric in new[] { false, true }) WithOpacityStore(store =>
    {
        var groupId = Guid.NewGuid(); var widgetId = Guid.NewGuid();
        object oldHigh = numeric ? 2 : "High"; object oldLow = numeric ? 0 : "Low";
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            Version = 1, Radius = "Mid", Opacity = oldHigh,
            Groups = new[] { new { Id = groupId, Name = "Legacy", Appearance = new { Radius = "Low", Opacity = oldLow } } },
            Widgets = new[] { new { Id = widgetId, Kind = "Cpu", GroupId = groupId, Appearance = new { Opacity = (object?)null } } },
            Presets = new[] { new { Id = Guid.NewGuid(), Name = "Old", Radius = "Mid", Opacity = oldHigh, Widgets = Array.Empty<object>(), Groups = Array.Empty<object>() } }
        });
        File.WriteAllText(store.FilePath, json); var loaded = store.Load();
        True(store.Warning is null); Equal(1, loaded.Version); Equal(OpacityLevel.High, loaded.Opacity);
        Equal(OpacityLevel.Low, AppearanceResolver.Resolve(loaded, loaded.Widgets.Single()).Opacity.Value);
        Equal(8.0, AppearanceResolver.Resolve(loaded, loaded.Widgets.Single()).CornerRadius);
        Equal(OpacityLevel.High, loaded.Presets.Single().Opacity);
    });
});
Check("Opacity None: disk roundtrip and preset restore retain every scope", () => WithOpacityStore(store =>
{
    var group = new WidgetGroup { Name = "Desk", Appearance = new() { Opacity = OpacityLevel.None, Radius = Level.Low } };
    var widget = new WidgetInstance { Kind = WidgetKind.Activity, GroupId = group.Id, ActivityPeriod = ActivityPeriod.Year,
        Appearance = new() { Opacity = OpacityLevel.None } };
    var state = new AppSettings { Opacity = OpacityLevel.None, Widgets = [widget], Groups = [group] };
    var preset = LayoutPresets.Capture(state, "Clear desk"); state.Presets.Add(preset);
    True(store.Save(state)); True(File.ReadAllText(store.FilePath).Contains("\"None\""));
    var loaded = store.Load(); True(store.Warning is null); Equal(OpacityLevel.None, loaded.Opacity);
    Equal(OpacityLevel.None, loaded.Groups.Single().Appearance.Opacity); Equal(OpacityLevel.None, loaded.Widgets.Single().Appearance.Opacity);
    loaded.Opacity = OpacityLevel.High; loaded.Widgets.Single().Appearance.Opacity = OpacityLevel.Mid;
    LayoutPresets.RestoreState(loaded, LayoutPresets.Prepare(loaded, loaded.Presets.Single()));
    Equal(0.0, AppearanceResolver.Resolve(loaded, loaded.Widgets.Single()).BackgroundOpacity);
    Equal(8.0, AppearanceResolver.Resolve(loaded, loaded.Widgets.Single()).CornerRadius);
    Equal(ActivityPeriod.Year, loaded.Widgets.Single().ActivityPeriod);
}));
Check("Opacity None: preset Undo restores None without sharing mutable overrides", () =>
{
    var widget = new WidgetInstance { Appearance = new() { Opacity = OpacityLevel.None } };
    var state = new AppSettings { Opacity = OpacityLevel.None, Widgets = [widget] };
    var before = LayoutPresets.Snapshot(state);
    var target = LayoutPresets.Capture(state, "Opaque"); target.Opacity = OpacityLevel.High; target.Widgets.Single().Appearance.Opacity = null;
    state.Presets.Add(target); LayoutPresets.RestoreState(state, LayoutPresets.Prepare(state, target));
    Equal(.96, AppearanceResolver.Resolve(state, state.Widgets.Single()).BackgroundOpacity);
    LayoutPresets.RestoreState(state, LayoutPresets.PrepareUndo(state, before));
    Equal(0.0, AppearanceResolver.Resolve(state, state.Widgets.Single()).BackgroundOpacity);
    state.Widgets.Single().Appearance.Opacity = OpacityLevel.Low;
    Equal(OpacityLevel.None, before.Widgets.Single().Appearance.Opacity);
});
Check("Opacity: invalid values are rejected without admitting None as Radius", () =>
{
    foreach (var state in new[]
    {
        new AppSettings { Radius = (Level)3 }, new AppSettings { Opacity = (OpacityLevel)99 },
        new AppSettings { Widgets = [new() { Appearance = new() { Opacity = (OpacityLevel)99 } }] },
        new AppSettings { Groups = [new() { Appearance = new() { Radius = (Level)3 } }] },
        new AppSettings { Presets = [new() { Opacity = (OpacityLevel)99 }] }
    })
    {
        bool rejected = false;
        try { SettingsStore.ValidateAndNormalize(state); } catch (System.Text.Json.JsonException) { rejected = true; }
        True(rejected);
    }
    WithOpacityStore(store =>
    {
        File.WriteAllText(store.FilePath, "{\"Opacity\":99}"); var loaded = store.Load();
        Equal(OpacityLevel.High, loaded.Opacity); True(store.Warning is not null);
        True(Directory.GetFiles(store.DirectoryPath, "settings.invalid-*.json").Length == 1);
    });
});
Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
