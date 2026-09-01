using System;

using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 186. FindReplaceDialogSession subscribes to the editor's Changed event through a weakly
/// referencing closure so the short-lived dialog session is not pinned alive by the long-lived bus
/// (round-160 F2). That works -- but nothing ever removed the closure, so each time the user opened
/// Find and Replace another one accumulated on the bus, and every subsequent document change walked
/// them all. A handler whose target has been collected now removes itself on the next notification.
/// </summary>
public sealed class Round186_FindReplaceHandlerSelfUnsubscribeTests
{
    [Fact]
    public void DeadHandlersDropOffTheEventInsteadOfAccumulating()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));

        for (var i = 0; i < 25; i++)
            _ = new FindReplaceDialogSession(editor);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // First real Changed notification: every dead handler sees a collected target and
        // unsubscribes itself. Selection changes do not raise Changed, so execute a command.
        editor.Bus.Execute(new InsertSlideCommand(0, new Slide()));

        // Not an exact count: the GC may legitimately still be holding the most recently created
        // session or two. What must not survive is 25 of them.
        HandlerCount(editor).Should().BeLessThan(
            5,
            "handlers whose session has been collected must leave the invocation list -- otherwise "
            + "opening the dialog repeatedly grows it without bound on a long-lived bus");
    }

    [Fact]
    public void ALiveSessionKeepsItsHandlerSubscribed()
    {
        // Sibling no-regression: self-removal must only happen once the target is gone, or the
        // dialog would stop being told about edits made outside it.
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var session = new FindReplaceDialogSession(editor);

        editor.Bus.Execute(new InsertSlideCommand(0, new Slide()));
        GC.KeepAlive(session);

        HandlerCount(editor).Should().Be(BaselineHandlers + 1);
    }

    /// <summary>EditingSession keeps one pass-through subscription of its own on the bus.</summary>
    private const int BaselineHandlers = 1;

    private static int HandlerCount(EditingSession editor)
    {
        var field = typeof(PresentationCommandBus).GetField(
            "Changed",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var handler = (Action?)field?.GetValue(editor.Bus);
        return handler?.GetInvocationList().Length ?? 0;
    }
}
