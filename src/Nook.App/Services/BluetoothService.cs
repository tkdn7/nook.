using System.Threading.Tasks;
using Windows.Devices.Radios;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
namespace Nook.App.Services;

public sealed record BluetoothItem(string Id, string Name, bool Connected, int? Battery);
public sealed class BluetoothService
{
    public bool? Enabled { get; private set; }
    public bool Busy { get; private set; }
    public string Status { get; private set; } = "확인 중";
    public IReadOnlyList<BluetoothItem> Devices { get; private set; } = [];
    private bool _reading;
    private DateTimeOffset _last;
    public async Task RefreshAsync(bool force = false)
    {
        if (_reading || !force && DateTimeOffset.UtcNow - _last < TimeSpan.FromSeconds(10)) return;
        _reading = true;
        try
        {
            var radios = await Radio.GetRadiosAsync(); var radio = radios.FirstOrDefault(x => x.Kind == RadioKind.Bluetooth);
            Enabled = radio is null || radio.State == RadioState.Disabled ? null : radio.State == RadioState.On;
            Status = radio is null ? "Bluetooth 없음" : radio.State == RadioState.Disabled ? "Bluetooth 사용 불가" : Enabled == true ? "ON" : "OFF";
            if (Enabled != true) { Devices = []; return; }
            var result = new List<BluetoothItem>();
            foreach (bool le in new[] { false, true })
            {
                string selector = le ? BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) : BluetoothDevice.GetDeviceSelectorFromPairingState(true);
                var devices = await DeviceInformation.FindAllAsync(selector, ["System.Devices.Aep.IsConnected", "System.Devices.BatteryLife", "System.Devices.ContainerId"]);
                foreach (var info in devices)
                {
                    bool connected = info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is true;
                    int? battery = info.Properties.TryGetValue("System.Devices.BatteryLife", out var b) && b is byte level && level <= 100 ? level : null;
                    string identity = info.Id;
                    if (info.Properties.TryGetValue("System.Devices.ContainerId", out var container) && container is Guid containerId)
                    {
                        identity = containerId.ToString();
                        if (battery is null)
                        {
                            try { var device = await DeviceInformation.CreateFromIdAsync(identity, ["System.Devices.BatteryLife"], DeviceInformationKind.DeviceContainer); if (device is not null && device.Properties.TryGetValue("System.Devices.BatteryLife", out var life) && life is byte percentage && percentage <= 100) battery = percentage; }
                            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or UnauthorizedAccessException) { }
                        }
                    }
                    if (le && connected && battery is null)
                    { try { battery = await BatteryFor(info.Id).WaitAsync(TimeSpan.FromSeconds(4)); } catch (TimeoutException) { } }
                    result.Add(new(identity, info.Name, connected, connected ? battery : null));
                }
            }
            Devices = result.GroupBy(x => x.Id).Select(g => new BluetoothItem(g.Key, g.First().Name, g.Any(x => x.Connected), g.Select(x => x.Battery).FirstOrDefault(x => x.HasValue))).OrderByDescending(x => x.Connected).ThenBy(x => x.Name).ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        { Enabled = null; Status = "Bluetooth 사용 불가"; Devices = []; }
        finally { _last = DateTimeOffset.UtcNow; _reading = false; }
    }
    private static async Task<int?> BatteryFor(string id)
    {
        try
        {
            using var device = await BluetoothLEDevice.FromIdAsync(id);
            if (device is null || device.ConnectionStatus != BluetoothConnectionStatus.Connected) return null;
            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached);
            int? battery = null;
            foreach (var service in services.Services)
            {
                using (service)
                {
                    var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached);
                    foreach (var characteristic in chars.Characteristics)
                    {
                        var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Cached);
                        if (value.Status == GattCommunicationStatus.Success && value.Value.Length > 0)
                        { using var reader = DataReader.FromBuffer(value.Value); byte level = reader.ReadByte(); if (level <= 100) battery = level; }
                    }
                }
            }
            return battery;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException) { return null; }
    }
    public async Task ToggleAsync()
    {
        if (Busy || Enabled is null) return;
        Busy = true;
        try
        {
            bool target = !Enabled.Value;
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) throw new IOException("Bluetooth 제어를 허용하지 않았습니다.");
            var radios = await Radio.GetRadiosAsync(); var radio = radios.FirstOrDefault(x => x.Kind == RadioKind.Bluetooth);
            if (radio is null || await radio.SetStateAsync(target ? RadioState.On : RadioState.Off) != RadioAccessStatus.Allowed) throw new IOException("Bluetooth 상태를 변경할 수 없습니다.");
            await Task.Delay(350); await RefreshAsync(true);
        }
        finally { Busy = false; }
    }
}
