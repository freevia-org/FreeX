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
    bool CreateBookmarks = false)
{
    public static ExportOptions ExcelLikeDefault { get; } =
        new(ExportContentScope.ActiveSheet, IncludeDocumentProperties: false, OpenAfterPublish: false);
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
        return PlanExport(path, format, options);
    }

    public static ExportRequest PlanExport(string path, ExportFormat format, ExportOptions options)
    {
        var normalizedPath = NormalizeExportPath(path, format);
        return new ExportRequest(normalizedPath, format, options, null);
    }

    public static string GetFallbackXpsPath(string requestedPath) =>
        Path.ChangeExtension(requestedPath, ".xps");

    private static string NormalizeExportPath(string path, ExportFormat format)
    {
        if (!string.IsNullOrEmpty(Path.GetExtension(path)))
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
        var properties = options.IncludeDocumentProperties
            ? "document properties are included"
            : "document properties are not included";
        var bookmarks = options.CreateBookmarks
            ? "bookmarks use sheet names"
            : null;
        var open = options.OpenAfterPublish
            ? "open after publishing"
            : null;

        return JoinOptionParts(scope, pageRange, quality, printAreas, properties, bookmarks, open);
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
        var quality = DescribeQuality(options.Quality);
        var printAreas = options.IgnorePrintAreas
            ? "print areas are ignored"
            : null;
        var properties = (options.IncludeDocumentProperties, format) switch
        {
            (true, ExportFormat.Pdf) => "document properties are included",
            (true, ExportFormat.Xps) => "document properties are included",
            _ => "document properties are not included"
        };
        var bookmarks = options.CreateBookmarks
            ? format == ExportFormat.Pdf
                ? "bookmarks use sheet names"
                : "bookmarks are PDF-only"
            : null;
        var open = options.OpenAfterPublish
            ? "open after publishing"
            : null;

        return JoinOptionParts(scope, pageRange, quality, printAreas, properties, bookmarks, open);
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
}
