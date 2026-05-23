using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Freexcel.App.Host;

internal sealed record PdfBookmark(string Title, int PageIndex);

internal static class PdfDocumentExporter
{
    private const double StandardDpi = 96.0;
    private const double MinimumSizeDpi = 72.0;

    public static void Save(FixedDocument document, string path, PdfDocumentProperties? properties = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SavePages(document, path, properties, firstPageIndex: 0, lastPageIndexInclusive: document.Pages.Count - 1);
    }

    public static void Save(
        FixedDocument document,
        string path,
        PdfDocumentProperties? properties,
        ExportPageRange? pageRange,
        ExportQuality quality = ExportQuality.Standard,
        IReadOnlyList<PdfBookmark>? bookmarks = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!ExportPlanner.TryValidatePageRange(pageRange, document.Pages.Count, out var pageRangeError))
            throw new InvalidOperationException(pageRangeError);

        var firstPageIndex = Math.Max(0, (pageRange?.FromPage ?? 1) - 1);
        var lastPageIndexInclusive = Math.Min(document.Pages.Count - 1, (pageRange?.ToPage ?? document.Pages.Count) - 1);
        SavePages(document, path, properties, firstPageIndex, lastPageIndexInclusive, ResolveRasterDpi(quality), bookmarks);
    }

    internal static double ResolveRasterDpi(ExportQuality quality) =>
        quality == ExportQuality.MinimumSize
            ? MinimumSizeDpi
            : StandardDpi;

    private static void SavePages(
        FixedDocument document,
        string path,
        PdfDocumentProperties? properties,
        int firstPageIndex,
        int lastPageIndexInclusive,
        double dpi = StandardDpi,
        IReadOnlyList<PdfBookmark>? bookmarks = null)
    {
        if (firstPageIndex > lastPageIndexInclusive || document.Pages.Count == 0)
            throw new InvalidOperationException("The requested page range does not contain any exportable pages.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var pdf = new PdfDocument();
        pdf.Info.Creator = "Freexcel";
        ApplyProperties(pdf, properties);

        for (int i = firstPageIndex; i <= lastPageIndexInclusive; i++)
        {
            var fixedPage = GetFixedPage(document.Pages[i]);
            var pageSize = GetPageSize(document, fixedPage);
            fixedPage.Measure(pageSize);
            fixedPage.Arrange(new Rect(pageSize));
            fixedPage.UpdateLayout();

            var bitmap = RenderPage(fixedPage, pageSize, dpi);
            var page = pdf.AddPage();
            page.Width = XUnit.FromPoint(pageSize.Width * 72.0 / StandardDpi);
            page.Height = XUnit.FromPoint(pageSize.Height * 72.0 / StandardDpi);

            using var gfx = XGraphics.FromPdfPage(page);
            using var image = XImage.FromBitmapSource(bitmap);
            gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
        }

        AddBookmarks(pdf, bookmarks, firstPageIndex, lastPageIndexInclusive);
        pdf.Save(path);
    }

    private static void AddBookmarks(
        PdfDocument pdf,
        IReadOnlyList<PdfBookmark>? bookmarks,
        int firstPageIndex,
        int lastPageIndexInclusive)
    {
        if (bookmarks is null || bookmarks.Count == 0)
            return;

        foreach (var bookmark in bookmarks)
        {
            if (string.IsNullOrWhiteSpace(bookmark.Title) ||
                bookmark.PageIndex < firstPageIndex ||
                bookmark.PageIndex > lastPageIndexInclusive)
            {
                continue;
            }

            var exportedPageIndex = bookmark.PageIndex - firstPageIndex;
            if (exportedPageIndex < 0 || exportedPageIndex >= pdf.Pages.Count)
                continue;

            pdf.Outlines.Add(bookmark.Title.Trim(), pdf.Pages[exportedPageIndex], opened: false);
        }

        if (pdf.Outlines.Count > 0)
            pdf.PageMode = PdfPageMode.UseOutlines;
    }

    private static void ApplyProperties(PdfDocument pdf, PdfDocumentProperties? properties)
    {
        if (properties is null)
            return;

        if (NormalizeProperty(properties.Title) is { } title)
            pdf.Info.Title = title;
        if (NormalizeProperty(properties.Author) is { } author)
            pdf.Info.Author = author;
        if (NormalizeProperty(properties.Subject) is { } subject)
            pdf.Info.Subject = subject;
        if (NormalizeProperty(properties.Keywords) is { } keywords)
            pdf.Info.Keywords = keywords;
    }

    private static string? NormalizeProperty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private static BitmapSource RenderPage(FixedPage page, Size pageSize, double dpi)
    {
        var scale = dpi / StandardDpi;
        var target = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(pageSize.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(pageSize.Height * scale)),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        target.Render(page);
        target.Freeze();
        return target;
    }
}
