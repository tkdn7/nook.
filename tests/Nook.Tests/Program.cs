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

Check("Appearance: per-property inheritance and global changes", () =>
{
    var s = new AppSettings(); var g = new WidgetGroup { Name = "시스템", Appearance = new() { Radius = Level.High } }; s.Groups.Add(g);
    var w = new WidgetInstance { GroupId = g.Id, Appearance = new() { Opacity = Level.Low } }; s.Widgets.Add(w);
    var r = AppearanceResolver.Resolve(s, w);
    Equal(Level.High, r.Radius.Value); Equal("그룹: 시스템", r.Radius.Source); Equal(Level.Low, r.Opacity.Value);
    s.Radius = Level.Low; s.Opacity = Level.Mid;
    r = AppearanceResolver.Resolve(s, w); Equal(Level.High, r.Radius.Value); Equal(Level.Low, r.Opacity.Value);
    w.Appearance.Radius = Level.Mid; Equal("개별 위젯", AppearanceResolver.Resolve(s, w).Radius.Source);
    w.Appearance = new(); Equal(Level.Mid, AppearanceResolver.Resolve(s, w).Opacity.Value);
});
Check("Appearance: reassignment, deletion preserves individual values, reset", () =>
{
    var s = new AppSettings(); var a = new WidgetGroup { Appearance = new() { Radius = Level.High } };
    var b = new WidgetGroup { Appearance = new() { Radius = Level.Low } }; s.Groups.AddRange([a, b]);
    var w = new WidgetInstance { GroupId = a.Id, Appearance = new() { Opacity = Level.Low } }; s.Widgets.Add(w);
    w.GroupId = b.Id; Equal(Level.Low, AppearanceResolver.Resolve(s, w).Radius.Value);
    AppearanceResolver.DeleteGroup(s, b.Id); Equal<Guid?>(null, w.GroupId); Equal(Level.Low, w.Appearance.Opacity);
    Equal(s.Radius, AppearanceResolver.Resolve(s, w).Radius.Value);
    AppearanceResolver.ResetAll(s); Equal<Level?>(null, w.Appearance.Opacity); Equal<Level?>(null, a.Appearance.Radius);
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
var testRoot = Path.Combine(Path.GetTempPath(), "Nook-tests-" + Guid.NewGuid().ToString("N"));
Check("Shape: metric-only circle and square normalization", () =>
{
    foreach (var kind in Enum.GetValues<WidgetKind>())
    {
        var widget = new WidgetInstance { Kind = kind, Shape = WidgetShape.Circle, Width = 300, Height = 200 };
        WidgetShapes.Normalize(widget);
        bool metric = kind is WidgetKind.Cpu or WidgetKind.Memory or WidgetKind.Gpu;
        Equal(metric ? WidgetShape.Circle : WidgetShape.Card, widget.Shape);
        Equal(metric ? 300.0 : 200.0, widget.Height);
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
}
finally { Directory.Delete(testRoot, true); }
Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
