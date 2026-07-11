using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Integration.Tests;

/// <summary>
/// R21-clipboard-paste-special-deep-1: Paste Link (Paste Special &gt; Paste Link) must reject
/// destinations that would place a linked-formula cell outside the worksheet grid, matching the
/// destination-bounds validation every other paste path applies via WorksheetBounds. Before the
/// fix, PasteLinkService.CreateLinkedCells did plain uint arithmetic with no bounds check at all,
/// silently producing an off-grid CellAddress (Row/Col beyond MaxRow/MaxCol) that could be dropped
/// or corrupt the row/col reference on XLSX save instead of being rejected up front.
/// </summary>
public sealed class R21_PasteLinkDestinationBoundsTests
{
    [Fact]
    public void CreateLinkedCells_RowOffsetWouldExceedMaxRow_ReturnsNoLinkedCells()
    {
        var sheetId = SheetId.New();

        // Copy a 7-row range near the bottom of the sheet (A1048570:A1048576), then Paste Link at
        // A1048572 — the last linked cell would land on row 1048578, past CellAddress.MaxRow
        // (1,048,576).
        var sourceRange = new GridRange(
            new CellAddress(sheetId, CellAddress.MaxRow - 6, 1),
            new CellAddress(sheetId, CellAddress.MaxRow, 1));
        var destination = new CellAddress(sheetId, CellAddress.MaxRow - 4, 1);

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination,
            sourceSheetName: "Sheet1",
            transpose: false);

        Assert.Empty(linkedCells);
    }

    [Fact]
    public void CreateLinkedCells_ColumnOffsetWouldExceedMaxCol_ReturnsNoLinkedCells()
    {
        var sheetId = SheetId.New();

        // Copy a 7-column range near the right edge of the sheet, then Paste Link close enough to
        // the right edge that the last linked cell would land past CellAddress.MaxCol (16,384).
        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 1, 7));
        var destination = new CellAddress(sheetId, 1, CellAddress.MaxCol - 4);

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination,
            sourceSheetName: "Sheet1",
            transpose: false);

        Assert.Empty(linkedCells);
    }

    [Fact]
    public void CreateLinkedCells_TransposedOffsetWouldExceedMaxRow_ReturnsNoLinkedCells()
    {
        var sheetId = SheetId.New();

        // A transposed paste swaps row/col offsets, so a source range spanning several columns
        // can overflow MaxRow at the destination once transposed.
        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 1, 7));
        var destination = new CellAddress(sheetId, CellAddress.MaxRow - 4, 1);

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination,
            sourceSheetName: "Sheet1",
            transpose: true);

        Assert.Empty(linkedCells);
    }

    [Fact]
    public void CreateLinkedCells_DestinationExactlyFitsGrid_StillCreatesLinkedCells()
    {
        var sheetId = SheetId.New();

        // A destination whose last linked cell lands exactly on MaxRow must still paste — only
        // ranges that actually overflow the grid should be rejected.
        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 3, 1));
        var destination = new CellAddress(sheetId, CellAddress.MaxRow - 2, 1);

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination,
            sourceSheetName: "Sheet1",
            transpose: false);

        Assert.Equal(3, linkedCells.Count);
        Assert.Equal(new CellAddress(sheetId, CellAddress.MaxRow - 2, 1), linkedCells[0].Address);
        Assert.Equal(new CellAddress(sheetId, CellAddress.MaxRow, 1), linkedCells[2].Address);
    }
}
