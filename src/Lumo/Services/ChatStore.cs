using System.IO;
using System.Text.Json;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>One stored chat turn ("user" | "assistant") with its UTC timestamp.</summary>
public sealed record ChatMessage(string Role, string Content, DateTime At);

/// <summary>
/// One persisted conversation. Title is auto-derived from the first user
/// message; the persona id records which system prompt shaped the chat.
/// </summary>
public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New chat";
    public string Persona { get; set; } = "assistant";
    public bool Pinned { get; set; }               // v2.4.0-alpha.6 — favorite chats float to the top
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>True while the conversation has nothing worth listing yet.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Messages.Count == 0;

    /// <summary>Monotonic upsert counter — makes ordering deterministic when two sessions share a timestamp.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    internal long Seq;

    /// <summary>v2.4.0-alpha.5 — derives a chat title from the first user message: first line, ≤40 chars.</summary>
    public static string DeriveTitle(string firstUserMessage)
    {
        string s = (firstUserMessage ?? "").Trim();
        if (s.Length == 0) return "New chat";
        int nl = s.IndexOfAny(new[] { '\r', '\n' });
        if (nl > 0) s = s[..nl].Trim();
        return s.Length <= 40 ? s : s[..40].TrimEnd() + "…";
    }
}

/// <summary>
/// v2.4.0-alpha.5 — persisted chat history for the AI chat tab (chats.json in
/// %APPDATA%\Lumo). Multiple conversations, each with its persona and messages,
/// so the sidebar can list past chats and reload them after a restart.
///
/// Storage rules (same doctrine as UsageStore):
///  · tolerant — a corrupt file loads as empty, never throws;
///  · bounded — MaxSessions newest sessions kept, MaxMessagesPerSession newest
///    messages kept per session (old turns fall off, context stays bounded);
///  · atomic — saves are serialized under a gate and each write uses a UNIQUE
///    tmp path before the swap, so overlapping saves can never swap a
///    partially-written file (the v2.4.0-alpha.4 UsageStore lesson, applied here
///    from day one);
///  · private — content lives only in the user's own settings dir; log lines
///    carry counts and lengths, never message text.
/// </summary>
public sealed class ChatStore
{
    public const int MaxSessions = 40;
    public const int MaxMessagesPerSession = 200;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private readonly List<ChatSession> _sessions = new();   // UpdatedAt desc, Seq desc
    private readonly string _file;
    private long _seqCounter;

    public ChatStore(string? file = null) => _file = file ?? AppPaths.ChatsFile;

    // ------------------------------------------------------------------ read

    public static ChatStore Load(string? file = null)
    {
        var store = new ChatStore(file);
        try
        {
            string path = file ?? AppPaths.ChatsFile;
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var loaded = doc.RootElement.EnumerateArray()
                        .Select(TryParseSession)
                        .Where(s => s is not null)
                        .Select(s => s!)
                        .ToList();
                    loaded.Sort((a, b) =>
                    {
                        int c = b.Pinned.CompareTo(a.Pinned);          // v2.4.0-alpha.6 — pinned chats first
                        if (c != 0) return c;
                        c = b.UpdatedAt.CompareTo(a.UpdatedAt);
                        return c != 0 ? c : b.Seq.CompareTo(a.Seq);
                    });
                    lock (store._gate)
                    {
                        store._sessions.AddRange(loaded);
                        store.PruneLocked();
                    }
                }
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ChatStore.Load", ex); }
        return store;
    }

