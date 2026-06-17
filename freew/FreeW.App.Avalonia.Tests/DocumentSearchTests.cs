using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public class DocumentSearchTests
{
    private static TextDocument TwoParagraphs()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("the quick brown fox"));
        doc.Blocks.Add(new Paragraph("jumps over the lazy dog"));
        return doc;
    }

    [Fact]
    public void Finds_first_occurrence_from_the_start()
    {
        var match = DocumentSearch.FindNext(TwoParagraphs(), "the", 0, 0);
        match.Should().Be(new DocumentSearch.Match(0, 0, 3));
    }

    [Fact]
    public void Finds_the_next_occurrence_after_the_cursor()
    {
        var match = DocumentSearch.FindNext(TwoParagraphs(), "the", 0, 3);
        match.Should().Be(new DocumentSearch.Match(1, 11, 3));
    }

    [Fact]
    public void Wraps_around_to_an_earlier_block()
    {
        var match = DocumentSearch.FindNext(TwoParagraphs(), "quick", 1, 0);
        match.Should().Be(new DocumentSearch.Match(0, 4, 5));
    }

    [Fact]
    public void Is_case_insensitive()
    {
        var match = DocumentSearch.FindNext(TwoParagraphs(), "QUICK", 0, 0);
        match.Should().Be(new DocumentSearch.Match(0, 4, 5));
    }

    [Fact]
    public void Returns_null_when_absent()
    {
        DocumentSearch.FindNext(TwoParagraphs(), "zebra", 0, 0).Should().BeNull();
    }
}
