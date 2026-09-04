using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r322: asks FreeX's r320 question of FreeW.
///
/// <para>r320 found that FreeX duplicated a drawing object when a user renamed one that had been
/// loaded from a file: the object was regenerated under its new name while the merger, which matches
/// originals by their CURRENT name, failed to supersede the original. The precondition for that class
/// is a model that is BOTH editable and replayed from preserved XML.</para>
///
/// <para>FreeW is built so those two cannot overlap: a run carries either a modelled
/// <see cref="InlineImage"/> -- which the writer emits in full, and which the SetImage* commands edit
/// -- or an opaque <c>PreservedDrawing</c> for a drawing the reader could not model, which has no
/// editable fields at all. So the class should not exist here. "Should" is why this test exists:
/// architecture that rules a defect out is a claim, and a claim about a save path is cheap to check
/// and expensive to assume.</para>
/// </summary>
public sealed class R322_LoadedImageEditsSurviveTests
{
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        new DocxFileAdapter().Save(document, stream);
        stream.Position = 0;
        return new DocxFileAdapter().Load(stream);
    }

    private static TextDocument WithOneImage()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("img") { Image = new InlineImage(OnePixelPng(), 10, 10) });
        document.Blocks.Add(paragraph);
        return document;
    }

    private static (int ParagraphIndex, int RunIndex, InlineImage Image) FindImage(TextDocument document)
    {
        var paragraphs = document.Blocks.OfType<Paragraph>().ToList();
        for (var p = 0; p < paragraphs.Count; p++)
        {
            for (var r = 0; r < paragraphs[p].Runs.Count; r++)
            {
                if (paragraphs[p].Runs[r].Image is { } image)
                    return (p, r, image);
            }
        }

        throw new InvalidOperationException("the fixture depends on the image surviving a round trip");
    }

    [Fact]
    public void EditingTheAltTextOfALoadedImageSurvivesAndDoesNotDuplicateIt()
    {
        var loaded = RoundTrip(WithOneImage());
        var (paragraphIndex, runIndex, _) = FindImage(loaded);

        new SetImageAltTextCommand(paragraphIndex, runIndex, "r322 alt").Apply(new Context(loaded));

        var resaved = RoundTrip(loaded);
        var images = resaved.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(run => run.Image is not null)
            .ToList();

        images.Should().ContainSingle("editing an image must not leave a second copy behind");
        images[0].Image!.AltText.Should().Be("r322 alt",
            "the edit must reach the file rather than being lost to replayed XML");
    }

    [Fact]
    public void ResizingALoadedImageSurvivesAndDoesNotDuplicateIt()
    {
        var loaded = RoundTrip(WithOneImage());
        var (paragraphIndex, runIndex, _) = FindImage(loaded);

        new SetImageSizeCommand(paragraphIndex, runIndex, 44, 33).Apply(new Context(loaded));

        var resaved = RoundTrip(loaded);
        var images = resaved.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(run => run.Image is not null)
            .ToList();

        images.Should().ContainSingle();
        images[0].Image!.WidthPt.Should().BeApproximately(44, 0.5);
        images[0].Image!.HeightPt.Should().BeApproximately(33, 0.5);
    }
}
