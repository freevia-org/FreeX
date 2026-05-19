using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class CreateTableInputParserTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void TryParse_ParsesRangeHeaderFlagAndTrimmedStyle()
    {
        CreateTableInputParser.TryParse(
                SheetId,
                " A1:C12 ",
                firstRowHasHeaders: false,
                tableStyleName: " TableStyleMedium2 ",
                out var result,
                out var error)
            .Should().BeTrue(error);

        result.Range.Should().Be(new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 12, 3)));
        result.FirstRowHasHeaders.Should().BeFalse();
        result.TableStyleName.Should().Be("TableStyleMedium2");
    }

    [Theory]
    [InlineData("", "Enter a table range.")]
    [InlineData("A1", "Table range must include at least two rows.")]
    [InlineData("A1:C1", "Table range must include at least two rows.")]
    [InlineData("bad", "Enter a valid table range.")]
    public void TryParse_RejectsInvalidTableRange(string rangeText, string expectedError)
    {
        CreateTableInputParser.TryParse(
                SheetId,
                rangeText,
                firstRowHasHeaders: true,
                tableStyleName: "TableStyleMedium2",
                out _,
                out var error)
            .Should().BeFalse();

        error.Should().Be(expectedError);
    }
}
