using Microsoft.Win32;

namespace Lumo.Services;

/// <summary>
/// Start-with-Windows support via the per-user HKCU Run key (no admin rights needed).
/// The value points at the running executable, so it works for the portable exe
/// wherever the user keeps it.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lumo";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string v &&
                   string.Equals(Normalize(v), Normalize(ExePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Startup.IsEnabled", ex); return false; }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return false;

            if (enabled)
                key.SetValue(ValueName, $"\"{ExePath}\"");
            else if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);

            DiagnosticLogger.Log("Startup", $"Start-with-Windows {(enabled ? "enabled" : "disabled")} ({ExePath})");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Startup.SetEnabled", ex);
            return false;
        }
    }

    private static string ExePath =>
        Environment.ProcessPath is { Length: > 0 } p ? p : System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "Lumo.exe";

    private static string Normalize(string p) => p.Trim().Trim('"');
}
