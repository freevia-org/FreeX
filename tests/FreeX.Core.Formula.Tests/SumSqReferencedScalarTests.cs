using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

public sealed class SumSqReferencedScalarTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void SumSq_IncludesNumericDirectCellReferences()
    {
        var workbook = new Workbook("Formula");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(100));

        _evaluator.Evaluate("=SUMSQ(A1)", sheet, workbook)
            .Should().Be(new NumberValue(10_000));
        _evaluator.Evaluate("=SUMSQ(2,A1)", sheet, workbook)
            .Should().Be(new NumberValue(10_004));
        _evaluator.Evaluate("=SUM(SUMSQ(A1),1)", sheet, workbook)
            .Should().Be(new NumberValue(10_001));
    }

    [Fact]
    public void SumSq_DirectCellReferencePreservesRangeCoercionAndErrors()
    {
        var workbook = new Workbook("Formula");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), ErrorValue.NA);

        _evaluator.Evaluate("=SUMSQ(A1)", sheet, workbook).Should().Be(new NumberValue(0));
        _evaluator.Evaluate("=SUMSQ(A2)", sheet, workbook).Should().Be(new NumberValue(0));
        _evaluator.Evaluate("=SUMSQ(A3)", sheet, workbook).Should().Be(ErrorValue.NA);
    }
}
