using System.IO;
using System.Text.Json;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// Persisted user settings (settings.json in %APPDATA%\Lumo).
///
/// v1.2 — advanced customization: accent colour, animated "glow border" effect
/// (style + speed), start-with-Windows and the file-index size cap.
/// v1.3 — Apple-style refinement: auto (system) theme and a global
/// "reduce motion" switch that disables every animation for snappiness.
/// </summary>
public sealed class Settings
{
    public string Hotkey { get; set; } = "Alt+Space";
    public string Theme { get; set; } = "dark";            // "dark" | "light" | "auto"
    public string WebEngine { get; set; } = "google";      // google | bing | duckduckgo
    public bool HideOnFocusLoss { get; set; } = false;

    // ---- v1.2 customization -------------------------------------------------
    public string AccentColor { get; set; } = "#7C6CFF";   // hex, used by text/caret/highlights
    public bool BorderEffect { get; set; } = true;         // animated glow border around the launcher
    public string BorderStyle { get; set; } = "Aurora";    // Aurora | Sunset | Ocean | Ember | Mint | Solid
    public double BorderSpeedSec { get; set; } = 9.0;      // seconds per perimeter lap (6 = fast, 14 = slow)
    public bool StartWithWindows { get; set; } = false;    // HKCU Run key (applied on Save)
    public int MaxIndexedFiles { get; set; } = 150_000;    // file-index cap (applied on next rebuild)

    // ---- v1.3 customization -------------------------------------------------
    public bool AnimationsEnabled { get; set; } = true;    // master switch for every animation
    public bool GlassEffect { get; set; } = true;          // v1.7 acrylic glass backdrop (graceful fallback)

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                // Tolerant per-property read: one bad value (e.g. a hand-edited
                // "Hotkey": { … } object) falls back to its default instead of
                // throwing away every saved preference.
                using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    s.Hotkey            = GetStr(root, nameof(Hotkey), s.Hotkey);
                    s.Theme             = GetStr(root, nameof(Theme), s.Theme);
                    s.WebEngine         = GetStr(root, nameof(WebEngine), s.WebEngine);
                    s.HideOnFocusLoss   = GetBool(root, nameof(HideOnFocusLoss), s.HideOnFocusLoss);
                    s.AccentColor       = GetStr(root, nameof(AccentColor), s.AccentColor);
                    s.BorderEffect      = GetBool(root, nameof(BorderEffect), s.BorderEffect);
                    s.BorderStyle       = GetStr(root, nameof(BorderStyle), s.BorderStyle);
                    s.BorderSpeedSec    = GetNum(root, nameof(BorderSpeedSec), s.BorderSpeedSec);
                    s.StartWithWindows  = GetBool(root, nameof(StartWithWindows), s.StartWithWindows);
                    s.MaxIndexedFiles   = (int)Math.Clamp(GetNum(root, nameof(MaxIndexedFiles), s.MaxIndexedFiles), 10_000, 500_000);
                    s.AnimationsEnabled = GetBool(root, nameof(AnimationsEnabled), s.AnimationsEnabled);
                    s.GlassEffect       = GetBool(root, nameof(GlassEffect), s.GlassEffect);
                }
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.Load", ex); }
        return s;
    }

    private static string GetStr(JsonElement root, string name, string fallback)
    {
        try
        {
            if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? fallback;
        }
        catch { /* defensive */ }
        DiagnosticLogger.Log("Settings.Load", $"Property '{name}' has an unexpected JSON type — using default");
        return fallback;
    }

    private static bool GetBool(JsonElement root, string name, bool fallback)
    {
        try
        {
            if (root.TryGetProperty(name, out var v))
            {
                if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
                if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n != 0;  // tolerate hand-edited 1/0
            }
        }
        catch { /* defensive */ }
        DiagnosticLogger.Log("Settings.Load", $"Property '{name}' has an unexpected JSON type — using default");
        return fallback;
    }

    private static double GetNum(JsonElement root, string name, double fallback)
    {
        try
        {
            if (root.TryGetProperty(name, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
                if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var d)) return d;
            }
        }
        catch { /* defensive */ }
        DiagnosticLogger.Log("Settings.Load", $"Property '{name}' has an unexpected JSON type — using default");
        return fallback;
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
        AnimationsEnabled = o.AnimationsEnabled;
    }

    /// <summary>
    /// Resolves the effective dark/light decision: "auto" follows the Windows
    /// personalization setting, otherwise the explicit choice wins.
    /// </summary>
    public bool EffectiveDark() =>
        Theme?.Equals("light", StringComparison.OrdinalIgnoreCase) == false
            ? Theme?.Equals("auto", StringComparison.OrdinalIgnoreCase) == true
                ? Appearance.IsSystemDark()
                : true
            : false;
}
