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

internal enum PdfConformance
{
    Standard,
    PdfA1b
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
    string PdfLanguage = ExportPlanner.DefaultPdfLanguage,
    PdfConformance PdfConformance = PdfConformance.Standard,
    bool IncludeDocumentStructureTags = false)
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

internal static partial class ExportPlanner
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

    public static string NormalizePdfLanguage(string? pdfLanguage)
    {
        return TryNormalizePdfLanguage(pdfLanguage, out var normalized, out _)
            ? normalized
            : DefaultPdfLanguage;
    }

    public static bool TryNormalizePdfLanguage(string? pdfLanguage, out string normalized, out string? error)
    {
        normalized = DefaultPdfLanguage;
        error = null;
        if (string.IsNullOrWhiteSpace(pdfLanguage))
            return true;

        var candidate = pdfLanguage.Trim().Replace('_', '-');
        try
        {
            var culture = CultureInfo.GetCultureInfo(candidate);
            if (string.IsNullOrWhiteSpace(culture.Name))
                return true;

            normalized = culture.Name;
            return true;
        }
        catch (CultureNotFoundException)
        {
            error = "Enter a valid PDF language tag, for example en-US.";
            return false;
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

    public static bool TryValidatePublishOptions(ExportOptions options, ExportFormat format, out string? error)
    {
        error = null;

        if (format == ExportFormat.Xps)
            return true;

        if (options.PdfConformance != PdfConformance.Standard)
        {
            error = "PDF/A compliance is not supported by the current PDF exporter.";
            return false;
        }

        if (options.IncludeDocumentStructureTags)
        {
            error = "Tagged PDF structure is not supported by the current PDF exporter.";
            return false;
        }

        return true;
    }

}
