using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public sealed class FormulaReferenceShifterAdoptionTests
{
    [Theory]
    [InlineData("=Sheet2!A1+$B2+C$3+$D$4", "Sheet2!B3+$B4+D$3+$D$4")]
    [InlineData("=SUM((A1,C2))", "SUM((B3,D4))")]
    [InlineData("=SUM(A1:C3 B2:D4)", "SUM(B3:D5 C4:E6)")]
    [InlineData("=SUM(A1:EndName)", "SUM(B3:ENDNAME)")]
    public void ConditionalFormatAndFormulaEntryPoints_UseIdenticalReferenceTransform(
        string formula,
        string expected)
    {
        var sheet = SheetId.New();
        var anchor = new CellAddress(sheet, 10, 10);
        var current = new CellAddress(sheet, 12, 11);
        var ast = FormulaEvaluator.ParseFormula(formula);

        var formulaShifted = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, current);
        var conditionalFormatShifted = ViewportConditionalFormatEvaluator
            .GetShiftedConditionalFormatFormula(ast, anchor, current);

        FormulaSerializer.Serialize(formulaShifted).Should().Be(expected);
        FormulaSerializer.Serialize(conditionalFormatShifted).Should().Be(expected);
    }

    [Fact]
    public void ConditionalFormatEntryPoint_HonorsPrecomputedNoRelativeReferenceFlag()
    {
        var sheet = SheetId.New();
        var ast = FormulaEvaluator.ParseFormula("=A1");

        ViewportConditionalFormatEvaluator.GetShiftedConditionalFormatFormula(
                ast,
                new CellAddress(sheet, 1, 1),
                new CellAddress(sheet, 2, 2),
                hasRelativeReferences: false)
            .Should().BeSameAs(ast);
    }
}
