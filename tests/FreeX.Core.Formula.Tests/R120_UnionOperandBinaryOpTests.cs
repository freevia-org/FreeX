using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R120-union-operand-binary-op: a parenthesized-union (multi-area) reference used directly as
/// an operand of &amp;, =, &lt;&gt;, &lt;, &gt;, &lt;=, &gt;= -- outside a function-call argument, where the
/// per-argument expansion loop (FormulaEvaluator.Functions.cs) already unwraps a UnionValue for
/// most consumers -- fell through EvaluateArrayOperand's generic UnionNode case
/// (FormulaEvaluator.References.cs) as a raw UnionValue that neither the ErrorValue nor RangeValue
/// guards in EvaluateBinaryOp caught. ConcatScalarOp's ValueToString had no UnionValue case and
/// fell to `v.ToString()`, embedding the literal .NET record dump into concatenated text; CompareValues
/// had no UnionValue case either and fell to TypeOrder's default bucket 4, so a comparison against a
/// union operand silently returned a constant boolean regardless of the referenced cells' actual
/// contents, instead of Excel's #VALUE!.
///
/// Fix: EvaluateBinaryOp (FormulaEvaluator.Operators.cs) now short-circuits to ErrorValue.Value as
/// soon as either evaluated operand is a UnionValue -- a single choke point ahead of the
/// Concat/Compare/Arith dispatch switch, rather than patching ValueToString/CompareValues/TypeOrder
/// individually. Arithmetic operators (+,-,*,/,^) are unaffected by this change in practice (they
/// already produced #VALUE! via CoerceToNumber/TryCoerceToNumberValue's default arms), but are also
/// exercised here as a no-regression check now that they pass through the same choke point.
///
/// Sibling finding note: this wave also fixed a UnionValue defect in TEXTJOIN
/// (R120_TextjoinUnionArgumentTests.cs / FormulaEvaluator.FunctionClassification.cs), but that is a
/// different context -- a union passed as a FUNCTION ARGUMENT is correctly materialized into a
/// synthetic range (matching AGGREGATE/SUM's existing union-unwrap behavior for most functions).
/// This fix only concerns a union used directly as an operand of a scalar binary operator OUTSIDE
/// any function call, where Excel has no range to reduce to and returns #VALUE! -- the two fixes
/// touch disjoint code paths (FormulaEvaluator.Functions.cs's per-argument loop vs
/// FormulaEvaluator.Operators.cs's EvaluateBinaryOp) and do not conflict.
/// </summary>
public sealed class R120_UnionOperandBinaryOpTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Workbook MakeWorkbook(out Sheet sheet, params (uint row, uint col, ScalarValue val)[] cells)
    {
        var workbook = new Workbook("Test");
        sheet = workbook.AddSheet("S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), val);
        return workbook;
    }

    [Fact]
    public void Concatenate_UnionOperandOnRight_ReturnsValueError_NotObjectDump()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new TextValue("a")),
            (2u, 1u, new TextValue("b")),
            (1u, 2u, new TextValue("c")),
            (2u, 2u, new TextValue("d")));

        var result = _eval.Evaluate("=\"X\"&(A1:A2,B1:B2)", sheet, workbook);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Concatenate_UnionOperandOnLeft_ReturnsValueError()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new TextValue("a")),
            (2u, 1u, new TextValue("b")),
            (1u, 2u, new TextValue("c")),
            (2u, 2u, new TextValue("d")));

        var result = _eval.Evaluate("=(A1:A2,B1:B2)&\"X\"", sheet, workbook);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Equal_UnionOperand_ReturnsValueError_NotFalse()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(5)),
            (1u, 2u, new NumberValue(5)),
            (2u, 2u, new NumberValue(5)));

        // Before the fix this silently evaluated to FALSE (UnionValue landed in TypeOrder's
        // default bucket 4, never equal to a number's bucket 1) regardless of the referenced
        // cells actually all being 5.
        var result = _eval.Evaluate("=(A1:A2,B1:B2)=5", sheet, workbook);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void GreaterThan_UnionOperand_ReturnsValueError_NotAlwaysTrue()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(1)),
            (1u, 2u, new NumberValue(1)),
            (2u, 2u, new NumberValue(1)));

        // Before the fix this silently evaluated to TRUE for every case (bucket 4 > bucket 1)
        // regardless of the actual cell contents.
        var result = _eval.Evaluate("=(A1:A2,B1:B2)>5", sheet, workbook);

        result.Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=(A1:A2,B1:B2)<>5")]
    [InlineData("=(A1:A2,B1:B2)<5")]
    [InlineData("=(A1:A2,B1:B2)<=5")]
    [InlineData("=(A1:A2,B1:B2)>=5")]
    public void AllComparisonOperators_UnionOperand_ReturnValueError(string formula)
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(5)),
            (1u, 2u, new NumberValue(5)),
            (2u, 2u, new NumberValue(5)));

        _eval.Evaluate(formula, sheet, workbook).Should().Be(ErrorValue.Value);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // No-regression siblings: the neighbouring paths this change must not disturb.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Arithmetic_UnionOperand_StillReturnsValueError_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(5)),
            (1u, 2u, new NumberValue(5)),
            (2u, 2u, new NumberValue(5)));

        _eval.Evaluate("=(A1:A2,B1:B2)+5", sheet, workbook).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Concatenate_PlainRangeOperands_StillWorkElementwise_NoRegression()
    {
        // A plain (non-union) RangeValue operand of & must still spill elementwise exactly as
        // before -- only the UnionValue case is short-circuited to an error.
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new TextValue("a")),
            (2u, 1u, new TextValue("b")));

        var result = _eval.Evaluate("=A1:A2&\"!\"", sheet, workbook);

        result.Should().BeOfType<RangeValue>();
        var range = (RangeValue)result;
        range.Cells[0, 0].Should().Be(new TextValue("a!"));
        range.Cells[1, 0].Should().Be(new TextValue("b!"));
    }

    [Fact]
    public void Equal_PlainScalarOperands_StillWork_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet, (1u, 1u, new NumberValue(5)));

        _eval.Evaluate("=A1=5", sheet, workbook).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Textjoin_UnionArgument_StillMaterializesAsRange_NotShortCircuited()
    {
        // Reconciliation with the sibling R120 TEXTJOIN fix: a union used as a FUNCTION ARGUMENT
        // must keep going through the per-argument materialization path (FormulaEvaluator.
        // FunctionClassification.cs / Functions.cs), not the new EvaluateBinaryOp short-circuit --
        // TEXTJOIN itself contains no binary operator over the union, so this test guards that the
        // two fixes stay on their own disjoint paths.
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new TextValue("a")),
            (2u, 1u, new TextValue("b")),
            (1u, 2u, new TextValue("c")),
            (2u, 2u, new TextValue("d")));

        _eval.Evaluate("=TEXTJOIN(\",\",TRUE,(A1:A2,B1:B2))", sheet, workbook)
            .Should().Be(new TextValue("a,b,c,d"));
    }
}
