using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class WorkbookRangeTextCodecTests
{
    [Fact]
    public void TryParse_UsesDefaultSheetForUnqualifiedRanges()
    {
        var sheetId = SheetId.New();

        WorkbookRangeTextCodec.TryParse(sheetId, "A1:B2", _ => null, out var range).Should().BeTrue();

        range.Start.Should().Be(new CellAddress(sheetId, 1, 1));
        range.End.Should().Be(new CellAddress(sheetId, 2, 2));
    }

    [Fact]
    public void TryParse_ResolvesQuotedSheetNames()
    {
        var defaultSheetId = SheetId.New();
        var salesSheetId = SheetId.New();

        WorkbookRangeTextCodec.TryParse(
            defaultSheetId,
            "'Sales Q1'!C3:D4",
            name => string.Equals(name, "Sales Q1", StringComparison.CurrentCultureIgnoreCase) ? salesSheetId : null,
            out var range).Should().BeTrue();

        range.Start.Should().Be(new CellAddress(salesSheetId, 3, 3));
        range.End.Should().Be(new CellAddress(salesSheetId, 4, 4));
    }

    [Theory]
    [InlineData("'Missing'!A1")]
    [InlineData("A1:B2:C3")]
    [InlineData("not a range")]
    public void TryParse_RejectsUnknownSheetsAndMalformedRanges(string input)
    {
        WorkbookRangeTextCodec.TryParse(SheetId.New(), input, _ => null, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseOnCurrentSheet_ParsesUnqualifiedRanges()
    {
        var sheetId = SheetId.New();

        WorkbookRangeTextCodec.TryParseOnCurrentSheet(sheetId, "C3:D4", out var range).Should().BeTrue();

        range.Start.Should().Be(new CellAddress(sheetId, 3, 3));
        range.End.Should().Be(new CellAddress(sheetId, 4, 4));
    }

    [Fact]
    public void TryParseOnCurrentSheet_RejectsSheetQualifiedRanges()
    {
        WorkbookRangeTextCodec.TryParseOnCurrentSheet(SheetId.New(), "Other!A1:B2", out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TryParseMany_AcceptsCommaSeparatedRangesAcrossSheets()
    {
        var defaultSheetId = SheetId.New();
        var outputsSheetId = SheetId.New();

        WorkbookRangeTextCodec.TryParseMany(
            defaultSheetId,
            "B2,'Output, Sheet'!D5:E5",
            name => name == "Output, Sheet" ? outputsSheetId : null,
            out var ranges).Should().BeTrue();

        ranges.Should().HaveCount(2);
        ranges[0].Start.Should().Be(new CellAddress(defaultSheetId, 2, 2));
        ranges[0].End.Should().Be(new CellAddress(defaultSheetId, 2, 2));
        ranges[1].Start.Should().Be(new CellAddress(outputsSheetId, 5, 4));
        ranges[1].End.Should().Be(new CellAddress(outputsSheetId, 5, 5));
    }

    [Fact]
    public void TryParseMany_KeepsEscapedQuotesInsideQuotedSheetNames()
    {
        var defaultSheetId = SheetId.New();
        var quotedSheetId = SheetId.New();

        WorkbookRangeTextCodec.TryParseMany(
            defaultSheetId,
            "'Bob''s, Sheet'!A1,B2",
            name => name == "Bob's, Sheet" ? quotedSheetId : null,
            out var ranges).Should().BeTrue();

        ranges.Should().HaveCount(2);
        ranges[0].Start.Should().Be(new CellAddress(quotedSheetId, 1, 1));
        ranges[0].End.Should().Be(new CellAddress(quotedSheetId, 1, 1));
        ranges[1].Start.Should().Be(new CellAddress(defaultSheetId, 2, 2));
        ranges[1].End.Should().Be(new CellAddress(defaultSheetId, 2, 2));
    }

    [Fact]
    public void TryParseMany_RejectsIfAnyRangeIsMalformed()
    {
        WorkbookRangeTextCodec.TryParseMany(SheetId.New(), "A1,not a range", _ => null, out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Format_OmitsCurrentSheetName()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2));

        WorkbookRangeTextCodec.Format(range, sheetId, _ => "Sheet1").Should().Be("A1:B2");
    }

    [Fact]
    public void Format_QuotesOtherSheetNames()
    {
        var currentSheetId = SheetId.New();
        var salesSheetId = SheetId.New();
        var range = new GridRange(new CellAddress(salesSheetId, 1, 1), new CellAddress(salesSheetId, 2, 2));

        WorkbookRangeTextCodec.Format(
                range,
                currentSheetId,
                sheetId => sheetId.Equals(salesSheetId) ? "Sales Q1" : null)
            .Should().Be("'Sales Q1'!A1:B2");
    }

    [Fact]
    public void Format_EscapesQuotesInOtherSheetNames()
    {
        var currentSheetId = SheetId.New();
        var quotedSheetId = SheetId.New();
        var range = new GridRange(new CellAddress(quotedSheetId, 1, 1), new CellAddress(quotedSheetId, 1, 1));

        WorkbookRangeTextCodec.Format(
                range,
                currentSheetId,
                sheetId => sheetId.Equals(quotedSheetId) ? "Bob's Sheet" : null)
            .Should().Be("'Bob''s Sheet'!A1:A1");
    }
}
