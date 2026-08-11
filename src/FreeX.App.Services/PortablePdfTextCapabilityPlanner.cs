using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum PortablePdfTextCapabilityPlanStatus
{
    Ready,
    ExportPlanNotReady,
    PageContentPlanNotReady,
    UnsupportedUnicodeText
}

public enum PortablePdfTextRunSource
{
    WorkbookName,
    PageHeader,
    Cell,
    PageFooter
}

public sealed record PortablePdfUnsupportedUnicodeTextDiagnostic(
    PortablePdfTextRunSource Source,
    int ExportPageNumber,
    string SheetName,
    uint? Row,
    uint? Column,
    string Text,
    IReadOnlyList<PdfUnsupportedUnicodeScalar> UnsupportedScalars);

public sealed record PortablePdfTextCapabilityPlan(
    PortablePdfTextCapabilityPlanStatus Status,
    string StatusText,
    IReadOnlyList<PortablePdfUnsupportedUnicodeTextDiagnostic> UnsupportedTextDiagnostics)
{
    public bool IsReady => Status == PortablePdfTextCapabilityPlanStatus.Ready;
}

public static class PortablePdfTextCapabilityPlanner
{
    public static PortablePdfTextCapabilityPlan CreatePlan(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        PortablePdfDocumentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);

        if (!exportPlan.IsReady)
        {
            return new PortablePdfTextCapabilityPlan(
                PortablePdfTextCapabilityPlanStatus.ExportPlanNotReady,
                $"Portable PDF text preflight cannot start because the export plan is not ready: {exportPlan.StatusText}",
                []);
        }

        options ??= new PortablePdfDocumentOptions();
        var diagnostics = new List<PortablePdfUnsupportedUnicodeTextDiagnostic>();
        var textRunCount = 0;
        foreach (var request in exportPlan.PageRequests)
        {
            var contentPlan = PortablePdfPageContentPlanner.CreatePlan(workbook, request);
            if (!contentPlan.IsReady)
            {
                return new PortablePdfTextCapabilityPlan(
                    PortablePdfTextCapabilityPlanStatus.PageContentPlanNotReady,
                    $"Portable PDF text preflight cannot inspect page {request.ExportPageNumber}: {contentPlan.StatusText}",
                    diagnostics);
            }

            AddTextRun(
                PortablePdfTextRunSource.WorkbookName,
                request,
                row: null,
                column: null,
                string.IsNullOrWhiteSpace(workbook.Name) ? "FreeX Workbook" : workbook.Name.Trim(),
                diagnostics,
                ref textRunCount);
            AddTextRun(
                PortablePdfTextRunSource.PageHeader,
                request,
                row: null,
                column: null,
                $"{request.SheetName} - sheet page {request.SheetPageNumber} - export page {request.ExportPageNumber} of {exportPlan.TotalPageCount}",
                diagnostics,
                ref textRunCount);

            foreach (var cell in contentPlan.Cells)
            {
                if (string.IsNullOrEmpty(cell.DisplayText))
                    continue;

                AddTextRun(
                    PortablePdfTextRunSource.Cell,
                    request,
                    cell.Row,
                    cell.Column,
                    PdfWinAnsiTextCapability.Truncate(cell.DisplayText, options.MaximumCellTextLength),
                    diagnostics,
                    ref textRunCount);
            }

            AddTextRun(
                PortablePdfTextRunSource.PageFooter,
                request,
                row: null,
                column: null,
                $"FreeX portable PDF - {request.SheetName} page {request.SheetPageNumber}",
                diagnostics,
                ref textRunCount);
        }

        if (diagnostics.Count > 0)
        {
            return new PortablePdfTextCapabilityPlan(
                PortablePdfTextCapabilityPlanStatus.UnsupportedUnicodeText,
                BuildUnsupportedUnicodeStatusText(diagnostics),
                diagnostics);
        }

        return new PortablePdfTextCapabilityPlan(
            PortablePdfTextCapabilityPlanStatus.Ready,
            $"Ready to render portable PDF text with ASCII/WinAnsi built-in Helvetica support: {textRunCount} {Pluralize(textRunCount, "text run")} across {exportPlan.TotalPageCount} {Pluralize(exportPlan.TotalPageCount, "page")}.",
            []);
    }

    private static void AddTextRun(
        PortablePdfTextRunSource source,
        PortablePdfExportPageRequest request,
        uint? row,
        uint? column,
        string text,
        List<PortablePdfUnsupportedUnicodeTextDiagnostic> diagnostics,
        ref int textRunCount)
    {
        if (string.IsNullOrEmpty(text))
            return;

        textRunCount++;
        var normalized = PdfWinAnsiTextCapability.NormalizePdfText(text);
        var unsupportedScalars = PdfWinAnsiTextCapability.FindUnsupportedUnicodeScalars(normalized);
        if (unsupportedScalars.Count == 0)
            return;

        diagnostics.Add(new PortablePdfUnsupportedUnicodeTextDiagnostic(
            source,
            request.ExportPageNumber,
            request.SheetName,
            row,
            column,
            normalized,
            unsupportedScalars));
    }

    private static string BuildUnsupportedUnicodeStatusText(
        IReadOnlyList<PortablePdfUnsupportedUnicodeTextDiagnostic> diagnostics)
    {
        var summary = string.Join(
            "; ",
            diagnostics.Take(3).Select(diagnostic =>
                $"{FormatLocation(diagnostic)} contains {FormatCodePoints(diagnostic.UnsupportedScalars)}"));
        if (diagnostics.Count > 3)
            summary += $"; plus {diagnostics.Count - 3} more text {Pluralize(diagnostics.Count - 3, "run")}";

        return $"{PdfWinAnsiTextCapability.UnsupportedUnicodeTextMessage} Unsupported text: {summary}.";
    }

    private static string FormatLocation(PortablePdfUnsupportedUnicodeTextDiagnostic diagnostic) =>
        diagnostic.Source switch
        {
            PortablePdfTextRunSource.WorkbookName => $"workbook name on export page {diagnostic.ExportPageNumber}",
            PortablePdfTextRunSource.PageHeader => $"page header on export page {diagnostic.ExportPageNumber}",
            PortablePdfTextRunSource.Cell => $"cell {FormatCellReference(diagnostic.Row, diagnostic.Column)} on export page {diagnostic.ExportPageNumber}",
            PortablePdfTextRunSource.PageFooter => $"page footer on export page {diagnostic.ExportPageNumber}",
            _ => $"text run on export page {diagnostic.ExportPageNumber}"
        };

    private static string FormatCodePoints(IReadOnlyList<PdfUnsupportedUnicodeScalar> unsupportedScalars) =>
        string.Join(", ", unsupportedScalars.Select(scalar => scalar.CodePoint).Distinct(StringComparer.Ordinal));

    private static string FormatCellReference(uint? row, uint? column) =>
        row is null || column is null
            ? "unknown"
            : $"{CellAddress.NumberToColumnName(column.Value)}{row.Value}";

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : $"{singular}s";
}
