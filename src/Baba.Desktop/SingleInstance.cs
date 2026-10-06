using System.IO.Pipes;

namespace Baba.Desktop;

/// <summary>
/// Makes sure only one Baba window runs. A second launch (for example a double-click on a .baba file) hands its file
/// path to the first one through a named pipe and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string Name = "Baba.Desktop.v1";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Raised on a background thread with the path from a later launch (empty if it had none).</summary>
    public event Action<string>? FileRequested;

    /// <summary>Returns the instance when this is the first launch, or null when Baba is already running.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{Name}", out var isFirst);
        if (isFirst)
            return new SingleInstance(mutex);

        mutex.Dispose();
        return null;
    }

    /// <summary>Tells the running instance to come to the front, and to open <paramref name="path"/> if given.</summary>
    public static void NotifyRunningInstance(string? path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            client.Connect(timeout: 3000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(path ?? "");
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            // The first instance is starting up or closing; nothing more to do.
        }
    }

    public void StartListening() => _ = Task.Run(ListenAsync);

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(_stop.Token) ?? "";
                FileRequested?.Invoke(line.Trim());
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client disconnected early; wait for the next one.
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
