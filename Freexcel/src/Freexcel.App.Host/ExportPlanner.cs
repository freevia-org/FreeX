using System.IO;

namespace Freexcel.App.Host;

public enum ExportFormat
{
    Xps,
    PdfViaWindowsPrinter
}

public sealed record ExportRequest(string Path, ExportFormat Format);

internal static class ExportPlanner
{
    public const string PdfFallbackMessage =
        "Windows Print to PDF is unavailable. Exported XPS instead; use a PDF printer or convert the XPS file.";

    public static ExportFormat InferExportFormat(string path) =>
        string.Equals(Path.GetExtension(path), ".xps", StringComparison.OrdinalIgnoreCase)
            ? ExportFormat.Xps
            : ExportFormat.PdfViaWindowsPrinter;

    public static ExportRequest PlanExport(string path) =>
        new(path, InferExportFormat(path));

    public static string GetFallbackXpsPath(string requestedPath) =>
        Path.ChangeExtension(requestedPath, ".xps");
}
