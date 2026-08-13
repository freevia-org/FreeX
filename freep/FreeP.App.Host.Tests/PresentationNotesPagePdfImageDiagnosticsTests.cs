using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

/// <summary>
/// R133-imageDiagnostics-wiring: an embedded slide picture with bytes the PDF writer cannot decode
/// (corrupt or an unrecognized format) used to be silently omitted from the exported page with no
/// trace anywhere -- the shared writer's imageDiagnostics sink existed since r132 but no production
/// caller ever passed a collection in. <see cref="PresentationFileCommandSession.ExportNotesPagePdfAsync"/> (File &gt; Export
/// &gt; Notes Page PDF) delegates to <see cref="PresentationFilePdfExportExecutor"/>; this test calls
/// that exact shared pipeline (not a re-implementation) to prove the production wiring itself
/// -- not just the shared library underneath it -- surfaces the loss instead of discarding it.
/// </summary>
public class PresentationNotesPagePdfImageDiagnosticsTests
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

    private static PresentationNotesPagePdfExportRequest AllSlidesRequest() =>
        new(new PresentationPrintRequest(
            PresentationPrintLayoutKind.NotesPages,
            new PresentationSlideRangeRequest(PresentationSlideRangeKind.AllSlides)));

    [Fact]
    public void ExportToBytes_SurfacesImageDiagnostics_WhenSlidePictureBytesAreUndecodable()
    {
        var deck = DeckWithPicture([0x00, 0x01, 0x02, 0x03, 0x04]);
        var artifact = PresentationFilePdfExportExecutor.ExportNotesPages(
            deck,
            AllSlidesRequest(),
            new WpfPresentationFileRenderPort());

        artifact.Bytes.Should().NotBeEmpty();
        artifact.ImageDiagnostics.Should().NotBeEmpty(
            "the slide picture's undecodable bytes must be surfaced, not silently dropped");
    }

    [Fact]
    public void ExportToBytes_NoImageDiagnostics_WhenSlidePictureIsDecodable()
    {
        // Sibling no-regression: a valid embedded picture must not spuriously report an image warning.
        var deck = DeckWithPicture(MinimalPngBytes());
        var artifact = PresentationFilePdfExportExecutor.ExportNotesPages(
            deck,
            AllSlidesRequest(),
            new WpfPresentationFileRenderPort());

        artifact.ImageDiagnostics.Should().BeEmpty();
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
}
