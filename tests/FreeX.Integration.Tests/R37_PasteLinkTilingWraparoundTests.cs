using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Integration.Tests;

/// <summary>
/// R37-meta-3: PasteLinkService.CreateLinkedCells' destination tiling previously only completed
/// WHOLE tiles that fit the destination selection
/// (`rowTileOffset + pasteRowCount &lt;= targetRowCount`), leaving any trailing partial tile
/// completely untouched. That diverges from PasteCommandFactory's EnumerateTiledAddresses (used
/// by Values/Formulas/Formats/All), which fills every cell of the destination selection
/// unconditionally, wrapping the source index modulo the source range's row/column count
/// (`rowOffset % sourceRange.RowCount`, `colOffset % sourceRange.ColCount`) -- so a plain Ctrl+V
/// paste and a Paste Special &gt; Paste Link of the identical source onto the identical
/// non-exact-multiple destination selection produced different footprints. The fix makes
/// CreateLinkedCells use the same full-fill, modulo-wraparound tiling as EnumerateTiledAddresses.
/// </summary>
public sealed class R37_PasteLinkTilingWraparoundTests
{
    /// <summary>
    /// Copy a 1x2 range (A1:B1) and Paste Link into a 1x3 destination selection (D1:F1) -- NOT an
    /// exact multiple of the 1x2 source in the column dimension. Before the fix, only D1:E1 (the
    /// one whole tile that fits) got linked formulas and F1 was left completely untouched (no
    /// cell at all). After the fix, F1 must also get a linked formula, wrapping back to the start
    /// of the source range (A1), exactly like an ordinary Values/Formulas paste onto the same
    /// selection would (PasteCommandFactory.EnumerateTiledAddresses).
    /// </summary>
    [Fact]
    public void CreateLinkedCells_NonExactMultipleDestination_WrapsTrailingPartialTile()
    {
        var sheetId = SheetId.New();
        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1), // A1
            new CellAddress(sheetId, 1, 2)); // B1
        var destination = new CellAddress(sheetId, 1, 4); // D1
        var destinationRange = new GridRange(
            destination,
            new CellAddress(sheetId, 1, 6)); // D1:F1 (1x3 selection)

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination,
            destinationRange,
            sourceSheetName: "Sheet1",
            transpose: false);

        Assert.Equal(3, linkedCells.Count);

        var byAddress = linkedCells.ToDictionary(c => c.Address, c => c.Cell);
        Assert.Equal("Sheet1!A1", byAddress[new CellAddress(sheetId, 1, 4)].FormulaText); // D1
        Assert.Equal("Sheet1!B1", byAddress[new CellAddress(sheetId, 1, 5)].FormulaText); // E1
        // The trailing partial tile (F1, the 3rd column) wraps back to the start of the source
        // range instead of being left untouched.
        Assert.Equal("Sheet1!A1", byAddress[new CellAddress(sheetId, 1, 6)].FormulaText); // F1
    }

    /// <summary>
    /// Sibling non-regression case: a destination selection that IS an exact whole multiple of the
    /// source range (2x2 source tiled onto a 4x4 destination) must still tile identically to
    /// before -- the wraparound formula produces the same result as the old whole-tiles-only loop
    /// whenever the destination is an exact multiple, so this must be unaffected by the fix.
    /// </summary>
    [Fact]
    public void CreateLinkedCells_ExactMultipleDestination_TilesWholeRepeatsUnchanged()
    {
        var sheetId = SheetId.New();
        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1), // A1
            new CellAddress(sheetId, 2, 2)); // B2
        var destination = new CellAddress(sheetId, 1, 4); // D1
        var destinationRange = new GridRange(
            destination,
            new CellAddress(sheetId, 4, 7)); // D1:G4 (4x4 selection, exact 2x multiple)

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination,
            destinationRange,
            sourceSheetName: "Sheet1",
            transpose: false);

        Assert.Equal(16, linkedCells.Count);

        var byAddress = linkedCells.ToDictionary(c => c.Address, c => c.Cell);
        Assert.Equal("Sheet1!A1", byAddress[new CellAddress(sheetId, 1, 4)].FormulaText); // D1
        Assert.Equal("Sheet1!B2", byAddress[new CellAddress(sheetId, 2, 5)].FormulaText); // E2
        // Tile (0,1): F1:G2 wraps back to A1:B2.
        Assert.Equal("Sheet1!A1", byAddress[new CellAddress(sheetId, 1, 6)].FormulaText); // F1
        // Tile (1,0): D3:E4 wraps back to A1:B2.
        Assert.Equal("Sheet1!A1", byAddress[new CellAddress(sheetId, 3, 4)].FormulaText); // D3
        // Tile (1,1): F3:G4 wraps back to A1:B2.
        Assert.Equal("Sheet1!B2", byAddress[new CellAddress(sheetId, 4, 7)].FormulaText); // G4
    }

    /// <summary>
    /// Non-exact-multiple destination under a transposed Paste Link: copy a 1x2 range (A1:B1),
    /// transpose so the paste footprint is 2x1, and select a destination taller than the transpose
    /// footprint but not an exact multiple (3 rows). The 3rd (partial) row must wrap back to the
    /// start of the source range rather than being left untouched.
    /// </summary>
    [Fact]
    public void CreateLinkedCells_TransposedNonExactMultipleDestination_WrapsTrailingPartialTile()
    {
        var sheetId = SheetId.New();
        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1), // A1
            new CellAddress(sheetId, 1, 2)); // B1
        var destination = new CellAddress(sheetId, 1, 4); // D1
        var destinationRange = new GridRange(
            destination,
            new CellAddress(sheetId, 3, 4)); // D1:D3 (3x1 selection, transpose footprint is 2x1)

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination,
            destinationRange,
            sourceSheetName: "Sheet1",
            transpose: true);

        Assert.Equal(3, linkedCells.Count);

        var byAddress = linkedCells.ToDictionary(c => c.Address, c => c.Cell);
        Assert.Equal("Sheet1!A1", byAddress[new CellAddress(sheetId, 1, 4)].FormulaText); // D1
        Assert.Equal("Sheet1!B1", byAddress[new CellAddress(sheetId, 2, 4)].FormulaText); // D2
        // The trailing partial row (D3) wraps back to the start of the source range.
        Assert.Equal("Sheet1!A1", byAddress[new CellAddress(sheetId, 3, 4)].FormulaText); // D3
    }
}
