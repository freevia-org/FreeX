using System.Globalization;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Excel-parity round 8: the VALUE / NUMBERVALUE / DATEVALUE / TIMEVALUE text grammar. The pinned corpus
/// (<see cref="ExcelParityRound8TextParsingData"/>) was measured against Excel 16.0 en-US through COM; the
/// explicit tests below cover the rules that are the point of the change.
/// </summary>
public class ExcelParityRound8TextParsingTests : IDisposable
{
    private readonly CultureInfo _savedCulture = CultureInfo.CurrentCulture;
    private readonly FormulaEvaluator _eval = new();

    public ExcelParityRound8TextParsingTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _savedCulture;

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, new Sheet(SheetId.New(), "S"));

    private static string Describe(ScalarValue value) => value switch
    {
        ErrorValue e => "E" + e.Code,
        NumberValue n => "N" + n.Value.ToString("R", CultureInfo.InvariantCulture),
        DateTimeValue d => "N" + d.Value.ToString("R", CultureInfo.InvariantCulture),
        _ => value.GetType().Name,
    };

    private static bool SameResult(string expected, ScalarValue actual)
    {
        if (expected[0] == 'E') return actual is ErrorValue e && "E" + e.Code == expected;
        if (actual is not (NumberValue or DateTimeValue)) return false;
        double want = double.Parse(expected[1..], CultureInfo.InvariantCulture);
        double got = actual is NumberValue n ? n.Value : ((DateTimeValue)actual).Value;
        double diff = Math.Abs(want - got);
        double mag = Math.Max(Math.Abs(want), Math.Abs(got));
        // Excel reports 15 significant digits; fractions of a day also carry a few ulps of residue.
        return diff < 1e-9 || diff / mag < 1e-7;
    }

    public static IEnumerable<object[]> Corpus()
    {
        foreach (var line in ExcelParityRound8TextParsingData.Rows.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.LastIndexOf('\t');
            yield return [line[..tab], line[(tab + 1)..].TrimEnd('\r', ';')];
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void MeasuredExcelResults(string formula, string expected)
    {
        var actual = Eval("=" + formula);
        SameResult(expected, actual).Should().BeTrue($"{formula} should be {expected} but was {Describe(actual)}");
    }

    // ------------------------------------------------------------------ number text (VALUE)

    [Theory]
    [InlineData("1,2345", 12345)]            // groups after the first only need three or more digits
    [InlineData("1,000", 1000)]
    [InlineData("$-1,000", -1000)]
    [InlineData("$ 5", 5)]
    [InlineData("- 5", -5)]
    [InlineData("(5%)", -0.05)]
    [InlineData("%5", 0.05)]
    [InlineData("$(1,000)", -1000)]
    [InlineData("1 1/2", 1.5)]               // mixed fraction
    [InlineData("-1 1/2", -1.5)]
    [InlineData("5 1/2", 5.5)]
    [InlineData("24:00", 1)]
    public void Value_AcceptsExcelNumberForms(string text, double expected)
    {
        Eval($"=VALUE(\"{text}\")").Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(expected, 1e-12);
    }

    [Theory]
    [InlineData("5%%")]
    [InlineData("$3%")]
    [InlineData("5$")]
    [InlineData("5-")]
    [InlineData("1,000-")]
    [InlineData("1 2")]
    [InlineData("1.2.3")]
    [InlineData("1,23")]
    [InlineData("1e400")]
    [InlineData("-1e-400")]
    [InlineData("1e308")]
    [InlineData("1/2/")]
    public void Value_RejectsWhatExcelRejects(string text)
    {
        Eval($"=VALUE(\"{text}\")").Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Value_OnlyTheAsciiSpaceIsWhitespace()
    {
        Eval("=VALUE(\"1\"&CHAR(9))").Should().Be(ErrorValue.Value);
        Eval("=VALUE(\"1\"&UNICHAR(160))").Should().Be(ErrorValue.Value);
        Eval("=VALUE(\" 12 \")").Should().BeOfType<NumberValue>().Which.Value.Should().Be(12);
    }

    [Fact]
    public void Value_AcceptsNonAsciiDecimalDigits()
    {
        Eval("=VALUE(UNICHAR(1633)&UNICHAR(1634)&UNICHAR(1635))").Should().BeOfType<NumberValue>().Which.Value.Should().Be(123);
    }

    // ------------------------------------------------------------------ NUMBERVALUE

    [Theory]
    [InlineData("=NUMBERVALUE(\"5-\")", "E#VALUE!")]
    [InlineData("=NUMBERVALUE(\"$5\")", "N5")]
    [InlineData("=NUMBERVALUE(\"$ -5\")", "N-5")]
    [InlineData("=NUMBERVALUE(\"1.5\",\",\")", "N15")]
    [InlineData("=NUMBERVALUE(\"1,5\",\",\")", "N1.5")]
    [InlineData("=NUMBERVALUE(\"1,5%\",\",\")", "N0.015")]
    [InlineData("=NUMBERVALUE(\"1E3000\")", "E#VALUE!")]
    [InlineData("=NUMBERVALUE(\"1/1/2020\")", "N43831")]
    [InlineData("=NUMBERVALUE(\"12:30\")", "N0.5208333333333334")]
    [InlineData("=NUMBERVALUE(\"%\")", "N0")]
    [InlineData("=NUMBERVALUE(\"(5%)\")", "N-0.05")]
    [InlineData("=NUMBERVALUE(\"1,0\")", "N10")]
    public void NumberValue_FollowsExcel(string formula, string expected)
    {
        SameResult(expected, Eval(formula)).Should().BeTrue($"{formula} -> {Describe(Eval(formula))}");
    }

    // ------------------------------------------------------------------ DATEVALUE / TIMEVALUE

    [Theory]
    [InlineData("Jan 15, 2020", 43845)]
    [InlineData("January 15, 2020", 43845)]
    [InlineData("15-Jan-2020", 43845)]
    [InlineData("15 Jan 20", 43845)]
    [InlineData("20 Jan 15", 42024)]          // D Mon Y: the last number is the year
    [InlineData("Sept 5, 2020", 44079)]        // month names match on any prefix of 3+ letters
    [InlineData("15 Septem 2020", 44089)]
    [InlineData("1/2020", 43831)]              // M/Y with a four-digit year
    [InlineData("Jan 2020", 43831)]
    [InlineData("Jan15, 2020", 43845)]         // letter/digit boundary separates tokens
    [InlineData("29-Feb-1900", 60)]            // Excel's phantom leap day in any layout
    [InlineData("Feb 29, 1900", 60)]
    [InlineData("1/32/2020", -1)]
    [InlineData("1/15/1899", -1)]
    [InlineData("1/15/030", -1)]
    [InlineData("12/31/9999", 2958465)]
    [InlineData("2020-01-15 25:00", 43846)]    // hours beyond 23 roll the date forward
    [InlineData("1/15/2020 23:59:59.9999", 43846)] // times round to the millisecond
    [InlineData("10:30", 0)]
    public void Datevalue_FollowsExcelGrammar(string text, double expected)
    {
        var result = Eval($"=DATEVALUE(\"{text}\")");
        if (expected < 0)
            result.Should().Be(ErrorValue.Value);
        else
            result.Should().BeOfType<NumberValue>().Which.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("Jan 15 2020")]              // no comma before the year
    [InlineData("Jan-15-2020")]
    [InlineData("Jan 15,2020")]              // the comma needs a space after it
    [InlineData("Sat, Jan 4, 2020")]         // weekday names are never accepted
    [InlineData("2020-01-15T10:30:00")]      // ISO "T" separator
    [InlineData("2020-01")]
    [InlineData("2020 Jan 15")]
    [InlineData("Jan. 15, 2020")]
    [InlineData("15.01.2020")]
    [InlineData("1/15/2020, 10:30")]
    [InlineData("10:30AM")]
    [InlineData("1/15/2020 PM")]
    public void Datevalue_RejectsWhatExcelRejects(string text)
    {
        Eval($"=DATEVALUE(\"{text}\")").Should().Be(ErrorValue.Value);
        Eval($"=VALUE(\"{text}\")").Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("10:30 P", 0.9375)]            // a lone A/P needs the space; AM/PM may carry a dot
    [InlineData("10:30 PM.", 0.9375)]
    [InlineData("10 PM", 0.9166666666666666)]
    [InlineData("0:00 PM", 0.5)]
    [InlineData("10:60", 0.4583333333333333)]  // one overflowing component is fine
    [InlineData("10:30:60", 0.4381944444444444)]
    [InlineData("10:30:", 0.4375)]             // trailing colon
    [InlineData("1:", 0.041666666666666664)]
    [InlineData("100:30", 0.1875)]
    [InlineData("1:30.5", 0.0010474537037037)] // minutes:seconds.fraction
    public void Timevalue_FollowsExcelGrammar(string text, double expected)
    {
        Eval($"=TIMEVALUE(\"{text}\")").Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(expected, 1e-11);
    }

    [Theory]
    [InlineData("10:30a")]
    [InlineData("10:30AM")]
    [InlineData("13:00 PM")]
    [InlineData("25:61")]                      // two overflowing components
    [InlineData("1:99:99")]
    [InlineData("10:30:45,5")]
    [InlineData("2020-01-15T10:30:00")]
    [InlineData("1 PM1")]
    public void Timevalue_RejectsWhatExcelRejects(string text)
    {
        Eval($"=TIMEVALUE(\"{text}\")").Should().Be(ErrorValue.Value);
    }

    // ------------------------------------------------------------------ year-less dates use the current year

    [Fact]
    public void YearlessDates_UseTheCurrentYear_AndFallBackToMonthYear()
    {
        var year = DateTime.Today.Year;
        double Serial(int y, int m, int d) => ExcelSerial(new DateTime(y, m, d));

        Eval("=DATEVALUE(\"1/15\")").Should().BeOfType<NumberValue>().Which.Value.Should().Be(Serial(year, 1, 15));
        Eval("=DATEVALUE(\"Jan 15\")").Should().BeOfType<NumberValue>().Which.Value.Should().Be(Serial(year, 1, 15));
        Eval("=DATEVALUE(\"15-Jan\")").Should().BeOfType<NumberValue>().Which.Value.Should().Be(Serial(year, 1, 15));
        // A day that does not exist is read as a two-digit year: "1/32" is January 1932, "Jan 0" January 2000.
        Eval("=DATEVALUE(\"1/32\")").Should().BeOfType<NumberValue>().Which.Value.Should().Be(Serial(1932, 1, 1));
        Eval("=DATEVALUE(\"Jan 32\")").Should().BeOfType<NumberValue>().Which.Value.Should().Be(Serial(1932, 1, 1));
        Eval("=DATEVALUE(\"Jan 0\")").Should().BeOfType<NumberValue>().Which.Value.Should().Be(Serial(2000, 1, 1));
        Eval("=DATEVALUE(\"4/31\")").Should().BeOfType<NumberValue>().Which.Value.Should().Be(Serial(1931, 4, 1));
        // "D Mon" has no such fallback.
        Eval("=DATEVALUE(\"32 Jan\")").Should().Be(ErrorValue.Value);
        Eval("=DATEVALUE(\"13/1\")").Should().Be(ErrorValue.Value);
    }

    private static double ExcelSerial(DateTime date) => (date - new DateTime(1899, 12, 30)).TotalDays;
}
