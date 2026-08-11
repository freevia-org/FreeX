using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.Drawing;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// R134 remediation (tracked as task #150, an r133 residual): r133 wired
/// <see cref="SlideImageRenderDiagnostics"/> into File &gt; Export to PDF, but left File &gt; Export &gt;
/// Images (<see cref="MainWindow.FileExportImagesToFolderCore"/>) and File &gt; Export &gt; Video
/// (<see cref="MainWindow.BuildVideoFramePackageCore"/>) unwired on the Avalonia shell too -- an
/// undecodable embedded picture silently disappeared from those two exports with zero surfacing.
///
/// <para>
/// This supersedes the original Avalonia copy of this test class in
/// <c>FreeP.App.Avalonia.Tests.R134_ExportImagesAndVideoImageDiagnosticsTests</c>, which ran under that
/// assembly's shared <see cref="Avalonia.Headless.HeadlessUnitTestSession"/>, itself bound (via the
/// assembly-level <c>[assembly: AvaloniaTestApplication]</c> attribute, one Application per assembly) to
/// <c>FreePHeadlessApp</c> with <c>UseHeadlessDrawing = true</c> (a stub, non-Skia backend). Under that
/// backend the render path never completed, so every assertion there sat behind an
/// <c>if (ran) { ... }</c> guard that never actually ran -- durations of 5-9ms, far too fast for real
/// layout+render+PNG-encode, and the tests passed identically whether or not the diagnostics wiring was
/// present at all. A test that cannot fail is worse than no test, so that copy was deleted rather than
/// kept as decorative coverage.
/// </para>
///
/// <para>
/// This copy instead runs in this project, whose assembly-level Application
/// (<see cref="SlideHeadlessApp"/>, see <c>SlideCanvasAvaloniaTests.cs</c>) uses a real Skia headless
/// backend (<c>UseHeadlessDrawing = false</c>) -- the same harness
/// <see cref="SlideCanvasRasterPdfImageDiagnosticsTests"/> already uses to prove the underlying
/// <see cref="SlideImageRenderDiagnostics"/> mechanism works when rendering actually completes. It drives
/// the exact production composition each command uses -- <see cref="MainWindow.FileExportImagesToFolderCore"/>
/// and <see cref="MainWindow.BuildVideoFramePackageCore"/>, not a re-implementation, and asserts
/// unconditionally: no <c>if (ran)</c> guard, because under a real Skia backend the render genuinely
/// completes and there is nothing to tolerate.
/// </para>
/// </summary>
public sealed class R134_ExportImagesAndVideoImageDiagnosticsTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    private static Task Run(Action action) => Session.Dispatch(action, CancellationToken.None);

    private static Presentation DeckWithPicture(byte[] pictureBytes)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var slide = new Slide { Title = "Slide with picture" };
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
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

    [Fact]
    public async Task FileExportImagesToFolderCore_SurfacesImageDiagnostics_WhenSlidePictureBytesAreUndecodable()
    {
        var deck = DeckWithPicture([0x00, 0x01, 0x02, 0x03, 0x04]);
        var outputDirectory = Path.Combine(Path.GetTempPath(), "FreeP-R134-Avalonia-Images-" + Guid.NewGuid().ToString("N"));
        var imageDiagnostics = new List<string>();
        PresentationImageExportResult? result = null;

        try
        {
            await Run(() =>
            {
                result = MainWindow.FileExportImagesToFolderCore(
                    deck,
                    new PresentationImageExportRequest(outputDirectory, BaseFileName: "Deck"),
                    imageDiagnostics);
            });

            result.Should().NotBeNull();
            result!.ExportedSlides.Should().HaveCount(1);
            imageDiagnostics.Should().NotBeEmpty(
                "the undecodable slide picture must be surfaced through the image export path (File > Export > Images), not silently dropped");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task FileExportImagesToFolderCore_NoImageDiagnostics_WhenSlidePictureIsDecodable()
    {
        // Sibling no-regression: a valid embedded picture must not spuriously report an image warning.
        var deck = DeckWithPicture(MinimalPngBytes());
        var outputDirectory = Path.Combine(Path.GetTempPath(), "FreeP-R134-Avalonia-Images-" + Guid.NewGuid().ToString("N"));
        var imageDiagnostics = new List<string>();

        try
        {
            await Run(() =>
            {
                MainWindow.FileExportImagesToFolderCore(
                    deck,
                    new PresentationImageExportRequest(outputDirectory, BaseFileName: "Deck"),
                    imageDiagnostics);
            });

            imageDiagnostics.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
    }

    // ---- Export > Video ----------------------------------------------------

    [Fact]
    public async Task BuildVideoFramePackageCore_SurfacesImageDiagnostics_WhenSlidePictureBytesAreUndecodable()
    {
        var deck = DeckWithPicture([0x00, 0x01, 0x02, 0x03, 0x04]);
        var imageDiagnostics = new List<string>();
        PresentationVideoFramePackage? package = null;

        await Run(() =>
        {
            package = MainWindow.BuildVideoFramePackageCore(
                deck, request: null, EncodableHostCapabilities(), imageDiagnostics);
        });

        package.Should().NotBeNull();
        package!.Frames.Should().NotBeEmpty();
        imageDiagnostics.Should().NotBeEmpty(
            "the undecodable slide picture must be surfaced through the video frame export path (File > Export > Video), not silently dropped");
    }

    [Fact]
    public async Task BuildVideoFramePackageCore_NoImageDiagnostics_WhenSlidePictureIsDecodable()
    {
        // Sibling no-regression: a valid embedded picture must not spuriously report an image warning.
        var deck = DeckWithPicture(MinimalPngBytes());
        var imageDiagnostics = new List<string>();

        await Run(() =>
        {
            MainWindow.BuildVideoFramePackageCore(
                deck, request: null, EncodableHostCapabilities(), imageDiagnostics);
        });

        imageDiagnostics.Should().BeEmpty();
    }

    // ---- Ambient-sink scoping: must not leak across sequential/concurrent exports --------

    [Fact]
    public async Task SlideImageRenderDiagnostics_DoesNotLeakBetweenSequentialExports()
    {
        // Round 134 note: the sink is an AsyncLocal ambient collector installed per-call via
        // SlideImageRenderDiagnostics.Capture; this proves a bad picture reported during one export's
        // Capture scope never lands in a completely separate, later export's diagnostics list -- i.e.
        // the scope truly resets to "no collector installed" once disposed, rather than leaving a
        // stale reference another export could accidentally append to.
        var badDeck = DeckWithPicture([0x00, 0x01, 0x02, 0x03, 0x04]);
        var goodDeck = DeckWithPicture(MinimalPngBytes());
        var badDir = Path.Combine(Path.GetTempPath(), "FreeP-R134-Avalonia-Leak-Bad-" + Guid.NewGuid().ToString("N"));
        var goodDir = Path.Combine(Path.GetTempPath(), "FreeP-R134-Avalonia-Leak-Good-" + Guid.NewGuid().ToString("N"));

        try
        {
            var firstDiagnostics = new List<string>();
            var secondDiagnostics = new List<string>();

            await Run(() =>
            {
                MainWindow.FileExportImagesToFolderCore(
                    badDeck,
                    new PresentationImageExportRequest(badDir, BaseFileName: "Bad"),
                    firstDiagnostics);

                MainWindow.FileExportImagesToFolderCore(
                    goodDeck,
                    new PresentationImageExportRequest(goodDir, BaseFileName: "Good"),
                    secondDiagnostics);

                // Reporting outside of any installed Capture scope (post-dispose) must be a no-op, not a
                // write into whichever list happened to be installed most recently.
                SlideImageRenderDiagnostics.ReportUndecodableImage(1, "post-scope report");
            });

            firstDiagnostics.Should().NotBeEmpty();
            secondDiagnostics.Should().BeEmpty(
                "diagnostics from an earlier, already-completed export must not leak into a later export's own list, " +
                "and reporting outside any Capture scope must be a no-op");
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
