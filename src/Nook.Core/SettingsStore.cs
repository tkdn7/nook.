using System.Text.Json;
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
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options)
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

    private static void Validate(AppSettings s)
    {
        if (s.Version != 1 || !Enum.IsDefined(s.Radius) || !Enum.IsDefined(s.Opacity) || !Enum.IsDefined(s.Theme)
            || s.Widgets is null || s.Groups is null) throw new JsonException("지원하지 않는 설정입니다.");
        if (s.Groups.Any(g => g is null || g.Id == Guid.Empty || string.IsNullOrWhiteSpace(g.Name) || !Valid(g.Appearance))
            || s.Groups.Select(g => g.Id).Distinct().Count() != s.Groups.Count) throw new JsonException("그룹 설정이 잘못되었습니다.");
        foreach (var w in s.Widgets)
        {
            if (w is null || w.Id == Guid.Empty || !Enum.IsDefined(w.Kind) || !Enum.IsDefined(w.Shape) || !Valid(w.Appearance) || w.Theme is { } theme && !Enum.IsDefined(theme)
                || !double.IsFinite(w.Left) || !double.IsFinite(w.Top) || !double.IsFinite(w.Width) || !double.IsFinite(w.Height))
                throw new JsonException("위젯 설정이 잘못되었습니다.");
            w.Width = Math.Clamp(w.Width, 180, 640);
            w.Height = Math.Clamp(w.Height, 140, 480);
            WidgetShapes.Normalize(w);
            if (!s.Groups.Any(g => g.Id == w.GroupId)) w.GroupId = null;
        }
        if (s.Widgets.Select(w => w.Id).Distinct().Count() != s.Widgets.Count) throw new JsonException("중복 위젯 ID입니다.");
    }
    private static bool Valid(AppearanceOverride? a) => a is not null
        && (a.Radius is null || Enum.IsDefined(a.Radius.Value)) && (a.Opacity is null || Enum.IsDefined(a.Opacity.Value));
}
