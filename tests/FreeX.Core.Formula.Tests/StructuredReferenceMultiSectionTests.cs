using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Table1[[#Headers],[#Data],[Col]] / [[#Data],[#Totals],[Col]] multi-item specifiers (found by the
/// corpus sweep: Melon.Learning LMS sheet, 60 cells returned #NAME?). Verified against Excel.
/// </summary>
public class StructuredReferenceMultiSectionTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook workbook, Sheet sheet) Build(bool totals)
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Income"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        if (totals)
            sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(60));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, totals ? 5u : 4u, 2)),
            HeaderRowCount = 1,
            TotalsRowShown = totals,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Month"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Income"));
        sheet.StructuredTables.Add(table);
        return (workbook, sheet);
    }

    [Fact]
    public void HeadersAndData_SpanHeaderThroughLastDataRow()
    {
        var (wb, sheet) = Build(totals: false);
        _eval.Evaluate("=ROWS(Table1[[#Headers],[#Data],[Income]])", sheet, wb).Should().Be(new NumberValue(4));
        _eval.Evaluate("=SUMIF(Table1[[#Headers],[#Data],[Month]],\"Feb\",Table1[[#Headers],[#Data],[Income]])", sheet, wb)
            .Should().Be(new NumberValue(20));
    }

    [Fact]
    public void DataAndTotals_SpanDataThroughTotalsRow()
    {
        var (wb, sheet) = Build(totals: true);
        _eval.Evaluate("=ROWS(Table1[[#Data],[#Totals],[Income]])", sheet, wb).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void HeadersAndData_WithoutColumn_SpansWholeTableBody()
    {
        var (wb, sheet) = Build(totals: false);
        _eval.Evaluate("=COLUMNS(Table1[[#Headers],[#Data]])", sheet, wb).Should().Be(new NumberValue(2));
        _eval.Evaluate("=ROWS(Table1[[#Headers],[#Data]])", sheet, wb).Should().Be(new NumberValue(4));
    }
}
