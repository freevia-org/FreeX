using FluentAssertions;

namespace FreeX.App.UI.Tests;

// F2 (round 160): Escape did not cancel an in-progress border-drag range move or fill-handle
// autofill drag in the WPF grid. The only paths that reset _selectionMoveDragging /
// _autofillDragging without committing were OnMouseLeftButtonUp (commit) and
// CancelActiveCapturedGridDrag (called only from OnLostMouseCapture or from OnMouseMove once the
// button was already released). GridView.OnKeyDown never checked either flag, so Escape had no
// effect while the mouse button was still held down mid-drag.
//
// GridView requires a live UI-thread control with real mouse capture to drive an actual drag end
// to end; a throwaway probe against this suite (mirroring the technique the sibling
// "LostMouseCaptureCancelsActiveResize" / "LostMouseCaptureClearsCapturedPointerDragStates" tests
// above already rely on) confirmed GridView.CaptureMouse() returns false in this headless test
// host -- even with a real Window shown -- so ReleaseMouseCapture() never raises
// OnLostMouseCapture here. Per the established pattern in this project for WPF-only
// GridView.Input.cs behavior that live tests cannot reach, these assert on the wiring's exact
// source text.
public sealed class R160_EscapeCancelsGridDragTests
{
    private static string ReadOnKeyDown()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");
        var start = source.IndexOf("protected override void OnKeyDown", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "OnKeyDown should be present exactly once");
        var end = source.IndexOf("private bool IsOnSelectionMoveBorder", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    [Fact]
    public void OnKeyDown_EscapeWhileSelectionMoveOrAutofillDraggingReleasesCaptureWithoutCommitting()
    {
        var onKeyDown = ReadOnKeyDown();

        onKeyDown.Should().Contain(
            "if (e.Key == Key.Escape && (_selectionMoveDragging || _autofillDragging))",
            "Escape must be checked against both in-flight drag flags, not just the comment-preview key");

        var dragBranchStart = onKeyDown.IndexOf(
            "if (e.Key == Key.Escape && (_selectionMoveDragging || _autofillDragging))",
            StringComparison.Ordinal);
        var commentBranchStart = onKeyDown.IndexOf(
            "if (e.Key == Key.Escape && _activeCommentPreviewKey.HasValue)",
            StringComparison.Ordinal);
        commentBranchStart.Should().BeGreaterThan(dragBranchStart, "the drag-cancel check must run before the comment-preview check");

        var dragBranch = onKeyDown[dragBranchStart..commentBranchStart];

        // Must actually drop capture (this is what triggers OnLostMouseCapture ->
        // CancelActiveCapturedGridDrag, the same non-committing cleanup used when capture is lost
        // any other way, e.g. Alt-Tab) rather than commit the drag or leave state dangling.
        dragBranch.Should().Contain("ReleaseMouseCapture();");
        dragBranch.Should().NotContain("SelectionMoveRequested?.Invoke");
        dragBranch.Should().NotContain("AutofillRequested?.Invoke");

        // Deliberately does NOT set e.Handled/return here (unlike the comment-preview branch
        // below): the host's window-level Escape handler (CancelCopyAndTransientModes) clears
        // unrelated transient state -- clipboard marquee, format painter, border-draw mode -- that
        // can be active at the same time as a border/fill-handle drag, and must keep running.
        // Swallowing the key here would silently regress that.
        dragBranch.Should().NotContain("e.Handled = true;");
        dragBranch.Should().NotContain("return;");
    }

    [Fact]
    public void OnKeyDown_EscapeForCommentPreviewStillDismissesPreviewAndHandlesEventUnaffectedByDragFix()
    {
        // Sibling no-regression case: the pre-existing comment-preview Escape branch (which does
        // not touch drag state at all) must still dismiss the preview and short-circuit (mark
        // handled, return) exactly as before this fix -- unlike the new drag-cancel branch above,
        // which intentionally lets the key keep bubbling.
        var onKeyDown = ReadOnKeyDown();

        var commentBranchStart = onKeyDown.IndexOf(
            "if (e.Key == Key.Escape && _activeCommentPreviewKey.HasValue)",
            StringComparison.Ordinal);
        commentBranchStart.Should().BeGreaterThanOrEqualTo(0);

        var baseCallIndex = onKeyDown.IndexOf("base.OnKeyDown(e);", StringComparison.Ordinal);
        baseCallIndex.Should().BeGreaterThan(commentBranchStart);

        var commentBranch = onKeyDown[commentBranchStart..baseCallIndex];
        commentBranch.Should().Contain("DismissCommentPreview();");
        commentBranch.Should().Contain("e.Handled = true;");
        commentBranch.Should().Contain("return;");
        commentBranch.Should().NotContain("ReleaseMouseCapture();", "the comment-preview branch must remain independent of the drag-cancel branch");
    }
}
