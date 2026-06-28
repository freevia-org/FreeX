using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum ExportFormat
{
    Xps,
    Pdf
}

public enum ExportContentScope
{
    ActiveSheet,
    Selection,
    EntireWorkbook
}

public enum ExportQuality
{
    Standard,
    MinimumSize
}

public enum PdfBookmarkMode
{
    None,
    SheetNames,
    PrintTitles,
    PageNumbers
}

public enum PdfInitialView
{
    SinglePage,
    OneColumn,
    TwoColumnLeft,
    TwoColumnRight
}

public enum PdfOpenMode
{
    Normal,
    Outlines,
    FullScreen
}

public enum PdfConformance
{
    Standard,
    PdfA1b
}

public sealed record ExportPageRange(int FromPage, int ToPage)
{
    public override string ToString() =>
        ExportPlanner.FormatPageRange(this);
}

public sealed record ExportOptions(
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

public sealed record ExportRequest(
    string Path,
    ExportFormat Format,
    ExportOptions Options,
    string? FallbackPath)
{
    public bool UsesXpsFallback => FallbackPath is not null;
    public string ActualPath => FallbackPath ?? Path;
}

public sealed class ExportPlannerTextResolver
{
    private static readonly IReadOnlyDictionary<string, string> EnglishText = new Dictionary<string, string>
    {
        ["Export_InvalidPdfLanguage"] = "Enter a valid PDF language tag, for example {0}.",
        ["Export_NoExportablePagesError"] = "There are no exportable pages.",
        ["Export_PageRangeEndsAfterLastPage"] = "Page range ends after the last exportable page ({0}).",
        ["Export_PageRangeFromLessThanToError"] = "From page must be less than or equal to To page.",
        ["Export_PageRangeMultiple"] = "pages {0}-{1}",
        ["Export_PageRangePositiveError"] = "Page numbers must be 1 or greater.",
        ["Export_PageRangeSingle"] = "page {0}",
        ["Export_PageRangeStartsAfterLastPage"] = "Page range starts after the last exportable page ({0}).",
        ["Export_PageRangeWholeNumbersError"] = "Page range must include whole-number From and To values.",
        ["Export_PdfAUnsupportedError"] = "PDF/A compliance is not supported by the current PDF exporter.",
        ["Export_TaggedPdfUnsupportedError"] = "Tagged PDF structure is not supported by the current PDF exporter."
    };

    public static ExportPlannerTextResolver InvariantEnglish { get; } =
        new(
            key => EnglishText.TryGetValue(key, out var text) ? text : key,
            (key, args) => string.Format(
                CultureInfo.CurrentCulture,
                EnglishText.TryGetValue(key, out var text) ? text : key,
                args));

    private readonly Func<string, string> _get;
    private readonly Func<string, object?[], string> _format;

    public ExportPlannerTextResolver(Func<string, string> get, Func<string, object?[], string> format)
    {
        _get = get ?? throw new ArgumentNullException(nameof(get));
        _format = format ?? throw new ArgumentNullException(nameof(format));
    }

    public string Get(string key) => _get(key);

    public string Format(string key, params object?[] args) => _format(key, args);
}

public static class ExportPlanner
{
    public const string DefaultPdfLanguage = "en-US";

    public static ExportFormat InferExportFormat(string path) =>
        FromExportFileFormat(ExportPathPlanner.InferFormat(path));

    public static ExportRequest PlanExport(string path) =>
        PlanExport(path, ExportOptions.ExcelLikeDefault);

    public static ExportRequest PlanExport(string path, ExportOptions options)
    {
        var plan = ExportPathPlanner.Plan(path);
        return new ExportRequest(plan.Path, FromExportFileFormat(plan.Format), options, plan.FallbackPath);
    }

    public static ExportRequest PlanExport(string path, ExportFormat format, ExportOptions options)
    {
        var plan = ExportPathPlanner.Plan(path, ToExportFileFormat(format));
        return new ExportRequest(plan.Path, format, options, plan.FallbackPath);
    }

    public static bool ShouldPromptForNormalizedOverwrite(
        string requestedPath,
        ExportRequest request,
        Func<string, bool> pathExists)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = new ExportPathPlan(request.Path, ToExportFileFormat(request.Format), request.FallbackPath);
        return ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, plan, pathExists);
    }

    public static string GetFallbackXpsPath(string requestedPath) =>
        ExportPathPlanner.GetFallbackXpsPath(requestedPath);

    public static ExportFormat FromExportFileFormat(ExportFileFormat format) =>
        format == ExportFileFormat.Xps
            ? ExportFormat.Xps
            : ExportFormat.Pdf;

    public static ExportFileFormat ToExportFileFormat(ExportFormat format) =>
        format == ExportFormat.Xps
            ? ExportFileFormat.Xps
            : ExportFileFormat.Pdf;

    public static string NormalizePdfLanguage(string? pdfLanguage)
    {
        return TryNormalizePdfLanguage(pdfLanguage, out var normalized, out _)
            ? normalized
            : DefaultPdfLanguage;
    }

    public static bool TryNormalizePdfLanguage(
        string? pdfLanguage,
        out string normalized,
        out string? error,
        ExportPlannerTextResolver? textResolver = null)
    {
        var text = textResolver ?? ExportPlannerTextResolver.InvariantEnglish;
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
            error = text.Format("Export_InvalidPdfLanguage", DefaultPdfLanguage);
            return false;
        }
    }

    public static bool TryCreatePageRange(
        string fromText,
        string toText,
        out ExportPageRange? range,
        out string? error,
        ExportPlannerTextResolver? textResolver = null)
    {
        var text = textResolver ?? ExportPlannerTextResolver.InvariantEnglish;
        range = null;
        error = null;

        var fromBlank = string.IsNullOrWhiteSpace(fromText);
        var toBlank = string.IsNullOrWhiteSpace(toText);
        if (fromBlank && toBlank)
            return true;

        if (!int.TryParse(fromText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromPage) ||
            !int.TryParse(toText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var toPage))
        {
            error = text.Get("Export_PageRangeWholeNumbersError");
            return false;
        }

        if (fromPage < 1 || toPage < 1)
        {
            error = text.Get("Export_PageRangePositiveError");
            return false;
        }

        if (fromPage > toPage)
        {
            error = text.Get("Export_PageRangeFromLessThanToError");
            return false;
        }

        range = new ExportPageRange(fromPage, toPage);
        return true;
    }

    public static bool TryValidatePageRange(
        ExportPageRange? range,
        int pageCount,
        out string? error,
        ExportPlannerTextResolver? textResolver = null)
    {
        var text = textResolver ?? ExportPlannerTextResolver.InvariantEnglish;
        error = null;

        if (pageCount <= 0)
        {
            error = text.Get("Export_NoExportablePagesError");
            return false;
        }

        if (range is null)
            return true;

        if (range.FromPage > pageCount)
        {
            error = text.Format("Export_PageRangeStartsAfterLastPage", pageCount);
            return false;
        }

        if (range.ToPage > pageCount)
        {
            error = text.Format("Export_PageRangeEndsAfterLastPage", pageCount);
            return false;
        }

        return true;
    }

    public static bool TryValidatePublishOptions(
        ExportOptions options,
        ExportFormat format,
        out string? error,
        ExportPlannerTextResolver? textResolver = null)
    {
        var text = textResolver ?? ExportPlannerTextResolver.InvariantEnglish;
        error = null;

        if (format == ExportFormat.Xps)
            return true;

        if (options.PdfConformance != PdfConformance.Standard)
        {
            error = text.Get("Export_PdfAUnsupportedError");
            return false;
        }

        if (options.IncludeDocumentStructureTags)
        {
            error = text.Get("Export_TaggedPdfUnsupportedError");
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

    public static string FormatPageRange(
        ExportPageRange range,
        ExportPlannerTextResolver? textResolver = null)
    {
        ArgumentNullException.ThrowIfNull(range);

        var text = textResolver ?? ExportPlannerTextResolver.InvariantEnglish;
        return range.FromPage == range.ToPage
            ? text.Format("Export_PageRangeSingle", range.FromPage)
            : text.Format("Export_PageRangeMultiple", range.FromPage, range.ToPage);
    }
}
