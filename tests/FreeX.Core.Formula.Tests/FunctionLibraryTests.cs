using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Tests for the expanded Phase 4.2 function library.
/// Covers IFERROR, IFNA, VLOOKUP, HLOOKUP, INDEX, MATCH,
/// SUMIF, COUNTIF, AVERAGEIF, TEXT, TRIM, UPPER, LOWER, PROPER,
/// SUBSTITUTE, FIND, SEARCH, MID, REPT, VALUE,
/// DATE, YEAR, MONTH, DAY, HOUR, MINUTE, SECOND, WEEKDAY, EDATE, DATEDIF,
/// MOD, POWER, SQRT, INT, CEILING, FLOOR, RANDBETWEEN, SIGN, LOG, LN, EXP, PI, FACT,
/// LARGE, SMALL, RANK, STDEV, MEDIAN.
/// </summary>
public partial class FunctionLibraryTests
{
    private readonly FormulaEvaluator _eval = new();

    private sealed class CultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        private readonly System.Globalization.CultureInfo _originalUiCulture = System.Globalization.CultureInfo.CurrentUICulture;

        public CultureScope(string cultureName)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            System.Globalization.CultureInfo.CurrentCulture = _originalCulture;
            System.Globalization.CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    private static void AssertTextColumn(ScalarValue value, params string[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.At(row + 1, 1).Should().Be(new TextValue(expected[row]));
    }

    private static void AssertColumn(ScalarValue value, params ScalarValue[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.At(row + 1, 1).Should().Be(expected[row]);
    }

    private static void AssertApproxColumn(ScalarValue value, params double[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            ((NumberValue)range.At(row + 1, 1)).Value.Should().BeApproximately(expected[row], 1e-10);
    }

    [Fact]
    public void ArrayConstant_CanBeSummedAsInlineRow()
    {
        _eval.Evaluate("=SUM({1,2,3})", MakeSheet())
            .Should().Be(new NumberValue(6));
    }

    [Fact]
    public void ArrayConstant_CanBeIndexedAsTwoDimensionalLiteral()
    {
        _eval.Evaluate("=INDEX({1,2;3,4},2,1)", MakeSheet())
            .Should().Be(new NumberValue(3));
    }

    [Fact]
    public void ArrayConstant_SupportsTextBooleanAndErrorLiterals()
    {
        var result = _eval.Evaluate("={\"x\",TRUE,#N/A}", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(3);
        result.Cells[0, 0].Should().Be(new TextValue("x"));
        result.Cells[0, 1].Should().Be(new BoolValue(true));
        result.Cells[0, 2].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void ArrayConstant_RejectsRaggedRows()
    {
        Action act = () => _eval.Evaluate("={1,2;3}", MakeSheet());

        act.Should().Throw<FormulaParseException>();
    }

    private static BoolValue True() => new(true);

    private static BoolValue False() => new(false);

    // ── IFERROR ─────────────────────────────────────────────────────────────

    [Fact]
    public void IfError_ValueOk_ReturnsValue()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=IFERROR(10,99)", sheet).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void IfError_DivByZero_ReturnsFallback()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=IFERROR(1/0,\"err\")", sheet).Should().Be(new TextValue("err"));
    }

    [Fact]
    public void IfError_NestedError_ReturnsFallback()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=IFERROR(NA(),0)", sheet).Should().Be(new NumberValue(0));
    }

    // ── IFNA ─────────────────────────────────────────────────────────────────

    [Fact]
    public void IfNa_NonNaValue_ReturnsValue()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=IFNA(42,0)", sheet).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void IfNa_NaError_ReturnsFallback()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=IFNA(NA(),\"not found\")", sheet).Should().Be(new TextValue("not found"));
    }

    [Fact]
    public void IfNa_DivByZero_ReturnsError_NotFallback()
    {
        // IFNA only catches #N/A, not other errors
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=IFNA(1/0,0)", sheet);
        result.Should().Be(ErrorValue.DivByZero);
    }

    // ── NA() function ────────────────────────────────────────────────────────

