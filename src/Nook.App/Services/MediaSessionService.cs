using Windows.Media.Control;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Collections.Concurrent;

namespace Nook.App.Services;

public sealed record MediaSnapshot(string Id, string AppId, string AppName, string Title, string Artist,
    bool Playing, bool Current, bool CanPlay, bool CanPause, bool CanNext, bool CanPrevious,
    TimeSpan Position, TimeSpan Duration, ImageSource? Artwork = null);

public sealed class MediaSessionService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Dictionary<string, GlobalSystemMediaTransportControlsSession> _sessions = [];
    private bool _busy, _disposed;
    private readonly Dictionary<string, (string Track, ImageSource? Cover, DateTimeOffset Read)> _covers = [];
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> _observed = [];
    private readonly ConcurrentDictionary<string, byte> _dirtyCovers = new();
    public IReadOnlyList<MediaSnapshot> Sessions { get; private set; } = [];
    public string Error { get; private set; } = "";
    public event Action? Updated;
    public async Task RefreshAsync()
    {
        if (_busy || _disposed) return;
        _busy = true;
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var current = _manager.GetCurrentSession();
            var snapshots = new List<MediaSnapshot>();
            var sessions = new Dictionary<string, GlobalSystemMediaTransportControlsSession>();
            foreach (var session in _manager.GetSessions())
            {
                try
                {
                    var properties = await session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                    var info = session.GetPlaybackInfo(); var timeline = session.GetTimelineProperties(); var controls = info.Controls;
                    string appId = session.SourceAppUserModelId;
                    string id = appId + ":" + RuntimeHelpers.GetHashCode(session);
                    if (!_observed.ContainsKey(id)) { _observed[id] = session; session.MediaPropertiesChanged += OnPropertiesChanged; }
                    string name = FriendlyName(appId);
                    bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    var duration = timeline.EndTime - timeline.StartTime;
                    var position = timeline.Position - timeline.StartTime;
                    if (playing && duration > TimeSpan.Zero)
                    {
                        double elapsed = Math.Max(0, (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds);
                        position += TimeSpan.FromSeconds(elapsed * (info.PlaybackRate ?? 1));
                    }
                    position = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Math.Max(0, duration.TotalSeconds)));
                    snapshots.Add(new(id, appId, name, properties.Title, properties.Artist, playing,
                        session == current, controls.IsPlayEnabled, controls.IsPauseEnabled, controls.IsNextEnabled,
                        controls.IsPreviousEnabled, position, duration, await ReadCover(id, properties)));
                    sessions[id] = session;
                }
                catch (Exception) { /* A closing session must not invalidate the other sources. */ }
            }
            if (_disposed) return;
            foreach (string old in _observed.Keys.Except(sessions.Keys).ToArray())
            { _observed[old].MediaPropertiesChanged -= OnPropertiesChanged; _observed.Remove(old); _covers.Remove(old); _dirtyCovers.TryRemove(old, out _); }
            Sessions = snapshots; _sessions = sessions; Error = "";
        }
        catch (Exception) { Sessions = []; _sessions.Clear(); _manager = null; Error = "미디어 연결 불가"; }
        finally { _busy = false; if (!_disposed) Updated?.Invoke(); }
    }
    private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => _dirtyCovers[sender.SourceAppUserModelId + ":" + RuntimeHelpers.GetHashCode(sender)] = 1;
    private async Task<ImageSource?> ReadCover(string id, GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        string track = properties.Title + "\n" + properties.Artist + "\n" + properties.AlbumTitle;
        bool dirty = _dirtyCovers.TryRemove(id, out _);
        if (!dirty && _covers.TryGetValue(id, out var cached) && cached.Track == track && (cached.Cover is not null || DateTimeOffset.UtcNow - cached.Read < TimeSpan.FromSeconds(5))) return cached.Cover;
        ImageSource? cover = null;
        try
        {
            if (properties.Thumbnail is { } reference)
            {
                using var stream = await reference.OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                if (stream.Size > 0 && stream.Size <= 8 * 1024 * 1024)
                {
                    using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
                    await reader.LoadAsync((uint)stream.Size).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                    byte[] bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
                    using var memory = new MemoryStream(bytes);
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 384; bitmap.StreamSource = memory; bitmap.EndInit(); bitmap.Freeze(); cover = bitmap;
                }
            }
        }
        catch (Exception) { /* Artwork failure leaves transport controls available. */ }
        _covers[id] = (track, cover, DateTimeOffset.UtcNow); return cover;
    }
    public async Task ControlAsync(string? id, string operation)
    {
        if (id is null || !_sessions.TryGetValue(id, out var session)) return;
        try
        {
            var info = session.GetPlaybackInfo(); var c = info.Controls;
            bool supported = operation switch { "play" => c.IsPlayEnabled, "pause" => c.IsPauseEnabled, "next" => c.IsNextEnabled, "previous" => c.IsPreviousEnabled, _ => false };
            if (!supported) return;
            bool result = operation switch
            {
                "play" => await session.TryPlayAsync(), "pause" => await session.TryPauseAsync(),
                "next" => await session.TrySkipNextAsync(), "previous" => await session.TrySkipPreviousAsync(), _ => false
            };
            Error = result ? "" : "이 앱에서 제어하지 못했습니다";
            if (!result) Updated?.Invoke();
            else await RefreshAsync();
        }
        catch (Exception) { Error = "미디어 제어 불가"; Updated?.Invoke(); }
    }
    public static string FriendlyName(string id)
    {
        if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (id.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (id.Contains("msedge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        try { return Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(id).DisplayInfo.DisplayName; }
        catch (Exception) { return id; }
    }
    public void Dispose() { _disposed = true; foreach (var s in _observed.Values) s.MediaPropertiesChanged -= OnPropertiesChanged; _observed.Clear(); _covers.Clear(); _dirtyCovers.Clear(); _sessions.Clear(); Sessions = []; _manager = null; }
}
