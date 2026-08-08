using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.Core.Model;

using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R119-render-fill-move-drag-preview: the Avalonia (Linux/macOS) shell showed NO live preview
/// overlay at all while a fill-handle drag or a selection-border-move drag was in progress.
/// <c>ContinueAutofillDrag</c>/<c>ContinueSelectionMoveDrag</c> (MainWindow.cs) only updated
/// <c>_autofillTarget</c>/<c>_selectionMovePreviewRange</c> with no rebuild of the sheet grid's
/// Border-tree, and no rendering method even read those fields -- so a user got zero visual
/// feedback about the pending fill/move destination until releasing the mouse (CommitAutofillDrag/
/// CommitSelectionMoveDragAsync's RefreshShell only then revealed the result). This mirrors the WPF
/// host's GridView.Input.cs, which calls InvalidateVisual() on every pointer move to repaint
/// RenderAutofillPreview (GridView.Overlays.cs) / RenderSelectionMovePreview
/// (GridView.Rendering.Selection.cs).
///
/// The fix adds AddAutofillPreviewOverlayToGrid/AddSelectionMovePreviewOverlayToGrid (consumed from
/// BuildSheetGrid) plus a scoped-down RefreshShellForGridPreview call from each Continue* method's
/// new *Core helper, so the sheet grid's hosted content is rebuilt -- and the preview becomes
/// visible -- on every drag-continuation call, without waiting for the release/commit.
///
/// These drive the real continuation logic via the internal test-only seams
/// RaiseContinueAutofillDragForTest/RaiseContinueSelectionMoveDragForTest (mirroring the
/// pre-existing commit-only RaiseAutofillDragForTest/RaiseSelectionMoveDragForTest seams), then
/// inspect the ACTUAL post-call hosted grid content -- with no extra manual rebuild call from the
/// test -- so the assertion proves the continuation method itself triggers the live repaint, not
/// just that some rebuild call CAN show a preview if one happens to be requested.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R119_FillHandleAndSelectionMoveDragPreviewTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ContinueAutofillDrag_ImmediatelyShowsLiveDashedPreviewOverlay_NoReleaseNeeded()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("R119AutofillDragPreviewFixture");
            window.Session.SelectSheet(sheet.Id);

            var source = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));
            window.Session.SelectCell(source.Start);

            // Sanity: before any drag starts, there is no autofill preview overlay.
            FindByAutomationId<AvaloniaRectangle>(window.RebuildSheetGridForTest(), "WorksheetAutofillPreview")
                .Should().BeNull("no drag is in progress yet");

            // Drives ContinueAutofillDrag's real continuation logic directly (bypassing pointer
            // capture, exactly like the pre-existing RaiseAutofillDragForTest bypasses it for the
            // commit path) -- this alone, with no separate rebuild call from the test, must be
            // enough to make the live preview overlay visible in the actual hosted grid content.
            window.RaiseContinueAutofillDragForTest(source, new CellAddress(sheet.Id, 4, 1));

            var hostedContent = window.SheetGridHostContentForTest;
            hostedContent.Should().NotBeNull(
                "ContinueAutofillDrag must rebuild the sheet grid's hosted content on every pointer " +
                "move, matching the WPF host's per-move InvalidateVisual() repaint");
            FindByAutomationId<AvaloniaRectangle>(hostedContent!, "WorksheetAutofillPreview").Should().NotBeNull(
                "a fill-handle drag in progress must show a live dashed preview rectangle over the " +
                "pending fill destination, exactly like Excel and the WPF host -- before this fix there " +
                "was no rendering path for _autofillTarget at all, so no rebuild call could ever have " +
                "shown one either");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ContinueSelectionMoveDrag_ImmediatelyShowsLiveDestinationOutline_NoReleaseNeeded()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("R119MoveDragPreviewFixture");
            window.Session.SelectSheet(sheet.Id);

            var source = new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 3, 3));
            window.Session.SelectRange(source);

            FindByAutomationId<Border>(window.RebuildSheetGridForTest(), "WorksheetSelectionMovePreview")
                .Should().BeNull("no border-move drag is in progress yet");

            window.RaiseContinueSelectionMoveDragForTest(source, source.Start, new CellAddress(sheet.Id, 6, 6));

            var hostedContent = window.SheetGridHostContentForTest;
            hostedContent.Should().NotBeNull(
                "ContinueSelectionMoveDrag must rebuild the sheet grid's hosted content on every " +
                "pointer move, matching the WPF host's UpdateSelectionMovePreview InvalidateVisual() call");
            FindByAutomationId<Border>(hostedContent!, "WorksheetSelectionMovePreview").Should().NotBeNull(
                "a selection-border-move drag in progress must show the destination outline live -- " +
                "before this fix, ContinueSelectionMoveDrag only updated _selectionMovePreviewRange " +
                "with no rebuild and no rendering method ever consumed it");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── No-regression: the preview overlays must never leak outside an active drag ────────────────

    [Fact]
    public async Task NoRegression_AutofillPreviewNeverRendersOutsideAnActiveDrag_EvenWithStaleTargetState()
    {
        // Guards the _autofillDragging gate in AddAutofillPreviewOverlayToGrid: the plain
        // drag-commit test seam (RaiseAutofillDragForTest, mirroring a real completed/released
        // drag) leaves _autofillSourceRange/_autofillTarget populated but never sets
        // _autofillDragging. If that gate were ever dropped, a stale source/target pair would
        // incorrectly keep showing the dashed preview after the drag has already ended.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("R119AutofillNoRegressionFixture");
            window.Session.SelectSheet(sheet.Id);
            var source = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));
            window.Session.SelectCell(source.Start);

            window.RaiseAutofillDragForTest(source, new CellAddress(sheet.Id, 3, 1));

            var rebuilt = window.RebuildSheetGridForTest();
            FindByAutomationId<AvaloniaRectangle>(rebuilt, "WorksheetAutofillPreview").Should().BeNull(
                "the completed (non-dragging) commit path must never show the live drag-preview " +
                "overlay, even though _autofillSourceRange/_autofillTarget remain populated from the " +
                "commit call");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NoRegression_SelectionMovePreviewNeverRendersOutsideAnActiveDrag_EvenWithStaleTargetState()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("R119MoveNoRegressionFixture");
            window.Session.SelectSheet(sheet.Id);
            var source = new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 3, 3));
            window.Session.SelectRange(source);

            await window.RaiseSelectionMoveDragForTest(source, new GridRange(
                new CellAddress(sheet.Id, 5, 5),
                new CellAddress(sheet.Id, 6, 6)));

            var rebuilt = window.RebuildSheetGridForTest();
            FindByAutomationId<Border>(rebuilt, "WorksheetSelectionMovePreview").Should().BeNull(
                "the completed (non-dragging) commit path must never show the live drag-preview " +
                "outline, even though _selectionMoveSourceRange/_selectionMovePreviewRange remain " +
                "populated from the commit call");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static T? FindByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        if (root is T own && AutomationProperties.GetAutomationId(own) == automationId)
            return own;

        return root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
    }
}
