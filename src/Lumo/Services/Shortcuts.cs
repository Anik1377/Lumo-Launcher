using System.IO;
using System.Text.Json;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// v1.4 — a user-defined shortcut ("macro"): a named one-tap launch.
/// Types: url (open a web page), file, folder, macro (open several things).
/// Invoked from the launcher by typing  /sc name  (or just / to browse).
/// </summary>
public sealed class ShortcutDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "";            // what the user types after /sc
    public string Type { get; set; } = "url";         // url | file | folder | macro
    public string Target { get; set; } = "";          // url / file path / folder path
    public List<string> Steps { get; set; } = new();  // macro: one target per step
    public string Keywords { get; set; } = "";        // extra match terms, comma separated

    public bool IsMacro => Type.Equals("macro", StringComparison.OrdinalIgnoreCase);

    /// <summary>v1.6 — a snippet holds text that Enter copies to the clipboard.</summary>
    public bool IsSnippet => Type.Equals("snippet", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bindable wrapper for lists.</summary>
    public string DescribeText => Describe();

    /// <summary>Short human description for result rows / settings list.</summary>
    public string Describe()
    {
        try
        {
            return Type.ToLowerInvariant() switch
            {
                "url" => "Opens " + Target,
                "file" => "Opens file — " + Target,
                "folder" => "Opens folder — " + Target,
                "macro" => $"Macro · {Steps.Count} step{(Steps.Count == 1 ? "" : "s")}",
                "snippet" => "Snippet — Enter copies the text",
                _ => Target,
            };
        }
        catch { return Target; }
    }
}

/// <summary>
/// Persists the user's shortcuts to %APPDATA%\Lumo\shortcuts.json.
/// All edits go through AddOrUpdate/Remove; Changed is raised so the
/// launcher result list and the settings page stay in sync live.
/// </summary>
public sealed class ShortcutStore
{
    private readonly object _gate = new();
    private List<ShortcutDef> _items = new();

    /// <summary>Raised after any mutation (already persisted).</summary>
    public event Action? Changed;

    public ShortcutStore() => Load();

    public List<ShortcutDef> Snapshot()
    {
        lock (_gate) return _items.Select(CloneDef).ToList();
    }

    public int Count { get { lock (_gate) return _items.Count; } }

    public ShortcutDef? Find(string id)
    {
        lock (_gate) return _items.FirstOrDefault(s => s.Id == id) is { } f ? CloneDef(f) : null;
    }

    public void AddOrUpdate(ShortcutDef def)
    {
        lock (_gate)
        {
            var i = _items.FindIndex(s => s.Id == def.Id);
            if (i >= 0) _items[i] = CloneDef(def); else _items.Add(CloneDef(def));
            Save();
        }
        Changed?.Invoke();
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _items.RemoveAll(s => s.Id == id) > 0;
            if (removed) Save();
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    /// <summary>Fuzzy-friendly listing: scores Name + Keywords against the query.</summary>
    public List<(ShortcutDef Def, int Score)> Match(string query, int max)
    {
        var list = new List<(ShortcutDef, int)>();
        lock (_gate)
        {
            foreach (var s in _items)
            {
                string haystack = s.Name + " " + s.Keywords;
                int score = Core.Fuzzy.Score(query, haystack);
                if (query.Length == 0 || score > 0) list.Add((CloneDef(s), score));
            }
        }
        return list.OrderByDescending(x => x.Item2).Take(max).ToList();
    }

    /// <summary>Shortcuts whose name starts with the query — used to surface quick hits in the default view.</summary>
    public List<ShortcutDef> PrefixMatches(string query, int max)
    {
        if (query.Length == 0) return new();
        lock (_gate)
            return _items.Where(s => s.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                         .Take(max).Select(CloneDef).ToList();
    }

    private static ShortcutDef CloneDef(ShortcutDef s) => new()
    {
        Id = s.Id, Name = s.Name, Type = s.Type, Target = s.Target,
        Steps = new List<string>(s.Steps ?? new List<string>()), Keywords = s.Keywords,
    };

    // ---------------------------------------------------------------- persistence

    private void Load()
    {
        try
        {
            if (File.Exists(AppPaths.ShortcutsFile))
            {
                var loaded = JsonSerializer.Deserialize<List<ShortcutDef>>(File.ReadAllText(AppPaths.ShortcutsFile));
                if (loaded is not null)
                {
                    lock (_gate) _items = loaded.Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList();
                    DiagnosticLogger.Log("Shortcuts", $"Loaded {_items.Count} shortcuts");
                }
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Shortcuts.Load", ex); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsDir);
            File.WriteAllText(AppPaths.ShortcutsFile,
                JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Shortcuts.Save", ex); }
    }
}
