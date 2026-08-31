namespace Lumo.Core;

/// <summary>
/// v2.4.0-alpha.5 — system-prompt personas for the AI chat tab.
///
/// A persona is a small, hand-tuned system prompt that shapes every reply in a
/// chat (tone, format, focus). The registry is pure data on purpose: the test
/// harness pins ids, glyphs and prompt lengths, and the request builders treat
/// it as an opaque string. Built-ins only for now — custom personas would be a
/// settings-page surface, not a chat-window one.
/// </summary>
public sealed record ChatPersona(string Id, string Name, string Glyph, string Prompt, string Blurb);

public static class ChatPersonas
{
    /// <summary>Conversation order = flyout order. First entry is the default.</summary>
    public static readonly ChatPersona[] All =
    {
        new(
            "assistant",
            "Assistant",
            "\uE99A", // robot
            "You are Lumo, a concise desktop assistant. Answer directly in short paragraphs or bullets. Use markdown (bold, lists, fenced code) where it helps. No preamble, no filler.",
            "Direct, concise answers with light markdown"),
        new(
            "developer",
            "Developer",
            "\uE943", // code
            "You are a senior software engineer. Give precise, correct answers with production-quality code in fenced code blocks tagged with the language. Call out edge cases and gotchas briefly. No filler.",
            "Code-first replies with fenced blocks"),
        new(
            "writer",
            "Writer",
            "\uE70F", // edit pencil
            "You are a sharp writing editor. Improve the user's text for tone, clarity and flow while keeping their voice. Show the improved text first, then a short bullet list of what changed.",
            "Polishes prose — tone, flow, clarity"),
        new(
            "brainstorm",
            "Brainstorm",
            "\uE735", // favorite star
            "You are a creative brainstorming partner. Produce varied, concrete ideas that mix safe and bold options. Give a one-line pitch per idea. No explanations unless asked.",
            "Bold, varied ideas on demand"),
        new(
            "translator",
            "Translator",
            "\uE774", // world
            "You are a precise translator. Detect the language of the user's text and translate it into the target language they indicate (default: English). Preserve tone, formatting and proper names. Output only the translation, nothing else.",
            "Faithful translation between languages"),
        new(
            "tldr",
            "Summarizer",
            "\uE8A5", // document
            "You are a summarizer. Condense the user's text into the fewest possible tight bullet points that keep every key fact and number. No preamble.",
            "Condenses anything into tight bullets"),
    };

    public static ChatPersona Default => All[0];

    /// <summary>v2.4.0-alpha.6 — id prefix of user-defined personas (PersonaStore).</summary>
    public const string CustomPrefix = "custom_";

    /// <summary>True when the id belongs to a user-defined persona.</summary>
    public static bool IsCustom(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Trim().StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves a stored persona id; unknown/empty ids fall back to the default.</summary>
    public static ChatPersona Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return Default;
        foreach (var p in All)
            if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                return p;
        return Default;
    }

    /// <summary>
    /// v2.4.0-alpha.6 — resolves against the user-defined list FIRST, then the
    /// built-in registry, and finally the default. Pure so the test harness can
    /// pin the precedence without touching real personas.json.
    /// </summary>
    public static ChatPersona ResolveWith(string? id, IReadOnlyList<ChatPersona>? custom)
    {
        if (string.IsNullOrWhiteSpace(id)) return Default;
        if (custom is { })
            foreach (var p in custom)
                if (string.Equals(p.Id, id.Trim(), StringComparison.OrdinalIgnoreCase))
                    return p;
        return Resolve(id);
    }
}
