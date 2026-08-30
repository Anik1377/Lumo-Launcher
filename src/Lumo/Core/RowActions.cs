namespace Lumo.Core;

/// <summary>
/// v2.2 (DEV_PLAN Task 2.1) — quick actions offered on a result row's context menu
/// (right-click, or Ctrl+→ from the keyboard).
/// </summary>
public enum RowAction
{
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
        RowAction.OpenContainingFolder => "Open containing folder",
        RowAction.CopyPath => "Copy path",
        RowAction.CopyName => "Copy name",
        RowAction.OpenTerminal => "Open in terminal",
        RowAction.RunAsAdmin => "Run as administrator",
        RowAction.Pin => "Pin to favourites",
        RowAction.Unpin => "Unpin from favourites",
        _ => a.ToString(),
    };

    private static readonly string[] ElevatedExtensions =
        { ".exe", ".lnk", ".bat", ".cmd", ".msc" };

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
                // folder itself → just open it; file → explorer /select
                list.Add(RowAction.OpenContainingFolder);
                list.Add(RowAction.CopyPath);
                list.Add(RowAction.CopyName);
                list.Add(RowAction.OpenTerminal);
                string ext = Path.GetExtension(arg);
                if (item.Kind == ResultKind.App &&
                    ElevatedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    list.Add(RowAction.RunAsAdmin);
                break;

            case ResultKind.Web or ResultKind.Image:
                // the "path" of a web row is its resolved URL
                list.Add(RowAction.CopyPath);
                list.Add(RowAction.CopyName);
                break;

            case ResultKind.Shortcut:
                // the RunArgument is the shortcut id, not a path — CopyPath would be noise
                list.Add(RowAction.CopyName);
                break;

            default:
                return list;   // headers, hints, errors, calculator, clipboard: no menu
        }

        if (hasPath || item.Kind is ResultKind.Web or ResultKind.Image or ResultKind.Shortcut)
            list.Add(pinned ? RowAction.Unpin : RowAction.Pin);
        return list;
    }
}
