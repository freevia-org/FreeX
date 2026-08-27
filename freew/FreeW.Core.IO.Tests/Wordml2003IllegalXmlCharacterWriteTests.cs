using System.IO;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// The surrogate half of the rule <see cref="Wordml2003ControlCharSanitizationTests"/> covers for C0
/// control codes: a lone UTF-16 surrogate is just as unrepresentable in XML 1.0 as a control character,
/// and reaches run text the same way (a paste or an import), but it fails inside the UTF-8 encoder
/// rather than the character check -- so it needs its own coverage. The paired case is the control that
/// says sanitizing does not damage real text: a surrogate PAIR is an ordinary character (an emoji) and
/// must survive untouched.
/// </summary>
public class Wordml2003IllegalXmlCharacterWriteTests
{
    private const string LoneHighSurrogate = "\ud83d";

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
