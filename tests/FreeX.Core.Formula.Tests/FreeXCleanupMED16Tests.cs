using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for cleanup batch MED16:
///  - P103: SUBTOTAL/AGGREGATE's direct-range fast path must defer to the slow path
///    (which resolves named FORMULAs via TryEvaluateNamedFormula) instead of returning
///    #NAME? when a workbook-global name refers to a dynamic-range formula (e.g. an
///    OFFSET/COUNTA pattern) rather than a plain cell range.
/// </summary>
public sealed class FreeXCleanupMED16Tests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void Subtotal_WithWorkbookGlobalNamedDynamicRangeFormula_ComputesSumInsteadOfNameError()
    {
        // Classic dynamic-range pattern: MyDyn = OFFSET(Sheet1!$A$1,0,0,COUNTA(Sheet1!$A:$A),1)
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        workbook.NamedFormulas["MyDyn"] = "OFFSET(Sheet1!$A$1,0,0,COUNTA(Sheet1!$A:$A),1)";

        var result = _evaluator.Evaluate("=SUBTOTAL(9,MyDyn)", sheet, workbook);

        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Aggregate_WithWorkbookGlobalNamedDynamicRangeFormula_ComputesSumInsteadOfNameError()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        workbook.NamedFormulas["MyDyn"] = "OFFSET(Sheet1!$A$1,0,0,COUNTA(Sheet1!$A:$A),1)";

        var result = _evaluator.Evaluate("=AGGREGATE(9,5,MyDyn)", sheet, workbook);

        result.Should().Be(new NumberValue(6));
    }
}
