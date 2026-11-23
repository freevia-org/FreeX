using System.Globalization;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Excel-parity round 9. The pinned corpora (<see cref="ExcelParityRound9Data"/>) were measured against Excel 16.0
/// (en-US) through COM over the fixture grid built by <see cref="BuildFixture"/>; the explicit tests cover the
/// rules that are the point of the change.
/// </summary>
public class ExcelParityRound9Tests : IDisposable
{
    private readonly CultureInfo _savedCulture = CultureInfo.CurrentCulture;
    private readonly FormulaEvaluator _eval = new();

    public ExcelParityRound9Tests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _savedCulture;

    private static void Put(Sheet sheet, string column, params ScalarValue?[] values)
    {
        uint col = (uint)(column[0] - 'A' + 1);
        for (int i = 0; i < values.Length; i++)
            if (values[i] is { } v)
                sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), col), v);
    }

    private static ScalarValue[] Numbers(params double[] values) => values.Select(v => (ScalarValue)new NumberValue(v)).ToArray();

    private static Sheet BuildFixture()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        Put(sheet, "A", Numbers(12, 15, 11, 18, 20, 14, 16, 19, 13, 17));
        Put(sheet, "B", Numbers(22, 26, 21, 30, 35, 25, 29, 33, 23, 31));
        Put(sheet, "C", new TextValue("apple"), new TextValue("Banana"), new TextValue("cherry"), new TextValue("apple"), new TextValue("date"), new TextValue("Banana"));
        Put(sheet, "D", Numbers(1, 2, 3, 4, 5, 6));
        Put(sheet, "E", Numbers(-1000, 300, 400, 500, 200));
        Put(sheet, "F", Numbers(43831, 43922, 44013, 44105, 44197));
        Put(sheet, "G", Numbers(1, 2, 2, 3, 3, 4, 5));
        Put(sheet, "H", Numbers(43832, 43835, 43840));
        Put(sheet, "I", new NumberValue(1), null, new NumberValue(3), new TextValue("x"), new NumberValue(5), ErrorValue.NA);
        Put(sheet, "J", Numbers(5, 1, 7));
        Put(sheet, "K", Numbers(3, 8, 4));
        Put(sheet, "L", Numbers(9, 2, 6));
        Put(sheet, "M", new TextValue("1"), new TextValue("2"), new TextValue("abc"), new TextValue(" 4 "));
        Put(sheet, "N", Numbers(2, 4, 8, 16, 32, 64));
        Put(sheet, "O", Numbers(1, 2, 3, 4, 5, 6));
        Put(sheet, "P", new TextValue("b"), new TextValue("a"), new TextValue("c"), new TextValue("a"), new TextValue("b"), new TextValue("c"));
        Put(sheet, "Q", Numbers(3, 1, 2, 3, 1, 2));
        Put(sheet, "R", new TextValue("x y z"), new TextValue("a,b;c"), new TextValue("one  two   three"), new TextValue("A-B-C"));
        Put(sheet, "S", new BoolValue(true), new BoolValue(false), new BoolValue(true), new NumberValue(5));
        Put(sheet, "T", new TextValue("1"), new TextValue("2"), new TextValue("a"), new NumberValue(4));
        return sheet;
    }

    private static string Describe(ScalarValue value) => value switch
    {
        ErrorValue e => "E" + e.Code,
        NumberValue n => "N" + n.Value.ToString("R", CultureInfo.InvariantCulture),
        DateTimeValue d => "N" + d.Value.ToString("R", CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "BTRUE" : "BFALSE",
        TextValue t => "S\"" + t.Value + "\"",
        _ => value.GetType().Name,
    };

    private static bool SameResult(string expected, ScalarValue actual)
    {
        switch (expected[0])
        {
            case 'E': return actual is ErrorValue e && "E" + e.Code == expected;
            case 'B': return actual is BoolValue b && (b.Value ? "BTRUE" : "BFALSE") == expected;
            case 'S': return actual is TextValue t && "S\"" + t.Value + "\"" == expected;
            case '_': return actual is BlankValue;
        }

        if (actual is not (NumberValue or DateTimeValue)) return false;
        double want = double.Parse(expected[1..], CultureInfo.InvariantCulture);
        double got = actual is NumberValue n ? n.Value : ((DateTimeValue)actual).Value;
        double diff = Math.Abs(want - got);
        double mag = Math.Max(Math.Abs(want), Math.Abs(got));
        // Excel reports 12-15 significant digits; XIRR also stops its iteration at its own tolerance.
        return diff < 1e-9 || diff / mag < 1e-7;
    }

    private static IEnumerable<object[]> Rows(string rows)
    {
        foreach (var line in rows.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.LastIndexOf('\t');
            yield return [line[..tab], line[(tab + 1)..].TrimEnd('\r', ';')];
        }
    }

    public static IEnumerable<object[]> ConditionalCriteriaCorpus() => Rows(ExcelParityRound9Data.ConditionalCriteriaRows);

    public static IEnumerable<object[]> AFunctionCorpus() => Rows(ExcelParityRound9Data.AFunctionRows);

    public static IEnumerable<object[]> OutOfRangeTextCorpus() => Rows(ExcelParityRound9Data.OutOfRangeTextRows);

    public static IEnumerable<object[]> XirrCorpus() => Rows(ExcelParityRound9Data.XirrRows);

    private void AssertMatchesExcel(string formula, string expected)
    {
        var actual = _eval.Evaluate("=" + formula, BuildFixture());
        SameResult(expected, actual).Should().BeTrue($"{formula} should be {expected} but was {Describe(actual)}");
    }

    [Theory]
    [MemberData(nameof(ConditionalCriteriaCorpus))]
    public void ConditionalAggregateCriteria_MatchExcelAgainstTextNumbers(string formula, string expected) =>
        AssertMatchesExcel(formula, expected);

    [Theory]
    [MemberData(nameof(AFunctionCorpus))]
    public void StatisticalAFunctions_MatchExcelForReferencesAndArrays(string formula, string expected) =>
        AssertMatchesExcel(formula, expected);

    [Theory]
    [MemberData(nameof(OutOfRangeTextCorpus))]
    public void OutOfRangeNumericText_MatchesExcel(string formula, string expected) =>
        AssertMatchesExcel(formula, expected);

    [Theory]
    [MemberData(nameof(XirrCorpus))]
    public void Xirr_MatchesExcelRootsAndGuessBehaviour(string formula, string expected) =>
        AssertMatchesExcel(formula, expected);

    [Theory]
    [InlineData("=COUNTIF(M1:M4,1)", 1)]          // the text "1" matches a numeric criterion
    [InlineData("=COUNTIF(M1:M4,\"=2.0\")", 1)]   // ... through any spelling of the same number
    [InlineData("=COUNTIF(M1:M4,\">=1\")", 0)]    // but never an ordering criterion
    [InlineData("=COUNTIF(M1:M4,\"<>1\")", 4)]    // and a text number is never "equal" for <>
    [InlineData("=SUMIF(M1:M4,4,N1:N6)", 16)]     // " 4 " (padded text) matches 4
    public void CountIf_NumericCriteriaMatchTextCellsThatReadAsTheNumber(string formula, double expected)
    {
        _eval.Evaluate(formula, BuildFixture()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=AVERAGEA(M1:M4)", 0)]               // text in a reference counts as 0, numeric-looking text is NOT parsed
    [InlineData("=AVERAGEA({1,\"a\",TRUE})", 0.5)]    // in a literal array booleans are ignored and AVERAGEA counts text as 0
    [InlineData("=MAXA({-1,TRUE})", -1)]              // array booleans are ignored by MAXA
    [InlineData("=MINA({1,\"a\"})", 1)]               // array text is ignored by MINA
    [InlineData("=AVERAGEA(S1:S4)", 1.75)]            // booleans in a reference are 1/0
    [InlineData("=AVERAGEA(TRANSPOSE(S1:S4))", 5)]    // a computed array loses reference semantics
    public void StatisticalAFunctions_TreatReferencesAndArraysDifferently(string formula, double expected)
    {
        _eval.Evaluate(formula, BuildFixture()).Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData("=VARA({1,\"a\"})")]
    [InlineData("=STDEVPA({TRUE,FALSE})")]
    public void StatisticalAFunctions_ArrayTextAndBooleansLeaveTooFewValues(string formula)
    {
        _eval.Evaluate(formula, BuildFixture()).Should().Be(ErrorValue.DivByZero);
    }

    [Theory]
    [InlineData("=ABS(\"1E309\")")]
    [InlineData("=SUM(1,\"1E308\")")]
    [InlineData("=\"1E309\"+1")]
    [InlineData("=-\"1E309\"")]
    [InlineData("=VALUE(\"0E309\")")]
    public void OutOfRangeNumericText_IsNotANumber(string formula)
    {
        _eval.Evaluate(formula, BuildFixture()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void OutOfRangeNumericText_IsNotCounted()
    {
        _eval.Evaluate("=COUNT(\"1E309\",1)", BuildFixture()).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void NumericText_LongerThan255Characters_IsNotANumber()
    {
        var sheet = BuildFixture();
        _eval.Evaluate("=VALUE(REPT(\"0\",254)&\"1\")", sheet).Should().Be(new NumberValue(1));
        _eval.Evaluate("=VALUE(REPT(\"0\",255)&\"1\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Xirr_ReachesEnormousRates()
    {
        // Two flows a day apart: the root is 1.1^365 - 1 (about 1.28E15), far beyond any absolute tolerance.
        _eval.Evaluate("=XIRR({-100,110},{43831,43832})", BuildFixture())
            .Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(1.28330557943E+15, 1E+9);
    }

    [Fact]
    public void Xirr_NegativeGuessNeverReachesAPositiveRoot()
    {
        var sheet = BuildFixture();
        _eval.Evaluate("=XIRR({-100,110},{43831,44197},-0.5)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=XIRR({-100,110},{43831,44197},0)", sheet).Should().BeOfType<NumberValue>();
    }

    [Theory]
    [InlineData(0.99999999, "yyyy-mm-dd h:mm", "1900-01-01 0:00")]   // the time rounds up to midnight and carries into the day
    [InlineData(0.99999999, "yyyy-mm-dd", "1900-01-01")]
    [InlineData(0.99999999, "d", "1")]
    [InlineData(0.99999999, "dddd", "Sunday")]
    [InlineData(43831.99999999, "d", "2")]
    [InlineData(58.99999999, "d", "28")]
    [InlineData(60.99999999, "d", "1")]
    [InlineData(0.5, "yyyy-mm-dd h:mm", "1900-01-00 12:00")]       // below the rounding step nothing carries
    public void DateFormats_RoundTheSerialToTheDisplayPrecisionBeforeSplittingIt(double serial, string format, string expected)
    {
        NumberFormatter.Format(new NumberValue(serial), format).Should().Be(expected);
    }
}
