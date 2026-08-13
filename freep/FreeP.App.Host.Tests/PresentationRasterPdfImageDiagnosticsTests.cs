using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

/// <summary>
/// R133 remediation: <see cref="PresentationNotesPagePdfImageDiagnosticsTests"/> proved the shared
/// writer's imageDiagnostics sink is wired, but only for the *vector* Notes-Page PDF path. File &gt;
/// Export to PDF (<see cref="PresentationFileCommandSession.ExportPdfAsync"/>) -- the primary raster export
/// command, on both shells -- rasterizes each slide first (via
/// <see cref="WpfPresentationSlideImageRenderer"/> + <c>SlideCanvas</c>), then hands the shared writer
/// an already-composited PNG. That PNG is one the host itself just encoded, so it is always
/// well-formed -- <see cref="Free.Shared.Pdf.Wpf.WpfRasterPdfWriter"/>'s imageDiagnostics sink can
/// never observe a picture dropped one layer further down, inside the slide composite itself (an
/// undecodable embedded picture used to be a bare <c>catch { return; }</c> in
/// <c>FreeP.App.Rendering.Wpf.SlideCanvas.RenderPicture</c> with no way to report it). This test drives
/// the exact shared composition <see cref="PresentationFileCommandSession.ExportPdfAsync"/> uses --
/// <see cref="PresentationFilePdfExportExecutor"/>, not a re-implementation -- to prove the raster
/// path itself surfaces the loss instead of the export silently looking clean.
/// </summary>
public class PresentationRasterPdfImageDiagnosticsTests
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

    [StaFact]
    public void ExportPdfRasterBytes_SurfacesImageDiagnostics_WhenSlidePictureBytesAreUndecodable()
    {
        var deck = DeckWithPicture([0x00, 0x01, 0x02, 0x03, 0x04]);
        var artifact = PresentationFilePdfExportExecutor.ExportRaster(
            deck,
            request: null,
            new WpfPresentationFileRenderPort());

        artifact.Bytes.Should().NotBeEmpty();
        artifact.ImageDiagnostics.Should().NotBeEmpty(
            "the undecodable slide picture must be surfaced through the raster export path (File > Export to PDF), not silently dropped");
    }

    [StaFact]
    public void ExportPdfRasterBytes_NoImageDiagnostics_WhenSlidePictureIsDecodable()
    {
        // Sibling no-regression: a valid embedded picture must not spuriously report an image warning.
        var deck = DeckWithPicture(MinimalPngBytes());
        var artifact = PresentationFilePdfExportExecutor.ExportRaster(
            deck,
            request: null,
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
