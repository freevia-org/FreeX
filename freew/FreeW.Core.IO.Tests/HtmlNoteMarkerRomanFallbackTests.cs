using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// HTML export labels notes with the document's own numbering format. A roman numeral has no
/// classic form past 3999; the export used to CLAMP there, printing "MMMCMXCIX" for note 4000 (a
/// wrong label). Browsers (CSS <c>upper-roman</c>, range 1..3999) and the .docx-side
/// NoteNumberFormatter fall back to the decimal number instead, and so does this now.
/// </summary>
public sealed class HtmlNoteMarkerRomanFallbackTests
{
    private static string ExportFootnoteLabel(NoteNumberFormat format, int startAt)
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        var paragraph = new Paragraph("Body ");
        paragraph.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(paragraph);
        document.Footnotes[1] = new Footnote(1) { Content = { new Paragraph("Note text") } };
        document.FootnoteNumbering.NumberFormat = format;
        document.FootnoteNumbering.StartAt = startAt;

        using var stream = new MemoryStream();
        HtmlFileAdapter.WebPage().Save(document, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void RomanNoteLabelAtTheClassicLimitStaysRoman()
    {
        ExportFootnoteLabel(NoteNumberFormat.UpperRoman, 3999)
            .Should().Contain("data-freew-note-id=\"1\"><sup>MMMCMXCIX</sup>");
    }

    [Theory]
    [InlineData(NoteNumberFormat.UpperRoman, 4000)]
    [InlineData(NoteNumberFormat.LowerRoman, 4000)]
    [InlineData(NoteNumberFormat.UpperRoman, 2_000_000_000)]
    public void RomanNoteLabelPastTheLimitFallsBackToDecimalInsteadOfAWrongClampedNumeral(
        NoteNumberFormat format, int startAt)
    {
        var html = ExportFootnoteLabel(format, startAt);

        html.Should().Contain($"data-freew-note-id=\"1\"><sup>{startAt}</sup>");
        html.Should().NotContain("<sup>MMMCMXCIX</sup>").And.NotContain("<sup>mmmcmxcix</sup>");
    }
}
