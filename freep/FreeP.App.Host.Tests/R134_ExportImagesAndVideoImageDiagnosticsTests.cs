using System;
using System.Collections.Generic;
using System.IO;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

/// <summary>
/// R134 remediation (tracked as task #150, an r133 residual): r133 wired
/// <see cref="SlideImageRenderDiagnostics"/> into File &gt; Export to PDF
/// (<see cref="PresentationRasterPdfImageDiagnosticsTests"/>) but left File &gt; Export &gt; Images
/// (<see cref="FileCommands.ExportImagesToFolder"/>) and File &gt; Export &gt; Video
/// (<see cref="FileCommands.BuildVideoFramePackage"/>) unwired -- an undecodable embedded picture
/// silently disappeared from those two exports with zero surfacing. This test drives the exact
/// production composition each command uses -- <see cref="FileCommands.ExportImagesToFolderCore"/>
/// and <see cref="FileCommands.BuildVideoFramePackageCore"/>, not a re-implementation -- to prove
/// the loss is now surfaced.
/// </summary>
public class R134_ExportImagesAndVideoImageDiagnosticsTests
{
    private static Presentation DeckWithPicture(byte[] pictureBytes)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var slide = new Slide { Title = "Slide with picture" };
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Picture = new ImagePart { Bytes = pictureBytes, ContentType = "image/png" },
        });
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static PresentationVideoExportHandoffHostCapabilities EncodableHostCapabilities() =>
        new("Test video host", CanEncodeMp4: true, CanCaptureNarration: false, CanCaptureCameraAndMedia: false,
            UnavailableReason: "", CanMuxTimedCaptions: false);

    // ---- Export > Images -------------------------------------------------

    [StaFact]
    public void ExportImagesToFolderCore_SurfacesImageDiagnostics_WhenSlidePictureBytesAreUndecodable()
    {
        var deck = DeckWithPicture([0x00, 0x01, 0x02, 0x03, 0x04]);
        var outputDirectory = Path.Combine(Path.GetTempPath(), "FreeP-R134-Images-" + Guid.NewGuid().ToString("N"));
        var imageDiagnostics = new List<string>();

        try
        {
            var result = FileCommands.ExportImagesToFolderCore(
                deck,
                new PresentationImageExportRequest(outputDirectory, BaseFileName: "Deck"),
                imageDiagnostics);

            result.ExportedSlides.Should().HaveCount(1);
            imageDiagnostics.Should().NotBeEmpty(
                "the undecodable slide picture must be surfaced through the image export path (File > Export > Images), not silently dropped");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [StaFact]
    public void ExportImagesToFolderCore_NoImageDiagnostics_WhenSlidePictureIsDecodable()
    {
        // Sibling no-regression: a valid embedded picture must not spuriously report an image warning.
        var deck = DeckWithPicture(MinimalPngBytes());
        var outputDirectory = Path.Combine(Path.GetTempPath(), "FreeP-R134-Images-" + Guid.NewGuid().ToString("N"));
        var imageDiagnostics = new List<string>();

        try
        {
            FileCommands.ExportImagesToFolderCore(
                deck,
                new PresentationImageExportRequest(outputDirectory, BaseFileName: "Deck"),
                imageDiagnostics);

            imageDiagnostics.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
    }

    // ---- Export > Video ----------------------------------------------------

    [StaFact]
    public void BuildVideoFramePackageCore_SurfacesImageDiagnostics_WhenSlidePictureBytesAreUndecodable()
    {
        var deck = DeckWithPicture([0x00, 0x01, 0x02, 0x03, 0x04]);
        var imageDiagnostics = new List<string>();

        var package = FileCommands.BuildVideoFramePackageCore(
            deck, request: null, EncodableHostCapabilities(), imageDiagnostics);

        package.Frames.Should().NotBeEmpty();
        imageDiagnostics.Should().NotBeEmpty(
            "the undecodable slide picture must be surfaced through the video frame export path (File > Export > Video), not silently dropped");
    }

    [StaFact]
    public void BuildVideoFramePackageCore_NoImageDiagnostics_WhenSlidePictureIsDecodable()
    {
        // Sibling no-regression: a valid embedded picture must not spuriously report an image warning.
        var deck = DeckWithPicture(MinimalPngBytes());
        var imageDiagnostics = new List<string>();

        FileCommands.BuildVideoFramePackageCore(
            deck, request: null, EncodableHostCapabilities(), imageDiagnostics);

        imageDiagnostics.Should().BeEmpty();
    }

    // ---- Ambient-sink scoping: must not leak across sequential/concurrent exports --------

    [StaFact]
    public void SlideImageRenderDiagnostics_DoesNotLeakBetweenSequentialExports()
    {
        // Round 134 note: the sink is an AsyncLocal ambient collector installed per-call via
        // SlideImageRenderDiagnostics.Capture; this proves a bad picture reported during one export's
        // Capture scope never lands in a completely separate, later export's diagnostics list -- i.e.
        // the scope truly resets to "no collector installed" once disposed, rather than leaving a
        // stale reference another export could accidentally append to.
        var badDeck = DeckWithPicture([0x00, 0x01, 0x02, 0x03, 0x04]);
        var goodDeck = DeckWithPicture(MinimalPngBytes());
        var badDir = Path.Combine(Path.GetTempPath(), "FreeP-R134-Leak-Bad-" + Guid.NewGuid().ToString("N"));
        var goodDir = Path.Combine(Path.GetTempPath(), "FreeP-R134-Leak-Good-" + Guid.NewGuid().ToString("N"));

        try
        {
            var firstDiagnostics = new List<string>();
            FileCommands.ExportImagesToFolderCore(
                badDeck,
                new PresentationImageExportRequest(badDir, BaseFileName: "Bad"),
                firstDiagnostics);
            firstDiagnostics.Should().NotBeEmpty();

            // A later, unrelated export against a clean deck must get a clean diagnostics list --
            // reusing a fresh List<string> (as every real call site does), so this catches the sink
            // itself leaking (e.g. via a static list, or Capture failing to restore the ambient value).
            var secondDiagnostics = new List<string>();
            FileCommands.ExportImagesToFolderCore(
                goodDeck,
                new PresentationImageExportRequest(goodDir, BaseFileName: "Good"),
                secondDiagnostics);

            secondDiagnostics.Should().BeEmpty(
                "diagnostics from an earlier, already-completed export must not leak into a later export's own list");

            // Reporting outside of any installed Capture scope (post-dispose) must be a no-op, not a
            // write into whichever list happened to be installed most recently.
            SlideImageRenderDiagnostics.ReportUndecodableImage(1, "post-scope report");
            secondDiagnostics.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(badDir))
                Directory.Delete(badDir, recursive: true);
            if (Directory.Exists(goodDir))
                Directory.Delete(goodDir, recursive: true);
        }
    }
}
