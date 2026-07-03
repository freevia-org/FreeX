using Free.Shared.Pdf;
using SkiaSharp;

namespace Free.Shared.Pdf.Skia;

/// <summary>SkiaSharp PDF backend for the shared raster page model.</summary>
public static class SkiaRasterPdfWriter
{
    public static int Write(PdfRasterDocument document, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("PDF export requires a writable stream.", nameof(stream));
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("PDF export requires at least one rendered page.");

        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(0);
        }

        var metadata = BuildMetadata(document.Properties);
        var pageCount = 0;
        using (var pdf = SKDocument.CreatePdf(stream, metadata))
        {
            foreach (var page in document.Pages)
            {
                using var image = DecodeImage(page.ImageBytes);
                var canvas = pdf.BeginPage((float)page.WidthPoints, (float)page.HeightPoints);
                canvas.Clear(SKColors.White);
                canvas.DrawImage(
                    image,
                    new SKRect(0, 0, (float)page.WidthPoints, (float)page.HeightPoints));
                pdf.EndPage();
                pageCount++;
            }

            pdf.Close();
        }

        return pageCount;
    }

    public static byte[] WriteToBytes(PdfRasterDocument document)
    {
        using var stream = new MemoryStream();
        Write(document, stream);
        return stream.ToArray();
    }

    private static SKImage DecodeImage(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
            throw new InvalidOperationException("A raster PDF page must carry encoded image bytes.");

        using var data = SKData.CreateCopy(imageBytes);
        return SKImage.FromEncodedData(data)
            ?? throw new InvalidOperationException("A raster PDF page must carry decodable image bytes.");
    }

    private static SKDocumentPdfMetadata BuildMetadata(PdfDocumentProperties? properties)
    {
        var metadata = new SKDocumentPdfMetadata();
        if (properties is null)
            return metadata;

        if (!string.IsNullOrWhiteSpace(properties.Title))
            metadata.Title = properties.Title;
        if (!string.IsNullOrWhiteSpace(properties.Author))
            metadata.Author = properties.Author;
        if (!string.IsNullOrWhiteSpace(properties.Subject))
            metadata.Subject = properties.Subject;
        if (!string.IsNullOrWhiteSpace(properties.Keywords))
            metadata.Keywords = properties.Keywords;
        if (!string.IsNullOrWhiteSpace(properties.Creator))
            metadata.Creator = properties.Creator;
        return metadata;
    }
}
