using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
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

        // A row-vector (1x2) crossed with a column-vector (2x1) is now a valid cross-broadcast
        // (R118-formula-arity3plus-cross-broadcast), so this uses B1:B3 (a same-axis, differently
        // sized column) to keep testing a genuine shape mismatch.
        _eval.Evaluate("=REPLACE(A1:A2,B1:B3,2,\"X\")", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=REPLACE(A1:A2,2,B1:B3,\"X\")", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=REPLACE(A1:A2,2,2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Replace_SlicesOnUtf16CodeUnitBoundaries()
    {
        var sheet = MakeSheet();

        // Excel REPLACE counts UTF-16 code units; 😀 is a surrogate pair (😀).
        _eval.Evaluate("=REPLACE(\"😀x\",1,1,\"Q\")", sheet).Should().Be(new TextValue("Q\uDE00x"));
        _eval.Evaluate("=REPLACE(\"x😀y\",2,1,\"Q\")", sheet).Should().Be(new TextValue("xQ\uDE00y"));
        _eval.Evaluate("=REPLACE(\"😀x\",2,0,\"Q\")", sheet).Should().Be(new TextValue("\uD83DQ\uDE00x"));
    }

    [Fact]
    public void Replace_SurrogatePair_ExcelParityRegression()
    {
        var sheet = MakeSheet();

        // Q12 regression: REPLACE("😀",1,2,"Q") replaces the full emoji (2 code units) with Q.
        _eval.Evaluate("=REPLACE(\"😀\",1,2,\"Q\")", sheet).Should().Be(new TextValue("Q"));
        // BMP-only text unchanged.
        _eval.Evaluate("=REPLACE(\"abcd\",2,2,\"XY\")", sheet).Should().Be(new TextValue("aXYd"));
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

    // F3 regression: CONCATENATE / TEXTJOIN number→text coercion must use
    // Excel's 15-significant-digit General format, not raw double.ToString.
    [Fact]
    public void Concatenate_NumberArg_Uses15SigDigits()
    {
        // CONCATENATE with numeric cell: 1/3 stored in A1, result must be 15 sig digits.
        // Excel: =CONCATENATE(A1,"x") where A1=1/3 → "0.333333333333333x"
        var sheet = MakeSheet((1, 1, new NumberValue(1.0 / 3.0)));
        _eval.Evaluate("=CONCATENATE(A1,\"x\")", sheet)
            .Should().Be(new TextValue("0.333333333333333x"));
    }

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
    public void Hyperlink_TrailingCommaEmptyFriendlyNameSlot_DisplaysZeroNotLinkLocation()
    {
        // `=HYPERLINK("url",)` supplies the friendly_name argument slot (unlike the
        // arity-omitted `=HYPERLINK("url")` case above) -- Excel's parser evaluates an empty
        // comma-slot argument as the same "Empty" value as a blank cell reference, so it is
        // subject to the same blank-coerces-to-"0" HYPERLINK quirk, not the link-location
        // fallback (which is reserved for a genuinely omitted argument slot).
        _eval.Evaluate("=HYPERLINK(\"https://example.com\",)", MakeSheet())
            .Should().Be(new TextValue("0"));
    }

    [Fact]
    public void Hyperlink_PresentButBlankFriendlyNameCell_DisplaysZeroNotLinkLocation()
    {
        // B2 is a genuinely empty (never-written) cell: the friendly_name argument slot IS
        // supplied (it's `B2`, not omitted), so real Excel does NOT fall back to the link
        // location -- it coerces the blank the same way it would for a numeric argument and
        // displays "0" (the documented HYPERLINK quirk; the workaround is `B2&""`).
        var sheet = MakeSheet();

        _eval.Evaluate("=HYPERLINK(\"https://example.com\",B2)", sheet)
            .Should().Be(new TextValue("0"));
    }

    [Fact]
    public void Hyperlink_OmittedFriendlyNameArgument_StillReturnsLinkLocation()
    {
        // Sibling/no-regression case: a genuinely omitted argument (no comma at all) must
        // keep falling back to the link location, unlike the present-but-blank-cell case above.
        _eval.Evaluate("=HYPERLINK(\"https://example.com\")", MakeSheet())
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
    public void Hyperlink_RowVectorAndColumnVectorRangeArguments_SpillToCrossBroadcastMatrix()
    {
        // Regression guard for R62-formula-array-broadcast-6-1: a 2x1 column vector crossed with
        // a 1x2 row vector must 2-D cross-broadcast into a 2x2 spilled result, not #VALUE! --
        // this test previously asserted the old (superseded) #VALUE! behavior.
        var sheet = MakeSheet(
            (1, 1, new TextValue("https://example.com/a")),
            (2, 1, new TextValue("https://example.com/b")),
            (1, 2, new TextValue("A")),
            (1, 3, new TextValue("B")));

        var result = _eval.Evaluate("=HYPERLINK(A1:A2,B1:C1)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        ((TextValue)result.At(1, 1)).Value.Should().Be("A");
        ((TextValue)result.At(1, 2)).Value.Should().Be("B");
        ((TextValue)result.At(2, 1)).Value.Should().Be("A");
        ((TextValue)result.At(2, 2)).Value.Should().Be("B");
    }

    [Fact]
    public void Hyperlink_TrulyMismatchedRangeArgumentShapes_ReturnValueError()
    {
        // Sibling no-regression: ranges that conflict on the SAME axis (neither equal nor size-1)
        // must still be a genuine #VALUE! shape mismatch.
        var sheet = MakeSheet(
            (1, 1, new TextValue("https://example.com/a")),
            (2, 1, new TextValue("https://example.com/b")),
            (1, 2, new TextValue("A")),
            (2, 2, new TextValue("B")),
            (3, 2, new TextValue("C")));

        _eval.Evaluate("=HYPERLINK(A1:A2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
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
    public void Dollar_RowVectorAndColumnVectorDecimalsArgument_SpillsToCrossBroadcastMatrix()
    {
        // Regression guard for R62-formula-array-broadcast-6-1: a 2x1 column vector crossed with
        // a 1x2 row vector must 2-D cross-broadcast into a 2x2 spilled result, not #VALUE! -- DOLLAR
        // is 2-arg (routed through MapBinaryMathArgs, the fixed helper). FIXED's third (no_commas)
        // argument routes it through MapTernaryTextArgs instead, which now applies the SAME
        // cross-broadcast rule (R118-formula-arity3plus-cross-broadcast) -- see
        // Fixed_TernaryHelperRowVectorAndColumnVectorArguments_SpillToCrossBroadcastMatrix below.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1234.56)),
            (2, 1, new NumberValue(-12.34)),
            (1, 2, new NumberValue(1)),
            (1, 3, new NumberValue(0)));

        var dollarResult = _eval.Evaluate("=DOLLAR(A1:A2,B1:C1)", sheet).Should().BeOfType<RangeValue>().Subject;
        dollarResult.RowCount.Should().Be(2);
        dollarResult.ColCount.Should().Be(2);
        ((TextValue)dollarResult.At(1, 1)).Value.Should().Be("$1,234.6");
        ((TextValue)dollarResult.At(1, 2)).Value.Should().Be("$1,235");
        ((TextValue)dollarResult.At(2, 1)).Value.Should().Be("($12.3)");
        ((TextValue)dollarResult.At(2, 2)).Value.Should().Be("($12)");
    }

    [Fact]
    public void FixedAndDollar_TrulyMismatchedDecimalsArgument_ReturnsValueError()
    {
        // Sibling no-regression: ranges that conflict on the SAME axis (neither equal nor size-1)
        // must still be a genuine #VALUE! shape mismatch.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1234.56)),
            (2, 1, new NumberValue(-12.34)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(0)),
            (3, 2, new NumberValue(2)));

        _eval.Evaluate("=FIXED(A1:A2,B1:B3,TRUE)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=DOLLAR(A1:A2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Fixed_TernaryHelperRowVectorAndColumnVectorArguments_SpillToCrossBroadcastMatrix()
    {
        // Regression guard for R118-formula-arity3plus-cross-broadcast: FIXED(number, decimals,
        // no_commas) is a 3-arg call routed through MapTernaryTextArgs (unlike DOLLAR's 2-arg
        // MapBinaryMathArgs, already covered above), so this proves the SAME row-vector (1x2) x
        // column-vector (2x1) cross-broadcast now also spills a 2x2 matrix here instead of #VALUE!.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1234.56)),
            (2, 1, new NumberValue(-12.34)),
            (1, 2, new NumberValue(1)),
            (1, 3, new NumberValue(0)));

        var fixedResult = _eval.Evaluate("=FIXED(A1:A2,B1:C1,TRUE)", sheet).Should().BeOfType<RangeValue>().Subject;
        fixedResult.RowCount.Should().Be(2);
        fixedResult.ColCount.Should().Be(2);
        ((TextValue)fixedResult.At(1, 1)).Value.Should().Be("1234.6");
        ((TextValue)fixedResult.At(1, 2)).Value.Should().Be("1235");
        ((TextValue)fixedResult.At(2, 1)).Value.Should().Be("-12.3");
        ((TextValue)fixedResult.At(2, 2)).Value.Should().Be("-12");
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


    [Fact]
    public void Dollar_ResultLongerThanExcelCellLimit_ReturnsValueError()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=DOLLAR(1,32768)", sheet).Should().Be(ErrorValue.Value);
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


    [Fact]
    public void Asc_NonDbcsCultureLeavesTextUnchanged()
    {
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate("=ASC(\"ＡＢＣ１２３\")", MakeSheet())
            .Should().Be(new TextValue("ＡＢＣ１２３"));
    }

    [Fact]
    public void Dbcs_NonDbcsCultureLeavesTextUnchanged()
    {
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate("=DBCS(\"ABC123\")", MakeSheet())
            .Should().Be(new TextValue("ABC123"));
    }

    [Fact]
    public void Asc_DbcsCultureConvertsFullWidthAsciiAndKanaToHalfWidthText()
    {
        using var culture = new TestCultureScope("ja-JP");

        _eval.Evaluate("=ASC(\"ＡＢＣ１２３！　アイウ\")", MakeSheet())
            .Should().Be(new TextValue("ABC123! ｱｲｳ"));
    }

    [Fact]
    public void Dbcs_DbcsCultureConvertsHalfWidthAsciiAndKanaToFullWidthText()
    {
        using var culture = new TestCultureScope("ja-JP");

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
    public void Filterxml_RowVectorAndColumnVectorRangeArguments_SpillToCrossBroadcastMatrix()
    {
        // Regression guard for R62-formula-array-broadcast-6-1: a 2x1 column vector crossed with
        // a 1x2 row vector must 2-D cross-broadcast into a 2x2 spilled result, not #VALUE! --
        // this test previously asserted the old (superseded) #VALUE! behavior.
        var sheet = MakeSheet(
            (1, 1, new TextValue("<root><item>A</item></root>")),
            (2, 1, new TextValue("<root><item>B</item></root>")),
            (1, 2, new TextValue("/root/item")),
            (1, 3, new TextValue("/root/item")));

        var result = _eval.Evaluate("=FILTERXML(A1:A2,B1:C1)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        ((TextValue)result.At(1, 1)).Value.Should().Be("A");
        ((TextValue)result.At(1, 2)).Value.Should().Be("A");
        ((TextValue)result.At(2, 1)).Value.Should().Be("B");
        ((TextValue)result.At(2, 2)).Value.Should().Be("B");
    }

    [Fact]
    public void Filterxml_TrulyMismatchedRangeArgumentShapes_ReturnValueError()
    {
        // Sibling no-regression: ranges that conflict on the SAME axis (neither equal nor size-1)
        // must still be a genuine #VALUE! shape mismatch.
        var sheet = MakeSheet(
            (1, 1, new TextValue("<root><item>A</item></root>")),
            (2, 1, new TextValue("<root><item>B</item></root>")),
            (1, 2, new TextValue("/root/item")),
            (2, 2, new TextValue("/root/item")),
            (3, 2, new TextValue("/root/item")));

        _eval.Evaluate("=FILTERXML(A1:A2,B1:B3)", sheet).Should().Be(ErrorValue.Value);
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

        _eval.Evaluate("=NUMBERVALUE(A1:A2,B1:B3,\".\")", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=NUMBERVALUE(A1:A2,\".\",B1:B3)", sheet).Should().Be(ErrorValue.Value);
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

}
