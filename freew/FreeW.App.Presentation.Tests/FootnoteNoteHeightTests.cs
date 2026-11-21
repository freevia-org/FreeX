using FreeW.Core.Model;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// The height a footnote band reserves must follow Word's note layout: every paragraph of a note (an
/// empty trailing one included) is a line of its own, so a two-paragraph note is taller than the same
/// text laid out as one paragraph.
/// </summary>
public sealed class FootnoteNoteHeightTests
{
    private static double HeightOf(params string[] noteParagraphs)
    {
        var document = new TextDocument();
        var note = new Footnote(1);
        foreach (var text in noteParagraphs)
            note.Content.Add(new Paragraph(text));
        document.Footnotes[1] = note;
        return DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [1], 1, 600).Rows.Single().EstimatedHeightDip;
    }

    [Fact]
    public void NoteParagraphs_EachTakeALineOfTheirOwn()
    {
        var oneLine = HeightOf("Short note text");
        HeightOf("Short note text", "Second paragraph").Should().BeApproximately(oneLine * 2, 0.001);
    }

    [Fact]
    public void NoteWithTrailingEmptyParagraph_CountsTheBlankLine()
    {
        var oneLine = HeightOf("Short note text");
        HeightOf("Short note text", string.Empty).Should().BeApproximately(oneLine * 2, 0.001,
            "Word keeps the blank trailing note paragraph, which takes a whole line above the page bottom");
    }

    [Fact]
    public void RegionHeight_IsNotRoundedUpToWholeDips()
    {
        var document = new TextDocument();
        document.Footnotes[1] = new Footnote(1, "Short note text");
        var region = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [1], 1, 600);
        var expected = region.SeparatorHeightDip + region.Rows.Sum(row => row.EstimatedHeightDip + region.RowGapDip);
        region.EstimatedHeightDip.Should().BeApproximately(expected, 0.0001);
    }
}
