using System.Diagnostics;
using Lumo.Services;

namespace Lumo.Core;

// v2.2.0-alpha.2 rework — ResultItem / ResultKind moved to Core/ResultItem.cs so
// the pure test target can compile them (and RowActions) without WPF.

/// <summary>
/// Central search pipeline. Everything it does is synchronous, in-memory and bounded;
/// any exception is caught, logged and surfaced as an error row — a text change can
/// never throw or block the UI thread.
/// </summary>
public sealed class SearchEngine
{
    private const int MaxResults = 24;

    private readonly AppIndex _apps;
    private readonly FileIndex _files;
    private readonly Settings _settings;
    private readonly ShortcutStore? _shortcuts;      // v1.4
    private readonly MacroRecorder? _recorder;       // v1.5
    private readonly ClipboardHistory? _clips;       // v1.6
    private readonly UsageStore? _usage;             // v2.1 — MRU ranking
    private readonly Favourites? _favs;              // v2.2 — pinned favourites
    private bool _restartPending;                    // v2.2 — a countdown restart is armed

    public SearchEngine(AppIndex apps, FileIndex files, Settings settings, ShortcutStore? shortcuts = null,
                        MacroRecorder? recorder = null, ClipboardHistory? clips = null, UsageStore? usage = null,
                        Favourites? favourites = null)
    {
        _apps = apps; _files = files; _settings = settings; _shortcuts = shortcuts; _recorder = recorder; _clips = clips;
        _usage = usage;
        _favs = favourites;
    }

    public List<ResultItem> Search(string rawQuery)
    {
        try
        {
            return Annotate(SearchCore(rawQuery));
        }
        catch (Exception ex)
        {
            Services.DiagnosticLogger.LogException("SearchEngine", ex);
            return new List<ResultItem>
            {
                new() { Title = "Search error (logged)", Subtitle = ex.Message, Glyph = "!", Kind = ResultKind.Error }
            };
        }
    }

    /// <summary>
    /// v2.2.0-alpha.2 rework — stamps every returned row with its pin state in ONE
    /// place, so the hover star (CanPin/Pinned) and the right-click menu can never
    /// disagree about what a row offers. Rows are re-annotated on every search, and
    /// pin/unpin triggers a fresh search, so the state is always current.
    /// </summary>
    private List<ResultItem> Annotate(List<ResultItem> rows)
    {
        foreach (var r in rows)
        {
            bool pinnable = RowActions.Pinnable(r);
            r.CanPin = pinnable;
            r.Pinned = pinnable && _favs is { } f && f.IsPinned(r.RunArgument);
        }
        return rows;
    }

