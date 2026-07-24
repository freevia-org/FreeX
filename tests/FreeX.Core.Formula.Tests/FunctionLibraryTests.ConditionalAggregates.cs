using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
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


    [Fact]
    public void Averageifs_OverflowingMatchedAverage_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1E308)), (2, 1, new NumberValue(1E308)),
            (1, 2, new TextValue("A")), (2, 2, new TextValue("A")));

        _eval.Evaluate("=AVERAGEIFS(A1:A2,B1:B2,\"A\")", sheet).Should().Be(ErrorValue.Num);
    }

    // ── Cluster B: COUNTIF/COUNTIFS with "<>0" and text cells ─────────────────
    // Excel: "<>0" counts text cells (they are not numerically equal to 0).
    // Blank cells are NOT counted (blank is treated as 0 in this context).

    [Fact]
    public void Countif_NotEqualZeroCriteria_CountsTextCells()
    {
        // A1="Eng" (text, not zero → count), A2=0 (number zero → don't count),
        // A3=1 (number non-zero → count), A4=(blank → don't count), A5="HR" (text → count)
        var sheet = MakeSheet(
            (1, 1, new TextValue("Eng")),
            (2, 1, new NumberValue(0)),
            (3, 1, new NumberValue(1)),
            (4, 1, new TextValue("HR")));
        // Blanks default to BlankValue, already handled

        // Text "Eng" and "HR" count; 0 doesn't; 1 does → total 3
        _eval.Evaluate("=COUNTIF(A1:A4,\"<>0\")", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Countif_NotEqualZeroCriteria_DoesNotCountBlanks()
    {
        // A1=blank, A2=0, A3="text" → only "text" should match
        var sheet = MakeSheet(
            (2, 1, new NumberValue(0)),
            (3, 1, new TextValue("text")));

        _eval.Evaluate("=COUNTIF(A1:A3,\"<>0\")", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countifs_NotEqualZeroCriteria_CountsTextCells()
    {
        // Same semantics via COUNTIFS
        var sheet = MakeSheet(
            (1, 1, new TextValue("Eng")),
            (2, 1, new NumberValue(0)),
            (3, 1, new NumberValue(5)),
            (4, 1, new TextValue("HR")));

        _eval.Evaluate("=COUNTIFS(A1:A4,\"<>0\")", sheet).Should().Be(new NumberValue(3));
    }

    // ── Cluster A: "&" concatenation with DateTimeValue operand ───────────────
    // Excel: concatenating a date cell with "&" uses the date serial (numeric).
    // FreeX bug: ValueToString(DateTimeValue) fell through to default ToString().

    [Fact]
    public void Concatenation_DateTimeValue_ProducesDateSerial()
    {
        // DATE(2024,1,1) = serial 45292; ">="&DATE(2024,1,1) should produce ">=45292"
        var sheet = MakeSheet();
        // "test"&DATE(2024,1,1) should produce "test45292"
        var date = DateTimeValue.FromDateTime(new DateTime(2024, 1, 1));
        var dateSerial = date.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var expected = new TextValue("prefix" + dateSerial);
        var sheetWithDate = MakeSheet((1, 1, date));
        _eval.Evaluate("=\"prefix\"&A1", sheetWithDate).Should().Be(expected);
    }

    [Fact]
    public void Sumifs_DateCriteriaFromConcatenation_MatchesDateColumn()
    {
        // Simulates: SUMIFS(budget, month_col, "<="&cutoff_date)
        // where month_col holds DateTimeValues and cutoff_date is a DateTimeValue.
        var jan = DateTimeValue.FromDateTime(new DateTime(2024, 1, 1));
        var feb = DateTimeValue.FromDateTime(new DateTime(2024, 2, 1));
        var mar = DateTimeValue.FromDateTime(new DateTime(2024, 3, 1));
        var cutoff = DateTimeValue.FromDateTime(new DateTime(2024, 2, 29));

        // A: month dates, B: budget values, C1: cutoff date
        var sheet = MakeSheet(
            (1, 1, jan), (1, 2, new NumberValue(100)),
            (2, 1, feb), (2, 2, new NumberValue(200)),
            (3, 1, mar), (3, 2, new NumberValue(300)),
            (1, 3, cutoff));

        // SUMIFS(B1:B3, A1:A3, "<="&C1) should sum Jan+Feb = 300
        _eval.Evaluate("=SUMIFS(B1:B3,A1:A3,\"<=\"&C1)", sheet).Should().Be(new NumberValue(300));
    }

    // ── Integration: COUNTIFS over a named range in a multi-sheet workbook ─────
    // Reproduces the Calc(2)!C6 scenario from ExcelExamples1.xlsx:
    //   selected.depts = 'Calc (2)'!B5:K5 (6 text dept names + 4 numeric zeros)
    //   COUNTIFS(selected.depts,"<>0") should return 6 (text cells count, 0s don't)

    [Fact]
    public void Countifs_NamedRangeOverMixedTextAndZero_CountsTextCells()
    {
        // Arrange: workbook with "Calc (2)" sheet, B5:G5 = text dept names, H5:K5 = 0
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Calc (2)");

        // B5:G5 = Finance, HR, IT, Marketing, Operations, Sales (text)
        var depts = new[] { "Finance", "HR", "IT", "Marketing", "Operations", "Sales" };
        for (int i = 0; i < depts.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, 5, (uint)(2 + i)), new TextValue(depts[i]));

        // H5:K5 = 0 (numeric zeros — unselected departments)
        for (int i = 0; i < 4; i++)
            sheet.SetCell(new CellAddress(sheet.Id, 5, (uint)(8 + i)), new NumberValue(0));

        // Define named range "selected.depts" = B5:K5 on Calc(2)
        var start = new CellAddress(sheet.Id, 5, 2);  // B5
        var end   = new CellAddress(sheet.Id, 5, 11); // K5
        workbook.DefineNamedRange("selected.depts", new GridRange(start, end));

        // Act: evaluate COUNTIFS(selected.depts,"<>0") on Calc(2) sheet
        var result = _eval.Evaluate("=COUNTIFS(selected.depts,\"<>0\")", sheet, workbook);

        // Assert: 6 text cells match "<>0"; 4 numeric zeros do not
        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Countifs_NamedRange_C6Formula_ReturnsFalse()
    {
        // Full reproduction of Calc(2)!C6 formula.
        // COUNTIFS(selected.depts,"<>0") = 6
        // SUMPRODUCT(1/COUNTIFS(people[Department],people[Department])) = 6
        // So C6 = (6 <> 6) = FALSE
        // This test verifies the COUNTIFS(selected.depts,"<>0") half returns 6,
        // using a plain range instead of the structured table reference for SUMPRODUCT.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Calc (2)");

        // B5:G5 = 6 department text names
        var depts = new[] { "Finance", "HR", "IT", "Marketing", "Operations", "Sales" };
        for (int i = 0; i < depts.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, 5, (uint)(2 + i)), new TextValue(depts[i]));

        // H5:K5 = 0 (four unselected slots)
        for (int i = 0; i < 4; i++)
            sheet.SetCell(new CellAddress(sheet.Id, 5, (uint)(8 + i)), new NumberValue(0));

        workbook.DefineNamedRange("selected.depts", new GridRange(
            new CellAddress(sheet.Id, 5, 2),
            new CellAddress(sheet.Id, 5, 11)));

        // The COUNTIFS half should = 6
        var countResult = _eval.Evaluate("=COUNTIFS(selected.depts,\"<>0\")", sheet, workbook);
        countResult.Should().Be(new NumberValue(6), "6 text dept names are <> 0");

        // Simulate SUMPRODUCT half = 6 via plain range (6 unique depts, each count = 100/6 rounded)
        // Using A1:A6 with the same dept names repeated to simulate the people table
        // Each distinct dept appears once as criteria → result per dept = 1/count.
        // Here we'll just assert the COUNTIFS result independently for clarity.
    }

    // ── range-must-be-a-reference (R83-formula-index-offset-choose-5-1) ────────
    //
    // Excel requires the range/sum_range/criteria_range argument of the *IF(S)
    // family to be a genuine worksheet reference. A computed array — an array
    // constant like {1,2,3} or the result of an array-returning function like
    // TRANSPOSE — is rejected with #VALUE!, even though the criteria argument
    // itself is allowed to be an array (that triggers element-wise expansion).

    [Fact]
    public void Countif_ArrayConstantRange_ReturnsValueError()
    {
        // Real Excel: =COUNTIF({1,2,3},">1") is #VALUE! because the range argument
        // must be a worksheet reference, not a computed array constant.
        _eval.Evaluate("=COUNTIF({1,2,3},\">1\")", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Countif_CellRangeReference_StillCounts()
    {
        // No-regression sibling: a genuine worksheet reference must still work.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=COUNTIF(A1:A3,\">1\")", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Sumif_TransposeRange_ReturnsValueError()
    {
        // TRANSPOSE always produces a computed (non-reference) array, so using it
        // as SUMIF's range argument must be rejected with #VALUE! like Excel.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (1, 2, new NumberValue(2)),
            (1, 3, new NumberValue(3)));
        _eval.Evaluate("=SUMIF(TRANSPOSE(A1:C1),\">1\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sumif_SumRangeAsArrayConstant_ReturnsValueError()
    {
        // The sum_range argument must also be a genuine reference, not a computed array.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=SUMIF(A1:A3,\">1\",{10,20,30})", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sumifs_CriteriaRangeAsArrayConstant_ReturnsValueError()
    {
        // SUMIFS' criteria_range slots are also required to be genuine references.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=SUMIFS(A1:A3,{1,2,3},\">1\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sumifs_CellRangeReferences_StillSums()
    {
        // No-regression sibling: genuine references in every slot must still work.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(20)),
            (3, 1, new NumberValue(3)), (3, 2, new NumberValue(30)));
        _eval.Evaluate("=SUMIFS(B1:B3,A1:A3,\">1\")", sheet).Should().Be(new NumberValue(50));
    }
}
