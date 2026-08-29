using System.Text;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// Minimal crash-proof file logger. Every failure path in the app writes here so that
/// "it freezes / it crashes" reports can be diagnosed from %LOCALAPPDATA%\Lumo\log.txt.
/// Never throws.
/// </summary>
public static class DiagnosticLogger
{
    private static readonly object Gate = new();
    private static long _seq;

    public static void Log(string tag, string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{Interlocked.Increment(ref _seq):D5}] [{tag}] {message}{Environment.NewLine}";
            lock (Gate) File.AppendAllText(AppPaths.LogFile, line, Encoding.UTF8);
        }
        catch { /* logging must never crash the app */ }
    }

    public static void LogException(string context, Exception ex)
    {
        Log(context, $"{ex.GetType().Name}: {ex.Message}\r\n{ex.StackTrace}");
        if (ex.InnerException is not null)
            Log(context + ".Inner", $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
    }
}
