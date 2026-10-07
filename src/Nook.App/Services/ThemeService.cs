using Microsoft.Win32;

namespace Nook.App.Services;

public sealed class ThemeService : IDisposable
{
    public bool IsDark { get; private set; }
    public ThemeMode Mode { get; private set; }
    public bool SystemIsDark { get; private set; }
    public bool DarkFor(ThemeMode mode) => mode == ThemeMode.Dark || mode == ThemeMode.System && SystemIsDark;
    public event Action? Changed;
    public ThemeService() => SystemEvents.UserPreferenceChanged += OnPreference;
    private void OnPreference(object sender, UserPreferenceChangedEventArgs e) =>
        Application.Current.Dispatcher.BeginInvoke(() => Apply(Mode));
    public void Apply(ThemeMode mode)
    {
        Mode = mode;
        bool systemDark = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            systemDark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException) { }
        SystemIsDark = systemDark;
        IsDark = DarkFor(mode);
        foreach (var (key, color) in Palette(IsDark))
            Application.Current.Resources[key] = new SolidColorBrush(color);
        Changed?.Invoke();
    }
    public static IReadOnlyDictionary<string, Color> Palette(bool dark)
    {
        var colors = dark
            ? new[] { "#181818", "#141414", "#222222", "#F6F0E6", "#AAAAA6", "#383838", "#F6F0E6", "#2C2C2C", "#171717", "#F6F0E6", "#191919", "#646460" }
            : new[] { "#F6F0E6", "#EDE6DB", "#F6F0E6", "#202020", "#70706A", "#DCD4C7", "#242424", "#E9E1D4", "#F6F0E6", "#242424", "#F6F0E6", "#BDBDB6" };
        string[] names = ["PageBrush", "SidebarBrush", "SurfaceBrush", "TextBrush", "MutedBrush", "LineBrush", "AccentBrush", "SoftAccentBrush", "AccentTextBrush", "ClockSurfaceBrush", "ClockTextBrush", "ClockMutedBrush"];
        return names.Select((name, i) => (name, color: (Color)ColorConverter.ConvertFromString(colors[i]))).ToDictionary(p => p.name, p => p.color);
    }
    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnPreference;
}
