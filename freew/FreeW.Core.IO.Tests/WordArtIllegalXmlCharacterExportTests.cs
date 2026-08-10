using System.IO;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Every text-emitting site in the DOCX writer routes through its XML sanitizer except WordArt,
/// which was simply missed. WordArt text is user-typed like any other run, so a pasted C0 control
/// character or lone surrogate reached XmlWriter and aborted the whole save with an
/// ArgumentException — losing the document rather than the character.
/// </summary>
public class WordArtIllegalXmlCharacterExportTests
{
    private const string VerticalTab = "";

    private static TextDocument DocumentWithWordArt(string text)
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromWordArt(new WordArt(text, WordArtStyle.Shadow, 36)));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var buffer = new MemoryStream();
        DocxWriter.Write(document, buffer);
        buffer.Position = 0;
        return DocxReader.Read(buffer);
    }

    [Fact]
    public void Write_WordArtTextWithControlCharacter_SavesWithTheCharacterDropped()
    {
        var recovered = RoundTrip(DocumentWithWordArt("Ban" + VerticalTab + "ner"));

        var wordArt = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.WordArt is not null).WordArt!;
        wordArt.Text.Should().Be("Banner");
    }

    [Fact]
    public void Write_WordArtTextWithLoneSurrogate_DoesNotThrow()
    {
        var document = DocumentWithWordArt("Ban\uD83Dner");

        using var buffer = new MemoryStream();
        var write = () => DocxWriter.Write(document, buffer);

        write.Should().NotThrow();
    }

    [Fact]
    public void Write_OrdinaryWordArtText_IsUnchanged()
    {
        var recovered = RoundTrip(DocumentWithWordArt("Banner \U0001F600"));

        ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.WordArt is not null).WordArt!
            .Text.Should().Be("Banner \U0001F600");
    }
}
