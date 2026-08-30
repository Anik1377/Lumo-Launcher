namespace Lumo.Core;

/// <summary>
/// v2.3.0-alpha.3 — tiny markdown renderer for the AI chat tab.
///
/// Local LLMs emit a pragmatic dialect: paragraphs, "- " bullets, "## " headings,
/// **bold**, `inline code` and fenced ``` blocks. This renders exactly that
/// dialect and deliberately nothing more (no links, tables or images — a launcher
/// chat bubble is not a browser). Everything is a PURE line/token decision so the
/// test harness can pin the behaviour; the WPF window maps blocks to visual runs.
///
/// Unknown syntax passes through as plain text — a chat answer must never lose
/// content because the renderer met something it doesn't know.
/// </summary>
public static class MarkdownLite
{
    /// <summary>A fenced code block. Lang is the info string after ``` (may be "").</summary>
    public sealed record CodeBlock(string Lang, string Text);

    /// <summary>"- / * •  " bullets and "1. " ordered items (ordered marker kept in Text prefix).</summary>
    public sealed record Bullet(string Text);

    /// <summary>"#", "##", "###" heading (Level 1–3).</summary>
    public sealed record Heading(int Level, string Text);

    /// <summary>A paragraph (consecutive plain lines joined with spaces).</summary>
    public sealed record Para(string Text);

    /// <summary>One inline run: bold **x** or code `x` — rendered with the matching WPF run style.</summary>
    public sealed record InlineRun(string Text, bool Bold, bool Code);

    /// <summary>
    /// Parses markdown into a block list. Never throws; null/empty → empty list.
    /// Fenced blocks win over every other rule (their content is verbatim).
    /// </summary>
    public static List<object> Parse(string? markdown)
    {
        var blocks = new List<object>();
        try
        {
            if (string.IsNullOrWhiteSpace(markdown)) return blocks;
            var lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            bool inFence = false;
            string fenceLang = "";
            var fenceLines = new List<string>();
            var para = new List<string>();

            void FlushPara()
            {
                if (para.Count == 0) return;
                string text = string.Join(" ", para.Select(l => l.Trim())).Trim();
                if (text.Length > 0) blocks.Add(new Para(text));
                para.Clear();
            }

            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();

                // fenced blocks — ``` or ~~~ opens/closes; a fence with a lang opens
                if (!inFence && (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~")))
                {
                    FlushPara();
                    inFence = true;
                    fenceLang = line.TrimStart()[3..].Trim();
                    fenceLines.Clear();
                    continue;
                }
                if (inFence)
                {
                    if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
                    {
                        blocks.Add(new CodeBlock(fenceLang, string.Join("\n", fenceLines)));
                        inFence = false;
                        fenceLines.Clear();
                    }
                    else
                    {
                        fenceLines.Add(raw);   // verbatim — indentation matters in code
                    }
                    continue;
                }

                string trimmed = line.Trim();

                // blank line ends a paragraph
                if (trimmed.Length == 0) { FlushPara(); continue; }

                // headings # .. ###
                if (trimmed.StartsWith("#"))
                {
                    int level = 0;
                    while (level < trimmed.Length && level < 4 && trimmed[level] == '#') level++;
                    if (level <= 3 && level < trimmed.Length && trimmed[level] == ' ')
                    {
                        FlushPara();
                        blocks.Add(new Heading(level, trimmed[(level + 1)..].Trim()));
                        continue;
                    }
                }

                // bullets: -, *, • and ordered "1. " / "1) "
                if (trimmed.Length >= 2 &&
                    (trimmed[0] is '-' or '*' or '•' && trimmed[1] == ' ' ||
                     char.IsDigit(trimmed[0]) && trimmed.IndexOfAny(['.', ')']) is int p && p > 0 && p <= 3 && p + 1 < trimmed.Length && trimmed[p + 1] == ' '))
                {
                    FlushPara();
                    string text = trimmed[0] is '-' or '*' or '•'
                        ? trimmed[2..].Trim()
                        : trimmed;   // keep "1. do this" verbatim as an ordered bullet
                    blocks.Add(new Bullet(text));
                    continue;
                }

                para.Add(trimmed);
            }

            // unterminated fence: keep what arrived (never lose content)
            if (inFence && fenceLines.Count > 0)
                blocks.Add(new CodeBlock(fenceLang, string.Join("\n", fenceLines)));
            FlushPara();
        }
        catch
        {
            // last-resort: return the raw text as one paragraph so nothing vanishes
            blocks.Clear();
            if (!string.IsNullOrWhiteSpace(markdown))
                blocks.Add(new Para(markdown.Trim()));
        }
        return blocks;
    }

    /// <summary>
    /// Splits one block's text into styled runs: **bold** and `code` spans, with
    /// plain text between them. Bold inside code is literal text; unknown or
    /// unbalanced markers stay visible as typed.
    /// </summary>
    public static List<InlineRun> Inline(string? text)
    {
        var runs = new List<InlineRun>();
        try
        {
            string s = text ?? "";
            int i = 0;
            var plain = new System.Text.StringBuilder();

            void FlushPlain()
            {
                if (plain.Length > 0) { runs.Add(new InlineRun(plain.ToString(), false, false)); plain.Clear(); }
            }

            while (i < s.Length)
            {
                // inline code first — its content is literal, bold inside is not parsed
                if (i + 1 < s.Length && s[i] == '`')
                {
                    int end = s.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        FlushPlain();
                        string code = s[(i + 1)..end];
                        if (code.Length > 0) runs.Add(new InlineRun(code, false, true));
                        i = end + 1;
                        continue;
                    }
                }

                // bold **x** (a lone ** stays literal)
                if (i + 1 < s.Length && s[i] == '*' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int end = s.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i + 1)
                    {
                        FlushPlain();
                        string bold = s[(i + 2)..end];
                        if (bold.Length > 0) runs.Add(new InlineRun(bold, true, false));
                        i = end + 2;
                        continue;
                    }
                }

                plain.Append(s[i]);
                i++;
            }
            FlushPlain();
        }
        catch
        {
            runs.Clear();
            runs.Add(new InlineRun(text ?? "", false, false));
        }
        return runs;
    }
}
