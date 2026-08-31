namespace Lumo.Core;

/// <summary>
/// v2.6.0-alpha.3 — pure voice-dictation policy, deliberately free of any SAPI /
/// System.Speech dependency so the two rules that matter (which recognizer to use,
/// how dictated text joins the prompt) stay unit-testable on the Linux dev box and
/// in the net8.0 test target. The Windows side lives in Services/VoiceInputService.
/// </summary>
public static class VoiceLanguage
{
    /// <summary>
    /// Picks the SAPI recognizer to run dictation on.
    ///
    /// Resolution order:
    ///   1. <paramref name="preferred"/> when it exactly matches an installed
    ///      recognizer id or culture name (case-insensitive) — this is the
    ///      settings.json "VoiceLanguage" override, e.g. "en-GB";
    ///   2. still preferred: the first recognizer whose language part matches
    ///      ("en" in "en-GB" matches "en-US");
    ///   3. the first recognizer whose language part matches <paramref name="uiCulture"/>
    ///      — the follow-the-OS default when VoiceLanguage is "";
    ///   4. the first installed recognizer, whatever it is;
    ///   5. null when the machine has none (the caller disables the mic).
    /// </summary>
    /// <param name="installed">(recognizer id, culture name) pairs from InstalledRecognizers().</param>
    public static string? Pick(string? preferred, IReadOnlyList<(string Id, string Culture)> installed, string? uiCulture = null)
    {
        if (installed.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            string p = preferred.Trim();

            var exact = installed.FirstOrDefault(r =>
                string.Equals(r.Id, p, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Culture, p, StringComparison.OrdinalIgnoreCase));
            if (exact.Id is not null) return exact.Id;

            var pl = LangPart(p);
            if (pl is not null)
            {
                var byLang = installed.FirstOrDefault(r => LangPart(r.Culture) == pl);
                if (byLang.Id is not null) return byLang.Id;
            }
            // an unknown preferred value deliberately falls through to the OS match —
            // a stale "en-GB" on a machine that only has "de-DE" must not kill the mic
        }

        var ul = LangPart(uiCulture);
        if (ul is not null)
        {
            var byUi = installed.FirstOrDefault(r => LangPart(r.Culture) == ul);
            if (byUi.Id is not null) return byUi.Id;
        }

        return installed[0].Id;
    }

    /// <summary>"en-US" → "en"; tolerates bare language codes and null/blank input.</summary>
    private static string? LangPart(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return null;
        var head = culture.Trim();
        int dash = head.IndexOf('-');
        var part = dash > 0 ? head[..dash] : head;
        return part.Length == 0 ? null : part.ToLowerInvariant();
    }
}

/// <summary>
/// v2.6.0-alpha.3 — how dictated text joins the prompt box. Speech arrives in
/// segments (SAPI finalizes on every pause), so the window keeps a "base" string
/// (everything committed so far) and asks this pure helper to lay the next
/// hypothesis or final segment on top of it.
/// </summary>
public static class VoiceText
{
    /// <summary>
    /// Joins <paramref name="spoken"/> onto the committed base text: trimmed at both
    /// ends, one separating space, none when the base already ends with whitespace
    /// (the user typed a space and is waiting for speech to fill it). Empty spoken
    /// input returns the base untouched.
    /// </summary>
    public static string Compose(string? baseText, string? spoken)
    {
        var tail = (spoken ?? "").Trim();
        if (tail.Length == 0) return baseText ?? "";
        var head = baseText ?? "";
        // an all-whitespace base behaves as an empty prompt (no stray leading gap)
        if (head.Length == 0 || head.AsSpan().Trim().Length == 0) return tail;
        return char.IsWhiteSpace(head[^1]) ? head + tail : head + " " + tail;
    }
}
