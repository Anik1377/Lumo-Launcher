using System.Diagnostics;

namespace Lumo.Core;

/// <summary>
/// v3.0 — the App Deck: nine quick-launch slots bound to numpad 1–9.
/// Pure model + policy (no WPF, no store I/O) so the test harness can pin the
/// normalization and validation rules.
/// </summary>
public static class DeckSlots
{
    public const int Count = 9;          // numpad 1–9
    public const int MaxNameChars = 40;
    public const int MaxTargetChars = 400;
    public const int MaxArgsChars = 300;

    /// <summary>One slot. Index 0..8 ↔ numpad 1..9 (the UI shows +1). Target empty = unassigned.</summary>
    public sealed record Slot(
        int Index, string Name, string Target, string Args, string WorkDir)
    {
        public bool IsAssigned => !string.IsNullOrWhiteSpace(Target);
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName(Index) : Name;
    }

    /// <summary>"Slot 1" … the label an empty card shows.</summary>
    public static string DefaultName(int index) => $"Slot {index + 1}";

    public static Slot Empty(int index) => new(
        Math.Clamp(index, 0, Count - 1), "", "", "", "");

    /// <summary>
    /// Normalizes a user edit into a slot. Empty target clears the slot entirely;
    /// whitespace collapses; length caps mirror the persona editor. Returns null
    /// only for a null/empty target combined with an empty name (nothing to save).
    /// </summary>
    public static Slot? Normalize(int index, string? name, string? target, string? args, string? workDir)
    {
        if (index < 0 || index >= Count) return null;

        target = (target ?? "").Trim();
        name = Collapse(name ?? "");
        args = (args ?? "").Trim();
        workDir = (workDir ?? "").Trim();

        if (target.Length == 0)
        {
            // clearing the target clears the slot — an empty name means "no edit"
            if (name.Length == 0 && args.Length == 0 && workDir.Length == 0) return null;
            return Empty(index);
        }

        return new Slot(
            index,
            name.Length > MaxNameChars ? name[..MaxNameChars] : name,
            target.Length > MaxTargetChars ? target[..MaxTargetChars] : target,
            args.Length > MaxArgsChars ? args[..MaxArgsChars] : args,
            workDir.Length > MaxTargetChars ? workDir[..MaxTargetChars] : workDir);
    }

    /// <summary>Collapse internal whitespace runs to single spaces, trim ends.</summary>
    public static string Collapse(string s) => string.Join(' ',
        (s ?? "").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// The launch policy: ShellExecute so .lnk/.url/exe/folders/documents all work
    /// like an explorer double-click. Returns a readable error instead of throwing;
    /// the caller decides where to surface it. (Pure decision-making — the actual
    /// Process.Start lives with the store/service side.)
    /// </summary>
    public static string? ValidateForLaunch(Slot slot, Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        if (!slot.IsAssigned) return "This slot is empty — right-click it to assign an app.";
        var target = slot.Target;
        bool ok = fileExists(target) || directoryExists(target);
        // Environment variables in the target (%WINDIR%\...) resolve at launch time;
        // the validator only probes literal paths.
        if (!ok && target.Contains('%')) ok = true;
        if (!ok) return $"Can't find {target} — it may have moved. Reassign this slot.";
        if (slot.WorkDir.Length > 0 && !directoryExists(slot.WorkDir) && !fileExists(slot.WorkDir))
            return $"Start-in folder {slot.WorkDir} doesn't exist — fix it in the slot editor.";
        return null;
    }

    /// <summary>Builds the launch start info (null when the slot is empty).</summary>
    public static ProcessStartInfo? BuildStartInfo(Slot slot)
    {
        if (!slot.IsAssigned) return null;
        return new ProcessStartInfo
        {
            FileName = slot.Target,
            Arguments = slot.Args,
            WorkingDirectory = slot.WorkDir.Length > 0 ? slot.WorkDir : "",
            UseShellExecute = true,
        };
    }
}
