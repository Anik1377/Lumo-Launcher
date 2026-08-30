using System.IO;

namespace Lumo.Core;

public sealed record AppEntry(string Name, string Path);

/// <summary>
/// Indexes Start Menu + Desktop shortcuts (.lnk / .url) on a background thread at startup.
/// Bounded (max 2000 entries) — cannot hang the UI, and the UI thread never touches it
/// until the published list is swapped in.
/// </summary>
public sealed class AppIndex
{
    private const int MaxEntries = 2000;

    private static readonly string[] Roots =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
    };

    private static readonly string[] Extensions = { ".lnk", ".url" };

    private volatile List<AppEntry> _entries = new();
    public IReadOnlyList<AppEntry> Entries => _entries;

    public void BeginIndexInBackground()
    {
        // Fire-and-forget; all exceptions are logged, never thrown.
        _ = Task.Run(() =>
        {
            try
            {
                var found = new List<AppEntry>(512);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var root in Roots)
                {
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                    var stack = new Stack<string>();
                    stack.Push(root);

                    while (stack.Count > 0 && found.Count < MaxEntries)
                    {
                        var dir = stack.Pop();
                        IEnumerable<string> files;
                        try { files = Directory.EnumerateFiles(dir); }
                        catch { continue; }

                        foreach (var file in files)
                        {
                            var ext = System.IO.Path.GetExtension(file);
                            if (!Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
                            var name = System.IO.Path.GetFileNameWithoutExtension(file);
                            if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                            found.Add(new AppEntry(name, file));
                            if (found.Count >= MaxEntries) break;
                        }

                        try
                        {
                            foreach (var sub in Directory.EnumerateDirectories(dir))
                                stack.Push(sub);
                        }
                        catch { /* access denied etc. */ }
                    }
                }

                _entries = found;
                Services.DiagnosticLogger.Log("AppIndex", $"Indexed {found.Count} app shortcuts");
            }
            catch (Exception ex) { Services.DiagnosticLogger.LogException("AppIndex", ex); }
        });
    }

    public List<AppEntry> Query(string query, int max, Services.UsageStore? usage = null)
    {
        var entries = _entries; // atomic reference read
        var scored = new List<(AppEntry App, int Score)>(entries.Count);
        foreach (var e in entries)
        {
            int s = Fuzzy.Score(query, e.Name);
            if (s <= 0) continue;
            // v2.1 — MRU blend: frequently-launched apps outrank never-launched equals.
            if (usage is not null) s = Fuzzy.ScoreWithUsage(s, usage.Get(e.Path));
            scored.Add((e, s));
        }
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored.Take(max).Select(x => x.App).ToList();
    }
}
