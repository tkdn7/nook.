namespace Nook.Core;

public enum ActivityPeriod { Week, Month, HalfYear, Year }
public sealed record ActivityCell(DateOnly Date, double Seconds, int Level);
public static class ActivityHistory
{
    public static DateOnly Start(DateOnly today, ActivityPeriod period) => period switch
    { ActivityPeriod.Week => today.AddDays(-6), ActivityPeriod.Month => today.AddMonths(-1).AddDays(1), ActivityPeriod.HalfYear => today.AddMonths(-6).AddDays(1), _ => today.AddYears(-1).AddDays(1) };
    public static int Level(double seconds) => seconds <= 0 ? 0 : seconds < 1800 ? 1 : seconds < 7200 ? 2 : seconds < 14400 ? 3 : 4;
    public static IReadOnlyList<ActivityCell> Cells(IEnumerable<UsageDay> days, DateOnly today, ActivityPeriod period)
    {
        var totals = days.ToDictionary(x => x.Date, x => x.TotalSeconds);
        var start = Start(today, period); var result = new List<ActivityCell>();
        for (var date = start; date <= today; date = date.AddDays(1)) { double seconds = totals.GetValueOrDefault(date); result.Add(new(date, seconds, Level(seconds))); }
        return result;
    }
}
