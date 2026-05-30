using System.Globalization;
using System.IO;

namespace FreeX.App.Host;

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
            ? UiText.Format("Export_PageRangeSingle", FromPage)
            : UiText.Format("Export_PageRangeMultiple", FromPage, ToPage);
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

    public static string PdfFallbackMessage => UiText.Get("Export_PdfFallbackMessage");

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
            error = UiText.Format("Export_InvalidPdfLanguage", DefaultPdfLanguage);
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
            error = UiText.Get("Export_PageRangeWholeNumbersError");
            return false;
        }

        if (fromPage < 1 || toPage < 1)
        {
            error = UiText.Get("Export_PageRangePositiveError");
            return false;
        }

        if (fromPage > toPage)
        {
            error = UiText.Get("Export_PageRangeFromLessThanToError");
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
            error = UiText.Get("Export_NoExportablePagesError");
            return false;
        }

        if (range is null)
            return true;

        if (range.FromPage > pageCount)
        {
            error = UiText.Format("Export_PageRangeStartsAfterLastPage", pageCount);
            return false;
        }

        if (range.ToPage > pageCount)
        {
            error = UiText.Format("Export_PageRangeEndsAfterLastPage", pageCount);
            return false;
        }

        return true;
    }

    public static bool TryValidatePublishOptions(ExportOptions options, ExportFormat format, out string? error)
    {
        error = null;

        if (format == ExportFormat.Xps)
            return true;

        if (options.PdfConformance != PdfConformance.Standard)
        {
            error = UiText.Get("Export_PdfAUnsupportedError");
            return false;
        }

        if (options.IncludeDocumentStructureTags)
        {
            error = UiText.Get("Export_TaggedPdfUnsupportedError");
            return false;
        }

        return true;
    }

    public static ExportOptions CreateEffectiveOptionsForFormat(ExportOptions options, ExportFormat format)
    {
        var normalized = options with
        {
            PdfLanguage = NormalizePdfLanguage(options.PdfLanguage)
        };

        if (format == ExportFormat.Pdf)
            return normalized;

        return normalized with
        {
            Quality = ExportQuality.Standard,
            CreateBookmarks = false,
            BookmarkMode = PdfBookmarkMode.None,
            InitialView = PdfInitialView.SinglePage,
            OpenMode = PdfOpenMode.Normal,
            BitmapTextWhenFontsMayNotBeEmbedded = false,
            PdfLanguage = DefaultPdfLanguage,
            PdfConformance = PdfConformance.Standard,
            IncludeDocumentStructureTags = false
        };
    }

}
