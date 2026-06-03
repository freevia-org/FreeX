using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FormulaEvaluatorTests
{
    // ── Cell references ──

    [Fact]
    public void CellRef_ReadsValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        _evaluator.Evaluate("=A1", sheet).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void CellRef_EmptyCell_ReturnsBlank()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=A1", sheet).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void CellRef_Arithmetic()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));
        _evaluator.Evaluate("=A1+B1", sheet).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void RepeatedFormulaTextCache_UpdatesWhenFormulaTextChanges()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));

        _evaluator.Evaluate("=A1+B1", sheet).Should().Be(new NumberValue(15));
        _evaluator.Evaluate("=A1-B1", sheet).Should().Be(new NumberValue(5));
        _evaluator.Evaluate("=A1+B1", sheet).Should().Be(new NumberValue(15));
    }
}