    [Fact]
    public void Na_ReturnsNaError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=NA()", sheet).Should().Be(ErrorValue.NA);
    }

    // ── SUMIF ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Sumif_EqualsCriteria_SumsMatching()
    {
        // A1:A4 = 1,2,1,3; sum where A=1 → 2
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(1)),
            (4, 1, new NumberValue(3)));
        _eval.Evaluate("=SUMIF(A1:A4,1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Sumif_WithSumRange()
    {
        // A: 1,2,3; B: 10,20,30; sumif A>1 → 20+30=50
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(20)),
            (3, 1, new NumberValue(3)), (3, 2, new NumberValue(30)));
        _eval.Evaluate("=SUMIF(A1:A3,\">1\",B1:B3)", sheet).Should().Be(new NumberValue(50));
    }

    [Fact]
    public void Sumif_ScalarSumRange_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));

        _eval.Evaluate("=SUMIF(A1:A1,1,5)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sumif_ShorterSumRange_ExpandsFromTopLeftCell()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, new NumberValue(10)),
            (2, 1, new TextValue("A")), (2, 2, new NumberValue(20)),
            (3, 1, new TextValue("B")), (3, 2, new NumberValue(30)));

        _eval.Evaluate("=SUMIF(A1:A3,\"A\",B1)", sheet).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Sumif_TextCriteria()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")), (1, 2, new NumberValue(10)),
            (2, 1, new TextValue("b")), (2, 2, new NumberValue(20)),
            (3, 1, new TextValue("a")), (3, 2, new NumberValue(30)));
        _eval.Evaluate("=SUMIF(A1:A3,\"a\",B1:B3)", sheet).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Sumif_CriteriaError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)));
        _eval.Evaluate("=SUMIF(A1:A1,NA(),B1:B1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sumif_RangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(10)));

        _eval.Evaluate("=SUMIF(NA(),1,A1:A1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sumif_SumRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));

        _eval.Evaluate("=SUMIF(A1:A1,1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sumif_MatchedSumRangeError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (1, 2, ErrorValue.NA));
        _eval.Evaluate("=SUMIF(A1:A1,1,B1:B1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sumif_MatchedDateSumRange_IncludesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, date),
            (2, 1, new TextValue("B")), (2, 2, new NumberValue(10)));

        _eval.Evaluate("=SUMIF(A1:A2,\"A\",B1:B2)", sheet).Should().Be(new NumberValue(date.Value));
    }

    [Fact]
    public void Sumif_DateCriteriaRange_MatchesDateSerialCriteria()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date), (1, 2, new NumberValue(10)),
            (2, 1, new NumberValue(5)), (2, 2, new NumberValue(20)));

        _eval.Evaluate("=SUMIF(A1:A2,DATE(2026,5,16),B1:B2)", sheet).Should().Be(new NumberValue(10));
    }

    // ── COUNTIF ───────────────────────────────────────────────────────────────

    [Fact]
    public void Sumif_OverflowingMatchedSum_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, new NumberValue(1E308)),
            (2, 1, new TextValue("A")), (2, 2, new NumberValue(1E308)));

        _eval.Evaluate("=SUMIF(A1:A2,\"A\",B1:B2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Countif_NumberCriteria()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(1)),
            (4, 1, new NumberValue(3)));
        _eval.Evaluate("=COUNTIF(A1:A4,1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void CountifAndSumif_TextNumericCriteria_CompareNumericCellsByValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)),
            (2, 1, new NumberValue(1.0)), (2, 2, new NumberValue(20)),
            (3, 1, new NumberValue(2)), (3, 2, new NumberValue(30)));

        _eval.Evaluate("=COUNTIF(A1:A3,\"1.0\")", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=SUMIF(A1:A3,\"1.0\",B1:B3)", sheet).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Countif_GreaterThanCriteria()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(5)),
            (3, 1, new NumberValue(10)));
        _eval.Evaluate("=COUNTIF(A1:A3,\">3\")", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Countif_TextMatch()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("apple")),
            (2, 1, new TextValue("banana")),
            (3, 1, new TextValue("apple")));
        _eval.Evaluate("=COUNTIF(A1:A3,\"apple\")", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void CountifAndSumif_TextErrorCriteriaMatchErrorCells()
    {
        var sheet = MakeSheet(
            (1, 1, ErrorValue.NA), (1, 2, new NumberValue(10)),
            (2, 1, ErrorValue.Value), (2, 2, new NumberValue(20)),
            (3, 1, new TextValue("#N/A")), (3, 2, new NumberValue(30)));

        _eval.Evaluate("=COUNTIF(A1:A3,\"#N/A\")", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=COUNTIF(A1:A3,\"#VALUE!\")", sheet).Should().Be(new NumberValue(1));
        _eval.Evaluate("=SUMIF(A1:A3,\"#N/A\",B1:B3)", sheet).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void CriteriaWildcards_MatchExcelTextOnlyAndOperatorPatterns()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Alpha")), (1, 2, new NumberValue(10)),
            (2, 1, new TextValue("Beta")), (2, 2, new NumberValue(20)),
            (3, 1, new NumberValue(123)), (3, 2, new NumberValue(30)),
            (4, 1, new BoolValue(true)), (4, 2, new NumberValue(40)));

        _eval.Evaluate("=COUNTIF(A1:A5,\"*\")", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=COUNTIF(A1:A5,\"=A*\")", sheet).Should().Be(new NumberValue(1));
        _eval.Evaluate("=COUNTIF(A1:A5,\"<>A*\")", sheet).Should().Be(new NumberValue(4));
        _eval.Evaluate("=SUMIF(A1:A5,\"=A*\",B1:B5)", sheet).Should().Be(new NumberValue(10));
        _eval.Evaluate("=SUMIF(A1:A5,\"<>A*\",B1:B5)", sheet).Should().Be(new NumberValue(90));
    }

    [Fact]
    public void Countif_CriteriaError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=COUNTIF(A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Countif_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=COUNTIF(NA(),1)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Countif_DateCell_MatchesDateSerialCriteria()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date),
            (2, 1, new NumberValue(10)));

        _eval.Evaluate("=COUNTIF(A1:A2,DATE(2026,5,16))", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countif_DateCell_MatchesNumericComparisonCriteria()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date),
            (2, 1, new NumberValue(10)));

        _eval.Evaluate("=COUNTIF(A1:A2,\">40000\")", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void CountifAndSumif_DateCriteriaText_ParsesLikeExcel()
    {
        var before = DateTimeValue.FromDateTime(new DateTime(2023, 12, 31));
        var cutoff = DateTimeValue.FromDateTime(new DateTime(2024, 1, 1));
        var after = DateTimeValue.FromDateTime(new DateTime(2024, 1, 2));
        var sheet = MakeSheet(
            (1, 1, before), (1, 2, new NumberValue(10)),
            (2, 1, cutoff), (2, 2, new NumberValue(20)),
            (3, 1, after), (3, 2, new NumberValue(30)));

        _eval.Evaluate("=COUNTIF(A1:A3,\">1/1/2024\")", sheet).Should().Be(new NumberValue(1));
        _eval.Evaluate("=SUMIF(A1:A3,\"1/1/2024\",B1:B3)", sheet).Should().Be(new NumberValue(20));
        _eval.Evaluate("=COUNTIFS(A1:A3,\">=1/1/2024\",A1:A3,\"<1/2/2024\")", sheet)
            .Should().Be(new NumberValue(1));
    }

    // ── AVERAGEIF ──────────────────────────────────────────────────────────────

    [Fact]
    public void Averageif_WithSumRange()
    {
        // A: 1,2,3; B: 10,20,30; averageif A>1 → avg(20,30)=25
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(20)),
            (3, 1, new NumberValue(3)), (3, 2, new NumberValue(30)));
        _eval.Evaluate("=AVERAGEIF(A1:A3,\">1\",B1:B3)", sheet).Should().Be(new NumberValue(25));
    }

    [Fact]
    public void Averageif_ScalarAverageRange_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));

        _eval.Evaluate("=AVERAGEIF(A1:A1,1,5)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Averageif_ShorterAverageRange_ExpandsFromTopLeftCell()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, new NumberValue(10)),
            (2, 1, new TextValue("A")), (2, 2, new NumberValue(20)),
            (3, 1, new TextValue("B")), (3, 2, new NumberValue(30)));

        _eval.Evaluate("=AVERAGEIF(A1:A3,\"A\",B1)", sheet).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Averageif_NoMatch_ReturnsDivZero()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)));
        var result = _eval.Evaluate("=AVERAGEIF(A1:A2,99)", sheet);
        result.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Averageif_CriteriaError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)));
        _eval.Evaluate("=AVERAGEIF(A1:A1,NA(),B1:B1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Averageif_RangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(10)));

        _eval.Evaluate("=AVERAGEIF(NA(),1,A1:A1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Averageif_AverageRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));

        _eval.Evaluate("=AVERAGEIF(A1:A1,1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Averageif_MatchedAverageRangeError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (1, 2, ErrorValue.NA));
        _eval.Evaluate("=AVERAGEIF(A1:A1,1,B1:B1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Averageif_MatchedDateAverageRange_IncludesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, date),
            (2, 1, new TextValue("B")), (2, 2, new NumberValue(10)));

        _eval.Evaluate("=AVERAGEIF(A1:A2,\"A\",B1:B2)", sheet).Should().Be(new NumberValue(date.Value));
    }

    [Fact]
    public void Averageif_OverflowingMatchedAverage_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, new NumberValue(1E308)),
            (2, 1, new TextValue("A")), (2, 2, new NumberValue(1E308)));

        _eval.Evaluate("=AVERAGEIF(A1:A2,\"A\",B1:B2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void IsFunctions_RangeArgument_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new TextValue("x")),
            (3, 1, ErrorValue.NA),
            (4, 1, new BoolValue(true)));

        AssertColumn(_eval.Evaluate("=ISNUMBER(A1:A5)", sheet), True(), False(), False(), False(), False());
        AssertColumn(_eval.Evaluate("=ISTEXT(A1:A5)", sheet), False(), True(), False(), False(), False());
        AssertColumn(_eval.Evaluate("=ISERROR(A1:A5)", sheet), False(), False(), True(), False(), False());
        AssertColumn(_eval.Evaluate("=ISNA(A1:A5)", sheet), False(), False(), True(), False(), False());
        AssertColumn(_eval.Evaluate("=ISLOGICAL(A1:A5)", sheet), False(), False(), False(), True(), False());
        AssertColumn(_eval.Evaluate("=ISBLANK(A1:A5)", sheet), False(), False(), False(), False(), True());
    }

    [Fact]
    public void Mod_BasicModulo()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=MOD(10,3)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Mod_DivByZero_ReturnsError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=MOD(10,0)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    // ── POWER ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Mod_OverflowingIntermediate_ReturnsNumError()
    {
        _eval.Evaluate("=MOD(1E308,1E-308)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Power_SquaresNumber()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=POWER(3,2)", sheet).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Power_NegativeBaseFractionalExponent_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=POWER(-1,0.5)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Power_ZeroNegativeExponent_ReturnsDivByZeroError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=POWER(0,-1)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Power_ExponentError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=POWER(2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    // ── SQRT ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Sqrt_PositiveNumber()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SQRT(9)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sqrt_NegativeNumber_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SQRT(-1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Sqrt_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=SQRT(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void UnaryMath_RangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-4)),
            (2, 1, new NumberValue(9)));

        AssertColumn(_eval.Evaluate("=ABS(A1:A2)", sheet), new NumberValue(4), new NumberValue(9));
        AssertColumn(_eval.Evaluate("=SQRT(A1:A2)", sheet), ErrorValue.Num, new NumberValue(3));
        AssertColumn(_eval.Evaluate("=INT(A1:A2)", sheet), new NumberValue(-4), new NumberValue(9));
        AssertColumn(_eval.Evaluate("=SIGN(A1:A2)", sheet), new NumberValue(-1), new NumberValue(1));
    }

    // ── INT ───────────────────────────────────────────────────────────────────

    [Fact]
    public void BinaryMath_RangeNumberArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(4)),
            (2, 1, new NumberValue(9)));

        AssertColumn(_eval.Evaluate("=POWER(A1:A2,2)", sheet), new NumberValue(16), new NumberValue(81));
        AssertColumn(_eval.Evaluate("=MOD(A1:A2,2)", sheet), new NumberValue(0), new NumberValue(1));
        AssertColumn(_eval.Evaluate("=LOG(A1:A2,2)", sheet), new NumberValue(2), new NumberValue(Math.Log(9) / Math.Log(2)));
        AssertColumn(_eval.Evaluate("=QUOTIENT(A1:A2,2)", sheet), new NumberValue(2), new NumberValue(4));
        AssertColumn(_eval.Evaluate("=CEILING(A1:A2,5)", sheet), new NumberValue(5), new NumberValue(10));
        AssertColumn(_eval.Evaluate("=FLOOR(A1:A2,5)", sheet), new NumberValue(0), new NumberValue(5));
        AssertColumn(_eval.Evaluate("=MROUND(A1:A2,5)", sheet), new NumberValue(5), new NumberValue(10));
    }

    [Fact]
    public void BinaryMath_RangeSecondArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(4)));

        AssertColumn(_eval.Evaluate("=POWER(2,A1:A2)", sheet), new NumberValue(4), new NumberValue(16));
        AssertColumn(_eval.Evaluate("=MOD(10,A1:A2)", sheet), new NumberValue(0), new NumberValue(2));
        AssertColumn(_eval.Evaluate("=LOG(16,A1:A2)", sheet), new NumberValue(4), new NumberValue(2));
        AssertColumn(_eval.Evaluate("=QUOTIENT(10,A1:A2)", sheet), new NumberValue(5), new NumberValue(2));
        AssertColumn(_eval.Evaluate("=CEILING(10,A1:A2)", sheet), new NumberValue(10), new NumberValue(12));
        AssertColumn(_eval.Evaluate("=FLOOR(10,A1:A2)", sheet), new NumberValue(10), new NumberValue(8));
        AssertColumn(_eval.Evaluate("=MROUND(10,A1:A2)", sheet), new NumberValue(10), new NumberValue(12));
    }

    [Fact]
    public void BinaryMath_SameShapeRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(4)),  (2, 1, new NumberValue(9)),
            (1, 2, new NumberValue(2)),  (2, 2, new NumberValue(4)));

        AssertColumn(_eval.Evaluate("=POWER(A1:A2,B1:B2)", sheet), new NumberValue(16), new NumberValue(6561));
        AssertColumn(_eval.Evaluate("=MOD(A1:A2,B1:B2)", sheet), new NumberValue(0), new NumberValue(1));
        AssertColumn(_eval.Evaluate("=LOG(A1:A2,B1:B2)", sheet), new NumberValue(2), new NumberValue(Math.Log(9) / Math.Log(4)));
        AssertColumn(_eval.Evaluate("=QUOTIENT(A1:A2,B1:B2)", sheet), new NumberValue(2), new NumberValue(2));
        AssertColumn(_eval.Evaluate("=CEILING(A1:A2,B1:B2)", sheet), new NumberValue(4), new NumberValue(12));
        AssertColumn(_eval.Evaluate("=FLOOR(A1:A2,B1:B2)", sheet), new NumberValue(4), new NumberValue(8));
        AssertColumn(_eval.Evaluate("=MROUND(A1:A2,B1:B2)", sheet), new NumberValue(4), new NumberValue(8));
    }

    [Fact]
    public void BinaryMath_OneCellRangeArgument_BroadcastsAcrossOtherRange()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(4)), (2, 1, new NumberValue(9)),
            (1, 2, new NumberValue(2)));

        AssertColumn(_eval.Evaluate("=MOD(A1:A2,B1:B1)", sheet), new NumberValue(0), new NumberValue(1));
    }

    [Fact]
    public void BinaryMath_MismatchedRangeArgumentShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(4)),  (2, 1, new NumberValue(9)),
            (1, 2, new NumberValue(2)),  (1, 3, new NumberValue(4)));

        _eval.Evaluate("=POWER(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=MOD(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=LOG(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=QUOTIENT(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CEILING(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=FLOOR(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=MROUND(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Int_TruncatesDown()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=INT(3.9)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Int_NegativeFloorTowardNegInfinity()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=INT(-3.1)", sheet).Should().Be(new NumberValue(-4));
    }

    [Fact]
    public void Abs_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ABS(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Int_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=INT(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Round_NegativeDigits_RoundsLeftOfDecimal()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=ROUND(1234,-2)", sheet).Should().Be(new NumberValue(1200));
    }

    [Fact]
    public void Round_ExcessiveDigits_ClampsLikeExcel()
    {
        _eval.Evaluate("=ROUND(1.2345,16)", MakeSheet()).Should().Be(new NumberValue(1.2345));
        _eval.Evaluate("=ROUND(12345,-16)", MakeSheet()).Should().Be(new NumberValue(0));
        _eval.Evaluate("=ROUND(1,309)", MakeSheet()).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Rounding_RangeNumberArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1.25)),
            (2, 1, new NumberValue(-1.25)));

        AssertColumn(_eval.Evaluate("=ROUND(A1:A2,1)", sheet), new NumberValue(1.3), new NumberValue(-1.3));
        AssertColumn(_eval.Evaluate("=ROUNDUP(A1:A2,1)", sheet), new NumberValue(1.3), new NumberValue(-1.3));
        AssertColumn(_eval.Evaluate("=ROUNDDOWN(A1:A2,1)", sheet), new NumberValue(1.2), new NumberValue(-1.2));
        AssertColumn(_eval.Evaluate("=TRUNC(A1:A2,1)", sheet), new NumberValue(1.2), new NumberValue(-1.2));
    }

    // ── CEILING ───────────────────────────────────────────────────────────────

    [Fact]
    public void Rounding_SameShapeDigitsArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(12.345)),
            (2, 1, new NumberValue(-12.345)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(-1)));

        AssertColumn(_eval.Evaluate("=ROUND(A1:A2,B1:B2)", sheet), new NumberValue(12.3), new NumberValue(-10));
        AssertColumn(_eval.Evaluate("=ROUNDUP(A1:A2,B1:B2)", sheet), new NumberValue(12.4), new NumberValue(-20));
        AssertColumn(_eval.Evaluate("=ROUNDDOWN(A1:A2,B1:B2)", sheet), new NumberValue(12.3), new NumberValue(-10));
        AssertColumn(_eval.Evaluate("=TRUNC(A1:A2,B1:B2)", sheet), new NumberValue(12.3), new NumberValue(-10));
    }

    [Fact]
    public void Rounding_MismatchedDigitsArgument_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(12.345)),
            (2, 1, new NumberValue(-12.345)),
            (1, 2, new NumberValue(1)),
            (1, 3, new NumberValue(-1)));

        _eval.Evaluate("=ROUND(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=ROUNDUP(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=ROUNDDOWN(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=TRUNC(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Round_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ROUND(A1,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Ceiling_RoundsUp()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=CEILING(2.3,1)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Ceiling_WithSignificance()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=CEILING(4.1,0.5)", sheet).Should().Be(new NumberValue(4.5));
    }

    [Fact]
    public void Ceiling_PositiveNumberNegativeSignificance_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=CEILING(2.3,-1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Ceiling_ArgumentError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=CEILING(2.3,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    // ── FLOOR ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Ceiling_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=CEILING(A1,1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Floor_RoundsDown()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FLOOR(2.9,1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Floor_WithSignificance()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FLOOR(4.9,0.5)", sheet).Should().Be(new NumberValue(4.5));
    }

    [Fact]
    public void Floor_PositiveNumberNegativeSignificance_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FLOOR(2.9,-1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Floor_NegativeNumberPositiveSignificance_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FLOOR(-2.9,1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Floor_ArgumentError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FLOOR(2.9,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    // ── RANDBETWEEN ───────────────────────────────────────────────────────────

    [Fact]
    public void Floor_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=FLOOR(A1,1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Randbetween_InRange()
    {
        var sheet = MakeSheet();
        for (int i = 0; i < 20; i++)
        {
            var result = _eval.Evaluate("=RANDBETWEEN(1,10)", sheet);
            result.Should().BeOfType<NumberValue>();
            var n = ((NumberValue)result).Value;
            n.Should().BeGreaterThanOrEqualTo(1).And.BeLessThanOrEqualTo(10);
        }
    }

    [Fact]
    public void Randbetween_SameShapeBottomAndTopRanges_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(5)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(6)));

        var result = _eval.Evaluate("=RANDBETWEEN(A1:A2,B1:B2)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        ((NumberValue)result.At(1, 1)).Value.Should().Be(1);
        ((NumberValue)result.At(2, 1)).Value.Should().BeGreaterThanOrEqualTo(5).And.BeLessThanOrEqualTo(6);
    }

    [Fact]
    public void Randbetween_IntegerRangeOverflow_ReturnsNumError()
    {
        _eval.Evaluate("=RANDBETWEEN(-9223372036854775808,9223372036854775807)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Randarray_ReturnsRequestedShapeWithinBounds()
    {
        var sheet = MakeSheet();

        var result = _eval.Evaluate("=RANDARRAY(2,3,5,6)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        foreach (var value in rv.Cells)
        {
            value.Should().BeOfType<NumberValue>();
            ((NumberValue)value).Value.Should().BeGreaterThanOrEqualTo(5).And.BeLessThan(6);
        }
    }

    [Fact]
    public void Randarray_WholeNumber_ReturnsIntegersWithinInclusiveBounds()
    {
        var sheet = MakeSheet();

        var result = _eval.Evaluate("=RANDARRAY(2,2,1,3,TRUE)", sheet);

        var rv = (RangeValue)result;
        foreach (var value in rv.Cells)
        {
            value.Should().BeOfType<NumberValue>();
            var number = ((NumberValue)value).Value;
            number.Should().BeOneOf(1, 2, 3);
            number.Should().Be(Math.Truncate(number));
        }
    }

    [Fact]
    public void Randarray_AcceptsSpilledScalarControlArguments()
    {
        var result = _eval.Evaluate("=RANDARRAY(SEQUENCE(1,,2),SEQUENCE(1,,2),SEQUENCE(1,,5),SEQUENCE(1,,7),SEQUENCE(1,,TRUE))", MakeSheet());

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        foreach (var value in rv.Cells)
        {
            var number = value.Should().BeOfType<NumberValue>().Subject.Value;
            number.Should().Be(Math.Truncate(number));
            number.Should().BeInRange(5, 7);
        }
    }

    [Fact]
    public void Randarray_WholeNumberIntegerRangeOverflow_ReturnsValueError()
    {
        _eval.Evaluate("=RANDARRAY(1,1,-9223372036854775808,9223372036854775807,TRUE)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_InvalidDimensions_ReturnsValueError()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=RANDARRAY(0,1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_NonFiniteRows_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=RANDARRAY(A1,1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_MinGreaterThanOrEqualToMax_ReturnsValueError()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=RANDARRAY(1,1,10,1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=RANDARRAY(1,1,5,5)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_OverflowingDecimalRange_ReturnsValueError()
    {
        _eval.Evaluate("=RANDARRAY(1,1,-1E308,1E308)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_IsVolatile()
    {
        BuiltInFunctions.IsVolatile("RANDARRAY").Should().BeTrue();
    }

    // ── SIGN ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Sign_Positive_Returns1()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SIGN(5)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sign_Zero_Returns0()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SIGN(0)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Sign_Negative_ReturnsMinus1()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SIGN(-7)", sheet).Should().Be(new NumberValue(-1));
    }

    // ── LOG ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Sign_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=SIGN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Log_Base10()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=LOG(100,10)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Log_DefaultBase10()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=LOG(1000)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(3, 1e-10);
    }

    // ── LN ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Log_OmittedBase_DefaultsTo10()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=LOG(1000,)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(3, 1e-10);
    }

    [Fact]
    public void Log_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=LOG(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Log_NonFiniteBase_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=LOG(100,A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Log_BaseError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=LOG(100,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Ln_NaturalLog()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=LN(1)", sheet);
        result.Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Ln_NegativeOrZero_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=LN(0)", sheet).Should().Be(ErrorValue.Num);
    }

    // ── EXP ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Ln_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=LN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Exp_ZeroReturns1()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=EXP(0)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Exp_OneReturnsE()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=EXP(1)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(Math.E, 1e-10);
    }

    [Fact]
    public void Exp_Overflow_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=EXP(1000)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Exp_ArgumentError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=EXP(NA())", sheet).Should().Be(ErrorValue.NA);
    }

    // ── PI ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Pi_ReturnsPi()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=PI()", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(Math.PI, 1e-10);
    }

    // ── FACT ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Fact_Factorial5()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FACT(5)", sheet).Should().Be(new NumberValue(120));
    }

    [Fact]
    public void Fact_Zero_Returns1()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FACT(0)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Fact_Decimal_TruncatesArgument()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FACT(5.9)", sheet).Should().Be(new NumberValue(120));
    }

    [Fact]
    public void Fact_Negative_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FACT(-1)", sheet).Should().Be(ErrorValue.Num);
    }

    // ── LARGE ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Large_FirstLargest()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)));
        _eval.Evaluate("=LARGE(A1:A4,1)", sheet).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void Large_Small_And_Rank_TreatScalarArraysAsSingleItemArrays()
    {
        _eval.Evaluate("=LARGE(5,1)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=SMALL(5,1)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=RANK(5,5)", MakeSheet()).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Large_SecondLargest()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)));
        _eval.Evaluate("=LARGE(A1:A4,2)", sheet).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Large_DuplicateValues_CountEachOccurrence()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(8)),
            (2, 1, new NumberValue(8)),
            (3, 1, new NumberValue(5)));

        _eval.Evaluate("=LARGE(A1:A3,2)", sheet).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void Large_KRangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)));

        AssertColumn(_eval.Evaluate("=LARGE(A1:A4,B1:B2)", sheet), new NumberValue(8), new NumberValue(5));
    }

    [Fact]
    public void Large_OutOfRange_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)));
        _eval.Evaluate("=LARGE(A1:A1,5)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Large_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(8)));
        _eval.Evaluate("=LARGE(A1:A3,1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Large_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=LARGE(NA(),1)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Large_DateTimeRange_IncludesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date),
            (2, 1, new NumberValue(10)));

        _eval.Evaluate("=LARGE(A1:A2,1)", sheet).Should().Be(new NumberValue(date.Value));
    }

    [Fact]
    public void Large_NonFiniteK_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=LARGE(A1:A1,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    // ── SMALL ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Small_FirstSmallest()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)));
        _eval.Evaluate("=SMALL(A1:A4,1)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Small_DuplicateValues_CountEachOccurrence()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(1)));

        _eval.Evaluate("=SMALL(A1:A3,2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Small_OutOfRange_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)));
        _eval.Evaluate("=SMALL(A1:A1,5)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Small_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(8)));
        _eval.Evaluate("=SMALL(A1:A3,1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Small_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=SMALL(NA(),1)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Small_NonFiniteK_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=SMALL(A1:A1,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Small_KRangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)));

        AssertColumn(_eval.Evaluate("=SMALL(A1:A4,B1:B2)", sheet), new NumberValue(1), new NumberValue(3));
    }

    [Fact]
    public void Sumproduct_DirectErrorArgument_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SUMPRODUCT(NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sumproduct_NonnumericRangeEntries_AreTreatedAsZero()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("5")),
            (2, 1, new TextValue("x")),
            (1, 2, new NumberValue(10)),
            (2, 2, new NumberValue(20)));

        _eval.Evaluate("=SUMPRODUCT(A1:A2,B1:B2)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Sumproduct_DirectTextEntry_IsTreatedAsZero()
    {
        _eval.Evaluate("=SUMPRODUCT(\"5\",2)", MakeSheet()).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Sumproduct_DateTimeRangeEntry_IncludesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date),
            (1, 2, new NumberValue(2)));

        _eval.Evaluate("=SUMPRODUCT(A1:A1,B1:B1)", sheet).Should().Be(new NumberValue(date.Value * 2));
    }

    // ── RANK ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Sumproduct_OverflowingProduct_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (1, 2, new NumberValue(1E308)));
        _eval.Evaluate("=SUMPRODUCT(A1:A1,B1:B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Rank_DescendingOrder()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)));
        // rank of 5 in descending = 2 (8>5>3>1)
        _eval.Evaluate("=RANK(5,A1:A4,0)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Rank_AscendingOrder()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)));
        // rank of 5 in ascending = 3 (1<3<5<8)
        _eval.Evaluate("=RANK(5,A1:A4,1)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Rank_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(8)));
        _eval.Evaluate("=RANK(5,A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Rank_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=RANK(5,NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Rank_OrderError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(8)));
        _eval.Evaluate("=RANK(5,A1:A2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Rank_NonFiniteOrder_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (2, 1, new NumberValue(8)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=RANK(5,A1:A2,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Rank_NonFiniteNumber_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (2, 1, new NumberValue(8)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=RANK(B1,A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Rank_NumberAndOrderRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(8)),
            (4, 1, new NumberValue(1)),
            (1, 2, new NumberValue(5)),
            (2, 2, new NumberValue(5)),
            (1, 3, new NumberValue(0)),
            (2, 3, new NumberValue(1)));

        AssertColumn(_eval.Evaluate("=RANK(B1:B2,A1:A4,C1:C2)", sheet), new NumberValue(2), new NumberValue(3));
    }

    // ── STDEV ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Stdev_SampleStdDev()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(4)),
            (3, 1, new NumberValue(4)),
            (4, 1, new NumberValue(4)),
            (5, 1, new NumberValue(5)),
            (6, 1, new NumberValue(5)),
            (7, 1, new NumberValue(7)),
            (8, 1, new NumberValue(9)));
        // Sample stddev of {2,4,4,4,5,5,7,9} ≈ 2.138
        var result = _eval.Evaluate("=STDEV(A1:A8)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(2.138, 0.001);
    }

    // ── MEDIAN ────────────────────────────────────────────────────────────────

    [Fact]
    public void Stdev_OverflowingVariance_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(-1E308)));
        _eval.Evaluate("=STDEV(A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Stdev_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(6)));
        _eval.Evaluate("=STDEV(A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Stdev_DirectLogical_IncludesValue()
    {
        ((NumberValue)_eval.Evaluate("=STDEV(TRUE,3)", MakeSheet())).Value
            .Should().BeApproximately(Math.Sqrt(2), 1e-10);
    }

    [Fact]
    public void Stdev_ReferencedLogical_IgnoresValue()
    {
        var sheet = MakeSheet(
            (1, 1, new BoolValue(true)),
            (2, 1, new NumberValue(3)));

        _eval.Evaluate("=STDEV(A1:A2)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Median_OddCount()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(5)));
        _eval.Evaluate("=MEDIAN(A1:A3)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Median_EvenCount_AveragesMiddle()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(4)));
        _eval.Evaluate("=MEDIAN(A1:A4)", sheet).Should().Be(new NumberValue(2.5));
    }

    [Fact]
    public void Median_OverflowingAverage_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=MEDIAN(A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Median_DirectLogical_IncludesValue()
    {
        _eval.Evaluate("=MEDIAN(TRUE,3)", MakeSheet()).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Median_DirectNumericText_IncludesValue()
    {
        _eval.Evaluate("=MEDIAN(\"4\",2)", MakeSheet()).Should().Be(new NumberValue(3));
    }

    // ── Bug regression: SUMIFS must receive RangeValues ────────────────────────

    [Fact]
    public void Median_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=MEDIAN(A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sumifs_RangeArg_WorksCorrectly()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (2, 1, new NumberValue(20)), (3, 1, new NumberValue(30)),
            (1, 2, new TextValue("A")),  (2, 2, new TextValue("B")),  (3, 2, new TextValue("A")));
        // SUMIFS(A1:A3, B1:B3, "A") → 40
        var result = _eval.Evaluate("=SUMIFS(A1:A3,B1:B3,\"A\")", sheet);
        result.Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Sumifs_CriteriaError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new TextValue("A")));
        _eval.Evaluate("=SUMIFS(A1:A1,B1:B1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sumifs_SumRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("A")));
        _eval.Evaluate("=SUMIFS(NA(),A1:A1,\"A\")", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sumifs_CriteriaRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(10)));
        _eval.Evaluate("=SUMIFS(A1:A1,NA(),\"A\")", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sumifs_MatchedSumRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, ErrorValue.NA),
            (1, 2, new TextValue("A")));
        _eval.Evaluate("=SUMIFS(A1:A1,B1:B1,\"A\")", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sumifs_MatchedDateSumRange_IncludesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date), (1, 2, new TextValue("A")),
            (2, 1, new NumberValue(10)), (2, 2, new TextValue("B")));

        _eval.Evaluate("=SUMIFS(A1:A2,B1:B2,\"A\")", sheet).Should().Be(new NumberValue(date.Value));
    }

    [Fact]
    public void Sumifs_MismatchedCriteriaRangeShape_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)),
            (1, 2, new TextValue("A")));

        _eval.Evaluate("=SUMIFS(A1:A2,B1:B1,\"A\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sumifs_OverflowingMatchedSum_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)),
            (1, 2, new TextValue("A")), (2, 2, new TextValue("A")));

        _eval.Evaluate("=SUMIFS(A1:A2,B1:B2,\"A\")", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Countifs_RangeArg_WorksCorrectly()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (2, 1, new NumberValue(20)), (3, 1, new NumberValue(30)),
            (1, 2, new TextValue("A")),  (2, 2, new TextValue("B")),  (3, 2, new TextValue("A")));
        // COUNTIFS(B1:B3, "A") → 2
        var result = _eval.Evaluate("=COUNTIFS(B1:B3,\"A\")", sheet);
        result.Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Countifs_CriteriaError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("A")));
        _eval.Evaluate("=COUNTIFS(A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Countifs_CriteriaRangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=COUNTIFS(NA(),\"A\")", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Countifs_DateCell_MatchesDateSerialCriteria()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date),
            (2, 1, new NumberValue(10)));

        _eval.Evaluate("=COUNTIFS(A1:A2,DATE(2026,5,16))", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countifs_MismatchedCriteriaRangeShape_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("A")),
            (1, 2, new TextValue("A")));

        _eval.Evaluate("=COUNTIFS(A1:A2,\"A\",B1:B1,\"A\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Averageifs_RangeArg_WorksCorrectly()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (2, 1, new NumberValue(20)), (3, 1, new NumberValue(30)),
            (1, 2, new TextValue("A")),  (2, 2, new TextValue("B")),  (3, 2, new TextValue("A")));
        // AVERAGEIFS(A1:A3, B1:B3, "A") → 20  (average of 10 and 30)
        var result = _eval.Evaluate("=AVERAGEIFS(A1:A3,B1:B3,\"A\")", sheet);
        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Averageifs_CriteriaError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new TextValue("A")));
        _eval.Evaluate("=AVERAGEIFS(A1:A1,B1:B1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Averageifs_AverageRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("A")));
        _eval.Evaluate("=AVERAGEIFS(NA(),A1:A1,\"A\")", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Averageifs_CriteriaRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(10)));
        _eval.Evaluate("=AVERAGEIFS(A1:A1,NA(),\"A\")", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Averageifs_MatchedAverageRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, ErrorValue.NA),
            (1, 2, new TextValue("A")));
        _eval.Evaluate("=AVERAGEIFS(A1:A1,B1:B1,\"A\")", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Averageifs_MatchedDateAverageRange_IncludesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date), (1, 2, new TextValue("A")),
            (2, 1, new NumberValue(10)), (2, 2, new TextValue("B")));

        _eval.Evaluate("=AVERAGEIFS(A1:A2,B1:B2,\"A\")", sheet).Should().Be(new NumberValue(date.Value));
    }

    [Fact]
    public void Averageifs_MismatchedCriteriaRangeShape_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)),
            (1, 2, new TextValue("A")));

        _eval.Evaluate("=AVERAGEIFS(A1:A2,B1:B1,\"A\")", sheet).Should().Be(ErrorValue.Value);
    }

    // ── Math / Trig ─────────────────────────────────────────────────────────────

    [Fact]
    public void Averageifs_OverflowingMatchedAverage_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)),
            (1, 2, new TextValue("A")), (2, 2, new TextValue("A")));

        _eval.Evaluate("=AVERAGEIFS(A1:A2,B1:B2,\"A\")", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Sin_Zero_ReturnsZero() =>
        _eval.Evaluate("=SIN(0)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact]
    public void Sin_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=SIN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Cos_Zero_ReturnsOne() =>
        _eval.Evaluate("=COS(0)", MakeSheet()).Should().Be(new NumberValue(1));

    [Fact]
    public void Cos_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=COS(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Tan_Zero_ReturnsZero() =>
        _eval.Evaluate("=TAN(0)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact]
    public void Tan_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=TAN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void UnaryTrig_RangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0)),
            (2, 1, new NumberValue(0)));

        AssertColumn(_eval.Evaluate("=SIN(A1:A2)", sheet), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=COS(A1:A2)", sheet), new NumberValue(1), new NumberValue(1));
        AssertColumn(_eval.Evaluate("=TAN(A1:A2)", sheet), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=DEGREES(A1:A2)", sheet), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=RADIANS(A1:A2)", sheet), new NumberValue(0), new NumberValue(0));
    }

    [Fact]
    public void AdditionalUnaryMath_RangeArgument_SpillsElementwise()
    {
        var zeros = MakeSheet(
            (1, 1, new NumberValue(0)),
            (2, 1, new NumberValue(0)));
        AssertColumn(_eval.Evaluate("=ASIN(A1:A2)", zeros), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=ATAN(A1:A2)", zeros), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=EXP(A1:A2)", zeros), new NumberValue(1), new NumberValue(1));

        var ones = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(1)));
        AssertColumn(_eval.Evaluate("=ACOS(A1:A2)", ones), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=LN(A1:A2)", ones), new NumberValue(0), new NumberValue(0));

        var facts = MakeSheet(
            (1, 1, new NumberValue(3)),
            (2, 1, new NumberValue(-1)));
        AssertColumn(_eval.Evaluate("=FACT(A1:A2)", facts), new NumberValue(6), ErrorValue.Num);
    }

    [Fact] public void Asin_One_ReturnsHalfPi() =>
        ((NumberValue)_eval.Evaluate("=ASIN(1)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI / 2, 1e-10);

    [Fact]
    public void Asin_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ASIN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Acos_One_ReturnsZero() =>
        ((NumberValue)_eval.Evaluate("=ACOS(1)", MakeSheet())).Value
            .Should().BeApproximately(0, 1e-10);

    [Fact]
    public void Acos_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ACOS(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Atan_One_ReturnsQuarterPi() =>
        ((NumberValue)_eval.Evaluate("=ATAN(1)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI / 4, 1e-10);

    [Fact]
    public void Atan_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ATAN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Atan2_XY_ReturnsCorrect() =>
        ((NumberValue)_eval.Evaluate("=ATAN2(1,1)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI / 4, 1e-10);

    [Fact]
    public void TwoArgumentCombinatoricsAndTrig_RangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(3)));

        AssertColumn(_eval.Evaluate("=ATAN2(1,A1:A2)", sheet), new NumberValue(Math.Atan2(2, 1)), new NumberValue(Math.Atan2(3, 1)));
        AssertColumn(_eval.Evaluate("=ATAN2(A1:A2,1)", sheet), new NumberValue(Math.Atan2(1, 2)), new NumberValue(Math.Atan2(1, 3)));
        AssertColumn(_eval.Evaluate("=COMBIN(5,A1:A2)", sheet), new NumberValue(10), new NumberValue(10));
        AssertColumn(_eval.Evaluate("=PERMUT(5,A1:A2)", sheet), new NumberValue(20), new NumberValue(60));

        var numbers = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(6)));
        AssertColumn(_eval.Evaluate("=COMBIN(A1:A2,2)", numbers), new NumberValue(10), new NumberValue(15));
        AssertColumn(_eval.Evaluate("=PERMUT(A1:A2,2)", numbers), new NumberValue(20), new NumberValue(30));
    }

    [Fact]
    public void TwoArgumentCombinatoricsAndTrig_SameShapeRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)), (2, 1, new NumberValue(3)),
            (1, 2, new NumberValue(4)), (2, 2, new NumberValue(5)));

        AssertColumn(_eval.Evaluate("=ATAN2(A1:A2,B1:B2)", sheet), new NumberValue(Math.Atan2(4, 2)), new NumberValue(Math.Atan2(5, 3)));
        AssertColumn(_eval.Evaluate("=COMBIN(B1:B2,A1:A2)", sheet), new NumberValue(6), new NumberValue(10));
        AssertColumn(_eval.Evaluate("=PERMUT(B1:B2,A1:A2)", sheet), new NumberValue(12), new NumberValue(60));
    }

    [Fact]
    public void TwoArgumentCombinatoricsAndTrig_MismatchedRangeArgumentShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)), (2, 1, new NumberValue(3)),
            (1, 2, new NumberValue(4)), (1, 3, new NumberValue(5)));

        _eval.Evaluate("=ATAN2(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=COMBIN(B1:C1,A1:A2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=PERMUT(B1:C1,A1:A2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Atan2_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ATAN2(A1,1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Degrees_Pi_Returns180() =>
        ((NumberValue)_eval.Evaluate("=DEGREES(PI())", MakeSheet())).Value
            .Should().BeApproximately(180, 1e-10);

    [Fact]
    public void Degrees_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=DEGREES(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Radians_180_ReturnsPi() =>
        ((NumberValue)_eval.Evaluate("=RADIANS(180)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI, 1e-10);

    [Fact]
    public void Radians_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=RADIANS(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Sum_NonFiniteDirectNumericText_ReturnsNumError()
    {
        _eval.Evaluate("=SUM(\"1E309\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Average_NonFiniteDirectNumericText_ReturnsNumError()
    {
        _eval.Evaluate("=AVERAGE(\"1E309\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Min_NonFiniteDirectNumericText_ReturnsNumError()
    {
        _eval.Evaluate("=MIN(\"1E309\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Max_NonFiniteDirectNumericText_ReturnsNumError()
    {
        _eval.Evaluate("=MAX(\"1E309\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Product_Range_MultipliesAll()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(3)),(3,1,new NumberValue(4)));
        _eval.Evaluate("=PRODUCT(A1:A3)", sheet).Should().Be(new NumberValue(24));
    }

    [Fact] public void Product_DirectTrue_MultipliesAsOne() =>
        _eval.Evaluate("=PRODUCT(TRUE,2)", MakeSheet()).Should().Be(new NumberValue(2));

    [Fact] public void Product_DirectFalse_MultipliesAsZero() =>
        _eval.Evaluate("=PRODUCT(FALSE,2)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact]
    public void Product_DirectTodayResult_MultipliesDateSerial()
    {
        _eval.Evaluate("=PRODUCT(TODAY(),2)", MakeSheet())
            .Should().Be(new NumberValue(DateTime.Today.ToOADate() * 2));
    }

    [Fact] public void Product_RangeFalse_IgnoresLogicalValue()
    {
        var sheet = MakeSheet(
            (1, 1, new BoolValue(false)),
            (2, 1, new NumberValue(2)));
        _eval.Evaluate("=PRODUCT(A1:A2)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Product_OverflowingProduct_ReturnsNumError()
    {
        _eval.Evaluate("=PRODUCT(1E308,1E308)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Quotient_5_2_Returns2() =>
        _eval.Evaluate("=QUOTIENT(5,2)", MakeSheet()).Should().Be(new NumberValue(2));

    [Fact]
    public void Quotient_NonFiniteNumerator_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=QUOTIENT(A1,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Gcd_12_8_Returns4() =>
        _eval.Evaluate("=GCD(12,8)", MakeSheet()).Should().Be(new NumberValue(4));

    [Fact] public void Gcd_DirectNumericText_CoercesValue() =>
        _eval.Evaluate("=GCD(\"6\",9)", MakeSheet()).Should().Be(new NumberValue(3));

    [Fact] public void Gcd_ReferencedLogicalAndText_IgnoresValues()
    {
        var sheet = MakeSheet(
            (1, 1, new BoolValue(true)),
            (2, 1, new TextValue("6")),
            (3, 1, new NumberValue(9)));
        _eval.Evaluate("=GCD(A1:A3)", sheet).Should().Be(new NumberValue(9));
    }

    [Fact] public void Gcd_NegativeArgument_ReturnsNumError() =>
        _eval.Evaluate("=GCD(-12,8)", MakeSheet()).Should().Be(ErrorValue.Num);

    [Fact] public void Lcm_4_6_Returns12() =>
        _eval.Evaluate("=LCM(4,6)", MakeSheet()).Should().Be(new NumberValue(12));

    [Fact] public void Lcm_DirectNumericText_CoercesValue() =>
        _eval.Evaluate("=LCM(\"6\",8)", MakeSheet()).Should().Be(new NumberValue(24));

    [Fact] public void Lcm_ReferencedLogicalAndText_IgnoresValues()
    {
        var sheet = MakeSheet(
            (1, 1, new BoolValue(true)),
            (2, 1, new TextValue("6")),
            (3, 1, new NumberValue(8)));
        _eval.Evaluate("=LCM(A1:A3)", sheet).Should().Be(new NumberValue(8));
    }

    [Fact] public void Lcm_NegativeArgument_ReturnsNumError() =>
        _eval.Evaluate("=LCM(-4,6)", MakeSheet()).Should().Be(ErrorValue.Num);

    [Fact] public void Rounddown_1_29_1_Returns1_2() =>
        _eval.Evaluate("=ROUNDDOWN(1.29,1)", MakeSheet()).Should().Be(new NumberValue(1.2));

    [Fact]
    public void Rounddown_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ROUNDDOWN(A1,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Rounddown_ExcessiveDigits_ClampsLikeExcel() =>
        _eval.Evaluate("=ROUNDDOWN(1.2345,309)", MakeSheet()).Should().Be(new NumberValue(1.2345));

    [Fact] public void Roundup_1_21_1_Returns1_3() =>
        _eval.Evaluate("=ROUNDUP(1.21,1)", MakeSheet()).Should().Be(new NumberValue(1.3));

    [Fact]
    public void Roundup_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ROUNDUP(A1,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Roundup_ExcessiveDigits_ClampsLikeExcel() =>
        _eval.Evaluate("=ROUNDUP(1.2345,309)", MakeSheet()).Should().Be(new NumberValue(1.2345));

    [Fact] public void Trunc_1_29_1_Returns1_2() =>
        _eval.Evaluate("=TRUNC(1.29,1)", MakeSheet()).Should().Be(new NumberValue(1.2));

    [Fact]
    public void Trunc_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=TRUNC(A1,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Trunc_ExcessiveDigits_ClampsLikeExcel() =>
        _eval.Evaluate("=TRUNC(1.2345,309)", MakeSheet()).Should().Be(new NumberValue(1.2345));

    [Fact] public void Mround_14_5_Returns15() =>
        _eval.Evaluate("=MROUND(14,5)", MakeSheet()).Should().Be(new NumberValue(15));

    [Fact] public void Mround_ZeroMultiple_ReturnsZero() =>
        _eval.Evaluate("=MROUND(14,0)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact]
    public void Mround_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=MROUND(A1,5)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Mround_OverflowingResult_ReturnsNumError()
    {
        _eval.Evaluate("=MROUND(1E308,0.1)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Combin_5_2_Returns10() =>
        _eval.Evaluate("=COMBIN(5,2)", MakeSheet()).Should().Be(new NumberValue(10));

    [Fact]
    public void Combin_OverflowingResult_ReturnsNumError()
    {
        _eval.Evaluate("=COMBIN(1030,515)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Permut_5_2_Returns20() =>
        _eval.Evaluate("=PERMUT(5,2)", MakeSheet()).Should().Be(new NumberValue(20));

    [Fact]
    public void Permut_OverflowingResult_ReturnsNumError()
    {
        _eval.Evaluate("=PERMUT(171,171)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Odd_2_Returns3() =>
        _eval.Evaluate("=ODD(2)", MakeSheet()).Should().Be(new NumberValue(3));

    [Fact] public void Even_3_Returns4() =>
        _eval.Evaluate("=EVEN(3)", MakeSheet()).Should().Be(new NumberValue(4));

    [Fact]
    public void OddEvenAndIsParity_RangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(3)));

        AssertColumn(_eval.Evaluate("=ODD(A1:A2)", sheet), new NumberValue(3), new NumberValue(3));
        AssertColumn(_eval.Evaluate("=EVEN(A1:A2)", sheet), new NumberValue(2), new NumberValue(4));
        AssertColumn(_eval.Evaluate("=ISEVEN(A1:A2)", sheet), True(), False());
        AssertColumn(_eval.Evaluate("=ISODD(A1:A2)", sheet), False(), True());
    }

    [Fact] public void VarS_ThreeValues_ReturnsSampleVariance()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        // mean=4, var.s = ((4+0+4)/2) = 4
        _eval.Evaluate("=VAR(A1:A3)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact] public void VarP_ThreeValues_ReturnsPopulationVariance()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        // mean=4, var.p = (4+0+4)/3 = 8/3
        ((NumberValue)_eval.Evaluate("=VAR.P(A1:A3)", sheet)).Value
            .Should().BeApproximately(8.0 / 3.0, 1e-10);
    }

    [Fact] public void VarP_DirectLogical_IncludesValue()
    {
        ((NumberValue)_eval.Evaluate("=VAR.P(TRUE,3)", MakeSheet())).Value
            .Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void VarP_OverflowingVariance_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(-1E308)));
        _eval.Evaluate("=VAR.P(A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void StdevP_ThreeValues_ReturnsStdDev()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        ((NumberValue)_eval.Evaluate("=STDEV.P(A1:A3)", sheet)).Value
            .Should().BeApproximately(Math.Sqrt(8.0 / 3.0), 1e-10);
    }

    [Fact]
    public void StdevP_OverflowingVariance_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(-1E308)));
        _eval.Evaluate("=STDEV.P(A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Percentile_Median_Returns4()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        _eval.Evaluate("=PERCENTILE(A1:A3,0.5)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Percentile_And_Quartile_TreatScalarArraysAsSingleItemArrays()
    {
        _eval.Evaluate("=PERCENTILE(5,0)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=PERCENTILE(5,1)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=QUARTILE(5,0)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=QUARTILE(5,4)", MakeSheet()).Should().Be(new NumberValue(5));
    }

    [Fact] public void Percentile_RangeError_PropagatesError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,ErrorValue.NA),(3,1,new NumberValue(6)));
        _eval.Evaluate("=PERCENTILE(A1:A3,0.5)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Percentile_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=PERCENTILE(NA(),0.5)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Percentile_OverflowingInterpolation_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(-1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=PERCENTILE(A1:A2,0.5)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Percentile_NonFiniteK_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=PERCENTILE(A1:A2,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Percentile_KRangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)), (4, 1, new NumberValue(4)),
            (1, 2, new NumberValue(0)), (2, 2, new NumberValue(1)));

        AssertColumn(_eval.Evaluate("=PERCENTILE(A1:A4,B1:B2)", sheet), new NumberValue(1), new NumberValue(4));
    }

    [Fact] public void PercentileExc_Middle_ReturnsInterpolated()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)));
        // PERCENTILE.EXC([1,2,3,4], 0.4): rank = 0.4*5-1 = 1, index 1 → value 2
        _eval.Evaluate("=PERCENTILE.EXC(A1:A4,0.4)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact] public void PercentileExc_RangeError_PropagatesError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,ErrorValue.NA),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)));
        _eval.Evaluate("=PERCENTILE.EXC(A1:A4,0.4)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void PercentileExc_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=PERCENTILE.EXC(NA(),0.4)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void PercentileExc_KRangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)), (4, 1, new NumberValue(4)),
            (1, 2, new NumberValue(0.4)), (2, 2, new NumberValue(0.6)));

        AssertColumn(_eval.Evaluate("=PERCENTILE.EXC(A1:A4,B1:B2)", sheet), new NumberValue(2), new NumberValue(3));
    }

    [Fact]
    public void ExponDist_LeadingOneCellLambdaRange_BroadcastsAcrossXArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (1, 2, new NumberValue(0.5)));

        AssertApproxColumn(
            _eval.Evaluate("=EXPON.DIST(A1:A2,B1:B1,FALSE)", sheet),
            ((NumberValue)_eval.Evaluate("=EXPON.DIST(A1,B1,FALSE)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=EXPON.DIST(A2,B1,FALSE)", sheet)).Value);
    }

    [Fact]
    public void NormSDist_LeadingOneCellCumulativeRange_BroadcastsAcrossZArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1)),
            (2, 1, new NumberValue(1)),
            (1, 2, new BoolValue(true)));

        AssertApproxColumn(
            _eval.Evaluate("=NORM.S.DIST(A1:A2,B1:B1)", sheet),
            ((NumberValue)_eval.Evaluate("=NORM.S.DIST(A1,B1)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=NORM.S.DIST(A2,B1)", sheet)).Value);
    }

    [Fact] public void Quartile_Q1_Returns25th()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)));
        // QUARTILE([1,2,3,4], 1) = 25th percentile = 1.75
        ((NumberValue)_eval.Evaluate("=QUARTILE(A1:A4,1)", sheet)).Value
            .Should().BeApproximately(1.75, 1e-10);
    }

    [Fact] public void Quartile_RangeError_PropagatesError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,ErrorValue.NA),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)));
        _eval.Evaluate("=QUARTILE(A1:A4,1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Quartile_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=QUARTILE(NA(),1)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Quartile_OverflowingInterpolation_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(-1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=QUARTILE(A1:A2,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Quartile_NonFiniteQuart_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=QUARTILE(A1:A2,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Quartile_QuartRangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)), (4, 1, new NumberValue(4)),
            (1, 2, new NumberValue(0)), (2, 2, new NumberValue(4)));

        AssertColumn(_eval.Evaluate("=QUARTILE(A1:A4,B1:B2)", sheet), new NumberValue(1), new NumberValue(4));
    }

    [Fact] public void Geomean_TwoNumbers_ReturnsGeometricMean()
    {
        var sheet = MakeSheet((1,1,new NumberValue(4)),(2,1,new NumberValue(9)));
        // geomean(4,9) = sqrt(36) = 6
        _eval.Evaluate("=GEOMEAN(A1:A2)", sheet).Should().Be(new NumberValue(6));
    }

    [Fact] public void Geomean_DirectLogical_IncludesValue()
    {
        _eval.Evaluate("=GEOMEAN(TRUE,4)", MakeSheet()).Should().Be(new NumberValue(2));
    }

    [Fact] public void Geomean_ReferencedLogical_IgnoresValue()
    {
        var sheet = MakeSheet((1,1,new BoolValue(true)),(2,1,new NumberValue(4)));
        _eval.Evaluate("=GEOMEAN(A1:A2)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact] public void Harmean_TwoNumbers_ReturnsHarmonicMean()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(4)));
        // harmean(1,4) = 2/(1+0.25) = 1.6
        ((NumberValue)_eval.Evaluate("=HARMEAN(A1:A2)", sheet)).Value
            .Should().BeApproximately(1.6, 1e-10);
    }

    [Fact] public void Harmean_DirectLogical_IncludesValue()
    {
        ((NumberValue)_eval.Evaluate("=HARMEAN(TRUE,4)", MakeSheet())).Value
            .Should().BeApproximately(1.6, 1e-10);
    }

    [Fact] public void Avedev_ThreeValues_ReturnsAvgAbsDev()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        // mean=4, deviations=2,0,2 → avg=4/3
        ((NumberValue)_eval.Evaluate("=AVEDEV(A1:A3)", sheet)).Value
            .Should().BeApproximately(4.0 / 3.0, 1e-10);
    }

    [Fact] public void Avedev_DirectNumericText_IncludesValue()
    {
        ((NumberValue)_eval.Evaluate("=AVEDEV(\"1\",3)", MakeSheet())).Value
            .Should().BeApproximately(1, 1e-10);
    }

    [Fact]
    public void Avedev_OverflowingDeviation_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(-1E308)));
        _eval.Evaluate("=AVEDEV(A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Avedev_ReferencedLogical_IgnoresValue()
    {
        var sheet = MakeSheet((1,1,new BoolValue(true)),(2,1,new NumberValue(3)));
        _eval.Evaluate("=AVEDEV(A1:A2)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact] public void Mode_ReturnsValueWithHighestFrequency()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(2)),(4,1,new NumberValue(3)));
        _eval.Evaluate("=MODE(A1:A4)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact] public void Mode_DirectLogical_IncludesValue()
    {
        _eval.Evaluate("=MODE(TRUE,TRUE,2)", MakeSheet()).Should().Be(new NumberValue(1));
    }

    [Fact] public void Mode_DirectNumericText_IncludesValue()
    {
        _eval.Evaluate("=MODE(\"2\",2,3)", MakeSheet()).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Mode_NonFiniteDirectNumericText_ReturnsNumError()
    {
        _eval.Evaluate("=MODE(\"1E309\",\"1E309\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Mode_AllUnique_ReturnsNA()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)));
        _eval.Evaluate("=MODE(A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Percentrank_FindsRank()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)),(5,1,new NumberValue(5)));
        // PERCENTRANK([1..5], 3) = 0.5
        _eval.Evaluate("=PERCENTRANK(A1:A5,3)", sheet).Should().Be(new NumberValue(0.5));
    }

    [Fact] public void Percentrank_InterpolatesWhenValueNotInArray()
    {
        // Excel PERCENTRANK interpolates between adjacent values when x is not an
        // exact array member but falls between min and max. For [1,2,3,4,5], the rank
        // of 3.5 is halfway between rank(3)=0.5 and rank(4)=0.75, i.e. 0.625
        // (truncated to 3 significant digits → 0.625).
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),
            (4,1,new NumberValue(4)),(5,1,new NumberValue(5)));
        _eval.Evaluate("=PERCENTRANK(A1:A5,3.5)", sheet).Should().Be(new NumberValue(0.625));
    }

    [Fact] public void Percentrank_OutsideRange_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)));
        _eval.Evaluate("=PERCENTRANK(A1:A3,10)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Percentrank_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,ErrorValue.NA),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)),(5,1,new NumberValue(5)));
        _eval.Evaluate("=PERCENTRANK(A1:A5,3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Percentrank_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=PERCENTRANK(NA(),3)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Percentrank_SignificanceError_PropagatesError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)));
        _eval.Evaluate("=PERCENTRANK(A1:A3,2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Percentrank_OverflowingSignificance_ReturnsNumError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)));
        _eval.Evaluate("=PERCENTRANK(A1:A3,2,400)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Percentrank_NonFiniteX_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=PERCENTRANK(A1:A2,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Percentrank_NonFiniteSignificance_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=PERCENTRANK(A1:A2,1,B1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Percentrank_XAndSignificanceRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(4)), (5, 1, new NumberValue(5)),
            (1, 2, new NumberValue(2)), (2, 2, new NumberValue(4)),
            (1, 3, new NumberValue(3)), (2, 3, new NumberValue(3)));

        AssertColumn(_eval.Evaluate("=PERCENTRANK(A1:A5,B1:B2,C1:C2)", sheet), new NumberValue(0.25), new NumberValue(0.75));
    }

    [Fact] public void Correl_PerfectPositive_Returns1()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),
            (1,2,new NumberValue(2)),(2,2,new NumberValue(4)),(3,2,new NumberValue(6)));
        ((NumberValue)_eval.Evaluate("=CORREL(A1:A3,B1:B3)", sheet)).Value
            .Should().BeApproximately(1.0, 1e-10);
    }

    [Fact] public void Correl_IgnoresNonnumericPairs()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(2)),(2,1,new TextValue("x")),(3,1,new NumberValue(6)),
            (1,2,new NumberValue(1)),(2,2,new NumberValue(2)),(3,2,new NumberValue(3)));
        ((NumberValue)_eval.Evaluate("=CORREL(A1:A3,B1:B3)", sheet)).Value
            .Should().BeApproximately(1.0, 1e-10);
    }

    [Fact] public void Correl_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,ErrorValue.NA),(3,1,new NumberValue(3)),
            (1,2,new NumberValue(2)),(2,2,new NumberValue(4)),(3,2,new NumberValue(6)));
        _eval.Evaluate("=CORREL(A1:A3,B1:B3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Correl_FirstRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)));
        _eval.Evaluate("=CORREL(NA(),A1:A2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Correl_SecondRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)));
        _eval.Evaluate("=CORREL(A1:A2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Correl_OverflowingVariance_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1E308)), (2, 1, new NumberValue(1E308)),
            (1, 2, new NumberValue(-1E308)), (2, 2, new NumberValue(1E308)));

        _eval.Evaluate("=CORREL(A1:A2,B1:B2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Forecast_LinearTrend_PredictsCorrectly()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),
            (1,2,new NumberValue(2)),(2,2,new NumberValue(4)),(3,2,new NumberValue(6)));
        // FORECAST(8, known_y=A1:A3=[1,2,3], known_x=B1:B3=[2,4,6]) → predict y at x=8 → 4
        ((NumberValue)_eval.Evaluate("=FORECAST(8,A1:A3,B1:B3)", sheet)).Value
            .Should().BeApproximately(4.0, 1e-10);
    }

    [Fact] public void Forecast_IgnoresNonnumericPairs()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(2)),(2,1,new TextValue("x")),(3,1,new NumberValue(6)),
            (1,2,new NumberValue(1)),(2,2,new NumberValue(2)),(3,2,new NumberValue(3)));
        ((NumberValue)_eval.Evaluate("=FORECAST.LINEAR(4,A1:A3,B1:B3)", sheet)).Value
            .Should().BeApproximately(8.0, 1e-10);
    }

    [Fact] public void Forecast_KnownYRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,ErrorValue.NA),(3,1,new NumberValue(3)),
            (1,2,new NumberValue(2)),(2,2,new NumberValue(4)),(3,2,new NumberValue(6)));
        _eval.Evaluate("=FORECAST(8,A1:A3,B1:B3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Forecast_KnownXRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),
            (1,2,new NumberValue(2)),(2,2,ErrorValue.NA),(3,2,new NumberValue(6)));
        _eval.Evaluate("=FORECAST(8,A1:A3,B1:B3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Forecast_KnownYArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(2)), (2, 1, new NumberValue(4)));
        _eval.Evaluate("=FORECAST(8,NA(),A1:A2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Forecast_KnownXArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)));
        _eval.Evaluate("=FORECAST(8,A1:A2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    // ── Financial ────────────────────────────────────────────────────────────────

    [Fact]
    public void Forecast_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)),
            (1, 2, new NumberValue(2)), (2, 2, new NumberValue(4)), (3, 2, new NumberValue(6)),
            (4, 1, new TextValue("1E309")));

        _eval.Evaluate("=FORECAST(A4,A1:A3,B1:B3)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Pmt_MonthlyPayment_ReturnsNegative()
    {
        // PMT(5%/12, 60, 10000) ≈ -188.71
        ((NumberValue)_eval.Evaluate("=PMT(0.05/12,60,10000)", MakeSheet())).Value
            .Should().BeApproximately(-188.71, 0.01);
    }

    [Fact] public void Pmt_FutureValueError_PropagatesError() =>
        _eval.Evaluate("=PMT(0.05/12,60,10000,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void CoreFinancialFunctions_RangeRateArgument_SpillElementwise()
    {
        var rates = MakeSheet((1, 1, new NumberValue(0.05 / 12)), (2, 1, new NumberValue(0.06 / 12)));

        AssertApproxColumn(
            _eval.Evaluate("=PMT(A1:A2,60,10000)", rates),
            ((NumberValue)_eval.Evaluate("=PMT(A1,60,10000)", rates)).Value,
            ((NumberValue)_eval.Evaluate("=PMT(A2,60,10000)", rates)).Value);
        AssertApproxColumn(
            _eval.Evaluate("=PV(A1:A2,60,188.71)", rates),
            ((NumberValue)_eval.Evaluate("=PV(A1,60,188.71)", rates)).Value,
            ((NumberValue)_eval.Evaluate("=PV(A2,60,188.71)", rates)).Value);
        AssertApproxColumn(
            _eval.Evaluate("=FV(A1:A2,12,-100)", rates),
            ((NumberValue)_eval.Evaluate("=FV(A1,12,-100)", rates)).Value,
            ((NumberValue)_eval.Evaluate("=FV(A2,12,-100)", rates)).Value);
        AssertApproxColumn(
            _eval.Evaluate("=NPER(A1:A2,-188.71,10000)", rates),
            ((NumberValue)_eval.Evaluate("=NPER(A1,-188.71,10000)", rates)).Value,
            ((NumberValue)_eval.Evaluate("=NPER(A2,-188.71,10000)", rates)).Value);
    }

    [Fact]
    public void Pmt_OneCellControlRanges_BroadcastAcrossRateArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.01)),
            (2, 1, new NumberValue(0.02)),
            (1, 2, new NumberValue(12)),
            (1, 3, new NumberValue(1000)));

        AssertApproxColumn(
            _eval.Evaluate("=PMT(A1:A2,B1:B1,C1:C1)", sheet),
            ((NumberValue)_eval.Evaluate("=PMT(A1,B1,C1)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=PMT(A2,B1,C1)", sheet)).Value);
    }

    [Fact]
    public void Pmt_LeadingOneCellRateRange_BroadcastsAcrossLaterArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.01)),
            (1, 2, new NumberValue(12)),
            (2, 2, new NumberValue(24)),
            (1, 3, new NumberValue(1000)));

        AssertApproxColumn(
            _eval.Evaluate("=PMT(A1:A1,B1:B2,C1:C1)", sheet),
            ((NumberValue)_eval.Evaluate("=PMT(A1,B1,C1)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=PMT(A1,B2,C1)", sheet)).Value);
    }

    [Fact]
    public void IpmtAndPpmt_SameShapeRateAndPeriodRanges_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.05 / 12)), (2, 1, new NumberValue(0.06 / 12)),
            (1, 2, new NumberValue(1)),         (2, 2, new NumberValue(2)));

        AssertApproxColumn(
            _eval.Evaluate("=IPMT(A1:A2,B1:B2,60,10000)", sheet),
            ((NumberValue)_eval.Evaluate("=IPMT(A1,B1,60,10000)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=IPMT(A2,B2,60,10000)", sheet)).Value);
        AssertApproxColumn(
            _eval.Evaluate("=PPMT(A1:A2,B1:B2,60,10000)", sheet),
            ((NumberValue)_eval.Evaluate("=PPMT(A1,B1,60,10000)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=PPMT(A2,B2,60,10000)", sheet)).Value);
    }

    [Fact]
    public void IpmtAndPpmt_MismatchedRateAndPeriodRangeShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.05 / 12)), (2, 1, new NumberValue(0.06 / 12)),
            (1, 2, new NumberValue(1)),         (1, 3, new NumberValue(2)));

        _eval.Evaluate("=IPMT(A1:A2,B1:C1,60,10000)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=PPMT(A1:A2,B1:C1,60,10000)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void CumipmtAndCumprinc_LeadingOneCellRateRange_BroadcastsAcrossStartArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.05 / 12)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)));

        AssertApproxColumn(
            _eval.Evaluate("=CUMIPMT(A1:A1,60,10000,B1:B2,12,0)", sheet),
            ((NumberValue)_eval.Evaluate("=CUMIPMT(A1,60,10000,B1,12,0)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=CUMIPMT(A1,60,10000,B2,12,0)", sheet)).Value);
        AssertApproxColumn(
            _eval.Evaluate("=CUMPRINC(A1:A1,60,10000,B1:B2,12,0)", sheet),
            ((NumberValue)_eval.Evaluate("=CUMPRINC(A1,60,10000,B1,12,0)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=CUMPRINC(A1,60,10000,B2,12,0)", sheet)).Value);
    }

    [Fact]
    public void Xnpv_RateRange_SpillsElementwiseAgainstValueAndDateArrays()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.05)), (2, 1, new NumberValue(0.10)),
            (1, 2, new NumberValue(-1000)), (2, 2, new NumberValue(600)), (3, 2, new NumberValue(600)),
            (1, 3, new NumberValue(new DateTime(2026, 1, 1).ToOADate())),
            (2, 3, new NumberValue(new DateTime(2026, 7, 1).ToOADate())),
            (3, 3, new NumberValue(new DateTime(2027, 1, 1).ToOADate())));

        AssertApproxColumn(
            _eval.Evaluate("=XNPV(A1:A2,B1:B3,C1:C3)", sheet),
            ((NumberValue)_eval.Evaluate("=XNPV(A1,B1:B3,C1:C3)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=XNPV(A2,B1:B3,C1:C3)", sheet)).Value);
    }

    [Fact]
    public void Mirr_FinanceAndReinvestRateRanges_SpillElementwiseAgainstCashFlowArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)), (2, 1, new NumberValue(400)), (3, 1, new NumberValue(700)),
            (1, 2, new NumberValue(0.05)), (2, 2, new NumberValue(0.06)),
            (1, 3, new NumberValue(0.07)), (2, 3, new NumberValue(0.08)));

        AssertApproxColumn(
            _eval.Evaluate("=MIRR(A1:A3,B1:B2,C1:C2)", sheet),
            ((NumberValue)_eval.Evaluate("=MIRR(A1:A3,B1,C1)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=MIRR(A1:A3,B2,C2)", sheet)).Value);
    }

    [Fact]
    public void Xirr_GuessRange_SpillsElementwiseAgainstValueAndDateArrays()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)), (2, 1, new NumberValue(600)), (3, 1, new NumberValue(600)),
            (1, 2, new NumberValue(new DateTime(2026, 1, 1).ToOADate())),
            (2, 2, new NumberValue(new DateTime(2026, 7, 1).ToOADate())),
            (3, 2, new NumberValue(new DateTime(2027, 1, 1).ToOADate())),
            (1, 3, new NumberValue(0.05)), (2, 3, new NumberValue(0.15)));

        AssertApproxColumn(
            _eval.Evaluate("=XIRR(A1:A3,B1:B3,C1:C2)", sheet),
            ((NumberValue)_eval.Evaluate("=XIRR(A1:A3,B1:B3,C1)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=XIRR(A1:A3,B1:B3,C2)", sheet)).Value);
    }

    [Fact]
    public void Fvschedule_PrincipalRange_SpillsElementwiseAgainstScheduleArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(100)), (2, 1, new NumberValue(200)),
            (1, 2, new NumberValue(0.10)), (2, 2, new NumberValue(0.05)));

        AssertApproxColumn(
            _eval.Evaluate("=FVSCHEDULE(A1:A2,B1:B2)", sheet),
            ((NumberValue)_eval.Evaluate("=FVSCHEDULE(A1,B1:B2)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=FVSCHEDULE(A2,B1:B2)", sheet)).Value);
    }

    [Fact] public void Pmt_TypeError_PropagatesError() =>
        _eval.Evaluate("=PMT(0.05/12,60,10000,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Pmt_NonFiniteRate_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=PMT(A1,60,10000)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Pmt_InvalidType_ReturnsNumError()
    {
        _eval.Evaluate("=PMT(0.05/12,60,10000,0,2)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Pv_FutureValue_ReturnsPresent()
    {
        // PV(5%/12, 60, 188.71) ≈ -10000
        ((NumberValue)_eval.Evaluate("=PV(0.05/12,60,188.71)", MakeSheet())).Value
            .Should().BeApproximately(-10000, 1.0);
    }

    [Fact] public void Pv_FutureValueError_PropagatesError() =>
        _eval.Evaluate("=PV(0.05/12,60,188.71,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Pv_TypeError_PropagatesError() =>
        _eval.Evaluate("=PV(0.05/12,60,188.71,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Pv_NonFiniteRate_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=PV(A1,60,188.71)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Pv_InvalidType_ReturnsNumError()
    {
        _eval.Evaluate("=PV(0.05/12,60,188.71,0,2)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Fv_Savings_ReturnsAccumulated()
    {
        // FV(5%/12, 12, -100) ≈ 1227.89
        ((NumberValue)_eval.Evaluate("=FV(0.05/12,12,-100)", MakeSheet())).Value
            .Should().BeApproximately(1227.89, 0.1);
    }

    [Fact] public void Fv_PresentValueError_PropagatesError() =>
        _eval.Evaluate("=FV(0.05/12,12,-100,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Fv_TypeError_PropagatesError() =>
        _eval.Evaluate("=FV(0.05/12,12,-100,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Fv_NonFiniteRate_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=FV(A1,12,-100)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Fv_InvalidType_ReturnsNumError()
    {
        _eval.Evaluate("=FV(0.05/12,12,-100,0,2)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Nper_CountPeriods_Returns60()
    {
        ((NumberValue)_eval.Evaluate("=NPER(0.05/12,-188.71,10000)", MakeSheet())).Value
            .Should().BeApproximately(60, 0.1);
    }

    [Fact] public void Nper_FutureValueError_PropagatesError() =>
        _eval.Evaluate("=NPER(0.05/12,-188.71,10000,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Nper_TypeError_PropagatesError() =>
        _eval.Evaluate("=NPER(0.05/12,-188.71,10000,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Nper_NonFiniteRate_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=NPER(A1,-188.71,10000)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Nper_InvalidType_ReturnsNumError()
    {
        _eval.Evaluate("=NPER(0.05/12,-188.71,10000,0,2)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Rate_FindsInterestRate()
    {
        // RATE(60, -188.71, 10000) ≈ 0.05/12
        ((NumberValue)_eval.Evaluate("=RATE(60,-188.71,10000)", MakeSheet())).Value
            .Should().BeApproximately(0.05 / 12, 1e-5);
    }

    [Fact] public void Rate_FutureValueError_PropagatesError() =>
        _eval.Evaluate("=RATE(60,-188.71,10000,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Rate_TypeError_PropagatesError() =>
        _eval.Evaluate("=RATE(60,-188.71,10000,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Rate_GuessError_PropagatesError() =>
        _eval.Evaluate("=RATE(60,-188.71,10000,0,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Rate_NonFiniteNper_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=RATE(A1,-188.71,10000)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Rate_NonFiniteGuess_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=RATE(60,-188.71,10000,0,0,A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Rate_InvalidType_ReturnsNumError()
    {
        _eval.Evaluate("=RATE(60,-188.71,10000,0,2)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Npv_BasicCashflow_ReturnsNpv()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(-1000)),
            (2,1,new NumberValue(400)),
            (3,1,new NumberValue(400)),
            (4,1,new NumberValue(400)));
        ((NumberValue)_eval.Evaluate("=NPV(0.1,A1:A4)", sheet)).Value
            .Should().BeApproximately(-1000.0/1.1 + 400.0/1.21 + 400.0/1.331 + 400.0/1.4641, 0.01);
    }

    [Fact] public void Npv_DirectLogical_IncludesValueArgument()
    {
        _eval.Evaluate("=NPV(0,TRUE,3)", MakeSheet()).Should().Be(new NumberValue(4));
    }

    [Fact] public void Npv_DirectNumericText_IncludesValueArgument()
    {
        _eval.Evaluate("=NPV(0,\"1\",3)", MakeSheet()).Should().Be(new NumberValue(4));
    }

    [Fact] public void Npv_ReferencedLogical_IgnoresValue()
    {
        var sheet = MakeSheet((1,1,new BoolValue(true)),(2,1,new NumberValue(3)));
        _eval.Evaluate("=NPV(0,A1:A2)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Npv_NonFiniteRate_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=NPV(A1,100)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Irr_CashflowSeries_ReturnsRate()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(-1000)),
            (2,1,new NumberValue(300)),
            (3,1,new NumberValue(400)),
            (4,1,new NumberValue(500)));
        ((NumberValue)_eval.Evaluate("=IRR(A1:A4)", sheet)).Value
            .Should().BeApproximately(0.0890, 0.001);
    }

    [Fact] public void Irr_GuessError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(-1000)),
            (2,1,new NumberValue(1100)));
        _eval.Evaluate("=IRR(A1:A2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Irr_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(-1000)),
            (2,1,ErrorValue.NA),
            (3,1,new NumberValue(1100)));
        _eval.Evaluate("=IRR(A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Irr_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=IRR(NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Irr_NonFiniteGuess_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)),
            (2, 1, new NumberValue(1100)),
            (3, 1, new TextValue("1E309")));

        _eval.Evaluate("=IRR(A1:A2,A3)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Irr_AllPositiveCashflows_ReturnsNumError()
    {
        // No sign change — IRR equation has no real solution above -1; Excel returns #NUM!.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(100)),
            (2, 1, new NumberValue(200)),
            (3, 1, new NumberValue(300)));
        _eval.Evaluate("=IRR(A1:A3)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Irr_AllNegativeCashflows_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-100)),
            (2, 1, new NumberValue(-200)),
            (3, 1, new NumberValue(-300)));
        _eval.Evaluate("=IRR(A1:A3)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Irr_GuessAtOrBelowMinusOne_ReturnsNumError()
    {
        // 1 + guess must be > 0 for the IRR Newton iteration to make sense.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)),
            (2, 1, new NumberValue(1100)));
        _eval.Evaluate("=IRR(A1:A2,-1)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=IRR(A1:A2,-2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Sln_StraightLine_ReturnsAnnualDep()
    {
        // SLN(10000, 1000, 9) = 1000
        _eval.Evaluate("=SLN(10000,1000,9)", MakeSheet()).Should().Be(new NumberValue(1000));
    }

    // ── Logical / Text ───────────────────────────────────────────────────────────

    [Fact]
    public void Sln_NonFiniteCost_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=SLN(A1,1000,9)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Xor_TrueTrue_ReturnsFalse() =>
        _eval.Evaluate("=XOR(TRUE,TRUE)", MakeSheet()).Should().Be(new BoolValue(false));

    [Fact] public void Xor_TrueFalse_ReturnsTrue() =>
        _eval.Evaluate("=XOR(TRUE,FALSE)", MakeSheet()).Should().Be(new BoolValue(true));

    [Fact] public void TrueFunc_ReturnsTrue() =>
        _eval.Evaluate("=TRUE()", MakeSheet()).Should().Be(new BoolValue(true));

    [Fact] public void FalseFunc_ReturnsFalse() =>
        _eval.Evaluate("=FALSE()", MakeSheet()).Should().Be(new BoolValue(false));

    [Fact] public void Iseven_4_ReturnsTrue() =>
        _eval.Evaluate("=ISEVEN(4)", MakeSheet()).Should().Be(new BoolValue(true));

    [Fact] public void Isodd_3_ReturnsTrue() =>
        _eval.Evaluate("=ISODD(3)", MakeSheet()).Should().Be(new BoolValue(true));

    [Fact]
    public void Countblank_Range_CountsBlankCells()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (3, 1, new TextValue("x")));

        _eval.Evaluate("=COUNTBLANK(A1:A3)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countblank_SingleCellReference_CountsBlankCell()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));

        _eval.Evaluate("=COUNTBLANK(A2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countblank_Range_CountsEmptyTextCells()
    {
        var sheet = MakeSheet((1, 1, new TextValue("")), (2, 1, new NumberValue(1)));

        _eval.Evaluate("=COUNTBLANK(A1:A2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countblank_Range_IgnoresErrors()
    {
        var sheet = MakeSheet((1, 1, ErrorValue.NA), (2, 1, new TextValue("")));

        _eval.Evaluate("=COUNTBLANK(A1:A2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Rows_Range_ReturnsRangeHeight()
    {
        var sheet = MakeSheet((2, 2, new NumberValue(1)), (4, 3, new NumberValue(2)));

        _eval.Evaluate("=ROWS(B2:C4)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Columns_Range_ReturnsRangeWidth()
    {
        var sheet = MakeSheet((2, 2, new NumberValue(1)), (4, 4, new NumberValue(2)));

        _eval.Evaluate("=COLUMNS(B2:D4)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Rows_FullColumnReference_ReturnsWorksheetRowCount()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=ROWS(A:A)", sheet).Should().Be(new NumberValue(CellAddress.MaxRow));
    }

    [Fact]
    public void Columns_FullRowReference_ReturnsWorksheetColumnCount()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=COLUMNS(1:1)", sheet).Should().Be(new NumberValue(CellAddress.MaxCol));
    }

    [Theory]
    [InlineData("=AREAS(B2:C4)")]
    [InlineData("=AREAS(A:A)")]
    [InlineData("=AREAS(1:1)")]
    public void Areas_SingleReference_ReturnsOneWithoutMaterializingReference(string formula)
    {
        var sheet = MakeSheet();

        _eval.Evaluate(formula, sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Areas_MissingSheetReference_ReturnsRefError()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("S");

        _eval.Evaluate("=AREAS(Missing!A:A)", sheet, workbook).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Areas_NonReferenceArgument_ReturnsValueError()
    {
        _eval.Evaluate("=AREAS(1)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Row_Range_SpillsRowNumbers()
    {
        var sheet = MakeSheet((2, 2, new NumberValue(1)), (4, 3, new NumberValue(2)));

        var result = _eval.Evaluate("=ROW(B2:C4)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(2));
        result.Cells[1, 0].Should().Be(new NumberValue(3));
        result.Cells[2, 0].Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Row_SingleCellReference_ReturnsCellRow()
    {
        var sheet = MakeSheet((5, 2, new NumberValue(1)));

        _eval.Evaluate("=ROW(B5)", sheet).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Row_NoArgument_ReturnsCurrentCellRow()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=ROW()", sheet, currentCell: new CellAddress(sheet.Id, 7, 4))
            .Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Column_Range_SpillsColumnNumbers()
    {
        var sheet = MakeSheet((2, 2, new NumberValue(1)), (4, 3, new NumberValue(2)));

        var result = _eval.Evaluate("=COLUMN(B2:C4)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new NumberValue(2));
        result.Cells[0, 1].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Column_SingleCellReference_ReturnsCellColumn()
    {
        var sheet = MakeSheet((5, 2, new NumberValue(1)));

        _eval.Evaluate("=COLUMN(B5)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Column_NoArgument_ReturnsCurrentCellColumn()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=COLUMN()", sheet, currentCell: new CellAddress(sheet.Id, 7, 4))
            .Should().Be(new NumberValue(4));
    }

    [Fact] public void Replace_Middle_ReplacesCorrectly() =>
        _eval.Evaluate("=REPLACE(\"Hello World\",7,5,\"Excel\")", MakeSheet())
            .Should().Be(new TextValue("Hello Excel"));

    [Fact]
    public void Replace_NumCharsBeyondRemainingText_ReplacesThroughEnd()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=REPLACE(\"abcdef\",3,2147483647,\"X\")", sheet)
            .Should().Be(new TextValue("abX"));
        _eval.Evaluate("=REPLACEB(\"A\u754cB\",2,2147483647,\"X\")", sheet)
            .Should().Be(new TextValue("AX"));
    }

    [Fact]
    public void Replace_RangeOldTextArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")));

        var result = _eval.Evaluate("=REPLACE(A1:A2,2,2,\"X\")", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(2);
        range.ColCount.Should().Be(1);
        range.At(1, 1).Should().Be(new TextValue("AXle"));
        range.At(2, 1).Should().Be(new TextValue("BXana"));
    }

    [Fact]
    public void Replace_SameShapeStartLengthAndNewTextArguments_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")),
            (1, 2, new NumberValue(2)),
            (2, 2, new NumberValue(3)),
            (1, 3, new NumberValue(2)),
            (2, 3, new NumberValue(3)),
            (1, 4, new TextValue("X")),
            (2, 4, new TextValue("YZ")));

        AssertTextColumn(_eval.Evaluate("=REPLACE(A1:A2,B1:B2,C1:C2,D1:D2)", sheet), "AXle", "BaYZa");
    }

    [Fact]
    public void Replace_MismatchedStartLengthOrNewTextArgument_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")),
            (1, 2, new NumberValue(2)),
            (1, 3, new NumberValue(3)));

        _eval.Evaluate("=REPLACE(A1:A2,B1:C1,2,\"X\")", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=REPLACE(A1:A2,2,B1:C1,\"X\")", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=REPLACE(A1:A2,2,2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Replace_DoesNotSplitSurrogatePairs()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=REPLACE(\"😀x\",1,1,\"Q\")", sheet).Should().Be(new TextValue("Qx"));
        _eval.Evaluate("=REPLACE(\"x😀y\",2,1,\"Q\")", sheet).Should().Be(new TextValue("xQy"));
        _eval.Evaluate("=REPLACE(\"😀x\",2,0,\"Q\")", sheet).Should().Be(new TextValue("😀Qx"));
    }

    [Fact]
    public void Replace_StartNumError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=REPLACE(\"abc\",NA(),1,\"x\")", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Replace_NumCharsError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=REPLACE(\"abc\",1,NA(),\"x\")", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Replace_NewTextError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=REPLACE(\"abc\",1,1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Replace_StartNumLessThanOne_ReturnsValueError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=REPLACE(\"abc\",0,1,\"x\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Replace_NumCharsNegative_ReturnsValueError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=REPLACE(\"abc\",1,-1,\"x\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Replace_StartNumPastAppendBoundary_ReturnsValueError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=REPLACE(\"abc\",5,0,\"x\")", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=REPLACEB(\"A\u754cB\",6,0,\"x\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Replace_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var text = new string('x', 32767);
        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=REPLACE(A1,1,0,\"y\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Replace_ResultAtExcelCellLimit_ReturnsText()
    {
        var text = new string('x', 32767);
        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=REPLACE(A1,1,1,\"x\")", sheet).Should().Be(new TextValue(text));
    }

    [Fact] public void Concatenate_TwoStrings_JoinsThem() =>
        _eval.Evaluate("=CONCATENATE(\"Hello \",\"World\")", MakeSheet())
            .Should().Be(new TextValue("Hello World"));

    [Fact]
    public void Concatenate_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue(new string('x', 32767))),
            (1, 2, new TextValue("y")));

        _eval.Evaluate("=CONCATENATE(A1,B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Concat_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue(new string('x', 32767))),
            (1, 2, new TextValue("y")));

        _eval.Evaluate("=CONCAT(A1,B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Concat_ResultAtExcelCellLimit_ReturnsText()
    {
        var text = new string('x', 32767);
        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=CONCAT(A1)", sheet).Should().Be(new TextValue(text));
    }

    [Fact]
    public void Concat_RangeArguments_FlattenCellsInExcelOrder()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (1, 2, new NumberValue(2)),
            (2, 1, new BoolValue(true)),
            (2, 2, BlankValue.Instance),
            (3, 1, new TextValue("z")));

        _eval.Evaluate("=CONCAT(A1:B2,\"-\",A3)", sheet).Should().Be(new TextValue("a2TRUE-z"));
    }

    [Fact]
    public void Concat_RangeArgumentErrors_Propagate()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (1, 2, ErrorValue.NA));

        _eval.Evaluate("=CONCAT(A1:B1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Concat_DirectTodayResult_UsesDateSerialText()
    {
        var expected = DateTime.Today.ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture);

        _eval.Evaluate("=CONCAT(TODAY())", MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void Textjoin_TextArgumentError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=TEXTJOIN(\",\",TRUE,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Textjoin_RangeArgument_FlattensCellsAndHonorsIgnoreEmpty()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (1, 3, new TextValue("b")));

        _eval.Evaluate("=TEXTJOIN(\"|\",TRUE,A1:C1)", sheet).Should().Be(new TextValue("a|b"));
        _eval.Evaluate("=TEXTJOIN(\"|\",FALSE,A1:C1)", sheet).Should().Be(new TextValue("a||b"));
    }

    [Fact]
    public void Textjoin_IgnoreEmptyOneCellRange_CoercesToScalarBoolean()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (3, 1, new TextValue("b")),
            (1, 2, new BoolValue(true)));

        _eval.Evaluate("=TEXTJOIN(\"|\",B1:B1,A1:A3)", sheet).Should().Be(new TextValue("a|b"));
    }

    [Fact]
    public void Textjoin_DelimiterRange_CyclesDelimitersBetweenTextItems()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("-")),
            (1, 2, new TextValue("|")));

        _eval.Evaluate("=TEXTJOIN(A1:B1,TRUE,\"x\",\"y\",\"z\")", sheet)
            .Should().Be(new TextValue("x-y|z"));
    }

    [Fact]
    public void Textjoin_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue(new string('x', 32767))),
            (1, 2, new TextValue("y")));

        _eval.Evaluate("=TEXTJOIN(\"\",TRUE,A1:B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Textjoin_ResultAtExcelCellLimit_ReturnsText()
    {
        var text = new string('x', 32767);
        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=TEXTJOIN(\"\",TRUE,A1)", sheet).Should().Be(new TextValue(text));
    }

    [Fact]
    public void CharAndCode_UseWindowsAnsiMappingForEuro()
    {
        _eval.Evaluate("=CHAR(128)", MakeSheet()).Should().Be(new TextValue("€"));
        _eval.Evaluate("=CODE(\"€\")", MakeSheet()).Should().Be(new NumberValue(128));
        _eval.Evaluate("=CODE(CHAR(128))", MakeSheet()).Should().Be(new NumberValue(128));
    }

    [Fact] public void T_Text_ReturnsText() =>
        _eval.Evaluate("=T(\"hello\")", MakeSheet()).Should().Be(new TextValue("hello"));

    [Fact] public void T_Number_ReturnsEmpty() =>
        _eval.Evaluate("=T(42)", MakeSheet()).Should().Be(new TextValue(""));

    [Fact]
    public void T_RangeArgument_PropagatesElementErrors()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("hello")),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(42)));

        AssertColumn(
            _eval.Evaluate("=T(A1:A3)", sheet),
            new TextValue("hello"),
            ErrorValue.NA,
            new TextValue(""));
    }

    [Fact]
    public void T_ArrayTextLiteral_ReturnsTextElement()
    {
        var result = _eval.Evaluate("=T({\"hello\",42})", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new TextValue("hello"));
        result.At(1, 2).Should().Be(new TextValue(""));
    }

    [Fact]
    public void Hyperlink_ReturnsDisplayTextWhenFriendlyNameIsProvided()
    {
        _eval.Evaluate("=HYPERLINK(\"https://example.com\",\"Example\")", MakeSheet())
            .Should().Be(new TextValue("Example"));
    }

    [Fact]
    public void Hyperlink_ReturnsLinkLocationWhenFriendlyNameIsOmitted()
    {
        _eval.Evaluate("=HYPERLINK(\"https://example.com\")", MakeSheet())
            .Should().Be(new TextValue("https://example.com"));
    }

    [Fact]
    public void Hyperlink_OmittedFriendlyNameSlot_ReturnsLinkLocation()
    {
        _eval.Evaluate("=HYPERLINK(\"https://example.com\",)", MakeSheet())
            .Should().Be(new TextValue("https://example.com"));
    }

    [Fact]
    public void Hyperlink_PropagatesLinkAndFriendlyNameErrors()
    {
        _eval.Evaluate("=HYPERLINK(NA(),\"Example\")", MakeSheet()).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=HYPERLINK(\"https://example.com\",NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Hyperlink_RangeArgument_SpillsDisplayTextElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("https://example.com/a")),
            (2, 1, new TextValue("https://example.com/b")),
            (1, 2, new TextValue("A")),
            (2, 2, new TextValue("B")));

        AssertTextColumn(_eval.Evaluate("=HYPERLINK(A1:A2)", sheet), "https://example.com/a", "https://example.com/b");
        AssertTextColumn(_eval.Evaluate("=HYPERLINK(\"https://example.com\",B1:B2)", sheet), "A", "B");
    }

    [Fact]
    public void Hyperlink_SameShapeRangeArguments_SpillsDisplayTextElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("https://example.com/a")),
            (2, 1, new TextValue("https://example.com/b")),
            (1, 2, new TextValue("A")),
            (2, 2, new TextValue("B")));

        AssertTextColumn(_eval.Evaluate("=HYPERLINK(A1:A2,B1:B2)", sheet), "A", "B");
    }

    [Fact]
    public void Hyperlink_MismatchedRangeArgumentShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("https://example.com/a")),
            (2, 1, new TextValue("https://example.com/b")),
            (1, 2, new TextValue("A")),
            (1, 3, new TextValue("B")));

        _eval.Evaluate("=HYPERLINK(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void T_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('x', 32768))));

        _eval.Evaluate("=T(A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact] public void T_Error_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, ErrorValue.Ref));
        _eval.Evaluate("=T(A1)", sheet).Should().Be(ErrorValue.Ref);
    }

    [Fact] public void Fixed_TwoDecimals_ReturnsFormatted() =>
        _eval.Evaluate("=FIXED(1234.567,2,TRUE)", MakeSheet())
            .Should().Be(new TextValue("1234.57"));

    [Fact]
    public void Fixed_BlankDecimalsSlot_UsesZeroDecimals()
    {
        _eval.Evaluate("=FIXED(1234.5,)", MakeSheet())
            .Should().Be(new TextValue("1,235"));
    }

    [Fact]
    public void FixedDollarTAndEncodeUrl_RangeArgument_SpillElementwise()
    {
        var numbers = MakeSheet(
            (1, 1, new NumberValue(1234.56)),
            (2, 1, new NumberValue(-12.3)));
        AssertTextColumn(_eval.Evaluate("=DOLLAR(A1:A2,1)", numbers), "$1,234.6", "($12.3)");
        AssertTextColumn(_eval.Evaluate("=FIXED(A1:A2,1,TRUE)", numbers), "1234.6", "-12.3");

        var mixed = MakeSheet(
            (1, 1, new TextValue("a b")),
            (2, 1, new NumberValue(42)));
        AssertTextColumn(_eval.Evaluate("=T(A1:A2)", mixed), "a b", "");
        AssertTextColumn(_eval.Evaluate("=ENCODEURL(A1:A2)", mixed), "a%20b", "42");
    }

    [Fact]
    public void FixedAndDollar_SameShapeDecimalsArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1234.56)),
            (2, 1, new NumberValue(-12.34)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(0)));

        AssertTextColumn(_eval.Evaluate("=FIXED(A1:A2,B1:B2,TRUE)", sheet), "1234.6", "-12");
        AssertTextColumn(_eval.Evaluate("=DOLLAR(A1:A2,B1:B2)", sheet), "$1,234.6", "($12)");
    }

    [Fact]
    public void Fixed_LeadingOneCellNoCommasRange_BroadcastsAcrossValueArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1234.56)),
            (2, 1, new NumberValue(9876.54)),
            (1, 2, new BoolValue(true)));

        AssertTextColumn(_eval.Evaluate("=FIXED(A1:A2,1,B1:B1)", sheet), "1234.6", "9876.5");
    }

    [Fact]
    public void FixedAndDollar_MismatchedDecimalsArgument_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1234.56)),
            (2, 1, new NumberValue(-12.34)),
            (1, 2, new NumberValue(1)),
            (1, 3, new NumberValue(0)));

        _eval.Evaluate("=FIXED(A1:A2,B1:C1,TRUE)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DOLLAR(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Fixed_NegativeDecimals_RoundsLeftOfDecimal()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FIXED(1234.567,-1,TRUE)", sheet).Should().Be(new TextValue("1230"));
    }

    [Fact]
    public void Fixed_ExcessiveNegativeDecimals_RoundsToZeroLikeExcel()
    {
        _eval.Evaluate("=FIXED(1,-309,TRUE)", MakeSheet()).Should().Be(new TextValue("0"));
    }

    [Fact]
    public void Fixed_DecimalsError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FIXED(1234,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Fixed_NoCommasError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FIXED(1234,2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Fixed_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=FIXED(1,32768,TRUE)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact] public void Clean_RemovesControlChars()
    {
        var sheet = MakeSheet((1, 1, new TextValue("Hello\x01World")));
        _eval.Evaluate("=CLEAN(A1)", sheet).Should().Be(new TextValue("HelloWorld"));
    }

    [Fact]
    public void Clean_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('x', 32768))));

        _eval.Evaluate("=CLEAN(A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact] public void Dollar_FormatsAsCurrency() =>
        _eval.Evaluate("=DOLLAR(1234.5,2)", MakeSheet())
            .Should().Be(new TextValue("$1,234.50"));

    [Fact]
    public void Dollar_NegativeNumber_UsesAccountingParentheses()
    {
        _eval.Evaluate("=DOLLAR(-1234.5,2)", MakeSheet())
            .Should().Be(new TextValue("($1,234.50)"));
    }

    [Fact]
    public void Dollar_BlankDecimalsSlot_UsesZeroDecimals()
    {
        _eval.Evaluate("=DOLLAR(1234.5,)", MakeSheet())
            .Should().Be(new TextValue("$1,235"));
    }

    [Fact]
    public void Dollar_NegativeDecimals_RoundsLeftOfDecimal()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=DOLLAR(1234.567,-1)", sheet).Should().Be(new TextValue("$1,230"));
    }

    [Fact]
    public void Dollar_NegativeDecimalsRoundedToZero_FormatsWithoutParentheses()
    {
        _eval.Evaluate("=DOLLAR(-1,-1)", MakeSheet()).Should().Be(new TextValue("$0"));
    }

    [Fact]
    public void Dollar_ExcessiveNegativeDecimals_RoundsToZeroLikeExcel()
    {
        _eval.Evaluate("=DOLLAR(1,-309)", MakeSheet()).Should().Be(new TextValue("$0"));
    }

    [Fact]
    public void Dollar_DecimalsError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=DOLLAR(1234,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    // ── Reference ────────────────────────────────────────────────────────────────

    [Fact]
    public void Dollar_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=DOLLAR(1,32768)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact] public void Indirect_A1String_ReturnsValue()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(42)));
        _eval.Evaluate("=INDIRECT(\"A1\")", sheet).Should().Be(new NumberValue(42));
    }

    [Fact] public void Indirect_A1RangeString_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=SUM(INDIRECT(\"A1:A3\"))", sheet).Should().Be(new NumberValue(6));
    }

    [Fact] public void Indirect_SheetQualifiedA1RangeString_ReturnsRangeValue()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var data = wb.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 1, 1), new NumberValue(1));
        data.SetCell(new CellAddress(data.Id, 2, 1), new NumberValue(2));
        data.SetCell(new CellAddress(data.Id, 3, 1), new NumberValue(3));

        _eval.Evaluate("=SUM(INDIRECT(\"Data!A1:A3\"))", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact] public void Indirect_UnquotedSheetNameWithSpace_ReturnsRefError()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var data = wb.AddSheet("My Sheet");
        data.SetCell(new CellAddress(data.Id, 1, 1), new NumberValue(42));

        _eval.Evaluate("=INDIRECT(\"My Sheet!A1\")", sheet, wb).Should().Be(ErrorValue.Ref);
        _eval.Evaluate("=INDIRECT(\"'My Sheet'!A1\")", sheet, wb).Should().Be(new NumberValue(42));
    }

    [Fact] public void Indirect_NamedRangeString_ReturnsRangeValue()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        wb.DefineNamedRange("MyData", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));

        _eval.Evaluate("=SUM(INDIRECT(\"MyData\"))", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact] public void Indirect_NamedRangeStringWithR1C1Flag_ReturnsRangeValue()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        wb.DefineNamedRange("MyData", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));

        _eval.Evaluate("=SUM(INDIRECT(\"MyData\",FALSE))", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact] public void Indirect_R1C1String_ReturnsValue()
    {
        var sheet = MakeSheet((2, 3, new NumberValue(99)));
        _eval.Evaluate("=INDIRECT(\"R2C3\",FALSE)", sheet).Should().Be(new NumberValue(99));
    }

    [Fact] public void Indirect_RelativeR1C1String_ReturnsValueRelativeToCurrentCell()
    {
        var sheet = MakeSheet((4, 6, new NumberValue(123)));

        _eval.Evaluate("=INDIRECT(\"R[-1]C[1]\",FALSE)", sheet, currentCell: new CellAddress(sheet.Id, 5, 5))
            .Should().Be(new NumberValue(123));
    }

    [Fact] public void Indirect_R1C1RangeString_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));

        _eval.Evaluate("=SUM(INDIRECT(\"R1C1:R3C1\",FALSE))", sheet)
            .Should().Be(new NumberValue(6));
    }

    [Fact] public void Indirect_A1FullRowString_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (1, 2, new NumberValue(2)));

        _eval.Evaluate("=SUM(INDIRECT(\"1:1\"))", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Fact] public void Indirect_A1FullColumnString_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)));

        _eval.Evaluate("=SUM(INDIRECT(\"B:B\"))", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Theory]
    [InlineData("=INDIRECT(\"A:A\")")]
    [InlineData("=INDIRECT(\"A:XFD\")")]
    [InlineData("=INDIRECT(\"1:1048576\")")]
    public void Indirect_HostileLargeRanges_ReturnRefWithoutMaterializing(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void NonFastPathFullColumnRanges_ClampToUsedRange_ComputeLikeExcel()
    {
        // Excel evaluates full-column ranges over the populated extent, not the whole 1,048,576-row
        // grid. These non-fast-path aggregates used to return #REF! (refusing to materialize); they
        // now clamp to the used range and compute the same result Excel does.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(4)),
            (3, 1, new NumberValue(6)));

        _eval.Evaluate("=COUNTA(A:A)", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=STDEV(A:A)", sheet).Should().Be(new NumberValue(2));      // sample stdev of 2,4,6
        _eval.Evaluate("=CONCAT(A:A)", sheet).Should().Be(new TextValue("246"));
        _eval.Evaluate("=MAX(A:A)", sheet).Should().Be(new NumberValue(6));
    }

    [Theory]
    [InlineData("=COUNTA(A:A)")]
    [InlineData("=COUNT(A:A)")]
    [InlineData("=SUM(A:A)")]
    public void FullColumnRanges_EmptySheet_ComputeZeroNotRef(string formula)
    {
        // On an empty sheet the used range is empty, so full-column aggregates evaluate over nothing
        // (0), matching Excel — rather than returning #REF!.
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void RangeMaterializationLimit_ReturnsCatchableError()
    {
        _eval.Evaluate("=IFERROR(COUNTA(A:A),0)", MakeSheet()).Should().Be(new NumberValue(0));
    }

    [Fact] public void Offset_A1FullColumnReferenceWithExplicitHeight_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)),
            (3, 2, new NumberValue(100)));

        _eval.Evaluate("=SUM(OFFSET(B:B,0,0,2,1))", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Fact] public void Offset_A1FullRowReferenceWithExplicitWidth_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (1, 2, new NumberValue(2)),
            (1, 3, new NumberValue(100)));

        _eval.Evaluate("=SUM(OFFSET(1:1,0,0,1,2))", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Fact] public void Offset_SheetQualifiedFullColumnReference_ReturnsRangeValue()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var data = wb.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 1, 2), new NumberValue(1));
        data.SetCell(new CellAddress(data.Id, 2, 2), new NumberValue(2));
        data.SetCell(new CellAddress(data.Id, 3, 2), new NumberValue(100));

        _eval.Evaluate("=SUM(OFFSET(Data!B:B,0,0,2,1))", sheet, wb)
            .Should().Be(new NumberValue(3));
    }

    [Fact] public void Indirect_InvalidR1C1String_ReturnsRefError()
    {
        _eval.Evaluate("=INDIRECT(\"R0C1\",FALSE)", MakeSheet()).Should().Be(ErrorValue.Ref);
    }

    [Fact] public void Indirect_A1ArgumentError_PropagatesError() =>
        _eval.Evaluate("=INDIRECT(\"A1\",NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Address_AbsoluteRef_ReturnsString() =>
        _eval.Evaluate("=ADDRESS(2,3)", MakeSheet()).Should().Be(new TextValue("$C$2"));

    [Fact] public void Address_RelativeRef_ReturnsString() =>
        _eval.Evaluate("=ADDRESS(2,3,4)", MakeSheet()).Should().Be(new TextValue("C2"));

    [Fact] public void Address_R1C1AbsoluteRef_ReturnsString() =>
        _eval.Evaluate("=ADDRESS(2,3,1,FALSE)", MakeSheet()).Should().Be(new TextValue("R2C3"));

    [Fact] public void Address_R1C1RelativeRef_ReturnsString() =>
        _eval.Evaluate("=ADDRESS(2,3,4,FALSE)", MakeSheet()).Should().Be(new TextValue("R[2]C[3]"));

    [Fact] public void Address_SimpleSheetTextDoesNotAddQuotes() =>
        _eval.Evaluate("=ADDRESS(2,3,1,TRUE,\"Sheet1\")", MakeSheet()).Should().Be(new TextValue("Sheet1!$C$2"));

    [Fact] public void Address_SheetTextEscapesApostrophes() =>
        _eval.Evaluate("=ADDRESS(2,3,1,TRUE,\"O'Brien\")", MakeSheet()).Should().Be(new TextValue("'O''Brien'!$C$2"));

    [Fact] public void Address_InvalidAbsNum_ReturnsValueError() =>
        _eval.Evaluate("=ADDRESS(2,3,5)", MakeSheet()).Should().Be(ErrorValue.Value);

    [Fact] public void Address_AbsNumError_PropagatesError() =>
        _eval.Evaluate("=ADDRESS(2,3,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Address_A1Error_PropagatesError() =>
        _eval.Evaluate("=ADDRESS(2,3,1,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Address_SheetTextError_PropagatesError() =>
        _eval.Evaluate("=ADDRESS(2,3,1,TRUE,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void N_Text_ReturnsZero() =>
        _eval.Evaluate("=N(\"hello\")", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact] public void N_Number_ReturnsNumber() =>
        _eval.Evaluate("=N(42)", MakeSheet()).Should().Be(new NumberValue(42));

    [Fact] public void N_True_ReturnsOne() =>
        _eval.Evaluate("=N(TRUE)", MakeSheet()).Should().Be(new NumberValue(1));

    [Fact]
    public void N_DateTimeCell_ReturnsDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet((1, 1, date));

        _eval.Evaluate("=N(A1)", sheet).Should().Be(new NumberValue(date.Value));
    }

    // ── SEQUENCE ──────────────────────────────────────────────────────────────────

    [Fact]
    public void N_RangeArgument_SpillsElementwise()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, new NumberValue(42)),
            (2, 1, new TextValue("x")),
            (3, 1, new BoolValue(true)),
            (4, 1, date),
            (5, 1, ErrorValue.NA));

        AssertColumn(
            _eval.Evaluate("=N(A1:A5)", sheet),
            new NumberValue(42),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(date.Value),
            ErrorValue.NA);
    }

    [Fact]
    public void Sequence_3Rows_ReturnsColumnVector()
    {
        var result = _eval.Evaluate("=SEQUENCE(3)", MakeSheet());
        result.Should().BeOfType<RangeValue>();
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sequence_2x3_ReturnsMatrix()
    {
        var result = _eval.Evaluate("=SEQUENCE(2,3)", MakeSheet());
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 2].Should().Be(new NumberValue(3));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Sequence_BlankLeadingArguments_UseExcelDefaults()
    {
        var cols = _eval.Evaluate("=SEQUENCE(,2)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        cols.RowCount.Should().Be(1);
        cols.ColCount.Should().Be(2);
        cols.Cells[0, 0].Should().Be(new NumberValue(1));
        cols.Cells[0, 1].Should().Be(new NumberValue(2));

        var start = _eval.Evaluate("=SEQUENCE(,,5)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        start.RowCount.Should().Be(1);
        start.ColCount.Should().Be(1);
        start.Cells[0, 0].Should().Be(new NumberValue(5));

        var step = _eval.Evaluate("=SEQUENCE(,,,2)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        step.RowCount.Should().Be(1);
        step.ColCount.Should().Be(1);
        step.Cells[0, 0].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sequence_WithStartAndStep_CountsByTwos()
    {
        var result = _eval.Evaluate("=SEQUENCE(4,1,0,2)", MakeSheet());
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new NumberValue(0));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(4));
        rv.Cells[3, 0].Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Sequence_HugeRowsCols_ReturnsValueError()
    {
        _eval.Evaluate("=SEQUENCE(1000,1001)", MakeSheet()).Should().Be(ErrorValue.Value,
            "rows×cols > 1,000,000 must return #VALUE! rather than allocating a massive array");
    }

    // ── FILTER ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sequence_AcceptsSpilledScalarControlArguments()
    {
        var result = _eval.Evaluate("=SEQUENCE(SEQUENCE(1,,2),SEQUENCE(1,,3),SEQUENCE(1,,5),SEQUENCE(1,,2))", MakeSheet());

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(5));
        rv.Cells[0, 1].Should().Be(new NumberValue(7));
        rv.Cells[0, 2].Should().Be(new NumberValue(9));
        rv.Cells[1, 0].Should().Be(new NumberValue(11));
        rv.Cells[1, 1].Should().Be(new NumberValue(13));
        rv.Cells[1, 2].Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Sequence_NonFiniteRows_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=SEQUENCE(A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sequence_HugeFiniteDimensions_ReturnsValueError()
    {
        _eval.Evaluate("=SEQUENCE(2147483648)", MakeSheet()).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=SEQUENCE(1,2147483648)", MakeSheet()).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=SEQUENCE(-2147483648)", MakeSheet()).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=SEQUENCE(1,-2147483648)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sequence_NonFiniteStart_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=SEQUENCE(1,1,A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Sequence_NonFiniteStep_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=SEQUENCE(1,1,1,A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Sequence_OverflowingGeneratedValue_ReturnsNumError()
    {
        _eval.Evaluate("=SEQUENCE(1,2,1E308,1E308)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Sequence_ColumnsError_PropagatesError() =>
        _eval.Evaluate("=SEQUENCE(2,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Sequence_StartError_PropagatesError() =>
        _eval.Evaluate("=SEQUENCE(2,1,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Sequence_StepError_PropagatesError() =>
        _eval.Evaluate("=SEQUENCE(2,1,1,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Sum_FlattensSequenceDynamicArrayResult()
    {
        _eval.Evaluate("=SUM(SEQUENCE(3,2,1,1))", MakeSheet())
            .Should().Be(new NumberValue(21));
    }

    [Fact]
    public void Filter_ByBoolArray_ReturnsMatchingRows()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(10)), (2,1,new NumberValue(20)), (3,1,new NumberValue(30)),
            (1,2,new BoolValue(true)), (2,2,new BoolValue(false)), (3,2,new BoolValue(true)));
        var result = _eval.Evaluate("=FILTER(A1:A3,B1:B3)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(10));
        rv.Cells[1, 0].Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Filter_NoMatches_ReturnsIfEmptyArg()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(10)),
            (1,2,new BoolValue(false)));
        var result = _eval.Evaluate("=FILTER(A1:A1,B1:B1,\"none\")", sheet);
        result.Should().BeOfType<RangeValue>();
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new TextValue("none"));
    }

    [Fact]
    public void Filter_TreatsScalarArrayAndIncludeAsSingleCellArrays()
    {
        var included = _eval.Evaluate("=FILTER(5,TRUE)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;
        included.RowCount.Should().Be(1);
        included.ColCount.Should().Be(1);
        included.Cells[0, 0].Should().Be(new NumberValue(5));

        var empty = _eval.Evaluate("=FILTER(5,FALSE,\"empty\")", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;
        empty.RowCount.Should().Be(1);
        empty.ColCount.Should().Be(1);
        empty.Cells[0, 0].Should().Be(new TextValue("empty"));
    }

    [Fact]
    public void Filter_NoMatchesWithoutIfEmpty_ReturnsCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new BoolValue(false)));

        _eval.Evaluate("=FILTER(A1:A1,B1:B1)", sheet).Should().Be(new ErrorValue("#CALC!"));
        _eval.Evaluate("=ERROR.TYPE(FILTER(A1:A1,B1:B1))", sheet).Should().Be(new NumberValue(14));
    }

    [Fact]
    public void Filter_BlankIfEmptyArgument_ReturnsCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new BoolValue(false)));

        _eval.Evaluate("=FILTER(A1:A1,B1:B1,)", sheet).Should().Be(new ErrorValue("#CALC!"));
    }

    [Fact]
    public void Iferror_CatchesFilterNoMatchesCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new BoolValue(false)));

        _eval.Evaluate("=IFERROR(FILTER(A1:A1,B1:B1),\"fallback\")", sheet)
            .Should().Be(new TextValue("fallback"));
    }

    [Fact]
    public void Ifna_DoesNotCatchFilterNoMatchesCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new BoolValue(false)));

        _eval.Evaluate("=IFNA(FILTER(A1:A1,B1:B1),\"fallback\")", sheet)
            .Should().Be(new ErrorValue("#CALC!"));
    }

    [Fact]
    public void Choose_DoesNotEvaluateUnselectedFilterCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new BoolValue(false)));

        _eval.Evaluate("=CHOOSE(2,FILTER(A1:A1,B1:B1),42)", sheet)
            .Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Filter_MultiColumn_PreservesAllColumns()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new TextValue("A")), (1,3,new BoolValue(true)),
            (2,1,new NumberValue(2)), (2,2,new TextValue("B")), (2,3,new BoolValue(false)),
            (3,1,new NumberValue(3)), (3,2,new TextValue("C")), (3,3,new BoolValue(true)));
        var result = _eval.Evaluate("=FILTER(A1:B3,C1:C3)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 1].Should().Be(new TextValue("A"));
        rv.Cells[1, 1].Should().Be(new TextValue("C"));
    }

    [Fact]
    public void Filter_DateTimeIncludeCell_TreatsDateSerialAsTrue()
    {
        var includeDate = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, new TextValue("keep")), (1, 2, includeDate),
            (2, 1, new TextValue("drop")), (2, 2, new NumberValue(0)));

        var result = _eval.Evaluate("=FILTER(A1:A2,B1:B2)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new TextValue("keep"));
    }

    [Fact]
    public void Filter_BlankIncludeCell_IsFalse()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("included")),
            (2, 1, new TextValue("blank")),
            (3, 1, new TextValue("excluded")),
            (1, 2, new BoolValue(true)),
            (3, 2, new BoolValue(false)));

        var result = _eval.Evaluate("=FILTER(A1:A3,B1:B3,\"empty\")", sheet);
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new TextValue("included"));
    }

    [Fact]
    public void Filter_TextIncludeCell_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("keep")), (1, 2, new TextValue("x")),
            (2, 1, new TextValue("drop")), (2, 2, new BoolValue(false)));

        _eval.Evaluate("=FILTER(A1:A2,B1:B2,\"empty\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Filter_MismatchedIncludeRows_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(20)),
            (2, 1, new NumberValue(30)), (2, 2, new NumberValue(40)),
            (1, 3, new BoolValue(true)));

        _eval.Evaluate("=FILTER(A1:B2,C1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Filter_HorizontalInclude_ReturnsMatchingColumns()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A1")), (1, 2, new TextValue("B1")), (1, 3, new TextValue("C1")),
            (2, 1, new TextValue("A2")), (2, 2, new TextValue("B2")), (2, 3, new TextValue("C2")),
            (3, 1, new BoolValue(true)), (3, 2, new BoolValue(false)), (3, 3, new BoolValue(true)));

        var result = _eval.Evaluate("=FILTER(A1:C2,A3:C3)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("A1"));
        rv.Cells[0, 1].Should().Be(new TextValue("C1"));
        rv.Cells[1, 0].Should().Be(new TextValue("A2"));
        rv.Cells[1, 1].Should().Be(new TextValue("C2"));
    }

    // ── SORT ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Filter_IncludeRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")),
            (1, 2, ErrorValue.NA));

        _eval.Evaluate("=FILTER(A1:A1,B1:B1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Filter_AcceptsArrayComparisonIncludeExpression()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));

        var rv = _eval.Evaluate("=FILTER(A1:A3,A1:A3>1)", sheet).Should().BeOfType<RangeValue>().Subject;

        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(1);
        rv.At(1, 1).Should().Be(new NumberValue(2));
        rv.At(2, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sumproduct_AcceptsArrayArithmeticExpression()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(20)),
            (3, 1, new NumberValue(3)), (3, 2, new NumberValue(30)));

        _eval.Evaluate("=SUMPRODUCT(A1:A3+1,B1:B3)", sheet).Should().Be(new NumberValue(200));
    }

    [Fact]
    public void Aggregate_FlattensDynamicArrayArithmeticResult()
    {
        _eval.Evaluate("=SUM(SEQUENCE(2,2)*2)", MakeSheet()).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Aggregate_FlattensDynamicArrayUnaryMinusResult()
    {
        _eval.Evaluate("=SUM(-SEQUENCE(2,2))", MakeSheet()).Should().Be(new NumberValue(-10));
    }

    [Fact]
    public void Aggregate_FlattensDynamicArrayPercentResult()
    {
        _eval.Evaluate("=SUM(SEQUENCE(2,2)%)", MakeSheet()).Should().Be(new NumberValue(0.1));
    }

    [Fact]
    public void DynamicArrayBinaryExpression_BroadcastsRowAndColumnVectors()
    {
        _eval.Evaluate("=SUM(SEQUENCE(3,1)+SEQUENCE(1,3))", MakeSheet()).Should().Be(new NumberValue(36));
    }

    [Fact]
    public void Sum_FlattensFilterDynamicArrayResult()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3)), (1, 2, new BoolValue(true)),
            (2, 1, new NumberValue(1)), (2, 2, new BoolValue(false)),
            (3, 1, new NumberValue(2)), (3, 2, new BoolValue(true)));

        _eval.Evaluate("=SUM(FILTER(A1:A3,B1:B3))", sheet)
            .Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Filter_ArrayArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new BoolValue(true)));

        _eval.Evaluate("=FILTER(NA(),A1:A1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Filter_IncludeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("A")));

        _eval.Evaluate("=FILTER(A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sort_ArrayArgumentError_PropagatesError()
    {
        _eval.Evaluate("=SORT(NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sort_TreatsScalarArrayAsSingleCellArray()
    {
        var result = _eval.Evaluate("=SORT(5)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Sort_SingleColumn_SortsAscending()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(3)), (2,1,new NumberValue(1)), (3,1,new NumberValue(2)));
        var result = _eval.Evaluate("=SORT(A1:A3)", sheet);
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sort_SingleColumn_SortsDescending()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(3)), (2,1,new NumberValue(1)), (3,1,new NumberValue(2)));
        var result = _eval.Evaluate("=SORT(A1:A3,1,-1)", sheet);
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new NumberValue(3));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sort_MultiColumn_SortsBySecondColumn()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("B")), (1,2,new NumberValue(2)),
            (2,1,new TextValue("A")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));
        // SORT(A1:B3, 2, 1) → sort by col 2 ascending
        var result = _eval.Evaluate("=SORT(A1:B3,2,1)", sheet);
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 0].Should().Be(new TextValue("B"));
        rv.Cells[2, 0].Should().Be(new TextValue("C"));
    }

    [Fact]
    public void Sort_AcceptsSpilledScalarControlArguments()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("B")), (1,2,new NumberValue(2)),
            (2,1,new TextValue("A")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));

        var rv = _eval.Evaluate("=SORT(A1:B3,SEQUENCE(1,,2),SEQUENCE(1,,-1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new TextValue("B"));
        rv.Cells[2, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sort_ZeroSortIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (2,1,new NumberValue(2)));
        _eval.Evaluate("=SORT(A1:A2,0)", sheet).Should().Be(ErrorValue.Value,
            "sort_index=0 is invalid (1-based) and must not cause an IndexOutOfRangeException");
    }

    [Fact]
    public void Sort_OutOfBoundsRowSortIndex_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)));

        _eval.Evaluate("=SORT(A1:B2,3)", sheet).Should().Be(ErrorValue.Value,
            "row-oriented SORT sort_index must refer to an existing column");
    }

    [Fact]
    public void Sort_OutOfBoundsColumnSortIndex_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(2)));

        _eval.Evaluate("=SORT(A1:B2,3,1,TRUE)", sheet).Should().Be(ErrorValue.Value,
            "column-oriented SORT sort_index must refer to an existing row");
    }

    [Fact]
    public void Sort_InvalidSortOrder_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)), (2,1,new NumberValue(1)));

        _eval.Evaluate("=SORT(A1:A2,1,0)", sheet).Should().Be(ErrorValue.Value,
            "Excel only accepts 1 or -1 for SORT sort_order");
    }

    [Fact]
    public void Sortby_SortsRowsBySeparateKeyArray()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(2)));

        var result = _eval.Evaluate("=SORTBY(A1:A3,B1:B3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new TextValue("B"));
        rv.Cells[1, 0].Should().Be(new TextValue("C"));
        rv.Cells[2, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sortby_OmittedSortOrder_DefaultsAscending()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(2)));

        var result = _eval.Evaluate("=SORTBY(A1:A3,B1:B3,)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.Cells[0, 0].Should().Be(new TextValue("B"));
        rv.Cells[1, 0].Should().Be(new TextValue("C"));
        rv.Cells[2, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sortby_TreatsScalarArrayAndKeyAsSingleCellArrays()
    {
        var result = _eval.Evaluate("=SORTBY(5,1)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Sortby_SortsColumnsBySeparateKeyArrayDescending()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(3)), (2,3,new NumberValue(2)));

        var result = _eval.Evaluate("=SORTBY(A1:C1,A2:C2,-1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new TextValue("B"));
        rv.Cells[0, 1].Should().Be(new TextValue("C"));
        rv.Cells[0, 2].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sortby_AcceptsSpilledScalarSortOrder()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(2)));

        var rv = _eval.Evaluate("=SORTBY(A1:A3,B1:B3,SEQUENCE(1,,-1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 0].Should().Be(new TextValue("C"));
        rv.Cells[2, 0].Should().Be(new TextValue("B"));
    }

    [Fact]
    public void Sortby_SortOrderError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(2)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)));

        _eval.Evaluate("=SORTBY(A1:A2,B1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sortby_RangeInSortOrderSlot_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)), (2,3,new NumberValue(4)));

        _eval.Evaluate("=SORTBY(A1:A2,B1:B2,C1:C2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sortby_MismatchedKeyShape_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (2,1,new TextValue("B")),
            (1,2,new NumberValue(1)));

        _eval.Evaluate("=SORTBY(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value,
            "SORTBY key arrays must align to either the sorted rows or sorted columns");
    }

    [Fact]
    public void Take_PositiveRowsAndColumns_ReturnsTopLeftSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=TAKE(A1:C3,2,2)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Take_TreatsScalarArrayAsSingleCellArray()
    {
        var taken = _eval.Evaluate("=TAKE(5,1)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;
        taken.RowCount.Should().Be(1);
        taken.ColCount.Should().Be(1);
        taken.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void TakeAndDrop_AcceptSpilledScalarSliceCounts()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var taken = _eval.Evaluate("=TAKE(A1:C3,SEQUENCE(1,,2),SEQUENCE(1,,2))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        taken.RowCount.Should().Be(2);
        taken.ColCount.Should().Be(2);
        taken.Cells[1, 1].Should().Be(new NumberValue(5));

        var dropped = _eval.Evaluate("=DROP(A1:C3,SEQUENCE(1,,1),SEQUENCE(1,,1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        dropped.RowCount.Should().Be(2);
        dropped.ColCount.Should().Be(2);
        dropped.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void DynamicArrayFunctions_AcceptSpilledScalarControlArguments()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(1)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)), (2,3,new NumberValue(3)),
            (1,4,new NumberValue(5)), (1,5,new NumberValue(6)), (1,6,new NumberValue(7)));

        var toCol = _eval.Evaluate("=TOCOL(A1:B2,,SEQUENCE(1,,1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        toCol.Cells[0, 0].Should().Be(new NumberValue(1));
        toCol.Cells[1, 0].Should().Be(new NumberValue(3));
        toCol.Cells[2, 0].Should().Be(new NumberValue(2));
        toCol.Cells[3, 0].Should().Be(new NumberValue(4));

        var wrapped = _eval.Evaluate("=WRAPROWS(D1:F1,SEQUENCE(1,,2))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        wrapped.RowCount.Should().Be(2);
        wrapped.ColCount.Should().Be(2);
        wrapped.Cells[1, 0].Should().Be(new NumberValue(7));

        var expanded = _eval.Evaluate("=EXPAND(A1:A1,SEQUENCE(1,,2),SEQUENCE(1,,2),0)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        expanded.RowCount.Should().Be(2);
        expanded.ColCount.Should().Be(2);
        expanded.Cells[1, 1].Should().Be(new NumberValue(0));

        var uniqueByColumn = _eval.Evaluate("=UNIQUE(A1:C2,SEQUENCE(1,,1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        uniqueByColumn.RowCount.Should().Be(2);
        uniqueByColumn.ColCount.Should().Be(2);
        uniqueByColumn.Cells[0, 0].Should().Be(new NumberValue(1));
        uniqueByColumn.Cells[0, 1].Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Take_OmittedRows_TakesRequestedColumnsFromAllRows()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=TAKE(A1:C3,,2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[2, 1].Should().Be(new NumberValue(8));
    }

    [Fact]
    public void Drop_OmittedRows_DropsRequestedColumnsFromAllRows()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=DROP(A1:C3,,1)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 1].Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Take_NegativeRowsAndColumns_ReturnsBottomRightSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=TAKE(A1:C3,-2,-2)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(5));
        rv.Cells[0, 1].Should().Be(new NumberValue(6));
        rv.Cells[1, 0].Should().Be(new NumberValue(8));
        rv.Cells[1, 1].Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Drop_PositiveRowsAndColumns_RemovesTopLeftSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=DROP(A1:C3,1,1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(5));
        rv.Cells[0, 1].Should().Be(new NumberValue(6));
        rv.Cells[1, 0].Should().Be(new NumberValue(8));
        rv.Cells[1, 1].Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Drop_NegativeRowsAndColumns_RemovesBottomRightSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=DROP(A1:C3,-1,-1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Take_ZeroRows_ReturnsCalcError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=TAKE(A1:A1,0)", sheet).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Drop_ZeroRowsOrColumns_ReturnsCalcError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (1,2,new NumberValue(2)));

        _eval.Evaluate("=DROP(A1:B1,0)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=DROP(A1:B1,,0)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=DROP(5,0)", MakeSheet()).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void TakeAndDrop_HugeFiniteSliceCount_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (2,1,new NumberValue(2)));

        _eval.Evaluate("=TAKE(A1:A2,2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=TAKE(A1:A2,-2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=TAKE(A1:A2,-2147483649)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DROP(A1:A2,2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DROP(A1:A2,-2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DROP(A1:A2,-2147483649)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Drop_AllRows_ReturnsCalcError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=DROP(A1:A1,1)", sheet).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Chooserows_ReordersRowsAndAllowsRepeats()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSEROWS(A1:B3,3,1,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new TextValue("A"));
        rv.Cells[2, 0].Should().Be(new TextValue("C"));
    }

    [Fact]
    public void Chooserows_NegativeIndexSelectsFromEnd()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")),
            (2,1,new TextValue("B")),
            (3,1,new TextValue("C")));

        var result = _eval.Evaluate("=CHOOSEROWS(A1:A3,-1,-3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Chooserows_AcceptsDynamicArrayRowIndexes()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSEROWS(A1:B3,VSTACK(3,1))", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[0, 1].Should().Be(new NumberValue(3));
        rv.Cells[1, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 1].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Choosecols_ReordersColumnsAndAllowsRepeats()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(2)), (2,3,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSECOLS(A1:C2,3,1,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[0, 1].Should().Be(new TextValue("A"));
        rv.Cells[0, 2].Should().Be(new TextValue("C"));
    }

    [Fact]
    public void Choosecols_NegativeIndexSelectsFromEnd()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")));

        var result = _eval.Evaluate("=CHOOSECOLS(A1:C1,-1,-3)", sheet);

        var rv = (RangeValue)result;
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[0, 1].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Choosecols_AcceptsDynamicArrayColumnIndexes()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(2)), (2,3,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSECOLS(A1:C2,HSTACK(1,3))", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[0, 1].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 1].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void ChooserowsAndChoosecols_TreatScalarArrayAsSingleCellArray()
    {
        var rows = _eval.Evaluate("=CHOOSEROWS(5,1)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        rows.RowCount.Should().Be(1);
        rows.ColCount.Should().Be(1);
        rows.Cells[0, 0].Should().Be(new NumberValue(5));

        var cols = _eval.Evaluate("=CHOOSECOLS(\"x\",1)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        cols.RowCount.Should().Be(1);
        cols.ColCount.Should().Be(1);
        cols.Cells[0, 0].Should().Be(new TextValue("x"));
    }

    [Fact]
    public void Chooserows_ZeroIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=CHOOSEROWS(A1:A1,0)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Choosecols_OutOfRangeIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=CHOOSECOLS(A1:A1,2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void ChooserowsAndChoosecols_HugeFiniteIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new TextValue("A")), (2,1,new TextValue("B")));

        _eval.Evaluate("=CHOOSEROWS(A1:A2,2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSEROWS(A1:A2,-2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSEROWS(A1:A2,-2147483649)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSECOLS(A1:A2,2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSECOLS(A1:A2,-2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSECOLS(A1:A2,-2147483649)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Vstack_AppendsRowsAndPadsShorterArraysWithNA()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")),
            (2,1,new TextValue("C")), (2,2,new TextValue("D")),
            (1,3,new TextValue("E")));

        var result = _eval.Evaluate("=VSTACK(A1:B2,C1:C1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 1].Should().Be(new TextValue("D"));
        rv.Cells[2, 0].Should().Be(new TextValue("E"));
        rv.Cells[2, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Hstack_AppendsColumnsAndPadsShorterArraysWithNA()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (2,1,new TextValue("B")),
            (1,2,new TextValue("C")));

        var result = _eval.Evaluate("=HSTACK(A1:A2,B1:B1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 0].Should().Be(new TextValue("B"));
        rv.Cells[0, 1].Should().Be(new TextValue("C"));
        rv.Cells[1, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void VstackAndHstack_TreatScalarArgumentsAsSingleCellArrays()
    {
        var vstack = _eval.Evaluate("=VSTACK(1,\"two\",TRUE)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        vstack.RowCount.Should().Be(3);
        vstack.ColCount.Should().Be(1);
        vstack.Cells[0, 0].Should().Be(new NumberValue(1));
        vstack.Cells[1, 0].Should().Be(new TextValue("two"));
        vstack.Cells[2, 0].Should().Be(new BoolValue(true));

        var hstack = _eval.Evaluate("=HSTACK(1,\"two\",TRUE)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        hstack.RowCount.Should().Be(1);
        hstack.ColCount.Should().Be(3);
        hstack.Cells[0, 0].Should().Be(new NumberValue(1));
        hstack.Cells[0, 1].Should().Be(new TextValue("two"));
        hstack.Cells[0, 2].Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Vstack_ScalarErrorArgument_SpillsErrorAsCell()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        var result = _eval.Evaluate("=VSTACK(A1:A1,NA())", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Hstack_ScalarErrorArgument_SpillsErrorAsCell()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        var result = _eval.Evaluate("=HSTACK(A1:A1,NA())", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[0, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Torow_DefaultScan_ReturnsSingleRowByRows()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)));

        var result = _eval.Evaluate("=TOROW(A1:B2)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(4);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[0, 2].Should().Be(new NumberValue(3));
        rv.Cells[0, 3].Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Tocol_ScanByColumn_ReturnsSingleColumnByColumns()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)));

        var result = _eval.Evaluate("=TOCOL(A1:B2,0,TRUE)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(4);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(3));
        rv.Cells[2, 0].Should().Be(new NumberValue(2));
        rv.Cells[3, 0].Should().Be(new NumberValue(4));
    }

    [Fact]
    public void TorowAndTocol_TreatScalarArgumentAsSingleCellArray()
    {
        var row = _eval.Evaluate("=TOROW(\"x\")", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        row.RowCount.Should().Be(1);
        row.ColCount.Should().Be(1);
        row.Cells[0, 0].Should().Be(new TextValue("x"));

        var col = _eval.Evaluate("=TOCOL(42)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        col.RowCount.Should().Be(1);
        col.ColCount.Should().Be(1);
        col.Cells[0, 0].Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Torow_IgnoreBlanksAndErrors_RemovesBoth()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,ErrorValue.NA),
            (2,2,new NumberValue(2)));

        var result = _eval.Evaluate("=TOROW(A1:B2,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
    }

    [Fact]
    public void TorowAndTocol_IgnoreBlanks_KeepsZeroLengthText()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("")),
            (1,3,new NumberValue(2)));

        var row = _eval.Evaluate("=TOROW(A1:C1,1)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        row.RowCount.Should().Be(1);
        row.ColCount.Should().Be(2);
        row.Cells[0, 0].Should().Be(new TextValue(""));
        row.Cells[0, 1].Should().Be(new NumberValue(2));

        var col = _eval.Evaluate("=TOCOL(A1:C1,1)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        col.RowCount.Should().Be(2);
        col.ColCount.Should().Be(1);
        col.Cells[0, 0].Should().Be(new TextValue(""));
        col.Cells[1, 0].Should().Be(new NumberValue(2));
    }

    [Fact]
    public void TorowAndTocol_AllValuesIgnored_ReturnCalcError()
    {
        var sheet = MakeSheet((1, 1, ErrorValue.NA));

        _eval.Evaluate("=TOROW(A1:B1,3)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=TOCOL(A1:B1,3)", sheet).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void TorowAndTocol_IgnoreScalarErrorsLikeSingleCellArrays()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=TOROW(NA(),2)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=TOCOL(NA(),2)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=TOROW(NA())", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=TOCOL(NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Tocol_InvalidIgnoreMode_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=TOCOL(A1:A1,4)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Wraprows_WrapsRowVectorAndPadsWithNA()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (1,4,new NumberValue(4)), (1,5,new NumberValue(5)));

        var result = _eval.Evaluate("=WRAPROWS(A1:E1,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 2].Should().Be(new NumberValue(3));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
        rv.Cells[1, 2].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wraprows_UsesCustomPadValue()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)));

        var result = _eval.Evaluate("=WRAPROWS(A1:C1,2,\"x\")", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[1, 0].Should().Be(new NumberValue(3));
        rv.Cells[1, 1].Should().Be(new TextValue("x"));
    }

    [Fact]
    public void WraprowsAndWrapcols_PadWithOneCellRange_UsesScalarValue()
    {
        var rowSheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (1, 2, new NumberValue(9)));
        var rows = _eval.Evaluate("=WRAPROWS(A1:A1,2,B1:B1)", rowSheet)
            .Should().BeOfType<RangeValue>().Subject;

        rows.RowCount.Should().Be(1);
        rows.ColCount.Should().Be(2);
        rows.Cells[0, 0].Should().Be(new NumberValue(1));
        rows.Cells[0, 1].Should().Be(new NumberValue(9));

        var colSheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (1, 2, new TextValue("z")));
        var cols = _eval.Evaluate("=WRAPCOLS(A1:A1,2,B1:B1)", colSheet)
            .Should().BeOfType<RangeValue>().Subject;

        cols.RowCount.Should().Be(2);
        cols.ColCount.Should().Be(1);
        cols.Cells[0, 0].Should().Be(new TextValue("a"));
        cols.Cells[1, 0].Should().Be(new TextValue("z"));
    }

    [Fact]
    public void WraprowsAndWrapcols_OmittedPadWith_DefaultsToNA()
    {
        var rowSheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)));
        var rows = _eval.Evaluate("=WRAPROWS(A1:C1,2,)", rowSheet)
            .Should().BeOfType<RangeValue>().Subject;

        rows.Cells[1, 1].Should().Be(ErrorValue.NA);

        var colSheet = MakeSheet(
            (1,1,new NumberValue(1)), (2,1,new NumberValue(2)), (3,1,new NumberValue(3)));
        var cols = _eval.Evaluate("=WRAPCOLS(A1:A3,2,)", colSheet)
            .Should().BeOfType<RangeValue>().Subject;

        cols.Cells[1, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wrapcols_WrapsColumnVectorByColumns()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),
            (2,1,new NumberValue(2)),
            (3,1,new NumberValue(3)),
            (4,1,new NumberValue(4)),
            (5,1,new NumberValue(5)));

        var result = _eval.Evaluate("=WRAPCOLS(A1:A5,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
        rv.Cells[0, 1].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
        rv.Cells[2, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void WraprowsAndWrapcols_TreatScalarArgumentAsOneItemVector()
    {
        var rows = _eval.Evaluate("=WRAPROWS(1,2)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        rows.RowCount.Should().Be(1);
        rows.ColCount.Should().Be(2);
        rows.Cells[0, 0].Should().Be(new NumberValue(1));
        rows.Cells[0, 1].Should().Be(ErrorValue.NA);

        var cols = _eval.Evaluate("=WRAPCOLS(\"x\",2)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        cols.RowCount.Should().Be(2);
        cols.ColCount.Should().Be(1);
        cols.Cells[0, 0].Should().Be(new TextValue("x"));
        cols.Cells[1, 0].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wraprows_InvalidWrapCount_ReturnsNumError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=WRAPROWS(A1:A1,0)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void WraprowsAndWrapcols_WrapCountError_PropagatesBeforeArrayShapeValidation()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)));

        _eval.Evaluate("=WRAPROWS(A1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=WRAPCOLS(A1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void WraprowsAndWrapcols_HugeFiniteWrapCount_ReturnsNumError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=WRAPROWS(A1:A1,2147483648)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WRAPROWS(A1:A1,-2147483648)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WRAPCOLS(A1:A1,2147483648)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WRAPCOLS(A1:A1,-2147483648)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Wrapcols_TwoDimensionalArray_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)));

        _eval.Evaluate("=WRAPCOLS(A1:B2,2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Expand_LargerRowsAndColumns_PadsWithNA()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")),
            (2,1,new TextValue("C")), (2,2,new TextValue("D")));

        var result = _eval.Evaluate("=EXPAND(A1:B2,3,4)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(4);
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 1].Should().Be(new TextValue("D"));
        rv.Cells[0, 2].Should().Be(ErrorValue.NA);
        rv.Cells[2, 0].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_UsesCustomPadValue()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        var result = _eval.Evaluate("=EXPAND(A1:A1,2,2,\"x\")", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new TextValue("x"));
        rv.Cells[1, 0].Should().Be(new TextValue("x"));
    }

    [Fact]
    public void Expand_PadWithOneCellRange_UsesScalarValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (1, 2, new NumberValue(9)));

        var result = _eval.Evaluate("=EXPAND(A1:A1,2,2,B1:B1)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(9));
        rv.Cells[1, 0].Should().Be(new NumberValue(9));
        rv.Cells[1, 1].Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Expand_OmittedPadWith_DefaultsToNA()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        var result = _eval.Evaluate("=EXPAND(A1:A1,2,2,)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.Cells[0, 1].Should().Be(ErrorValue.NA);
        rv.Cells[1, 0].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_TreatsScalarArgumentAsSingleCellArray()
    {
        var result = _eval.Evaluate("=EXPAND(1,2,2)", MakeSheet());

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(ErrorValue.NA);
        rv.Cells[1, 0].Should().Be(ErrorValue.NA);
        rv.Cells[1, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_RowsOnly_KeepsOriginalColumnCount()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (1,2,new NumberValue(2)));

        var result = _eval.Evaluate("=EXPAND(A1:B1,2)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[1, 0].Should().Be(ErrorValue.NA);
        rv.Cells[1, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_OmittedRowsArgument_KeepsOriginalRowCount()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (1,2,new NumberValue(2)));

        var result = _eval.Evaluate("=EXPAND(A1:B1,,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[0, 2].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_SmallerTarget_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (1,2,new NumberValue(2)));

        _eval.Evaluate("=EXPAND(A1:B1,1,1)", sheet).Should().Be(ErrorValue.Value);
    }

    // ── UNIQUE ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Expand_RowOrColumnError_PropagatesError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (1,2,new NumberValue(2)));

        _eval.Evaluate("=EXPAND(A1:B1,NA())", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=EXPAND(A1:B1,2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_TooManyCells_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=EXPAND(A1,1000001,1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact] public void Sort_SortIndexError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=SORT(A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Sort_SortOrderError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=SORT(A1:A1,1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Sort_ByColError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=SORT(A1:A1,1,1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Unique_SingleColumn_RemovesDuplicates()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (2,1,new NumberValue(2)),
            (3,1,new NumberValue(1)), (4,1,new NumberValue(3)));
        var result = _eval.Evaluate("=UNIQUE(A1:A4)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Unique_TreatsScalarArrayAsSingleCellArray()
    {
        var result = _eval.Evaluate("=UNIQUE(5)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Unique_ExactlyOnce_ReturnsOnlySingletons()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (2,1,new NumberValue(2)),
            (3,1,new NumberValue(1)), (4,1,new NumberValue(3)));
        // UNIQUE(A1:A4, FALSE, TRUE) → only values appearing exactly once
        var result = _eval.Evaluate("=UNIQUE(A1:A4,FALSE,TRUE)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Unique_ExactlyOnceWithNoSingletons_ReturnsCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(2)), (4, 1, new NumberValue(2)));

        _eval.Evaluate("=UNIQUE(A1:A4,FALSE,TRUE)", sheet)
            .Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Unique_MultiColumn_DeduplicatesRows()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)),
            (3,1,new TextValue("A")), (3,2,new NumberValue(1)));
        var result = _eval.Evaluate("=UNIQUE(A1:B3)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
    }

    // ── SUBTOTAL ─────────────────────────────────────────────────────────────

    [Fact]
    public void Unique_DistinguishesScalarTypesWhenDeduplicating()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new TextValue("1")),
            (3, 1, new BoolValue(true)),
            (4, 1, new TextValue("TRUE")),
            (5, 1, new NumberValue(1)));

        var result = _eval.Evaluate("=UNIQUE(A1:A5)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(4);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new TextValue("1"));
        rv.Cells[2, 0].Should().Be(new BoolValue(true));
        rv.Cells[3, 0].Should().Be(new TextValue("TRUE"));
    }

    [Fact] public void Unique_ByColError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=UNIQUE(A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Unique_ArrayArgumentError_PropagatesError()
    {
        _eval.Evaluate("=UNIQUE(NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Unique_ExactlyOnceError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=UNIQUE(A1:A1,FALSE,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Subtotal_FuncNum9_Sum_NoHiddenRows()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        var result = _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet);
        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Subtotal_FuncNumOneCellRange_IsScalarized()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(9)),
            (1, 2, new NumberValue(10)),
            (2, 2, new NumberValue(20)));

        _eval.Evaluate("=SUBTOTAL(A1:A1,B1:B2)", sheet).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Subtotal_FuncNum1_Average_NoHiddenRows()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        var result = _eval.Evaluate("=SUBTOTAL(1,A1:A3)", sheet);
        result.Should().Be(new NumberValue(20));
    }

    [Theory]
    [InlineData(2, 8d)]
    [InlineData(6, 201_600d)]
    [InlineData(7, 2.138089935299395d)]
    [InlineData(8, 2d)]
    [InlineData(10, 4.571428571428571d)]
    [InlineData(11, 4d)]
    public void Subtotal_StatisticalAndProductFunctions_ReturnExpectedNumericResults(int functionNumber, double expected)
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(4)),
            (3, 1, new NumberValue(4)),
            (4, 1, new NumberValue(4)),
            (5, 1, new NumberValue(5)),
            (6, 1, new NumberValue(5)),
            (7, 1, new NumberValue(7)),
            (8, 1, new NumberValue(9)));

        var result = _eval.Evaluate($"=SUBTOTAL({functionNumber},A1:A8)", sheet);

        result.Should().BeOfType<NumberValue>().Subject.Value.Should().BeApproximately(expected, 1e-12);
    }

    [Fact]
    public void Subtotal_FuncNum4_Max_NoHiddenRows()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        var result = _eval.Evaluate("=SUBTOTAL(4,A1:A3)", sheet);
        result.Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Subtotal_FuncNum5_Min_NoHiddenRows()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        var result = _eval.Evaluate("=SUBTOTAL(5,A1:A3)", sheet);
        result.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Subtotal_FuncNum109_SumExcludesGroupHiddenRow()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        // Mark row 2 as group-hidden
        sheet.GroupHiddenRows.Add(2);
        var result = _eval.Evaluate("=SUBTOTAL(109,A1:A3)", sheet);
        result.Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_FuncNum9_IncludesGroupHiddenRow()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        // With funcNum=9 (not 109), hidden rows are NOT excluded
        sheet.GroupHiddenRows.Add(2);
        var result = _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet);
        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Subtotal_FuncNum9_ExcludesFilterHiddenRow()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        sheet.FilterHiddenRows.Add(2);

        var result = _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet);

        result.Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_FuncNum9_IncludesManualHiddenRow()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        sheet.HiddenRows.Add(2);

        var result = _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet);

        result.Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Subtotal_FuncNum9_IgnoresNestedSubtotalFormulaCell()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "SUBTOTAL(9,A1:A1)",
            Value = new NumberValue(10)
        });

        var result = _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet);

        result.Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_FuncNum9_ExcludesFilterHiddenRowsOnReferencedSheet()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var data = wb.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 1, 1), new NumberValue(10));
        data.SetCell(new CellAddress(data.Id, 2, 1), new NumberValue(20));
        data.SetCell(new CellAddress(data.Id, 3, 1), new NumberValue(30));
        data.FilterHiddenRows.Add(2);

        var result = _eval.Evaluate("=SUBTOTAL(9,Data!A1:A3)", sheet, wb);

        result.Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_FuncNum3_CountaIncludesTextCells()
    {
        // COUNTA should count text cells too, not just numbers
        var sheet = MakeSheet(
            (1, 1, new TextValue("hello")),
            (2, 1, new NumberValue(42)));
        // row 3 is blank (not set)
        var result = _eval.Evaluate("=SUBTOTAL(3,A1:A3)", sheet);
        result.Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Subtotal_FuncNum4_EmptyRange_ReturnsDivByZero()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=SUBTOTAL(4,B1:B3)", sheet);
        result.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Subtotal_SumRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(30)));

        _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Subtotal_CountaRangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("hello")),
            (2, 1, ErrorValue.NA));

        _eval.Evaluate("=SUBTOTAL(3,A1:A2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Subtotal_ExcludedHiddenRowError_IsIgnored()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(30)));
        sheet.GroupHiddenRows.Add(2);

        _eval.Evaluate("=SUBTOTAL(109,A1:A3)", sheet).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_SumDateRange_IncludesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date),
            (2, 1, new NumberValue(10)));

        _eval.Evaluate("=SUBTOTAL(9,A1:A2)", sheet).Should().Be(new NumberValue(date.Value + 10));
    }

    [Fact]
    public void Subtotal_OverflowingSum_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=SUBTOTAL(9,A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Subtotal_OverflowingAverage_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=SUBTOTAL(1,A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Subtotal_OverflowingProduct_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)));
        _eval.Evaluate("=SUBTOTAL(6,A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(11)]
    public void Subtotal_OverflowingStatisticalFunction_PreservesNumError(int functionNumber)
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)));

        _eval.Evaluate($"=SUBTOTAL({functionNumber},A1:A2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Transpose_Range_ReturnsTransposedMatrix()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)));

        var result = _eval.Evaluate("=TRANSPOSE(A1:C2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.At(1, 1).Should().Be(new NumberValue(1));
        rv.At(1, 2).Should().Be(new NumberValue(4));
        rv.At(3, 1).Should().Be(new NumberValue(3));
        rv.At(3, 2).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Sqrtpi_PositiveNumber_ReturnsSquareRootOfNumberTimesPi()
    {
        var result = _eval.Evaluate("=SQRTPI(2)", MakeSheet());

        result.Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(Math.Sqrt(2 * Math.PI), 1e-12);
    }

    [Fact]
    public void Numbervalue_ParsesCustomDecimalAndGroupSeparators()
    {
        _eval.Evaluate("=NUMBERVALUE(\"1.234,56\",\",\",\".\")", MakeSheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void Unicode_AndUnichar_RoundTripCodePoint()
    {
        _eval.Evaluate("=UNICODE(\"A\")", MakeSheet()).Should().Be(new NumberValue(65));
        _eval.Evaluate("=UNICHAR(9731)", MakeSheet()).Should().Be(new TextValue("\u2603"));
    }

    [Fact]
    public void Char_AndCode_MatchExcelAsciiBoundaryBehavior()
    {
        _eval.Evaluate("=CHAR(65)", MakeSheet()).Should().Be(new TextValue("A"));
        _eval.Evaluate("=CODE(\"Apple\")", MakeSheet()).Should().Be(new NumberValue(65));
        _eval.Evaluate("=CHAR(0)", MakeSheet()).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CODE(\"\")", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=CHAR(65.9)", "A")]
    [InlineData("=CHAR(255.9)", "\u00FF")]
    public void Char_TruncatesFractionalCodeBeforeDomainCheck(string formula, string expected) =>
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));

    [Theory]
    [InlineData("=CHAR(0.9)")]
    [InlineData("=CHAR(256.9)")]
    public void Char_TruncatedCodeOutsideExcelDomainReturnsValue(string formula) =>
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Value);

    [Fact]
    public void CharAndCode_RangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")),
            (1, 2, new NumberValue(65)),
            (2, 2, new NumberValue(66)));

        var code = _eval.Evaluate("=CODE(A1:A2)", sheet).Should().BeOfType<RangeValue>().Subject;
        code.Cells[0, 0].Should().Be(new NumberValue(65));
        code.Cells[1, 0].Should().Be(new NumberValue(66));

        var chars = _eval.Evaluate("=CHAR(B1:B2)", sheet).Should().BeOfType<RangeValue>().Subject;
        chars.Cells[0, 0].Should().Be(new TextValue("A"));
        chars.Cells[1, 0].Should().Be(new TextValue("B"));
    }

    [Fact]
    public void Exact_IsCaseSensitiveAndPropagatesErrors()
    {
        _eval.Evaluate("=EXACT(\"Excel\",\"Excel\")", MakeSheet()).Should().Be(new BoolValue(true));
        _eval.Evaluate("=EXACT(\"Excel\",\"excel\")", MakeSheet()).Should().Be(new BoolValue(false));
        _eval.Evaluate("=EXACT(NA(),\"x\")", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Exact_RangeArgument_SpillsElementwiseComparison()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("x")),
            (2, 1, new TextValue("y")));

        var result = _eval.Evaluate("=EXACT(A1:A2,\"x\")", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new BoolValue(true));
        result.Cells[1, 0].Should().Be(new BoolValue(false));
    }

    [Fact]
    public void Exact_LeadingOneCellRange_BroadcastsAcrossLaterTextArray()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("x")),
            (1, 2, new TextValue("x")),
            (2, 2, new TextValue("y")));

        AssertColumn(_eval.Evaluate("=EXACT(A1:A1,B1:B2)", sheet), new BoolValue(true), new BoolValue(false));
    }

    [Fact]
    public void Unichar_BasicAscii_ReturnsLetter() =>
        _eval.Evaluate("=UNICHAR(65)", MakeSheet()).Should().Be(new TextValue("A"));

    [Fact]
    public void Unichar_SupplementaryPlaneCodePoint_ReturnsSurrogatePairText() =>
        _eval.Evaluate("=UNICHAR(128512)", MakeSheet()).Should().Be(new TextValue(char.ConvertFromUtf32(128512)));

    [Fact]
    public void Unichar_TruncatesFractionalCodePoint()
    {
        _eval.Evaluate("=UNICHAR(65.9)", MakeSheet()).Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Unichar_Zero_ReturnsValueError() =>
        _eval.Evaluate("=UNICHAR(0)", MakeSheet()).Should().Be(ErrorValue.Value);

    [Fact]
    public void Unichar_OutOfRange_ReturnsValueError() =>
        _eval.Evaluate("=UNICHAR(1114112)", MakeSheet()).Should().Be(ErrorValue.Value);

    [Fact]
    public void Unichar_Surrogate_ReturnsValueError() =>
        _eval.Evaluate("=UNICHAR(55296)", MakeSheet()).Should().Be(ErrorValue.Value);

    [Fact]
    public void Unicode_BasicAscii_ReturnsCodePoint() =>
        _eval.Evaluate("=UNICODE(\"A\")", MakeSheet()).Should().Be(new NumberValue(65));

    [Fact]
    public void Unicode_SupplementaryPlaneText_ReturnsFullCodePoint() =>
        _eval.Evaluate("=UNICODE(UNICHAR(128512))", MakeSheet()).Should().Be(new NumberValue(128512));

    [Theory]
    [InlineData("=UNICODE(65)", 54)]
    [InlineData("=UNICODE(TRUE)", 84)]
    public void Unicode_CoercesScalarArgumentsToText(string formula, double expected) =>
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(expected));

    [Fact]
    public void Unicode_EmptyText_ReturnsValueError() =>
        _eval.Evaluate("=UNICODE(\"\")", MakeSheet()).Should().Be(ErrorValue.Value);

    [Fact]
    public void UnicharUnicodeAndNumbervalue_RangeArgument_SpillsElementwise()
    {
        var codePoints = MakeSheet(
            (1, 1, new NumberValue(65)),
            (2, 1, new NumberValue(9731)));
        AssertTextColumn(_eval.Evaluate("=UNICHAR(A1:A2)", codePoints), "A", "\u2603");

        var text = MakeSheet(
            (1, 1, new TextValue("A")),
            (2, 1, new TextValue("\u2603")));
        AssertColumn(_eval.Evaluate("=UNICODE(A1:A2)", text), new NumberValue(65), new NumberValue(9731));

        var numbers = MakeSheet(
            (1, 1, new TextValue("1234.5")),
            (2, 1, new TextValue("x")));
        AssertColumn(_eval.Evaluate("=NUMBERVALUE(A1:A2)", numbers), new NumberValue(1234.5), ErrorValue.Value);
    }

    // ── ASC / DBCS / PHONETIC / BAHTTEXT ─────────────────────────────────────

    [Fact]
    public void Asc_NonDbcsCultureLeavesTextUnchanged()
    {
        using var culture = new CultureScope("en-US");

        _eval.Evaluate("=ASC(\"ＡＢＣ１２３\")", MakeSheet())
            .Should().Be(new TextValue("ＡＢＣ１２３"));
    }

    [Fact]
    public void Dbcs_NonDbcsCultureLeavesTextUnchanged()
    {
        using var culture = new CultureScope("en-US");

        _eval.Evaluate("=DBCS(\"ABC123\")", MakeSheet())
            .Should().Be(new TextValue("ABC123"));
    }

    [Fact]
    public void Asc_DbcsCultureConvertsFullWidthAsciiAndKanaToHalfWidthText()
    {
        using var culture = new CultureScope("ja-JP");

        _eval.Evaluate("=ASC(\"ＡＢＣ１２３！　アイウ\")", MakeSheet())
            .Should().Be(new TextValue("ABC123! ｱｲｳ"));
    }

    [Fact]
    public void Dbcs_DbcsCultureConvertsHalfWidthAsciiAndKanaToFullWidthText()
    {
        using var culture = new CultureScope("ja-JP");

        _eval.Evaluate("=DBCS(\"ABC123! ｱｲｳ\")", MakeSheet())
            .Should().Be(new TextValue("ＡＢＣ１２３！　アイウ"));
    }

    [Fact]
    public void Jis_MatchesDbcsAsExcelCompatibilityName()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=JIS(\"ABC123!\")", sheet)
            .Should().Be(_eval.Evaluate("=DBCS(\"ABC123!\")", sheet));
    }

    [Fact]
    public void AscDbcsAndJis_RangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("ï¼¡ï¼¢ï¼£")),
            (2, 1, new TextValue("ABC")));

        var asc = _eval.Evaluate("=ASC(A1:A2)", sheet);
        var ascRange = asc.Should().BeOfType<RangeValue>().Subject;
        ascRange.RowCount.Should().Be(2);
        ascRange.ColCount.Should().Be(1);
        ascRange.At(1, 1).Should().Be(_eval.Evaluate("=ASC(A1)", sheet));
        ascRange.At(2, 1).Should().Be(_eval.Evaluate("=ASC(A2)", sheet));

        var dbcs = _eval.Evaluate("=DBCS(A1:A2)", sheet);
        var dbcsRange = dbcs.Should().BeOfType<RangeValue>().Subject;
        dbcsRange.RowCount.Should().Be(2);
        dbcsRange.ColCount.Should().Be(1);
        dbcsRange.At(1, 1).Should().Be(_eval.Evaluate("=DBCS(A1)", sheet));
        dbcsRange.At(2, 1).Should().Be(_eval.Evaluate("=DBCS(A2)", sheet));

        var jis = _eval.Evaluate("=JIS(A1:A2)", sheet);
        var jisRange = jis.Should().BeOfType<RangeValue>().Subject;
        jisRange.RowCount.Should().Be(2);
        jisRange.ColCount.Should().Be(1);
        jisRange.At(1, 1).Should().Be(_eval.Evaluate("=DBCS(A1)", sheet));
        jisRange.At(2, 1).Should().Be(_eval.Evaluate("=DBCS(A2)", sheet));
    }

    [Fact]
    public void Phonetic_ReturnsTextOrUpperLeftRangeText()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("東京")),
            (1, 2, new TextValue("大阪")));

        _eval.Evaluate("=PHONETIC(\"東京\")", sheet).Should().Be(new TextValue("東京"));
        _eval.Evaluate("=PHONETIC(A1:B1)", sheet).Should().Be(new TextValue("東京"));
    }

    [Fact]
    public void Bahttext_ConvertsNumbersToThaiBahtText()
    {
        _eval.Evaluate("=BAHTTEXT(1234)", MakeSheet())
            .Should().Be(new TextValue("หนึ่งพันสองร้อยสามสิบสี่บาทถ้วน"));
        _eval.Evaluate("=BAHTTEXT(1234.56)", MakeSheet())
            .Should().Be(new TextValue("หนึ่งพันสองร้อยสามสิบสี่บาทห้าสิบหกสตางค์"));
        _eval.Evaluate("=BAHTTEXT(-21.5)", MakeSheet())
            .Should().Be(new TextValue("ลบยี่สิบเอ็ดบาทห้าสิบสตางค์"));
    }

    [Fact]
    public void Bahttext_RangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1234.56)),
            (2, 1, new NumberValue(-12.3)));

        var result = _eval.Evaluate("=BAHTTEXT(A1:A2)", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(2);
        range.ColCount.Should().Be(1);
        range.At(1, 1).Should().Be(_eval.Evaluate("=BAHTTEXT(A1)", sheet));
        range.At(2, 1).Should().Be(_eval.Evaluate("=BAHTTEXT(A2)", sheet));
    }

    [Fact]
    public void Bahttext_RoundsHalfAwayFromZeroAtSatangBoundary()
    {
        _eval.Evaluate("=BAHTTEXT(1.005)", MakeSheet())
            .Should().Be(new TextValue("หนึ่งบาทหนึ่งสตางค์"));
    }

    [Fact]
    public void Bahttext_OmitsZeroBahtForSatangOnlyAmounts()
    {
        _eval.Evaluate("=BAHTTEXT(0.005)", MakeSheet())
            .Should().Be(new TextValue("หนึ่งสตางค์"));
    }

    [Fact]
    public void Encodeurl_EncodesReservedSpacesAndUnicodeAsUtf8PercentEscapes()
    {
        _eval.Evaluate("=ENCODEURL(\"https://example.com/a b?q=São Paulo&x=1\")", MakeSheet())
            .Should().Be(new TextValue("https%3A%2F%2Fexample.com%2Fa%20b%3Fq%3DS%C3%A3o%20Paulo%26x%3D1"));
    }

    [Fact]
    public void Encodeurl_EmptyText_ReturnsEmptyText()
    {
        _eval.Evaluate("=ENCODEURL(\"\")", MakeSheet())
            .Should().Be(new TextValue(""));
    }

    [Fact]
    public void Filterxml_ReturnsSingleXPathNodeText()
    {
        _eval.Evaluate("=FILTERXML(\"<root><item>A</item><item>B</item></root>\",\"/root/item[2]\")", MakeSheet())
            .Should().Be(new TextValue("B"));
    }

    [Fact]
    public void Filterxml_ReturnsMultipleXPathNodeTextsAsVerticalArray()
    {
        var result = _eval.Evaluate("=FILTERXML(\"<root><item>A</item><item>B</item></root>\",\"/root/item\")", MakeSheet())
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new TextValue("A"));
        result.At(2, 1).Should().Be(new TextValue("B"));
    }

    [Theory]
    [InlineData("=FILTERXML(\"<root>\",\"/root\")")]
    [InlineData("=FILTERXML(\"<root><item>A</item></root>\",\"/root/missing\")")]
    [InlineData("=FILTERXML(\"<root><item>A</item></root>\",\"//*[)\")")]
    public void Filterxml_InvalidXmlXPathOrNoMatch_ReturnsValueError(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Value);
    }

    // ── NUMBERVALUE edge cases ───────────────────────────────────────────────

    [Fact]
    public void Filterxml_RangeXmlArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("<root><item>A</item></root>")),
            (2, 1, new TextValue("<root><item>B</item></root>")));

        AssertTextColumn(_eval.Evaluate("=FILTERXML(A1:A2,\"/root/item\")", sheet), "A", "B");
    }

    [Fact]
    public void Filterxml_RangeXPathArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("/root/item[1]")),
            (2, 1, new TextValue("/root/item[2]")));

        AssertTextColumn(_eval.Evaluate("=FILTERXML(\"<root><item>A</item><item>B</item></root>\",A1:A2)", sheet), "A", "B");
    }

    [Fact]
    public void Filterxml_SameShapeRangeArguments_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("<root><item>A</item></root>")),
            (2, 1, new TextValue("<root><item>B</item></root>")),
            (1, 2, new TextValue("/root/item")),
            (2, 2, new TextValue("/root/item")));

        AssertTextColumn(_eval.Evaluate("=FILTERXML(A1:A2,B1:B2)", sheet), "A", "B");
    }

    [Fact]
    public void Filterxml_MismatchedRangeArgumentShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("<root><item>A</item></root>")),
            (2, 1, new TextValue("<root><item>B</item></root>")),
            (1, 2, new TextValue("/root/item")),
            (1, 3, new TextValue("/root/item")));

        _eval.Evaluate("=FILTERXML(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Numbervalue_DefaultSeparators_ParsesPlainNumber() =>
        _eval.Evaluate("=NUMBERVALUE(\"1234.56\")", MakeSheet())
            .Should().Be(new NumberValue(1234.56));

    [Fact]
    public void Numbervalue_TrailingPercent_DividesBy100() =>
        _eval.Evaluate("=NUMBERVALUE(\"10%\")", MakeSheet())
            .Should().Be(new NumberValue(0.1));

    [Fact]
    public void Numbervalue_AccountingParentheses_ReturnsNegativeNumber() =>
        _eval.Evaluate("=NUMBERVALUE(\"(1)\")", MakeSheet())
            .Should().Be(new NumberValue(-1));

    [Fact]
    public void Numbervalue_AccountingParenthesesWithPercent_ReturnsNegativePercent() =>
        _eval.Evaluate("=NUMBERVALUE(\"(10%)\")", MakeSheet())
            .Should().Be(new NumberValue(-0.1));

    [Fact]
    public void Numbervalue_LocalizedAccountingParentheses_ReturnsNegativeNumber() =>
        _eval.Evaluate("=NUMBERVALUE(\"(1.234,56)\",\",\",\".\")", MakeSheet())
            .Should().Be(new NumberValue(-1234.56));

    [Fact]
    public void Numbervalue_MultiCharacterSeparators_UseFirstCharacterLikeExcel() =>
        _eval.Evaluate("=NUMBERVALUE(\"1.234,56\",\",ignored\",\".ignored\")", MakeSheet())
            .Should().Be(new NumberValue(1234.56));

    [Fact]
    public void Numbervalue_GroupSeparatorAfterDecimal_ReturnsValueError() =>
        _eval.Evaluate("=NUMBERVALUE(\"1.234,56\",\".\",\",\")", MakeSheet())
            .Should().Be(ErrorValue.Value);

    [Theory]
    [InlineData("=NUMBERVALUE(\"1\t234\")")]
    [InlineData("=NUMBERVALUE(\"1\n234\")")]
    [InlineData("=NUMBERVALUE(\"1\r234\")")]
    public void Numbervalue_StripsExcelAsciiSpacingControlsAnywhere(string formula) =>
        _eval.Evaluate(formula, MakeSheet())
            .Should().Be(new NumberValue(1234));

    [Fact]
    public void Numbervalue_DoesNotStripNonBreakingSpace() =>
        _eval.Evaluate("=NUMBERVALUE(\"1\u00A0234\")", MakeSheet())
            .Should().Be(ErrorValue.Value);

    [Fact]
    public void Numbervalue_SameShapeSeparatorArguments_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("1.234,56")),
            (2, 1, new TextValue("1 234.5")),
            (1, 2, new TextValue(",")),
            (2, 2, new TextValue(".")),
            (1, 3, new TextValue(".")),
            (2, 3, new TextValue(" ")));

        AssertColumn(_eval.Evaluate("=NUMBERVALUE(A1:A2,B1:B2,C1:C2)", sheet), new NumberValue(1234.56), new NumberValue(1234.5));
    }

    [Fact]
    public void Numbervalue_OneCellSeparatorRanges_BroadcastAcrossTextArray()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("1,2")),
            (2, 1, new TextValue("3,4")),
            (1, 2, new TextValue(",")),
            (1, 3, new TextValue(".")));

        AssertColumn(_eval.Evaluate("=NUMBERVALUE(A1:A2,B1:B1,C1:C1)", sheet), new NumberValue(1.2), new NumberValue(3.4));
    }

    [Fact]
    public void Numbervalue_MismatchedSeparatorArgument_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("1.234,56")),
            (2, 1, new TextValue("1 234.5")),
            (1, 2, new TextValue(",")),
            (1, 3, new TextValue(".")));

        _eval.Evaluate("=NUMBERVALUE(A1:A2,B1:C1,\".\")", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=NUMBERVALUE(A1:A2,\".\",B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Numbervalue_InvalidSeparators_ReturnsValueError() =>
        _eval.Evaluate("=NUMBERVALUE(\"1.234\",\".\",\".\")", MakeSheet())
            .Should().Be(ErrorValue.Value);

    [Fact]
    public void Numbervalue_ExplicitBlankDecimalSeparator_ReturnsValueError() =>
        _eval.Evaluate("=NUMBERVALUE(\"1234\",)", MakeSheet())
            .Should().Be(ErrorValue.Value);

    [Fact]
    public void Numbervalue_ExplicitBlankGroupSeparator_ReturnsValueError() =>
        _eval.Evaluate("=NUMBERVALUE(\"1234\",\".\",)", MakeSheet())
            .Should().Be(ErrorValue.Value);

    // ── SQRTPI additional ────────────────────────────────────────────────────

    [Fact]
    public void BitFunctions_SameShapeRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)), (2, 1, new NumberValue(6)),
            (1, 2, new NumberValue(3)), (2, 2, new NumberValue(1)));

        AssertColumn(_eval.Evaluate("=BITAND(A1:A2,B1:B2)", sheet), new NumberValue(1), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=BITOR(A1:A2,B1:B2)", sheet), new NumberValue(7), new NumberValue(7));
        AssertColumn(_eval.Evaluate("=BITXOR(A1:A2,B1:B2)", sheet), new NumberValue(6), new NumberValue(7));
        AssertColumn(_eval.Evaluate("=BITLSHIFT(A1:A2,B1:B2)", sheet), new NumberValue(40), new NumberValue(12));
        AssertColumn(_eval.Evaluate("=BITRSHIFT(A1:A2,B1:B2)", sheet), new NumberValue(0), new NumberValue(3));
    }

    [Fact]
    public void BitFunctions_MismatchedRangeArgumentShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)), (2, 1, new NumberValue(6)),
            (1, 2, new NumberValue(3)), (1, 3, new NumberValue(1)));

        _eval.Evaluate("=BITAND(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=BITOR(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=BITXOR(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=BITLSHIFT(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=BITRSHIFT(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void EngineeringBaseConversions_SameShapeNumberAndPlacesRanges_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),      (2, 1, new NumberValue(6)),
            (1, 2, new NumberValue(4)),      (2, 2, new NumberValue(5)),
            (1, 3, new TextValue("101")),    (2, 3, new TextValue("111")),
            (1, 4, new NumberValue(3)),      (2, 4, new NumberValue(4)));

        AssertTextColumn(_eval.Evaluate("=DEC2BIN(A1:A2,B1:B2)", sheet), "0101", "00110");
        AssertTextColumn(_eval.Evaluate("=BIN2HEX(C1:C2,D1:D2)", sheet), "005", "0007");
    }

    [Fact]
    public void EngineeringBaseConversions_MismatchedNumberAndPlacesRanges_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)), (2, 1, new NumberValue(6)),
            (1, 2, new NumberValue(4)), (1, 3, new NumberValue(5)));

        _eval.Evaluate("=DEC2BIN(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=BIN2HEX(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sqrtpi_One_ReturnsSqrtPi() =>
        _eval.Evaluate("=SQRTPI(1)", MakeSheet())
            .Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(Math.Sqrt(Math.PI), 1e-12);

    [Fact]
    public void Sqrtpi_Negative_ReturnsNumError() =>
        _eval.Evaluate("=SQRTPI(-1)", MakeSheet()).Should().Be(ErrorValue.Num);

    // ── MULTINOMIAL ──────────────────────────────────────────────────────────

    [Fact]
    public void Multinomial_TwoArgs_ReturnsExpected() =>
        // (2+3)!/(2!*3!) = 120 / (2*6) = 10
        _eval.Evaluate("=MULTINOMIAL(2,3)", MakeSheet())
            .Should().Be(new NumberValue(10));

    [Fact]
    public void Multinomial_NegativeArg_ReturnsNumError() =>
        _eval.Evaluate("=MULTINOMIAL(2,-1)", MakeSheet()).Should().Be(ErrorValue.Num);

    // ── SERIESSUM ────────────────────────────────────────────────────────────

    [Fact]
    public void SeriesSum_SimplePolynomial_ReturnsExpected()
    {
        // x=2, n=0, m=1, coeffs = {1,2,3} → 1*2^0 + 2*2^1 + 3*2^2 = 1+4+12 = 17
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=SERIESSUM(2,0,1,A1:A3)", sheet)
            .Should().Be(new NumberValue(17));
    }

    // ── Matrix functions ────────────────────────────────────────────────────

    [Fact]
    public void Mmult_2x3_Times_3x2_Returns2x2()
    {
        // A = [[1,2,3],[4,5,6]], B = [[7,8],[9,10],[11,12]]
        // A*B = [[58,64],[139,154]]
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)),
            (4, 1, new NumberValue(7)),  (4, 2, new NumberValue(8)),
            (5, 1, new NumberValue(9)),  (5, 2, new NumberValue(10)),
            (6, 1, new NumberValue(11)), (6, 2, new NumberValue(12)));
        var result = _eval.Evaluate("=MMULT(A1:C2,A4:B6)", sheet);
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.At(1, 1).Should().Be(new NumberValue(58));
        rv.At(1, 2).Should().Be(new NumberValue(64));
        rv.At(2, 1).Should().Be(new NumberValue(139));
        rv.At(2, 2).Should().Be(new NumberValue(154));
    }

    [Fact]
    public void Mmult_IncompatibleDimensions_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(1)), (2, 2, new NumberValue(2)));
        // 2x2 * 1x1 = invalid (k mismatch)
        _eval.Evaluate("=MMULT(A1:B2,A1:A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Mdeterm_2x2_ReturnsMinusTwo()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)));
        var result = _eval.Evaluate("=MDETERM(A1:B2)", sheet);
        result.Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(-2, 1e-12);
    }

    [Fact]
    public void Mdeterm_NonSquare_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)));
        _eval.Evaluate("=MDETERM(A1:C2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Minverse_2x2_ReturnsInverse()
    {
        // A = [[1,2],[3,4]]; A^-1 = [[-2,1],[1.5,-0.5]]
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)));
        var result = _eval.Evaluate("=MINVERSE(A1:B2)", sheet);
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        ((NumberValue)rv.At(1, 1)).Value.Should().BeApproximately(-2, 1e-12);
        ((NumberValue)rv.At(1, 2)).Value.Should().BeApproximately(1, 1e-12);
        ((NumberValue)rv.At(2, 1)).Value.Should().BeApproximately(1.5, 1e-12);
        ((NumberValue)rv.At(2, 2)).Value.Should().BeApproximately(-0.5, 1e-12);
    }

    [Fact]
    public void Minverse_Singular_ReturnsNumError()
    {
        // Singular matrix [[1,2],[2,4]] – det = 0
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(4)));
        _eval.Evaluate("=MINVERSE(A1:B2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Type_Number_Returns1() =>
        _eval.Evaluate("=TYPE(1)", MakeSheet()).Should().Be(new NumberValue(1));

    [Fact]
    public void Type_Text_Returns2() =>
        _eval.Evaluate("=TYPE(\"x\")", MakeSheet()).Should().Be(new NumberValue(2));

    [Fact]
    public void Type_Logical_Returns4() =>
        _eval.Evaluate("=TYPE(TRUE)", MakeSheet()).Should().Be(new NumberValue(4));

    [Fact]
    public void Type_Error_Returns16() =>
        _eval.Evaluate("=TYPE(NA())", MakeSheet()).Should().Be(new NumberValue(16));

    // (TYPE on a range argument is subject to implicit intersection in scalar contexts;
    // tested via TRANSPOSE result indirectly via the dedicated TRANSPOSE tests.)

    // ── ERROR.TYPE ──────────────────────────────────────────────────────────

    [Fact]
    public void ErrorType_DivByZero_Returns2() =>
        _eval.Evaluate("=ERROR.TYPE(1/0)", MakeSheet()).Should().Be(new NumberValue(2));

    [Fact]
    public void ErrorType_Na_Returns7() =>
        _eval.Evaluate("=ERROR.TYPE(NA())", MakeSheet()).Should().Be(new NumberValue(7));

    [Fact]
    public void ErrorType_GettingDataLiteral_Returns8()
    {
        _eval.Evaluate("=ERROR.TYPE(#GETTING_DATA)", MakeSheet()).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void ErrorType_NotAnError_ReturnsNa() =>
        _eval.Evaluate("=ERROR.TYPE(1)", MakeSheet()).Should().Be(ErrorValue.NA);

    // ── DSUM ────────────────────────────────────────────────────────────────

    [Fact]
    public void ErrorType_RangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, ErrorValue.DivByZero),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(1)));

        AssertColumn(
            _eval.Evaluate("=ERROR.TYPE(A1:A3)", sheet),
            new NumberValue(2),
            new NumberValue(7),
            ErrorValue.NA);
    }

    private Sheet MakeDbSheet()
    {
        // Database A1:C5 (1 header row + 4 data rows):
        //   Name   Age  Salary
        //   Alice  30   100
        //   Bob    25   200
        //   Carol  30   300
        //   Dave   40   400
        return MakeSheet(
            (1, 1, new TextValue("Name")), (1, 2, new TextValue("Age")), (1, 3, new TextValue("Salary")),
            (2, 1, new TextValue("Alice")), (2, 2, new NumberValue(30)), (2, 3, new NumberValue(100)),
            (3, 1, new TextValue("Bob")),   (3, 2, new NumberValue(25)), (3, 3, new NumberValue(200)),
            (4, 1, new TextValue("Carol")), (4, 2, new NumberValue(30)), (4, 3, new NumberValue(300)),
            (5, 1, new TextValue("Dave")),  (5, 2, new NumberValue(40)), (5, 3, new NumberValue(400)),
            // Criteria E1:E2 = Age | 30
            (1, 5, new TextValue("Age")),
            (2, 5, new NumberValue(30)));
    }

    [Fact]
    public void DSum_FilterByAge_SumsMatchingSalaries()
    {
        var sheet = MakeDbSheet();
        // Rows where Age=30 (Alice 100, Carol 300) → Sum = 400
        _eval.Evaluate("=DSUM(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(400));
    }

    [Fact]
    public void DAverage_FilterByAge_AveragesMatchingSalaries()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DAVERAGE(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(200));
    }

    [Fact]
    public void DCount_FilterByAge_CountsMatching()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DCOUNT(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void DCountA_FilterByAge_CountsNonBlank()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DCOUNTA(A1:C5,\"Name\",E1:E2)", sheet)
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void DGet_UniqueMatch_ReturnsValue()
    {
        var sheet = MakeDbSheet();
        // Filter Age=25 (Bob) → single match → return Salary 200
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Age"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(25));
        _eval.Evaluate("=DGET(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(200));
    }

    [Fact]
    public void DGet_MultipleMatches_ReturnsNum()
    {
        var sheet = MakeDbSheet();
        // Age=30 matches 2 rows → #NUM!
        _eval.Evaluate("=DGET(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void DMax_FilterByAge_ReturnsMax()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DMAX(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(300));
    }

    [Fact]
    public void DMin_FilterByAge_ReturnsMin()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DMIN(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(100));
    }

    [Fact]
    public void DProduct_FilterByAge_ReturnsProduct()
    {
        var sheet = MakeDbSheet();
        _eval.Evaluate("=DPRODUCT(A1:C5,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(30000));
    }
}
