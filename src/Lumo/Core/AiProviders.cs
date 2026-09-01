using System.Text;
using System.Text.Json;

namespace Lumo.Core;

/// <summary>
/// v2.3 (DEV_PLAN Task 3.1) — pure request/response layer for the ? AI answers.
///
/// Two provider styles are supported, matching the DEV_PLAN's "Ollama / Anthropic
/// compatible" brief:
///
///   · ollama    — local runtime, default endpoint http://localhost:11434,
///                 POST {endpoint}/api/chat  (stream:false), no API key needed.
///   · anthropic — POST {endpoint}/v1/messages with the x-api-key +
///                 anthropic-version headers (endpoint default https://api.anthropic.com).
///
/// Everything here is synchronous and side-effect free so the test harness can
/// assert on exact URLs, headers and JSON bodies — and on the rule that the API
/// key NEVER lands in a log line: only Redact()-ed text may be logged.
/// </summary>
public static class AiProviders
{
    public const string OllamaStyle = "ollama";
    public const string AnthropicStyle = "anthropic";
    public const string AnthropicVersion = "2023-06-01";
    public const int MaxAnswerTokens = 1024;

    /// <summary>A fully-built HTTP request: URL, JSON body and the headers to set.</summary>
    public sealed record AiRequestSpec(string Url, string Json, IReadOnlyDictionary<string, string> Headers);

    /// <summary>v2.3.0-alpha.3 — one conversation turn for the AI chat tab ("user" | "assistant").
    /// v2.6.0-alpha.5 — <see cref="Image"/> optionally carries one attached image
    /// (prompt-kit's ImageAttachment) for vision models; null = text-only turn.</summary>
    public sealed record AiTurn(string Role, string Content, ImagePayload? Image = null);

    /// <summary>
    /// v2.6.0-alpha.5 — one image attached to a chat turn. The base64 payload is
    /// the RAW base64 (no "data:" prefix — Ollama wants the bare string and the
    /// Anthropic media_type field carries the mime separately). Only the formats
    /// and sizes the two providers actually accept get through Create();
    /// everything else is rejected before it can burn a request.
    /// </summary>
    public sealed record ImagePayload(string Base64, string MediaType)
    {
        public const long MaxBytes = 4 * 1024 * 1024;   // 4 MB — Ollama's per-image cap
        public static readonly string[] SupportedMediaTypes = ["image/png", "image/jpeg", "image/webp", "image/gif"];

        public static bool IsSupportedMediaType(string? mediaType) =>
            SupportedMediaTypes.Contains((mediaType ?? "").Trim().ToLowerInvariant());

        /// <summary>Validates + strips a data-URI prefix if present; null when the payload is unusable.</summary>
        public static ImagePayload? Create(byte[]? bytes, string? mediaType)
        {
            try
            {
                if (bytes is not { Length: > 0 } || bytes.Length > MaxBytes) return null;
                if (!IsSupportedMediaType(mediaType)) return null;
                return new ImagePayload(Convert.ToBase64String(bytes), mediaType!.Trim().ToLowerInvariant());
            }
            catch { return null; }
        }

        /// <summary>Decoded byte size (base64 → bytes), for the attachment chip caption.</summary>
        public long ByteCount => Base64.Length * 3 / 4 - (Base64.Length > 0 && Base64[^1] == '=' ? (Base64.Length % 4 == 0 ? 2 : 1) : 0);
    }

    /// <summary>
    /// One decoded streaming chunk. Delta is the text to append; Done ends the
    /// generation; Error carries a short human reason (Delta "" then). Tolerant
    /// parsers below NEVER throw — a garbage line yields an empty chunk.
    /// </summary>
    public sealed record StreamChunk(string Delta, bool Done, string Error)
    {
        public static StreamChunk Empty { get; } = new("", false, "");
    }

