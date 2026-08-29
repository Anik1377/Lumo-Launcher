using System.IO.Pipes;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// Single-instance activation.
///
/// FIX (v1.1): clicking the desktop shortcut while Lumo was already running did nothing
/// visible. Now: the first instance owns a named mutex AND runs a tiny named-pipe server;
/// any second launch sends "SHOW" over the pipe, and the running instance opens and
/// focuses its window.
/// </summary>
public static class SingleInstance
{
    private const string MutexName = "Lumo.SingleInstance.Mutex.v1";
    private const string PipeName = "Lumo.SingleInstance.Pipe.v1";

    public static Mutex? TryAcquireFirst()
    {
        Mutex? mutex = new(true, MutexName, out bool createdNew);
        if (createdNew) return mutex;

        mutex.Dispose();
        return null;
    }

    public static void SignalExistingToShow()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("SHOW");
            DiagnosticLogger.Log("SingleInstance", "Signalled existing instance to show");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("SingleInstance.Signal", ex);
        }
    }

    /// <summary>Runs a background pipe server; every "SHOW" line triggers <paramref name="onShow"/> on the UI thread.</summary>
    public static void StartShowServer(Action onShow)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await server.WaitForConnectionAsync().ConfigureAwait(false);
                    using var reader = new StreamReader(server);
                    string? line = await reader.ReadLineAsync().ConfigureAwait(false);

                    if (line is not null && line.Trim().Equals("SHOW", StringComparison.OrdinalIgnoreCase))
                    {
                        DiagnosticLogger.Log("SingleInstance", "Received SHOW from second instance");
                        onShow();
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogException("SingleInstance.Server", ex);
                    try { await Task.Delay(300).ConfigureAwait(false); } catch { }
                }
            }
        });
    }
}
