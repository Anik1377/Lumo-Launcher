namespace Lumo.Core;

/// <summary>
/// v3.0.0-alpha.7 hotfix — the press → drag → click decision for App Deck cards,
/// extracted as a pure state machine because a stale flag used to swallow clicks:
///
/// a drag-to-swap runs a modal OLE loop that CONSUMES the physical mouse release,
/// so MouseLeftButtonUp often never fires on the card. The old code latched
/// "a drag consumed the release" and only cleared it on an ASSIGNED card's next
/// press — so after one drag, every click on an EMPTY card (click-to-assign!)
/// was misread as the end of a drag and silently dropped. Assigning apps by
/// clicking appeared dead. The fix is structural: the latch clears itself on
/// every press (empty cards included) and on every drag completion, so no
/// sequence of gestures can poison the one after it.
///
/// Pure (no WPF) so the gesture table is unit-tested without a window.
/// </summary>
public sealed class DeckDragLatch
{
    private bool _pressed;      // a live press is awaiting its release verdict
    private bool _started;      // this press initiated an OLE drag
    private bool _suppressed;   // the drag machinery ran — the release is suspect

    /// <summary>True while this press's OLE drag is in flight — MouseMove re-entry
    /// (WPF pumps messages inside the modal drag loop) must not nest a second one.</summary>
    public bool InDrag => _started;

    /// <summary>A new press begins — arms the click verdict and clears every stale
    /// flag from the previous gesture.</summary>
    public void Press()
    {
        _pressed = true;
        _started = false;
        _suppressed = false;
    }

    /// <summary>The press crossed the system drag threshold — DoDragDrop now owns the mouse.</summary>
    public void DragStarted() => _started = true;

    /// <summary>
    /// DoDragDrop returned. The release it consumed must never surface as a click;
    /// if a MouseLeftButtonUp is delivered after the loop unwinds, it is a leftover,
    /// not a click. The next press starts clean either way.
    /// </summary>
    public void DragFinished()
    {
        _suppressed = true;
        _started = false;
    }

    /// <summary>
    /// A MouseLeftButtonUp arrived — is it a real click (run the card's action),
    /// or a leftover from a drag that consumed the release? Strict: a verdict
    /// requires a fresh press, and self-clears — one verdict per release.
    /// </summary>
    public bool IsClick()
    {
        bool click = _pressed && !_started && !_suppressed;
        _pressed = false;
        _started = false;
        _suppressed = false;
        return click;
    }
}
