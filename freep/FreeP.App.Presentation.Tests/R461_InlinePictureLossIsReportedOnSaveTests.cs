using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r461: an inline picture the .pptx writer cannot carry must be reported, not dropped in silence.
///
/// <para>Found by a reflective round-trip sweep -- set every writable property to a distinctive
/// value, save, reload, compare. Most of what it flagged was the r419 trap (a value field is
/// meaningless without its mode field: bullet fields need <c>BulletKind</c>, group child transforms
/// need a group). <c>Run.InlineImage</c> was not: with the prerequisite satisfied it still did not
/// survive, and <c>InlineImage</c> appears nowhere in <c>FreeP.Core.IO</c> at all.</para>
///
/// <para>The path is ordinary. <c>ExternalRichTextClipboardPlanner</c> and
/// <c>ExternalXamlClipboardPlanner</c> build runs carrying <c>InlineImage</c> when rich text with a
/// picture is pasted from another application. Saving then writes no image part, and leaves the run
/// holding the bare U+FFFC object-replacement character -- so the user's picture becomes a stray box
/// glyph, and the image is gone from the file for good.</para>
///
/// <para>Writing inline pictures properly is a feature (they would have to become positioned picture
/// shapes). Telling the user is not, and both sibling apps already do it: FreeX's
/// <c>LossyFormatFeatureLossPlanner</c> and FreeW's <c>DocumentSaveCompatibilityPlanner</c>. This is
/// the save-side mirror of the load-side channel r454 added to this app.</para>
/// </summary>
public sealed class R461_InlinePictureLossIsReportedOnSaveTests
{
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static SlideShape TextShape(TextBody body) => new()
    {
        Id = 2,
        Name = "Body",
        Kind = SlideShapeKind.AutoShape,
        AutoShapeKind = DrawingShapeKind.Rectangle,
        OffsetXEmu = 100000,
        OffsetYEmu = 200000,
        ExtentCxEmu = 1000000,
        ExtentCyEmu = 500000,
        TextBody = body,
    };

    private static Presentation DeckWithPastedPicture()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "before " });
        paragraph.Runs.Add(new Run
        {
            // Exactly what the rich-text paste planners produce.
            Text = "￼",
            InlineImage = new ImagePart { Bytes = MinimalPng(), ContentType = "image/png" },
            InlineImageWidthEmu = 914400,
            InlineImageHeightEmu = 914400,
        });
        paragraph.Runs.Add(new Run { Text = " after" });

        var body = new TextBody();
        body.Paragraphs.Add(paragraph);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(TextShape(body));
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static Presentation PlainDeck()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "just text" });

        var body = new TextBody();
        body.Paragraphs.Add(paragraph);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(TextShape(body));
        presentation.Slides.Add(slide);
        return presentation;
    }

    [Fact]
    public void AnInlinePictureProducesASaveWarning()
    {
        var warnings = PptxSaveWarnings.Describe(DeckWithPastedPicture());

        warnings.Should().ContainSingle(
                "the picture is dropped by the save, so the user must be told before the session " +
                "holding the only copy is closed")
            .Which.Should().Contain("inline picture").And.Contain("lost");
    }

    [Fact]
    public void TheLossIsRealAndTheImageNeverReachesTheFile()
    {
        // The premise, asserted rather than assumed: if the writer did carry the picture, warning
        // about it would be noise. This is what makes the warning honest.
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(DeckWithPastedPicture(), stream);

        using var archive = new System.IO.Compression.ZipArchive(
            new MemoryStream(stream.ToArray()), System.IO.Compression.ZipArchiveMode.Read);

        archive.Entries.Should().NotContain(
            entry => entry.FullName.Contains("media/", StringComparison.OrdinalIgnoreCase),
            "no image part is written at all");
    }

    [Fact]
    public void APlainPresentationWarnsAboutNothing()
    {
        // A warning that fires on ordinary documents trains the user to dismiss the one that matters.
        PptxSaveWarnings.Describe(PlainDeck()).Should().BeEmpty();
    }

    [Fact]
    public void AnInlinePictureInsideAGroupIsFoundToo()
    {
        // Pasted content can land in a grouped shape, and a scan that only walked top-level shapes
        // would miss it -- reporting "nothing will be lost" while losing it.
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run
        {
            Text = "￼",
            InlineImage = new ImagePart { Bytes = MinimalPng(), ContentType = "image/png" },
        });

        var body = new TextBody();
        body.Paragraphs.Add(paragraph);

        var group = new SlideShape
        {
            Id = 2,
            Name = "Group",
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 100000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1000000,
        };
        group.Children.Add(TextShape(body));

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(group);
        presentation.Slides.Add(slide);

        PptxSaveWarnings.Describe(presentation).Should().ContainSingle("a grouped shape's runs count too");
    }

    [Fact]
    public void TheWarningReachesTheSaveResult()
    {
        // End to end: a warning that stops at the detector reaches nobody. This is the seam the
        // load-side equivalent (r454) had to add for the same reason.
        var path = Path.Combine(Path.GetTempPath(), "r461_" + Guid.NewGuid().ToString("N") + ".pptx");

        try
        {
            var result = PresentationFilePersistenceWorkflow.Save(path, DeckWithPastedPicture());

            result.SaveWarnings.Should().NotBeNullOrEmpty(
                "the save path is what the shell reports from");
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void AnOrdinarySaveCarriesNoWarningThroughTheSavePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "r461ok_" + Guid.NewGuid().ToString("N") + ".pptx");

        try
        {
            var result = PresentationFilePersistenceWorkflow.Save(path, PlainDeck());

            result.SaveWarnings.Should().BeNullOrEmpty("nothing was lost");
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
