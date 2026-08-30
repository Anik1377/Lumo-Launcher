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
