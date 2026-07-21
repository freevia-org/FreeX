using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-56 commands-bucket fixes:
/// - R56-commands-insert-delete-shift-5-1: a whole-row DELETE that consumes exactly a structured
///   table's Totals row must reset TotalsRowShown, matching Excel's own automatic "Total Row"
///   uncheck when the physical totals row no longer exists.
/// - R56-services-autofilter-sort-5-1: Top-10/Bottom-10 and Above/Below-Average AutoFilter must
///   scope their statistic (heap boundary / average) to rows still VISIBLE under another active
///   column's filter, not the entire column.
/// - R56-io-table-listobject-5-3: a custom (non-built-in) totals-row formula must be captured with
///   TotalsRowFunction="custom", matching Excel's own table1.xml serialization.
/// </summary>
public sealed class R56_CommandsBucketTests
{
    // ── R56-commands-insert-delete-shift-5-1 ──────────────────────────────────

    [Fact]
    public void DeleteRows_ConsumingTotalsRow_ResetsTotalsRowShown()
    {
        // Table1 spans A5:C10: header row 5, data rows 6-9, totals row 10 (TotalsRowShown=true).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var sid = sheet.Id;

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sid, 5, 1), new CellAddress(sid, 10, 3)),
            TotalsRowShown = true,
            HeaderRowCount = 1,
            TotalsRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "A"),
                new StructuredTableColumnModel(2, "B"),
                new StructuredTableColumnModel(3, "C")
            }
        };
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);

        // Whole-row "Delete Sheet Rows" on row 10 only -- just the totals row.
        var command = new DeleteRowsCommand(sid, startRow: 10, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.StructuredTables.Single(t => t.Id == 1);
        shifted.Range.End.Row.Should().Be(9, "the table should have shrunk to exclude the deleted totals row");
        shifted.TotalsRowShown.Should().BeFalse(
            "the physical totals row no longer exists after the delete, so Excel automatically unchecks Total Row " +
            "and row 9 (genuine surviving data) must not be mislabeled as the totals row");
    }

    [Fact]
    public void DeleteRows_NotConsumingTotalsRow_LeavesTotalsRowShownUnchanged()
    {
        // Sibling no-regression: deleting an ordinary DATA row (not the totals row) must leave
        // TotalsRowShown untouched.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var sid = sheet.Id;

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sid, 5, 1), new CellAddress(sid, 10, 3)),
            TotalsRowShown = true,
            HeaderRowCount = 1,
            TotalsRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "A"),
                new StructuredTableColumnModel(2, "B"),
                new StructuredTableColumnModel(3, "C")
            }
        };
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);

        // Delete row 7 -- a data row, well away from the totals row (10).
        var command = new DeleteRowsCommand(sid, startRow: 7, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.StructuredTables.Single(t => t.Id == 1);
        shifted.Range.End.Row.Should().Be(9, "the table shrinks by one row as the deleted row is removed");
        shifted.TotalsRowShown.Should().BeTrue("the totals row itself was never touched by this delete");
    }

    // ── R56-services-autofilter-sort-5-1 ──────────────────────────────────────

    [Fact]
    public void TopBottomFilterCommand_TopN_ScopesBoundaryToRowsVisibleUnderOtherColumnFilter()
    {
        // A1:B6, header row 1: Region (col A) / Sales (col B).
        // Row2 East/100, Row3 West/1000000, Row4 East/150, Row5 West/2000000, Row6 East/50.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sid, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sid, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sid, 3, 2), new NumberValue(1000000));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sid, 4, 2), new NumberValue(150));
        sheet.SetCell(new CellAddress(sid, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sid, 5, 2), new NumberValue(2000000));
        sheet.SetCell(new CellAddress(sid, 6, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sid, 6, 2), new NumberValue(50));

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 6, 2));
        var ctx = new TestCommandContext(wb);

        // First: value filter Region='East' hides rows 3 and 5 (the West rows).
        new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["East"]).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain([3u, 5u]);

        // Then: Top 1 on the Sales column, scoped to the visible East rows (100, 150, 50) ->
        // boundary must be 150 (row 4), NOT 2000000 (a hidden West row).
        var top1 = new TopBottomFilterCommand(sid, range, filterColOffset: 1, count: 1, top: true);
        top1.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(4u, "row 4 (150) is the highest value among the VISIBLE East rows and must be kept");
        sheet.FilterHiddenRows.Should().Contain(2u, "row 2 (100) is below the Top-1 boundary among visible rows");
        sheet.FilterHiddenRows.Should().Contain(6u, "row 6 (50) is below the Top-1 boundary among visible rows");
    }

    [Fact]
    public void TopBottomFilterCommand_TopN_WithNoOtherActiveFilter_StillScopesOverWholeColumn()
    {
        // Sibling no-regression: with no other column filter active, Top-N must still behave
        // exactly as before (boundary computed over the full data range).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sid, 5, 1), new NumberValue(40));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));
        var ctx = new TestCommandContext(wb);

        var command = new TopBottomFilterCommand(sid, range, filterColOffset: 0, count: 2, top: true);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
        sheet.FilterHiddenRows.Should().NotContain(5u);
    }

    [Fact]
    public void AverageFilterCommand_AboveAverage_ScopesAverageToRowsVisibleUnderOtherColumnFilter()
    {
        // Same dataset as the Top-N test above: Above-Average must average only the visible East
        // rows (100, 150, 50 -> avg 100), not the entire column.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sid, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sid, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sid, 3, 2), new NumberValue(1000000));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sid, 4, 2), new NumberValue(150));
        sheet.SetCell(new CellAddress(sid, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sid, 5, 2), new NumberValue(2000000));
        sheet.SetCell(new CellAddress(sid, 6, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sid, 6, 2), new NumberValue(50));

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 6, 2));
        var ctx = new TestCommandContext(wb);

        new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["East"]).Apply(ctx).Success.Should().BeTrue();

        var aboveAvg = new AverageFilterCommand(sid, range, filterColOffset: 1, above: true);
        aboveAvg.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(4u, "150 is above the 100-average of the VISIBLE East rows (100,150,50) and must be kept");
        sheet.FilterHiddenRows.Should().Contain(2u, "100 is not strictly above the 100-average of the visible rows");
        sheet.FilterHiddenRows.Should().Contain(6u, "50 is below the 100-average of the visible rows");
    }

    // ── R56-io-table-listobject-5-3 ────────────────────────────────────────────

    [Fact]
    public void HideTotalsRow_CustomAggregateFormula_IsCapturedAsCustomFunction()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Amount", TotalsRowFunction: "sum"),
                new StructuredTableColumnModel(2, "Refunds")
            }
        };
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Refunds"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(300));
        // User directly types a custom totals formula that isn't a recognized SUBTOTAL(n,[Column])
        // built-in shape.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromFormula("SUM(Sales[Amount])-SUM(Sales[Refunds])"));
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);

        var hide = new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: false);
        hide.Apply(ctx).Success.Should().BeTrue();

        var hiddenTable = sheet.StructuredTables.Single(t => t.Id == table.Id);
        hiddenTable.Columns[0].TotalsRowFunction.Should().Be(
            "custom",
            "ECMA-376 18.3.1.90: a non-built-in totals formula must be captured with totalsRowFunction=\"custom\", " +
            "matching how real Excel always serializes a directly-typed custom total");
        hiddenTable.Columns[0].TotalsRowFormula.Should().Be("SUM(Sales[Amount])-SUM(Sales[Refunds])");
    }

    [Fact]
    public void HideTotalsRow_RecognizedSubtotalAggregate_IsStillCapturedAsBuiltinFunction()
    {
        // Sibling no-regression: a recognized SUBTOTAL(n,[Column]) shape must still map to its
        // built-in function name (e.g. "sum"), not be mislabeled "custom".
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            TotalsRowShown = true,
            Columns = { new StructuredTableColumnModel(1, "Amount", TotalsRowFunction: "sum") }
        };
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromFormula("SUBTOTAL(109,[Amount])"));
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);

        var hide = new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: false);
        hide.Apply(ctx).Success.Should().BeTrue();

        var hiddenTable = sheet.StructuredTables.Single(t => t.Id == table.Id);
        hiddenTable.Columns[0].TotalsRowFunction.Should().Be("sum");
        hiddenTable.Columns[0].TotalsRowFormula.Should().BeNull();
    }
}
