using FluentAssertions;

namespace FreeX.App.UI.Tests;

// Source-string regression coverage for the F-autofill-core review findings that live in the
// WPF-only GridView.Input.cs (GridView requires a live UI-thread control to drive real mouse
// events, so the established pattern in this test project -- see GridViewAutofillTests.cs -- is
// to assert on the wiring's exact source text):
//   J37 - Ctrl state at drop time is captured and surfaced via AutofillModifiersResolved.
//   J54 - double-clicking the fill handle raises AutofillHandleDoubleClicked instead of starting
//         an ordinary single-cell drag.
public sealed class GridViewFAutofillCoreTests
{
    [Fact]
    public void AutofillMouseUp_InvokesModifiersResolvedBeforeAutofillRequestedWithCtrlState()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var dragStartMouseUp = source.IndexOf(
            "var fillRange = GridAutofillPlanner.CalculateFillRange(src, _autofillTarget.Value)",
            StringComparison.Ordinal);

        dragStartMouseUp.Should().BeGreaterThanOrEqualTo(0, "the mouse-up autofill-commit block should be present exactly once");

        var handler = source[dragStartMouseUp..(dragStartMouseUp + 600)];
        handler.Should().Contain("?? GridAutofillPlanner.CalculateClearRange(src, _autofillTarget.Value);");
        handler.Should().Contain("AutofillModifiersResolved?.Invoke(IsCtrlModifierDown());");
        handler.Should().Contain("AutofillRequested?.Invoke(src, fillRange.Value);");

        // Ctrl state must be surfaced before (or in the same invocation batch as) the fill request,
        // so a host reading it in an AutofillModifiersResolved handler sees it before acting on
        // the paired AutofillRequested call.
        handler.IndexOf("AutofillModifiersResolved?.Invoke", StringComparison.Ordinal)
            .Should()
            .BeLessThan(handler.IndexOf("AutofillRequested?.Invoke(src, fillRange.Value);", StringComparison.Ordinal));
    }

    [Fact]
    public void AutofillHandleMouseDown_DoubleClick_RaisesDoubleClickEventInsteadOfStartingDrag()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var handleStart = source.IndexOf("if (SelectedRange.HasValue && IsOnAutofillHandle(pos))", StringComparison.Ordinal);
        var nextBlockStart = source.IndexOf("if (TryBeginSelectionMoveDrag(pos))", handleStart, StringComparison.Ordinal);

        handleStart.Should().BeGreaterThanOrEqualTo(0);
        nextBlockStart.Should().BeGreaterThan(handleStart);

        var handleBlock = source[handleStart..nextBlockStart];
        handleBlock.Should().Contain("if (e.ClickCount >= 2)");
        handleBlock.Should().Contain("AutofillHandleDoubleClicked?.Invoke(SelectedRange.Value);");
        handleBlock.Should().Contain("_autofillDragging    = true;");

        // The double-click branch must return before falling into the ordinary drag-start code,
        // otherwise a double-click would both fire the event AND begin a single-cell drag.
        var doubleClickBranchStart = handleBlock.IndexOf("if (e.ClickCount >= 2)", StringComparison.Ordinal);
        var doubleClickBranchEnd = handleBlock.IndexOf("_autofillDragging    = true;", StringComparison.Ordinal);
        var doubleClickBranch = handleBlock[doubleClickBranchStart..doubleClickBranchEnd];
        doubleClickBranch.Should().Contain("return;");
    }
}
