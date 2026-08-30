using System.IO;

namespace Lumo.Core;

public sealed record FileEntry(string Name, string FullPath);

/// <summary>
/// Hybrid file index:
///  • Builds in the BACKGROUND at startup (bounded, iterative, cancellable) — the UI thread
///    never participates in crawling, so typing can never freeze on indexing.
///  • While the index is building, F/ queries fall back to a small synchronous scan of
///    Desktop / Downloads / Documents so the user always gets instant results.
/// </summary>
public sealed class FileIndex
{
    /// <summary>Cap on indexed files — user-tunable from Settings → Search (v1.2).</summary>
    public int MaxEntries { get; set; } = 150_000;

    private const int MaxDepth = 14;

    private static readonly string[] SkippedDirNames =
    {
        "$recycle.bin", "system volume information", "windows", "appdata", "node_modules",
        ".git", ".svn", "packages", "temp", "cache", "$windows.~ws", "$windows.~bt",
        "program files", "program files (x86)", "programdata", "recovery", "perflogs",
    };

    private volatile List<FileEntry> _entries = new();
    public IReadOnlyList<FileEntry> Entries => _entries;

    private volatile bool _ready;
    public bool Ready => _ready;

    private long _seenCount;
    public long IndexedCount => Interlocked.Read(ref _seenCount);

    public void BeginIndexInBackground()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var list = new List<FileEntry>(4096);
                var seen = new HashSet<string>(256_000, StringComparer.OrdinalIgnoreCase);
                long seenCount = 0;

                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                    .Select(d => d.RootDirectory.FullName)
                    .ToList();

                // Prefer user profile first so the most relevant files get in before the cap.
                var seeds = new List<string>();
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile) && Directory.Exists(profile)) seeds.Add(profile);
                foreach (var d in drives)
                    if (!profile.StartsWith(d, StringComparison.OrdinalIgnoreCase) && Directory.Exists(d))
                        seeds.Add(d);

                foreach (var seed in seeds)
                {
                    if (list.Count >= MaxEntries) break;
                    var stack = new Stack<(string Dir, int Depth)>();
                    stack.Push((seed, 0));

                    while (stack.Count > 0 && list.Count < MaxEntries)
                    {
                        var (dir, depth) = stack.Pop();
                        if (depth > MaxDepth) continue;

                        try
                        {
                            foreach (var file in Directory.EnumerateFiles(dir))
                            {
                                seenCount++;
                                if (list.Count >= MaxEntries) break;

                                var name = System.IO.Path.GetFileName(file);
                                if (string.IsNullOrEmpty(name) || name.StartsWith("~$")) continue;
                                if (!seen.Add(file)) continue;
                                list.Add(new FileEntry(name, file));
                            }
                        }
                        catch { /* access denied */ }

                        if (list.Count >= MaxEntries) break;

                        try
                        {
                            foreach (var sub in Directory.EnumerateDirectories(dir))
                            {
                                var leaf = System.IO.Path.GetFileName(sub).ToLowerInvariant();
                                if (SkippedDirNames.Contains(leaf)) continue;
                                stack.Push((sub, depth + 1));
                            }
                        }
                        catch { /* access denied */ }
                    }
                }

                _entries = list;
                _ready = true;
                Interlocked.Exchange(ref _seenCount, seenCount);
                Services.DiagnosticLogger.Log("FileIndex", $"Indexed {list.Count} files ({seenCount} scanned)");
            }
            catch (Exception ex) { Services.DiagnosticLogger.LogException("FileIndex", ex); }
        });
    }

    /// <summary>In-memory filter over the published list — fast (bounded 150k) and synchronous.
    /// v2.1 — optional usage store blends launch frequency into the ranking.</summary>
    public List<FileEntry> Query(string query, int max, Services.UsageStore? usage = null)
    {
        var entries = _entries;
        var scored = new List<(FileEntry File, int Score)>(64);
        foreach (var e in entries)
        {
            int s = Fuzzy.Score(query, e.Name);
            if (s <= 0) continue;
            if (usage is not null) s = Fuzzy.ScoreWithUsage(s, usage.Get(e.FullPath));
            scored.Add((e, s));
            if (scored.Count > max * 8) break; // keep responsiveness on huge matches
        }
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored.Take(max).Select(x => x.File).ToList();
    }

    /// <summary>Small bounded synchronous scan used until the background index is ready.</summary>
    public List<FileEntry> QuickScan(string query, int max)
    {
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };

        var results = new List<(FileEntry File, int Score)>(16);
        foreach (var dir in dirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = System.IO.Path.GetFileName(file);
                    int s = Fuzzy.Score(query, name);
                    if (s > 0) results.Add((new FileEntry(name, file), s));
                    if (results.Count >= max * 6) break;
                }
            }
            catch { /* ignore */ }
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results.Take(max).Select(x => x.File).ToList();
    }
}
