using System.IO.Compression;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

public sealed class NoteEditCommandRoundTripTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    [Fact]
    public void EditedFootnoteAndEndnote_RoundTripInPackageAndReopenedModel()
    {
        var document = TextDocument.CreateEmpty();
        document.Footnotes[1] = new Footnote(1, "old footnote");
        document.Footnotes[2] = new Footnote(2, "untouched footnote");
        document.Endnotes[3] = new Endnote(3, "old endnote");
        var bus = new DocumentCommandBus(new Context(document));

        var richFootnote = new Paragraph();
        richFootnote.Runs.Add(new Run("edited footnote")
        {
            Formatting = RunFormatting.Default with { Bold = true },
        });
        bus.Execute(new ReplaceNoteContentCommand(1, footnote: true, [richFootnote, new Paragraph("second paragraph")]));
        bus.Execute(new ReplaceNoteContentCommand(3, footnote: false, [new Paragraph("edited endnote")]));

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        var package = stream.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            ReadEntry(archive, "word/footnotes.xml").Should()
                .Contain("edited footnote")
                .And.Contain("second paragraph")
                .And.Contain("untouched footnote")
                .And.Contain("<w:b");
            ReadEntry(archive, "word/endnotes.xml").Should().Contain("edited endnote");
        }

        var reopened = DocxReader.Read(new MemoryStream(package));
        reopened.Footnotes[1].Content.Select(paragraph => paragraph.PlainText)
            .Should().Equal("edited footnote", "second paragraph");
        reopened.Footnotes[1].Content[0].Runs.Single().Formatting.Bold.Should().BeTrue();
        reopened.Footnotes[2].PlainText.Should().Be("untouched footnote");
        reopened.Endnotes[3].PlainText.Should().Be("edited endnote");
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using var reader = new StreamReader(archive.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }
}
