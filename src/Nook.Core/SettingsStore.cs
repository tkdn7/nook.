using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Nook.Core;

public sealed class SettingsStore(string directory)
{
    public string DirectoryPath { get; } = directory;
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public string? Warning { get; private set; }
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(FilePath));
            // Filter the retired widget before enum conversion so other saved widgets survive.
            // Numeric id 22 belonged to the removed widget; all remaining ids are unchanged.
            if (root is JsonObject config && config["Widgets"] is JsonArray widgets)
                for (int i = widgets.Count - 1; i >= 0; i--)
                    if (widgets[i] is JsonObject widget && widget["Kind"] is JsonValue kind
                        && (kind.TryGetValue<string>(out var name) && (string.Equals(name?.Trim(), "Cat", StringComparison.OrdinalIgnoreCase) || name?.Trim() == "22")
                            || kind.TryGetValue<int>(out int id) && id == 22))
                        widgets.RemoveAt(i);
            var settings = root?.Deserialize<AppSettings>(Options)
                ?? throw new JsonException("설정이 비어 있습니다.");
            Validate(settings);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Warning = "설정을 읽지 못해 기본값으로 시작했습니다.";
            try
            {
                File.Copy(FilePath, Path.Combine(DirectoryPath, $"settings.invalid-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"));
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            {
                Warning += " 원본 백업에 실패하여 자동 저장을 중지했습니다.";
                _preserveOriginal = true;
            }
            return new();
        }
    }

    private bool _preserveOriginal;
    public bool Save(AppSettings settings)
    {
        if (_preserveOriginal) return false;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, FilePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warning = "설정을 저장할 수 없습니다. 폴더 접근 권한을 확인해 주세요.";
            return false;
        }
    }

    public static void ValidateAndNormalize(AppSettings settings) => Validate(settings);

    private static void Validate(AppSettings s)
    {
        if (s.Version != 1 || !Enum.IsDefined(s.Radius) || !Enum.IsDefined(s.Opacity) || !Enum.IsDefined(s.Theme)
            || s.MainTheme is { } mainTheme && !Enum.IsDefined(mainTheme)
            || s.Widgets is null || s.Groups is null || s.Presets is null || s.MemoArchive is null) throw new JsonException("지원하지 않는 설정입니다.");
        if (s.MemoArchive.Any(p => p.Key == Guid.Empty || p.Value is null || p.Value.Length > 100000)) throw new JsonException("메모 보관 설정이 잘못되었습니다.");
        if (s.Presets.Any(p => p is null || p.Id == Guid.Empty || string.IsNullOrWhiteSpace(p.Name))
            || s.Presets.Select(p => p.Id).Distinct().Count() != s.Presets.Count) throw new JsonException("프리셋 설정이 잘못되었습니다.");
        foreach (var preset in s.Presets)
        {
            Validate(new AppSettings { Widgets = preset.Widgets, Groups = preset.Groups, Radius = preset.Radius, Opacity = preset.Opacity, Theme = preset.Theme });
            foreach (var widget in preset.Widgets) CountdownEngine.Reset(widget.Countdown);
        }
        if (!s.Presets.Any(p => p.Id == s.ActivePresetId)) s.ActivePresetId = null;
        if (s.Groups.Any(g => g is null || g.Id == Guid.Empty || string.IsNullOrWhiteSpace(g.Name) || !Valid(g.Appearance))
            || s.Groups.Select(g => g.Id).Distinct().Count() != s.Groups.Count) throw new JsonException("그룹 설정이 잘못되었습니다.");
        foreach (var w in s.Widgets)
        {
            if (w is null || w.Id == Guid.Empty || !Enum.IsDefined(w.Kind) || !Enum.IsDefined(w.Shape) || !Valid(w.Appearance) || w.Theme is { } theme && !Enum.IsDefined(theme)
                || !double.IsFinite(w.Left) || !double.IsFinite(w.Top) || !double.IsFinite(w.Width) || !double.IsFinite(w.Height))
                throw new JsonException("위젯 설정이 잘못되었습니다.");
            w.Width = Math.Clamp(w.Width, 180, WidgetLimits.MaxWidth(w.Kind));
            w.Height = Math.Clamp(w.Height, 140, WidgetLimits.MaxHeight(w.Kind));
            if (w.AirQuality is null || !Enum.IsDefined(w.AirQuality.Style) || !Enum.IsDefined(w.AirQuality.Stage)
                || w.Media is null || !Enum.IsDefined(w.Media.Mode) || w.Media.Priority is null || w.Shelf is null
                || w.SystemButtons is null || w.SystemButtons.Any(x => !Enum.IsDefined(x)) || w.MemoText is null
                || w.Shelf.Any(x => x is null || x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Path) || x.Name is null)
                || w.Shelf.Select(x => x.Id).Distinct().Count() != w.Shelf.Count)
                throw new JsonException("위젯 기능 설정이 잘못되었습니다.");
            if (!Enum.IsDefined(w.ControlOrientation)) throw new JsonException("조절 방향이 잘못되었습니다.");
            w.BrightnessTarget ??= "";
            w.Name ??= ""; w.Name = w.Name.Trim(); if (w.Name.Length > 60) w.Name = w.Name[..60];
            if (!Enum.IsDefined(w.ActivityPeriod)) throw new JsonException("활동 설정이 잘못되었습니다.");
            if (!Enum.IsDefined(w.ClockStyle) || w.Countdown is null || !Enum.IsDefined(w.Countdown.Phase) || !double.IsFinite(w.Countdown.RemainingSeconds)) throw new JsonException("시간 설정이 잘못되었습니다.");
            w.Countdown.TimerSeconds = Math.Clamp(w.Countdown.TimerSeconds, 1, 86400);
            w.Countdown.FocusMinutes = Math.Clamp(w.Countdown.FocusMinutes, 1, 180);
            w.Countdown.BreakMinutes = Math.Clamp(w.Countdown.BreakMinutes, 1, 60);
            w.Countdown.LongBreakMinutes = Math.Clamp(w.Countdown.LongBreakMinutes, 1, 120);
            w.Countdown.CompletedFocus = Math.Clamp(w.Countdown.CompletedFocus, 0, 1000000);
            w.Countdown.RemainingSeconds = Math.Clamp(w.Countdown.RemainingSeconds, -1, 86400);
            w.DiceCount = Math.Clamp(w.DiceCount, 1, 3);
            if (!Enum.IsDefined(w.BreatheTone)) throw new JsonException("그라데이션 설정이 잘못되었습니다.");
            if (!s.Widgets.Any(m => m.Kind == WidgetKind.Memo && m.Id == w.CalculatorMemoId)) w.CalculatorMemoId = null;
            w.ScreenTimeEtcThreshold = double.IsFinite(w.ScreenTimeEtcThreshold) ? Math.Clamp(w.ScreenTimeEtcThreshold, 1, 10) : 3;
            w.Media.PinnedApp ??= "";
            w.Media.Priority = w.Media.Priority.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            w.SystemButtons = w.SystemButtons.Distinct().ToList();
            WidgetShapes.Normalize(w);
            if (!s.Groups.Any(g => g.Id == w.GroupId)) w.GroupId = null;
        }
        if (s.Widgets.Select(w => w.Id).Distinct().Count() != s.Widgets.Count) throw new JsonException("중복 위젯 ID입니다.");
    }
    private static bool Valid(AppearanceOverride? a) => a is not null
        && (a.Radius is null || Enum.IsDefined(a.Radius.Value)) && (a.Opacity is null || Enum.IsDefined(a.Opacity.Value));
}
