using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Integration.Tests;

/// <summary>
/// U2-shared-large-document-limits-F1: PasteCommandFactory.CreateExternalTextPasteCommand's tiled
/// destination path was capped (MaxTiledPasteCellCount) against a whole-sheet Paste from an
/// external clipboard. PasteLinkService.CreateLinkedCells -- the tiling overload behind Paste
/// Special &gt; Paste Link, reachable from both MainWindow.ClipboardCommands.ExecutePasteLink and
/// WorkbookSession.CreatePasteLinkCommand -- had no equivalent cap, only the pre-existing
/// WorksheetBounds.TryGetRectangleEnd check (see R21_PasteLinkDestinationBoundsTests), which a
/// whole-sheet destination always passes. Copying a single 1x1 cell and pasting a link across a
/// whole-sheet selection built one Cell.FromFormula + string-formatted entry per destination cell
/// (measured: 4,004,001 entries from a 2,001 x 2,001 destination in 6.7s, ~45x the per-cell cost of
/// the now-capped external-paste path) -- at least as severe an OOM/hang at whole-sheet scale. This
/// reuses PasteCommandFactory.MaxTiledPasteCellCount (now internal) rather than declaring a second
/// limit that could drift from it, and follows the exact "return no linked cells" rejection
/// contract R21 already established for this method's bounds check.
/// </summary>
public sealed class R163_PasteLinkTiledCellCapTests
{
    [Fact]
    public void CreateLinkedCells_JustOverCapDestination_ReturnsNoLinkedCellsInsteadOfBuildingMillions()
    {
        var sheetId = SheetId.New();

        // Exactly the size measured in the finding: a 1x1 copied source tiled across a
        // 2,001 x 2,001 destination = 4,004,001 cells, one over MaxTiledPasteCellCount
        // (4,000,000) -- small enough to safely exercise the pre-fix code path (which built the
        // full `linkedCells` list unconditionally) without the OOM/hang risk of an actual
        // whole-sheet-scale (~4 x 10^9 cell) destination from the real-world gesture.
        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 1, 1));
        var destination = new CellAddress(sheetId, 1, 1);
        var destinationRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 2001, 2001));

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination,
            destinationRange,
            sourceSheetName: "Sheet1",
            transpose: false);

        Assert.Empty(linkedCells);
    }

    [Fact]
    public void CreateLinkedCells_DestinationAtCap_StillCreatesLinkedCells()
    {
        // Sibling no-regression case: a destination exactly at the existing cap
        // (2,000 x 2,000 = 4,000,000 cells, matching PasteCommandFactory's own at-cap test) must
        // still be tiled normally -- the new guard must not tighten the limit below what the
        // internal/external paste paths already allow.
        var sheetId = SheetId.New();
        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 1, 1));
        var destination = new CellAddress(sheetId, 1, 1);
        var destinationRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 2000, 2000));

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination,
            destinationRange,
            sourceSheetName: "Sheet1",
            transpose: false);

        Assert.Equal(4_000_000, linkedCells.Count);
    }

    [Fact]
    public void CreateLinkedCells_SmallDestination_StillCreatesLinkedCells()
    {
        // Sibling no-regression case: an ordinary small Paste Link (no tiling near any cap) must
        // be entirely unaffected -- same shape as R21's untouched-behaviour case.
        var sheetId = SheetId.New();
        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 3, 1));
        var destination = new CellAddress(sheetId, 10, 1);

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination,
            sourceSheetName: "Sheet1",
            transpose: false);

        Assert.Equal(3, linkedCells.Count);
    }
}
