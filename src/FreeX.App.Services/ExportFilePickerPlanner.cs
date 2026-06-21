using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record ExportSaveDialogPlan(
    string SuggestedFileName,
    string DefaultExtensionWithDot,
    int DefaultFilterIndex);

public sealed record ExportSavePickerPlan(
    IReadOnlyList<FilePickerTypeDescriptor> FileTypes,
    string SuggestedFileName,
    string DefaultExtensionWithoutDot);

/// <summary>
/// UI-free file picker metadata for PDF/XPS export. Renderers still own native dialog construction and
/// localized titles; this planner owns stable extensions, suggested names, picker types, and format mapping.
/// </summary>
public static class ExportFilePickerPlanner
{
    public const string PdfExtensionWithDot = ".pdf";
    public const string XpsExtensionWithDot = ".xps";
    public const string PdfPickerDisplayName = "PDF Document";
    public const string XpsPickerDisplayName = "XPS Document";
    public const string DefaultExportDisplayName = "FreeX";
    public const int PdfXpsDialogPdfFilterIndex = 1;
    public const int PdfXpsDialogXpsFilterIndex = 2;

    public static ExportSavePickerPlan BuildPortablePdfPickerPlan(
        string? sourceName,
        string fallbackDisplayName) =>
        new(
            [BuildPickerType(ExportFileFormat.Pdf)],
            BuildSuggestedExportFileName(sourceName, fallbackDisplayName, ExportFileFormat.Pdf),
            PdfExtensionWithDot[1..]);

    public static ExportSaveDialogPlan BuildPdfXpsDialogPlan(
        string? sourceName,
        string fallbackDisplayName) =>
        new(
            BuildSuggestedExportBaseName(sourceName, fallbackDisplayName),
            PdfExtensionWithDot,
            PdfXpsDialogPdfFilterIndex);

    public static ExportFileFormat FormatFromPdfXpsFilterIndex(int filterIndex) =>
        filterIndex == PdfXpsDialogXpsFilterIndex
            ? ExportFileFormat.Xps
            : ExportFileFormat.Pdf;

    public static string BuildSuggestedExportFileName(
        string? sourceName,
        string fallbackDisplayName,
        ExportFileFormat format) =>
        BuildSuggestedExportBaseName(sourceName, fallbackDisplayName) + ExtensionFor(format);

    public static FilePickerTypeDescriptor BuildPickerType(ExportFileFormat format) =>
        new(DisplayNameFor(format), ["*" + ExtensionFor(format)]);

    private static string BuildSuggestedExportBaseName(string? sourceName, string fallbackDisplayName)
    {
        var effectiveFallbackName = string.IsNullOrWhiteSpace(fallbackDisplayName)
            ? DefaultExportDisplayName
            : fallbackDisplayName;
        var effectiveSourceName = string.IsNullOrWhiteSpace(sourceName)
            ? effectiveFallbackName
            : sourceName;
        var baseName = Path.GetFileNameWithoutExtension(effectiveSourceName);
        return string.IsNullOrWhiteSpace(baseName)
            ? effectiveFallbackName
            : baseName;
    }

    private static string ExtensionFor(ExportFileFormat format) =>
        format == ExportFileFormat.Xps
            ? XpsExtensionWithDot
            : PdfExtensionWithDot;

    private static string DisplayNameFor(ExportFileFormat format) =>
        format == ExportFileFormat.Xps
            ? XpsPickerDisplayName
            : PdfPickerDisplayName;
}
