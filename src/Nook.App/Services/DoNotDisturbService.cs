using System.Runtime.InteropServices;

namespace Nook.App.Services;

// Windows' QuietHoursSettings local COM server. This interface is not a public
// Windows SDK contract: unsupported builds return an unavailable state.
public static class DoNotDisturbService
{
    private const string Off = "Microsoft.QuietHoursProfile.Unrestricted";
    private const string On = "Microsoft.QuietHoursProfile.PriorityOnly";
    [ComImport, Guid("6BFF4732-81EC-4FFB-AE67-B6C1BC29631F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IQuietHours
    {
        void GetUserSelectedProfile(out IntPtr profile);
        void SetUserSelectedProfile([MarshalAs(UnmanagedType.LPWStr)] string profile);
    }
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IQuietHours value);
    private static T WithSettings<T>(Func<IQuietHours, T> action)
    {
        var clsid = new Guid("F53321FA-34F8-4B7F-B9A3-361877CB94CF"); var iid = typeof(IQuietHours).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 4, ref iid, out var settings));
        try { return action(settings); } finally { Marshal.ReleaseComObject(settings); }
    }
    private static string? Profile(IQuietHours settings)
    {
        settings.GetUserSelectedProfile(out var pointer);
        try { return Marshal.PtrToStringUni(pointer); } finally { Marshal.FreeCoTaskMem(pointer); }
    }
    public static bool? Read()
    {
        try { return WithSettings<bool?>(s => Profile(s) switch { Off => false, On or "Microsoft.QuietHoursProfile.AlarmsOnly" => true, _ => null }); }
        catch (Exception) { return null; }
    }
    public static string? ReadProfile() => WithSettings(Profile);
    public static void RestoreProfile(string profile)
    {
        if (profile is not (Off or On or "Microsoft.QuietHoursProfile.AlarmsOnly")) throw new ArgumentException("Unknown quiet-hours profile.", nameof(profile));
        WithSettings(s => { s.SetUserSelectedProfile(profile); return true; });
    }
    public static void Set(bool enabled) => WithSettings(s => { s.SetUserSelectedProfile(enabled ? On : Off); return true; });
    public static void Toggle()
    {
        bool? enabled = Read();
        if (!enabled.HasValue) throw new InvalidOperationException("이 Windows 버전에서 방해 금지를 제어할 수 없습니다.");
        Set(!enabled.Value);
    }
}
