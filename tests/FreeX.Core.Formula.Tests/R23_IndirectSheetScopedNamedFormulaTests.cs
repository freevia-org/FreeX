using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R23-name-scope-resolution-1: INDIRECT("Name") must respect Excel's sheet-scope precedence
/// (§18.2.6) — a sheet-scoped named FORMULA shadows a same-named workbook-global named RANGE on
/// that sheet, regardless of the shadowed name's kind. Before the fix, TryResolveIndirectRangeReference
/// checked the range-only ctx.TryResolveNamedRange first, so a workbook-global range was always
/// found and returned before the formula-aware fallback ever ran.
/// </summary>
public class R23_IndirectSheetScopedNamedFormulaTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void Indirect_SheetScopedNamedFormula_ShadowsWorkbookGlobalRange_OnMatchingSheet()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // Workbook-global: Data -> Sheet1!$A$1:$A$5, filled with 100s.
        for (uint r = 1; r <= 5; r++)
            sheet1.SetCell(new CellAddress(sheet1.Id, r, 1), new NumberValue(100));
        workbook.DefineNamedRange(
            "Data",
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 5, 1)));

        // Sheet2-scoped: Data = OFFSET(Sheet2!$A$1,0,0,COUNTA(Sheet2!$A:$A),1) — must shadow the
        // workbook-global range when evaluated from Sheet2.
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(2));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new NumberValue(3));
        workbook.DefineNamedFormula(
            "Data",
            "OFFSET(Sheet2!$A$1,0,0,COUNTA(Sheet2!$A:$A),1)",
            sheet2.Id);

        var result = _evaluator.Evaluate("=SUM(INDIRECT(\"Data\"))", sheet2, workbook);

        // Sheet2's dynamic range (1+2+3=6), NOT Sheet1's global range (100*5=500).
        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Indirect_NamedRange_FallsBackToWorkbookGlobal_WhenNoSheetScopedFormulaOnThatSheet()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        for (uint r = 1; r <= 5; r++)
            sheet1.SetCell(new CellAddress(sheet1.Id, r, 1), new NumberValue(100));
        workbook.DefineNamedRange(
            "Data",
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 5, 1)));

        // Sheet2 has the scoped formula, but we evaluate from Sheet1 (no scoped binding there),
        // so INDIRECT("Data") must still resolve to the workbook-global range.
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        workbook.DefineNamedFormula(
            "Data",
            "OFFSET(Sheet2!$A$1,0,0,COUNTA(Sheet2!$A:$A),1)",
            sheet2.Id);

        var result = _evaluator.Evaluate("=SUM(INDIRECT(\"Data\"))", sheet1, workbook);

        result.Should().Be(new NumberValue(500));
    }
}