    /// <summary>
    /// Builds the provider request. Returns Ok=false with a short human reason when
    /// the configuration is incomplete (missing model, missing key for Anthropic, …).
    /// The prompt is embedded JSON-escaped; the key only ever appears in Headers.
    /// </summary>
    public static (bool Ok, AiRequestSpec? Spec, string Error) Build(
        string? style, string? endpoint, string? model, string? apiKey, string prompt)
    {
        try
        {
            string promptText = (prompt ?? "").Trim();
            if (promptText.Length == 0) return (false, null, "empty prompt");

            bool anthropic = IsAnthropic(style, endpoint);
            string baseUri = NormalizeBase(endpoint, anthropic);

            if (string.IsNullOrWhiteSpace(model))
                return (false, null, anthropic ? "no model set — pick one in Settings" : "no model set — e.g. llama3.2");
            if (anthropic && string.IsNullOrWhiteSpace(apiKey))
                return (false, null, "no API key set — add your Anthropic key in Settings");

            string url, body;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (anthropic)
            {
                url = baseUri + "/v1/messages";
                body = JsonSerializer.Serialize(new
                {
                    model = model!.Trim(),
                    max_tokens = MaxAnswerTokens,
                    messages = new[] { new { role = "user", content = promptText } },
                });
                headers["x-api-key"] = apiKey!.Trim();
                headers["anthropic-version"] = AnthropicVersion;
                headers["content-type"] = "application/json";
            }
            else
            {
                url = baseUri + "/api/chat";
                body = JsonSerializer.Serialize(new
                {
                    model = model!.Trim(),
                    stream = false,
                    messages = new[] { new { role = "user", content = promptText } },
                });
                headers["content-type"] = "application/json";
                if (!string.IsNullOrWhiteSpace(apiKey))
                    headers["authorization"] = "Bearer " + apiKey.Trim();   // optional gateways only
            }

            return (true, new AiRequestSpec(url, body, headers), "");
        }
        catch (Exception ex)
        {
            return (false, null, Redact(apiKey, ex.Message));
        }
    }

