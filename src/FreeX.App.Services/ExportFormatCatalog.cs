using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record ExportFormatDefinition(
    WorkbookExportPrintOutputKind OutputKind,
    ExportFileFormat FileFormat,
    ExportFormat Format,
    string ExtensionWithDot,
    string PickerDisplayName,
    int PdfXpsFilterIndex,
    bool IsPortable);

/// <summary>
/// Canonical format and scope conversions for renderer-owned export surfaces. Native pickers and
/// exporters consume these definitions without repeating extension, filter-index, or enum mappings.
/// </summary>
public static class ExportFormatCatalog
{
    public const string PdfExtensionWithDot = ".pdf";
    public const string XpsExtensionWithDot = ".xps";
    public const string PdfPickerDisplayName = "PDF Document";
    public const string XpsPickerDisplayName = "XPS Document";
    public const int PdfXpsDialogPdfFilterIndex = 1;
    public const int PdfXpsDialogXpsFilterIndex = 2;

    public static ExportFormatDefinition Pdf { get; } = new(
        WorkbookExportPrintOutputKind.Pdf,
        ExportFileFormat.Pdf,
        ExportFormat.Pdf,
        PdfExtensionWithDot,
        PdfPickerDisplayName,
        PdfXpsDialogPdfFilterIndex,
        IsPortable: true);

    public static ExportFormatDefinition Xps { get; } = new(
        WorkbookExportPrintOutputKind.Xps,
        ExportFileFormat.Xps,
        ExportFormat.Xps,
        XpsExtensionWithDot,
        XpsPickerDisplayName,
        PdfXpsDialogXpsFilterIndex,
        IsPortable: false);

    public static IReadOnlyList<ExportFormatDefinition> All { get; } = [Pdf, Xps];

    public static ExportFormatDefinition Get(WorkbookExportPrintOutputKind outputKind) =>
        outputKind == WorkbookExportPrintOutputKind.Xps ? Xps : Pdf;

    public static ExportFormatDefinition Get(ExportFileFormat fileFormat) =>
        fileFormat == ExportFileFormat.Xps ? Xps : Pdf;

    public static ExportFormatDefinition Get(ExportFormat format) =>
        format == ExportFormat.Xps ? Xps : Pdf;

    public static ExportFormatDefinition FromPdfXpsFilterIndex(int filterIndex) =>
        filterIndex == PdfXpsDialogXpsFilterIndex ? Xps : Pdf;

    public static ExportContentScope ToContentScope(WorkbookExportPrintScope scope) =>
        scope switch
        {
            WorkbookExportPrintScope.SelectedRange => ExportContentScope.Selection,
            WorkbookExportPrintScope.VisibleWorkbook => ExportContentScope.EntireWorkbook,
            _ => ExportContentScope.ActiveSheet
        };

    public static WorkbookExportPrintScope ToPrintScope(ExportContentScope scope) =>
        scope switch
        {
            ExportContentScope.Selection => WorkbookExportPrintScope.SelectedRange,
            ExportContentScope.EntireWorkbook => WorkbookExportPrintScope.VisibleWorkbook,
            _ => WorkbookExportPrintScope.ActiveSheet
        };
}
