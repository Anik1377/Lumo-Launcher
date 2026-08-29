using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumo.Services;

/// <summary>
/// v1.5 — one action inside a macro, Apple-Shortcuts style.
/// Types: auto (open anything), app, url, file, folder, wait, clip.
/// Steps are persisted as strings inside ShortcutDef.Steps:
///   • legacy plain string  → auto (open URL or path, decided at run time)
///   • v1.5 typed step      → compact JSON  {"t":"wait","a":"1500"}
/// so old shortcuts.json files keep working untouched.
/// </summary>
public sealed class MacroStep
{
    public string Type { get; set; } = "auto";
    public string Arg { get; set; } = "";

    public const int MaxSteps = 30;

    public MacroStep() { }
    public MacroStep(string type, string arg) { Type = type; Arg = arg; }

    // ---------------------------------------------------------------- display

    public string Describe() => Type switch
    {
        "app"    => "Open app — " + Arg,
        "url"    => "Open URL — " + Arg,
        "file"   => "Open file — " + Arg,
        "folder" => "Open folder — " + Arg,
        "wait"   => $"Wait — {WaitMs:N0} ms",
        "clip"   => "Copy text to clipboard",
        _        => "Open — " + Arg,                       // auto
    };

    public string Glyph => Type switch
    {
        "app"    => "\uE768",   // rocket-ish grid/apps
        "url"    => "\uE774",   // globe
        "file"   => "\uE8A5",   // document
        "folder" => "\uE8B7",   // folder
        "wait"   => "\uE916",   // clock
        "clip"   => "\uE8C8",   // copy
        _        => "\uE945",   // lightning (auto)
    };

    /// <summary>Short pill label shown in the visual builder cards.</summary>
    public string TypeLabel => Type switch
    {
        "app"    => "Open App",
        "url"    => "Open URL",
        "file"   => "Open File",
        "folder" => "Open Folder",
        "wait"   => "Wait",
        "clip"   => "Clipboard",
        _        => "Open",
    };

    public int WaitMs => Type == "wait" && int.TryParse(Arg, out int ms) ? Math.Clamp(ms, 100, 60_000) : 1000;

    // ---------------------------------------------------------------- validation

    /// <summary>Returns a human error message, or null when the step is runnable.</summary>
    public string? Validate(int index)
    {
        string where = $"Step {index + 1}: ";
        switch (Type)
        {
            case "auto":
                if (string.IsNullOrWhiteSpace(Arg)) return where + "nothing to open";
                return null;
            case "app":
            case "file":
                if (string.IsNullOrWhiteSpace(Arg)) return where + "no path given";
                if (!File.Exists(Environment.ExpandEnvironmentVariables(Arg)))
                    return where + $"file not found — {Arg}";
                return null;
            case "folder":
                if (string.IsNullOrWhiteSpace(Arg)) return where + "no path given";
                if (!Directory.Exists(Environment.ExpandEnvironmentVariables(Arg)))
                    return where + $"folder not found — {Arg}";
                return null;
            case "url":
                if (string.IsNullOrWhiteSpace(Arg)) return where + "no URL given";
                return null;
            case "wait":
                if (!int.TryParse(Arg, out int ms) || ms < 100 || ms > 60_000)
                    return where + "wait needs 100–60000 ms";
                return null;
            case "clip":
                if (string.IsNullOrWhiteSpace(Arg)) return where + "no text to copy";
                return null;
            default:
                return where + $"unknown action “{Type}”";
        }
    }

    // ---------------------------------------------------------------- (de)serialisation

    private sealed record Enc([property: JsonPropertyName("t")] string T, [property: JsonPropertyName("a")] string A);

    public static MacroStep Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new MacroStep("auto", "");
        string s = raw.Trim();
        if (s.StartsWith('{'))
        {
            try
            {
                var e = JsonSerializer.Deserialize<Enc>(s);
                if (e is not null) return new MacroStep(e.T ?? "auto", e.A ?? "");
            }
            catch { /* fall through → legacy */ }
        }
        return new MacroStep("auto", s);   // legacy plain target
    }

    public string Encode() =>
        Type == "auto"
            ? Arg                                                     // keep legacy form readable
            : JsonSerializer.Serialize(new Enc(Type, Arg));
}

/// <summary>Runnable view of a macro: parsed, expanded, bounded.</summary>
public static class MacroProgram
{
    public static List<MacroStep> FromDef(ShortcutDef def) =>
        (def.Steps ?? new List<string>()).Select(MacroStep.Parse).Take(MacroStep.MaxSteps).ToList();

    /// <summary>Validates every step. Returns the first error found, or null.</summary>
    public static string? Validate(IReadOnlyList<MacroStep> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var err = steps[i].Validate(i);
            if (err is not null) return err;
        }
        return steps.Count == 0 ? "This macro has no steps yet" : null;
    }
}
