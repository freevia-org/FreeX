using System.IO;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Slide text, notes, comments, shape names and alt text reach the writer straight from the model,
/// and a user can paste a C0 control character or a lone UTF-16 surrogate into any of them.
/// XmlWriter validates characters on write, so one such character aborted the entire save with an
/// ArgumentException — the user lost the file, not the character. Word and PowerPoint drop these
/// characters; so do we now.
/// </summary>
public sealed class PptxIllegalXmlCharacterExportTests
{
    private const string VerticalTab = "";
    private const string LoneHighSurrogate = "\uD83D";

    private static Presentation WithShapeText(string text)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();

        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);

        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Body",
            Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 685800,
            TextBody = body
        });

        return presentation;
    }

    private static Presentation RoundTrip(Presentation presentation)
    {
        using var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;
        return PptxPackageReader.Read(buffer);
    }

    [Fact]
    public void Write_ShapeTextWithControlCharacter_SavesWithTheCharacterDropped()
    {
        var reloaded = RoundTrip(WithShapeText("before" + VerticalTab + "after"));

        var text = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text;
        text.Should().Be("beforeafter");
    }

    [Fact]
    public void Write_ShapeTextWithLoneSurrogate_SavesWithTheCharacterDropped()
    {
        var reloaded = RoundTrip(WithShapeText("ok" + LoneHighSurrogate));

        reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("ok");
    }

    [Fact]
    public void Write_ShapeNameWithControlCharacter_DoesNotThrow()
    {
        var presentation = WithShapeText("fine");
        presentation.Slides[0].Shapes[0].Name = "Shape" + VerticalTab + "1";

        using var buffer = new MemoryStream();
        var write = () => PptxPackageWriter.Write(presentation, buffer);

        write.Should().NotThrow();
    }

    [Fact]
    public void Write_NotesWithControlCharacter_DoesNotThrow()
    {
        var presentation = WithShapeText("fine");
        var notes = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "note" + VerticalTab + "text" });
        notes.Paragraphs.Add(paragraph);
        presentation.Slides[0].Notes = notes;

        using var buffer = new MemoryStream();
        var write = () => PptxPackageWriter.Write(presentation, buffer);

        write.Should().NotThrow();
    }

    [Fact]
    public void Write_OrdinaryTextIncludingEmoji_IsUnchanged()
    {
        // Valid surrogate PAIRS are legal XML and must survive; only lone surrogates are dropped.
        var reloaded = RoundTrip(WithShapeText("hello \U0001F600 world"));

        reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("hello \U0001F600 world");
    }
}
