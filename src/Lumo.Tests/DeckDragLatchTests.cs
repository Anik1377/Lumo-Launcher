using Lumo.Core;
using Xunit;

namespace Lumo.Tests;

/// <summary>
/// v3.0.0-alpha.7 — the App Deck card click/drag latch.
///
/// The regression: after ONE drag-to-swap, every click on an EMPTY card (the
/// click-to-assign gesture) was silently swallowed — the OLE drag loop consumed
/// the mouse release, the old "a drag consumed the release" flag was never
/// cleared by the drag's finally block, and an empty card's press didn't clear
/// it either (that line lived after the assigned-only early return). The user
/// reported it as "assigning apps by clicking is not working".
///
/// These tests pin the FULL gesture table: clean clicks, clicks after drags,
/// jittery presses that cross the drag threshold, real drags with and without
/// a delivered release, and the in-drag re-entry guard.
/// </summary>
public class DeckDragLatchTests
{
    [Fact]
    public void Clean_press_and_release_is_a_click()
    {
        var latch = new DeckDragLatch();
        latch.Press();
        Assert.True(latch.IsClick());
    }

    [Fact]
    public void IsClick_is_self_clearing_a_second_verdict_without_press_is_negative()
    {
        var latch = new DeckDragLatch();
        latch.Press();
        Assert.True(latch.IsClick());
        // a release without a press can never be a click
        Assert.False(latch.IsClick());
    }

    [Fact]
    public void Click_on_an_empty_card_after_a_drag_still_assigns_the_regression()
    {
        var latch = new DeckDragLatch();

        // gesture 1: drag-to-swap — press, threshold crossed, OLE loop consumes the release
        latch.Press();
        latch.DragStarted();
        latch.DragFinished();          // the finally block after DoDragDrop returns
        Assert.False(latch.IsClick()); // a late MouseUp, if delivered, is a leftover

        // gesture 2: the user clicks an EMPTY card to assign an app —
        // the press MUST clear every stale flag from gesture 1
        latch.Press();
        Assert.True(latch.IsClick());
    }

    [Fact]
    public void Jittery_click_that_crosses_the_drag_threshold_never_launches()
    {
        var latch = new DeckDragLatch();
        latch.Press();
        latch.DragStarted();           // 5 px of shake — the drag machinery fires
        latch.DragFinished();          // release ends the OLE loop
        Assert.False(latch.IsClick()); // no launch on a de-facto drag
        // and the NEXT press is clean — no brick carries over
        latch.Press();
        Assert.True(latch.IsClick());
    }

    [Fact]
    public void Real_drag_with_a_delivered_release_is_not_a_click()
    {
        var latch = new DeckDragLatch();
        latch.Press();
        latch.DragStarted();
        // some WPF versions deliver the up after DoDragDrop unwinds
        latch.DragFinished();
        Assert.False(latch.IsClick());
    }

    [Fact]
    public void Drag_finished_without_started_suppresses_nothing_extra()
    {
        var latch = new DeckDragLatch();
        latch.Press();
        latch.DragFinished();          // defensive: a finished without a started
        Assert.False(latch.IsClick()); // still not a click — the release is suspect
        latch.Press();
        Assert.True(latch.IsClick());
    }

    [Fact]
    public void Press_resets_everything_even_after_a_full_drag_cycle()
    {
        var latch = new DeckDragLatch();
        for (int i = 0; i < 5; i++)
        {
            latch.Press();
            latch.DragStarted();
            latch.DragFinished();
        }
        latch.Press();
        Assert.True(latch.IsClick());
    }

    [Fact]
    public void InDrag_guards_reentry_and_clears_on_finish()
    {
        var latch = new DeckDragLatch();
        Assert.False(latch.InDrag);
        latch.Press();
        Assert.False(latch.InDrag);
        latch.DragStarted();
        Assert.True(latch.InDrag);     // MouseMove re-entry inside the OLE loop sees this
        latch.DragFinished();
        Assert.False(latch.InDrag);    // a follow-up MouseMove must not nest a drag
    }

    [Fact]
    public void Mixed_sequence_launch_then_drag_then_assign_all_fire()
    {
        var latch = new DeckDragLatch();

        // launch slot 3 by click
        latch.Press();
        Assert.True(latch.IsClick());

        // drag slot 3 onto slot 5
        latch.Press();
        latch.DragStarted();
        latch.DragFinished();

        // click an empty slot to assign — the reported breakage, pinned green
        latch.Press();
        Assert.True(latch.IsClick());
    }
}
