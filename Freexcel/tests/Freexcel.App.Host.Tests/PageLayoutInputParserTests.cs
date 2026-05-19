using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class PageLayoutInputParserTests
{
    [Theory]
    [InlineData("row 4", "row", true, 4)]
    [InlineData("Column 12", "column", true, 12)]
    [InlineData("col x", "col", false, 0)]
    [InlineData("rows 4", "row", false, 0)]
    public void TryParseBreakInput_ParsesKeywordAndNumber(string input, string keyword, bool expected, uint expectedValue)
    {
        var result = PageLayoutInputParser.TryParseBreakInput(input, keyword, out var value);

        result.Should().Be(expected);
        value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("1:2", true, 1, 2)]
    [InlineData("4", true, 4, 4)]
    [InlineData("5:3", true, 3, 5)]
    [InlineData("none", true, null, null)]
    [InlineData("0:2", false, null, null)]
    [InlineData("A:C", false, null, null)]
    public void TryParseRepeatRows_ParsesExcelStyleRowRanges(string input, bool expected, int? expectedStart, int? expectedEnd)
    {
        var result = PageLayoutInputParser.TryParseRepeatRows(input, out var range);

        result.Should().Be(expected);
        AssertRange(range, expectedStart, expectedEnd);
    }

    [Theory]
    [InlineData("A:C", true, 1, 3)]
    [InlineData("D", true, 4, 4)]
    [InlineData("C:A", true, 1, 3)]
    [InlineData("clear", true, null, null)]
    [InlineData("1:2", false, null, null)]
    [InlineData("A:B:C", false, null, null)]
    public void TryParseRepeatColumns_ParsesExcelStyleColumnRanges(string input, bool expected, int? expectedStart, int? expectedEnd)
    {
        var result = PageLayoutInputParser.TryParseRepeatColumns(input, out var range);

        result.Should().Be(expected);
        AssertRange(range, expectedStart, expectedEnd);
    }

    private static void AssertRange(WorksheetRepeatRange? range, int? expectedStart, int? expectedEnd)
    {
        if (expectedStart is null)
        {
            range.Should().BeNull();
            return;
        }

        range.Should().NotBeNull();
        range!.Value.Start.Should().Be((uint)expectedStart.Value);
        range.Value.End.Should().Be((uint)expectedEnd!.Value);
    }
}
