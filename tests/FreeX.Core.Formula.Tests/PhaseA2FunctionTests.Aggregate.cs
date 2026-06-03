using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseA2FunctionTests
{
    // ── AGGREGATE ────────────────────────────────────────────────────────────

    [Fact]
    public void Aggregate_Sum_BasicRange()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        // function 9 = SUM, options 4 = ignore nothing
        _eval.Evaluate("=AGGREGATE(9,4,A1:A3)", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Aggregate_Sum_Option5IgnoresHiddenRows()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        sheet.HiddenRows.Add(2);

        _eval.Evaluate("=AGGREGATE(9,5,A1:A3)", sheet, wb).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Aggregate_Sum_Option4IncludesHiddenRows()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        sheet.HiddenRows.Add(2);

        _eval.Evaluate("=AGGREGATE(9,4,A1:A3)", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Aggregate_Sum_Option0IgnoresNestedSubtotalFormulaCell()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "SUBTOTAL(9,A1:A1)",
            Value = new NumberValue(10)
        });

        _eval.Evaluate("=AGGREGATE(9,0,A1:A3)", sheet, wb).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Aggregate_Sum_Option4IncludesNestedSubtotalFormulaCell()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "SUBTOTAL(9,A1:A1)",
            Value = new NumberValue(10)
        });

        _eval.Evaluate("=AGGREGATE(9,4,A1:A3)", sheet, wb).Should().Be(new NumberValue(50));
    }

    [Fact]
    public void Aggregate_Sum_IgnoresErrorsWhenOption6()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(9,6,A1:A3)", sheet, wb).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Aggregate_Sum_PropagatesErrorsWhenOption4()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero));
        _eval.Evaluate("=AGGREGATE(9,4,A1:A2)", sheet, wb).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Aggregate_Average_BasicRange()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)));
        _eval.Evaluate("=AGGREGATE(1,4,A1:A2)", sheet, wb).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Aggregate_Large_RequiresK()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(14,4,A1:A3,1)", sheet, wb).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Aggregate_Small_WithK()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(15,4,A1:A3,2)", sheet, wb).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Aggregate_ModeSngl_ReturnsFirstModeWhenCountsTie()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(2)),
            (4, 1, new NumberValue(1)));

        _eval.Evaluate("=AGGREGATE(13,4,A1:A4)", sheet, wb).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Aggregate_InvalidFuncNum_ReturnsValueError()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(1)));
        _eval.Evaluate("=AGGREGATE(20,4,A1)", sheet, wb).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Aggregate_Count()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new TextValue("x")),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(2,4,A1:A3)", sheet, wb).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Aggregate_Max()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(11)));
        _eval.Evaluate("=AGGREGATE(4,4,A1:A2)", sheet, wb).Should().Be(new NumberValue(11));
    }
}
