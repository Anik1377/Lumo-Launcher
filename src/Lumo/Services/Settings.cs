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
    public string AccentColor { get; set; } = "#FF6363";   // hex — v2.4 Raycast red (was violet)
    public bool BorderEffect { get; set; } = true;         // animated glow border around the launcher
    public string BorderStyle { get; set; } = "Aurora";    // Aurora | Sunset | Ocean | Ember | Mint | Solid
    public double BorderSpeedSec { get; set; } = 9.0;      // seconds per perimeter lap (6 = fast, 14 = slow)
    public bool StartWithWindows { get; set; } = false;    // HKCU Run key (applied on Save)
    public int MaxIndexedFiles { get; set; } = 150_000;    // file-index cap (applied on next rebuild)

    // ---- v1.3 customization -------------------------------------------------
    public bool AnimationsEnabled { get; set; } = true;    // master switch for every animation
    public bool GlassEffect { get; set; } = true;          // v1.7 legacy key (unused since v2.0, kept for JSON back-compat)

    // ---- v2.0.1 advanced customization --------------------------------------
    public double GlowOpacity { get; set; } = 0.9;         // rim comet brightness (0.40–1.00)
    public double RimThickness { get; set; } = 3.0;        // glowing rim band width in px (2–6)
    public double WindowWidth { get; set; } = 744.0;       // launcher width in DIP (560–900) — Raycast proportion
    public string CornerStyle { get; set; } = "rounded";   // rounded (Win11 8 px) | square
    public string RowDensity { get; set; } = "comfortable"; // comfortable | compact

    // ---- v2.4.0-alpha.2 — frosted-glass launcher (Raycast material)
    // Real DWM acrylic blur-behind under a translucent panel brush. Falls back to
    // the solid palette on unsupported builds / remote sessions / failed calls.
    public bool Acrylic { get; set; } = true;

    // ---- v3.0 — the theme system ---------------------------------------------
    // ThemePreset is a Core/ThemeSelect catalog id ("lumo-dark", "dusk", "nord", …);
    // "" keeps the legacy v2 pair (Theme + AccentColor) so upgrades render exactly
    // as before. CustomThemeFile is a file NAME inside AppPaths.ThemesDir (imported
    // themes); when it resolves and parses, it outranks the preset entirely.
    public string ThemePreset { get; set; } = "";
    public string CustomThemeFile { get; set; } = "";

    // ---- v3.0 — App Deck -----------------------------------------------------
    // When true, numpad 1–9 are registered as GLOBAL hotkeys (no modifier) so the
    // deck launches from anywhere — including inside games. Off by default: bare
    // numpad keys belong to games first, and this is an opt-in trade.
    public bool DeckGlobalHotkeys { get; set; } = false;

    // ---- v2.1 (DEV_PLAN Task 1.3) — user-defined web providers: keyword → URL template
    public Dictionary<string, string> CustomWebProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ---- v2.6 (DEV_PLAN Phase 5) — product round
    public bool FirstRunDone { get; set; } = false;      // onboarding tour shown once (Task 5.3)
    public bool UpdatesEnabled { get; set; } = true;     // auto-check GitHub Releases every 24 h (Task 5.1)
    public string LastUpdateCheckUtc { get; set; } = ""; // ISO-8601 stamp of the last completed check

    // ---- v2.5 (DEV_PLAN Task 4.2) — JSON plugins: ids the user switched off in Settings → Plugins
    public List<string> DisabledPlugins { get; set; } = new();

    // ---- v2.3 (DEV_PLAN Task 3.1) — ? AI answers. Ollama (local) or Anthropic.
    // AiApiKey NEVER appears in a log line (AiProviders.Redact is mandatory there);
    // it is stored plaintext in settings.json on this PC only, like every launcher.
    public bool AiEnabled { get; set; } = false;                       // off until the user opts in
    public string AiStyle { get; set; } = "ollama";                    // ollama | anthropic
    public string AiEndpoint { get; set; } = "http://localhost:11434"; // ollama default
    public string AiModel { get; set; } = "llama3.2";                  // provider model id
    public string AiApiKey { get; set; } = "";                         // anthropic x-api-key / optional gateway bearer
    public string AiPersona { get; set; } = "assistant";               // v2.4.0-alpha.5 — persona id for NEW chats

    // ---- v2.6.0-alpha.3 — voice typing in the AI chat (offline dictation).
    // The recognizer is chosen at session start: VoiceLanguage non-empty pins a
    // culture ("en-GB"); empty follows the OS UI language.
    // v2.6.0-alpha.5 — VoiceEngine picks the transcription engine: "whisper"
    // (default — whisper.cpp, offline, downloaded on demand) or "windows" (the
    // SAPI fallback). VoiceModel is a Core/VoiceWhisper catalog id ("base.en").
    public bool VoiceEnabled { get; set; } = true;                     // mic button in the chat window
    public string VoiceLanguage { get; set; } = "";                    // "" = follow the OS UI language
    public string VoiceEngine { get; set; } = "whisper";               // "whisper" | "windows"
    public string VoiceModel { get; set; } = Core.VoiceWhisper.DefaultModelId;

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
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    ApplyJson(s, doc.RootElement);
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Settings.Load", ex); }
        return s;
    }

    /// <summary>
    /// Phase 0 (DEV_PLAN) — the tolerant per-property reader, extracted so the test
    /// harness can exercise it without touching the real settings.json on disk.
    /// One bad value falls back to that property's default; nothing throws.
    /// </summary>
    internal static void ApplyJson(Settings s, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
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
        s.GlowOpacity       = GetNum(root, nameof(GlowOpacity), s.GlowOpacity);
        s.RimThickness      = GetNum(root, nameof(RimThickness), s.RimThickness);
        s.WindowWidth       = GetNum(root, nameof(WindowWidth), s.WindowWidth);
        s.CornerStyle       = GetStr(root, nameof(CornerStyle), s.CornerStyle);
        s.RowDensity        = GetStr(root, nameof(RowDensity), s.RowDensity);
        s.Acrylic           = GetBool(root, nameof(Acrylic), s.Acrylic);
        s.ThemePreset       = GetStr(root, nameof(ThemePreset), s.ThemePreset);             // v3.0
        s.CustomThemeFile   = GetStr(root, nameof(CustomThemeFile), s.CustomThemeFile);     // v3.0
        s.DeckGlobalHotkeys = GetBool(root, nameof(DeckGlobalHotkeys), s.DeckGlobalHotkeys); // v3.0
        s.CustomWebProviders = GetStrMap(root, nameof(CustomWebProviders), s.CustomWebProviders);
        s.DisabledPlugins  = GetStrList(root, nameof(DisabledPlugins), s.DisabledPlugins);   // v2.5 — Task 4.2
        s.AiEnabled         = GetBool(root, nameof(AiEnabled), s.AiEnabled);
        s.AiStyle           = GetStr(root, nameof(AiStyle), s.AiStyle);
        s.AiEndpoint        = GetStr(root, nameof(AiEndpoint), s.AiEndpoint);
        s.AiModel           = GetStr(root, nameof(AiModel), s.AiModel);
        s.AiApiKey          = GetStr(root, nameof(AiApiKey), s.AiApiKey);
        s.AiPersona         = GetStr(root, nameof(AiPersona), s.AiPersona);
        s.VoiceEnabled      = GetBool(root, nameof(VoiceEnabled), s.VoiceEnabled);          // v2.6.0-alpha.3
        s.VoiceLanguage     = GetStr(root, nameof(VoiceLanguage), s.VoiceLanguage);
        s.VoiceEngine       = GetStr(root, nameof(VoiceEngine), s.VoiceEngine);             // v2.6.0-alpha.5
        s.VoiceModel        = GetStr(root, nameof(VoiceModel), s.VoiceModel);
        s.FirstRunDone      = GetBool(root, nameof(FirstRunDone), s.FirstRunDone);          // v2.6 — Task 5.3
        s.UpdatesEnabled    = GetBool(root, nameof(UpdatesEnabled), s.UpdatesEnabled);      // v2.6 — Task 5.1
        s.LastUpdateCheckUtc = GetStr(root, nameof(LastUpdateCheckUtc), s.LastUpdateCheckUtc);

        // v2.4 design-system migration — pre-2.4 installs carry an accent that was only
        // ever the old default (violet #7C6CFF or Win11 blue #0078D4). The Raycast-grade
        // system ships a new signature accent, so silently carry those installs over;
        // a colour the user actually picked (anything else) is respected untouched.
        if (string.Equals(s.AccentColor, "#7C6CFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.AccentColor, "#0078D4", StringComparison.OrdinalIgnoreCase))
            s.AccentColor = "#FF6363";
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

    /// <summary>Tolerant string list read (v2.5 disabled plugin ids).</summary>
    private static List<string> GetStrList(JsonElement root, string name, List<string> fallback)
    {
        try
        {
            if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var e in v.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && e.GetString() is { } id && id.Length > 0)
                        list.Add(id);
                return list;
            }
        }
        catch { /* defensive */ }
        return fallback;
    }

    /// <summary>Tolerant string→string map read (v2.1 custom web providers).</summary>
    private static Dictionary<string, string> GetStrMap(JsonElement root, string name, Dictionary<string, string> fallback)
    {
        try
        {
            if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in v.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { } url)
                        map[p.Name] = url;
                return map;
            }
        }
        catch { /* defensive */ }
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
        GlowOpacity = o.GlowOpacity;
        RimThickness = o.RimThickness;
        WindowWidth = o.WindowWidth;
        CornerStyle = o.CornerStyle;
        RowDensity = o.RowDensity;
        Acrylic = o.Acrylic;
        ThemePreset = o.ThemePreset;           // v3.0
        CustomThemeFile = o.CustomThemeFile;   // v3.0
        DeckGlobalHotkeys = o.DeckGlobalHotkeys; // v3.0
        CustomWebProviders = new Dictionary<string, string>(o.CustomWebProviders, StringComparer.OrdinalIgnoreCase);
        DisabledPlugins = new List<string>(o.DisabledPlugins);   // v2.5 — Task 4.2
        AiEnabled = o.AiEnabled;
        AiStyle = o.AiStyle;
        AiEndpoint = o.AiEndpoint;
        AiModel = o.AiModel;
        AiApiKey = o.AiApiKey;
        AiPersona = o.AiPersona;
        VoiceEnabled = o.VoiceEnabled;          // v2.6.0-alpha.3
        VoiceLanguage = o.VoiceLanguage;
        VoiceEngine = o.VoiceEngine;            // v2.6.0-alpha.5
        VoiceModel = o.VoiceModel;
        FirstRunDone = o.FirstRunDone;          // v2.6 — Task 5.3
        UpdatesEnabled = o.UpdatesEnabled;      // v2.6 — Task 5.1
        LastUpdateCheckUtc = o.LastUpdateCheckUtc;
    }

    /// <summary>
    /// Resolves the effective dark/light decision: "auto" follows the Windows
    /// personalization setting, otherwise the explicit choice wins.
    /// </summary>
    public bool EffectiveDark() =>
        Theme?.Equals("light", StringComparison.OrdinalIgnoreCase) == false
            ? Theme?.Equals("auto", StringComparison.OrdinalIgnoreCase) == true
                ? SystemTheme.IsDark()
                : true
            : false;
}
