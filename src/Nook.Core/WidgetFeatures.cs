namespace Nook.Core;

public enum AirStyle { Casual, Modern }
public enum AirStage { Good, Moderate, Sensitive, Unhealthy, VeryUnhealthy, Hazardous, Loading, Unavailable }
public enum SystemAction { Mute, Shutdown, Restart, Wifi, DoNotDisturb, MicrophoneMute, Capture }
public enum MediaMode { Automatic, Pinned }
public sealed class AirQualityOptions
{
    public AirStyle Style { get; set; } = AirStyle.Casual;
    public AirStage Stage { get; set; } = AirStage.Good;
}
public readonly record struct AirSample(string Label, int? Aqi, string Color)
{
    public static AirSample For(AirStage stage) => stage switch
    {
        AirStage.Good => new("좋음", 28, "#69BDA1"),
        AirStage.Moderate => new("보통", 76, "#D3BA6C"),
        AirStage.Sensitive => new("민감군 주의", 128, "#DEA16C"),
        AirStage.Unhealthy => new("나쁨", 178, "#CE827D"),
        AirStage.VeryUnhealthy => new("매우 나쁨", 248, "#A493C5"),
        AirStage.Hazardous => new("위험", 368, "#A76E86"),
        AirStage.Loading => new("불러오는 중", null, "#A5B0B7"),
        _ => new("데이터 없음", null, "#AAA69F")
    };
}
public sealed class ShelfEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}
public sealed class MediaOptions
{
    public MediaMode Mode { get; set; }
    public string PinnedApp { get; set; } = "";
    public List<string> Priority { get; set; } = ["Spotify", "Chrome", "Edge"];
}
public sealed record MediaCandidate(string Id, string AppId, bool Playing, bool Current);
public static class MediaSelection
{
    // App IDs persist across runs; transient session IDs distinguish simultaneous sessions.
    public static MediaCandidate? Choose(IEnumerable<MediaCandidate> candidates, MediaOptions options, string? previous = null)
    {
        var list = candidates.ToArray();
        if (options.Mode == MediaMode.Pinned)
            return list.Where(x => x.AppId.Equals(options.PinnedApp, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Id == previous).ThenByDescending(x => x.Playing).ThenByDescending(x => x.Current).FirstOrDefault();
        int Rank(MediaCandidate x)
        {
            int index = options.Priority.FindIndex(p => !string.IsNullOrWhiteSpace(p) && x.AppId.Contains(p, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? int.MaxValue : index;
        }
        if (!list.Any(x => x.Playing) && list.FirstOrDefault(x => x.Id == previous) is { } paused) return paused;
        // Keep the same session on ties, including pauses; a higher-priority playing session wins.
        return list.OrderByDescending(x => x.Playing).ThenBy(Rank)
            .ThenByDescending(x => x.Id == previous).ThenByDescending(x => x.Current).ThenBy(x => x.Id).FirstOrDefault();
    }
}
public readonly record struct BatterySample(int? Percentage, bool Present, bool Charging, bool Plugged, int? SecondsRemaining)
{
    public static BatterySample Decode(byte ac, byte flags, byte percentage, uint seconds)
    {
        bool present = flags == 255 || (flags & 128) == 0;
        return new(percentage <= 100 && present ? percentage : null, present, present && flags != 255 && (flags & 8) != 0,
            ac == 1, present && seconds != uint.MaxValue && seconds <= int.MaxValue ? (int)seconds : null);
    }
}
