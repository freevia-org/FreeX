using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R26-table-structured-ref-deep-3: a table column literally named with a colon (e.g. "Q1:Q2") must
// resolve as that single literal column, not be mis-parsed as a "Q1" through "Q2" column-range
// selector. Verifies the bug case alongside the sibling already-working colon-range case to confirm
// no regression to genuine multi-column range selectors.
public sealed class R26_StructuredRefColonColumnNameTests
{
    [Fact]
    public void ColonNamedColumn_BareSelector_ResolvesLiteralColumnNotRange()
    {
        var (workbook, sheet) = CreateWorkbookWithColonNamedColumn();
        var evaluator = new FormulaEvaluator();

        // Table1[Q1:Q2] must resolve to the single literal column "Q1:Q2" (value 6), not the
        // range spanning columns Q1 (A) and Q2 (B) (which would sum to 1+2+3+4=10).
        var result = evaluator.Evaluate("=SUM(Table1[Q1:Q2])", sheet, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void GenuineColumnRange_TwoBracketedColumnNames_StillResolvesAsRange()
    {
        // Sibling already-working case: an explicit bracketed range [Q1]:[Q2] over two distinct
        // columns must still resolve as a multi-column range, unaffected by the literal-name guard.
        var (workbook, sheet) = CreateWorkbookWithColonNamedColumn();
        var evaluator = new FormulaEvaluator();

        var result = evaluator.Evaluate("=SUM(Table1[[Q1]:[Q2]])", sheet, workbook);

        result.Should().Be(new NumberValue(10));
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbookWithColonNamedColumn()
    {
        var workbook = new Workbook("StructuredRefColonTest");
        var sheet = workbook.AddSheet("Data");

        // Columns: A="Q1", B="Q2", C="Q1:Q2" (a literal, perfectly legal Excel header), D="Total"
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Q1:Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Total"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(5));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(11));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 4)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Q1"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Q2"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Q1:Q2"));
        table.Columns.Add(new StructuredTableColumnModel(4, "Total"));
        sheet.StructuredTables.Add(table);

        return (workbook, sheet);
    }
}
