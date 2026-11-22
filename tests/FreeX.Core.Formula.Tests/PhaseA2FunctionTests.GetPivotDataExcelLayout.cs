using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseA2FunctionTests
{
    // Layout of an Excel-written matrix pivot (caption row "Sum of X" above "Row Labels" + date column items).
    // Expectations verified against real Excel (SP2016 corpus workbook, COM): DATE() and a serial match a
    // date item, a text "1/1/2016" does not (#REF!), the bare source-field name and the "Sum of" caption both
    // resolve the data field, and an empty pivot cell yields 0.
    private (Workbook wb, Sheet sheet) MakeExcelLayoutDatePivot()
    {
        var (wb, sheet) = MakeWb(
            (1, 26, new TextValue("Project")),
            (1, 27, new TextValue("Amount")),
            (1, 28, new TextValue("Month")),
            (2, 1, new TextValue("Sum of Amount")),
            (3, 1, new TextValue("Row Labels")),
            (3, 2, new DateTimeValue(42370)),
            (3, 3, new DateTimeValue(42401)),
            (3, 4, new TextValue("Grand Total")),
            (4, 1, new TextValue("Alpha")),
            (4, 2, new NumberValue(10)),
            (4, 3, new NumberValue(20)),
            (4, 4, new NumberValue(30)),
            (5, 1, new TextValue("Beta")),
            (5, 2, new NumberValue(5)),
            (5, 4, new NumberValue(5)),
            (6, 1, new TextValue("Grand Total")),
            (6, 2, new NumberValue(15)),
            (6, 3, new NumberValue(20)),
            (6, 4, new NumberValue(35)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 26), new CellAddress(sheet.Id, 4, 28)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 6, 4))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        return (wb, sheet);
    }

    [Theory]
    [InlineData("=GETPIVOTDATA(\"Amount\",A2,\"Month\",DATE(2016,1,1))", 15, null)]
    [InlineData("=GETPIVOTDATA(\"Amount\",A2,\"Month\",DATE(2016,2,1))", 20, null)]
    [InlineData("=GETPIVOTDATA(\"Sum of Amount\",A2,\"Month\",DATE(2016,2,1))", 20, null)]
    [InlineData("=GETPIVOTDATA(\"Amount\",A2,\"Month\",42370)", 15, null)]
    [InlineData("=GETPIVOTDATA(\"amount\",A2,\"month\",DATE(2016,1,1))", 15, null)]
    [InlineData("=GETPIVOTDATA(\"Amount\",A2)", 35, null)]
    [InlineData("=GETPIVOTDATA(\"Amount\",A2,\"Project\",\"Alpha\")", 30, null)]
    [InlineData("=GETPIVOTDATA(\"Amount\",A2,\"Project\",\"Alpha\",\"Month\",DATE(2016,2,1))", 20, null)]
    [InlineData("=GETPIVOTDATA(\"Amount\",A2,\"Project\",\"Beta\",\"Month\",DATE(2016,2,1))", 0, null)]
    public void GetPivotData_ExcelLayoutDateColumnItems_MatchExcel(string formula, double expected, string? unused)
    {
        _ = unused;
        var (wb, sheet) = MakeExcelLayoutDatePivot();
        _eval.Evaluate(formula, sheet, wb).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=GETPIVOTDATA(\"Amount\",A2,\"Month\",\"1/1/2016\")")]
    [InlineData("=GETPIVOTDATA(\"Amount\",A2,\"Month\",DATE(2016,3,1))")]
    [InlineData("=GETPIVOTDATA(\"Missing\",A2)")]
    public void GetPivotData_ExcelLayoutDateColumnItems_UnmatchedIsRef(string formula)
    {
        var (wb, sheet) = MakeExcelLayoutDatePivot();
        _eval.Evaluate(formula, sheet, wb).Should().Be(ErrorValue.Ref);
    }
}
