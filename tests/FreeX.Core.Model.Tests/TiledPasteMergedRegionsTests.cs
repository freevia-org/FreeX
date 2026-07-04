using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for review finding H41: tiled paste (a copied range pasted into a larger
/// selected destination) must recreate the source's merged region at every repeated tile, not
/// just drop it, matching the single-tile paste path's merge recreation (G36).
/// </summary>
public sealed class TiledPasteMergedRegionsTests
{
    [Fact]
    public void PasteCommandFactory_TiledAllModeRecreatesMergedRegionAtEveryTile()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Source: a single merged row A1:B1 ("Header").
        var mergeStart = new CellAddress(sheet.Id, 1, 1);
        var mergeEnd = new CellAddress(sheet.Id, 1, 2);
        var mergeRange = new GridRange(mergeStart, mergeEnd);
        sheet.SetCell(mergeStart, Cell.FromValue(new TextValue("Header")));
        sheet.AddMergedRegion(mergeRange);

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in mergeRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        // Destination selection is 4 rows tall (rows 4-7) x 2 cols wide, so the 1-row source tiles
        // 4 times vertically.
        var destinationStart = new CellAddress(sheet.Id, 4, 1);
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 7, 2));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            mergeRange,
            sourceCells,
            destinationRange,
            PasteCellsMode.All,
            default);

        var applyOutcome = command.Apply(ctx);
        applyOutcome.Success.Should().BeTrue(applyOutcome.ErrorMessage);

        // A merged region must be recreated at each of the 4 tile rows.
        for (uint tileRow = 4; tileRow <= 7; tileRow++)
        {
            var expectedTileMerge = new GridRange(
                new CellAddress(sheet.Id, tileRow, 1),
                new CellAddress(sheet.Id, tileRow, 2));
            sheet.MergedRegions.Should().Contain(expectedTileMerge, because: $"tile at row {tileRow} should recreate the source merge");
            sheet.GetValue(new CellAddress(sheet.Id, tileRow, 1)).Should().Be(new TextValue("Header"));
        }

        command.Revert(ctx);

        for (uint tileRow = 4; tileRow <= 7; tileRow++)
        {
            var expectedTileMerge = new GridRange(
                new CellAddress(sheet.Id, tileRow, 1),
                new CellAddress(sheet.Id, tileRow, 2));
            sheet.MergedRegions.Should().NotContain(expectedTileMerge);
        }
    }

    [Fact]
    public void PasteCommandFactory_TiledAllModeSkipsTileWhenDestinationAlreadyHasOverlappingMerge()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var mergeStart = new CellAddress(sheet.Id, 1, 1);
        var mergeEnd = new CellAddress(sheet.Id, 1, 2);
        var mergeRange = new GridRange(mergeStart, mergeEnd);
        sheet.SetCell(mergeStart, Cell.FromValue(new TextValue("Header")));
        sheet.AddMergedRegion(mergeRange);

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in mergeRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        var destinationStart = new CellAddress(sheet.Id, 4, 1);
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 5, 2));

        // Pre-existing merge collides with the second tile (row 5) only.
        var collidingMerge = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 2));
        sheet.AddMergedRegion(collidingMerge);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            mergeRange,
            sourceCells,
            destinationRange,
            PasteCellsMode.All,
            default);

        var applyOutcome = command.Apply(ctx);
        applyOutcome.Success.Should().BeTrue(applyOutcome.ErrorMessage);

        var firstTileMerge = new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 4, 2));
        sheet.MergedRegions.Should().Contain(firstTileMerge);

        // The colliding second tile is left alone (not duplicated), same as the single-tile path.
        sheet.MergedRegions.Should().ContainSingle(r => r.Equals(collidingMerge));
    }

    [Fact]
    public void PasteCommandFactory_TiledFormatsModeRecreatesMergedRegionAtEveryTile()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var mergeStart = new CellAddress(sheet.Id, 1, 1);
        var mergeEnd = new CellAddress(sheet.Id, 1, 2);
        var mergeRange = new GridRange(mergeStart, mergeEnd);
        sheet.SetCell(mergeStart, Cell.FromValue(new TextValue("Header")));
        sheet.AddMergedRegion(mergeRange);

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in mergeRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        var destinationStart = new CellAddress(sheet.Id, 4, 1);
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 5, 2));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            mergeRange,
            sourceCells,
            destinationRange,
            PasteCellsMode.Formats,
            default);

        var applyOutcome = command.Apply(ctx);
        applyOutcome.Success.Should().BeTrue(applyOutcome.ErrorMessage);

        sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 4, 2)));
        sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 2)));
    }
}
