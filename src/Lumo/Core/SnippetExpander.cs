namespace Lumo.Core;

/// <summary>
/// v2.3 (DEV_PLAN Task 3.3) — variable expansion for snippets and macro steps.
///
/// Supported tokens (case-insensitive, whitespace tolerated):
///   {{date}}       → current date, ISO — 2026-08-30
///   {{time}}       → current time, HH:mm
///   {{datetime}}   → both — 2026-08-30 14:05
///   {{clipboard}}  → the current clipboard text (empty when there is none)
///   {{name:Jane}}  → any key:default pair expands to its default — self-documenting
///                    placeholders ("Dear {{name:Jane}}")
///   {{cursor}}     → the caret marker: text BEFORE it is one chunk, text AFTER it
///                    is the other. In paste mode the marker simply disappears
///                    (paste lands the caret after the text anyway); in a future
///                    type mode it will position the caret mid-text.
///
/// Unknown {{tokens}} are left VERBATIM so typos are visible instead of silently
/// vanishing. No nesting, no recursion: the innermost {{…}} wins and its result is
/// never re-scanned, so {{clipboard}} containing "{{date}}" stays literal.
/// </summary>
public static class SnippetExpander
{
    /// <summary>Split view: text before the caret marker, text after it.</summary>
    public readonly record struct Expansion(string Before, string After, bool HasCursor)
    {
        /// <summary>The whole text with the marker dropped (paste mode).</summary>
        public string Whole => HasCursor ? Before + After : Before;
    }

    /// <summary>Paste-mode helper: everything expanded, {{cursor}} dropped.</summary>
    public static string ExpandAll(string template, Func<string?> clipboard, DateTime now) =>
        Expand(template, clipboard, now).Whole;

    /// <summary>Full expansion with the {{cursor}} split point.</summary>
    public static Expansion Expand(string template, Func<string?> clipboard, DateTime now)
    {
        var (before, after, has) = ExpandWithCursor(template, clipboard, now);
        return new Expansion(before, after, has);
    }

    /// <summary>
    /// Expansion that honours {{cursor}}: everything before the FIRST marker is
    /// returned as Before, everything after it as After. Later markers are dropped
    /// (only one caret). Unknown tokens survive in both halves.
    /// </summary>
    public static (string Before, string After, bool HasCursor) ExpandWithCursor(
        string template, Func<string?> clipboard, DateTime now)
    {
        string full = template ?? "";
        try
        {
            int cut = IndexOfToken(full, "cursor");
            if (cut < 0)
                return (Scan(full, clipboard, now), "", false);

            string head = full[..cut];
            int markerEnd = cut + "{{cursor}}".Length;
            string before = Scan(head, clipboard, now);
            string after = Scan(full[markerEnd..], clipboard, now);
            return (before, after, true);
        }
        catch
        {
            return (full, "", false);
        }
    }

    // ------------------------------------------------------------------ core

    /// <summary>Single-pass expansion over everything EXCEPT {{cursor}} markers.</summary>
    private static string Scan(string text, Func<string?> clipboard, DateTime now)
    {
        if (text.Length == 0) return text;

        var sb = new System.Text.StringBuilder(text.Length + 32);
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0) { sb.Append(text, i, text.Length - i); break; }

            sb.Append(text, i, open - i);

            int close = text.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0) { sb.Append(text, open, text.Length - open); break; }   // unterminated → literal

            string token = text.Substring(open + 2, close - open - 2).Trim();
            sb.Append(Resolve(token, clipboard, now, text, open, close));
            i = close + 2;
        }
        return sb.ToString();
    }

    /// <summary>Resolves one token; unknown tokens go back verbatim (visible typo > silent loss).</summary>
    private static string Resolve(string token, Func<string?> clipboard, DateTime now,
                                  string source, int open, int close)
    {
        if (token.Equals("cursor", StringComparison.OrdinalIgnoreCase))
            return source.Substring(open, close - open + 2);   // defensive: markers are split out before Scan runs

        if (token.Equals("date", StringComparison.OrdinalIgnoreCase))
            return now.ToString("yyyy-MM-dd");

        if (token.Equals("time", StringComparison.OrdinalIgnoreCase))
            return now.ToString("HH:mm");

        if (token.Equals("datetime", StringComparison.OrdinalIgnoreCase))
            return now.ToString("yyyy-MM-dd HH:mm");

        if (token.Equals("clipboard", StringComparison.OrdinalIgnoreCase))
        {
            try { return clipboard?.Invoke() ?? ""; }
            catch { return ""; }   // clipboard can be locked — an empty insert beats a crash
        }

        // key:default → default ("name:Jane" → Jane). A default may itself contain
        // ':' — split on the FIRST colon only.
        int colon = token.IndexOf(':');
        if (colon > 0)
            return token[(colon + 1)..].Trim();

        return "{{" + token + "}}";   // unknown → verbatim
    }

    private static int IndexOfToken(string text, string token)
    {
        int i = 0;
        while ((i = text.IndexOf("{{", i, StringComparison.Ordinal)) >= 0)
        {
            int close = text.IndexOf("}}", i + 2, StringComparison.Ordinal);
            if (close < 0) return -1;
            if (text.Substring(i + 2, close - i - 2).Trim().Equals(token, StringComparison.OrdinalIgnoreCase))
                return i;
            i = i + 2;
        }
        return -1;
    }
}
