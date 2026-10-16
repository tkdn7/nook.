using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nook.App.Services;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _owns;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancel = new();
    public bool IsPrimary => _owns;
    public SingleInstance(string settingsDirectory)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Environment.UserName + "|" + Path.GetFullPath(settingsDirectory).ToUpperInvariant())))[..20];
        _pipeName = "Nook-" + hash;
        _mutex = new Mutex(true, @"Local\" + _pipeName, out _owns);
    }
    public async Task SignalPrimaryAsync()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(500);
                await pipe.WriteAsync(new byte[] { 1 });
                return;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException) { await Task.Delay(100); }
        }
    }
    public void Listen(Action show) => _ = Task.Run(async () =>
    {
        while (!_cancel.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cancel.Token);
                byte[] buffer = new byte[1];
                if (await pipe.ReadAsync(buffer, _cancel.Token) > 0) _ = Application.Current.Dispatcher.BeginInvoke(show);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { await Task.Delay(100); }
        }
    });
    public void Dispose()
    {
        _cancel.Cancel();
        if (_owns) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
