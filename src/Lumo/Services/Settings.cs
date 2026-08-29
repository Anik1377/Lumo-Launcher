using System.IO;
using System.Text.Json;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>Persisted user settings (settings.json in %APPDATA%\Lumo).</summary>
public sealed class Settings
{
    public string Hotkey { get; set; } = "Alt+Space";
    public string Theme { get; set; } = "dark";            // "dark" | "light"
    public string WebEngine { get; set; } = "google";      // google | bing | duckduckgo
    public bool HideOnFocusLoss { get; set; } = false;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile));
                if (s is not null) return s;
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.Load", ex); }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsDir);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.Save", ex); }
    }
}
