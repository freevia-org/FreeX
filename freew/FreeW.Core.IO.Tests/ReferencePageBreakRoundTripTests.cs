namespace FreeW.Core.IO.Tests;

public sealed class ReferencePageBreakRoundTripTests
{
    [Fact]
    public void EmptyPageBreakAfterCitation_RemainsAfterTheCitationRun()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var citation = new Paragraph();
        citation.Runs.Add(new Run("Citation: "));
        citation.Runs.Add(Run.ComplexFieldRun(" CITATION Knuth1997 ", "[1]"));
        document.Blocks.Add(citation);
        document.Blocks.Add(DocumentOps.CreatePageBreak());
        document.Blocks.Add(new Paragraph("Page two"));

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);
        var paragraphs = read.Blocks.OfType<Paragraph>().ToArray();

        paragraphs.Should().HaveCount(3);
        paragraphs[0].Formatting.PageBreakBefore.Should().BeFalse();
        paragraphs[1].Runs.Should().BeEmpty();
        paragraphs[1].Formatting.PageBreakBefore.Should().BeTrue();
        paragraphs[2].PlainText.Should().Be("Page two");
    }
}
