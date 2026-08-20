using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for freex-freeze-split F1 (src/FreeX.App.Services/WorkbookSession.cs):
/// <see cref="WorkbookSession.ReconcileViewStateForSave"/> already pushed zoom/view-mode/gridlines/
/// headings/rulers/show-formulas/freeze/split per-view overrides back onto the shared
/// <see cref="Sheet"/> before save (R120), but never included the per-view scroll position kept in
/// the private <c>_viewViewportOrigins</c> dictionary. A "New Window" sibling
/// (<see cref="WorkbookSession.CreateSiblingView"/>) that scrolled away from A1 and saved from
/// that window therefore persisted whatever the ROOT window's shared
/// <see cref="Sheet.ViewTopRow"/>/<see cref="Sheet.ViewLeftCol"/> happened to be (often A1, since
/// <see cref="WorkbookSession.SetViewViewportOrigin"/> only writes those shared fields when
/// <c>_sharedDocumentStateOwner is null</c>), not the sibling's own displayed scroll position.
/// </summary>
public sealed class R152_FreezeSplitF1_ScrollReconcileForSaveTests
{
    [Fact]
    public void ReconcileViewStateForSave_PushesSiblingsOwnScrollPosition_NotRootsA1()
    {
        var workbook = CreateWorkbook();
        var root = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var sibling = root.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        var sheet = workbook.GetSheetAt(0);

        // Root window never scrolls -- shared fields stay at their initial (unset/A1) state.
        sheet.ViewTopRow.Should().BeNull();
        sheet.ViewLeftCol.Should().BeNull();

        // The sibling window scrolls far down/right, exactly like "View > New Window" then
        // scrolling to row 500 in the new window.
        sibling.SetViewportOrigin(500, 3).Should().BeTrue();
        sibling.ViewportOrigin.Should().Be((500u, 3u));

        // The shared Sheet fields the file writer actually reads from are still untouched by the
        // sibling's own scroll -- that's the existing per-window display contract (R142/R147).
        sheet.ViewTopRow.Should().BeNull();
        sheet.ViewLeftCol.Should().BeNull();

        // *** The fix under test ***: saving FROM the sibling must push the sibling's own scroll
        // position onto the shared fields the writer reads, not leave the root's A1 in place.
        sibling.ReconcileViewStateForSave();

        sheet.ViewTopRow.Should().Be(500u,
            "Ctrl+S from the sibling window must persist what THAT window is actually scrolled to");
        sheet.ViewLeftCol.Should().Be(3u);

        // Unlike zoom/freeze/split (cached-on-first-read via GetOrSeedViewOverride, and never
        // invalidated for _viewViewportOrigins by InvalidateAllPerViewOverridesForSheet), scroll
        // position is deliberately read live through to the shared field whenever a view's own
        // _viewViewportOrigins has no entry for that sheet -- CustomViewStatePlanner already relies
        // on exactly this same live fallback so that Apply Custom View's shared ViewTopRow/ViewLeftCol
        // write is picked up without a dedicated re-seed step. Root here never scrolled itself, so
        // its own dict has no entry and it now reads through to the same shared field the sibling
        // just reconciled -- pre-existing, deliberate behavior this fix does not change.
        root.ViewportOrigin.Should().Be((500u, 3u));

        // Root remains authoritative the moment it acts on its own scroll again: as the root
        // session (_sharedDocumentStateOwner is null), its own SetViewportOrigin writes both its
        // local cache AND the shared field directly, overwriting the sibling's persisted value.
        root.SetViewportOrigin(1, 1).Should().BeTrue();
        sheet.ViewTopRow.Should().Be(1u);
        sheet.ViewLeftCol.Should().Be(1u);
    }

    /// <summary>
    /// No-regression sibling: a plain single-window session (the ordinary case, no "New Window" in
    /// play) must still reconcile its own scroll position as a pure no-op onto its own
    /// already-current shared fields -- <see cref="WorkbookSession.SetViewportOrigin"/> already
    /// writes the shared fields directly for the root session, so reconciliation must not disturb
    /// that value.
    /// </summary>
    [Fact]
    public void ReconcileViewStateForSave_SingleSession_ScrollPositionIsUnchanged()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.SetViewportOrigin(10, 6).Should().BeTrue();
        var sheet = workbook.GetSheetAt(0);
        sheet.ViewTopRow.Should().Be(10u);
        sheet.ViewLeftCol.Should().Be(6u);

        session.ReconcileViewStateForSave();

        sheet.ViewTopRow.Should().Be(10u, "reconciling a session with no diverged sibling must not change anything");
        sheet.ViewLeftCol.Should().Be(6u);
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        var sheet = workbook.AddSheet("Sheet1");
        for (var row = 1u; row <= 600; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
