using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class SparklineInputParserTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Theory]
    [InlineData("A1:E1", true, 1, 1, 1, 5)]
    [InlineData("bad", false, 0, 0, 0, 0)]
    public void TryParseDataRange_ParsesSparklineSourceRange(string input, bool expected, uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var result = SparklineInputParser.TryParseDataRange(input, SheetId, out var range);

        result.Should().Be(expected);
        if (expected)
            range.Should().Be(new GridRange(new CellAddress(SheetId, startRow, startCol), new CellAddress(SheetId, endRow, endCol)));
    }

    [Theory]
    [InlineData("F1", true, 1, 6)]
    [InlineData("bad", false, 0, 0)]
    public void TryParseLocation_ParsesSparklineLocationCell(string input, bool expected, uint row, uint col)
    {
        var result = SparklineInputParser.TryParseLocation(input, SheetId, out var location);

        result.Should().Be(expected);
        if (expected)
            location.Should().Be(new CellAddress(SheetId, row, col));
    }

    [Theory]
    [InlineData("column", SparklineKind.Column)]
    [InlineData("winloss", SparklineKind.WinLoss)]
    [InlineData("line", SparklineKind.Line)]
    [InlineData("anything", SparklineKind.Line)]
    public void ParseKind_MapsToolbarKindText(string input, SparklineKind expected)
    {
        SparklineInputParser.ParseKind(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("column", SparklineKindChoice.Column)]
    [InlineData("winloss", SparklineKindChoice.WinLoss)]
    [InlineData("line", SparklineKindChoice.Line)]
    [InlineData("anything", SparklineKindChoice.Line)]
    public void ParseDialogKindChoice_MapsToolbarKindText(string input, SparklineKindChoice expected)
    {
        SparklineInputParser.ParseDialogKindChoice(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(SparklineKindChoice.Column, SparklineKind.Column)]
    [InlineData(SparklineKindChoice.WinLoss, SparklineKind.WinLoss)]
    [InlineData(SparklineKindChoice.Line, SparklineKind.Line)]
    public void ToModelKind_MapsDialogChoiceToCoreKind(SparklineKindChoice input, SparklineKind expected)
    {
        SparklineInputParser.ToModelKind(input).Should().Be(expected);
    }
}