    /// <summary>
    /// v2.3.0-alpha.3 — multi-turn variant for the AI chat tab. Same contract as
    /// <see cref="Build"/>, but the body carries the whole conversation:
    ///   · ollama    → /api/chat { model, stream:true, messages:[…] }  (token streaming)
    ///   · anthropic → /v1/messages { model, max_tokens, stream:true, messages:[…] }
    ///     v2.4.0-alpha.6 — Anthropic now streams too: the body carries stream:true
    ///     and the reply arrives as SSE (content_block_delta lines the window's
    ///     stream loop already parses), replacing the old buffered request that
    ///     the SSE parser could never read.
    /// v2.4.0-alpha.5 — <paramref name="systemPrompt"/> adds a persona: a leading
    /// role:"system" message for Ollama, the top-level "system" field for
    /// Anthropic (its API has no system role inside messages). Omitted when null.
    /// The key only ever travels in headers, as before.
    /// </summary>
    public static (bool Ok, AiRequestSpec? Spec, string Error) BuildChat(
        string? style, string? endpoint, string? model, string? apiKey, IReadOnlyList<AiTurn> turns,
        string? systemPrompt = null)
    {
        try
        {
            bool anthropic = IsAnthropic(style, endpoint);
            string baseUri = NormalizeBase(endpoint, anthropic);

            if (turns.Count == 0 || string.IsNullOrWhiteSpace(turns[^1].Content))
                return (false, null, "empty prompt");
            if (string.IsNullOrWhiteSpace(model))
                return (false, null, anthropic ? "no model set — pick one in Settings" : "no model set — e.g. llama3.2");
            if (anthropic && string.IsNullOrWhiteSpace(apiKey))
                return (false, null, "no API key set — add your Anthropic key in Settings");

            // v2.6.0-alpha.5 — images ride only on the last few turns: a long
            // conversation must not re-upload every screenshot ever attached on
            // every request (MaxImageTurns bounds the payload from the end).
            int imageBudget = MaxImageTurns;
            var turnsList = new List<object>();
            for (int i = turns.Count - 1; i >= 0; i--)
            {
                var t = turns[i];
                if (string.IsNullOrWhiteSpace(t.Content) ||
                    (t.Role != "user" && t.Role != "assistant")) continue;
                var img = t.Image;
                if (img is not null)
                {
                    if (imageBudget <= 0) img = null;   // older than the budget — text only
                    else imageBudget--;
                }
                turnsList.Add(SerializeTurn(t.Role, t.Content, img, anthropic));
            }
            turnsList.Reverse();
            string sys = (systemPrompt ?? "").Trim();
            if (sys.Length > 0 && !anthropic)
                turnsList.Insert(0, new { role = "system", content = sys });   // Ollama: leading system message
            if (turnsList.Count == 0)
                return (false, null, "empty prompt");

            string url, body;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (anthropic)
            {
                url = baseUri + "/v1/messages";
                // Anthropic carries the persona as the top-level system field, never
                // as a message role; omit the property entirely when there is none.
                // v2.4.0-alpha.6 — stream:true turns the reply into an SSE token
                // stream (parsed by ParseAnthropicSseLine in the chat window's loop).
                object bodyObj = sys.Length > 0
                    ? new { model = model!.Trim(), max_tokens = MaxAnswerTokens, stream = true, system = sys, messages = turnsList }
                    : new { model = model!.Trim(), max_tokens = MaxAnswerTokens, stream = true, messages = turnsList };
                body = JsonSerializer.Serialize(bodyObj);
                headers["x-api-key"] = apiKey!.Trim();
                headers["anthropic-version"] = AnthropicVersion;
                headers["content-type"] = "application/json";
            }
            else
            {
                url = baseUri + "/api/chat";
                body = JsonSerializer.Serialize(new { model = model!.Trim(), stream = true, messages = turnsList });
                headers["content-type"] = "application/json";
                if (!string.IsNullOrWhiteSpace(apiKey))
                    headers["authorization"] = "Bearer " + apiKey.Trim();   // optional gateways only
            }

            return (true, new AiRequestSpec(url, body, headers), "");
        }
        catch (Exception ex)
        {
            return (false, null, Redact(apiKey, ex.Message));
        }
    }

    /// <summary>How many of the most recent turns keep their attached images on the wire.</summary>
    public const int MaxImageTurns = 2;

    /// <summary>
    /// v2.6.0-alpha.5 — one turn as the provider's message JSON. Text-only turns
    /// keep the flat {role, content} shape both providers accept; an image turn
    /// becomes:
    ///   · ollama    → { role, content, images: [ "&lt;base64&gt;" ] }  (bare base64,
    ///                 the shape /api/chat documents for vision models);
    ///   · anthropic → { role, content: [ {type:"image", source:{type:"base64",
    ///                 media_type, data}}, {type:"text", text} ] } — image block
    ///                 first, the order Claude's vision guide recommends.
    /// </summary>
    private static object SerializeTurn(string role, string content, ImagePayload? image, bool anthropic)
    {
        if (image is null)
            return new { role, content };
        if (anthropic)
        {
            return new
            {
                role,
                content = new object[]
                {
                    new { type = "image", source = new { type = "base64", media_type = image.MediaType, data = image.Base64 } },
                    new { type = "text", text = content },
                },
            };
        }
        return new { role, content, images = new[] { image.Base64 } };
    }

    // ---------------------------------------------------------------- v2.3.0-alpha.3 — streaming parsers

    /// <summary>
    /// One NDJSON line of Ollama's streamed /api/chat (stream:true):
    /// { "message": { "role":"assistant", "content":"tok" }, "done": false }
    /// The final line carries "done":true (its content is usually "").
    /// </summary>
    public static StreamChunk ParseOllamaStreamLine(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return StreamChunk.Empty;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return StreamChunk.Empty;

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                return new StreamChunk("", true, err.GetString() ?? "provider error");

            bool done = root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True;
            string delta = "";
            if (root.TryGetProperty("message", out var msg) &&
                msg.ValueKind == JsonValueKind.Object &&
                msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                delta = c.GetString() ?? "";
            return new StreamChunk(delta, done, "");
        }
        catch { return StreamChunk.Empty; }
    }