    private static ChatSession? TryParseSession(JsonElement el)
    {
        try
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            var s = new ChatSession();
            if (el.TryGetProperty(nameof(ChatSession.Id), out var id) && id.ValueKind == JsonValueKind.String)
                s.Id = id.GetString() ?? s.Id;
            if (el.TryGetProperty(nameof(ChatSession.Title), out var t) && t.ValueKind == JsonValueKind.String)
                s.Title = t.GetString() ?? s.Title;
            if (el.TryGetProperty(nameof(ChatSession.Persona), out var p) && p.ValueKind == JsonValueKind.String)
                s.Persona = p.GetString() ?? s.Persona;
            if (el.TryGetProperty(nameof(ChatSession.Pinned), out var pin))
            {
                if (pin.ValueKind is JsonValueKind.True or JsonValueKind.False) s.Pinned = pin.GetBoolean();
                else if (pin.ValueKind == JsonValueKind.String && bool.TryParse(pin.GetString(), out var pb)) s.Pinned = pb;
            }
            if (el.TryGetProperty(nameof(ChatSession.CreatedAt), out var c) && c.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(c.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var cd))
                s.CreatedAt = cd.ToUniversalTime();
            if (el.TryGetProperty(nameof(ChatSession.UpdatedAt), out var u) && u.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(u.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var ud))
                s.UpdatedAt = ud.ToUniversalTime();
            if (el.TryGetProperty(nameof(ChatSession.Messages), out var ms) && ms.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in ms.EnumerateArray())
                {
                    if (m.ValueKind != JsonValueKind.Object) continue;
                    string role = "", content = "";
                    DateTime at = DateTime.UtcNow;
                    if (m.TryGetProperty(nameof(ChatMessage.Role), out var r) && r.ValueKind == JsonValueKind.String)
                        role = r.GetString() ?? "";
                    if (m.TryGetProperty(nameof(ChatMessage.Content), out var ct) && ct.ValueKind == JsonValueKind.String)
                        content = ct.GetString() ?? "";
                    if (m.TryGetProperty(nameof(ChatMessage.At), out var ma) && ma.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(ma.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var mad))
                        at = mad.ToUniversalTime();
                    if (role.Length > 0 && content.Length > 0)
                        s.Messages.Add(new ChatMessage(role, content, at));
                }
            }
            if (s.Id.Length == 0) return null;
            return s;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ write API

    /// <summary>Inserts or refreshes a session (matched by Id), keeps it bounded, persists off-thread.</summary>
    public void Upsert(ChatSession session)
    {
        if (session is null) return;
        try
        {
            lock (_gate)
            {
                session.UpdatedAt = DateTime.UtcNow;
                session.Seq = ++_seqCounter;   // deterministic tie-break on equal timestamps

                // bound per-session history: newest turns win, old ones fall off
                if (session.Messages.Count > MaxMessagesPerSession)
                    session.Messages.RemoveRange(0, session.Messages.Count - MaxMessagesPerSession);

                _sessions.RemoveAll(s => string.Equals(s.Id, session.Id, StringComparison.Ordinal));
                _sessions.Insert(0, session);
                PruneLocked();
            }
            ScheduleSave();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ChatStore.Upsert", ex); }
    }

    /// <summary>Removes a session by id. Returns true when something was removed.</summary>
    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            bool removed;
            lock (_gate)
            {
                removed = _sessions.RemoveAll(s => string.Equals(s.Id, id, StringComparison.Ordinal)) > 0;
            }
            if (removed) ScheduleSave();
            return removed;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ChatStore.Delete", ex); return false; }
    }

    /// <summary>Sessions ordered newest-first (metadata + messages; treat as read-only).</summary>
    public IReadOnlyList<ChatSession> Sessions
    {
        get { lock (_gate) { return _sessions.ToList(); } }
    }

    public ChatSession? Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate)
            return _sessions.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Number of stored sessions (diagnostics/tests).</summary>
    public int Count { get { lock (_gate) { return _sessions.Count; } } }

    private void PruneLocked()
    {
        // newest first: recency wins, upsert order breaks equal-timestamp ties
        if (_sessions.Count > MaxSessions)
            _sessions.RemoveRange(MaxSessions, _sessions.Count - MaxSessions);
        _sessions.Sort((a, b) =>
        {
            int c = b.Pinned.CompareTo(a.Pinned);              // v2.4.0-alpha.6 — pinned chats first
            if (c != 0) return c;
            c = b.UpdatedAt.CompareTo(a.UpdatedAt);
            return c != 0 ? c : b.Seq.CompareTo(a.Seq);
        });
    }

    // ------------------------------------------------------------------ persistence

    private int _saving;

    private void ScheduleSave()
    {
        if (Interlocked.CompareExchange(ref _saving, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try { Save(); }
            finally { Interlocked.Exchange(ref _saving, 0); }
        });
    }

    /// <summary>
    /// Atomic write: serialize under _saveGate, write to a UNIQUE tmp path, then
    /// swap. (UsageStore v2.4.0-alpha.4 fix applied from day one — overlapping
    /// scheduled + explicit saves can never swap a partially-written file.)
    /// </summary>
    public void Save()
    {
        try
        {
            string json;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(_sessions.ToList(), JsonOpts);
            }
            lock (_saveGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                string tmp = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _file, overwrite: true);
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("ChatStore.Save", ex); }
        try
        {
            // orphan-tmp cleanup: sweeps any tmp left behind by a killed process
            string? dir = Path.GetDirectoryName(_file);
            if (dir is null) return;
            string name = Path.GetFileName(_file);
            foreach (var orphan in Directory.GetFiles(dir, name + ".*.tmp"))
            { try { File.Delete(orphan); } catch { } }
        }
        catch { /* best-effort */ }
    }
}
