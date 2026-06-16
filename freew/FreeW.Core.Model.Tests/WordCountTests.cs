namespace FreeW.Core.Model.Tests;

public class WordCountTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("hello", 1)]
    [InlineData("hello world", 2)]
    [InlineData("  hello   world  ", 2)]
    [InlineData("one\ttwo\nthree", 3)]
    public void Words_CountsNonWhitespaceRuns(string? text, int expected)
    {
        WordCount.Words(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("hello world", true, 11)]
    [InlineData("hello world", false, 10)]
    [InlineData("  a b  ", true, 7)]
    [InlineData("  a b  ", false, 2)]
    [InlineData("", true, 0)]
    [InlineData(null, false, 0)]
    public void Characters_CountsWithOrWithoutSpaces(string? text, bool includeSpaces, int expected)
    {
        WordCount.Characters(text, includeSpaces).Should().Be(expected);
    }

    [Fact]
    public void Of_EmptyDocument_IsZeroWordsAndChars()
    {
        var doc = new TextDocument();

        var stats = WordCount.Of(doc);

        stats.Words.Should().Be(0);
        stats.CharactersWithSpaces.Should().Be(0);
        stats.CharactersWithoutSpaces.Should().Be(0);
        stats.Paragraphs.Should().Be(0);
    }

    [Fact]
    public void Of_SingleEmptyParagraph_CountsOneParagraph()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph());

        var stats = WordCount.Of(doc);

        stats.Words.Should().Be(0);
        stats.CharactersWithSpaces.Should().Be(0);
        stats.Paragraphs.Should().Be(1);
    }

    [Fact]
    public void Of_MultipleParagraphs_SumsWordsAndCountsParagraphs()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Hello world"));
        doc.Blocks.Add(new Paragraph("Three little words"));

        var stats = WordCount.Of(doc);

        stats.Words.Should().Be(5);
        stats.Paragraphs.Should().Be(2);
        // "Hello world" (11) + "Three little words" (18) = 29 with spaces.
        stats.CharactersWithSpaces.Should().Be(29);
        // Excludes the 1 + 2 internal spaces.
        stats.CharactersWithoutSpaces.Should().Be(26);
    }

    [Fact]
    public void Of_MultiRunParagraph_CountsAcrossRuns()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Free"));
        paragraph.Runs.Add(new Run("W "));
        paragraph.Runs.Add(new Run("rocks"));
        var doc = new TextDocument();
        doc.Blocks.Add(paragraph);

        var stats = WordCount.Of(doc);

        // Concatenated text is "FreeW rocks" -> 2 words.
        stats.Words.Should().Be(2);
        stats.Paragraphs.Should().Be(1);
        stats.CharactersWithSpaces.Should().Be(11);
        stats.CharactersWithoutSpaces.Should().Be(10);
    }

    [Fact]
    public void Of_IncludesTableCellParagraphs()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));

        var table = new Table();
        var row = new TableRow();
        row.Cells.Add(new TableCell("cell one"));
        row.Cells.Add(new TableCell("cell two words"));
        table.Rows.Add(row);
        doc.Blocks.Add(table);

        var stats = WordCount.Of(doc);

        // "Body text" (2) + "cell one" (2) + "cell two words" (3) = 7 words.
        stats.Words.Should().Be(7);
        // 1 body paragraph + 2 table-cell paragraphs.
        stats.Paragraphs.Should().Be(3);
    }
}
