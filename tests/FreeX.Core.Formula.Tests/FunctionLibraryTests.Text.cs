using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    // TEXT / LEN / LEFT / RIGHT

    [Fact]
    public void Text_FormatsNumber()
    {
        var sheet = MakeSheet();
        // "0.00" format
        var result = _eval.Evaluate("=TEXT(3.14159,\"0.00\")", sheet);
        result.Should().BeOfType<TextValue>();
        ((TextValue)result).Value.Should().Contain("3.14");
    }

    [Fact]
    public void Text_DirectTodayResult_FormatsDateSerial()
    {
        var expected = DateTime.Today.ToOADate().ToString("0", System.Globalization.CultureInfo.InvariantCulture);

        _eval.Evaluate("=TEXT(TODAY(),\"0\")", MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void Text_FormatsDateAndTimeSerialsWithExcelMasks()
    {
        _eval.Evaluate("=TEXT(DATE(2024,1,15),\"yyyy-mm-dd\")", MakeSheet()).Should().Be(new TextValue("2024-01-15"));
        _eval.Evaluate("=TEXT(DATE(2024,1,15),\"mmm d, yyyy\")", MakeSheet()).Should().Be(new TextValue("Jan 15, 2024"));
        _eval.Evaluate("=TEXT(TIME(13,5,7),\"h:mm AM/PM\")", MakeSheet()).Should().Be(new TextValue("1:05 PM"));
    }

    [Fact]
    public void Text_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('0', 32768))));

        _eval.Evaluate("=TEXT(1,A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Len_DirectTodayResult_UsesDateSerialText()
    {
        var expected = DateTime.Today.ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

        _eval.Evaluate("=LEN(TODAY())", MakeSheet()).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Len_RangeArgument_SpillsElementwiseLengths()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")));

        var result = _eval.Evaluate("=LEN(A1:A2)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.Cells[0, 0].Should().Be(new NumberValue(5));
        result.Cells[1, 0].Should().Be(new NumberValue(6));
    }

    [Fact]
    public void LenLeftAndRight_CountSurrogatePairsAsUtf16CodeUnits()
    {
        var sheet = MakeSheet();

        // Excel counts UTF-16 code units: 😀 is a surrogate pair (2 code units).
        _eval.Evaluate("=LEN(\"😀x\")", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=LEFT(\"😀x\",2)", sheet).Should().Be(new TextValue("😀"));
        _eval.Evaluate("=RIGHT(\"x😀\",2)", sheet).Should().Be(new TextValue("😀"));
    }

    [Fact]
    public void Len_SurrogatePairCountsAsTwoCodeUnits()
    {
        var sheet = MakeSheet();

        // Q12 regression: LEN must return UTF-16 code-unit count, not grapheme count.
        _eval.Evaluate("=LEN(\"😀\")", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=LEN(\"A😀B\")", sheet).Should().Be(new NumberValue(4));
        // BMP-only text is unchanged.
        _eval.Evaluate("=LEN(\"abc\")", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=LEN(\"\")", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void LeftAndRight_OmittedNumChars_DefaultsToOne()
    {
        var sheet = MakeSheet();

        // R102: a genuinely-omitted num_chars (no 2nd argument at all) defaults to 1 -- this used
        // to (incorrectly) use a trailing comma here, which is a PRESENT-but-blank argument, not
        // the same thing; Excel coerces that to numeric 0 instead (see
        // R102_TrailingCommaEmptyOrdinalArgumentTests for the corrected trailing-comma-empty case).
        _eval.Evaluate("=LEFT(\"abc\")", sheet).Should().Be(new TextValue("a"));
        _eval.Evaluate("=RIGHT(\"abc\")", sheet).Should().Be(new TextValue("c"));
    }

    [Fact]
    public void LeftAndRight_RangeTextArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")));

        var left = _eval.Evaluate("=LEFT(A1:A2,2)", sheet).Should().BeOfType<RangeValue>().Subject;
        left.Cells[0, 0].Should().Be(new TextValue("Ap"));
        left.Cells[1, 0].Should().Be(new TextValue("Ba"));

        var right = _eval.Evaluate("=RIGHT(A1:A2,3)", sheet).Should().BeOfType<RangeValue>().Subject;
        right.Cells[0, 0].Should().Be(new TextValue("ple"));
        right.Cells[1, 0].Should().Be(new TextValue("ana"));
    }

    [Fact]
    public void LeftAndRight_SameShapeNumCharsArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")),
            (1, 2, new NumberValue(2)),
            (2, 2, new NumberValue(4)));

        AssertTextColumn(_eval.Evaluate("=LEFT(A1:A2,B1:B2)", sheet), "Ap", "Bana");
        AssertTextColumn(_eval.Evaluate("=RIGHT(A1:A2,B1:B2)", sheet), "le", "nana");
    }

    [Fact]
    public void LeftAndRight_RowVectorAndColumnVectorNumCharsArgument_SpillToCrossBroadcastMatrix()
    {
        // Regression guard for R62-formula-array-broadcast-6-1: a 2x1 column vector crossed with
        // a 1x2 row vector must 2-D cross-broadcast into a 2x2 spilled result, not #VALUE! --
        // this test previously asserted the old (superseded) #VALUE! behavior.
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")),
            (1, 2, new NumberValue(2)),
            (1, 3, new NumberValue(4)));

        var left = _eval.Evaluate("=LEFT(A1:A2,B1:C1)", sheet).Should().BeOfType<RangeValue>().Subject;
        left.RowCount.Should().Be(2);
        left.ColCount.Should().Be(2);
        ((TextValue)left.At(1, 1)).Value.Should().Be("Ap");
        ((TextValue)left.At(1, 2)).Value.Should().Be("Appl");
        ((TextValue)left.At(2, 1)).Value.Should().Be("Ba");
        ((TextValue)left.At(2, 2)).Value.Should().Be("Bana");

        var right = _eval.Evaluate("=RIGHT(A1:A2,B1:C1)", sheet).Should().BeOfType<RangeValue>().Subject;
        right.RowCount.Should().Be(2);
        right.ColCount.Should().Be(2);
        ((TextValue)right.At(1, 1)).Value.Should().Be("le");
        ((TextValue)right.At(1, 2)).Value.Should().Be("pple");
        ((TextValue)right.At(2, 1)).Value.Should().Be("na");
        ((TextValue)right.At(2, 2)).Value.Should().Be("nana");
    }

    [Fact]
    public void LeftAndRight_TrulyMismatchedNumCharsArgument_ReturnsValueError()
    {
        // Sibling no-regression: ranges that conflict on the SAME axis (neither equal nor size-1)
        // must still be a genuine #VALUE! shape mismatch.
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")),
            (1, 2, new NumberValue(2)),
            (2, 2, new NumberValue(4)),
            (3, 2, new NumberValue(1)));

        _eval.Evaluate("=LEFT(A1:A2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=RIGHT(A1:A2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Left_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('x', 32768))));

        _eval.Evaluate("=LEFT(A1,32768)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Right_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('x', 32768))));

        _eval.Evaluate("=RIGHT(A1,32768)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Left_NonFiniteNumChars_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("abcdef")), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=LEFT(A1,B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Right_NonFiniteNumChars_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("abcdef")), (1, 2, new TextValue("1E309")));
        _eval.Evaluate("=RIGHT(A1,B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=LEFT(\"abc\",-0.5)")]
    [InlineData("=RIGHT(\"abc\",-0.5)")]
    [InlineData("=LEFTB(\"A\u754cB\",-0.5)")]
    [InlineData("=RIGHTB(\"A\u754cB\",-0.5)")]
    public void LeftAndRight_NegativeFractionalNumChars_ReturnValueError(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void LeftAndRight_SplitSurrogatePairAtCodeUnitBoundary()
    {
        // Excel slices on UTF-16 code-unit boundaries, splitting surrogate pairs.
        // LEFT("😀x",1) returns just the high surrogate; RIGHT("x😀",1) just the low surrogate.
        _eval.Evaluate("=LEFT(\"😀x\",1)", MakeSheet()).Should().Be(new TextValue("\uD83D"));
        _eval.Evaluate("=RIGHT(\"x😀\",1)", MakeSheet()).Should().Be(new TextValue("\uDE00"));
    }

    [Fact]
    public void Left_ResultAtExcelCellLimit_ReturnsText()
    {
        var text = new string('x', 32767);
        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=LEFT(A1,32767)", sheet).Should().Be(new TextValue(text));
    }

    [Fact]
    public void Text_FormatTextError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=TEXT(1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void TextAndValue_RangeArgument_SpillsElementwise()
    {
        var textSheet = MakeSheet(
            (1, 1, new TextValue(" apple ")),
            (2, 1, new TextValue("BANANA")));
        AssertTextColumn(_eval.Evaluate("=TEXT(A1:A2,\"@\")", textSheet), " apple ", "BANANA");

        var valueSheet = MakeSheet(
            (1, 1, new TextValue("10")),
            (2, 1, new TextValue("x")));
        AssertColumn(_eval.Evaluate("=VALUE(A1:A2)", valueSheet), new NumberValue(10), ErrorValue.Value);
    }

    [Fact]
    public void Text_SameShapeFormatArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3.14159)),
            (2, 1, new NumberValue(42)),
            (1, 2, new TextValue("0.00")),
            (2, 2, new TextValue("000")));

        AssertTextColumn(_eval.Evaluate("=TEXT(A1:A2,B1:B2)", sheet), "3.14", "042");
    }

    [Fact]
    public void Text_RowVectorAndColumnVectorFormatArgument_SpillsToCrossBroadcastMatrix()
    {
        // Regression guard for R62-formula-array-broadcast-6-1: a 2x1 column vector crossed with
        // a 1x2 row vector must 2-D cross-broadcast into a 2x2 spilled result, not #VALUE! --
        // this test previously asserted the old (superseded) #VALUE! behavior.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3.14159)),
            (2, 1, new NumberValue(42)),
            (1, 2, new TextValue("0.00")),
            (1, 3, new TextValue("000")));

        var result = _eval.Evaluate("=TEXT(A1:A2,B1:C1)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        ((TextValue)result.At(1, 1)).Value.Should().Be("3.14");
        ((TextValue)result.At(1, 2)).Value.Should().Be("003");
        ((TextValue)result.At(2, 1)).Value.Should().Be("42.00");
        ((TextValue)result.At(2, 2)).Value.Should().Be("042");
    }

    [Fact]
    public void Text_TrulyMismatchedFormatArgument_ReturnsValueError()
    {
        // Sibling no-regression: ranges that conflict on the SAME axis (neither equal nor size-1)
        // must still be a genuine #VALUE! shape mismatch.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3.14159)),
            (2, 1, new NumberValue(42)),
            (1, 2, new TextValue("0.00")),
            (2, 2, new TextValue("000")),
            (3, 2, new TextValue("0")));

        _eval.Evaluate("=TEXT(A1:A2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
    }

    // TRIM / CASE / SUBSTITUTE / SEARCH / VALUE

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

    [Fact]
    public void Trim_OnlyRemovesAsciiSpacesLikeExcel()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("\u00A0  hello  \u00A0")),
            (2, 1, new TextValue("\t  hello  \t")));

        _eval.Evaluate("=TRIM(A1)", sheet).Should().Be(new TextValue("\u00A0 hello \u00A0"));
        _eval.Evaluate("=TRIM(A2)", sheet).Should().Be(new TextValue("\t hello \t"));
    }

    [Fact]
    public void Trim_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('x', 32768))));

        _eval.Evaluate("=TRIM(A1)", sheet).Should().Be(ErrorValue.Value);
    }

    // ── UPPER / LOWER / PROPER ─────────────────────────────────────────────────

    [Fact]
    public void Upper_ConvertsToUppercase()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=UPPER(\"hello\")", sheet).Should().Be(new TextValue("HELLO"));
    }

    [Fact]
    public void Upper_ResultAtExcelCellLimit_ReturnsText()
    {
        var text = new string('x', 32767);
        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=UPPER(A1)", sheet).Should().Be(new TextValue(new string('X', 32767)));
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

    [Theory]
    [InlineData("=PROPER(\"2-way street\")", "2-Way Street")]
    [InlineData("=PROPER(\"76BudGet\")", "76Budget")]
    public void Proper_CapitalizesLettersAfterNonLettersLikeExcel(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void Upper_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('x', 32768))));

        _eval.Evaluate("=UPPER(A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Lower_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('X', 32768))));

        _eval.Evaluate("=LOWER(A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Proper_ResultAtExcelCellLimit_ReturnsText()
    {
        var text = new string('x', 32767);
        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=PROPER(A1)", sheet).Should().Be(new TextValue("X" + new string('x', 32766)));
    }

    [Fact]
    public void Proper_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('x', 32768))));

        _eval.Evaluate("=PROPER(A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void TextCaseAndCleanup_RangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue(" apple ")),
            (2, 1, new TextValue("BANANA")));

        AssertTextColumn(_eval.Evaluate("=UPPER(A1:A2)", sheet), " APPLE ", "BANANA");
        AssertTextColumn(_eval.Evaluate("=LOWER(A1:A2)", sheet), " apple ", "banana");
        AssertTextColumn(_eval.Evaluate("=PROPER(A1:A2)", sheet), " Apple ", "Banana");
        AssertTextColumn(_eval.Evaluate("=TRIM(A1:A2)", sheet), "apple", "BANANA");
        AssertTextColumn(_eval.Evaluate("=CLEAN(A1:A2)", sheet), " apple ", "BANANA");
    }

    // ── SUBSTITUTE ─────────────────────────────────────────────────────────────

    [Fact]
    public void Substitute_ReplacesAll()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SUBSTITUTE(\"aababc\",\"ab\",\"X\")", sheet).Should().Be(new TextValue("aXXc"));
    }

    [Fact]
    public void Substitute_OmittedInstanceNum_ReplacesAll()
    {
        var sheet = MakeSheet();
        // R102: a genuinely-omitted instance_num (no 4th argument at all) means "replace all" --
        // this used to (incorrectly) use a trailing comma here, which is a PRESENT-but-blank
        // argument; Excel coerces that to numeric 0 instead, which is < 1 and errors #VALUE! (see
        // R102_TrailingCommaEmptyOrdinalArgumentTests for the corrected trailing-comma-empty case).
        _eval.Evaluate("=SUBSTITUTE(\"aababc\",\"ab\",\"X\")", sheet).Should().Be(new TextValue("aXXc"));
    }

    [Fact]
    public void Substitute_ReplacesSpecificInstance()
    {
        var sheet = MakeSheet();
        // "aababc" has "ab" at index 1 and index 3; replacing the 2nd gives "aabXc"
        _eval.Evaluate("=SUBSTITUTE(\"aababc\",\"ab\",\"X\",2)", sheet).Should().Be(new TextValue("aabXc"));
    }

    [Fact]
    public void SubstituteReptAndConcatenate_RangeTextArgument_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("aababc")),
            (2, 1, new TextValue("banana")));

        AssertTextColumn(_eval.Evaluate("=SUBSTITUTE(A1:A2,\"a\",\"X\")", sheet), "XXbXbc", "bXnXnX");
        AssertTextColumn(_eval.Evaluate("=REPT(A1:A2,2)", sheet), "aababcaababc", "bananabanana");
        AssertTextColumn(_eval.Evaluate("=CONCATENATE(A1:A2,\"!\")", sheet), "aababc!", "banana!");
    }

    // ── FIND ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Substitute_SameShapeTextArguments_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("aababc")),
            (2, 1, new TextValue("banana")),
            (1, 2, new TextValue("a")),
            (2, 2, new TextValue("na")),
            (1, 3, new TextValue("x")),
            (2, 3, new TextValue("N")),
            (1, 4, new NumberValue(2)),
            (2, 4, new NumberValue(1)));

        AssertTextColumn(_eval.Evaluate("=SUBSTITUTE(A1:A2,B1:B2,C1:C2,D1:D2)", sheet), "axbabc", "baNna");
    }

    [Fact]
    public void Substitute_OneCellInstanceRange_BroadcastsAcrossTextArray()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("aababc")),
            (2, 1, new TextValue("banana")),
            (1, 2, new NumberValue(2)));

        AssertTextColumn(_eval.Evaluate("=SUBSTITUTE(A1:A2,\"a\",\"x\",B1:B1)", sheet), "axbabc", "banxna");
    }

    [Fact]
    public void Substitute_MismatchedTextOrInstanceArgument_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("aababc")),
            (2, 1, new TextValue("banana")),
            (1, 2, new TextValue("a")),
            (1, 3, new TextValue("x")));

        _eval.Evaluate("=SUBSTITUTE(A1:A2,B1:B3,\"x\")", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=SUBSTITUTE(A1:A2,\"a\",B1:B3)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=SUBSTITUTE(A1:A2,\"a\",\"x\",B1:B3)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Substitute_OldTextError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SUBSTITUTE(\"abc\",NA(),\"x\")", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Substitute_NewTextError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SUBSTITUTE(\"abc\",\"a\",NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Substitute_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var text = new string('x', 32767);
        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=SUBSTITUTE(A1,\"x\",\"yy\",1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Substitute_ResultAtExcelCellLimit_ReturnsText()
    {
        var text = new string('x', 32767);
        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=SUBSTITUTE(A1,\"x\",\"x\",1)", sheet).Should().Be(new TextValue(text));
    }

    [Fact]
    public void Substitute_ResultWithSurrogatePairsAtExcelCellLimit_ReturnsText()
    {
        // Cell limit is 32767 UTF-16 code units; 😀 is 2 code units.
        // 16383 emoji = 32766 code units ≤ 32767 → valid text.
        var text = string.Concat(Enumerable.Repeat("😀", 16383));
        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=SUBSTITUTE(A1,\"😀\",\"😀\",1)", sheet).Should().Be(new TextValue(text));
    }

    [Fact]
    public void Substitute_ResultWithSurrogatePairsLongerThanExcelCellLimit_ReturnsValueError()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 32768));
        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=SUBSTITUTE(A1,\"x\",\"y\",1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Substitute_UnchangedResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('x', 32768))));

        _eval.Evaluate("=SUBSTITUTE(A1,\"z\",\"y\",1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Substitute_NonFiniteInstanceNum_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=SUBSTITUTE(\"abc\",\"a\",\"x\",A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Find_CaseSensitive_ReturnsPosition()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FIND(\"lo\",\"hello\")", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void FindAndSearch_OmittedStartNum_DefaultsToOne()
    {
        var sheet = MakeSheet();

        // R102: a genuinely-omitted start_num (no 3rd argument at all) defaults to 1 -- this used
        // to (incorrectly) use a trailing comma here, which is a PRESENT-but-blank argument; Excel
        // coerces that to numeric 0 instead, which is < 1 and errors #VALUE! (see
        // R102_TrailingCommaEmptyOrdinalArgumentTests for the corrected trailing-comma-empty case).
        _eval.Evaluate("=FIND(\"h\",\"hello\")", sheet).Should().Be(new NumberValue(1));
        _eval.Evaluate("=SEARCH(\"H\",\"hello\")", sheet).Should().Be(new NumberValue(1));
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
    public void Find_EmptyFindTextAtEndBoundary_ReturnsStartNum()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FIND(\"\",\"abc\",4)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void FindAndSearch_EmptyFindTextUseScalarEndBoundary()
    {
        var sheet = MakeSheet();

        // 😀 is 2 UTF-16 code units; empty-string match is valid up to length+1 = 3.
        _eval.Evaluate("=FIND(\"\",\"😀\",2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=FIND(\"\",\"😀\",3)", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=FIND(\"\",\"😀\",4)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=SEARCH(\"\",\"😀\",2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=SEARCH(\"\",\"😀\",3)", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=SEARCH(\"\",\"😀\",4)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void FindAndSearch_ReturnUtf16CodeUnitPositionsAfterSurrogatePairs()
    {
        var sheet = MakeSheet();

        // 😀 is a surrogate pair (2 UTF-16 code units); positions are code-unit based.
        _eval.Evaluate("=FIND(\"y\",\"😀y\")", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=FIND(\"y\",\"x😀y\",3)", sheet).Should().Be(new NumberValue(4));
        _eval.Evaluate("=SEARCH(\"Y\",\"😀y\")", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=SEARCH(\"Y\",\"x😀y\",3)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Find_SurrogatePairUtf16Positions_ExcelParity()
    {
        var sheet = MakeSheet();

        // Q12 regression: FIND positions count UTF-16 code units.
        // FIND("X","A😀X"): "A😀X" = A+\uD83D+\uDE00+X → "X" at index 3 (0-based) = position 4.
        _eval.Evaluate("=FIND(\"X\",\"A😀X\")", sheet).Should().Be(new NumberValue(4));
        // FIND("😀","A😀B"): "😀" at index 1 (0-based) = position 2.
        _eval.Evaluate("=FIND(\"😀\",\"A😀B\")", sheet).Should().Be(new NumberValue(2));
        // BMP-only text unchanged.
        _eval.Evaluate("=FIND(\"c\",\"abcd\")", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Find_WithinTextError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FIND(\"x\",NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Find_StartNumError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FIND(\"x\",\"xyz\",NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Find_NonFiniteStartNum_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=FIND(\"x\",\"xyz\",A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Find_RangeWithinTextArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")));

        var result = _eval.Evaluate("=FIND(\"p\",A1:A2)", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(2);
        range.ColCount.Should().Be(1);
        range.At(1, 1).Should().Be(new NumberValue(2));
        range.At(2, 1).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Find_SameShapeFindAndWithinTextRanges_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("p")),
            (2, 1, new TextValue("n")),
            (1, 2, new TextValue("Apple")),
            (2, 2, new TextValue("Banana")));

        AssertColumn(_eval.Evaluate("=FIND(A1:A2,B1:B2)", sheet), new NumberValue(2), new NumberValue(3));
    }

    [Fact]
    public void Find_LeadingOneCellFindTextRange_BroadcastsAcrossWithinTextArray()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (1, 2, new TextValue("cat")),
            (2, 2, new TextValue("bad")));

        AssertColumn(_eval.Evaluate("=FIND(A1:A1,B1:B2)", sheet), new NumberValue(2), new NumberValue(2));
    }

    [Fact]
    public void Find_MismatchedFindAndWithinTextRanges_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("p")),
            (2, 1, new TextValue("n")),
            (1, 2, new TextValue("Apple")),
            (1, 3, new TextValue("Banana")));

        // A row-vector (1x2) crossed with a column-vector (2x1) is now a valid cross-broadcast
        // (R118-formula-arity3plus-cross-broadcast), so this uses B1:B3 (a same-axis, differently
        // sized column) to keep testing a genuine shape mismatch.
        _eval.Evaluate("=FIND(A1:A2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Find_SameShapeStartNumArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("banana")),
            (2, 1, new TextValue("cocoa")),
            (1, 2, new NumberValue(3)),
            (2, 2, new NumberValue(3)));

        AssertColumn(_eval.Evaluate("=FIND(\"a\",A1:A2,B1:B2)", sheet), new NumberValue(4), new NumberValue(5));
    }

    [Fact]
    public void Find_MismatchedStartNumArgument_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("banana")),
            (2, 1, new TextValue("cocoa")),
            (1, 2, new NumberValue(2)),
            (1, 3, new NumberValue(3)));

        _eval.Evaluate("=FIND(\"a\",A1:A2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
    }

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
    public void Search_WildcardQuestionTreatsSurrogatePairAsSingleCharacter()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=SEARCH(\"?x\",\"😀x\")", sheet).Should().Be(new NumberValue(1));
        _eval.Evaluate("=SEARCH(\"??\",\"😀\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Search_TildeEscapesWildcard_MatchesLiteralQuestion()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SEARCH(\"~?\",\"a?b\")", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Search_NotFound_ReturnsValueError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SEARCH(\"xyz\",\"hello\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Search_RangeWithinTextArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")));

        var result = _eval.Evaluate("=SEARCH(\"a\",A1:A2)", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(2);
        range.ColCount.Should().Be(1);
        range.At(1, 1).Should().Be(new NumberValue(1));
        range.At(2, 1).Should().Be(new NumberValue(2));
    }

    // ── MID ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Search_SameShapeFindAndWithinTextRanges_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("P")),
            (2, 1, new TextValue("N")),
            (1, 2, new TextValue("Apple")),
            (2, 2, new TextValue("Banana")));

        AssertColumn(_eval.Evaluate("=SEARCH(A1:A2,B1:B2)", sheet), new NumberValue(2), new NumberValue(3));
    }

    [Fact]
    public void Search_MismatchedFindAndWithinTextRanges_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("P")),
            (2, 1, new TextValue("N")),
            (1, 2, new TextValue("Apple")),
            (1, 3, new TextValue("Banana")));

        _eval.Evaluate("=SEARCH(A1:A2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Search_SameShapeStartNumArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("banana")),
            (2, 1, new TextValue("cocoa")),
            (1, 2, new NumberValue(3)),
            (2, 2, new NumberValue(3)));

        AssertColumn(_eval.Evaluate("=SEARCH(\"A\",A1:A2,B1:B2)", sheet), new NumberValue(4), new NumberValue(5));
    }

    [Fact]
    public void Search_MismatchedStartNumArgument_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("banana")),
            (2, 1, new TextValue("cocoa")),
            (1, 2, new NumberValue(2)),
            (1, 3, new NumberValue(3)));

        _eval.Evaluate("=SEARCH(\"A\",A1:A2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Search_EmptyFindTextAtEndBoundary_ReturnsStartNum()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SEARCH(\"\",\"abc\",4)", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Search_WithinTextError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SEARCH(\"x\",NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Search_StartNumError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SEARCH(\"x\",\"xyz\",NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Search_NonFiniteStartNum_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=SEARCH(\"x\",\"xyz\",A1)", sheet).Should().Be(ErrorValue.Value);
    }

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

    [Fact]
    public void Mid_RangeTextArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")));

        var result = _eval.Evaluate("=MID(A1:A2,2,2)", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(2);
        range.ColCount.Should().Be(1);
        range.At(1, 1).Should().Be(new TextValue("pp"));
        range.At(2, 1).Should().Be(new TextValue("an"));
    }

    // ── REPT ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Mid_SameShapeStartAndLengthArguments_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")),
            (1, 2, new NumberValue(2)),
            (2, 2, new NumberValue(3)),
            (1, 3, new NumberValue(2)),
            (2, 3, new NumberValue(3)));

        AssertTextColumn(_eval.Evaluate("=MID(A1:A2,B1:B2,C1:C2)", sheet), "pp", "nan");
    }

    [Fact]
    public void Mid_MismatchedStartOrLengthArgument_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Apple")),
            (2, 1, new TextValue("Banana")),
            (1, 2, new NumberValue(2)),
            (1, 3, new NumberValue(3)));

        _eval.Evaluate("=MID(A1:A2,B1:B3,2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=MID(A1:A2,2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Mid_SlicesOnUtf16CodeUnitBoundaries()
    {
        var sheet = MakeSheet();

        // Excel MID counts UTF-16 code units; 😀 is a surrogate pair at indices 0+1.
        _eval.Evaluate("=MID(\"😀x\",1,1)", sheet).Should().Be(new TextValue("\uD83D"));
        _eval.Evaluate("=MID(\"😀x\",2,1)", sheet).Should().Be(new TextValue("\uDE00"));
        _eval.Evaluate("=MID(\"😀x\",3,1)", sheet).Should().Be(new TextValue("x"));
        _eval.Evaluate("=MID(\"x😀y\",2,1)", sheet).Should().Be(new TextValue("\uD83D"));
        _eval.Evaluate("=MID(\"x😀y\",3,1)", sheet).Should().Be(new TextValue("\uDE00"));
        _eval.Evaluate("=MID(\"x😀y\",4,1)", sheet).Should().Be(new TextValue("y"));
    }

    [Fact]
    public void Mid_SurrogatePair_ExcelParityRegression()
    {
        var sheet = MakeSheet();

        // Q12 regression: MID("😀",1,1) returns the lone high surrogate (Excel behavior).
        _eval.Evaluate("=MID(\"😀\",1,1)", sheet).Should().Be(new TextValue("\uD83D"));
        // MID("😀",1,2) returns the whole emoji.
        _eval.Evaluate("=MID(\"😀\",1,2)", sheet).Should().Be(new TextValue("😀"));
        // BMP-only text unchanged.
        _eval.Evaluate("=MID(\"abcd\",2,2)", sheet).Should().Be(new TextValue("bc"));
    }

    [Fact]
    public void Mid_StartNumError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=MID(\"hello\",NA(),1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Mid_NumCharsError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=MID(\"hello\",1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Mid_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue(new string('x', 32768))));

        _eval.Evaluate("=MID(A1,1,32768)", sheet).Should().Be(ErrorValue.Value);
    }

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

    [Fact]
    public void Rept_NegativeFractionalTimes_ReturnsValueError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=REPT(\"x\",-0.5)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Rept_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=REPT(\"x\",32768)", sheet).Should().Be(ErrorValue.Value);
    }

    // ── VALUE ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Rept_SupplementaryUnicodeUsesCodeUnitLimitOf32767()
    {
        var sheet = MakeSheet();

        // 😀 is 2 UTF-16 code units; cell limit is 32767 code units.
        // 16383 * 2 = 32766 ≤ 32767 → valid; 16384 * 2 = 32768 > 32767 → error.
        _eval.Evaluate("=LEN(REPT(\"😀\",16383))", sheet).Should().Be(new NumberValue(32766));
        _eval.Evaluate("=REPT(\"😀\",16384)", sheet).Should().Be(ErrorValue.Value);
        // The old 32767-repetition limit no longer applies to emoji.
        _eval.Evaluate("=REPT(\"😀\",32767)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Rept_SameShapeTimesArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (2, 1, new TextValue("bc")),
            (1, 2, new NumberValue(3)),
            (2, 2, new NumberValue(2)));

        AssertTextColumn(_eval.Evaluate("=REPT(A1:A2,B1:B2)", sheet), "aaa", "bcbc");
    }

    [Fact]
    public void Rept_RowVectorAndColumnVectorTimesArgument_SpillsToCrossBroadcastMatrix()
    {
        // Regression guard for R62-formula-array-broadcast-6-1: a 2x1 column vector crossed with
        // a 1x2 row vector must 2-D cross-broadcast into a 2x2 spilled result, not #VALUE! --
        // this test previously asserted the old (superseded) #VALUE! behavior.
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (2, 1, new TextValue("bc")),
            (1, 2, new NumberValue(3)),
            (1, 3, new NumberValue(2)));

        var result = _eval.Evaluate("=REPT(A1:A2,B1:C1)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        ((TextValue)result.At(1, 1)).Value.Should().Be("aaa");
        ((TextValue)result.At(1, 2)).Value.Should().Be("aa");
        ((TextValue)result.At(2, 1)).Value.Should().Be("bcbcbc");
        ((TextValue)result.At(2, 2)).Value.Should().Be("bcbc");
    }

    [Fact]
    public void Rept_TrulyMismatchedTimesArgument_ReturnsValueError()
    {
        // Sibling no-regression: ranges that conflict on the SAME axis (neither equal nor size-1)
        // must still be a genuine #VALUE! shape mismatch.
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (2, 1, new TextValue("bc")),
            (1, 2, new NumberValue(3)),
            (2, 2, new NumberValue(2)),
            (3, 2, new NumberValue(1)));

        _eval.Evaluate("=REPT(A1:A2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Value_ParsesNumber()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=VALUE(\"42.5\")", sheet).Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void Value_ParsesPercentText()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=VALUE(\"50%\")", sheet).Should().Be(new NumberValue(0.5));
    }

    [Fact]
    public void Value_ParsesCurrencyThousandsAndDateText()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=VALUE(\"$1,234.50\")", sheet).Should().Be(new NumberValue(1234.5));
        _eval.Evaluate("=VALUE(\"1/2/2024\")", sheet).Should().Be(new NumberValue(45293));
    }

    [Fact]
    public void Value_ParsesTimeAndDateTimeText()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=VALUE(\"1:30 PM\")", sheet).Should().Be(new NumberValue(0.5625));
        _eval.Evaluate("=VALUE(\"1/2/2024 6:00 AM\")", sheet)
            .Should().Be(new NumberValue(new DateTime(2024, 1, 2, 6, 0, 0).ToOADate()));
    }

    [Fact]
    public void Value_ParsesExcelFakeLeapDayText()
    {
        _eval.Evaluate("=VALUE(\"2/29/1900\")", MakeSheet()).Should().Be(new NumberValue(60));
        _eval.Evaluate("=VALUE(\"1900-02-29\")", MakeSheet()).Should().Be(new NumberValue(60));
        _eval.Evaluate("=VALUE(\"2/29/1900 6:00 AM\")", MakeSheet()).Should().Be(new NumberValue(60.25));
    }

    [Fact]
    public void Value_InvalidText_ReturnsValueError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=VALUE(\"abc\")", sheet).Should().Be(ErrorValue.Value);
    }

}
