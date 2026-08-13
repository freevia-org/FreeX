using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationPdfExportArtifact(
    byte[] Bytes,
    IReadOnlyList<string> ImageDiagnostics);

/// <summary>
/// Owns the renderer-neutral PDF export pipeline, including diagnostics emitted while a platform
/// renderer composites slide pictures and while its PDF backend decodes the resulting images.
/// </summary>
public static class PresentationFilePdfExportExecutor
{
    public static PresentationPdfExportArtifact ExportRaster(
        Presentation presentation,
        PresentationRasterPdfExportRequest? request,
        IPresentationFileRenderPort renderPort)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(renderPort);

        var imageDiagnostics = new List<string>();
        byte[] bytes;
        using (SlideImageRenderDiagnostics.Capture(imageDiagnostics))
        {
            bytes = PresentationRasterPdfExporter.ExportToBytes(
                presentation,
                request,
                renderPort.RenderSlideToPng,
                document => renderPort.WriteRasterPdfWithDiagnostics(document, imageDiagnostics));
        }

        return new PresentationPdfExportArtifact(bytes, imageDiagnostics);
    }

    public static PresentationPdfExportArtifact ExportNotesPages(
        Presentation presentation,
        PresentationNotesPagePdfExportRequest request,
        IPresentationFileRenderPort renderPort)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(renderPort);

        var imageDiagnostics = new List<string>();
        var bytes = PresentationNotesPagePdfExporter.ExportToBytes(
            presentation,
            request,
            document => renderPort.WriteVectorPdfWithDiagnostics(document, imageDiagnostics));
        return new PresentationPdfExportArtifact(bytes, imageDiagnostics);
    }
}
