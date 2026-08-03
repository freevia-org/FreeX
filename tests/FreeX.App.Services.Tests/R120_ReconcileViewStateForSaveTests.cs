using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for the R120 finding (src/FreeX.Core.IO/XlsxWorksheetViewWriter.cs:110):
/// <c>WorkbookSession</c>'s per-view overrides (<c>_viewZoomOverrides</c>/<c>_viewFrozenRowsOverrides</c>/
/// <c>_viewFrozenColsOverrides</c>/etc.) exist so each "New Window" sibling
/// (<see cref="WorkbookSession.CreateSiblingView"/>) keeps displaying its own remembered
/// zoom/view-mode/gridlines/headings/show-formulas/freeze/split even after a sibling changes the
/// shared <see cref="Sheet"/> fields those overrides shadow -- but every writer (e.g.
/// <c>XlsxWorksheetViewWriter</c>) only ever reads the shared <see cref="Sheet"/> fields directly.
/// Before the fix, saving from a view whose own state had diverged from the shared fields would
/// silently persist whichever sibling view's command last mutated them, not this view's own
/// displayed state.
///
/// The fix adds <see cref="WorkbookSession.ReconcileViewStateForSave"/>, called by both shells
/// (<c>MainWindow.SaveWorkbookToTargetAsync</c> in FreeX.App.Avalonia) immediately before handing
/// the workbook to <c>WorkbookSaveService.SaveAsync</c>/<c>IFileAdapter.Save</c>: it pushes this
/// view's own remembered overrides back onto the shared <see cref="Sheet"/> fields for every sheet
/// this view has diverged on. These tests exercise the REAL production session object
/// (<see cref="WorkbookSession"/>, constructed via <see cref="WorkbookSessionFactory"/> exactly like
/// the Avalonia shell does) rather than a hand-built model, per the existing R86/R87 sibling-view
/// test convention in this file's neighbors.
/// </summary>
public sealed class R120_ReconcileViewStateForSaveTests
{
    [Fact]
    public void ReconcileViewStateForSave_PushesThisViewsOwnZoomAndFreeze_NotSiblingsLaterOverwrite()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        // Both views start out agreeing with the shared (default) state.
        session.ZoomPercent.Should().Be(100);
        sibling.ZoomPercent.Should().Be(100);
        session.GetEffectiveFrozenRows().Should().Be(0u);

        // "session" (the view about to save) sets its own zoom and freezes the top row.
        session.SetZoomPercent(150).Success.Should().BeTrue();
        session.FreezeTopRow().Success.Should().BeTrue();

        var sheet = workbook.GetSheetAt(0);
        sheet.ZoomPercent.Should().Be(150);
        sheet.FrozenRows.Should().Be(1u);
        sheet.FrozenCols.Should().Be(0u);

        // Any sheet-metadata command invalidates EVERY per-view cache for that sheet (the single
        // choke point ApplySuccessfulWorkbookMetadataResult -> InvalidateAllPerViewOverridesForSheet
        // -- see its remarks), reseeding only the field(s) the command itself just applied. So
        // FreezeTopRow above dropped "session"'s own cached Zoom entry too, not just Freeze's.
        // Reading ZoomPercent back now (mirroring how the real shell re-reads it for the status bar
        // immediately after any view command) reseeds it from the shared field while it still holds
        // "session"'s own 150 -- i.e. before the sibling gets a chance to touch it below.
        session.ZoomPercent.Should().Be(150);

        // The sibling then independently changes the SAME shared Sheet's zoom/freeze to DIFFERENT
        // values -- mutating the shared fields "session" is about to serialize.
        sibling.SetZoomPercent(75).Success.Should().BeTrue();
        sibling.FreezeFirstColumn().Success.Should().BeTrue();
        // Same reseed-timing note as above, this time for the sibling: FreezeFirstColumn's
        // invalidate-all dropped the sibling's own cached Zoom entry too, so read it back now
        // (shared field still holds the sibling's own 75) before "session" reconciles below.
        sibling.ZoomPercent.Should().Be(75);

        sheet.ZoomPercent.Should().Be(75, "the sibling's own command last mutated the shared field");
        sheet.FrozenRows.Should().Be(0u);
        sheet.FrozenCols.Should().Be(1u);

        // "session" still displays ITS OWN zoom/freeze, unaffected by the sibling's later change --
        // this is the existing (already-fixed, R85/R86/R87) per-window DISPLAY behavior.
        session.ZoomPercent.Should().Be(150);
        session.GetEffectiveFrozenRows().Should().Be(1u);
        session.GetEffectiveFrozenCols().Should().Be(0u);

        // *** The R120 fix under test ***: reconciling before save must push "session"'s own view
        // back onto the shared Sheet fields, not leave the sibling's later overwrite in place.
        session.ReconcileViewStateForSave();

        sheet.ZoomPercent.Should().Be(150,
            "Ctrl+S from the saving view must persist what THAT view is displaying, not whichever " +
            "sibling view last touched the shared fields");
        sheet.FrozenRows.Should().Be(1u);
        sheet.FrozenCols.Should().Be(0u);

        // The sibling's own display must be completely unaffected by "session" reconciling for its
        // own save -- reconciliation is a one-way push from the saving view, never a broadcast.
        sibling.ZoomPercent.Should().Be(75);
        sibling.GetEffectiveFrozenRows().Should().Be(0u);
        sibling.GetEffectiveFrozenCols().Should().Be(1u);
    }

    /// <summary>
    /// No-regression sibling: a session with no sibling in play (the ordinary single-window case)
    /// must still reconcile as a pure no-op onto its own already-current shared fields, and must
    /// never touch cell data -- reconciliation only ever writes the nine view-state fields, never
    /// anything else on the Sheet.
    /// </summary>
    [Fact]
    public void ReconcileViewStateForSave_SingleSession_IsANoOpAndLeavesCellDataUntouched()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.SetZoomPercent(200).Success.Should().BeTrue();
        session.FreezePanesAtActiveCell(); // active cell is A1 by default -> frozenRows=0, frozenCols=0

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        session.ReconcileViewStateForSave();

        sheet.ZoomPercent.Should().Be(200, "reconciling a session with no diverged sibling must not change anything");
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(42),
            "reconciliation must only ever touch the nine view-state fields, never cell data");
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
