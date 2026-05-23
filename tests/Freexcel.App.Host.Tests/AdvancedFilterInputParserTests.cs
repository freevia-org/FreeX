using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class AdvancedFilterInputParserTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Theory]
    [InlineData("A1:C10", true, "A1", "C10")]
    [InlineData(" B2 ", true, "B2", "B2")]
    [InlineData("Missing!A1:B2", false, "", "")]
    [InlineData("A1:B2:C3", false, "", "")]
    public void TryParseRange_ParsesWorkbookRangeText(
        string input,
        bool expected,
        string expectedStart,
        string expectedEnd)
    {
        var result = AdvancedFilterInputParser.TryParseRange(
            SheetId,
            input,
            sheetName => string.Equals(sheetName, "Sheet1", StringComparison.OrdinalIgnoreCase) ? SheetId : null,
            out var range);

        result.Should().Be(expected);
        if (expected)
        {
            range.Start.ToA1().Should().Be(expectedStart);
            range.End.ToA1().Should().Be(expectedEnd);
        }
    }

    [Fact]
    public void TryParseRange_ParsesSheetQualifiedRange()
    {
        AdvancedFilterInputParser.TryParseRange(
            SheetId.New(),
            "Sheet1!A1:B2",
            sheetName => string.Equals(sheetName, "Sheet1", StringComparison.OrdinalIgnoreCase) ? SheetId : null,
            out var range).Should().BeTrue();

        range.Start.Sheet.Should().Be(SheetId);
        range.Start.ToA1().Should().Be("A1");
        range.End.ToA1().Should().Be("B2");
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("   ", true, null)]
    [InlineData("D4", true, "D4")]
    [InlineData("ZZ1", true, "ZZ1")]
    [InlineData("A1:B2", false, null)]
    [InlineData("bad", false, null)]
    public void TryParseCopyDestination_AllowsBlankOrSingleCellAddress(string input, bool expected, string? expectedAddress)
    {
        var result = AdvancedFilterInputParser.TryParseCopyDestination(input, SheetId, out var address);

        result.Should().Be(expected);
        address?.ToA1().Should().Be(expectedAddress);
    }

    [Theory]
    [InlineData("A8:C8", true, "A8", "C8")]
    [InlineData("D4", true, "D4", "D4")]
    [InlineData("A8:C9", false, "", "")]
    [InlineData("bad", false, "", "")]
    public void TryParseCopyDestinationRange_AllowsCellOrSingleRowHeaderRange(
        string input,
        bool expected,
        string expectedStart,
        string expectedEnd)
    {
        var result = AdvancedFilterInputParser.TryParseCopyDestinationRange(input, SheetId, out var range);

        result.Should().Be(expected);
        if (expected && range is not null)
        {
            range.Value.Start.ToA1().Should().Be(expectedStart);
            range.Value.End.ToA1().Should().Be(expectedEnd);
        }
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("Y", true)]
    [InlineData("true", true)]
    [InlineData("no", false)]
    [InlineData("false", false)]
    [InlineData("", false)]
    public void ParseUniqueOnly_MatchesExcelStyleAffirmativePromptAliases(string input, bool expected)
    {
        AdvancedFilterInputParser.ParseUniqueOnly(input).Should().Be(expected);
    }
}
