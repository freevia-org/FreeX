using System.IO;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Run text can legitimately hold characters XML 1.0 cannot represent -- a C0 control code or a lone
/// UTF-16 surrogate arrives by pasting from another application or by importing a file, and nothing in
/// the editor rejects it. <see cref="Wordml2003Writer"/> serializes the whole document in one pass, so
/// one such character anywhere in it made the write throw <see cref="System.ArgumentException"/> and
/// took the entire save down with no file written -- the user lost the save, not the character.
/// <see cref="Free.Shared.Opc.OoxmlXmlText"/> now drops them at the writer's serialization boundary,
/// which is what DOCX and PPTX already do.
/// </summary>
public class Wordml2003IllegalXmlCharacterWriteTests
{
    private const string Control = "\u0001";
    private const string LoneHighSurrogate = "\ud83d";

    [Fact]
    public void Write_WithControlCharacterInRunText_SucceedsAndDropsTheCharacter()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("Quarterly" + Control + " report"));

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>()
            .Select(p => p.PlainText)
            .Should().Contain("Quarterly report");
    }

    [Fact]
    public void Write_WithLoneSurrogateInRunText_SucceedsAndDropsTheCharacter()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("Sales" + LoneHighSurrogate + " 2026"));

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>()
            .Select(p => p.PlainText)
            .Should().Contain("Sales 2026");
    }

    // A valid surrogate PAIR is a real character (an emoji), not a defect: it must survive untouched.
    [Fact]
    public void Write_WithEmojiInRunText_PreservesTheEmoji()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("Revenue \U0001F4C8"));

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>()
            .Select(p => p.PlainText)
            .Should().Contain("Revenue \U0001F4C8");
    }

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        Wordml2003Writer.Write(document, stream);
        stream.Position = 0;
        return Wordml2003Reader.Read(stream);
    }
}
