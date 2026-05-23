using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class NamedRangeInputParserTests
{
    [Fact]
    public void TryParseRange_ParsesUnqualifiedRangeOnFirstSheet()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");

        NamedRangeInputParser.TryParseRange(workbook, " A1:B2 ", out var range).Should().BeTrue();

        range.Start.Should().Be(new CellAddress(sheet.Id, 1, 1));
        range.End.Should().Be(new CellAddress(sheet.Id, 2, 2));
    }

    [Fact]
    public void TryParseRange_ParsesQuotedSheetQualifiedRange()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        var sheet = workbook.AddSheet("Sales FY26");

        NamedRangeInputParser.TryParseRange(workbook, "'Sales FY26'!C3:D4", out var range).Should().BeTrue();

        range.Start.Should().Be(new CellAddress(sheet.Id, 3, 3));
        range.End.Should().Be(new CellAddress(sheet.Id, 4, 4));
    }

    [Fact]
    public void TryParseRange_RejectsUnknownSheetQualifiedRange()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");

        NamedRangeInputParser.TryParseRange(workbook, "Missing!C3:D4", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    [InlineData("A1:B2:C3")]
    public void TryParseRange_RejectsBlankOrMalformedText(string input)
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");

        NamedRangeInputParser.TryParseRange(workbook, input, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseRange_RejectsWorkbookWithoutSheets()
    {
        NamedRangeInputParser.TryParseRange(new Workbook("Book"), "A1:B2", out _).Should().BeFalse();
    }
}
