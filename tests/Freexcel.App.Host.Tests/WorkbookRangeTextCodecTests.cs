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
}
