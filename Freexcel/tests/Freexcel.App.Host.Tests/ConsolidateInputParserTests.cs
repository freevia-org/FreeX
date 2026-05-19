using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ConsolidateInputParserTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void TryParseSourceRanges_ParsesCommaAndSemicolonSeparatedRanges()
    {
        ConsolidateInputParser.TryParseSourceRanges("A1:B2; C3:D4", SheetId, out var ranges, out var invalidPart)
            .Should().BeTrue();

        invalidPart.Should().BeNull();
        ranges.Should().HaveCount(2);
        ranges[0].Should().Be(new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 2, 2)));
        ranges[1].Should().Be(new GridRange(new CellAddress(SheetId, 3, 3), new CellAddress(SheetId, 4, 4)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("A1:B2; nope")]
    public void TryParseSourceRanges_RejectsBlankOrMalformedSourceRanges(string input)
    {
        ConsolidateInputParser.TryParseSourceRanges(input, SheetId, out var ranges, out var invalidPart)
            .Should().BeFalse();

        ranges.Should().BeEmpty();
        invalidPart.Should().NotBeNull();
    }

    [Theory]
    [InlineData("C5", true, 5, 3)]
    [InlineData("bad", false, 0, 0)]
    public void TryParseDestination_ParsesDestinationCell(string input, bool expected, uint row, uint col)
    {
        var result = ConsolidateInputParser.TryParseDestination(input, SheetId, out var destination);

        result.Should().Be(expected);
        if (expected)
            destination.Should().Be(new CellAddress(SheetId, row, col));
    }
}
