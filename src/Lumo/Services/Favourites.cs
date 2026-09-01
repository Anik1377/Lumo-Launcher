using System.IO;
using System.Text.Json;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// One pinned favourite. Besides the stable key (the result's RunArgument — file/app
/// path, resolved URL, or shortcut id) it stores just enough row data to re-render
/// the row on the empty view without re-resolving anything: title, subtitle, glyph
/// and the ResultKind name. The icon is deliberately NOT persisted — it is refreshed
/// from the shell at render time (and simply absent for web rows).
/// </summary>
public sealed record FavEntry(string Key, string Title, string Subtitle, string Glyph, string Kind);

/// <summary>
/// v2.2 (DEV_PLAN Task 2.2) — pinned favourites behind the FAVOURITES section on the
/// empty view. Rows are pinned/unpinned from the row context menu or the hover star
/// (v2.2.0-alpha.2); the store is keyed by RunArgument (OrdinalIgnoreCase) and keeps
/// insertion order — Snapshot() is oldest-first (storage order), DisplaySnapshot()
/// flips to newest-first for display.
///
/// Storage mirrors UsageStore: favourites.json in %APPDATA%\Lumo — tiny, tolerant of
/// corruption, written on a background thread (never on the keystroke path) with a
/// temp-file swap so a crash can never truncate the JSON.
/// </summary>
public sealed class Favourites
{
    private readonly object _gate = new();
    private readonly List<FavEntry> _items = new();          // insertion order
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _file;
    private int _saving;                                     // 1 while a background save is in flight

    public Favourites(string? file = null) => _file = file ?? AppPaths.FavouritesFile;

    /// <summary>True when the key is pinned. Never throws.</summary>
    public bool IsPinned(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        lock (_gate) { return _keys.Contains(key); }
    }

    /// <summary>Number of pinned favourites (diagnostics/tests).</summary>
    public int Count { get { lock (_gate) { return _items.Count; } } }

    /// <summary>Ordered copy of the pinned rows (oldest pin first — storage order). Never throws.</summary>
    public List<FavEntry> Snapshot()
    {
        lock (_gate) { return new List<FavEntry>(_items); }
    }

    /// <summary>
    /// v2.2.0-alpha.2 rework — the display order for the FAVOURITES section: newest
    /// pin first. The row the user just pinned is the one they most likely want
    /// next, so it leads the section (storage order is untouched — Snapshot() still
    /// returns oldest-first, and the JSON file keeps its insertion order).
    /// </summary>
    public List<FavEntry> DisplaySnapshot()
    {
        lock (_gate)
        {
            var list = new List<FavEntry>(_items);
            list.Reverse();
            return list;
        }
    }

    /// <summary>
    /// v2.2.0-alpha.2 — one call for the hover star and the menu's pin/unpin pair:
    /// adds when unpinned, removes when pinned. Returns the NEW pinned state
    /// (true = now pinned) — NOT the operation's success, so callers can show
    /// feedback directly. Callers are UI-thread-only, so the check-then-act is
    /// safe; the store itself stays fully thread-safe.
    /// </summary>
    public bool Toggle(string? key, string title, string subtitle, string glyph, string kind)
    {
        if (IsPinned(key)) { Remove(key); return false; }
        return Add(key, title, subtitle, glyph, kind);
    }

    /// <summary>
    /// Pins a row. Returns false (and changes nothing) when the key was already
    /// pinned or the key is empty. The latest snapshot of the row's texts is stored,
    /// so re-pinning after unpin refreshes the cached title/subtitle.
    /// </summary>
    public bool Add(string? key, string title, string subtitle, string glyph, string kind)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        try
        {
            lock (_gate)
            {
                if (!_keys.Add(key)) return false;
                _items.Add(new FavEntry(key, title ?? "", subtitle ?? "", glyph ?? "", kind ?? ""));
            }
            ScheduleSave();
            return true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Favourites.Add", ex); return false; }
    }

    /// <summary>Unpins a key. Returns true when something was actually removed.</summary>
    public bool Remove(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        try
        {
            bool removed;
            lock (_gate)
            {
                removed = _keys.Remove(key) && _items.RemoveAll(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)) > 0;
            }
            if (removed) ScheduleSave();
            return removed;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Favourites.Remove", ex); return false; }
    }

    /// <summary>
    /// Persist off the UI thread. A single-flight guard collapses bursts (rapid
    /// pin/unpin) into one write; the write itself is a temp-file swap.
    /// </summary>
    private void ScheduleSave()
    {
        if (Interlocked.CompareExchange(ref _saving, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try { Save(); }
            finally { Interlocked.Exchange(ref _saving, 0); }
        });
    }

    public void Save()
    {
        try
        {
            string json;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(new List<FavEntry>(_items), JsonOpts);
            }
            var dir = Path.GetDirectoryName(_file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, json);
            AtomicIo.Swap(tmp, _file);   // v3.0.0-alpha.4 — atomic replace
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Favourites.Save", ex); }
    }

    /// <summary>Tolerant load — a corrupt or half-written file costs nothing.
    /// v2.6.0-alpha.6 — the read itself retries past the transient lock an
    /// antivirus scanner can hold on a freshly written file (the same CI flake
    /// UsageStore.Load needed medicine for).</summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            string json = ReadWithRetry(_file);
            if (json.Length == 0) return;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            lock (_gate)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    try
                    {
                        if (el.ValueKind != JsonValueKind.Object) continue;
                        string key = el.TryGetProperty("Key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(key) || _keys.Contains(key)) continue;
                        string title = el.TryGetProperty("Title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
                        string sub = el.TryGetProperty("Subtitle", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
                        string glyph = el.TryGetProperty("Glyph", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() ?? "" : "";
                        string kind = el.TryGetProperty("Kind", out var kd) && kd.ValueKind == JsonValueKind.String ? kd.GetString() ?? "" : "";
                        _keys.Add(key);
                        _items.Add(new FavEntry(key, title, sub, glyph, kind));
                    }
                    catch { /* one bad entry never kills the rest */ }
                }
            }
            if (_items.Count > 0)
                DiagnosticLogger.Log("Favourites", $"Loaded {_items.Count} pinned favourites");
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Favourites.Load", ex); }
    }

    /// <summary>v2.6.0-alpha.6 — reads the store file, retrying past the transient
    /// IO errors an antivirus scanner can produce right after a fresh write.</summary>
    private static string ReadWithRetry(string file)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return File.ReadAllText(file); }
            catch (Exception ex) when (attempt < 3 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(120 * attempt);
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };
}
