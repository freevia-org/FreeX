using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-21 review fixes for BuiltInFunctions.EngineeringComplex.cs:
///   R21-engineering-functions-1: IMSUB/IMDIV/COMPLEX must broadcast over RangeValue args,
///     mirroring the existing IMPOWER broadcast pattern, instead of returning #NUM!/#VALUE!.
///   R21-engineering-functions-2: IMSUM/IMPRODUCT must return #NUM! when arguments mix "i"
///     and "j" imaginary-unit notation, matching Excel.
///   R21-engineering-functions-3: FormatComplex's blanket &lt;1e-14 snap-to-zero must not
///     erase a genuinely tiny user-entered component passed directly through COMPLEX.
/// </summary>
public sealed class R21_EngineeringComplexTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void ImSub_BroadcastsOverRangeArgs_LikeImPower()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("3+6i")), (2, 1, new TextValue("5-2i")),
            (1, 2, new TextValue("1+i")), (2, 2, new NumberValue(2)));

        AssertColumn(
            _eval.Evaluate("=IMSUB(A1:A2,B1:B2)", sheet),
            _eval.Evaluate("=IMSUB(\"3+6i\",\"1+i\")", sheet),
            _eval.Evaluate("=IMSUB(\"5-2i\",2)", sheet));
    }

    [Fact]
    public void ImDiv_BroadcastsOverRangeArgs_LikeImPower()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("3+6i")), (2, 1, new TextValue("5-2i")),
            (1, 2, new TextValue("1+i")), (2, 2, new NumberValue(2)));

        AssertColumn(
            _eval.Evaluate("=IMDIV(A1:A2,B1:B2)", sheet),
            _eval.Evaluate("=IMDIV(\"3+6i\",\"1+i\")", sheet),
            _eval.Evaluate("=IMDIV(\"5-2i\",2)", sheet));
    }

    [Fact]
    public void Complex_BroadcastsOverRangeArgs_LikeImPower()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3)), (2, 1, new NumberValue(5)),
            (1, 2, new NumberValue(6)), (2, 2, new NumberValue(-2)));

        AssertColumn(
            _eval.Evaluate("=COMPLEX(A1:A2,B1:B2)", sheet),
            _eval.Evaluate("=COMPLEX(3,6)", sheet),
            _eval.Evaluate("=COMPLEX(5,-2)", sheet));
    }

    [Theory]
    [InlineData("=IMSUM(\"3+4i\",\"1+2j\")")]
    [InlineData("=IMPRODUCT(\"1+1i\",\"1+1j\")")]
    public void ImSumImProduct_MixedIJSuffixes_ReturnsNum(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void ImSum_ConsistentSuffix_StillSumsCorrectly()
    {
        // Sanity check that the new mixed-suffix guard doesn't break same-notation sums.
        _eval.Evaluate("=IMSUM(\"3+4i\",\"1+2i\")", MakeSheet())
            .Should().Be(new TextValue("4+6i"));
    }

    [Fact]
    public void ImProduct_ConsistentSuffix_StillMultipliesCorrectly()
    {
        _eval.Evaluate("=IMPRODUCT(\"1+1j\",\"1+1j\")", MakeSheet())
            .Should().Be(new TextValue("2j"));
    }

    [Fact]
    public void Complex_TinyUserEnteredComponent_IsNotSnappedToZero()
    {
        // COMPLEX just formats its literal inputs -- no trig computation is involved, so
        // there is no floating-point noise to clean up, unlike IMPOWER/IMSQRT etc.
        _eval.Evaluate("=COMPLEX(0.000000000000005,3)", MakeSheet())
            .Should().Be(new TextValue("5E-15+3i"));
    }

    private static void AssertColumn(ScalarValue value, params ScalarValue[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.Cells[row, 0].Should().Be(expected[row]);
    }

    private static Sheet MakeSheet(params (uint Row, uint Col, ScalarValue Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
        return sheet;
    }
}
