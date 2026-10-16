using Microsoft.Win32;

namespace Nook.App.Services;

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly Func<string?>? _read;
    private readonly Action<string?>? _write;
    private readonly string _executable;
    public bool Available => _read is not null && _write is not null;
    public bool Enabled { get { try { return Available && !string.IsNullOrWhiteSpace(_read!()); } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; } } }
    public StartupService() { _executable = ""; }
    public StartupService(Func<string?> read, Action<string?> write, string executable)
    { _read = read; _write = write; _executable = executable; }
    public static StartupService ForCurrentUser(bool enabled)
    {
        if (!enabled || Environment.ProcessPath is not { } executable) return new();
        return new(() => { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("Nook") as string; },
            value => { using var key = Registry.CurrentUser.CreateSubKey(RunKey, true); if (value is null) key.DeleteValue("Nook", false); else key.SetValue("Nook", value, RegistryValueKind.String); }, executable);
    }
    public static string Command(string executable)
    {
        string path = Path.GetFullPath(executable);
        if (path.Contains('"') || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("실행 파일 경로를 확인해 주세요.");
        string command = '"' + path + "\" --background";
        if (command.Length > 260) throw new IOException("더 짧은 경로에 Nook을 두고 다시 설정해 주세요.");
        return command;
    }
    public void SetEnabled(bool enabled)
    {
        if (!Available) throw new InvalidOperationException("별도 설정으로 실행 중입니다.");
        _write!(enabled ? Command(_executable) : null);
    }
}