    /// <summary>
    /// One line of Anthropic's SSE stream ("data: {…}"). content_block_delta
    /// carries text deltas, message_stop ends the turn, event errors surface
    /// their message. Non-data lines (event:, comments, blank) → empty chunk.
    /// </summary>
    public static StreamChunk ParseAnthropicSseLine(string? line)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(line)) return StreamChunk.Empty;
            string s = line.Trim();
            if (!s.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return StreamChunk.Empty;
            s = s[5..].Trim();
            if (s.Length == 0 || s == "[DONE]") return StreamChunk.Empty;

            using var doc = JsonDocument.Parse(s);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return StreamChunk.Empty;

            string type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";

            if (type == "error")
            {
                string msg = "provider error";
                if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object &&
                    e.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    msg = m.GetString() ?? msg;
                return new StreamChunk("", true, msg);
            }
            if (type == "message_stop") return new StreamChunk("", true, "");
            if (type == "content_block_delta" &&
                root.TryGetProperty("delta", out var delta) &&
                delta.ValueKind == JsonValueKind.Object &&
                delta.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String)
                return new StreamChunk(tx.GetString() ?? "", false, "");

            return StreamChunk.Empty;
        }
        catch { return StreamChunk.Empty; }
    }

    /// <summary>
    /// Pulls the answer text out of a provider response. Tolerant: returns null for
    /// anything unexpected (error payloads, streaming chunks, shape changes).
    ///   · ollama /api/chat  → { message: { content: "…" } }
    ///   · ollama /api/generate (fallback) → { response: "…" }
    ///   · anthropic /v1/messages → { content: [ { type: "text", text: "…" }, … ] }
    /// </summary>
    public static string? Extract(string? style, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (IsAnthropic(style, null))
            {
                if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    return null;
                var sb = new StringBuilder();
                foreach (var block in content.EnumerateArray())
                    if (block.ValueKind == JsonValueKind.Object &&
                        block.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        sb.Append(t.GetString());
                string joined = sb.ToString().Trim();
                return joined.Length > 0 ? joined : null;
            }

            // Ollama chat shape first, generate shape as the fallback.
            if (root.TryGetProperty("message", out var msg) &&
                msg.ValueKind == JsonValueKind.Object &&
                msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            {
                string s = c.GetString()?.Trim() ?? "";
                return s.Length > 0 ? s : null;
            }
            if (root.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.String)
            {
                string s = r.GetString()?.Trim() ?? "";
                return s.Length > 0 ? s : null;
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>True when the style/endpoint pair means "Anthropic Messages API".</summary>
    public static bool IsAnthropic(string? style, string? endpoint)
    {
        if (!string.IsNullOrWhiteSpace(style))
            return style.Trim().Equals(AnthropicStyle, StringComparison.OrdinalIgnoreCase);
        // No explicit style: sniff the endpoint, default stays local Ollama.
        return !string.IsNullOrWhiteSpace(endpoint) &&
               endpoint.Contains("anthropic", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Trims trailing slashes and scheme typos so endpoint edits can't corrupt URLs.</summary>
    public static string NormalizeBase(string? endpoint, bool anthropic)
    {
        string fallback = anthropic ? "https://api.anthropic.com" : "http://localhost:11434";
        string s = (endpoint ?? "").Trim();
        if (s.Length == 0) return fallback;
        if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            s = "http://" + s;   // user typed "localhost:11434" — never https for a local port
        return s.TrimEnd('/');
    }

    /// <summary>
    /// Defence-in-depth for logging: replaces every occurrence of the API key with
    /// "***". Any log line that could echo request material MUST pass through this.
    /// </summary>
    public static string Redact(string? secret, string message)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(message)) return message ?? "";
        return message.Replace(secret, "***", StringComparison.OrdinalIgnoreCase);
    }
}
