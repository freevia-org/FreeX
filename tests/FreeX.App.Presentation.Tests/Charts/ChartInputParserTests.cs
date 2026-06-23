using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartInputParserTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Theory]
    [InlineData("A1:D12", true, 1, 1, 12, 4)]
    [InlineData("$A$1:$D$12", true, 1, 1, 12, 4)]
    [InlineData(" B2:A1 ", true, 1, 1, 2, 2)]
    [InlineData("R1C1:R12C4", true, 1, 1, 12, 4)]
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

    [Fact]
    public void TryParseDataRange_ResolvesSheetQualifiedWorkbookRange()
    {
        var otherSheetId = SheetId.New();

        var result = ChartInputParser.TryParseDataRange(
            "Data!$B$2:$D$6",
            SheetId,
            sheetName => sheetName == "Data" ? otherSheetId : null,
            out var range);

        result.Should().BeTrue();
        range.Start.Should().Be(new CellAddress(otherSheetId, 2, 2));
        range.End.Should().Be(new CellAddress(otherSheetId, 6, 4));
    }
}
