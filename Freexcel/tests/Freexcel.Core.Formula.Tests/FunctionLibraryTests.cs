using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

/// <summary>
/// Tests for the expanded Phase 4.2 function library.
/// Covers IFERROR, IFNA, VLOOKUP, HLOOKUP, INDEX, MATCH,
/// SUMIF, COUNTIF, AVERAGEIF, TEXT, TRIM, UPPER, LOWER, PROPER,
/// SUBSTITUTE, FIND, SEARCH, MID, REPT, VALUE,
/// DATE, YEAR, MONTH, DAY, HOUR, MINUTE, SECOND, WEEKDAY, EDATE, DATEDIF,
/// MOD, POWER, SQRT, INT, CEILING, FLOOR, RANDBETWEEN, SIGN, LOG, LN, EXP, PI, FACT,
/// LARGE, SMALL, RANK, STDEV, MEDIAN.
/// </summary>
public class FunctionLibraryTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

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

    // ── VLOOKUP ──────────────────────────────────────────────────────────────

    [Fact]
    public void Vlookup_ExactMatch_ReturnsValue()
    {
        // A1:B3 = {10,"apple"; 20,"banana"; 30,"cherry"}
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("apple")),
            (2, 1, new NumberValue(20)), (2, 2, new TextValue("banana")),
            (3, 1, new NumberValue(30)), (3, 2, new TextValue("cherry")));
        _eval.Evaluate("=VLOOKUP(20,A1:B3,2,FALSE)", sheet).Should().Be(new TextValue("banana"));
    }

    [Fact]
    public void Vlookup_NotFound_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("apple")));
        var result = _eval.Evaluate("=VLOOKUP(99,A1:B1,2,FALSE)", sheet);
        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Vlookup_ApproximateMatch_ReturnsBestFit()
    {
        // Sorted: 1,10,100
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),   (1, 2, new TextValue("one")),
            (2, 1, new NumberValue(10)),  (2, 2, new TextValue("ten")),
            (3, 1, new NumberValue(100)), (3, 2, new TextValue("hundred")));
        // lookup 15 in approximate mode → row with 10
        _eval.Evaluate("=VLOOKUP(15,A1:B3,2,TRUE)", sheet).Should().Be(new TextValue("ten"));
    }

    [Fact]
    public void Vlookup_TextKey_ExactMatch()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")), (1, 2, new NumberValue(1)),
            (2, 1, new TextValue("b")), (2, 2, new NumberValue(2)),
            (3, 1, new TextValue("c")), (3, 2, new NumberValue(3)));
        _eval.Evaluate("=VLOOKUP(\"b\",A1:B3,2,FALSE)", sheet).Should().Be(new NumberValue(2));
    }

    // ── HLOOKUP ──────────────────────────────────────────────────────────────

    [Fact]
    public void Hlookup_ExactMatch_ReturnsValue()
    {
        // Row1: 10 20 30;  Row2: "a" "b" "c"
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(20)), (1, 3, new NumberValue(30)),
            (2, 1, new TextValue("a")),  (2, 2, new TextValue("b")),  (2, 3, new TextValue("c")));
        _eval.Evaluate("=HLOOKUP(20,A1:C2,2,FALSE)", sheet).Should().Be(new TextValue("b"));
    }

    [Fact]
    public void Hlookup_NotFound_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(20)));
        _eval.Evaluate("=HLOOKUP(99,A1:B2,2,FALSE)", sheet).Should().Be(ErrorValue.NA);
    }

    // ── INDEX ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Index_ReturnsCorrectCell()
    {
        // A1:C2
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)));
        _eval.Evaluate("=INDEX(A1:C2,2,3)", sheet).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Index_OutOfRange_ReturnsRef()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)));
        var result = _eval.Evaluate("=INDEX(A1:B1,1,5)", sheet);
        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Index_SingleColumn_DefaultCol()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        _eval.Evaluate("=INDEX(A1:A3,2)", sheet).Should().Be(new NumberValue(20));
    }

    // ── MATCH ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Match_ExactMatch_ReturnsPosition()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        _eval.Evaluate("=MATCH(20,A1:A3,0)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Match_NotFound_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)));
        _eval.Evaluate("=MATCH(99,A1:A2,0)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Match_ApproximateAscending_ReturnsBestFit()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(5)),
            (3, 1, new NumberValue(10)));
        // lookup 7 with match_type=1 → position 2 (5 is largest <= 7)
        _eval.Evaluate("=MATCH(7,A1:A3,1)", sheet).Should().Be(new NumberValue(2));
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
    public void Sumif_TextCriteria()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")), (1, 2, new NumberValue(10)),
            (2, 1, new TextValue("b")), (2, 2, new NumberValue(20)),
            (3, 1, new TextValue("a")), (3, 2, new NumberValue(30)));
        _eval.Evaluate("=SUMIF(A1:A3,\"a\",B1:B3)", sheet).Should().Be(new NumberValue(40));
    }

    // ── COUNTIF ───────────────────────────────────────────────────────────────

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
    public void Averageif_NoMatch_ReturnsDivZero()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)));
        var result = _eval.Evaluate("=AVERAGEIF(A1:A2,99)", sheet);
        result.Should().Be(ErrorValue.DivByZero);
    }

    // ── TEXT ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Text_FormatsNumber()
    {
        var sheet = MakeSheet();
        // "0.00" format
        var result = _eval.Evaluate("=TEXT(3.14159,\"0.00\")", sheet);
        result.Should().BeOfType<TextValue>();
        ((TextValue)result).Value.Should().Contain("3.14");
    }

    // ── TRIM ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Trim_RemovesLeadingTrailing()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=TRIM(\"  hello  \")", sheet).Should().Be(new TextValue("hello"));
    }

    [Fact]
    public void Trim_CollapsesInteriorSpaces()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=TRIM(\"hello   world\")", sheet).Should().Be(new TextValue("hello world"));
    }

    // ── UPPER / LOWER / PROPER ─────────────────────────────────────────────────

    [Fact]
    public void Upper_ConvertsToUppercase()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=UPPER(\"hello\")", sheet).Should().Be(new TextValue("HELLO"));
    }

    [Fact]
    public void Lower_ConvertsToLowercase()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=LOWER(\"HELLO\")", sheet).Should().Be(new TextValue("hello"));
    }

    [Fact]
    public void Proper_TitleCasesWords()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=PROPER(\"hello world\")", sheet).Should().Be(new TextValue("Hello World"));
    }

    // ── SUBSTITUTE ─────────────────────────────────────────────────────────────

    [Fact]
    public void Substitute_ReplacesAll()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SUBSTITUTE(\"aababc\",\"ab\",\"X\")", sheet).Should().Be(new TextValue("aXXc"));
    }

    [Fact]
    public void Substitute_ReplacesSpecificInstance()
    {
        var sheet = MakeSheet();
        // "aababc" has "ab" at index 1 and index 3; replacing the 2nd gives "aabXc"
        _eval.Evaluate("=SUBSTITUTE(\"aababc\",\"ab\",\"X\",2)", sheet).Should().Be(new TextValue("aabXc"));
    }

    // ── FIND ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Find_CaseSensitive_ReturnsPosition()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FIND(\"lo\",\"hello\")", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Find_NotFound_ReturnsValueError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FIND(\"xyz\",\"hello\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Find_CaseSensitive_WontMatchWrongCase()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FIND(\"LO\",\"hello\")", sheet).Should().Be(ErrorValue.Value);
    }

    // ── SEARCH ────────────────────────────────────────────────────────────────

    [Fact]
    public void Search_CaseInsensitive_ReturnsPosition()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SEARCH(\"LO\",\"hello\")", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Search_WithWildcard_Matches()
    {
        var sheet = MakeSheet();
        // "h*o" matches "hello"
        _eval.Evaluate("=SEARCH(\"h?llo\",\"hello\")", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Search_NotFound_ReturnsValueError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SEARCH(\"xyz\",\"hello\")", sheet).Should().Be(ErrorValue.Value);
    }

    // ── MID ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Mid_ExtractsSubstring()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=MID(\"hello world\",7,5)", sheet).Should().Be(new TextValue("world"));
    }

    [Fact]
    public void Mid_BeyondEnd_ClipsToEnd()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=MID(\"hello\",3,100)", sheet).Should().Be(new TextValue("llo"));
    }

    // ── REPT ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Rept_RepeatsTimes()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=REPT(\"ab\",3)", sheet).Should().Be(new TextValue("ababab"));
    }

    [Fact]
    public void Rept_ZeroTimes_ReturnsEmpty()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=REPT(\"x\",0)", sheet).Should().Be(new TextValue(""));
    }

    // ── VALUE ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Value_ParsesNumber()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=VALUE(\"42.5\")", sheet).Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void Value_InvalidText_ReturnsValueError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=VALUE(\"abc\")", sheet).Should().Be(ErrorValue.Value);
    }

    // ── DATE ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Date_ConstructsSerial()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=DATE(2024,1,15)", sheet);
        result.Should().BeOfType<NumberValue>();
        var dt = DateTime.FromOADate(((NumberValue)result).Value);
        dt.Year.Should().Be(2024);
        dt.Month.Should().Be(1);
        dt.Day.Should().Be(15);
    }

    // ── YEAR / MONTH / DAY ────────────────────────────────────────────────────

    [Fact]
    public void Year_ExtractsYear()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 6, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=YEAR(A1)", sheet).Should().Be(new NumberValue(2024));
    }

    [Fact]
    public void Month_ExtractsMonth()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 6, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=MONTH(A1)", sheet).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Day_ExtractsDay()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 6, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=DAY(A1)", sheet).Should().Be(new NumberValue(15));
    }

    // ── HOUR / MINUTE / SECOND ────────────────────────────────────────────────

    [Fact]
    public void Hour_ExtractsHour()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 1, 14, 30, 45).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=HOUR(A1)", sheet).Should().Be(new NumberValue(14));
    }

    [Fact]
    public void Minute_ExtractsMinute()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 1, 14, 30, 45).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=MINUTE(A1)", sheet).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Second_ExtractsSecond()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 1, 14, 30, 45).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=SECOND(A1)", sheet).Should().Be(new NumberValue(45));
    }

    // ── WEEKDAY ────────────────────────────────────────────────────────────────

    [Fact]
    public void Weekday_ReturnType1_SundayIs1()
    {
        // 2024-01-07 is a Sunday
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 7).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=WEEKDAY(A1,1)", sheet).Should().Be(new NumberValue(1)); // Sunday=1
    }

    [Fact]
    public void Weekday_ReturnType2_MondayIs1()
    {
        // 2024-01-08 is a Monday
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 8).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        _eval.Evaluate("=WEEKDAY(A1,2)", sheet).Should().Be(new NumberValue(1)); // Monday=1
    }

    // ── EDATE ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Edate_AddMonths()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 1, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        var result = _eval.Evaluate("=EDATE(A1,3)", sheet);
        result.Should().BeOfType<NumberValue>();
        var dt = DateTime.FromOADate(((NumberValue)result).Value);
        dt.Month.Should().Be(4);
        dt.Day.Should().Be(15);
    }

    [Fact]
    public void Edate_SubtractMonths()
    {
        var sheet = MakeSheet();
        var serial = new DateTime(2024, 6, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
        var result = _eval.Evaluate("=EDATE(A1,-2)", sheet);
        result.Should().BeOfType<NumberValue>();
        var dt = DateTime.FromOADate(((NumberValue)result).Value);
        dt.Month.Should().Be(4);
    }

    // ── DATEDIF ────────────────────────────────────────────────────────────────

    [Fact]
    public void Datedif_Days()
    {
        var sheet = MakeSheet();
        var s1 = new DateTime(2024, 1, 1).ToOADate();
        var s2 = new DateTime(2024, 1, 11).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(s1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(s2));
        _eval.Evaluate("=DATEDIF(A1,B1,\"D\")", sheet).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Datedif_Years()
    {
        var sheet = MakeSheet();
        var s1 = new DateTime(2020, 3, 15).ToOADate();
        var s2 = new DateTime(2024, 3, 15).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(s1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(s2));
        _eval.Evaluate("=DATEDIF(A1,B1,\"Y\")", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Datedif_Months()
    {
        var sheet = MakeSheet();
        var s1 = new DateTime(2024, 1, 1).ToOADate();
        var s2 = new DateTime(2024, 4, 1).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(s1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(s2));
        _eval.Evaluate("=DATEDIF(A1,B1,\"M\")", sheet).Should().Be(new NumberValue(3));
    }

    // ── MOD ──────────────────────────────────────────────────────────────────

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
    public void Power_SquaresNumber()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=POWER(3,2)", sheet).Should().Be(new NumberValue(9));
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

    // ── INT ───────────────────────────────────────────────────────────────────

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

    // ── CEILING ───────────────────────────────────────────────────────────────

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

    // ── FLOOR ─────────────────────────────────────────────────────────────────

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

    // ── RANDBETWEEN ───────────────────────────────────────────────────────────

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
    public void Large_OutOfRange_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)));
        _eval.Evaluate("=LARGE(A1:A1,5)", sheet).Should().Be(ErrorValue.Num);
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
    public void Small_OutOfRange_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)));
        _eval.Evaluate("=SMALL(A1:A1,5)", sheet).Should().Be(ErrorValue.Num);
    }

    // ── RANK ──────────────────────────────────────────────────────────────────

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

    // ── Bug regression: SUMIFS must receive RangeValues ────────────────────────

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
    public void Xlookup_RangeArg_WorksCorrectly()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")), (3, 1, new TextValue("C")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)), (3, 2, new NumberValue(3)));
        // XLOOKUP("B", A1:A3, B1:B3) → 2
        var result = _eval.Evaluate("=XLOOKUP(\"B\",A1:A3,B1:B3)", sheet);
        result.Should().Be(new NumberValue(2));
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
    public void Averageifs_RangeArg_WorksCorrectly()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (2, 1, new NumberValue(20)), (3, 1, new NumberValue(30)),
            (1, 2, new TextValue("A")),  (2, 2, new TextValue("B")),  (3, 2, new TextValue("A")));
        // AVERAGEIFS(A1:A3, B1:B3, "A") → 20  (average of 10 and 30)
        var result = _eval.Evaluate("=AVERAGEIFS(A1:A3,B1:B3,\"A\")", sheet);
        result.Should().Be(new NumberValue(20));
    }

    // ── Math / Trig ─────────────────────────────────────────────────────────────

    [Fact] public void Sin_Zero_ReturnsZero() =>
        _eval.Evaluate("=SIN(0)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact] public void Cos_Zero_ReturnsOne() =>
        _eval.Evaluate("=COS(0)", MakeSheet()).Should().Be(new NumberValue(1));

    [Fact] public void Tan_Zero_ReturnsZero() =>
        _eval.Evaluate("=TAN(0)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact] public void Asin_One_ReturnsHalfPi() =>
        ((NumberValue)_eval.Evaluate("=ASIN(1)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI / 2, 1e-10);

    [Fact] public void Acos_One_ReturnsZero() =>
        ((NumberValue)_eval.Evaluate("=ACOS(1)", MakeSheet())).Value
            .Should().BeApproximately(0, 1e-10);

    [Fact] public void Atan_One_ReturnsQuarterPi() =>
        ((NumberValue)_eval.Evaluate("=ATAN(1)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI / 4, 1e-10);

    [Fact] public void Atan2_XY_ReturnsCorrect() =>
        ((NumberValue)_eval.Evaluate("=ATAN2(1,1)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI / 4, 1e-10);

    [Fact] public void Degrees_Pi_Returns180() =>
        ((NumberValue)_eval.Evaluate("=DEGREES(PI())", MakeSheet())).Value
            .Should().BeApproximately(180, 1e-10);

    [Fact] public void Radians_180_ReturnsPi() =>
        ((NumberValue)_eval.Evaluate("=RADIANS(180)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI, 1e-10);

    [Fact] public void Product_Range_MultipliesAll()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(3)),(3,1,new NumberValue(4)));
        _eval.Evaluate("=PRODUCT(A1:A3)", sheet).Should().Be(new NumberValue(24));
    }

    [Fact] public void Quotient_5_2_Returns2() =>
        _eval.Evaluate("=QUOTIENT(5,2)", MakeSheet()).Should().Be(new NumberValue(2));

    [Fact] public void Gcd_12_8_Returns4() =>
        _eval.Evaluate("=GCD(12,8)", MakeSheet()).Should().Be(new NumberValue(4));

    [Fact] public void Lcm_4_6_Returns12() =>
        _eval.Evaluate("=LCM(4,6)", MakeSheet()).Should().Be(new NumberValue(12));

    [Fact] public void Mround_14_5_Returns15() =>
        _eval.Evaluate("=MROUND(14,5)", MakeSheet()).Should().Be(new NumberValue(15));

    [Fact] public void Combin_5_2_Returns10() =>
        _eval.Evaluate("=COMBIN(5,2)", MakeSheet()).Should().Be(new NumberValue(10));

    [Fact] public void Permut_5_2_Returns20() =>
        _eval.Evaluate("=PERMUT(5,2)", MakeSheet()).Should().Be(new NumberValue(20));

    [Fact] public void Odd_2_Returns3() =>
        _eval.Evaluate("=ODD(2)", MakeSheet()).Should().Be(new NumberValue(3));

    [Fact] public void Even_3_Returns4() =>
        _eval.Evaluate("=EVEN(3)", MakeSheet()).Should().Be(new NumberValue(4));

    // ── Date / Time ──────────────────────────────────────────────────────────────

    [Fact] public void Time_HMS_ReturnsFraction()
    {
        // TIME(12, 0, 0) = 0.5 (half a day)
        ((NumberValue)_eval.Evaluate("=TIME(12,0,0)", MakeSheet())).Value
            .Should().BeApproximately(0.5, 1e-10);
    }

    [Fact] public void Timevalue_String_ReturnsFraction()
    {
        ((NumberValue)_eval.Evaluate("=TIMEVALUE(\"12:00:00\")", MakeSheet())).Value
            .Should().BeApproximately(0.5, 1e-10);
    }

    [Fact] public void Datevalue_String_ReturnsSerial()
    {
        // 2024-01-01 OADate
        double expected = new DateTime(2024, 1, 1).ToOADate();
        ((NumberValue)_eval.Evaluate("=DATEVALUE(\"2024-01-01\")", MakeSheet())).Value
            .Should().BeApproximately(expected, 1);
    }

    [Fact] public void Eomonth_Jan_ReturnsLastDayJan()
    {
        // DATE(2024,1,15) + EOMONTH offset 0 → 2024-01-31
        double jan15 = new DateTime(2024, 1, 15).ToOADate();
        double jan31 = new DateTime(2024, 1, 31).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan15)));
        ((NumberValue)_eval.Evaluate("=EOMONTH(A1,0)", sheet)).Value
            .Should().BeApproximately(jan31, 1);
    }

    [Fact] public void Weeknum_Jan8_Returns2()
    {
        double jan8 = new DateTime(2024, 1, 8).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan8)));
        _eval.Evaluate("=WEEKNUM(A1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact] public void Isoweeknum_Jan8_2024_Returns2()
    {
        double jan8 = new DateTime(2024, 1, 8).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan8)));
        _eval.Evaluate("=ISOWEEKNUM(A1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact] public void Workday_5BusinessDays_SkipsWeekend()
    {
        // 2024-01-08 (Monday) + 5 workdays = 2024-01-15 (Monday)
        double mon = new DateTime(2024, 1, 8).ToOADate();
        double expected = new DateTime(2024, 1, 15).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(mon)));
        ((NumberValue)_eval.Evaluate("=WORKDAY(A1,5)", sheet)).Value
            .Should().BeApproximately(expected, 1);
    }

    [Fact] public void Networkdays_MonToFri_Returns5()
    {
        double mon = new DateTime(2024, 1, 8).ToOADate();
        double fri = new DateTime(2024, 1, 12).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(mon)), (1, 2, new NumberValue(fri)));
        _eval.Evaluate("=NETWORKDAYS(A1,B1)", sheet).Should().Be(new NumberValue(5));
    }

    [Fact] public void Days_EndMinusStart_ReturnsDifference()
    {
        double d1 = new DateTime(2024, 1, 1).ToOADate();
        double d2 = new DateTime(2024, 1, 11).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(d2)), (1, 2, new NumberValue(d1)));
        _eval.Evaluate("=DAYS(A1,B1)", sheet).Should().Be(new NumberValue(10));
    }

    [Fact] public void Yearfrac_HalfYear_ReturnsApprox05()
    {
        double jan1 = new DateTime(2024, 1, 1).ToOADate();
        double jul1 = new DateTime(2024, 7, 1).ToOADate();
        var sheet = MakeSheet((1, 1, new NumberValue(jan1)), (1, 2, new NumberValue(jul1)));
        ((NumberValue)_eval.Evaluate("=YEARFRAC(A1,B1,3)", sheet)).Value
            .Should().BeApproximately(182.0 / 365.0, 0.01);
    }

    // ── Statistical ──────────────────────────────────────────────────────────────

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

    [Fact] public void StdevP_ThreeValues_ReturnsStdDev()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        ((NumberValue)_eval.Evaluate("=STDEV.P(A1:A3)", sheet)).Value
            .Should().BeApproximately(Math.Sqrt(8.0 / 3.0), 1e-10);
    }

    [Fact] public void Percentile_Median_Returns4()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        _eval.Evaluate("=PERCENTILE(A1:A3,0.5)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact] public void PercentileExc_Middle_ReturnsInterpolated()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)));
        // PERCENTILE.EXC([1,2,3,4], 0.4): rank = 0.4*5-1 = 1, index 1 → value 2
        _eval.Evaluate("=PERCENTILE.EXC(A1:A4,0.4)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact] public void Quartile_Q1_Returns25th()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)));
        // QUARTILE([1,2,3,4], 1) = 25th percentile = 1.75
        ((NumberValue)_eval.Evaluate("=QUARTILE(A1:A4,1)", sheet)).Value
            .Should().BeApproximately(1.75, 1e-10);
    }

    [Fact] public void Geomean_TwoNumbers_ReturnsGeometricMean()
    {
        var sheet = MakeSheet((1,1,new NumberValue(4)),(2,1,new NumberValue(9)));
        // geomean(4,9) = sqrt(36) = 6
        _eval.Evaluate("=GEOMEAN(A1:A2)", sheet).Should().Be(new NumberValue(6));
    }

    [Fact] public void Harmean_TwoNumbers_ReturnsHarmonicMean()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(4)));
        // harmean(1,4) = 2/(1+0.25) = 1.6
        ((NumberValue)_eval.Evaluate("=HARMEAN(A1:A2)", sheet)).Value
            .Should().BeApproximately(1.6, 1e-10);
    }

    [Fact] public void Avedev_ThreeValues_ReturnsAvgAbsDev()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(4)),(3,1,new NumberValue(6)));
        // mean=4, deviations=2,0,2 → avg=4/3
        ((NumberValue)_eval.Evaluate("=AVEDEV(A1:A3)", sheet)).Value
            .Should().BeApproximately(4.0 / 3.0, 1e-10);
    }

    [Fact] public void Mode_ReturnsValueWithHighestFrequency()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(2)),(4,1,new NumberValue(3)));
        _eval.Evaluate("=MODE(A1:A4)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact] public void Percentrank_FindsRank()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),(4,1,new NumberValue(4)),(5,1,new NumberValue(5)));
        // PERCENTRANK([1..5], 3) = 0.5
        _eval.Evaluate("=PERCENTRANK(A1:A5,3)", sheet).Should().Be(new NumberValue(0.5));
    }

    [Fact] public void Correl_PerfectPositive_Returns1()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),
            (1,2,new NumberValue(2)),(2,2,new NumberValue(4)),(3,2,new NumberValue(6)));
        ((NumberValue)_eval.Evaluate("=CORREL(A1:A3,B1:B3)", sheet)).Value
            .Should().BeApproximately(1.0, 1e-10);
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
}
