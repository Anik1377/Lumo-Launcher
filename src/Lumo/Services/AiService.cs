using System.Net.Http;
using System.Text;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// v2.3 (DEV_PLAN Task 3.1) — the runtime half of ? AI answers.
///
/// Design constraints (DEV_PLAN agent rules):
///  · NEVER on the search thread — AskAsync is awaited from Task.Run in the window;
///    the synchronous Search pipeline only ever reads the in-memory cache.
///  · Bounded — at most 8 cached answers and ONE in-flight request per prompt
///    (in-flight dedupe means "type ?question, edit, retype" cannot stack requests).
///  · Log, don't crash — every failure path is caught and returned as AiReply.Error;
///    log lines are run through AiProviders.Redact so the key can never leak.
/// </summary>
public sealed class AiService
{
    private const int MaxCacheEntries = 8;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(45),   // slow local models still land; hung endpoints can't stall the UI
    };

    private readonly object _gate = new();
    private readonly List<KeyValuePair<string, string>> _cache = new();      // oldest first, cap 8
    private readonly Dictionary<string, Task<AiReply>> _inflight = new(StringComparer.Ordinal);

    /// <summary>Cached answer for this exact prompt, if one already exists.</summary>
    public bool TryGetCached(string prompt, out string answer)
    {
        answer = "";
        try
        {
            lock (_gate)
            {
                string key = (prompt ?? "").Trim();
                var hit = _cache.FirstOrDefault(kv => kv.Key == key);
                if (hit.Key is null) return false;
                answer = hit.Value;
                return true;
            }
        }
        catch { return false; }
    }

    public bool HasCached(string prompt) => TryGetCached(prompt, out _);

    /// <summary>
    /// Asks the configured provider. Concurrent calls for the same prompt share one
    /// request; the answer is cached for the launcher's synchronous result rows.
    /// </summary>
    public Task<AiReply> AskAsync(Settings settings, string prompt)
    {
        try
        {
            string key = (prompt ?? "").Trim();
            if (key.Length == 0)
                return Task.FromResult(AiReply.Bad("empty prompt"));

            lock (_gate)
            {
                if (_inflight.TryGetValue(key, out var running))
                    return running;

                var task = AskCoreAsync(settings, key);
                _inflight[key] = task;
                _ = task.ContinueWith(_ =>
                {
                    try { lock (_gate) _inflight.Remove(key); } catch { }
                }, TaskScheduler.Default);
                return task;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Ai.Ask", ex);
            return Task.FromResult(AiReply.Bad(AiProviders.Redact(settings?.AiApiKey, ex.Message)));
        }
    }

    private async Task<AiReply> AskCoreAsync(Settings settings, string prompt)
    {
        string style = settings.AiStyle;
        try
        {
            if (!settings.AiEnabled)
                return AiReply.Bad("AI is off — enable it in Settings");

            var (ok, spec, err) = AiProviders.Build(style, settings.AiEndpoint, settings.AiModel, settings.AiApiKey, prompt);
            if (!ok || spec is null)
                return AiReply.Bad(err);

            using var req = new HttpRequestMessage(HttpMethod.Post, spec.Url);
            foreach (var (name, value) in spec.Headers)
                req.Headers.TryAddWithoutValidation(name, value);
            req.Content = new StringContent(spec.Json, Encoding.UTF8, "application/json");

            DiagnosticLogger.Log("Ai", $"asking {style} · {settings.AiModel} · {HostOf(spec.Url)} · {prompt.Length} chars");

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string reason = $"HTTP {(int)resp.StatusCode} from {HostOf(spec.Url)}";
                DiagnosticLogger.Log("Ai", AiProviders.Redact(settings.AiApiKey, reason + " · " + Trunc(body, 200)));
                return AiReply.Bad(reason);
            }

            string? text = AiProviders.Extract(style, body);
            if (string.IsNullOrWhiteSpace(text))
            {
                DiagnosticLogger.Log("Ai", "response had no answer text (shape change or error payload)");
                return AiReply.Bad("the reply had no answer — check the model name in Settings");
            }

            Remember(prompt, text);
            return AiReply.Good(text);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Ai.AskCore", ex);
            return AiReply.Bad(AiProviders.Redact(settings?.AiApiKey, ex.Message));
        }
    }

    private void Remember(string prompt, string answer)
    {
        try
        {
            lock (_gate)
            {
                _cache.RemoveAll(kv => kv.Key == prompt);
                _cache.Add(new(prompt, answer));
                while (_cache.Count > MaxCacheEntries)
                    _cache.RemoveAt(0);   // oldest answer falls off — the cache is a scratchpad, not history
            }
        }
        catch { /* cache is best-effort */ }
    }

    // ---------------------------------------------------------------- v2.3.0-alpha.3 — AI chat tab

    /// <summary>Outcome of one streamed chat turn.</summary>
    public sealed record AiStreamResult(bool Ok, string Text, string Error, bool Cancelled)
    {
        public static AiStreamResult Good(string text) => new(true, text, "", false);
        public static AiStreamResult Fail(string error) => new(false, "", error, false);
        public static AiStreamResult Stopped(string text) => new(false, text, "", true);
    }

    /// <summary>
    /// One full chat turn with token streaming for the AI chat window.
    ///
    ///  · ollama    — true token stream (NDJSON, /api/chat stream:true); every
    ///                decoded delta is pushed to <paramref name="onDelta"/> as it
    ///                arrives, so answers appear word by word.
    ///  · anthropic — buffered /v1/messages request; the whole text arrives in a
    ///                single delta and the WINDOW plays the typewriter reveal, so
    ///                both providers feel identical from the user's seat.
    ///
    /// Constraints (DEV_PLAN agent rules): runs entirely on the caller's worker
    /// thread, the UI thread is only touched by the window's own dispatcher
    /// marshal inside onDelta; history is trimmed to the last 16 turns to bound
    /// request size; one hard 180 s cap (local models think longer than the
    /// quick-ask path); every failure is caught and returned, never thrown;
    /// log lines carry LENGTHS ONLY — user prompts and answers never hit disk.
    /// </summary>
    public async Task<AiStreamResult> StreamChatAsync(
        Settings settings, IReadOnlyList<AiProviders.AiTurn> history, string prompt,
        Action<string> onDelta, CancellationToken ct)
    {
        string style = settings.AiStyle;
        try
        {
            if (!settings.AiEnabled)
                return AiStreamResult.Fail("AI is off — enable it in Settings → AI");

            // bounded context: the last 16 turns are plenty and keep requests small
            var turns = history.Skip(Math.Max(0, history.Count - 16))
                .Select(t => new AiProviders.AiTurn(t.Role, t.Content))
                .ToList();
            turns.Add(new AiProviders.AiTurn("user", prompt ?? ""));

            var (ok, spec, err) = AiProviders.BuildChat(style, settings.AiEndpoint, settings.AiModel, settings.AiApiKey, turns);
            if (!ok || spec is null)
                return AiStreamResult.Fail(err);

            DiagnosticLogger.Log("AiChat", $"turn · {style} · {settings.AiModel} · {turns.Count} turns · {(prompt ?? "").Length} chars in");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(180));

            using var req = new HttpRequestMessage(HttpMethod.Post, spec.Url);
            foreach (var (name, value) in spec.Headers)
                req.Headers.TryAddWithoutValidation(name, value);
            req.Content = new StringContent(spec.Json, Encoding.UTF8, "application/json");

            using var resp = await HttpChat.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string body = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                string reason = $"HTTP {(int)resp.StatusCode} · {Trunc(body, 160)}";
                DiagnosticLogger.Log("AiChat", AiProviders.Redact(settings.AiApiKey, "failed: " + reason));
                return AiStreamResult.Fail(reason);
            }

            var sb = new StringBuilder();
            bool anthropic = AiProviders.IsAnthropic(style, settings.AiEndpoint);

            await using var stream = await resp.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false) is { } line)
            {
                var chunk = anthropic
                    ? AiProviders.ParseAnthropicSseLine(line)
                    : AiProviders.ParseOllamaStreamLine(line);

                if (chunk.Error.Length > 0)
                {
                    DiagnosticLogger.Log("AiChat", AiProviders.Redact(settings.AiApiKey, "stream error: " + chunk.Error));
                    return sb.Length > 0 ? AiStreamResult.Stopped(sb.ToString()) : AiStreamResult.Fail(chunk.Error);
                }
                if (chunk.Done) break;
                if (chunk.Delta.Length > 0)
                {
                    sb.Append(chunk.Delta);
                    try { onDelta(chunk.Delta); } catch { /* a UI hiccup must not kill the stream */ }
                }
            }

            string text = sb.ToString().Trim();
            if (text.Length == 0)
                return AiStreamResult.Fail("the reply was empty — check the model name in Settings");

            DiagnosticLogger.Log("AiChat", $"done · {text.Length} chars out");
            return AiStreamResult.Good(text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // the linked 180 s timeout fired (the user's own cancel token is still live)
            return AiStreamResult.Fail("the model took too long — try a lighter model or a shorter question");
        }
        catch (OperationCanceledException)
        {
            return AiStreamResult.Stopped("");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("AiChat.Stream", ex);
            return AiStreamResult.Fail(AiProviders.Redact(settings?.AiApiKey, ex.Message));
        }
    }

    /// <summary>Chat client: no 45 s cap — streaming turns own their lifetime via linked CTS.</summary>
    private static readonly HttpClient HttpChat = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static string HostOf(string url)
    {
        try { return new Uri(url).Host; } catch { return url; }
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    public sealed record AiReply(bool Ok, string Text, string Error)
    {
        public static AiReply Good(string text) => new(true, text, "");
        public static AiReply Bad(string error) => new(false, "", error);
    }
}
