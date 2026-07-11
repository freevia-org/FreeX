using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R27-merged-cells-deep-1: tiling a merged-cell source into a destination selection whose size is
/// NOT an exact multiple of the source range's size used to create a trailing merge that overhangs
/// past the selected destination -- e.g. copying a 2-column merge (A1:B1) into a 3-column destination
/// (C1:E1) created a second-tile merge anchored at E1 but spanning E1:F1, silently absorbing F1 (which
/// was never part of the copy/paste selection and had its own prior content ghosted away). Excel never
/// creates a merge that extends outside the pasted destination. Fixed by only recreating a tile's merge
/// when the whole tile (one full source-range period) fits within the tiled destination footprint,
/// mirroring how per-cell value/format tiling (EnumerateTiledAddresses) was already naturally bounded.
/// </summary>
public sealed class R27_MergedCellsTiledPasteTests
{
    [Fact]
    public void TiledMergePaste_PartialTrailingTile_IsNotCreated_AndDoesNotTouchCellPastDestination()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Source A1:B1 merged, anchor A1 = "Q1".
        var sourceAnchor = new CellAddress(sheet.Id, 1, 1);
        var sourceCovered = new CellAddress(sheet.Id, 1, 2);
        var sourceRange = new GridRange(sourceAnchor, sourceCovered);
        var sourceAnchorCell = Cell.FromValue(new TextValue("Q1"));
        sheet.SetCell(sourceAnchor, sourceAnchorCell);
        sheet.AddMergedRegion(sourceRange);

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in sourceRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        // F1 sits one column past the 3-column destination selection (C1:E1) and must be left
        // completely untouched by the paste -- neither absorbed into a merge nor cleared/overwritten.
        var ghostCell = new CellAddress(sheet.Id, 1, 6);
        sheet.SetCell(ghostCell, Cell.FromValue(new TextValue("ghost")));

        // Select C1:E1 (3 columns -- not a multiple of the 2-column source) and paste.
        var destinationStart = new CellAddress(sheet.Id, 1, 3);
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 1, 5));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            destinationRange,
            PasteCellsMode.All,
            new PasteSpecialOptions());

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // The original source merge (A1:B1) plus exactly one fully-fitting destination tile (C1:D1);
        // no merge overhangs into F1 (or beyond) for the trailing partial tile.
        sheet.MergedRegions.Should().HaveCount(2);
        sheet.MergedRegions.Should().Contain(new GridRange(
            new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 4)));
        sheet.MergedRegions.Should().NotContain(region => region.Contains(ghostCell));

        // F1's prior content survives untouched -- it was never part of the pasted destination.
        sheet.GetValue(ghostCell).Should().Be(new TextValue("ghost"));

        command.Revert(ctx);

        // Only the original source merge remains after undo; the pasted tile is gone.
        sheet.MergedRegions.Should().Equal(sourceRange);
        sheet.GetValue(ghostCell).Should().Be(new TextValue("ghost"));
    }

    /// <summary>
    /// Regression guard for the sibling case the fix must not break: when the destination selection
    /// IS an exact multiple of the copied merge's size, every tile still gets its own recreated merge,
    /// exactly as before the fix (this is the ordinary "tile a row of same-size merged headers" Excel
    /// workflow).
    /// </summary>
    [Fact]
    public void TiledMergePaste_ExactMultipleDestination_RecreatesMergeAtEveryTile()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceAnchor = new CellAddress(sheet.Id, 1, 1);
        var sourceCovered = new CellAddress(sheet.Id, 1, 2);
        var sourceRange = new GridRange(sourceAnchor, sourceCovered);
        var sourceAnchorCell = Cell.FromValue(new TextValue("Q1"));
        sheet.SetCell(sourceAnchor, sourceAnchorCell);
        sheet.AddMergedRegion(sourceRange);

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in sourceRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        // Select C1:F1 (4 columns -- an exact multiple of the 2-column source).
        var destinationStart = new CellAddress(sheet.Id, 1, 3);
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 1, 6));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            destinationRange,
            PasteCellsMode.All,
            new PasteSpecialOptions());

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // The original source merge (A1:B1) plus both destination tiles (C1:D1 and E1:F1).
        sheet.MergedRegions.Should().HaveCount(3);
        sheet.MergedRegions.Should().Contain(new GridRange(
            new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 4)));
        sheet.MergedRegions.Should().Contain(new GridRange(
            new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 6)));

        command.Revert(ctx);

        // Only the original source merge remains after undo; both pasted tiles are gone.
        sheet.MergedRegions.Should().Equal(sourceRange);
    }
}
