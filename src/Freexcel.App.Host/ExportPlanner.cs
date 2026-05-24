using System.Globalization;
using System.IO;

namespace Freexcel.App.Host;

internal enum ExportFormat
{
    Xps,
    Pdf
}

internal enum ExportContentScope
{
    ActiveSheet,
    Selection,
    EntireWorkbook
}

internal enum ExportQuality
{
    Standard,
    MinimumSize
}

internal enum PdfBookmarkMode
{
    None,
    SheetNames,
    PrintTitles,
    PageNumbers
}

internal enum PdfInitialView
{
    SinglePage,
    OneColumn,
    TwoColumnLeft,
    TwoColumnRight
}

internal enum PdfOpenMode
{
    Normal,
    Outlines,
    FullScreen
}

internal sealed record ExportPageRange(int FromPage, int ToPage)
{
    public override string ToString() =>
        FromPage == ToPage
            ? $"page {FromPage}"
            : $"pages {FromPage}-{ToPage}";
}

internal sealed record ExportOptions(
    ExportContentScope Scope,
    bool IncludeDocumentProperties,
    bool OpenAfterPublish,
    bool IgnorePrintAreas = false,
    ExportPageRange? PageRange = null,
    ExportQuality Quality = ExportQuality.Standard,
    bool CreateBookmarks = false,
    PdfBookmarkMode BookmarkMode = PdfBookmarkMode.None,
    PdfInitialView InitialView = PdfInitialView.SinglePage,
    PdfOpenMode OpenMode = PdfOpenMode.Normal,
    bool BitmapTextWhenFontsMayNotBeEmbedded = false,
    string PdfLanguage = ExportPlanner.DefaultPdfLanguage)
{
    public static ExportOptions ExcelLikeDefault { get; } =
        new(ExportContentScope.ActiveSheet, IncludeDocumentProperties: false, OpenAfterPublish: false);

    public PdfBookmarkMode EffectiveBookmarkMode =>
        BookmarkMode != PdfBookmarkMode.None
            ? BookmarkMode
            : CreateBookmarks
                ? PdfBookmarkMode.SheetNames
                : PdfBookmarkMode.None;
}

internal sealed record ExportRequest(
    string Path,
    ExportFormat Format,
    ExportOptions Options,
    string? FallbackPath)
{
    public bool UsesXpsFallback => FallbackPath is not null;
    public string ActualPath => FallbackPath ?? Path;
}

internal static class ExportPlanner
{
    public const string DefaultPdfLanguage = "en-US";

    public const string PdfFallbackMessage =
        "PDF export uses Freexcel's print renderer. XPS export remains available for Windows print-pipeline workflows.";

    public static ExportFormat InferExportFormat(string path) =>
        string.Equals(Path.GetExtension(path), ".xps", StringComparison.OrdinalIgnoreCase)
            ? ExportFormat.Xps
            : ExportFormat.Pdf;

    public static ExportRequest PlanExport(string path) =>
        PlanExport(path, ExportOptions.ExcelLikeDefault);

    public static ExportRequest PlanExport(string path, ExportOptions options)
    {
        var format = InferExportFormat(path);
        var normalizedPath = NormalizeExportPath(path, format, forceMatchingExtension: false);
        return new ExportRequest(normalizedPath, format, options, null);
    }

    public static ExportRequest PlanExport(string path, ExportFormat format, ExportOptions options)
    {
        var normalizedPath = NormalizeExportPath(path, format, forceMatchingExtension: true);
        return new ExportRequest(normalizedPath, format, options, null);
    }

    public static string GetFallbackXpsPath(string requestedPath) =>
        Path.ChangeExtension(requestedPath, ".xps");

    private static string NormalizeExportPath(string path, ExportFormat format, bool forceMatchingExtension)
    {
        if (!forceMatchingExtension && !string.IsNullOrEmpty(Path.GetExtension(path)))
            return path;

        return Path.ChangeExtension(path, format == ExportFormat.Xps ? ".xps" : ".pdf");
    }

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
        var initialView = format == ExportFormat.Pdf ? DescribeInitialView(options.InitialView) : null;
        var openMode = format == ExportFormat.Pdf ? DescribeOpenMode(options.OpenMode) : null;
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

    public static string NormalizePdfLanguage(string? pdfLanguage)
    {
        if (string.IsNullOrWhiteSpace(pdfLanguage))
            return DefaultPdfLanguage;

        var normalized = pdfLanguage.Trim().Replace('_', '-');
        try
        {
            var culture = CultureInfo.GetCultureInfo(normalized);
            return string.IsNullOrWhiteSpace(culture.Name)
                ? DefaultPdfLanguage
                : culture.Name;
        }
        catch (CultureNotFoundException)
        {
            return DefaultPdfLanguage;
        }
    }

    public static bool TryCreatePageRange(string fromText, string toText, out ExportPageRange? range, out string? error)
    {
        range = null;
        error = null;

        var fromBlank = string.IsNullOrWhiteSpace(fromText);
        var toBlank = string.IsNullOrWhiteSpace(toText);
        if (fromBlank && toBlank)
            return true;

        if (!int.TryParse(fromText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromPage) ||
            !int.TryParse(toText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var toPage))
        {
            error = "Page range must include whole-number From and To values.";
            return false;
        }

        if (fromPage < 1 || toPage < 1)
        {
            error = "Page numbers must be 1 or greater.";
            return false;
        }

        if (fromPage > toPage)
        {
            error = "From page must be less than or equal to To page.";
            return false;
        }

        range = new ExportPageRange(fromPage, toPage);
        return true;
    }

    public static bool TryValidatePageRange(ExportPageRange? range, int pageCount, out string? error)
    {
        error = null;

        if (pageCount <= 0)
        {
            error = "There are no exportable pages.";
            return false;
        }

        if (range is null || range.FromPage <= pageCount)
            return true;

        error = $"Page range starts after the last exportable page ({pageCount}).";
        return false;
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

    private static string? DescribeOpenMode(PdfOpenMode openMode) =>
        openMode switch
        {
            PdfOpenMode.Outlines => "opens with bookmarks visible",
            PdfOpenMode.FullScreen => "opens full screen",
            _ => null
        };
}
