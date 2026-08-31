using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// A bare bitmap -- what an image tool leaves with no text, RTF, or XAML beside it -- was the one
/// clipboard shape the in-canvas resolver could not answer. WPF got it anyway by declining the
/// key and letting its RichTextBox paste the bitmap itself, which skipped every rule the payload
/// path applies; Avalonia, which never declines, dropped it. Resolving it here gives both shells
/// the same answer through the same path.
/// </summary>
public sealed class InCanvasBitmapPasteResolutionTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");

    [Fact]
    public void Resolve_TurnsABareBitmapIntoAnInlineImageRun()
    {
        var resolution = InCanvasRichClipboardFormatResolver.Resolve(
            new PresentationClipboardContent { PngBytes = Png });

        resolution.Source.Should().Be(PresentationClipboardPasteSource.Image);
        var run = resolution.Payload!.Body.Paragraphs.Single().Runs.Single();
        run.InlineImage!.Bytes.Should().BeEquivalentTo(Png);
        run.InlineImage.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void Resolve_PrefersTextOverAnAccompanyingBitmap()
    {
        // A copied spreadsheet range offers both. The text is the content; the bitmap is a
        // picture of it. Choosing the bitmap here would turn a paste into an inert image.
        var resolution = InCanvasRichClipboardFormatResolver.Resolve(
            new PresentationClipboardContent { Text = "A\tB", PngBytes = Png });

        resolution.Source.Should().Be(PresentationClipboardPasteSource.Text);
        resolution.Payload!.Body.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => run.InlineImage == null);
    }

    [Fact]
    public void Resolve_PrefersARichPayloadOverABitmap()
    {
        var payload = InCanvasRichClipboardPayload.FromPlainText("rich");

        var resolution = InCanvasRichClipboardFormatResolver.Resolve(
            new PresentationClipboardContent
            {
                RichTextBytes = InCanvasRichClipboardPlanner.Serialize(payload),
                PngBytes = Png,
            });

        resolution.Source.Should().Be(PresentationClipboardPasteSource.RichText);
        resolution.Payload!.PlainText.Should().Be("rich");
    }

    [Fact]
    public void Resolve_StillReportsNothingForAnEmptyClipboard()
    {
        var resolution = InCanvasRichClipboardFormatResolver.Resolve(
            new PresentationClipboardContent());

        resolution.Source.Should().Be(PresentationClipboardPasteSource.Nothing);
        resolution.Payload.Should().BeNull();
    }

    [Fact]
    public void RichTextReadRequest_AsksForTheImageSoTheBitmapEverReachesTheResolver()
    {
        PresentationClipboardPlatformMapper.RichTextReadRequest.IncludeImage.Should().BeTrue();
    }

    [Fact]
    public void FromInlineImage_PastesThroughThePlannerAsAnInlineImage()
    {
        var payload = InCanvasRichClipboardPayload.FromInlineImage(Png, "image/png");

        var pasted = InCanvasRichClipboardPlanner.Apply(
            InCanvasRichClipboardPayload.FromPlainText(string.Empty).Body,
            new InCanvasEditorTextSelection(0, 0),
            payload,
            out _);

        pasted.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Should().ContainSingle(run => run.InlineImage != null);
    }
}
