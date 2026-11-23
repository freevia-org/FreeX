using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Excel-parity round 8, dynamic arrays. Expected values were measured against Excel 16.0 (en-US) through COM.
/// </summary>
public class ExcelParityRound8DynamicArrayTests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, new Sheet(SheetId.New(), "S"));

    private static string[] Column(ScalarValue value)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        var cells = new string[range.RowCount];
        for (int r = 0; r < range.RowCount; r++)
            cells[r] = range.Cells[r, 0] switch
            {
                TextValue t => t.Value,
                NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ErrorValue e => e.Code,
                var other => other.GetType().Name,
            };
        return cells;
    }

    [Fact]
    public void Sort_UsesExcelTextCollation_HyphenIgnoredCaseIgnoredAccentsBreakTies()
    {
        // "_a" (symbol) < "1" (digit) < "a" = "A" (stable) < "-a" (the ignored hyphen only breaks the final tie).
        Column(Eval("=SORT({\"_a\";\"a\";\"-a\";\"1\";\"A\"})")).Should().Equal("_a", "1", "a", "A", "-a");
        // Letters compare before their accents, and "é" stays with "e" rather than sorting after "f".
        Column(Eval("=SORT({\"é\";\"e\";\"f\";\"E\";\"É\"})")).Should().Equal("e", "E", "é", "É", "f");
    }

    [Fact]
    public void Sequence_LiftsArrayArguments_ToTheFirstValueOfEachElement()
    {
        var single = Eval("=SEQUENCE({2})");
        (single is RangeValue one ? one.Cells[0, 0] : single).Should().Be(new NumberValue(1));
        var row = Eval("=SEQUENCE({2,3})").Should().BeOfType<RangeValue>().Subject;
        (row.RowCount, row.ColCount).Should().Be((1, 2));
        row.Cells[0, 0].Should().Be(new NumberValue(1));
        row.Cells[0, 1].Should().Be(new NumberValue(1));
        Column(Eval("=SEQUENCE({2;3})")).Should().Equal("1", "1");
    }

    [Fact]
    public void Expand_ExplicitlyEmptyPadWith_PadsWithZero()
    {
        var padded = Eval("=EXPAND({1,2;3,4},3,3,)").Should().BeOfType<RangeValue>().Subject;
        padded.Cells[2, 2].Should().Be(new NumberValue(0));
        var omitted = Eval("=EXPAND({1,2;3,4},3,3)").Should().BeOfType<RangeValue>().Subject;
        omitted.Cells[2, 2].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void MakeArray_NonIntegerDimensions_AreValueErrors()
    {
        Eval("=MAKEARRAY(2.9,2.9,LAMBDA(r,c,r*c))").Should().Be(ErrorValue.Value);
        Eval("=MAKEARRAY(2,2,LAMBDA(r,c,r*c))").Should().BeOfType<RangeValue>();
    }

    [Theory]
    [InlineData("=LEFT({\"apple\";\"Banana\";\"cherry\"},{1;2})", "a|Ba|#N/A")]            // text functions pad the shorter array with #N/A
    [InlineData("=MID({\"apple\";\"Banana\";\"cherry\"},{1;2},2)", "ap|an|#N/A")]
    [InlineData("=REPT({\"apple\";\"Banana\";\"cherry\"},{1;2})", "apple|BananaBanana|#N/A")]
    [InlineData("=ROUND({12;15;11},{0;1})", "12|15|#N/A")]
    [InlineData("=PMT({0.12;0.1;0.2},{5;6},-1000)", "277.409731941|229.607380363|#N/A")]
    [InlineData("=IF({12;15;11}>12,{0;26},0)", "0|26|0")]                                  // only the picked branch positions matter
    [InlineData("=IFS({1;2;3}>0,{10;20})", "10|20|#N/A")]
    public void MismatchedArrayAxes_GrowToTheLongerLength_AndPadWithNa(string formula, string expected)
    {
        var cells = Column(Eval(formula));
        var wanted = expected.Split('|');
        cells.Length.Should().Be(wanted.Length);
        for (int i = 0; i < wanted.Length; i++)
        {
            if (double.TryParse(wanted[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                double.Parse(cells[i], System.Globalization.CultureInfo.InvariantCulture).Should().BeApproximately(number, 1e-6);
            else
                cells[i].Should().Be(wanted[i]);
        }
    }
}