    private List<ResultItem> SearchCore(string rawQuery)
    {
        var query = (rawQuery ?? string.Empty).Trim();
        if (query.Length == 0) return DefaultRows();

        if (query.StartsWith("A/", StringComparison.OrdinalIgnoreCase))
            return AppRows(query[2..].Trim());

        if (query.StartsWith("F/", StringComparison.OrdinalIgnoreCase))
            return FileRows(query[2..].Trim());

        if (query.StartsWith("C/", StringComparison.OrdinalIgnoreCase))
            return CalcRows(query[2..].Trim());

        if (query.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            return WebRows(query[2..].Trim());

        if (query.StartsWith("I/", StringComparison.OrdinalIgnoreCase))
            return ImageRows(query[2..].Trim());

        if (query.StartsWith("U/", StringComparison.OrdinalIgnoreCase))
            return ToolRows(query[2..].Trim());

        // v1.6 — H/ : Raycast-style clipboard history
        if (query.StartsWith("H/", StringComparison.OrdinalIgnoreCase))
            return ClipboardRows(query[2..].Trim());

        // v1.6 — S/ : Raycast-style window management (snap / maximize / center)
        if (query.StartsWith("S/", StringComparison.OrdinalIgnoreCase))
            return WindowRows(query[2..].Trim());

        // v1.6 — !keyword : snippet search (type ! and part of a snippet name)
        if (query.StartsWith("!"))
            return SnippetRows(query[1..].Trim());

        // v1.4 — /sc <name> : user shortcuts & macros. Any "/…" query enters
        // shortcut mode; a leading "sc" token is optional and stripped.
        if (query.StartsWith("/"))
        {
            string rest = query[1..].Trim();
            if (rest.Equals("sc", StringComparison.OrdinalIgnoreCase)) rest = "";
            else if (rest.StartsWith("sc ", StringComparison.OrdinalIgnoreCase)) rest = rest[3..].Trim();
            return ShortcutRows(rest);
        }

        // Default view: apps + tools matching, plus top files.
        return MixedRows(query);
    }

    // ---------------------------------------------------------------- rows

    private List<ResultItem> DefaultRows()
    {
        var rows = new List<ResultItem>();

        // v2.2.0-alpha.2 rework (DEV_PLAN Task 2.2) — pinned favourites lead the empty
        // view, newest pin FIRST: the thing you just pinned is the thing you most
        // likely want next, and a fresh pin visibly lands at the top of the section.
        // Up to 12 rows are shown (was a silent 6 — pinning more made rows vanish).
        // Row data comes from the store; the shell icon is refreshed live for app/file
        // rows, and Annotate() stamps CanPin/Pinned so the hover star appears immediately.
        if (_favs is { Count: > 0 })
        {
            rows.Add(new ResultItem { Title = "FAVOURITES", Subtitle = "pinned — hover a row for its ★, right-click for quick actions", Kind = ResultKind.Header });
            int shown = 0;
            foreach (var f in _favs.DisplaySnapshot())
            {
                if (shown >= 12) break;   // header + up to 12 favourites
                shown++;
                var kind = Enum.TryParse<ResultKind>(f.Kind, out var k) ? k : ResultKind.Tool;
                rows.Add(new ResultItem
                {
                    Title = f.Title,
                    Subtitle = f.Subtitle,
                    Glyph = string.IsNullOrWhiteSpace(f.Glyph) ? "★" : f.Glyph,
                    Kind = kind,
                    RunArgument = f.Key,
                    Icon = kind is ResultKind.App or ResultKind.File ? Services.AppIcons.ForPath(f.Key) : null,
                });
            }
        }

        rows.AddRange(new List<ResultItem>
        {
            new() { Title = "A/  Applications", Subtitle = "Type A/ then a name — e.g.  A/chrome", Glyph = "A", Kind = ResultKind.Hint, RunArgument = "A/" },
            new() { Title = "F/  Files", Subtitle = "Type F/ then part of a file name", Glyph = "F", Kind = ResultKind.Hint, RunArgument = "F/" },
            new() { Title = "C/  Calculator", Subtitle = "e.g.  C/(1920*1080)/3", Glyph = "C", Kind = ResultKind.Hint, RunArgument = "C/" },
            new() { Title = "W/  Web search", Subtitle = "e.g.  W/weather tomorrow — or switch per query: W/github · W/youtube · W/ddg · W/wiki", Glyph = "W", Kind = ResultKind.Hint, RunArgument = "W/" },
            new() { Title = "I/  Image search", Subtitle = "e.g.  I/mountain sunrise", Glyph = "I", Kind = ResultKind.Hint, RunArgument = "I/" },
            new() { Title = "U/  Utilities", Subtitle = "lock · sleep · hibernate · mute · restart · shutdown · bin · night light", Glyph = "U", Kind = ResultKind.Hint, RunArgument = "U/" },
            new() { Title = "H/  Clipboard history", Subtitle = "everything you copied — pick one to copy again (Raycast style)", Glyph = "⧉", Kind = ResultKind.Hint, RunArgument = "H/" },
            new() { Title = "S/  Snap window", Subtitle = "left/right half · maximize · center · restore — for the last window you used", Glyph = "▣", Kind = ResultKind.Hint, RunArgument = "S/" },
            new() { Title = "!  Snippets", Subtitle = "type ! then a name — your paste-anywhere texts, e.g.  !email", Glyph = "S", Kind = ResultKind.Hint, RunArgument = "!" },
            new() { Title = "/sc  Shortcuts & macros", Subtitle = "your saved one-tap launches — e.g.  /sc work", Glyph = "⚡", Kind = ResultKind.Hint, RunArgument = "/sc" },
            new() { Title = "Settings — customize Lumo", Subtitle = "themes · accent colour · glow border · hotkey · index", Glyph = "⚙", Kind = ResultKind.Tool, RunArgument = "cmd:app-settings" },
        });

        // v1.5 — record a macro straight from the empty view
        AddRecordRows(rows, "");

        // v1.4 — surface the most useful saved shortcuts right on the empty view
        if (_shortcuts is { Count: > 0 })
        {
            foreach (var (def, _) in _shortcuts.Match("", 3))
                rows.Add(ShortcutRow(def));
        }

        foreach (var app in _apps.Query("", 6))
            rows.Add(new ResultItem { Title = app.Name, Subtitle = "Application", Glyph = "A", Kind = ResultKind.App, RunArgument = app.Path, Icon = Services.AppIcons.ForPath(app.Path) });

        return rows;
    }

    // ---------------------------------------------------------------- shortcuts (v1.4)

    /// <summary>v1.5 — record rows for the default and /sc views.</summary>
    private void AddRecordRows(List<ResultItem> rows, string q)
    {
        if (_recorder is null) return;
        if (_recorder.Active)
        {
            int n = _recorder.Count;
            // v1.5.1 — guidance banner first: what recording does and what to do next
            rows.Add(new ResultItem
            {
                Title = "● Recording — type to open an app, file or URL",
                Subtitle = "every launch through Lumo is captured as a step of your macro",
                Glyph = "⏺", Kind = ResultKind.Hint,
            });
            rows.Add(new ResultItem
            {
                Title = $"⏺ Stop & save ({n} step{(n == 1 ? "" : "s")})",
                Subtitle = "open the builder with everything you just launched",
                Glyph = "⏺", Kind = ResultKind.Tool, RunArgument = "cmd:record-stop",
            });
            rows.Add(new ResultItem
            {
                Title = "✕ Cancel recording",
                Subtitle = "throw the captured steps away",
                Glyph = "✕", Kind = ResultKind.Tool, RunArgument = "cmd:record-cancel",
            });
        }
        else
        {
            rows.Add(new ResultItem
            {
                Title = "⏺ Record a macro",
                Subtitle = "launch things in Lumo — they become the steps (Apple-Shortcuts style)",
                Glyph = "⏺", Kind = ResultKind.Tool, RunArgument = "cmd:record-macro",
            });
        }
    }

    private static ResultItem ShortcutRow(ShortcutDef def) => new()
    {
        Title = def.Name,
        Subtitle = def.Describe(),
        Glyph = def.IsSnippet ? "S" : "⚡",
        Kind = ResultKind.Shortcut,
        RunArgument = def.Id,
        Icon = def.IsMacro || def.IsSnippet ? null : Services.AppIcons.ForPath(def.Target), // real icon for file/folder targets
    };

    private List<ResultItem> ShortcutRows(string q)
    {
        var rows = new List<ResultItem>();

        // v1.5 — recording controls on top
        AddRecordRows(rows, q);

        // management rows always available
        rows.Add(string.IsNullOrWhiteSpace(q)
            ? new ResultItem { Title = "New shortcut", Subtitle = "save a URL, file, folder or a multi-step macro", Glyph = "＋", Kind = ResultKind.Tool, RunArgument = "cmd:new-shortcut" }
            : new ResultItem { Title = $"Create shortcut “{q}”", Subtitle = "save this name as a new one-tap shortcut", Glyph = "＋", Kind = ResultKind.Tool, RunArgument = "cmd:new-shortcut:" + q });
        rows.Add(new ResultItem { Title = "Manage shortcuts", Subtitle = "edit or delete in the settings window", Glyph = "⚙", Kind = ResultKind.Tool, RunArgument = "cmd:manage-shortcuts" });

        if (_shortcuts is not null)
        {
            foreach (var (def, _) in _shortcuts.Match(q, MaxResults - rows.Count))
                rows.Add(ShortcutRow(def));
        }

        if (q.Length == 0 && (_shortcuts is null || _shortcuts.Count == 0))
            rows.Add(new ResultItem
            {
                Title = "No shortcuts yet",
                Subtitle = "Press Enter on “New shortcut” — then run it any time with /sc name",
                Glyph = "⚡", Kind = ResultKind.Hint,
            });

        return rows;
    }

    private List<ResultItem> AppRows(string q)
    {
        var rows = new List<ResultItem>();
        if (q.Length == 0)
        {
            foreach (var app in _apps.Query("", MaxResults, _usage))
                rows.Add(new ResultItem { Title = app.Name, Subtitle = "Application", Glyph = "A", Kind = ResultKind.App, RunArgument = app.Path, Icon = Services.AppIcons.ForPath(app.Path) });
            return rows;
        }

        foreach (var app in _apps.Query(q, MaxResults, _usage))
            rows.Add(new ResultItem { Title = app.Name, Subtitle = "Application — " + app.Path, Glyph = "A", Kind = ResultKind.App, RunArgument = app.Path, Icon = Services.AppIcons.ForPath(app.Path) });

        if (rows.Count == 0)
            rows.Add(new ResultItem { Title = $"No apps matching \"{q}\"", Subtitle = "Tip: use F/ to search files instead", Glyph = "?", Kind = ResultKind.Hint });
        return rows;
    }

    private List<ResultItem> FileRows(string q)
    {
        var rows = new List<ResultItem>();
        if (q.Length == 0)
        {
            rows.Add(new ResultItem { Title = "Type part of a file name", Subtitle = $"Index: {_files.IndexedCount:N0} files — " + (_files.Ready ? "ready" : "building…"), Glyph = "F", Kind = ResultKind.Hint });
            return rows;
        }

        IReadOnlyList<FileEntry> matches = _files.Ready
            ? _files.Query(q, MaxResults, _usage)
            : _files.QuickScan(q, MaxResults);

        if (!_files.Ready)
            rows.Add(new ResultItem { Title = "Building file index…", Subtitle = $"{_files.IndexedCount:N0} files found so far — showing quick scan results", Glyph = "…", Kind = ResultKind.Hint });

        foreach (var f in matches)
            rows.Add(new ResultItem { Title = f.Name, Subtitle = f.FullPath, Glyph = "F", Kind = ResultKind.File, RunArgument = f.FullPath, Icon = Services.AppIcons.ForPath(f.FullPath) });

        if (matches.Count == 0)
            rows.Add(new ResultItem { Title = $"No files matching \"{q}\"", Subtitle = _files.Ready ? "" : "index still building", Glyph = "?", Kind = ResultKind.Hint });

        return rows;
    }

    private List<ResultItem> CalcRows(string q)
    {
        var rows = new List<ResultItem>();
        if (q.Length == 0)
        {
            rows.Add(new ResultItem { Title = "Type an expression", Subtitle = "Supported: + - * / % ^ ( ) sqrt sin cos tan log ln pi e — and units: 10 ft in cm, 50 usd to eur", Glyph = "C", Kind = ResultKind.Hint });
            return rows;
        }

        // v2.1 (DEV_PLAN Task 1.2) — inline unit + currency conversion first:
        // "10 ft in cm", "5kg in lbs", "50 usd to eur" — pure, synchronous, no I/O.
        if (UnitConverter.TryConvert(q, out var converted))
        {
            rows.Add(new ResultItem { Title = converted, Subtitle = $"{q}  —  press Enter to copy", Glyph = "=", Kind = ResultKind.Calculator, RunArgument = converted });
            return rows;
        }

        if (Calculator.TryEvaluate(q, out var value))
            rows.Add(new ResultItem { Title = value, Subtitle = $"{q}  —  press Enter to copy", Glyph = "=", Kind = ResultKind.Calculator, RunArgument = value });
        else
            rows.Add(new ResultItem { Title = "Not a valid expression or unit conversion", Subtitle = "e.g.  (1920*1080)/3   sqrt(2)^10   10 ft in cm   50 usd to eur", Glyph = "?", Kind = ResultKind.Hint });

        return rows;
    }

    private List<ResultItem> WebRows(string q)
    {
        if (q.Length == 0)
            return new() { new ResultItem { Title = "Type what to search on the web", Subtitle = "e.g.  W/dotnet 8 release notes · W/github lumo · W/youtube cats · or a URL like W/example.com", Glyph = "W", Kind = ResultKind.Hint } };

        // v2.1 (DEV_PLAN Task 1.3) — per-query provider quick-switch: "W/github dotnet"
        // searches GitHub, "W/youtube cats" searches YouTube; the default engine wins
        // for everything else (raw URLs keep the open-directly behaviour).
        if (WebProviders.TryResolve(q, _settings.CustomWebProviders, out var purl, out var pkey, out var prest))
        {
            string label = char.ToUpperInvariant(pkey[0]) + pkey[1..] + " — press Enter";
            return new()
            {
                new ResultItem { Title = prest, Subtitle = label, Glyph = "W", Kind = ResultKind.Web, RunArgument = purl }
            };
        }

        string arg = LooksLikeUrl(q) ? EnsureUrl(q) : SearchUrl(q);
        var defaultLabel = LooksLikeUrl(q) ? "Open URL — press Enter" : "Search the web — press Enter";
        return new()
        {
            new ResultItem { Title = q, Subtitle = defaultLabel, Glyph = "W", Kind = ResultKind.Web, RunArgument = arg }
        };
    }

    private List<ResultItem> ImageRows(string q)
    {
        if (q.Length == 0)
            return new() { new ResultItem { Title = "Type what images to search", Subtitle = "e.g.  I/aurora borealis", Glyph = "I", Kind = ResultKind.Hint } };

        return new()
        {
            new ResultItem { Title = q, Subtitle = "Search images — press Enter", Glyph = "I", Kind = ResultKind.Image,
                             RunArgument = $"https://www.google.com/search?tbm=isch&q={Uri.EscapeDataString(q)}" }
        };
    }

    private List<ResultItem> ToolRows(string q)
    {
        var tools = new List<ResultItem>();

        // v2.2 — while a countdown restart is armed, the abort row leads the list
        if (_restartPending)
            tools.Add(new() { Title = "✕ Cancel pending restart", Subtitle = "shutdown /a — aborts the 10-second countdown", Glyph = "✕", Kind = ResultKind.Tool, RunArgument = "cmd:restart-cancel" });

        tools.AddRange(new List<ResultItem>
        {
            new() { Title = "Lock computer",       Subtitle = "Windows + L equivalent",                 Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:lock" },
            new() { Title = "Sleep computer",      Subtitle = "Standby",                                Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:sleep" },
            new() { Title = "Hibernate computer",  Subtitle = "session saved to disk, then powers off", Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:hibernate" },
            new() { Title = "Mute / unmute volume", Subtitle = "toggles the system volume",              Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:mute" },
            new() { Title = "Night light settings", Subtitle = "opens Windows › night light",            Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:nightlight" },
            new() { Title = "Battery settings",     Subtitle = "opens Windows › battery saver",          Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:battery" },
            new() { Title = "Empty Recycle Bin",   Subtitle = "No confirmation",                        Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:emptybin" },
            new() { Title = "Restart computer",    Subtitle = "shutdown /r /t 0",                       Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:restart" },
            new() { Title = "Restart in 10 seconds", Subtitle = "shutdown /r /t 10 — search “cancel” to abort", Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:restart-countdown" },
            new() { Title = "Shut down computer",  Subtitle = "shutdown /s /t 0",                       Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:shutdown" },
            new() { Title = "Open settings window", Subtitle = "full customization UI (v1.3)",              Glyph = "⚙", Kind = ResultKind.Tool, RunArgument = "cmd:app-settings" },
            new() { Title = "Open settings file",  Subtitle = "edit settings.json directly",               Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:settings" },
            new() { Title = "Open diagnostics log",Subtitle = AppPaths.LogFile,                         Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:log" },
        });

        if (q.Length == 0) return tools;
        var filtered = tools
            .Select(t => (T: t, S: Fuzzy.Score(q, t.Title)))
            .Where(x => x.S > 0)
            .OrderByDescending(x => x.S)
            .Select(x => x.T)
            .ToList();
        return filtered.Count > 0 ? filtered : tools;
    }

    private List<ResultItem> MixedRows(string q)
    {
        var rows = new List<ResultItem>();

        // v1.6 — Raycast-style section headers
        var apps = _apps.Query(q, 8, _usage).ToList();
        if (apps.Count > 0)
        {
            rows.Add(new ResultItem { Title = "APPS", Kind = ResultKind.Header });
            foreach (var app in apps)
                rows.Add(new ResultItem { Title = app.Name, Subtitle = "Application", Glyph = "A", Kind = ResultKind.App, RunArgument = app.Path, Icon = Services.AppIcons.ForPath(app.Path) });
        }

        if (Calculator.TryEvaluate(q, out var value))
            rows.Add(new ResultItem { Title = value, Subtitle = $"{q}  —  press Enter to copy", Glyph = "=", Kind = ResultKind.Calculator, RunArgument = value });

        // v1.4 — quick-hit shortcuts whose name starts with the query
        if (_shortcuts is not null)
        {
            foreach (var def in _shortcuts.PrefixMatches(q, 2))
                rows.Add(ShortcutRow(def));
        }

        if (_files.Ready)
        {
            var files = _files.Query(q, 6, _usage).ToList();
            if (files.Count > 0)
            {
                rows.Add(new ResultItem { Title = "FILES", Kind = ResultKind.Header });
                foreach (var f in files)
                    rows.Add(new ResultItem { Title = f.Name, Subtitle = f.FullPath, Glyph = "F", Kind = ResultKind.File, RunArgument = f.FullPath, Icon = Services.AppIcons.ForPath(f.FullPath) });
            }
        }

        rows.Add(new ResultItem
        {
            Title = $"Search the web for \"{q}\"",
            Subtitle = "Enter — or type W/ or I/ first",
            Glyph = "W",
            Kind = ResultKind.Web,
            RunArgument = SearchUrl(q),
        });

        return rows.Take(MaxResults).ToList();
    }

    // ---------------------------------------------------------------- v1.6 — clipboard / windows / snippets

    private static string Age(DateTime at)
    {
        var d = DateTime.Now - at;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours}h ago";
        return $"{(int)d.TotalDays}d ago";
    }

    private List<ResultItem> ClipboardRows(string q)
    {
        var rows = new List<ResultItem>
        {
            new() { Title = "CLIPBOARD HISTORY", Subtitle = "in memory only — cleared when Lumo exits", Kind = ResultKind.Header },
        };

        var items = _clips?.Snapshot() ?? new List<ClipboardHistory.Entry>();
        int shown = 0;
        foreach (var e in items)
        {
            string first = e.Text.Replace("\r", "").Split('\n', 2)[0];
            if (first.Length > 96) first = first[..96] + "…";
            if (q.Length > 0 && !first.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !e.Text.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;

            int lines = e.Text.Count(c => c == '\n') + 1;
            string sub = $"copied {Age(e.At)} · {e.Text.Length:N0} chars" + (lines > 1 ? $" · {lines} lines" : "") + " · Enter to copy again";
            rows.Add(new ResultItem { Title = first, Subtitle = sub, Glyph = "⧉", Kind = ResultKind.Clipboard, RunArgument = e.Id });
            if (++shown >= MaxResults) break;
        }

        if (shown == 0)
            rows.Add(new ResultItem
            {
                Title = q.Length == 0 ? "Nothing copied yet" : $"No copies matching \"{q}\"",
                Subtitle = "Lumo remembers your last 50 copies while it runs",
                Glyph = "⧉", Kind = ResultKind.Hint,
            });

        rows.Add(new ResultItem { Title = "Clear clipboard history", Subtitle = "forget everything remembered so far", Glyph = "✕", Kind = ResultKind.Tool, RunArgument = "cmd:clear-clipboard" });
        return rows;
    }

    private List<ResultItem> WindowRows(string q)
    {
        var rows = new List<ResultItem>
        {
            new() { Title = "WINDOW MANAGEMENT", Subtitle = "snaps the window you used right before opening Lumo", Kind = ResultKind.Header },
            new() { Title = "Left half",  Subtitle = "snap to the left half of the screen",  Glyph = "▣", Kind = ResultKind.Tool, RunArgument = "cmd:win-left" },
            new() { Title = "Right half", Subtitle = "snap to the right half of the screen", Glyph = "▣", Kind = ResultKind.Tool, RunArgument = "cmd:win-right" },
            new() { Title = "Maximize",    Subtitle = "fill the whole screen",               Glyph = "▣", Kind = ResultKind.Tool, RunArgument = "cmd:win-max" },
            new() { Title = "Center",      Subtitle = "center the window on its monitor",    Glyph = "▣", Kind = ResultKind.Tool, RunArgument = "cmd:win-center" },
            new() { Title = "Restore down", Subtitle = "back to its normal size",            Glyph = "▣", Kind = ResultKind.Tool, RunArgument = "cmd:win-restore" },
        };

        if (q.Length == 0) return rows;
        var filtered = rows
            .Where(r => r.Kind != ResultKind.Header)
            .Select(r => (R: r, S: Fuzzy.Score(q, r.Title)))
            .Where(x => x.S > 0)
            .OrderByDescending(x => x.S)
            .Select(x => x.R)
            .ToList();
        return filtered.Count > 0 ? filtered : rows.Skip(1).ToList();
    }

    private List<ResultItem> SnippetRows(string q)
    {
        var rows = new List<ResultItem>
        {
            new() { Title = "SNIPPETS", Subtitle = "Enter copies the text — then Ctrl+V pastes it anywhere", Kind = ResultKind.Header },
        };

        var found = _shortcuts?.Match(q, MaxResults - 1).Where(x => x.Def.IsSnippet).ToList()
                    ?? new List<(ShortcutDef, int)>();
        foreach (var (def, _) in found)
        {
            string preview = def.Target.Replace("\r", "").Split('\n', 2)[0];
            if (preview.Length > 90) preview = preview[..90] + "…";
            rows.Add(new ResultItem { Title = def.Name, Subtitle = preview, Glyph = "S", Kind = ResultKind.Shortcut, RunArgument = def.Id });
        }

        if (found.Count == 0)
            rows.Add(new ResultItem
            {
                Title = q.Length == 0 ? "No snippets yet" : $"No snippets matching \"{q}\"",
                Subtitle = "Create one: /  → New shortcut → type “Snippet” — e.g. !email for your address",
                Glyph = "S", Kind = ResultKind.Hint,
            });
        return rows;
    }

    // ---------------------------------------------------------------- helpers

    public static bool IsExecutable(ResultItem item) =>
        item.Kind is ResultKind.App or ResultKind.File or ResultKind.Web or ResultKind.Image
            or ResultKind.Tool or ResultKind.Calculator or ResultKind.Shortcut or ResultKind.Clipboard;

    /// <summary>
    /// Runs the item. Returns null on success, or a short human-readable
    /// failure message so the UI can show feedback instead of failing silently.
    /// </summary>
    public string? Execute(ResultItem item)
    {
        try
        {
            switch (item.Kind)
            {
                case ResultKind.App or ResultKind.File:
                    OpenPath(item.RunArgument);
                    _recorder?.Capture(item);           // v1.5 — feed the macro recorder
                    RecordUsage(item.RunArgument);      // v2.1 — feed the MRU store
                    break;

                case ResultKind.Web or ResultKind.Image:
                    OpenUrl(item.RunArgument);
                    if (item.Kind == ResultKind.Web) _recorder?.Capture(item);   // v1.5
                    RecordUsage(item.RunArgument);      // v2.1 — the resolved URL is the key
                    break;

                case ResultKind.Calculator:
                    TrySetClipboard(item.RunArgument);
                    break;

                case ResultKind.Clipboard:   // v1.6 — copy the entry back to the clipboard
                    if (_clips?.Find(item.RunArgument) is { } e) _clips.Restore(e);
                    break;

                case ResultKind.Tool:
                    return RunTool(item.RunArgument);

                case ResultKind.Shortcut:
                    string? shortcutError = RunShortcut(item.RunArgument);
                    if (shortcutError is null && _shortcuts?.Find(item.RunArgument) is { } ran && !ran.IsSnippet)
                        RecordUsage(ran.Target);        // v2.1 — shortcut launches count towards their target
                    return shortcutError;
            }
            return null;
        }
        catch (Exception ex)
        {
            Services.DiagnosticLogger.LogException("SearchEngine.Execute", ex);
            return $"Couldn't open “{item.Title}” — {ex.InnerException?.Message ?? ex.Message}";
        }
    }

    /// <summary>v2.1 (DEV_PLAN Task 1.1) — usage recording is fire-and-forget: the
    /// store bumps its counter and persists on a background thread, so the launch
    /// path never touches the disk.</summary>
    private void RecordUsage(string? key) => _usage?.Record(key);

    private string? RunTool(string arg)
    {
        switch (arg)
        {
            case "cmd:lock": Native.NativeMethods.LockWorkStation(); break;
            case "cmd:sleep": Native.NativeMethods.SleepComputer(); break;
            case "cmd:emptybin": Native.NativeMethods.EmptyRecycleBin(); break;
            case "cmd:restart": OpenCommand("shutdown", "/r /t 0"); break;
            case "cmd:shutdown": OpenCommand("shutdown", "/s /t 0"); break;

            // v2.2 (DEV_PLAN Task 2.4) — more system utilities
            case "cmd:hibernate": Native.NativeMethods.HibernateComputer(); break;
            case "cmd:mute": Native.NativeMethods.ToggleMute(); break;
            case "cmd:nightlight": OpenUrl("ms-settings:nightlight"); break;
            case "cmd:battery": OpenUrl("ms-settings:batterysaver"); break;
            case "cmd:restart-countdown": OpenCommand("shutdown", "/r /t 10"); _restartPending = true; break;
            case "cmd:restart-cancel": OpenCommand("shutdown", "/a"); _restartPending = false; break;
            case "cmd:settings": OpenPath(AppPaths.SettingsDir); break;
            case "cmd:log": OpenPath(AppPaths.DataDir); break;
            case "cmd:clear-clipboard": _clips?.Clear(); break;

            // v1.6 — window management; Apply returns null on success (→ launcher hides,
            // you see the snap happen) or an error (→ shown in the status bar)
            case "cmd:win-left": return WindowManager.Apply(WindowMode.Left);
            case "cmd:win-right": return WindowManager.Apply(WindowMode.Right);
            case "cmd:win-max": return WindowManager.Apply(WindowMode.Maximize);
            case "cmd:win-center": return WindowManager.Apply(WindowMode.Center);
            case "cmd:win-restore": return WindowManager.Apply(WindowMode.Restore);
        }
        return null;
    }

    /// <summary>v1.4 — runs a saved shortcut/macro by id. Null = success.</summary>
    private string? RunShortcut(string id)
    {
        if (_shortcuts?.Find(id) is not { } def)
            return "Shortcut not found — it may have been deleted";

        try
        {
            if (def.IsSnippet)
            {
                // v1.6 — snippets copy their text; Ctrl+V pastes it anywhere
                TrySetClipboard(def.Target);
                return null;
            }

            if (def.IsMacro)
            {
                var steps = MacroProgram.FromDef(def);
                string? invalid = MacroProgram.Validate(steps);
                if (invalid is not null)
                    return $"Macro “{def.Name}” — {invalid} (edit it in Settings → Shortcuts)";

                // waits / many launches must never freeze the launcher — run on a worker
                _ = Task.Run(() => RunMacroSteps(steps));
                return null;
            }

            OpenAnyTarget(def.Target);
            return null;
        }
        catch (Exception ex)
        {
            Services.DiagnosticLogger.LogException("SearchEngine.RunShortcut", ex);
            return $"Shortcut “{def.Name}” failed — {ex.InnerException?.Message ?? ex.Message}";
        }
    }

    // ---------------------------------------------------------------- macro engine (v1.5)

    /// <summary>
    /// Runs a parsed macro program on a worker thread. Each step is isolated:
    /// one failure is logged and the remaining steps still run. Clipboard steps
    /// hop to the UI thread (STA requirement).
    /// </summary>
    public static void RunMacroSteps(IReadOnlyList<MacroStep> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            try
            {
                switch (s.Type)
                {
                    case "wait":
                        Thread.Sleep(s.WaitMs);
                        break;

                    case "clip":
                        var disp = System.Windows.Application.Current?.Dispatcher;
                        if (disp is null) TrySetClipboard(s.Arg);
                        else disp.Invoke(() => TrySetClipboard(s.Arg));
                        break;

                    case "app":
                    case "file":
                    case "folder":
                    case "auto":
                    case "url":
                    default:
                        OpenAnyTarget(Environment.ExpandEnvironmentVariables(s.Arg));
                        break;
                }
            }
            catch (Exception ex)
            {
                Services.DiagnosticLogger.Log($"Macro step {i + 1}", $"{s.Type}:{s.Arg} → {ex.Message}");
            }
        }
    }

    /// <summary>v1.5 — lets the visual builder test-run the macro being edited.</summary>
    public static string? TestRunSteps(IReadOnlyList<MacroStep> steps)
    {
        string? invalid = MacroProgram.Validate(steps);
        if (invalid is not null) return invalid;
        _ = Task.Run(() => RunMacroSteps(steps));
        return null;
    }

    /// <summary>Opens a URL or a file/folder path, whichever the target looks like.</summary>
    private static void OpenAnyTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            (target.Contains('.') && !target.Contains('\\') && !target.Contains('/') && !File.Exists(target)))
        {
            OpenUrl(EnsureUrl(target));
        }
        else
        {
            OpenPath(Environment.ExpandEnvironmentVariables(target));
        }
    }

    private static void OpenCommand(string fileName, string args)
    {
        var psi = new ProcessStartInfo(fileName, args) { UseShellExecute = true, CreateNoWindow = true };
        Process.Start(psi);
    }

    private static void OpenPath(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "open",
        };
        try
        {
            Process.Start(psi);
        }
        catch (Exception)
        {
            // Some Start-Menu shortcuts (e.g. "WSL Settings.lnk") are special
            // packaged-app items ShellExecuteEx refuses to spawn directly.
            // Best effort: resolve the shortcut's real target and start that.
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && TryResolveShortcut(path, out var target))
            {
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
                return;
            }
            throw;
        }
    }

    /// <summary>Reads a .lnk's TargetPath via the Windows Script Host shell COM object.</summary>
    private static bool TryResolveShortcut(string lnkPath, out string target)
    {
        target = "";
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return false;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null) return false;
            try
            {
                dynamic lnk = shell.CreateShortcut(lnkPath);
                string tp = lnk.TargetPath as string ?? "";
                if (!string.IsNullOrWhiteSpace(tp) && File.Exists(tp)) { target = tp; return true; }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
        catch { /* best effort only */ }
        return false;
    }

    private static void OpenUrl(string url)
    {
        var psi = new ProcessStartInfo { FileName = url, UseShellExecute = true };
        Process.Start(psi);
    }

    private static void TrySetClipboard(string text)
    {
        try { System.Windows.Clipboard.SetText(text); } catch { /* clipboard can be locked */ }
    }

    internal string SearchUrl(string query)
    {
        var engine = (_settings.WebEngine ?? "google").ToLowerInvariant();
        return engine switch
        {
            "bing" => $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}",
            "duckduckgo" => $"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}",
            _ => $"https://www.google.com/search?q={Uri.EscapeDataString(query)}",
        };
    }

    private static bool LooksLikeUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        (s.Contains('.') && !s.Contains(' ') && s.EndsWith(".", StringComparison.OrdinalIgnoreCase) == false &&
         Uri.IsWellFormedUriString("https://" + s, UriKind.Absolute));

    private static string EnsureUrl(string s) =>
        s.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? s : "https://" + s;
}
