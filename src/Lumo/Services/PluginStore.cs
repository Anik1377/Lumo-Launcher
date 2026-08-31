using System.IO;
using Lumo.Core;

namespace Lumo.Services;

/// <summary>
/// v2.5 (DEV_PLAN Task 4.2) — discovers and owns the installed JSON plugins
/// (%APPDATA%\Lumo\plugins\&lt;id&gt;\plugin.json).
///
/// Doctrine (same as ChatStore/UsageStore/PersonaStore):
///  · tolerant — a broken plugin.json or a stray file is logged and skipped, never thrown;
///  · bounded  — MaxPlugins folders, ≤24 commands each, first plugin wins a keyword;
///  · private  — plugin folders live only in the user's own settings dir; logs carry
///    counts and ids, never command templates.
///
/// Freshness: EnsureFresh() stats the plugins dir ONCE per call (only the P/ route
/// calls it per keystroke) and rescans when its mtime changed. Edits INSIDE a plugin
/// folder don't move the parent's mtime — Settings → Plugins → Rescan covers that.
/// </summary>
public sealed class PluginStore
{
    /// <summary>Raised after a rescan that changed the catalog (UI reloads its list).</summary>
    public event Action? Changed;

    private readonly object _gate = new();
    private readonly List<PluginDefinition> _plugins = new();
    private readonly string _dir;
    private readonly Settings _settings;
    private DateTime _dirStamp;
    private bool _scannedOnce;

    public PluginStore(Settings settings, string? dir = null)
    {
        _settings = settings;
        _dir = dir ?? AppPaths.PluginsDir;
        Rescan();
    }

    // ------------------------------------------------------------------ reads

    /// <summary>All loaded plugins (enabled + disabled — the caller checks IsEnabled).</summary>
    public List<PluginDefinition> All()
    {
        lock (_gate) return _plugins.Select(p => p).ToList();
    }

    public int Count { get { lock (_gate) return _plugins.Count; } }

    public string DirectoryPath => _dir;

