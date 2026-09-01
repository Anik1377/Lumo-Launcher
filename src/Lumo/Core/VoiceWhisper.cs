namespace Lumo.Core;

/// <summary>
/// v2.6.0-alpha.5 — pure policy for the Whisper transcription engine: the model
/// catalog the user can install, and how a language preference maps onto a
/// whisper language code. Deliberately free of any Whisper.net / HTTP / disk
/// dependency so the catalog and the language table stay unit-testable in the
/// net8.0 harness (the downloader and the inference wrapper live in
/// Services/WhisperEngine; the UI setup card lives in the chat window).
///
/// Models come from the official whisper.cpp ggml releases on Hugging Face
/// (ggerganov/whisper.cpp) — the same weights the whisper.cpp README documents,
/// fetched over https and verified by size before use.
/// </summary>
public static class VoiceWhisper
{
    /// <summary>One installable Whisper model: identity, file name, trusted URL and advertised size.</summary>
    public sealed record WhisperModel(
        string Id, string Name, string FileName, string Url, long Bytes, string Description, bool EnglishOnly)
    {
        /// <summary>Rough download-size caption for the setup card ("148 MB").</summary>
        public string SizeLabel => Bytes <= 0 ? "?" : $"{Bytes / 1_000_000.0:0} MB";
    }

    public const string DefaultModelId = "base.en";
    public const string CatalogHost = "huggingface.co";
    private const string Base = $"https://{CatalogHost}/ggerganov/whisper.cpp/resolve/main/";

    /// <summary>
    /// The catalog, ordered fastest → most accurate. tiny.en / base.en are
    /// English-only checkpoints (better English accuracy per byte); small is the
    /// first multilingual tier, so non-pinned language handling switches to
    /// whisper's own auto-detect when it is selected.
    /// </summary>
    public static readonly IReadOnlyList<WhisperModel> Catalog =
    [
        new("tiny.en", "Tiny", "ggml-tiny.en.bin", Base + "ggml-tiny.en.bin",
            77_700_000, "Fastest · English · lighter machine, quick notes", true),
        new("base.en", "Base", "ggml-base.en.bin", Base + "ggml-base.en.bin",
            147_900_000, "Balanced · English · the recommended default", true),
        new("base", "Base (multi)", "ggml-base.bin", Base + "ggml-base.bin",
            147_900_000, "Balanced · auto-detects 90+ languages", false),
        new("small", "Small", "ggml-small.bin", Base + "ggml-small.bin",
            488_000_000, "Most accurate · auto-detects 90+ languages · bigger download", false),
    ];

    /// <summary>Resolves a settings.json model id to a catalog entry; null-safe, falls back to the default.</summary>
    public static WhisperModel FromId(string? id)
    {
        var hit = Catalog.FirstOrDefault(m => string.Equals(m.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;
        return Catalog.First(m => m.Id == DefaultModelId);
    }

    /// <summary>True when the file name looks like one of ours (guards a hand-edited settings value).</summary>
    public static bool IsKnownFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) &&
        fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) &&
        fileName.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase) &&
        fileName.IndexOfAny(PathInvalidChars) < 0;

    private static readonly char[] PathInvalidChars =
        ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0'];

    /// <summary>
    /// Maps the voice language preference onto a whisper language code.
    /// English-only models only accept "en"; multilingual models with no pin use
    /// whisper's auto-detect ("auto"); a pinned culture ("en-GB", "de-DE"…)
    /// becomes its two-letter ISO code. Unknown shapes fall back to the model's
    /// safe default rather than throwing — a bad pin must never kill the mic.
    /// </summary>
    public static string ResolveLanguage(WhisperModel model, string? voiceLanguagePin)
    {
        if (model.EnglishOnly) return "en";
        string pin = (voiceLanguagePin ?? "").Trim();
        if (pin.Length == 0) return "auto";
        // "en-GB" → "en"; "en" → "en"; anything odd → auto
        int dash = pin.IndexOf('-');
        string primary = (dash > 0 ? pin[..dash] : pin).Trim();
        return primary.Length is 2 or 3 && primary.All(char.IsLetter)
            ? primary.ToLowerInvariant()
            : "auto";
    }
}
