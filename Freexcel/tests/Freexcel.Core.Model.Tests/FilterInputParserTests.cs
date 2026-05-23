using FluentAssertions;
using Freexcel.Core.Commands;

namespace Freexcel.Core.Model.Tests;

public sealed class FilterInputParserTests
{
    [Fact]
    public void Parse_SplitsCommaAndSemicolonSeparatedValues()
    {
        var values = FilterInputParser.ParseAllowedValues("Apple, Banana; Cherry");

        values.Should().Equal("Apple", "Banana", "Cherry");
    }

    [Fact]
    public void Parse_EmptyInputClearsFilter()
    {
        var values = FilterInputParser.ParseAllowedValues("  ");

        values.Should().BeEmpty();
    }

    [Fact]
    public void TryParseCriterion_AcceptsContainsSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("contains: apple", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<TextContainsFilterCriterion>()
            .Which.Matches(new TextValue("Green Apple")).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsGreaterThanSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("> 10", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<NumberGreaterThanFilterCriterion>()
            .Which.Matches(new NumberValue(11)).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsLessThanSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("< 10", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<NumberLessThanFilterCriterion>()
            .Which.Matches(new NumberValue(9)).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsEqualsSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("= 10", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<NumberEqualsFilterCriterion>()
            .Which.Matches(new NumberValue(10)).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsNotEqualsSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("<> 10", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<NumberNotEqualsFilterCriterion>()
            .Which.Matches(new NumberValue(11)).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsGreaterThanOrEqualSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion(">= 10", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<NumberGreaterThanOrEqualFilterCriterion>()
            .Which.Matches(new NumberValue(10)).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsLessThanOrEqualSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("<= 10", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<NumberLessThanOrEqualFilterCriterion>()
            .Which.Matches(new NumberValue(10)).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsBetweenSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("between:10:20", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<NumberBetweenFilterCriterion>()
            .Which.Matches(new NumberValue(15)).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsBeginsWithSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("begins: Red", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<TextBeginsWithFilterCriterion>()
            .Which.Matches(new TextValue("Red Apple")).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsEndsWithSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("ends: Apple", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<TextEndsWithFilterCriterion>()
            .Which.Matches(new TextValue("Green Apple")).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsNotContainsSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("notcontains: apple", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<TextDoesNotContainFilterCriterion>()
            .Which.Matches(new TextValue("Banana")).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsTextEqualsSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("text= Red Apple", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<TextEqualsFilterCriterion>()
            .Which.Matches(new TextValue("red apple")).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsMenuTextEqualsAlias()
    {
        var parsed = FilterInputParser.TryParseCriterion("equals: Red Apple", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<TextEqualsFilterCriterion>()
            .Which.Matches(new TextValue("red apple")).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsTextNotEqualsSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("text<> Red Apple", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<TextNotEqualsFilterCriterion>()
            .Which.Matches(new TextValue("Banana")).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsBlankSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("blank", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<BlankFilterCriterion>()
            .Which.Matches(BlankValue.Instance).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsNonBlankSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("nonblank", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<NonBlankFilterCriterion>()
            .Which.Matches(new TextValue("value")).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsDateEqualsSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("date=2026-05-15", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<DateEqualsFilterCriterion>()
            .Which.Matches(DateTimeValue.FromDateTime(new DateTime(2026, 5, 15, 12, 30, 0))).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsDateNotEqualsSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("date<>2026-05-15", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<DateNotEqualsFilterCriterion>()
            .Which.Matches(DateTimeValue.FromDateTime(new DateTime(2026, 5, 16))).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsDateAfterSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("date>2026-05-15", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<DateAfterFilterCriterion>()
            .Which.Matches(DateTimeValue.FromDateTime(new DateTime(2026, 5, 16))).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsDateBeforeSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("date<2026-05-15", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<DateBeforeFilterCriterion>()
            .Which.Matches(DateTimeValue.FromDateTime(new DateTime(2026, 5, 14))).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsDateOnOrAfterSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("date>=2026-05-15", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<DateOnOrAfterFilterCriterion>()
            .Which.Matches(DateTimeValue.FromDateTime(new DateTime(2026, 5, 15))).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsDateOnOrBeforeSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("date<=2026-05-15", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<DateOnOrBeforeFilterCriterion>()
            .Which.Matches(DateTimeValue.FromDateTime(new DateTime(2026, 5, 15))).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsDateBetweenSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("datebetween:2026-05-15:2026-05-20", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<DateBetweenFilterCriterion>()
            .Which.Matches(DateTimeValue.FromDateTime(new DateTime(2026, 5, 18))).Should().BeTrue();
    }

    [Fact]
    public void TryParseCriterion_AcceptsAndCompositeSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("and:>10|<20", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<CompositeFilterCriterion>()
            .Which.Matches(new NumberValue(15)).Should().BeTrue();
        criterion!.Matches(new NumberValue(25)).Should().BeFalse();
    }

    [Fact]
    public void TryParseCriterion_AcceptsOrCompositeSyntax()
    {
        var parsed = FilterInputParser.TryParseCriterion("or:begins:Red|ends:Apple", out var criterion, out var error);

        parsed.Should().BeTrue(error);
        criterion.Should().BeOfType<CompositeFilterCriterion>()
            .Which.Matches(new TextValue("Red Pear")).Should().BeTrue();
        criterion!.Matches(new TextValue("Green Apple")).Should().BeTrue();
        criterion.Matches(new TextValue("Green Pear")).Should().BeFalse();
    }

    [Fact]
    public void TryParseTopBottom_AcceptsTopSyntax()
    {
        var parsed = FilterInputParser.TryParseTopBottom("top:10", out var count, out var top, out var error);

        parsed.Should().BeTrue(error);
        count.Should().Be(10);
        top.Should().BeTrue();
    }

    [Fact]
    public void TryParseTopBottom_AcceptsBottomSyntax()
    {
        var parsed = FilterInputParser.TryParseTopBottom("bottom:5", out var count, out var top, out var error);

        parsed.Should().BeTrue(error);
        count.Should().Be(5);
        top.Should().BeFalse();
    }

    [Fact]
    public void TryParseTopBottom_AcceptsTopPercentSyntax()
    {
        var parsed = FilterInputParser.TryParseTopBottom("toppercent:25", out var count, out var top, out var percent, out var error);

        parsed.Should().BeTrue(error);
        count.Should().Be(25);
        top.Should().BeTrue();
        percent.Should().BeTrue();
    }

    [Fact]
    public void TryParseTopBottom_AcceptsBottomPercentSyntax()
    {
        var parsed = FilterInputParser.TryParseTopBottom("bottompercent:10", out var count, out var top, out var percent, out var error);

        parsed.Should().BeTrue(error);
        count.Should().Be(10);
        top.Should().BeFalse();
        percent.Should().BeTrue();
    }

    [Fact]
    public void TryParseAverage_AcceptsAboveAverageSyntax()
    {
        var parsed = FilterInputParser.TryParseAverage("aboveavg", out var above);

        parsed.Should().BeTrue();
        above.Should().BeTrue();
    }

    [Fact]
    public void TryParseAverage_AcceptsMenuAboveAverageSyntax()
    {
        var parsed = FilterInputParser.TryParseAverage("above average", out var above);

        parsed.Should().BeTrue();
        above.Should().BeTrue();
    }

    [Fact]
    public void TryParseAverage_AcceptsBelowAverageSyntax()
    {
        var parsed = FilterInputParser.TryParseAverage("belowaverage", out var above);

        parsed.Should().BeTrue();
        above.Should().BeFalse();
    }

    [Fact]
    public void TryParseAverage_AcceptsMenuBelowAverageSyntax()
    {
        var parsed = FilterInputParser.TryParseAverage("below average", out var above);

        parsed.Should().BeTrue();
        above.Should().BeFalse();
    }
}
