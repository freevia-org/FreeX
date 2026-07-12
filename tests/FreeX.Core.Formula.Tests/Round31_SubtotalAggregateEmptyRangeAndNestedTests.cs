using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R31-formula-math-aggregate-1: SUBTOTAL(4/5,...) / AGGREGATE(4,...)/AGGREGATE(5,...) (MAX/MIN)
// over an all-non-numeric/empty range must return 0 (matching plain MAX()/MIN() and real Excel),
// not #DIV/0!. AVERAGE/STDEV/VAR (1,7,8,10,11) still correctly error on an empty sample.
//
// R31-formula-math-aggregate-2: the nested-SUBTOTAL/AGGREGATE exclusion must recognize a
// SUBTOTAL/AGGREGATE call ANYWHERE in a cell's formula text (e.g. "=1+SUBTOTAL(...)"), not just
// when the whole formula is literally that call, so nested cells aren't double-counted.
public partial class FunctionLibraryTests
{
    [Fact]
    public void Subtotal_FuncNum4_AllTextRange_ReturnsZeroNotDivByZero()
    {
        var sheet = MakeSheet((1, 1, new TextValue("hello")));
        _eval.Evaluate("=SUBTOTAL(4,A1:A1)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Subtotal_FuncNum5_AllTextRange_ReturnsZeroNotDivByZero()
    {
        var sheet = MakeSheet((1, 1, new TextValue("hello")));
        _eval.Evaluate("=SUBTOTAL(5,A1:A1)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Subtotal_FuncNum4_NonEmptyNumericRange_StillReturnsMax()
    {
        // Sibling case: MAX over a range that DOES contain numbers must still work normally.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(12)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=SUBTOTAL(4,A1:A3)", sheet).Should().Be(new NumberValue(12));
    }

    [Fact]
    public void Subtotal_FuncNum1_AllTextRange_StillErrorsDivByZero()
    {
        // Sibling case: AVERAGE (1) must still genuinely error on an empty/all-text sample,
        // unlike MAX/MIN (4/5).
        var sheet = MakeSheet((1, 1, new TextValue("hello")));
        _eval.Evaluate("=SUBTOTAL(1,A1:A1)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Subtotal_FuncNum9_ExcludesWrappedNestedSubtotalFormulaCell()
    {
        // A2's formula wraps the nested SUBTOTAL in "1+..." -- must still be recognized as
        // nested and excluded, not just a bare "SUBTOTAL(...)" formula.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "1+SUBTOTAL(9,A1:A1)",
            Value = new NumberValue(11)
        });

        _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_FuncNum9_ExcludesBarePrefixNestedSubtotalFormulaCell()
    {
        // Sibling case: the original bare "SUBTOTAL(...)" formula (no wrapping operator) must
        // still be recognized as nested and excluded.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "SUBTOTAL(9,A1:A1)",
            Value = new NumberValue(10)
        });

        _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet).Should().Be(new NumberValue(40));
    }
}

public partial class PhaseA2FunctionTests
{
    [Fact]
    public void Aggregate_FuncNum4_Option6_AllTextRange_ReturnsZeroNotDivByZero()
    {
        var (wb, sheet) = MakeWb((1, 1, new TextValue("hello")));
        _eval.Evaluate("=AGGREGATE(4,6,A1:A1)", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Aggregate_FuncNum5_Option6_AllTextRange_ReturnsZeroNotDivByZero()
    {
        var (wb, sheet) = MakeWb((1, 1, new TextValue("hello")));
        _eval.Evaluate("=AGGREGATE(5,6,A1:A1)", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Aggregate_FuncNum4_Option6_NonEmptyNumericRange_StillReturnsMax()
    {
        // Sibling case: MAX over a range that DOES contain numbers must still work normally.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(12)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(4,6,A1:A3)", sheet, wb).Should().Be(new NumberValue(12));
    }

    [Fact]
    public void Aggregate_Sum_Option0_ExcludesWrappedNestedSubtotalFormulaCell()
    {
        // A2's formula wraps the nested SUBTOTAL in "1+..." -- must still be recognized as
        // nested and excluded, not just a bare "SUBTOTAL(...)" formula.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "1+SUBTOTAL(9,A1:A1)",
            Value = new NumberValue(11)
        });

        _eval.Evaluate("=AGGREGATE(9,0,A1:A3)", sheet, wb).Should().Be(new NumberValue(40));
    }
}
