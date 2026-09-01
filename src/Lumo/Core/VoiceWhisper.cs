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

/// <summary>
/// v2.6.0-alpha.7 — the on-disk layout Whisper.net 1.9.1 demands NEXT TO the
/// exe. Its loader never looks at the .NET single-file temp extraction dir:
/// it checks &lt;exe dir&gt;/runtimes/win-x64/ for the native dlls and throws
/// "Native Library not found in default paths" when the folder is absent.
/// alpha.5/6 shipped the dlls embedded inside the single-file exe — invisible
/// to that check, which is exactly why voice died on installed machines
/// ("whisper failed with log…") while dev runs worked (the build output always
/// has the folder). The fix is packaging: the dlls ship as real files in
/// runtimes/win-x64/ beside Lumo.exe, and the zip carries the folder. This
/// helper keeps the required file list and the readable diagnostics pure so a
/// half-extracted zip is caught with an actionable message instead of
/// whisper's cryptic loader exception.
/// </summary>
public static class WhisperNative
{
    /// <summary>Folder (relative to the exe) the Whisper.net loader probes on Windows x64. Display form.</summary>
    public const string RuntimeFolder = "runtimes/win-x64";

    /// <summary>
    /// The exact win-x64 set Whisper.net.Runtime 1.9.1 copies into
    /// runtimes/win-x64/ (its build targets): whisper.dll plus the three ggml
    /// dlls the CPU build of whisper.cpp needs. A Whisper.net upgrade that
    /// adds or renames natives must update this list — the pre-flight in
    /// WhisperEngine refuses to start the factory while one is missing.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredFiles =
    [
        "whisper.dll", "ggml-whisper.dll", "ggml-base-whisper.dll", "ggml-cpu-whisper.dll",
    ];

    /// <summary>The absolute path of the runtime folder under the app's base directory.</summary>
    public static string FolderPath(string baseDir) => Path.Combine(baseDir, "runtimes", "win-x64");

    /// <summary>The absolute path of one native dll under the runtime folder.</summary>
    public static string FilePath(string baseDir, string fileName) =>
        Path.Combine(FolderPath(baseDir), fileName);

    /// <summary>
    /// First required dll missing from the layout, or null when the folder is
    /// complete. <paramref name="exists"/> is injected so the rule stays pure
    /// (WhisperEngine passes File.Exists; tests pass a fake).
    /// </summary>
    public static string? MissingFile(string baseDir, Func<string, bool> exists)
    {
        foreach (var f in RequiredFiles)
            if (!exists(FilePath(baseDir, f)))
                return f;
        return null;
    }

    /// <summary>Short actionable reason for the chat failure line and the log.</summary>
    public static string MissingMessage(string missingFile) =>
        $"the Whisper runtime file {missingFile} is missing next to Lumo.exe ({RuntimeFolder}) — re-extract the full Lumo zip, keeping every file and folder together";
}
