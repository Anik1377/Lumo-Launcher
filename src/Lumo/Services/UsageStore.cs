using System.IO;
using System.Text.Json;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>One entry of the MRU store: how often a target was launched, and when last.</summary>
public sealed record UsageEntry(long Count, DateTime LastUsed);

/// <summary>
/// v2.1 (DEV_PLAN Task 1.1) — usage / frequency store behind the MRU ranking.
///
/// Every launch through Lumo bumps a counter for the target's stable key
/// (the file/app path, or the resolved URL). SearchEngine blends the counter
/// into the fuzzy score via Fuzzy.ScoreWithUsage, so the apps the user actually
/// opens float to the top of equal-match result lists.
///
/// Storage: usage.json in %APPDATA%\Lumo — tiny, tolerant of corruption, and
/// written on a background thread (never on the keystroke path).
/// </summary>
public sealed class UsageStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, UsageEntry> _m = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _file;
    private int _saving;                     // 1 while a background save is in flight

    public UsageStore(string? file = null) => _file = file ?? AppPaths.UsageFile;

    /// <summary>Bump the launch counter for a key (path or URL). Never throws.</summary>
    public void Record(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        try
        {
            lock (_gate)
            {
                var existing = _m.TryGetValue(key, out var e) ? e : null;
                _m[key] = new UsageEntry((existing?.Count ?? 0) + 1, DateTime.UtcNow);
            }
            ScheduleSave();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("UsageStore.Record", ex); }
    }

    public UsageEntry? Get(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        lock (_gate) { return _m.TryGetValue(key, out var e) ? e : null; }
    }

    /// <summary>Number of tracked targets (diagnostics/tests).</summary>
    public int Count { get { lock (_gate) { return _m.Count; } } }

    /// <summary>
    /// Persist off the UI thread. A single-flight guard collapses bursts (rapid
    /// macro launches) into one write; the write itself is a temp-file swap so a
    /// crash can never truncate the JSON.
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
                var flat = new Dictionary<string, UsageEntry>(_m, StringComparer.OrdinalIgnoreCase);
                json = JsonSerializer.Serialize(flat, JsonOpts);
            }
            var dir = Path.GetDirectoryName(_file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("UsageStore.Save", ex); }
    }

    /// <summary>Tolerant load — a corrupt or half-written file costs nothing.</summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(_file));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            lock (_gate)
            {
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    try
                    {
                        if (p.Value.ValueKind != JsonValueKind.Object) continue;
                        // Read the record's own property names (Count/LastUsed), plus a
                        // compact legacy spelling, tolerantly — one bad entry never
                        // kills the rest.
                        long count = 0;
                        DateTime last = DateTime.UtcNow;

                        if (p.Value.TryGetProperty("Count", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var cl))
                            count = Math.Max(0, cl);
                        else if (p.Value.TryGetProperty("c", out var c2) && c2.ValueKind == JsonValueKind.Number && c2.TryGetInt64(out var cl2))
                            count = Math.Max(0, cl2);

                        if (p.Value.TryGetProperty("LastUsed", out var t) && t.ValueKind == JsonValueKind.String &&
                            DateTime.TryParse(t.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                            last = dt;
                        else if (p.Value.TryGetProperty("t", out var t2) && t2.ValueKind == JsonValueKind.String &&
                            DateTime.TryParse(t2.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt2))
                            last = dt2;

                        if (count > 0)
                            _m[p.Name] = new UsageEntry(count, last);
                    }
                    catch { /* one bad entry never kills the rest */ }
                }
            }
            DiagnosticLogger.Log("UsageStore", $"Loaded {_m.Count} usage entries");
        }
        catch (Exception ex) { DiagnosticLogger.LogException("UsageStore.Load", ex); }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };
}
