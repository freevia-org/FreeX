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
}
