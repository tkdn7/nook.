using System.Net.NetworkInformation;
using System.Threading.Tasks;
namespace Nook.App.Services;

public sealed class NetworkService
{
    private string _id = "";
    private Task<(string Id, string Name, long Rx, long Tx, DateTimeOffset Time)?>? _pending;
    private RateDelta _active = new();
    public NetworkRate Current { get; private set; } = new(null, null, "측정 중");
    public MetricHistory Download { get; } = new();
    public MetricHistory Upload { get; } = new();
    public void Poll(bool enabled)
    {
        if (!enabled) { _active = new(); _id = ""; return; }
        if (_pending is { IsCompleted: true } task)
        {
            var value = task.IsCompletedSuccessfully ? task.Result : null;
            if (value is { } sample)
            {
                if (_id != sample.Id) { _id = sample.Id; _active = new(); }
                Current = _active.Sample(sample.Rx, sample.Tx, sample.Time, sample.Name);
                Download.Add(sample.Time, Current.Download); Upload.Add(sample.Time, Current.Upload);
            }
            else { Current = new(null, null, "연결되지 않음"); _id = ""; _active = new(); }
            _pending = null;
        }
        _pending ??= Task.Run(() =>
        {
            try
            {
                var candidates = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                    .Where(x => x.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any))).ToArray();
                var nic = candidates.FirstOrDefault(x => x.Id == _id) ?? candidates.OrderByDescending(x => x.Speed).FirstOrDefault();
                if (nic is null) return ((string, string, long, long, DateTimeOffset)?)null;
                var statistics = nic.GetIPStatistics(); return (nic.Id, nic.Name, statistics.BytesReceived, statistics.BytesSent, DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
        });
    }
}
