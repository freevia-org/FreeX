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
    [InlineData("clear", PageBreakInputKind.Clear, null, null)]
    [InlineData("row 5", PageBreakInputKind.Row, 5, null)]
    [InlineData("col 3", PageBreakInputKind.Column, null, 3)]
    [InlineData("column 7", PageBreakInputKind.Column, null, 7)]
    public void TryParsePageBreakInput_ParsesClearRowAndColumnCommands(
        string input,
        PageBreakInputKind expectedKind,
        int? expectedRow,
        int? expectedColumn)
    {
        var result = PageLayoutInputParser.TryParsePageBreakInput(input, out var pageBreak);

        result.Should().BeTrue();
        pageBreak.Kind.Should().Be(expectedKind);
        pageBreak.Row.Should().Be(expectedRow is null ? null : (uint)expectedRow.Value);
        pageBreak.Column.Should().Be(expectedColumn is null ? null : (uint)expectedColumn.Value);
    }

    [Theory]
    [InlineData("row x")]
    [InlineData("columns 4")]
    [InlineData("break")]
    public void TryParsePageBreakInput_RejectsMalformedCommands(string input)
    {
        PageLayoutInputParser.TryParsePageBreakInput(input, out _).Should().BeFalse();
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

    [Theory]
    [InlineData(100, null, null, "100")]
    [InlineData(null, 1, 1, "1x1")]
    [InlineData(null, null, null, "1x1")]
    [InlineData(null, 2, 3, "2x3")]
    public void FormatScaleToFit_FormatsPercentOrFitPages(int? percent, int? wide, int? tall, string expected)
    {
        var scaleToFit = new WorksheetScaleToFit(percent, wide, tall);

        PageLayoutInputParser.FormatScaleToFit(scaleToFit).Should().Be(expected);
    }

    [Theory]
    [InlineData("75", true, 75, null, null)]
    [InlineData("400", true, 400, null, null)]
    [InlineData("1x1", true, null, 1, 1)]
    [InlineData("2 x 3", true, null, 2, 3)]
    [InlineData("9", false, null, null, null)]
    [InlineData("401", false, null, null, null)]
    [InlineData("x3", false, null, null, null)]
    [InlineData("2x", false, null, null, null)]
    [InlineData("0x1", false, null, null, null)]
    [InlineData("abc", false, null, null, null)]
    public void TryParseScaleToFit_ParsesExcelScaleText(
        string input,
        bool expected,
        int? expectedPercent,
        int? expectedWide,
        int? expectedTall)
    {
        var result = PageLayoutInputParser.TryParseScaleToFit(input, out var scaleToFit);

        result.Should().Be(expected);
        if (!expected)
        {
            scaleToFit.Should().Be(WorksheetScaleToFit.Default);
            return;
        }

        scaleToFit.ScalePercent.Should().Be(expectedPercent);
        scaleToFit.FitToPagesWide.Should().Be(expectedWide);
        scaleToFit.FitToPagesTall.Should().Be(expectedTall);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("auto", true, null)]
    [InlineData("1", true, 1)]
    [InlineData("-3", true, -3)]
    [InlineData("0", false, null)]
    [InlineData("abc", false, null)]
    public void TryParseOptionalFirstPageNumber_ParsesAutoOrNonZeroIntegers(
        string input,
        bool expected,
        int? expectedValue)
    {
        var result = PageLayoutInputParser.TryParseOptionalFirstPageNumber(input, out var value);

        result.Should().Be(expected);
        value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("0.75", true, 0.75)]
    [InlineData("-0.1", false, 0)]
    [InlineData("NaN", false, 0)]
    [InlineData("Infinity", false, 0)]
    [InlineData("abc", false, 0)]
    public void TryParseMarginDistance_ParsesNonNegativeFiniteDistances(
        string input,
        bool expected,
        double expectedValue)
    {
        var result = PageLayoutInputParser.TryParseMarginDistance(input, out var value);

        result.Should().Be(expected);
        if (expected)
            value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("auto", true, null)]
    [InlineData("300", true, 300)]
    [InlineData("0", false, null)]
    [InlineData("-1", false, null)]
    [InlineData("600.5", false, null)]
    public void TryParseOptionalPrintQuality_ParsesAutoOrPositiveIntegerDpi(
        string input,
        bool expected,
        int? expectedValue)
    {
        var result = PageLayoutInputParser.TryParseOptionalPrintQuality(input, out var value);

        result.Should().Be(expected);
        value.Should().Be(expectedValue);
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
