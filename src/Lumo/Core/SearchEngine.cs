using System.Diagnostics;
using Lumo.Services;

namespace Lumo.Core;

/// <summary>A single row in the launcher result list.</summary>
public sealed class ResultItem
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Glyph { get; init; } = "·";
    public string RunArgument { get; init; } = "";   // file path / url / shell command / expression result
    public ResultKind Kind { get; init; }

    /// <summary>v1.3 — short label shown in the right-hand chip of a result row.</summary>
    public string KindLabel => Kind switch
    {
        ResultKind.App => "App",
        ResultKind.File => "File",
        ResultKind.Calculator => "=",
        ResultKind.Web => "Web",
        ResultKind.Image => "Image",
        ResultKind.Tool => "Tool",
        ResultKind.Hint => "Tip",
        ResultKind.Shortcut => "Shortcut",
        _ => "",
    };
}

public enum ResultKind
{
    App,
    File,
    Calculator,
    Web,
    Image,
    Tool,
    Hint,
    Error,
    Shortcut,   // v1.4 — user-defined /sc launch
}

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
    private readonly ShortcutStore? _shortcuts;   // v1.4

    public SearchEngine(AppIndex apps, FileIndex files, Settings settings, ShortcutStore? shortcuts = null)
    {
        _apps = apps; _files = files; _settings = settings; _shortcuts = shortcuts;
    }

    public List<ResultItem> Search(string rawQuery)
    {
        try
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
        catch (Exception ex)
        {
            Services.DiagnosticLogger.LogException("SearchEngine", ex);
            return new List<ResultItem>
            {
                new() { Title = "Search error (logged)", Subtitle = ex.Message, Glyph = "!", Kind = ResultKind.Error }
            };
        }
    }

    // ---------------------------------------------------------------- rows

    private List<ResultItem> DefaultRows()
    {
        var rows = new List<ResultItem>
        {
            new() { Title = "A/  Applications", Subtitle = "Type A/ then a name — e.g.  A/chrome", Glyph = "A", Kind = ResultKind.Hint, RunArgument = "A/" },
            new() { Title = "F/  Files", Subtitle = "Type F/ then part of a file name", Glyph = "F", Kind = ResultKind.Hint, RunArgument = "F/" },
            new() { Title = "C/  Calculator", Subtitle = "e.g.  C/(1920*1080)/3", Glyph = "C", Kind = ResultKind.Hint, RunArgument = "C/" },
            new() { Title = "W/  Web search", Subtitle = "e.g.  W/weather tomorrow", Glyph = "W", Kind = ResultKind.Hint, RunArgument = "W/" },
            new() { Title = "I/  Image search", Subtitle = "e.g.  I/mountain sunrise", Glyph = "I", Kind = ResultKind.Hint, RunArgument = "I/" },
            new() { Title = "U/  Utilities", Subtitle = "lock · sleep · restart · shutdown · empty bin", Glyph = "U", Kind = ResultKind.Hint, RunArgument = "U/" },
            new() { Title = "/sc  Shortcuts & macros", Subtitle = "your saved one-tap launches — e.g.  /sc work", Glyph = "⚡", Kind = ResultKind.Hint, RunArgument = "/sc" },
            new() { Title = "Settings — customize Lumo", Subtitle = "themes · accent colour · glow border · hotkey · index", Glyph = "⚙", Kind = ResultKind.Tool, RunArgument = "cmd:app-settings" },
        };

        // v1.4 — surface the most useful saved shortcuts right on the empty view
        if (_shortcuts is { Count: > 0 })
        {
            foreach (var (def, _) in _shortcuts.Match("", 3))
                rows.Add(ShortcutRow(def));
        }

        foreach (var app in _apps.Query("", 6))
            rows.Add(new ResultItem { Title = app.Name, Subtitle = "Application", Glyph = "A", Kind = ResultKind.App, RunArgument = app.Path });

        return rows;
    }

    // ---------------------------------------------------------------- shortcuts (v1.4)

    private static ResultItem ShortcutRow(ShortcutDef def) => new()
    {
        Title = def.Name,
        Subtitle = def.Describe(),
        Glyph = "⚡",
        Kind = ResultKind.Shortcut,
        RunArgument = def.Id,
    };

    private List<ResultItem> ShortcutRows(string q)
    {
        var rows = new List<ResultItem>();

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
            foreach (var app in _apps.Query("", MaxResults))
                rows.Add(new ResultItem { Title = app.Name, Subtitle = "Application", Glyph = "A", Kind = ResultKind.App, RunArgument = app.Path });
            return rows;
        }

        foreach (var app in _apps.Query(q, MaxResults))
            rows.Add(new ResultItem { Title = app.Name, Subtitle = "Application — " + app.Path, Glyph = "A", Kind = ResultKind.App, RunArgument = app.Path });

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
            ? _files.Query(q, MaxResults)
            : _files.QuickScan(q, MaxResults);

        if (!_files.Ready)
            rows.Add(new ResultItem { Title = "Building file index…", Subtitle = $"{_files.IndexedCount:N0} files found so far — showing quick scan results", Glyph = "…", Kind = ResultKind.Hint });

        foreach (var f in matches)
            rows.Add(new ResultItem { Title = f.Name, Subtitle = f.FullPath, Glyph = "F", Kind = ResultKind.File, RunArgument = f.FullPath });

        if (matches.Count == 0)
            rows.Add(new ResultItem { Title = $"No files matching \"{q}\"", Subtitle = _files.Ready ? "" : "index still building", Glyph = "?", Kind = ResultKind.Hint });

        return rows;
    }

    private List<ResultItem> CalcRows(string q)
    {
        var rows = new List<ResultItem>();
        if (q.Length == 0)
        {
            rows.Add(new ResultItem { Title = "Type an expression", Subtitle = "Supported: + - * / % ^ ( ) sqrt sin cos tan log ln pi e", Glyph = "C", Kind = ResultKind.Hint });
            return rows;
        }

        if (Calculator.TryEvaluate(q, out var value))
            rows.Add(new ResultItem { Title = value, Subtitle = $"{q}  —  press Enter to copy", Glyph = "=", Kind = ResultKind.Calculator, RunArgument = value });
        else
            rows.Add(new ResultItem { Title = "Not a valid expression", Subtitle = "e.g.  (1920*1080)/3   sqrt(2)^10   log(1000)", Glyph = "?", Kind = ResultKind.Hint });

        return rows;
    }

    private List<ResultItem> WebRows(string q)
    {
        if (q.Length == 0)
            return new() { new ResultItem { Title = "Type what to search on the web", Subtitle = "e.g.  W/dotnet 8 release notes   or a URL like W/example.com", Glyph = "W", Kind = ResultKind.Hint } };

        string arg = LooksLikeUrl(q) ? EnsureUrl(q) : SearchUrl(q);
        var label = LooksLikeUrl(q) ? "Open URL — press Enter" : "Search the web — press Enter";
        return new()
        {
            new ResultItem { Title = q, Subtitle = label, Glyph = "W", Kind = ResultKind.Web, RunArgument = arg }
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
        var tools = new List<ResultItem>
        {
            new() { Title = "Lock computer",       Subtitle = "Windows + L equivalent",                 Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:lock" },
            new() { Title = "Sleep computer",      Subtitle = "Standby",                                Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:sleep" },
            new() { Title = "Empty Recycle Bin",   Subtitle = "No confirmation",                        Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:emptybin" },
            new() { Title = "Restart computer",    Subtitle = "shutdown /r /t 0",                       Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:restart" },
            new() { Title = "Shut down computer",  Subtitle = "shutdown /s /t 0",                       Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:shutdown" },
            new() { Title = "Open settings window", Subtitle = "full customization UI (v1.3)",              Glyph = "⚙", Kind = ResultKind.Tool, RunArgument = "cmd:app-settings" },
            new() { Title = "Open settings file",  Subtitle = "edit settings.json directly",               Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:settings" },
            new() { Title = "Open diagnostics log",Subtitle = AppPaths.LogFile,                         Glyph = "U", Kind = ResultKind.Tool, RunArgument = "cmd:log" },
        };

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
        foreach (var app in _apps.Query(q, 8))
            rows.Add(new ResultItem { Title = app.Name, Subtitle = "Application", Glyph = "A", Kind = ResultKind.App, RunArgument = app.Path });

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
            foreach (var f in _files.Query(q, 6))
                rows.Add(new ResultItem { Title = f.Name, Subtitle = f.FullPath, Glyph = "F", Kind = ResultKind.File, RunArgument = f.FullPath });
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

    // ---------------------------------------------------------------- helpers

    public static bool IsExecutable(ResultItem item) =>
        item.Kind is ResultKind.App or ResultKind.File or ResultKind.Web or ResultKind.Image
            or ResultKind.Tool or ResultKind.Calculator or ResultKind.Shortcut;

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
                    break;

                case ResultKind.Web or ResultKind.Image:
                    OpenUrl(item.RunArgument);
                    break;

                case ResultKind.Calculator:
                    TrySetClipboard(item.RunArgument);
                    break;

                case ResultKind.Tool:
                    RunTool(item.RunArgument);
                    break;

                case ResultKind.Shortcut:
                    return RunShortcut(item.RunArgument) ?? null;
            }
            return null;
        }
        catch (Exception ex)
        {
            Services.DiagnosticLogger.LogException("SearchEngine.Execute", ex);
            return $"Couldn't open “{item.Title}” — {ex.InnerException?.Message ?? ex.Message}";
        }
    }

    private void RunTool(string arg)
    {
        switch (arg)
        {
            case "cmd:lock": Native.NativeMethods.LockWorkStation(); break;
            case "cmd:sleep": Native.NativeMethods.SleepComputer(); break;
            case "cmd:emptybin": Native.NativeMethods.EmptyRecycleBin(); break;
            case "cmd:restart": OpenCommand("shutdown", "/r /t 0"); break;
            case "cmd:shutdown": OpenCommand("shutdown", "/s /t 0"); break;
            case "cmd:settings": OpenPath(AppPaths.SettingsDir); break;
            case "cmd:log": OpenPath(AppPaths.DataDir); break;
        }
    }

    /// <summary>v1.4 — runs a saved shortcut/macro by id. Null = success.</summary>
    private string? RunShortcut(string id)
    {
        if (_shortcuts?.Find(id) is not { } def)
            return "Shortcut not found — it may have been deleted";

        try
        {
            if (def.IsMacro)
            {
                int launched = 0;
                foreach (var step in def.Steps.Where(s => !string.IsNullOrWhiteSpace(s)).Take(12))
                {
                    OpenAnyTarget(step.Trim());
                    launched++;
                }
                return launched == 0 ? "Macro has no valid steps — edit it in Settings → Shortcuts" : null;
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
