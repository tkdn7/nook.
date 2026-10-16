namespace Nook.Core;

public enum ClockStyle { Text, Analog }
public enum FocusPhase { Focus, ShortBreak, LongBreak }
public sealed class CountdownOptions
{
    public int TimerSeconds { get; set; } = 300;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public FocusPhase Phase { get; set; }
    public int CompletedFocus { get; set; }
    public double RemainingSeconds { get; set; } = -1;
    public DateTimeOffset? Deadline { get; set; }
    public bool Finished { get; set; }
}
public static class CountdownEngine
{
    public static double Duration(CountdownOptions o, bool focus) => focus ? 60 * (o.Phase switch { FocusPhase.Focus => o.FocusMinutes, FocusPhase.ShortBreak => o.BreakMinutes, _ => o.LongBreakMinutes }) : o.TimerSeconds;
    public static double Remaining(CountdownOptions o, bool focus, DateTimeOffset now) => o.Deadline is { } end ? Math.Clamp((end - now).TotalSeconds, 0, Duration(o, focus)) : o.RemainingSeconds < 0 ? Duration(o, focus) : Math.Clamp(o.RemainingSeconds, 0, Duration(o, focus));
    public static double Progress(CountdownOptions o, bool focus, DateTimeOffset now) => Math.Clamp(1 - Remaining(o, focus, now) / Duration(o, focus), 0, 1);
    public static bool Tick(CountdownOptions o, bool focus, DateTimeOffset now)
    {
        if (o.Deadline is not { } end || end > now) return false;
        o.Deadline = null; o.RemainingSeconds = 0; o.Finished = true;
        if (focus) Next(o);
        return true;
    }
    public static void Toggle(CountdownOptions o, bool focus, DateTimeOffset now)
    {
        Tick(o, focus, now);
        if (o.Deadline is not null) { o.RemainingSeconds = Remaining(o, focus, now); o.Deadline = null; }
        else { if (Remaining(o, focus, now) <= 0) o.RemainingSeconds = Duration(o, focus); o.Deadline = now.AddSeconds(Remaining(o, focus, now)); o.Finished = false; }
    }
    public static void Reset(CountdownOptions o) { o.Deadline = null; o.RemainingSeconds = -1; o.Finished = false; }
    public static void Next(CountdownOptions o)
    {
        if (o.Phase == FocusPhase.Focus) { o.CompletedFocus++; o.Phase = o.CompletedFocus % 4 == 0 ? FocusPhase.LongBreak : FocusPhase.ShortBreak; }
        else o.Phase = FocusPhase.Focus;
        o.Deadline = null; o.RemainingSeconds = -1;
    }
    public static string Display(double seconds) { int value = (int)Math.Ceiling(Math.Max(0, seconds)); return value >= 3600 ? $"{value / 3600}:{value / 60 % 60:00}:{value % 60:00}" : $"{value / 60:00}:{value % 60:00}"; }
}
public readonly record struct NetworkRate(double? Download, double? Upload, string Name);
public sealed class RateDelta
{
    private (long Rx, long Tx, DateTimeOffset Time)? _previous;
    public NetworkRate Sample(long rx, long tx, DateTimeOffset time, string name)
    {
        var previous = _previous; _previous = (rx, tx, time);
        if (previous is not { } p || time <= p.Time || (time - p.Time).TotalSeconds > 5 || rx < p.Rx || tx < p.Tx) return new(null, null, name);
        double elapsed = (time - p.Time).TotalSeconds;
        return new((rx - p.Rx) / elapsed, (tx - p.Tx) / elapsed, name);
    }
}
public sealed record WidgetEntry(WidgetKind Kind, string Name, string Description, string Category, string Keywords);
public static class WidgetCatalog
{
    public static IReadOnlyList<WidgetEntry> All { get; } = [
        new(WidgetKind.Clock,"Clock","시간과 날짜","일상","시계 clock"), new(WidgetKind.Weather,"Weather","샘플 날씨","일상","날씨 weather"), new(WidgetKind.AirQuality,"Air Quality","대기질","일상","aqi 공기"),
        new(WidgetKind.Cpu,"CPU","프로세서 사용량","시스템","cpu"), new(WidgetKind.Memory,"Memory","물리 메모리","시스템","ram 메모리"), new(WidgetKind.Gpu,"GPU","그래픽 사용량","시스템","gpu"), new(WidgetKind.Disk,"Disk","읽기 · 쓰기 속도","시스템","디스크 read write"), new(WidgetKind.Network,"Network","다운로드 · 업로드","시스템","인터넷 네트워크 속도"), new(WidgetKind.Battery,"Battery","배터리와 충전","시스템","배터리"),
        new(WidgetKind.ProgramShelf,"Program Shelf","폴더와 프로그램","도구","프로그램 바로가기"), new(WidgetKind.SystemShelf,"System Shelf","시스템 컨트롤","도구","마이크 캡처 음소거"), new(WidgetKind.Bluetooth,"Bluetooth","연결된 장치","도구","블루투스 장치 배터리"), new(WidgetKind.Volume,"Volume","시스템 볼륨","도구","볼륨 소리"), new(WidgetKind.Brightness,"Brightness","디스플레이 밝기","도구","밝기"), new(WidgetKind.Media,"Media","음악과 영상","일상","미디어 음악"),
        new(WidgetKind.Memo,"Memo","나의 메모","집중","메모 sticky"), new(WidgetKind.Pomodoro,"Pomodoro","집중과 휴식","집중","포모도로 pomodoro"), new(WidgetKind.Timer,"Timer","나만의 타이머","집중","타이머 timer"), new(WidgetKind.ScreenTime,"Screen Time","오늘의 PC 사용 시간","집중","스크린타임"), new(WidgetKind.KeyStats,"Key Stats","전체 타자 수","집중","키보드 타자 통계"), new(WidgetKind.Activity,"Activity","PC 사용 기록","집중","잔디 접속 활동 기록 heatmap"), new(WidgetKind.Dice,"Dice","가볍게 굴리는 주사위","놀이","주사위 게임 dice"),
        new(WidgetKind.MediaController,"Media Controller","음향 출력 장치","도구","오디오 스피커 헤드폰 출력"), new(WidgetKind.Calculator,"Calculator","계산과 메모 전송","도구","계산기 공학 수학"), new(WidgetKind.Breathe,"Breathe","부드러운 색의 흐름","집중","호흡 breathe 숨 휴식") ];
    public static IEnumerable<WidgetEntry> Search(string category, string query) => All.Where(x => (category == "전체" || x.Category == category) && $"{x.Name} {x.Description} {x.Keywords}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
}

