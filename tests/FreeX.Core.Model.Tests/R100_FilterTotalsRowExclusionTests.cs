using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R100-commands-filter-totalsrow-1: a structured table's shown Totals Row must never be treated as
/// a filterable/matchable data row by any interactively-triggered AutoFilter mechanism (value-list,
/// Top-N, Above/Below-Average, custom-condition) nor by the Slicer item list. Real Excel always
/// keeps the Totals Row visible and excludes it from the filterable data set entirely -- it is never
/// offered as a selectable value, never hidden as a filter side effect, and never averaged/ranked.
/// Mirrors the totals-row-aware bound (<c>StructuredTableEditEffects.GetDataBodyRowBounds</c>) every
/// other table-editing command (Sort/InsertDeleteRows/InsertDeleteColumns) already uses.
/// </summary>
public sealed class R100_FilterTotalsRowExclusionTests
{
    private static StructuredTableModel AddTable(Sheet sheet, GridRange range, bool totalsRowShown)
    {
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            Range = range,
            TotalsRowShown = totalsRowShown,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Status"));
        sheet.StructuredTables.Add(table);
        return table;
    }

    [Fact]
    public void FilterCommand_ValueListFilter_TableWithTotalsRow_NeverHidesTotalsRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // Table A1:A5 -- header row 1, data rows 2-4, Totals Row shown at row 5.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        AddTable(sheet, range, totalsRowShown: true);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Keep"));
        // The Totals Row's own displayed value ("Drop" here, standing in for a totals label/function
        // result) would fail a "Keep"-only value filter if it were wrongly treated as a data row.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Drop"));

        var ctx = new TestCommandContext(wb);
        var command = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Keep"]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(3u, "row 3 (\"Drop\") fails the value-list filter");
        sheet.FilterHiddenRows.Should().NotContain(5u,
            "the Totals Row must never be hidden as a filter side effect -- Excel keeps it visible and excludes it from the filterable data set");
    }

    [Fact]
    public void TopBottomFilterCommand_TopN_TableWithTotalsRow_ExcludesTotalsRowFromRankingAndHiding()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        AddTable(sheet, range, totalsRowShown: true);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        // Totals Row: a SUM-like grand total (90) that would dominate a "Top 1" ranking if wrongly
        // included -- if the bug is present, this row is kept visible (it's numerically the biggest)
        // and BOTH real data rows are hidden instead.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(90));

        var ctx = new TestCommandContext(wb);
        var command = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 1, top: true);

        command.Apply(ctx).Success.Should().BeTrue();

        // Top-1 over the real data body (10, 50, 30) keeps only row 3 (50).
        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
        // The Totals Row must never be a Top-N candidate nor be hidden by this mechanism.
        sheet.FilterHiddenRows.Should().NotContain(5u,
            "the Totals Row must be excluded from the Top-N ranking data set entirely");
    }

    [Fact]
    public void AverageFilterCommand_AboveAverage_TableWithTotalsRow_ExcludesTotalsRowFromStatistic()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        AddTable(sheet, range, totalsRowShown: true);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sales"));
        // Data body average is (10 + 50 + 30) / 3 = 30, so only row 3 (50) is above average.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        // Totals Row grand total -- deliberately a small value (5) so that, if wrongly folded into
        // the average statistic, it pulls the average DOWN to (10+50+30+5)/4 = 23.75 and the Totals
        // Row itself (5 < 23.75) would then be classified as "below average" and hidden -- unlike the
        // correct DATA-BODY-only average of (10+50+30)/3 = 30, under which the Totals Row is simply
        // never evaluated at all.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(5));

        var ctx = new TestCommandContext(wb);
        var command = new AverageFilterCommand(sheet.Id, range, filterColOffset: 0, above: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u, "50 is above the DATA-BODY average of 30");
        sheet.FilterHiddenRows.Should().Contain(4u, "30 is not strictly above the DATA-BODY average of 30");
        sheet.FilterHiddenRows.Should().NotContain(5u,
            "the Totals Row must be excluded from the Above-Average statistic and never hidden by it");
    }

    [Fact]
    public void FilterConditionCommand_CustomCriterion_TableWithTotalsRow_NeverHidesTotalsRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        AddTable(sheet, range, totalsRowShown: true);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        // Totals Row value (5) would fail a ">= 10" criterion if wrongly matched as a data row.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(5));

        var ctx = new TestCommandContext(wb);
        var criterion = new NumberGreaterThanOrEqualFilterCriterion(10);
        var command = new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, criterion);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
        sheet.FilterHiddenRows.Should().NotContain(5u,
            "the Totals Row must never be evaluated against (or hidden by) a custom AutoFilter criterion");
    }

    [Fact]
    public void SlicerItemResolver_TableWithTotalsRow_ExcludesTotalsRowValueFromAvailableItems()
    {
        var workbook = new Workbook("TableSlicer");
        var sheet = workbook.AddSheet("Tasks");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        var table = AddTable(sheet, range, totalsRowShown: true);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Admin"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Sales"));
        // Totals Row label -- must never appear as a slicer item.
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Total"));

        var slicer = new SlicerModel
        {
            Name = "Category",
            SourceTableId = table.Id,
            SourceTableColumnId = 1,
        };

        var items = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);

        items.Should().Equal("Admin", "Sales");
        items.Should().NotContain("Total",
            "the Totals Row must never be offered as a selectable slicer item");
    }

    // ---- No-regression siblings ----

    [Fact]
    public void FilterCommand_TableWithoutTotalsRow_StillFiltersLastDataRow()
    {
        // Regression guard: when the table's Totals Row is NOT shown, table.Range.End.Row IS a real
        // data row and must still participate in filtering exactly as before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        AddTable(sheet, range, totalsRowShown: false);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Drop"));

        var ctx = new TestCommandContext(wb);
        var command = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Keep"]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(4u,
            "with no Totals Row shown, the table's last row is a normal data row and must still be filterable");
    }

    [Fact]
    public void FilterCommand_PlainWorksheetRange_NoStructuredTable_UnaffectedByTotalsRowLogic()
    {
        // Regression guard: a plain (non-table) AutoFilter range must behave exactly as before --
        // GetFilterableLastRow only special-cases a range that matches a structured table's Range.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Keep"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));

        var ctx = new TestCommandContext(wb);
        var command = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Keep"]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u]);
    }
}
