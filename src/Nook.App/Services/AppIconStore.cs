using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Nook.App.Services;

/// <summary>Frozen Windows Shell icons. Disk cache uses app IDs, never executable paths.</summary>
public sealed class AppIconStore(string directory)
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FileInfo
    { public IntPtr Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Type; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern UIntPtr SHGetFileInfoW(string path, uint attributes, out FileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImageSource> _apps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _directory = Path.Combine(directory, "icons");
    private int _version;
    public int Version => System.Threading.Volatile.Read(ref _version);
    public static Task<ImageSource?> ForPathAsync(string path) => Paths.GetOrAdd(path, key => Task.Run(() => ReadShell(key)));
    private static ImageSource? ReadShell(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return null;
        IntPtr icon = IntPtr.Zero;
        int apartment = CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            if (SHGetFileInfoW(path, 0, out var info, (uint)Marshal.SizeOf<FileInfo>(), 0x100) == UIntPtr.Zero) return null;
            icon = info.Icon; if (icon == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze(); return source;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or ExternalException or NotSupportedException or FormatException) { return null; }
        finally { if (icon != IntPtr.Zero) DestroyIcon(icon); if (apartment >= 0) CoUninitialize(); }
    }
    private string FileFor(string id) => Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToLowerInvariant())))[..32] + ".png");
    public ImageSource? Get(string id)
    {
        if (id is "etc" or "unknown" || string.IsNullOrWhiteSpace(id)) return null;
        if (_apps.TryGetValue(id, out var image)) return image;
        if (_attempts.TryGetValue(id, out var previous) && DateTimeOffset.UtcNow - previous < TimeSpan.FromMinutes(1)) return null;
        _attempts[id] = DateTimeOffset.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                string file = FileFor(id);
                if (File.Exists(file))
                {
                    using var stream = File.OpenRead(file); var source = new BitmapImage(); source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.StreamSource = stream; source.EndInit(); source.Freeze();
                    _apps[id] = source; System.Threading.Interlocked.Increment(ref _version); return;
                }
                // Older usage records can regain their icons when the app is running.
                var processes = Process.GetProcessesByName(id);
                try
                {
                    foreach (var process in processes)
                    { try { if (process.MainModule?.FileName is { } path) { await CaptureAsync(id, path); return; } } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { } }
                }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception or FormatException) { }
        });
        return null;
    }
    public async Task CaptureAsync(string id, string path)
    {
        if (_apps.ContainsKey(id)) return;
        var image = await ForPathAsync(path).ConfigureAwait(false);
        if (image is not BitmapSource source) return;
        _apps[id] = source; System.Threading.Interlocked.Increment(ref _version);
        try
        {
            Directory.CreateDirectory(_directory); string file = FileFor(id), temporary = file + ".tmp-" + Guid.NewGuid().ToString("N");
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(temporary)) png.Save(stream);
            File.Move(temporary, file, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ExternalException) { }
    }
}
