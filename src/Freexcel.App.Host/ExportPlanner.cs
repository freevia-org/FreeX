using System.IO;

namespace Freexcel.App.Host;

internal enum ExportFormat
{
    Xps,
    PdfViaWindowsPrinter
}

internal enum ExportContentScope
{
    ActiveSheet
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
        "Direct PDF file export is limited by the Windows print pipeline. Exported XPS instead; use a PDF printer or convert the XPS file.";

    public static ExportFormat InferExportFormat(string path) =>
        string.Equals(Path.GetExtension(path), ".xps", StringComparison.OrdinalIgnoreCase)
            ? ExportFormat.Xps
            : ExportFormat.PdfViaWindowsPrinter;

    public static ExportRequest PlanExport(string path) =>
        PlanExport(path, ExportOptions.ExcelLikeDefault);

    public static ExportRequest PlanExport(string path, ExportOptions options)
    {
        var format = InferExportFormat(path);
        var fallbackPath = format == ExportFormat.PdfViaWindowsPrinter
            ? GetFallbackXpsPath(path)
            : null;
        return new ExportRequest(path, format, options, fallbackPath);
    }

    public static string GetFallbackXpsPath(string requestedPath) =>
        Path.ChangeExtension(requestedPath, ".xps");

    public static string DescribeOptions(ExportOptions options)
    {
        var scope = options.Scope switch
        {
            ExportContentScope.ActiveSheet => "Active sheet only",
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
