namespace Lumo.Core;

/// <summary>
/// v2.2 (DEV_PLAN Task 2.1) — quick actions offered on a result row's context menu
/// (right-click, or Ctrl+→ from the keyboard). v2.2.0-alpha.2 rework: the menu now
/// leads with the row's PRIMARY action (Open — exactly what Enter does), tool rows
/// are pinnable, and files can be elevated too, not just Start-Menu apps.
/// </summary>
public enum RowAction
{
    Open,                   // v2.2.0-alpha.2 — primary action, same as pressing Enter
    OpenContainingFolder,
    CopyPath,
    CopyName,
    OpenTerminal,
    RunAsAdmin,
    Pin,
    Unpin,
}

/// <summary>Builds the per-row action list and the human-readable menu labels.</summary>
public static class RowActions
{
    public static string Label(RowAction a) => a switch
    {
        RowAction.Open => "Open",
        RowAction.OpenContainingFolder => "Open containing folder",
        RowAction.CopyPath => "Copy path",
        RowAction.CopyName => "Copy name",
        RowAction.OpenTerminal => "Open in terminal",
        RowAction.RunAsAdmin => "Run as administrator",
        RowAction.Pin => "★ Pin to favourites",
        RowAction.Unpin => "☆ Unpin from favourites",
        _ => a.ToString(),
    };

    private static readonly string[] ElevatedExtensions =
        { ".exe", ".lnk", ".bat", ".cmd", ".msc" };

    /// <summary>
    /// The single source of pin policy (v2.2.0-alpha.2): these row kinds may be
    /// pinned, provided they carry a key and are not launcher-management commands.
    /// Tools (cmd:mute, cmd:app-settings, …) are deliberately pinnable — a utility
    /// you reach for constantly is the best possible favourite. Recording and
    /// shortcut-editor controls are transient UI state, never pinned.
    /// </summary>
    public static bool Pinnable(ResultItem? item)
    {
        if (item is null) return false;
        string arg = item.RunArgument ?? "";
        if (arg.Length == 0) return false;
        if (item.Kind is ResultKind.Header or ResultKind.Hint or ResultKind.Error
                      or ResultKind.Calculator or ResultKind.Clipboard) return false;
        if (arg.StartsWith("cmd:record", StringComparison.OrdinalIgnoreCase)) return false;
        if (arg.StartsWith("cmd:new-shortcut", StringComparison.OrdinalIgnoreCase)) return false;
        if (arg.StartsWith("cmd:manage-shortcuts", StringComparison.OrdinalIgnoreCase)) return false;
        if (arg.StartsWith("cmd:ai", StringComparison.OrdinalIgnoreCase)) return false;   // v2.3 — the "Ask …" row is transient UI state
        return item.Kind is ResultKind.App or ResultKind.File or ResultKind.Web
                        or ResultKind.Image or ResultKind.Tool or ResultKind.Shortcut
                        or ResultKind.Plugin;   // v2.5 — a plugin command with its arg survives the pin
    }

    /// <summary>
    /// The actions that make sense for this row, in menu order. Rows without a
    /// filesystem target (hints, headers, calculator, clipboard) return an empty
    /// list — the launcher shows no context menu for them.
    /// </summary>
    public static List<RowAction> For(ResultItem item, bool pinned)
    {
        var list = new List<RowAction>();
        if (item is null) return list;

        string arg = item.RunArgument ?? "";
        bool hasPath = arg.Length > 0 && !arg.StartsWith("cmd:", StringComparison.OrdinalIgnoreCase)
                       && item.Kind is not (ResultKind.Header or ResultKind.Hint or ResultKind.Error);

        switch (item.Kind)
        {
            case ResultKind.App or ResultKind.File:
                list.Add(RowAction.Open);
                // folder itself → just open it; file → explorer /select
                list.Add(RowAction.OpenContainingFolder);
                list.Add(RowAction.OpenTerminal);
                list.Add(RowAction.CopyPath);
                list.Add(RowAction.CopyName);
                // v2.2.0-alpha.2 — scripts and .msc snapped by the FILE index can be
                // elevated too; elevation is about the file type, not where it was found
                string ext = Path.GetExtension(arg);
                if (hasPath && ElevatedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    list.Add(RowAction.RunAsAdmin);
                break;

            case ResultKind.Web or ResultKind.Image:
                // the "path" of a web row is its resolved URL
                list.Add(RowAction.Open);
                list.Add(RowAction.CopyPath);
                list.Add(RowAction.CopyName);
                break;

            case ResultKind.Tool:
                // v2.2.0-alpha.2 — tools (cmd:* utilities, Settings) can now be opened
                // and PINNED from the menu; path actions are meaningless for them
                list.Add(RowAction.Open);
                break;

            case ResultKind.Shortcut:
                // the RunArgument is the shortcut id, not a path — CopyPath would be noise
                list.Add(RowAction.Open);
                list.Add(RowAction.CopyName);
                break;

            case ResultKind.Plugin:
                // v2.5 — RunArgument is "plugin:<id>:<keyword>[ <arg>]", not a path
                list.Add(RowAction.Open);
                list.Add(RowAction.CopyName);
                break;

            default:
                return list;   // headers, hints, errors, calculator, clipboard: no menu
        }

        if (Pinnable(item))
            list.Add(pinned ? RowAction.Unpin : RowAction.Pin);
        return list;
    }
}
