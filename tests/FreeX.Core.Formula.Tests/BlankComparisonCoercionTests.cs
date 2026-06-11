using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Verifies that comparison operators coerce blank (empty cell) operands to match the other
/// operand's type class, matching Excel semantics.
/// Excel: =A1=0 → TRUE, =A1="" → TRUE, =A1=FALSE → TRUE when A1 is empty.
/// </summary>
public sealed class BlankComparisonCoercionTests
{
    private readonly FormulaEvaluator _eval = new();

    // A1 is never set in any of these tests — it stays blank (BlankValue).
    private static Sheet EmptySheet() => new(SheetId.New(), "S");

    // ── Blank vs Number ──

    [Fact]
    public void BlankEqualsZero_IsTrue()
    {
        _eval.Evaluate("=A1=0", EmptySheet()).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void BlankNotEqualZero_IsFalse()
    {
        _eval.Evaluate("=A1<>0", EmptySheet()).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void BlankGreaterThanNegativeOne_IsTrue()
    {
        _eval.Evaluate("=A1>-1", EmptySheet()).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void BlankLessThanOne_IsTrue()
    {
        _eval.Evaluate("=A1<1", EmptySheet()).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void BlankGreaterOrEqualZero_IsTrue()
    {
        _eval.Evaluate("=A1>=0", EmptySheet()).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void BlankLessOrEqualZero_IsTrue()
    {
        _eval.Evaluate("=A1<=0", EmptySheet()).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void ZeroEqualsBlank_IsTrue()
    {
        _eval.Evaluate("=0=A1", EmptySheet()).Should().Be(new BoolValue(true));
    }

    // ── Blank vs Text ──

    [Fact]
    public void BlankEqualsEmptyString_IsTrue()
    {
        _eval.Evaluate("=A1=\"\"", EmptySheet()).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void BlankLessThanNonEmptyText_IsTrue()
    {
        _eval.Evaluate("=A1<\"a\"", EmptySheet()).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void NonEmptyTextGreaterThanBlank_IsTrue()
    {
        var sheet = EmptySheet();
        // A2 (row 2) has "hello"; A1 (row 1) is blank
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("hello"));
        _eval.Evaluate("=A2>A1", sheet).Should().Be(new BoolValue(true));
    }

    // ── Blank vs Bool ──

    [Fact]
    public void BlankEqualsFalse_IsTrue()
    {
        _eval.Evaluate("=A1=FALSE", EmptySheet()).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void BlankLessThanTrue_IsTrue()
    {
        _eval.Evaluate("=A1<TRUE", EmptySheet()).Should().Be(new BoolValue(true));
    }

    // ── Blank vs Blank ──

    [Fact]
    public void BlankEqualsBlank_IsTrue()
    {
        // A1 and A2 are both never set — both blank
        _eval.Evaluate("=A1=A2", EmptySheet()).Should().Be(new BoolValue(true));
    }

    // ── Regressions: non-blank mixed-type ordering must be unchanged ──

    [Fact]
    public void NumberVsText_NotCoerced_IsFalse()
    {
        // 5="5" must remain FALSE — blank coercion must not affect non-blank mixed types
        _eval.Evaluate("=5=\"5\"", EmptySheet()).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void BoolGreaterThanText_StaysTrue()
    {
        // TRUE>"text" must remain TRUE (bool ranks above text)
        _eval.Evaluate("=TRUE>\"text\"", EmptySheet()).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void NumberLessThanText_StaysTrue()
    {
        // 5<"5" must remain TRUE (number ranks below text)
        _eval.Evaluate("=5<\"5\"", EmptySheet()).Should().Be(new BoolValue(true));
    }

    // ── Blank coercion in IF — the canonical real-world pattern ──

    [Fact]
    public void If_BlankEqualsZero_TakesTrueBranch()
    {
        // =IF(A1=0,"yes","no") with A1 empty — Excel returns "yes"
        _eval.Evaluate("=IF(A1=0,\"yes\",\"no\")", EmptySheet())
            .Should().Be(new TextValue("yes"));
    }

    [Fact]
    public void If_BlankEqualsEmptyString_TakesTrueBranch()
    {
        // =IF(A1="","empty","not") with A1 empty — Excel returns "empty"
        _eval.Evaluate("=IF(A1=\"\",\"empty\",\"not\")", EmptySheet())
            .Should().Be(new TextValue("empty"));
    }
}
