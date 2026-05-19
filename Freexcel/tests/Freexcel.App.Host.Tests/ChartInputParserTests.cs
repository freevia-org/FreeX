using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ChartInputParserTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Theory]
    [InlineData("A1:D12", true, 1, 1, 12, 4)]
    [InlineData(" B2:A1 ", true, 1, 1, 2, 2)]
    [InlineData("A1", false, 0, 0, 0, 0)]
    [InlineData("A1:B2:C3", false, 0, 0, 0, 0)]
    [InlineData("bad", false, 0, 0, 0, 0)]
    public void TryParseDataRange_ParsesChartSourceRangeWithoutThrowing(
        string input,
        bool expected,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol)
    {
        var result = ChartInputParser.TryParseDataRange(input, SheetId, out var range);

        result.Should().Be(expected);
        if (expected)
        {
            range.Start.Should().Be(new CellAddress(SheetId, startRow, startCol));
            range.End.Should().Be(new CellAddress(SheetId, endRow, endCol));
        }
    }
}
