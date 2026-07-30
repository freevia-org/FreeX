using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R92-app-pivot-drilldown-5-3: refreshing a pivot table whose source shrank enough to invalidate a
/// field's SourceFieldIndex used to blank the pivot's on-sheet output with no error, because
/// PivotTableRefreshService.Refresh called ClearRefreshRanges unconditionally before checking whether
/// the row/column/page/data fields were still valid for the new (shrunk) header count -- on failure it
/// set LastRenderedRange = null and returned without writing any replacement content, leaving a
/// permanently blank hole where the pivot used to be, with RefreshPivotTableCommand.Apply still
/// reporting success. The fix prunes the now-invalid field entries from the live field lists (the way
/// Excel drops a field whose source column disappeared) before anything is cleared, then re-renders
/// cleanly with whatever fields are still valid.
///
/// Both tests drive the real product entry point, RefreshPivotTableCommand (the command
/// RefreshPivotTableCommand.Apply/the ribbon's Data > Refresh action constructs), not the internal
/// service method directly.
/// </summary>
public sealed class R92_PivotSourceShrinkRefreshTests
{
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) CreateRegionAmountUnitsPivot(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Units"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(2));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(3));

        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(4));

        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), new NumberValue(5));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 10, 8)),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Units", "sum"));
        sheet.PivotTables.Add(pivot);

        var initialRefresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        initialRefresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        return (workbook, sheet, pivot);
    }

    private static string Text(Sheet sheet, uint row, uint col) =>
        sheet.GetCell(new CellAddress(sheet.Id, row, col))?.Value is TextValue text ? text.Value : "";

    private static double Number(Sheet sheet, uint row, uint col) =>
        sheet.GetCell(new CellAddress(sheet.Id, row, col))?.Value is NumberValue number ? number.Value : double.NaN;

    // --- bug case: source loses an entire field's backing column (SourceFieldIndex goes out of range) ---

    [Fact]
    public void Refresh_SourceLosesDataFieldColumn_DropsInvalidFieldAndRelayoutsCleanly()
    {
        var (workbook, sheet, pivot) = CreateRegionAmountUnitsPivot("PivotSourceShrinkFieldLostTest");

        // Sanity: the initial refresh materialized both data fields before the shrink.
        Text(sheet, 1, 7).Should().Be("Sum of Amount");
        Text(sheet, 1, 8).Should().Be("Sum of Units");

        // Delete the "Units" source column (D) -- the source now only spans A:C, so the "Sum of
        // Units" data field's SourceFieldIndex (3) is out of range for the new 3-column header set.
        for (uint row = 1; row <= 5; row++)
            sheet.ClearCell(row, 4);
        pivot.SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));

        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        var outcome = refresh.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();

        // Bug (before fix): ClearRefreshRanges wiped the previous render, the field-validity check
        // then aborted with LastRenderedRange = null and no replacement content -- every one of these
        // would have failed because the whole block was blank.
        Text(sheet, 1, 6).Should().Be("Region", "the still-valid row field must keep rendering, not vanish into a blank hole");
        Text(sheet, 1, 7).Should().Be("Sum of Amount", "the still-valid data field must keep rendering");
        Text(sheet, 2, 6).Should().Be("East");
        Number(sheet, 2, 7).Should().Be(25);
        Text(sheet, 3, 6).Should().Be("West");
        Number(sheet, 3, 7).Should().Be(45);
        Text(sheet, 4, 6).Should().Be("Grand Total");
        Number(sheet, 4, 7).Should().Be(70);

        // The dropped "Sum of Units" data field must not leave stale content behind either.
        Text(sheet, 1, 8).Should().BeEmpty("the invalid data field was dropped from the layout, so its old column must be cleared, not left stale");

        pivot.DataFields.Should().ContainSingle(field => field.Name == "Sum of Amount");
        pivot.LastRenderedRange.Should().NotBeNull();
    }

    // --- no-regression sibling: source loses rows only, no field index is invalidated ---

    [Fact]
    public void Refresh_SourceLosesRows_RefreshesCleanlyWithoutDroppingAnyField()
    {
        var (workbook, sheet, pivot) = CreateRegionAmountUnitsPivot("PivotSourceShrinkRowsLostTest");

        Number(sheet, 3, 7).Should().Be(45); // West / Sum of Amount before the shrink (Q1=20 + Q2=25)

        // Drop the last data row (West / Q2 / 25 / 5) -- shrinks SourceRange by one row only; no
        // field's SourceFieldIndex is affected.
        sheet.ClearCell(5, 1);
        sheet.ClearCell(5, 2);
        sheet.ClearCell(5, 3);
        sheet.ClearCell(5, 4);
        pivot.SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));

        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        var outcome = refresh.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();

        Text(sheet, 1, 6).Should().Be("Region");
        Text(sheet, 1, 7).Should().Be("Sum of Amount");
        Text(sheet, 1, 8).Should().Be("Sum of Units");
        Text(sheet, 2, 6).Should().Be("East");
        Number(sheet, 2, 7).Should().Be(25);
        Number(sheet, 2, 8).Should().Be(5);
        Text(sheet, 3, 6).Should().Be("West");
        Number(sheet, 3, 7).Should().Be(20, "the West/Q2 row was dropped by the shrink, leaving only the West/Q1 row (Amount=20)");
        Number(sheet, 3, 8).Should().Be(4);
        Text(sheet, 4, 6).Should().Be("Grand Total");
        Number(sheet, 4, 7).Should().Be(45);
        Number(sheet, 4, 8).Should().Be(9);

        pivot.DataFields.Should().HaveCount(2, "no field index was invalidated by a row-only shrink, so nothing should be dropped");
        pivot.LastRenderedRange.Should().NotBeNull();
    }
}
