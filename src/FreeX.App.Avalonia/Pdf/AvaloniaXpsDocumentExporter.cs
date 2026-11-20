using System.IO;
using Free.Shared.Pdf.Skia;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Pdf;

/// <summary>Publishes workbook print pages as a fixed-layout XPS document.</summary>
public static class AvaloniaXpsDocumentExporter
{
    public static AvaloniaXpsDocumentExportResult Save(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        Stream stream,
        string workbookDirectory = "")
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("XPS export requires a writable stream.", nameof(stream));
        if (!exportPlan.IsReady)
            throw new InvalidOperationException(exportPlan.StatusText);
        if (exportPlan.ExportPrintPlan.Intent.OutputKind != WorkbookExportPrintOutputKind.Xps)
            throw new ArgumentException("The export plan must target XPS.", nameof(exportPlan));

        using var textMeasurer = new SkiaPdfTextMeasurer();
        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(
            workbook, exportPlan, workbookDirectory, textMeasurer);
        var diagnostics = new List<string>();
        stream.Write(SkiaXpsWriter.WriteToBytes(document, diagnostics));

        var pageCount = document.Pages.Count;
        var statusText = $"Exported XPS: {pageCount} {(pageCount == 1 ? "page" : "pages")}";
        if (diagnostics.Count > 0)
            statusText += $" ({diagnostics.Count} export warning{(diagnostics.Count == 1 ? "" : "s")})";

        return new AvaloniaXpsDocumentExportResult(pageCount, statusText + ".", diagnostics);
    }
}

public sealed record AvaloniaXpsDocumentExportResult(int PageCount, string StatusText, IReadOnlyList<string> Diagnostics);
