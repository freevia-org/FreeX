using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

public sealed class ClipboardCsvTextRendererTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("A\tB", "A,B")]
    [InlineData("A\tB\nC\tD", "A,B\r\nC,D")]
    [InlineData("A\tB\rC\tD", "A,B\r\nC,D")]
    [InlineData("A\tB\r\nC\tD\r\n", "A,B\r\nC,D")]
    [InlineData("\t\r\n\tValue", ",\r\n,Value")]
    public void Render_PreservesEmptyInputAndNormalizesRowSeparators(string? tsvText, string expected)
    {
        ClipboardCsvTextRenderer.Render(tsvText).Should().Be(expected);
    }

    [Theory]
    [InlineData("Smith, John\tPlain", "\"Smith, John\",Plain")]
    [InlineData("He said \"hi\"", "\"He said \"\"hi\"\"\"")]
    [InlineData("\"Line 1\rLine 2\"", "\"Line 1\rLine 2\"")]
    [InlineData("\"Line 1\nLine 2\"", "\"Line 1\nLine 2\"")]
    [InlineData("\"Line 1\r\nLine 2\"", "\"Line 1\r\nLine 2\"")]
    public void Render_QuotesCsvSpecialCharacters(string tsvText, string expected)
    {
        ClipboardCsvTextRenderer.Render(tsvText).Should().Be(expected);
    }

    [Theory]
    [InlineData("Left 1\t\tRight, 1\r\nLeft 2\t\tRight 2", "Left 1,,\"Right, 1\"\r\nLeft 2,,Right 2")]
    [InlineData("Top 1\tTop 2\r\n\t\r\nBottom 1\tBottom 2", "Top 1,Top 2\r\n,\r\nBottom 1,Bottom 2")]
    public void Render_MultiRangeBoundingBlock_PreservesHorizontalAndVerticalGaps(string tsvText, string expected)
    {
        ClipboardCsvTextRenderer.Render(tsvText).Should().Be(expected);
    }

    [Fact]
    public void Render_UsesClipboardSerializerDisplayTextWithoutReformattingValues()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [
                new DisplayCell(1, 1, new NumberValue(1234), "$1,234.00", null, default, null),
                new DisplayCell(1, 2, new TextValue("00501"), "00501", null, default, null)
            ],
            [],
            []);
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 1, 2));

        var tsvText = ClipboardSerializer.Serialize(viewport, range);

        tsvText.Should().Be("$1,234.00\t'00501");
        ClipboardCsvTextRenderer.Render(tsvText).Should().Be("\"$1,234.00\",'00501");
    }
}
