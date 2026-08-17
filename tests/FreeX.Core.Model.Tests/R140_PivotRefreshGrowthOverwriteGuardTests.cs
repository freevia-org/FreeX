using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R140-commands-pivot-refresh-growth-dataloss: a refresh that legitimately needs to grow the pivot's
/// footprint (a new distinct row item appeared in the source) must not silently overwrite adjacent user
/// content, and must not leave that content unrecoverable by Undo. Real Excel refuses the refresh
/// outright with a warning rather than clobbering the cell; these tests pin that same refusal in
/// RefreshPivotTableCommand -- the actual F5 / "Refresh PivotTable" entry point a real user reaches --
/// plus a sibling test proving an ordinary refresh that grows into genuinely BLANK space still succeeds
/// exactly as before.
/// </summary>
public sealed class R140_PivotRefreshGrowthOverwriteGuardTests
{
    [Fact]
    public void RefreshPivotTableCommand_RefusesGrowthThatWouldOverwriteAdjacentData_AndLeavesSheetUntouched()
    {
        var workbook = new Workbook("PivotGrowthGuardTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F6"),
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        // Initial render with 2 categories (A, B): header D3, "A" row D4, "B" row D5, Grand Total D6 --
        // footprint D3:E6 (matches the shape asserted by PivotTableCommandTests.Create's Grand-Total-at-
        // D6 case for this same 2-category seed/layout).
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));

        // A user note sitting one row BELOW the pivot's current footprint -- exactly where a 3rd
        // category's row would land if the pivot grew.
        var noteAddress = Addr(sheet, "D7");
        sheet.SetCell(noteAddress, new TextValue("Notes: verify Q3"));

        // The source gains a 3rd distinct category, and the pivot's source range is widened to see it
        // (the ordinary "someone added a row to the source table" case).
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        pivot.SourceRange = Range(sheet, "A1", "B4");

        var command = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        var outcome = command.Apply(ctx);

        // Matches Excel: refuse the refresh rather than silently destroying the neighbour's data.
        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");

        // The note must survive completely untouched -- not merely "restorable via Undo", but never
        // clobbered in the first place (Apply itself must roll back before returning failure).
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));

        // The pivot's own previous render must also be exactly as it was (still only 2 categories) --
        // a refused refresh must not leave a half-applied pivot behind either.
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(Addr(sheet, "D5"))!.Value.Should().Be(new TextValue("B"));
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("Grand Total"));

        // Since Apply returned failure, a real CommandBus never pushes this onto the undo stack -- but
        // even calling Revert directly must be harmless (no snapshot was retained to replay).
        command.Revert(ctx);
        sheet.GetCell(noteAddress)!.Value.Should().Be(new TextValue("Notes: verify Q3"));
    }

    [Fact]
    public void RefreshPivotTableCommand_StillGrowsIntoGenuinelyBlankSpace_AndUndoStillRestoresOldFootprint()
    {
        // Sibling/neighbour test: growth into cells that were actually blank (the overwhelmingly common
        // case -- nothing sits below/right of most pivots) must keep working exactly as before this fix,
        // including the pre-existing Undo-restores-the-old-footprint behavior.
        var workbook = new Workbook("PivotGrowthGuardNeighbourTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F6"),
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));
        sheet.GetCell(Addr(sheet, "D7")).Should().BeNull(); // genuinely blank -- no neighbour data here

        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        pivot.SourceRange = Range(sheet, "A1", "B4");

        var command = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E7"));
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("C"));
        sheet.GetCell(Addr(sheet, "D7"))!.Value.Should().Be(new TextValue("Grand Total"));

        command.Revert(ctx);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));
        sheet.GetCell(Addr(sheet, "D6"))!.Value.Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(Addr(sheet, "D7")).Should().BeNull();
    }

    private static void SeedData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
    }

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));
}