    public bool IsEnabled(string id) => !_settings.DisabledPlugins.Contains(id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Enabled plugins only — what search sees.</summary>
    public List<PluginDefinition> Enabled() =>
        All().Where(p => IsEnabled(p.Id)).ToList();

    /// <summary>Finds a command by (plugin id, keyword) among ENABLED plugins.</summary>
    public bool FindCommand(string pluginId, string keyword, out PluginDefinition def, out PluginCommand cmd)
    {
        def = null!; cmd = null!;
        if (!IsEnabled(pluginId)) return false;
        lock (_gate)
        {
            var d = _plugins.FirstOrDefault(p => p.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
            if (d is null) return false;
            var c = d.Commands.FirstOrDefault(c => c.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            if (c is null) return false;
            def = d; cmd = c;
            return true;
        }
    }

    /// <summary>
    /// Keyword routing for the search pipeline: "kw …" / bare "kw" when an enabled
    /// plugin owns the token (first registered keyword wins duplicates at scan time,
    /// so at most ONE plugin answers a token). Static routes (A/, W/, AI…) are
    /// checked by the caller BEFORE this — built-ins always win.
    /// </summary>
    public bool TryRoute(string query, out PluginDefinition def, out PluginCommand cmd, out string arg)
    {
        def = null!; cmd = null!; arg = "";
        lock (_gate)
        {
            foreach (var d in _plugins)
            {
                if (!IsEnabled(d.Id)) continue;
                foreach (var c in d.Commands)
                {
                    if (Plugins.KeywordRoutes(query, c.Keyword))
                    {
                        def = d; cmd = c;
                        arg = query.Length > c.Keyword.Length ? query[(c.Keyword.Length + 1)..].Trim() : "";
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>Keyword → row lookup used by Execute (arg included in the RunArgument after a space).</summary>
    public bool TryRouteExact(string runArgument, out PluginDefinition def, out PluginCommand cmd, out string arg)
    {
        def = null!; cmd = null!; arg = "";
        // RunArgument shape: "plugin:<id>:<keyword>[ <arg>]"
        if (!runArgument.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase)) return false;
        string rest = runArgument["plugin:".Length..];
        int sep = rest.IndexOf(':');
        if (sep <= 0) return false;
        string id = rest[..sep];
        string tail = rest[(sep + 1)..];
        int space = tail.IndexOf(' ');
        string keyword = space < 0 ? tail : tail[..space];
        arg = space < 0 ? "" : tail[(space + 1)..].Trim();
        return FindCommand(id, keyword, out def, out cmd);
    }

    // ------------------------------------------------------------------ writes

    public void SetEnabled(string id, bool enabled)
    {
        try
        {
            bool changed;
            var list = new List<string>(_settings.DisabledPlugins);
            if (enabled) changed = list.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
            else
            {
                changed = !list.Contains(id, StringComparer.OrdinalIgnoreCase);
                if (changed) list.Add(id);
            }
            if (!changed) return;
            _settings.DisabledPlugins = list;
            _settings.Save();
            Changed?.Invoke();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("PluginStore.SetEnabled", ex); }
    }

    // ------------------------------------------------------------------ scanning

    /// <summary>Force a rescan (startup, Settings → Rescan, P/ management row).</summary>
    public void Rescan()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            _dirStamp = SafeStamp(_dir);

            var found = new List<PluginDefinition>();
            var usedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirs = Directory.EnumerateDirectories(_dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var dir in dirs)
            {
                if (found.Count >= Plugins.MaxPlugins)
                {
                    DiagnosticLogger.Log("Plugins", $"Cap {Plugins.MaxPlugins} reached — ignoring the rest");
                    break;
                }

                string manifest = Path.Combine(dir, Plugins.ManifestFile);
                if (!File.Exists(manifest)) continue;

                try
                {
                    var fi = new FileInfo(manifest);
                    if (fi.Length > Plugins.MaxJsonBytes) { DiagnosticLogger.Log("Plugins", $"{dir} — manifest over {Plugins.MaxJsonBytes} B, skipped"); continue; }

                    if (!Plugins.TryParse(File.ReadAllText(manifest), Path.GetFileName(dir), out var def, out var error) || def is null)
                    {
                        DiagnosticLogger.Log("Plugins", $"{dir} — {error}");
                        continue;
                    }

                    // keyword dedupe: first plugin (alphabetical folder order) owns the token
                    var commands = new List<PluginCommand>();
                    foreach (var c in def.Commands)
                    {
                        if (usedKeywords.Add(c.Keyword)) commands.Add(c);
                        else DiagnosticLogger.Log("Plugins", $"{def.Id} — keyword '{c.Keyword}' already owned by another plugin, skipped");
                    }
                    if (commands.Count == 0) { DiagnosticLogger.Log("Plugins", $"{def.Id} — no unique keywords left, skipped"); continue; }

                    found.Add(def with { Commands = commands });
                }
                catch (Exception ex) { DiagnosticLogger.LogException("PluginStore.ScanEntry", ex); }
            }

            bool mutated;
            lock (_gate)
            {
                mutated = _plugins.Count != found.Count
                       || !_plugins.Select(p => (p.Id, p.Version, p.Commands.Count))
                                   .SequenceEqual(found.Select(p => (p.Id, p.Version, p.Commands.Count)));
                _plugins.Clear();
                _plugins.AddRange(found);
            }
            _scannedOnce = true;
            DiagnosticLogger.Log("Plugins", $"Scanned '{_dir}': {found.Count} plugin(s), {_settings.DisabledPlugins.Count} disabled");
            if (mutated) Changed?.Invoke();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("PluginStore.Rescan", ex); }
    }

    /// <summary>Cheap freshness probe — one stat; rescans only when the dir changed on disk.</summary>
    public void EnsureFresh()
    {
        if (!_scannedOnce) { Rescan(); return; }
        try
        {
            var stamp = SafeStamp(_dir);
            if (stamp != _dirStamp)
            {
                _dirStamp = stamp;
                Rescan();
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("PluginStore.EnsureFresh", ex); }
    }

    private DateTime SafeStamp(string path)
    {
        try { return Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : DateTime.MinValue; }
        catch { return DateTime.MinValue; }
    }
}
