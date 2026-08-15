using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Unit coverage for the WPF adapter of Insert &gt; Illustrations &gt; Screenshot: native PNG dimension
/// decoding followed by shared <see cref="InlineImage"/> construction.
/// The drag-select overlay and live <see cref="Graphics.CopyFromScreen(int, int, int, int, Size)"/> path
/// can't run headlessly, so we exercise the deterministic encode/convert helper with a known-size PNG.
/// </summary>
public sealed class ScreenshotCaptureTests
{
    private const double PxPerPoint = 96.0 / 72.0;

    private static byte[] MakePng(int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.CornflowerBlue);
        using var buffer = new MemoryStream();
        bitmap.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
        return buffer.ToArray();
    }

    [Fact]
    public void PngToCapture_PreservesBytesAndDerivesPointDimensions()
    {
        // 96x48 px at 96 DPI -> 72x36 pt (no width cap needed).
        var png = MakePng(96, 48);

        var image = BuildImage(ScreenshotCapture.PngToCapture(png));

        image.Format.Should().Be(FreeW.Core.Model.ImageFormat.Png);
        image.Bytes.Should().Equal(png);
        image.WidthPt.Should().BeApproximately(96 / PxPerPoint, 0.001);
        image.HeightPt.Should().BeApproximately(48 / PxPerPoint, 0.001);
        image.OriginalPixelWidth.Should().Be(96);
        image.OriginalPixelHeight.Should().Be(48);
    }

    [Fact]
    public void SharedWorkflow_CapsWidthAndPreservesAspectRatio()
    {
        // 1200x600 px -> 900x450 pt before the 400 pt cap; capping width keeps the 2:1 aspect ratio.
        var png = MakePng(1200, 600);

        var image = BuildImage(ScreenshotCapture.PngToCapture(png));

        image.WidthPt.Should().BeApproximately(400, 0.001);
        image.HeightPt.Should().BeApproximately(200, 0.001);
    }

    [Fact]
    public void PngToCapture_RejectsEmptyBytes()
    {
        var act = () => ScreenshotCapture.PngToCapture(Array.Empty<byte>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PngToCapture_RejectsNonImageBytes()
    {
        var act = () => ScreenshotCapture.PngToCapture(new byte[] { 1, 2, 3, 4, 5 });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CapturedClip_WrittenToDocx_RoundTripsBytesAndDimensions()
    {
        // A screen clip flows: native PNG payload -> shared workflow -> InsertImage -> docx write. Assert the
        // captured PNG (bytes + derived point size) survives a real DocxWriter -> DocxReader round-trip.
        var png = MakePng(128, 64);
        var image = BuildImage(ScreenshotCapture.PngToCapture(png));

        var doc = new TextDocument();
        var paragraph = new FreeW.Core.Model.Paragraph();
        paragraph.Runs.Add(FreeW.Core.Model.Run.FromImage(image));
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var recovered = DocxReader.Read(stream);

        var recoveredImage = recovered.Paragraphs.First().Runs.Single(r => r.Image is not null).Image!;
        recoveredImage.Format.Should().Be(FreeW.Core.Model.ImageFormat.Png);
        recoveredImage.Bytes.Should().Equal(png);
        recoveredImage.WidthPt.Should().BeApproximately(image.WidthPt, 0.001);
        recoveredImage.HeightPt.Should().BeApproximately(image.HeightPt, 0.001);
    }

    private static InlineImage BuildImage(ScreenClipCapture capture)
    {
        InlineImage? inserted = null;
        var result = new ScreenClipWorkflowCoordinator().Execute(
            () => capture,
            image => inserted = image);

        result.Outcome.Should().Be(ScreenClipWorkflowOutcome.Inserted);
        inserted.Should().NotBeNull();
        return inserted!;
    }
}
