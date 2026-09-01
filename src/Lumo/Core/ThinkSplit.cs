namespace Lumo.Core;

/// <summary>
/// v2.6.0-alpha.5 — pure splitter for reasoning-model output (prompt-kit's
/// "Reasoning" block). Reasoning chat models — deepseek-r1 and friends — inline
/// their chain of thought inside &lt;think&gt;…&lt;/think&gt; (some hosts emit
/// &lt;thinking&gt;…&lt;/thinking&gt;) followed by the visible answer. The chat
/// window needs those two halves separated every flush tick:
///
///   · no tag at all            → everything is the answer (normal models);
///   · closed tag               → the block is reasoning, the rest the answer;
///   · still-open tag (stream)  → everything after the opener is reasoning and
///                                the answer is still empty — the UI keeps the
///                                reasoning panel live until the closer arrives;
///   · opener split across two flushes ("&lt;thi" + "nk&gt;") — handled by
///     scanning the raw accumulated text each tick, never the delta.
///
/// Splitting is side-effect free and returns trimmed halves so the renderer can
/// drop empty panels instead of showing blank blocks. Tag case is ignored.
/// </summary>
public static class ThinkSplit
{
    private static readonly string[] Openers = ["<think>", "<thinking>"];
    private static readonly string[] Closers = ["</think>", "</thinking>"];

    /// <summary>The two halves of one assistant reply. Either side may be empty (never null).</summary>
    public readonly record struct Parts(string Reasoning, string Answer)
    {
        public bool HasReasoning => !string.IsNullOrEmpty(Reasoning);
    }

    public static Parts Split(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new Parts("", "");

        int opener = IndexOfAny(text, Openers, out string openTag);
        if (opener < 0)
            return new Parts("", text.Trim());

        string before = text[..opener].Trim();
        int body = opener + openTag.Length;

        int closer = IndexOfAny(text, Closers, body, out _);
        if (closer < 0)
        {
            // stream still inside the think block — the answer hasn't started
            return new Parts(Join(before, text[body..]), "");
        }

        string reasoning = Join(before, text[body..closer]);
        string after = text[(closer + CloseLen(text, closer))..].Trim();
        return new Parts(reasoning.Trim(), after);
    }

    /// <summary>True when the text opens (and has not closed) a think block — the panel should stay expanded.</summary>
    public static bool IsThinking(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int opener = IndexOfAny(text, Openers, out string openTag);
        if (opener < 0) return false;
        int body = opener + openTag.Length;
        return IndexOfAny(text, Closers, body, out _) < 0;
    }

    private static string Join(string a, string b)
    {
        a = a.Trim();
        b = b.Trim();
        return a.Length == 0 ? b : b.Length == 0 ? a : a + "\n\n" + b;
    }

    private static int IndexOfAny(string text, string[] tags, out string match) =>
        IndexOfAny(text, tags, 0, out match);

    private static int IndexOfAny(string text, string[] tags, int start, out string match)
    {
        match = "";
        int best = -1;
        foreach (var tag in tags)
        {
            int at = text.IndexOf(tag, start, StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && (best < 0 || at < best || (at == best && tag.Length > match.Length)))
            {
                best = at;
                match = tag;
            }
        }
        return best;
    }

    private static int CloseLen(string text, int closer)
    {
        foreach (var tag in Closers)
            if (text.IndexOf(tag, closer, StringComparison.OrdinalIgnoreCase) == closer)
                return tag.Length;
        return 0;
    }
}
