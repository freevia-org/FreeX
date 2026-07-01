using FluentAssertions;
using FreeX.App.Presentation.TableUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TableUI;

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
                out var issue)
            .Should().BeTrue(issue.ToString());

        result.Range.Should().Be(new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 12, 3)));
        result.FirstRowHasHeaders.Should().BeFalse();
        result.TableStyleName.Should().Be("TableStyleMedium2");
        issue.Should().Be(CreateTableInputParseIssue.None);
    }

    [Theory]
    [InlineData("", CreateTableInputParseIssue.MissingRange)]
    [InlineData("A1", CreateTableInputParseIssue.MinimumRows)]
    [InlineData("A1:C1", CreateTableInputParseIssue.MinimumRows)]
    [InlineData("bad", CreateTableInputParseIssue.InvalidRange)]
    public void TryParse_RejectsInvalidTableRange(string rangeText, CreateTableInputParseIssue expectedIssue)
    {
        CreateTableInputParser.TryParse(
                SheetId,
                rangeText,
                firstRowHasHeaders: true,
                tableStyleName: "TableStyleMedium2",
                out _,
                out var issue)
            .Should().BeFalse();

        issue.Should().Be(expectedIssue);
    }
}
