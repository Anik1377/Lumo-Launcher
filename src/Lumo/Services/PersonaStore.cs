using System.IO;
using System.Text.Json;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// v2.4.0-alpha.6 — user-defined AI personas (personas.json in %APPDATA%\Lumo).
///
/// A custom persona is the same <see cref="ChatPersona"/> shape the built-in
/// registry uses, with a "custom_"-prefixed id so it can never collide with a
/// built-in id. The chat window resolves session ids against the custom list
/// FIRST (ChatPersonas.ResolveWith), so deleting a persona simply falls the
/// chats that used it back to the default assistant.
///
/// Storage doctrine (same as ChatStore/UsageStore):
///  · tolerant — a corrupt file loads as empty, never throws;
///  · bounded  — MaxPersonas newest-defined personas kept;
///  · atomic   — saves are serialized under a gate and each write uses a
///    UNIQUE tmp path before the swap (the alpha.4 lesson, applied everywhere);
///  · private  — prompts live only in the user's own settings dir; logs carry
///    counts and lengths, never prompt text.
/// </summary>
public sealed class PersonaStore
{
    public const int MaxPersonas = 24;

    private static readonly Lazy<PersonaStore> Lazy = new(() => new PersonaStore());
    /// <summary>App-wide shared instance — the chat window and Settings must see the same list.</summary>
    public static PersonaStore Current => Lazy.Value;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private readonly List<ChatPersona> _personas = new();   // definition order (oldest first)
    private readonly string _file;

    public PersonaStore(string? file = null) => _file = file ?? AppPaths.PersonasFile;

    // ------------------------------------------------------------------ read

    public static PersonaStore Load(string? file = null)
    {
        var store = new PersonaStore(file);
        try
        {
            string path = file ?? AppPaths.PersonasFile;
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(AtomicIo.ReadWithRetry(path));
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var p = TryParse(el);
                        if (p is not null)
                            lock (store._gate) { store._personas.Add(p); }
                    }
                    lock (store._gate) { store.PruneLocked(); }
                }
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("PersonaStore.Load", ex); }
        return store;
    }

    private static ChatPersona? TryParse(JsonElement el)
    {
        try
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            string id = "", name = "", glyph = "", prompt = "", blurb = "", face = "", color = "";
            if (el.TryGetProperty(nameof(ChatPersona.Id), out var v) && v.ValueKind == JsonValueKind.String)
                id = v.GetString() ?? "";
            if (el.TryGetProperty(nameof(ChatPersona.Name), out v) && v.ValueKind == JsonValueKind.String)
                name = v.GetString() ?? "";
            if (el.TryGetProperty(nameof(ChatPersona.Glyph), out v) && v.ValueKind == JsonValueKind.String)
                glyph = v.GetString() ?? "";
            if (el.TryGetProperty(nameof(ChatPersona.Prompt), out v) && v.ValueKind == JsonValueKind.String)
                prompt = v.GetString() ?? "";
            if (el.TryGetProperty(nameof(ChatPersona.Blurb), out v) && v.ValueKind == JsonValueKind.String)
                blurb = v.GetString() ?? "";
            if (el.TryGetProperty("Face", out v) && v.ValueKind == JsonValueKind.String)
                face = PersonaFaces.NormalizeId(v.GetString());
            if (el.TryGetProperty("Color", out v) && v.ValueKind == JsonValueKind.String)
                color = PersonaFaces.NormalizeColor(v.GetString());
            if (id.Length == 0 || name.Length == 0 || prompt.Length == 0) return null;
            return new ChatPersona(id, name, glyph.Length == 0 ? "\uE77B" : glyph, prompt, blurb, face, color);
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ read API

    /// <summary>Custom personas in definition order (treat as read-only).</summary>
    public IReadOnlyList<ChatPersona> All
    {
        get { lock (_gate) { return _personas.ToList(); } }
    }

    public ChatPersona? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate)
            return _personas.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public int Count { get { lock (_gate) { return _personas.Count; } } }

    // ------------------------------------------------------------------ write API

    /// <summary>
    /// Creates a persona from user input. Trims every field; an empty glyph gets
    /// the contact default; face/color normalize (unknown face or junk hex persist
    /// as "" = default). Returns the stored persona, or null when the input
    /// cannot make a valid persona (no name / no prompt / store full).
    /// </summary>
    public ChatPersona? Add(string name, string glyph, string prompt, string blurb,
                            string face = "", string color = "")
    {
        try
        {
            name = (name ?? "").Trim();
            prompt = (prompt ?? "").Trim();
            if (name.Length == 0 || prompt.Length == 0) return null;

            lock (_gate)
            {
                if (_personas.Count >= MaxPersonas) return null;
                var p = new ChatPersona(
                    Id: NewId(),
                    Name: Trunc(name, 40),
                    Glyph: Trunc((glyph ?? "").Trim(), 4) is { Length: > 0 } g ? g : "\uE77B",
                    Prompt: Trunc(prompt, 4000),
                    Blurb: Trunc((blurb ?? "").Trim(), 80),
                    Face: PersonaFaces.NormalizeId(face),
                    Color: PersonaFaces.NormalizeColor(color));
                _personas.Add(p);
                ScheduleSave();
                return p;
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("PersonaStore.Add", ex); return null; }
    }

    /// <summary>Updates name/glyph/prompt/blurb (+ optional face/color) on an existing persona.
    /// False when the id is unknown.</summary>
    public bool Update(string id, string name, string glyph, string prompt, string blurb,
                       string face = "", string color = "")
    {
        try
        {
            name = (name ?? "").Trim();
            prompt = (prompt ?? "").Trim();
            if (name.Length == 0 || prompt.Length == 0) return false;
            lock (_gate)
            {
                int idx = _personas.FindIndex(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) return false;
                _personas[idx] = _personas[idx] with
                {
                    Name = Trunc(name, 40),
                    Glyph = Trunc((glyph ?? "").Trim(), 4) is { Length: > 0 } g ? g : "\uE77B",
                    Prompt = Trunc(prompt, 4000),
                    Blurb = Trunc((blurb ?? "").Trim(), 80),
                    Face = PersonaFaces.NormalizeId(face),
                    Color = PersonaFaces.NormalizeColor(color),
                };
                ScheduleSave();
                return true;
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("PersonaStore.Update", ex); return false; }
    }

    /// <summary>Removes a persona by id. Chats that referenced it fall back to the default.</summary>
    public bool Delete(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            bool removed;
            lock (_gate)
                removed = _personas.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) ScheduleSave();
            return removed;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("PersonaStore.Delete", ex); return false; }
    }

    /// <summary>"custom_" + 8 hex chars — the prefix keeps the namespace disjoint from built-ins.</summary>
    public static string NewId() => ChatPersonas.CustomPrefix + Guid.NewGuid().ToString("N")[..8];

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];

    private void PruneLocked()
    {
        if (_personas.Count > MaxPersonas)
            _personas.RemoveRange(0, _personas.Count - MaxPersonas);   // oldest definitions fall off
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

    /// <summary>Atomic write: unique tmp per write under a gate, then swap (ChatStore doctrine).</summary>
    public void Save()
    {
        try
        {
            string json;
            lock (_gate) { json = JsonSerializer.Serialize(_personas.ToList(), JsonOpts); }
            lock (_saveGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                string tmp = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmp, json);
                AtomicIo.Swap(tmp, _file);   // v3.0.0-alpha.4 — atomic replace
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("PersonaStore.Save", ex); }
    }
}
