namespace Lumo.Core;

/// <summary>
/// v3.0.0-alpha.6 — the App Deck's app picker ranking, as a pure function.
///
/// The picker window lists every Start Menu / Desktop shortcut on the PC; this
/// class decides the ORDER. No query → the user's most-launched apps first
/// (UsageStore blend), then alphabetical. A query → the launcher's own fuzzy
/// score, boosted the same way. Pure so the ranking rules are unit-testable
/// without a window or a real Start Menu.
/// </summary>
public static class AppPicker
{
    /// <summary>Hard cap on rows handed to the UI for the no-query listing.</summary>
    public const int BrowseLimit = 400;

    public static List<AppEntry> Filter(
        IReadOnlyList<AppEntry> entries, string? query,
        Func<string, Services.UsageEntry?>? usage = null, int max = BrowseLimit)
    {
        if (entries.Count == 0) return [];
        max = Math.Clamp(max, 1, 2000);

        if (string.IsNullOrWhiteSpace(query))
        {
            // Browse mode: usage first (descending), then A→Z — the picker opens
            // on "the apps you actually launch", not a wall of alphabetical junk.
            return entries
                .OrderByDescending(e => usage?.Invoke(e.Path)?.Count ?? 0)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(max)
                .ToList();
        }

        var q = query.Trim();
        var scored = new List<(AppEntry App, int Score)>(entries.Count);
        foreach (var e in entries)
        {
            int s = Fuzzy.Score(q, e.Name);
            if (s <= 0) continue;
            if (usage is not null) s = Fuzzy.ScoreWithUsage(s, usage(e.Path));
            scored.Add((e, s));
        }
        scored.Sort((a, b) =>
        {
            int c = b.Score.CompareTo(a.Score);
            return c != 0 ? c : string.Compare(a.App.Name, b.App.Name, StringComparison.CurrentCultureIgnoreCase);
        });
        return scored.Take(max).Select(x => x.App).ToList();
    }
}
