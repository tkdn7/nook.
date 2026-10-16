namespace Nook.Core;

public sealed record UsageApp(string Id, string Name, string Color);
public sealed class UsageEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#87949E";
    public double Seconds { get; set; }
}
public sealed class UsageDay
{
    public DateOnly Date { get; set; }
    public List<UsageEntry> Apps { get; set; } = [];
    public double TotalSeconds => Apps.Sum(x => x.Seconds);
}
public sealed record UsageShare(string Id, string Name, string Color, double Seconds, double Percentage, string Detail = "", IReadOnlyList<UsageShare>? Members = null);

/// <summary>Foreground usage only. Monotonic intervals prevent clock edits and sleep from inflating totals.</summary>
public sealed class UsageLedger
{
    public List<UsageDay> Days { get; }
    private long? _previousTick;
    private DateTimeOffset _previousTime;
    private UsageApp? _previousApp;
    public UsageLedger(List<UsageDay>? days = null) => Days = days ?? [];
    public void Reset() { _previousTick = null; _previousApp = null; }
    public void Sample(long tick, DateTimeOffset localTime, UsageApp? activeApp)
    {
        if (_previousTick is { } previous && _previousApp is { } app && activeApp is not null)
        {
            double seconds = (tick - previous) / 1000.0;
            // A suspended PC or a delayed collector has no observed foreground interval.
            if (seconds is > 0 and <= 5 && localTime >= _previousTime && (localTime - _previousTime).TotalSeconds <= 5)
            {
                var date = DateOnly.FromDateTime(localTime.DateTime);
                var previousDate = DateOnly.FromDateTime(_previousTime.DateTime);
                if (date != previousDate)
                {
                    var midnight = new DateTimeOffset(localTime.DateTime.Date, localTime.Offset);
                    double tail = Math.Clamp((localTime - midnight).TotalSeconds, 0, seconds);
                    Add(previousDate, app, seconds - tail); Add(date, app, tail);
                }
                else Add(date, app, seconds);
            }
        }
        _previousTick = tick; _previousTime = localTime; _previousApp = activeApp;
        Days.RemoveAll(d => d.Date < DateOnly.FromDateTime(localTime.DateTime).AddDays(-365));
    }
    private void Add(DateOnly date, UsageApp app, double seconds)
    {
        if (seconds <= 0) return;
        var day = Days.Find(x => x.Date == date);
        if (day is null) { day = new() { Date = date }; Days.Add(day); }
        var entry = day.Apps.Find(x => x.Id == app.Id);
        if (entry is null) { entry = new() { Id = app.Id, Name = app.Name, Color = app.Color }; day.Apps.Add(entry); }
        entry.Seconds += seconds;
    }
    public UsageDay Today(DateOnly date) => Days.Find(x => x.Date == date) ?? new() { Date = date };
    public static UsageDay Excluding(UsageDay day, string id) => new() { Date = day.Date, Apps = day.Apps.Where(a => !string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)).ToList() };
    public static IReadOnlyList<UsageShare> Shares(UsageDay day, double threshold = 3)
    {
        double total = day.TotalSeconds;
        if (total <= 0) return [];
        var result = day.Apps.Where(x => x.Id != "unknown" && x.Seconds / total * 100 >= threshold)
            .OrderByDescending(x => x.Seconds).ThenBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => new UsageShare(x.Id, x.Name, x.Color, x.Seconds, x.Seconds / total * 100)).ToList();
        double rest = total - result.Sum(x => x.Seconds);
        if (rest > .000001)
        {
            var visibleIds = result.Select(x => x.Id).ToHashSet();
            string detail = string.Join("\n", day.Apps.Where(x => !visibleIds.Contains(x.Id)).OrderByDescending(x => x.Seconds).Select(x => $"{x.Name} · {x.Seconds / total * 100:F1}% · {Duration(x.Seconds)}"));
            var members = day.Apps.Where(x => !visibleIds.Contains(x.Id)).OrderByDescending(x => x.Seconds)
                .Select(x => new UsageShare(x.Id, x.Name, x.Color, x.Seconds, x.Seconds / total * 100)).ToArray();
            result.Add(new("etc", "ETC", "#91969B", rest, rest / total * 100, detail, members));
        }
        return result;
    }
    public static string Duration(double seconds)
    {
        int minutes = (int)(Math.Max(0, seconds) / 60);
        if (minutes == 0) return $"{(int)Math.Max(0, seconds)}초";
        return minutes >= 60 ? $"{minutes / 60}시간 {minutes % 60}분" : $"{minutes}분";
    }
}
