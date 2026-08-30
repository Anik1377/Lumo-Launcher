namespace Lumo.Core;

/// <summary>A single row in the launcher result list.</summary>
/// <remarks>
/// v2.2.0-alpha.2 rework — moved out of SearchEngine.cs so the Phase 0 pure
/// test target (net8.0, no WPF) can compile it and exercise RowActions policy.
/// The only WPF-typed member (the shell icon) is compiled just for Windows.
/// </remarks>
public sealed class ResultItem
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Glyph { get; init; } = "·";
    public string RunArgument { get; init; } = "";   // file path / url / shell command / expression result
    public ResultKind Kind { get; init; }

#if LUMO_WPF
    /// <summary>v1.4.1 — real shell icon (app/logo/file type). Null → the row shows Glyph instead.</summary>
    public System.Windows.Media.ImageSource? Icon { get; init; }
#endif

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
        ResultKind.Clipboard => "Copy",   // v1.6
        ResultKind.Answer => "AI",        // v2.3 — ? answer row
        ResultKind.Header => "",          // v1.6 — section title, no chip
        _ => "",
    };

    // v2.2.0-alpha.2 rework — pin affordance data, stamped by SearchEngine.Annotate
    // for every row it returns. CanPin mirrors RowActions.Pinnable (the single
    // source of policy); Pinned reflects the favourites store right now. Both are
    // re-evaluated on every search, and pin/unpin triggers a fresh search — so the
    // hover star never shows stale state. (These two are the project's only mutable
    // row properties: they are stamped AFTER construction, post-hoc, so init-only
    // would force a full copy of every row on every search.)

    /// <summary>True when this row may be pinned (star shows on hover).</summary>
    public bool CanPin { get; set; }

    /// <summary>True when this row's RunArgument is currently pinned (star stays visible, filled).</summary>
    public bool Pinned { get; set; }

    /// <summary>
    /// v2.3.0-alpha.3 — optional payload forwarded to the action target (the AI chat
    /// row carries the typed question here so the chat window can auto-send it).
    /// Pure string, no WPF type — the test target compiles this file as-is.
    /// </summary>
    public string? ForwardText { get; init; }
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
    Clipboard,  // v1.6 — one clipboard-history entry (Enter copies it back)
    Answer,     // v2.3 — an AI answer for a ? query (Enter copies it)
    Header,     // v1.6 — section title row (Raycast "Favourites" style)
}
