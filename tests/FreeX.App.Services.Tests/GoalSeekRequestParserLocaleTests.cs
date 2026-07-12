using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Covers R29-localization-resx-culture-2: under a comma-decimal / dot-grouping culture (e.g.
/// de-DE), TryParseTargetValue's CurrentCulture-first NumberStyles.Any parse used to spuriously
/// accept a dot-decimal value like "1.5" by misreading the '.' as a (malformed) thousands
/// separator, silently producing 15 instead of falling through to the InvariantCulture parse.
/// </summary>
public sealed class GoalSeekRequestParserLocaleTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void Parse_UnderCommaDecimalCulture_DoesNotMisreadDotDecimalAsGroupedInteger()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        var result = GoalSeekRequestParser.Parse(
            SheetId,
            "B2",
            "1.5",
            "A1");

        result.Success.Should().BeTrue();
        result.Request.Should().NotBeNull();
        result.Request!.TargetValue.Should().Be(1.5, "the InvariantCulture fallback must win once the" +
            " CurrentCulture parse's bogus 3-digit-group shape is rejected");
    }

    [Fact]
    public void Parse_UnderCommaDecimalCulture_StillAcceptsGenuineCurrentCultureGroupedNumber()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        var result = GoalSeekRequestParser.Parse(
            SheetId,
            "B2",
            "1.234,5",
            "A1");

        result.Success.Should().BeTrue();
        result.Request.Should().NotBeNull();
        result.Request!.TargetValue.Should().Be(1234.5, "a well-formed de-DE grouped number (3-digit" +
            " groups) must still parse via CurrentCulture, not be rejected alongside the malformed case");
    }

    [Fact]
    public void Parse_UnderCommaDecimalCulture_StillAcceptsPlainCurrentCultureDecimal()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        var result = GoalSeekRequestParser.Parse(
            SheetId,
            "B2",
            "12,5",
            "A1");

        result.Success.Should().BeTrue();
        result.Request.Should().NotBeNull();
        result.Request!.TargetValue.Should().Be(12.5);
    }
}
