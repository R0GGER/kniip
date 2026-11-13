using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kniip.Infrastructure;

/// <summary>
/// Zorgt dat kniip maar één keer draait. Een tweede start stuurt zijn argumenten
/// via een named pipe door naar de eerste instantie en sluit daarna af.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string BaseName = AppIdentity.Key + "-" + Environment.UserName;
    private static readonly string PipeName = BaseName + "-pipe";

    private readonly CancellationTokenSource _cts = new();
    private Mutex? _mutex;

    public bool TryAcquire()
    {
        _mutex = new Mutex(true, BaseName + "-mutex", out bool createdNew);
        return createdNew;
    }

    public static bool SendToPrimary(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.Write(string.Join("\n", args));
            writer.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void StartServer(Action<string[]> onArguments)
    {
        Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string content = await reader.ReadToEndAsync(_cts.Token);
                    string[] args = content.Length == 0
                        ? Array.Empty<string>()
                        : content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    onArguments(args);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    await Task.Delay(500);
                }
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
        }
    }
}
