using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R47-formula-textjoin-textsplit-3-1: TEXTSPLIT's pad_with argument must distinguish "argument
/// slot genuinely omitted" (defaults to #N/A) from "argument explicitly supplied as a reference
/// to a blank cell" (must pad with the cell's actual -- blank -- value, not #N/A). Previously both
/// cases collapsed to the same BlankValue.Instance singleton by value-expansion time, so an
/// explicit blank-cell reference wrongly fell back to the omitted-argument #N/A default.
///
/// FormulaEvaluator.Functions.cs now substitutes a dedicated TextSplitOmittedPadArgumentValue
/// sentinel for argIndex 5 only when the raw AST node is a genuine OmittedArgumentNode, so
/// BuiltInFunctions.TextSplit.cs's Textsplit() can tell the two cases apart.
/// </summary>
public sealed class R47_TextSplitPadWithBlankRefTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    [Fact]
    public void Textsplit_PadWithExplicitReferenceToBlankCell_PadsWithBlank_NotNa()
    {
        // A1 = "a,b;c", A5 is a genuinely empty cell explicitly passed as pad_with.
        // The ragged second row ("c") needs one more column; it must be padded with A5's
        // (blank) value, not the #N/A reserved for a truly omitted pad_with.
        var sheet = Sheet((1, 1, new TextValue("a,b;c")));

        var rv = _eval.Evaluate("=TEXTSPLIT(A1,\",\",\";\",,,A5)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        rv.At(2, 2).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void Textsplit_PadWithTrulyOmitted_StillDefaultsToNa_NoRegression()
    {
        var rv = _eval.Evaluate("=TEXTSPLIT(\"a,b;c\",\",\",\";\")", Sheet())
            .Should().BeOfType<RangeValue>().Subject;

        rv.At(2, 2).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Textsplit_PadWithTrailingCommaOmitted_StillDefaultsToNa_NoRegression()
    {
        var rv = _eval.Evaluate("=TEXTSPLIT(\"a,b;c\",\",\",\";\",,,)", Sheet())
            .Should().BeOfType<RangeValue>().Subject;

        rv.At(2, 2).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Textsplit_NormalUsageWithoutPadWith_Unaffected()
    {
        // No ragged rows at all -- a plain TEXTSPLIT unaffected by the pad_with change.
        var rv = _eval.Evaluate("=TEXTSPLIT(\"a,b,c\",\",\")", Sheet())
            .Should().BeOfType<RangeValue>().Subject;

        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(3);
        rv.At(1, 1).Should().Be(new TextValue("a"));
        rv.At(1, 2).Should().Be(new TextValue("b"));
        rv.At(1, 3).Should().Be(new TextValue("c"));
    }

    [Fact]
    public void Textsplit_PadWithExplicitLiteralValue_StillPadsWithThatValue_NoRegression()
    {
        // An explicit, non-blank pad_with literal must still be used verbatim (unaffected by the
        // omitted-vs-blank-reference distinction, which only applies to the omitted case).
        var rv = _eval.Evaluate("=TEXTSPLIT(\"a,b;c\",\",\",\";\",,,\"X\")", Sheet())
            .Should().BeOfType<RangeValue>().Subject;

        rv.At(2, 2).Should().Be(new TextValue("X"));
    }
}
