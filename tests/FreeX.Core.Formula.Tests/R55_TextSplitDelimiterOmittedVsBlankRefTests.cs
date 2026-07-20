using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R55-formula-text-split-before-after-5-1: TEXTSPLIT's col_delimiter/row_delimiter arguments must
/// distinguish a genuinely-omitted argument slot (e.g. the double-comma in TEXTSPLIT(A1,,";"),
/// which means "don't split on that axis") from an explicit argument that evaluates to blank (e.g.
/// TEXTSPLIT(A1,B1,";") where B1 is a never-set cell). Excel coerces the blank cell reference to ""
/// for the delimiter argument, and TEXTSPLIT explicitly rejects an empty-string delimiter with
/// #VALUE! -- exactly like a literal ="" delimiter already does. Previously both cases collapsed to
/// the same BlankValue.Instance singleton by value-expansion time, so an explicit blank-cell-reference
/// delimiter was wrongly treated as "axis omitted" and silently produced a valid split instead of
/// #VALUE!.
///
/// FormulaEvaluator.Functions.cs now substitutes a dedicated TextSplitOmittedDelimiterArgumentValue
/// sentinel for argIndex 1/2 only when the raw AST node is a genuine OmittedArgumentNode, so
/// BuiltInFunctions.TextSplit.cs's TryCollectTextDelimiters can tell the two cases apart.
/// </summary>
public sealed class R55_TextSplitDelimiterOmittedVsBlankRefTests
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
    public void Textsplit_ExplicitBlankCellReferenceColDelimiter_IsValueError()
    {
        // A1 = "a;b", B1 is a genuinely empty cell explicitly passed as col_delimiter.
        // Excel coerces the blank B1 reference to "" and rejects the empty-string delimiter.
        var sheet = Sheet((1, 1, new TextValue("a;b")));

        var result = _eval.Evaluate("=TEXTSPLIT(A1,B1,\";\")", sheet);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Textsplit_OmittedColDelimiter_StillSplitsOnRowDelimiter_NoRegression()
    {
        // Sibling: the col_delimiter slot is genuinely omitted (double-comma), not an explicit
        // blank-cell reference -- must still split only on the row delimiter, not error.
        var sheet = Sheet((1, 1, new TextValue("a;b")));

        var rv = _eval.Evaluate("=TEXTSPLIT(A1,,\";\")", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(1);
        rv.At(1, 1).Should().Be(new TextValue("a"));
        rv.At(2, 1).Should().Be(new TextValue("b"));
    }

    [Fact]
    public void Textsplit_ExplicitBlankCellReferenceRowDelimiter_IsValueError()
    {
        // Same distinction, but on the row_delimiter axis (argIndex 2).
        var sheet = Sheet((1, 1, new TextValue("a;b")));

        var result = _eval.Evaluate("=TEXTSPLIT(A1,\";\",B1)", sheet);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Textsplit_RowDelimiterTrulyAbsent_StillSplitsOnColDelimiterOnly_NoRegression()
    {
        // No third argument at all (not even a trailing comma) -- row axis genuinely omitted.
        var sheet = Sheet((1, 1, new TextValue("a;b")));

        var rv = _eval.Evaluate("=TEXTSPLIT(A1,\";\")", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(2);
        rv.At(1, 1).Should().Be(new TextValue("a"));
        rv.At(1, 2).Should().Be(new TextValue("b"));
    }
}
