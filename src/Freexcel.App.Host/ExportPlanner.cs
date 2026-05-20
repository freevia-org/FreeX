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

internal sealed record ExportOptions(
    ExportContentScope Scope,
    bool IncludeDocumentProperties,
    bool OpenAfterPublish)
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
        return new ExportRequest(path, format, options, null);
    }

    public static string GetFallbackXpsPath(string requestedPath) =>
        Path.ChangeExtension(requestedPath, ".xps");

    public static string DescribeOptions(ExportOptions options)
    {
        var scope = options.Scope switch
        {
            ExportContentScope.ActiveSheet => "Active sheet only",
            ExportContentScope.Selection => "Selection",
            ExportContentScope.EntireWorkbook => "Entire workbook",
            _ => "Active sheet only"
        };
        var properties = options.IncludeDocumentProperties
            ? "document properties are included"
            : "document properties are not included";
        var open = options.OpenAfterPublish
            ? "open after publishing"
            : null;

        return open is null
            ? $"{scope}; {properties}."
            : $"{scope}; {properties}; {open}.";
    }

    public static string DescribeRequest(ExportRequest request)
    {
        var options = DescribeOptions(request.Options);
        return request.UsesXpsFallback
            ? $"{PdfFallbackMessage}\n\nOptions: {options}"
            : $"Options: {options}";
    }
}
