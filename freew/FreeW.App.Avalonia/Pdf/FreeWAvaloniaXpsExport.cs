using Free.Shared.Xps;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Skia;
using FreeW.App.Avalonia.Editing;

namespace FreeW.App.Avalonia.Pdf;

/// <summary>Avalonia adapter for the shared fixed-layout XPS writer.</summary>
public static class FreeWAvaloniaXpsExport
{
    public static XpsExportabilityReport Analyze(DocumentView view, XpsWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        return PortableXpsWriter.Analyze(view.BuildPdfContent(), options);
    }

    public static void Save(DocumentView view, Stream stream, XpsWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("XPS export requires a writable stream.", nameof(stream));

        var document = view.BuildPdfContent();
        byte[] bytes;
        try
        {
            bytes = PortableXpsWriter.WriteToBytes(document, options);
        }
        catch (XpsUnsupportedContentException)
        {
            // XPS remains a real OPC package: raster pages are embedded as PNG resources when the
            // portable vector writer cannot represent the document's text without an XPS font.
            var pngPages = SkiaPdfWriter.RenderPagesToPng(document);
            var rasterDocument = new PdfContentDocument(
                document.Pages.Select((page, index) => new PdfContentPage(
                    page.WidthPoints,
                    page.HeightPoints,
                    [new PdfImage(0, 0, page.WidthPoints, page.HeightPoints, pngPages[index], "image/png")]))
                    .ToArray(),
                document.Properties);
            bytes = PortableXpsWriter.WriteToBytes(rasterDocument, options);
        }
        stream.Write(bytes);
    }

}
