using System.IO;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// v2.3 (DEV_PLAN Task 3.2) — in-memory index of the user's Chrome &amp; Edge bookmarks.
///
/// Follows the established index pattern (AppIndex/FileIndex): a single background
/// load at startup, results swapped in atomically, every exception logged instead
/// of thrown. Queries are synchronous, in-memory and bounded — a B/ keystroke never
/// touches the disk. A cheap mtime probe (≤ 8 stat calls, only on B/ queries)
/// triggers a background reload when the user bookmarks something new.
/// </summary>
public sealed class BookmarkIndex
{
    private const int MaxFiles = 8;

    private readonly object _gate = new();
    private List<BookmarkEntry> _items = new();
    private readonly List<(string Path, DateTime Mtime)> _sources = new();
    private volatile bool _ready;

    public bool Ready { get { lock (_gate) return _ready; } }
    public int Count { get { lock (_gate) return _items.Count; } }

    /// <summary>Discovers Chrome/Edge profile Bookmarks files for this user.</summary>
    internal static List<string> DiscoverFiles()
    {
        var found = new List<string>();
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] browserRoots =
            {
                Path.Combine(local, "Google", "Chrome", "User Data"),
                Path.Combine(local, "Microsoft", "Edge", "User Data"),
            };
            foreach (var root in browserRoots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    // One file per profile: Default, Profile 1, Profile 2… — capped for sanity.
                    foreach (var profile in Directory.EnumerateDirectories(root))
                    {
                        string candidate = Path.Combine(profile, "Bookmarks");
                        if (File.Exists(candidate)) found.Add(candidate);
                        if (found.Count >= MaxFiles) return found;
                    }
                }
                catch (Exception ex) { DiagnosticLogger.LogException("Bookmarks.Discover", ex); }
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Bookmarks.DiscoverRoot", ex); }
        return found;
    }

    /// <summary>Kicks off the initial background load (called once from the window).</summary>
    public void BeginLoadInBackground() => _ = Task.Run(() => { try { LoadAll(); } catch { } });

    /// <summary>
    /// Cheap freshness probe for B/ queries: if any source file's mtime moved since
    /// the last load, a background reload starts; the (stale) current list keeps
    /// serving until the new one swaps in.
    /// </summary>
    public void RefreshIfStale()
    {
        try
        {
            (string, DateTime)[] sources;
            lock (_gate) sources = _sources.Select(s => (s.Path, s.Mtime)).ToArray();
            if (sources.Length == 0) return;

            foreach (var (path, mtime) in sources)
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) != mtime)
                    {
                        DiagnosticLogger.Log("Bookmarks", "source changed — reloading in background");
                        _ = Task.Run(() => { try { LoadAll(); } catch { } });
                        return;
                    }
                }
                catch { /* file vanished — ignore until next load */ }
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Bookmarks.Refresh", ex); }
    }

    private void LoadAll()
    {
        var loaded = new List<BookmarkEntry>();
        var sources = new List<(string, DateTime)>();

        foreach (var file in DiscoverFiles())
        {
            try
            {
                loaded.AddRange(Bookmarks.Parse(File.ReadAllText(file)));
                sources.Add((file, File.GetLastWriteTimeUtc(file)));
                if (loaded.Count >= Bookmarks.MaxEntries) break;
            }
            catch (Exception ex) { DiagnosticLogger.LogException("Bookmarks.Load", ex); }
        }

        lock (_gate)
        {
            _items = loaded;
            _sources.Clear();
            _sources.AddRange(sources);
            _ready = true;
        }
        DiagnosticLogger.Log("Bookmarks", $"indexed {loaded.Count} bookmarks from {sources.Count} profile file(s)");
    }

    /// <summary>
    /// Synchronous, in-memory, bounded query. Empty query → newest first (the
    /// "what did I just save" view); otherwise fuzzy-scored on name + folder + URL.
    /// </summary>
    public List<BookmarkEntry> Query(string q, int max)
    {
        List<BookmarkEntry> items;
        lock (_gate) items = _items;

        try
        {
            if (q.Length == 0)
                return items.OrderByDescending(b => b.AddedAtMicros).Take(max).ToList();

            return items
                .Select(b => (B: b, S: Math.Max(
                    Fuzzy.Score(q, b.Name),
                    Math.Max(Fuzzy.Score(q, b.Folder), Fuzzy.Score(q, b.Url) / 2))))
                .Where(x => x.S > 0)
                .OrderByDescending(x => x.S)
                .Take(max)
                .Select(x => x.B)
                .ToList();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Bookmarks.Query", ex);
            return new List<BookmarkEntry>();
        }
    }
}
