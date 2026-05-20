using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Freexcel.App.Host;

internal static class PdfDocumentExporter
{
    private const double Dpi = 96.0;

    public static void Save(FixedDocument document, string path, PdfDocumentProperties? properties = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var pdf = new PdfDocument();
        pdf.Info.Creator = "Freexcel";
        ApplyProperties(pdf, properties);

        for (int i = 0; i < document.Pages.Count; i++)
        {
            var fixedPage = GetFixedPage(document.Pages[i]);
            var pageSize = GetPageSize(document, fixedPage);
            fixedPage.Measure(pageSize);
            fixedPage.Arrange(new Rect(pageSize));
            fixedPage.UpdateLayout();

            var bitmap = RenderPage(fixedPage, pageSize);
            var page = pdf.AddPage();
            page.Width = XUnit.FromPoint(pageSize.Width * 72.0 / Dpi);
            page.Height = XUnit.FromPoint(pageSize.Height * 72.0 / Dpi);

            using var gfx = XGraphics.FromPdfPage(page);
            using var image = XImage.FromBitmapSource(bitmap);
            gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
        }

        pdf.Save(path);
    }

    private static void ApplyProperties(PdfDocument pdf, PdfDocumentProperties? properties)
    {
        if (properties is null)
            return;

        if (!string.IsNullOrWhiteSpace(properties.Title))
            pdf.Info.Title = properties.Title;
        if (!string.IsNullOrWhiteSpace(properties.Author))
            pdf.Info.Author = properties.Author;
        if (!string.IsNullOrWhiteSpace(properties.Subject))
            pdf.Info.Subject = properties.Subject;
        if (!string.IsNullOrWhiteSpace(properties.Keywords))
            pdf.Info.Keywords = properties.Keywords;
    }

    private static FixedPage GetFixedPage(PageContent pageContent)
    {
        pageContent.GetPageRoot(forceReload: false);
        return pageContent.Child ??
            throw new InvalidOperationException("FixedDocument page content did not contain a FixedPage.");
    }

    private static Size GetPageSize(FixedDocument document, FixedPage page)
    {
        var width = page.Width;
        if (double.IsNaN(width) || width <= 0)
            width = document.DocumentPaginator.PageSize.Width;

        var height = page.Height;
        if (double.IsNaN(height) || height <= 0)
            height = document.DocumentPaginator.PageSize.Height;

        if (double.IsNaN(width) || width <= 0 || double.IsNaN(height) || height <= 0)
            throw new InvalidOperationException("Cannot export a PDF page without a valid page size.");

        return new Size(width, height);
    }

    private static BitmapSource RenderPage(FixedPage page, Size pageSize)
    {
        var target = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(pageSize.Width)),
            Math.Max(1, (int)Math.Ceiling(pageSize.Height)),
            Dpi,
            Dpi,
            PixelFormats.Pbgra32);
        target.Render(page);
        target.Freeze();
        return target;
    }
}
