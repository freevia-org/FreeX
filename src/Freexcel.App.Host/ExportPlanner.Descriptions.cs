namespace Freexcel.App.Host;

internal static partial class ExportPlanner
{
    public static string DescribeOptions(ExportOptions options)
    {
        var scope = options.Scope switch
        {
            ExportContentScope.ActiveSheet => "Active sheet only",
            ExportContentScope.Selection => "Selection",
            ExportContentScope.EntireWorkbook => "Entire workbook",
            _ => "Active sheet only"
        };
        var pageRange = options.PageRange is null
            ? null
            : options.PageRange.ToString();
        var quality = DescribeQuality(options.Quality);
        var printAreas = options.IgnorePrintAreas
            ? "print areas are ignored"
            : null;
        var initialView = DescribeInitialView(options.InitialView);
        var openMode = DescribeOpenMode(options.OpenMode);
        var properties = options.IncludeDocumentProperties
            ? "document properties are included"
            : "document properties are not included";
        var bookmarks = DescribeBookmarkMode(options.EffectiveBookmarkMode, ExportFormat.Pdf);
        var bitmapText = options.BitmapTextWhenFontsMayNotBeEmbedded
            ? "bitmap text when fonts may not be embedded"
            : null;
        var language = DescribePdfLanguage(options.PdfLanguage, ExportFormat.Pdf);
        var open = options.OpenAfterPublish
            ? "open after publishing"
            : null;

        return JoinOptionParts(scope, pageRange, quality, printAreas, initialView, openMode, properties, bookmarks, bitmapText, language, open);
    }

    public static string DescribeOptions(ExportOptions options, ExportFormat format) =>
        DescribeOptionsForFormat(options, format);

    public static string DescribeRequest(ExportRequest request)
    {
        var options = DescribeOptionsForFormat(request.Options, request.Format);
        return request.UsesXpsFallback
            ? $"{PdfFallbackMessage}\n\nOptions: {options}"
            : $"Options: {options}";
    }

    private static string DescribeOptionsForFormat(ExportOptions options, ExportFormat format)
    {
        var scope = options.Scope switch
        {
            ExportContentScope.ActiveSheet => "Active sheet only",
            ExportContentScope.Selection => "Selection",
            ExportContentScope.EntireWorkbook => "Entire workbook",
            _ => "Active sheet only"
        };
        var pageRange = options.PageRange is null
            ? null
            : options.PageRange.ToString();
        var quality = DescribeQualityForFormat(options.Quality, format);
        var printAreas = options.IgnorePrintAreas
            ? "print areas are ignored"
            : null;
        var initialView = DescribeInitialViewForFormat(options.InitialView, format);
        var openMode = DescribeOpenModeForFormat(options.OpenMode, format);
        var properties = (options.IncludeDocumentProperties, format) switch
        {
            (true, ExportFormat.Pdf) => "document properties are included",
            (true, ExportFormat.Xps) => "document properties are included",
            _ => "document properties are not included"
        };
        var bookmarks = DescribeBookmarkMode(options.EffectiveBookmarkMode, format);
        var bitmapText = DescribeBitmapTextOption(options.BitmapTextWhenFontsMayNotBeEmbedded, format);
        var language = DescribePdfLanguage(options.PdfLanguage, format);
        var open = options.OpenAfterPublish
            ? "open after publishing"
            : null;

        return JoinOptionParts(scope, pageRange, quality, printAreas, initialView, openMode, properties, bookmarks, bitmapText, language, open);
    }


    private static string JoinOptionParts(params string?[] parts) =>
        string.Join("; ", parts.Where(part => !string.IsNullOrWhiteSpace(part))) + ".";

    private static string DescribeQuality(ExportQuality quality) =>
        quality == ExportQuality.MinimumSize
            ? "minimum size"
            : "standard quality";

    private static string DescribeQualityForFormat(ExportQuality quality, ExportFormat format) =>
        quality == ExportQuality.MinimumSize && format == ExportFormat.Xps
            ? "minimum size is PDF-only"
            : DescribeQuality(quality);

    private static string? DescribeBookmarkMode(PdfBookmarkMode bookmarkMode, ExportFormat format)
    {
        if (bookmarkMode == PdfBookmarkMode.None)
            return null;

        if (format == ExportFormat.Xps)
            return "bookmarks are PDF-only";

        return bookmarkMode switch
        {
            PdfBookmarkMode.PrintTitles => "bookmarks use print titles",
            PdfBookmarkMode.PageNumbers => "bookmarks use page numbers",
            _ => "bookmarks use sheet names"
        };
    }

    private static string? DescribeBitmapTextOption(bool bitmapTextWhenFontsMayNotBeEmbedded, ExportFormat format)
    {
        if (!bitmapTextWhenFontsMayNotBeEmbedded)
            return null;

        return format == ExportFormat.Xps
            ? "bitmap text is PDF-only"
            : "bitmap text when fonts may not be embedded";
    }

    private static string? DescribePdfLanguage(string? pdfLanguage, ExportFormat format)
    {
        var normalized = NormalizePdfLanguage(pdfLanguage);
        if (string.Equals(normalized, DefaultPdfLanguage, StringComparison.OrdinalIgnoreCase))
            return null;

        return format == ExportFormat.Xps
            ? "PDF language is PDF-only"
            : $"PDF language {normalized}";
    }

    private static string? DescribeInitialView(PdfInitialView initialView) =>
        initialView switch
        {
            PdfInitialView.OneColumn => "opens as one continuous column",
            PdfInitialView.TwoColumnLeft => "opens as two columns with odd pages left",
            PdfInitialView.TwoColumnRight => "opens as two columns with odd pages right",
            _ => null
        };

    private static string? DescribeInitialViewForFormat(PdfInitialView initialView, ExportFormat format)
    {
        if (initialView == PdfInitialView.SinglePage)
            return null;

        return format == ExportFormat.Xps
            ? "PDF initial view is PDF-only"
            : DescribeInitialView(initialView);
    }

    private static string? DescribeOpenMode(PdfOpenMode openMode) =>
        openMode switch
        {
            PdfOpenMode.Outlines => "opens with bookmarks visible",
            PdfOpenMode.FullScreen => "opens full screen",
            _ => null
        };

    private static string? DescribeOpenModeForFormat(PdfOpenMode openMode, ExportFormat format)
    {
        if (openMode == PdfOpenMode.Normal)
            return null;

        return format == ExportFormat.Xps
            ? "PDF open mode is PDF-only"
            : DescribeOpenMode(openMode);
    }
}
