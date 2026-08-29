using System.IO;
using System.Text.Json;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// Persisted user settings (settings.json in %APPDATA%\Lumo).
///
/// v1.2 — advanced customization: accent colour, animated "glow border" effect
/// (style + speed), start-with-Windows and the file-index size cap.
/// </summary>
public sealed class Settings
{
    public string Hotkey { get; set; } = "Alt+Space";
    public string Theme { get; set; } = "dark";            // "dark" | "light"
    public string WebEngine { get; set; } = "google";      // google | bing | duckduckgo
    public bool HideOnFocusLoss { get; set; } = false;

    // ---- v1.2 customization -------------------------------------------------
    public string AccentColor { get; set; } = "#7C6CFF";   // hex, used by text/caret/highlights
    public bool BorderEffect { get; set; } = true;         // animated glow border around the launcher
    public string BorderStyle { get; set; } = "Aurora";    // Aurora | Sunset | Ocean | Ember | Mint | Solid
    public double BorderSpeedSec { get; set; } = 3.5;      // seconds per full rotation (2 = fast, 6 = slow)
    public bool StartWithWindows { get; set; } = false;    // HKCU Run key (applied on Save)
    public int MaxIndexedFiles { get; set; } = 150_000;    // file-index cap (applied on next rebuild)

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

    /// <summary>JSON round-trip clone — used by the settings window for Cancel/restore.</summary>
    public Settings Clone()
    {
        try { return JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    /// <summary>Copy every value from another instance (used to undo live edits on Cancel).</summary>
    public void RestoreFrom(Settings o)
    {
        Hotkey = o.Hotkey;
        Theme = o.Theme;
        WebEngine = o.WebEngine;
        HideOnFocusLoss = o.HideOnFocusLoss;
        AccentColor = o.AccentColor;
        BorderEffect = o.BorderEffect;
        BorderStyle = o.BorderStyle;
        BorderSpeedSec = o.BorderSpeedSec;
        StartWithWindows = o.StartWithWindows;
        MaxIndexedFiles = o.MaxIndexedFiles;
    }
}
