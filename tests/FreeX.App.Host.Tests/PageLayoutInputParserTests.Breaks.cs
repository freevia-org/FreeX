using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class PageLayoutInputParserTests
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
    [InlineData("Column C", "column", true, 3)]
    [InlineData("col AA", "col", true, 27)]
    [InlineData("col 12", "col", true, 12)]
    [InlineData("row C", "row", false, 0)]
    [InlineData("column A1", "column", false, 0)]
    public void TryParseColumnBreakInput_AcceptsExcelColumnLettersOrNumbers(
        string input,
        string keyword,
        bool expected,
        uint expectedValue)
    {
        var result = PageLayoutInputParser.TryParseColumnBreakInput(input, keyword, out var value);

        result.Should().Be(expected);
        value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("clear", PageBreakInputKind.Clear, null, null)]
    [InlineData("row 5", PageBreakInputKind.Row, 5, null)]
    [InlineData("col 3", PageBreakInputKind.Column, null, 3)]
    [InlineData("col C", PageBreakInputKind.Column, null, 3)]
    [InlineData("column 7", PageBreakInputKind.Column, null, 7)]
    [InlineData("column AA", PageBreakInputKind.Column, null, 27)]
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
    [InlineData("row 0")]
    [InlineData("row 1048577")]
    [InlineData("col 0")]
    [InlineData("col 16385")]
    [InlineData("column XFE")]
    [InlineData("columns 4")]
    [InlineData("break")]
    public void TryParsePageBreakInput_RejectsMalformedCommands(string input)
    {
        PageLayoutInputParser.TryParsePageBreakInput(input, out _).Should().BeFalse();
    }
}
