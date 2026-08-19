using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r146 REMEDIATION: the r146 fix wave taught <see cref="FilterCommand"/> to compute its first data
/// row via <see cref="FilterHiddenRowUpdater.GetFilterableFirstRow"/> (header/totals-row-aware),
/// matching the already header-aware <see cref="StructuredTableEditEffects.GetFilterableLastRow"/>
/// used for the end bound -- but left three siblings reachable from the SAME AutoFilter dropdown
/// still hardcoding <c>range.Start.Row + 1</c> for their start bound
/// (<see cref="TopBottomFilterCommand"/>, <see cref="AverageFilterCommand"/>,
/// <see cref="FilterConditionCommand"/>), plus a fourth, <see cref="ApplyStructuredTableFiltersCommand"/>
/// (the Slicer/table-resize recompute path -- see its real production call site at
/// <c>PivotTableSlicerCommands.cs:269</c>, <c>SelectSlicerItemsCommand.Apply</c>), which is the SECOND
/// round this enumeration gap has appeared. Every one of the four already used the header-aware
/// <c>GetFilterableLastRow</c> for its END bound, so they carried exactly the asymmetry the r146
/// finding named: header-aware end, header-naive start. For a Structured Table loaded with
/// <c>headerRowCount="0"</c> (a genuine, round-tripped Excel feature), <c>table.Range.Start.Row</c> IS
/// ITSELF a data row -- but the naive <c>+ 1</c> skipped it, leaving it permanently un-evaluated (and
/// so permanently visible) no matter what the user filtered on. Mirrors
/// <c>R146_HeaderlessTableFilterRecomputeTests</c> for these four sibling commands.
/// </summary>
public sealed class R146B_HeaderlessTableFilterSiblingCommandsTests
{
    private static StructuredTableModel AddTable(Sheet sheet, GridRange range, int? headerRowCount)
    {
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            Range = range,
            HeaderRowCount = headerRowCount,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Value"));
        sheet.StructuredTables.Add(table);
        return table;
    }

    [Fact]
    public void TopBottomFilterCommand_Apply_HeaderlessTable_EvaluatesFirstDataRowInRanking()
    {
        // headerRowCount=0: Range.Start.Row (row 1) IS itself a data row. Values 1,2,3,4 -- Top 3
        // keeps the three highest (2,3,4) and hides row 1 (value 1), the lowest.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        AddTable(sheet, range, headerRowCount: 0);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(4));

        var ctx = new TestCommandContext(wb);
        var command = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 3, top: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([1u],
            "row 1 is the first DATA row of a headerless table (headerRowCount=0), not a header, " +
            "and the lowest of the four values must be excluded by a Top-3 ranking");
    }

    [Fact]
    public void AverageFilterCommand_Apply_HeaderlessTable_EvaluatesFirstDataRowInStatistic()
    {
        // headerRowCount=0: values 1,2,3,4 -- data-body average is 2.5, so rows 1 and 2 (1, 2) are
        // NOT above average and must be hidden; rows 3 and 4 (3, 4) stay visible.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        AddTable(sheet, range, headerRowCount: 0);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(4));

        var ctx = new TestCommandContext(wb);
        var command = new AverageFilterCommand(sheet.Id, range, filterColOffset: 0, above: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([1u, 2u],
            "row 1 is the first DATA row of a headerless table and must be folded into the " +
            "data-body average (2.5) and evaluated against it like every other data row");
    }

    [Fact]
    public void FilterConditionCommand_Apply_HeaderlessTable_EvaluatesFirstDataRowAgainstCriterion()
    {
        // headerRowCount=0: North/South/North/North, custom criterion "equals South" keeps only row 2.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        AddTable(sheet, range, headerRowCount: 0);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));

        var ctx = new TestCommandContext(wb);
        var criterion = new TextEqualsFilterCriterion("South");
        var command = new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, criterion);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([1u, 3u, 4u],
            "row 1 is the first DATA row of a headerless table and must be evaluated against the " +
            "active custom criterion like every other data row");
    }

    [Fact]
    public void ApplyStructuredTableFiltersCommand_Apply_HeaderlessTable_EvaluatesFirstDataRow()
    {
        // The Slicer/table-resize recompute path: PivotTableSlicerCommands.cs:269
        // (SelectSlicerItemsCommand.Apply) sets table.FilterColumns then dispatches through this
        // exact command -- the same command ResizeStructuredTableCommand.RecomputeHiddenRows calls
        // (StructuredTableDesignCommands.cs:600) after a table resize.
        // headerRowCount=0: North/South/North/North, value-list filter ["South"] keeps only row 2.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        var table = AddTable(sheet, range, headerRowCount: 0);
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["South"]));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));

        var ctx = new TestCommandContext(wb);
        var command = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([1u, 3u, 4u],
            "row 1 is the first DATA row of a headerless table and must be evaluated against the " +
            "table's own value-list filter like every other data row");
    }

    // ---- No-regression siblings: default (headered) table behavior must be unchanged ----

    [Fact]
    public void TopBottomFilterCommand_Apply_HeaderedTable_StillSkipsHeaderRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        AddTable(sheet, range, headerRowCount: null);

        // Header row's own text would parse as non-numeric and must never be a ranking candidate.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));

        var ctx = new TestCommandContext(wb);
        var command = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u],
            "the header row (row 1) must never be evaluated or hidden by Top-N, and Top-2 of " +
            "(10,20,30) hides only the lowest, row 2 (10)");
    }

    [Fact]
    public void AverageFilterCommand_Apply_HeaderedTable_StillSkipsHeaderRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        AddTable(sheet, range, headerRowCount: null);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));

        var ctx = new TestCommandContext(wb);
        var command = new AverageFilterCommand(sheet.Id, range, filterColOffset: 0, above: true);

        command.Apply(ctx).Success.Should().BeTrue();

        // Data-body average of (10,20,30) is 20; only row 4 (30) is above it.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 3u],
            "the header row (row 1) must never be evaluated or hidden by Above-Average");
    }

    [Fact]
    public void FilterConditionCommand_Apply_HeaderedTable_StillSkipsHeaderRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        AddTable(sheet, range, headerRowCount: null);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));

        var ctx = new TestCommandContext(wb);
        var criterion = new TextEqualsFilterCriterion("South");
        var command = new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, criterion);

        command.Apply(ctx).Success.Should().BeTrue();

        // Header row's own text ("South") would pass the criterion if wrongly evaluated -- it must
        // stay un-evaluated (and so un-hidden) regardless.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u],
            "the header row (row 1) must never be evaluated or hidden by a custom criterion");
    }

    [Fact]
    public void ApplyStructuredTableFiltersCommand_Apply_HeaderedTable_StillSkipsHeaderRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        var table = AddTable(sheet, range, headerRowCount: null);
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["South"]));

        // Header row's own text ("South") would match the value-list filter if wrongly evaluated.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));

        var ctx = new TestCommandContext(wb);
        var command = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u],
            "the header row (row 1) must never be evaluated or hidden by the table's own value-list filter");
    }
}
