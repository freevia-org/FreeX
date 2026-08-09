using FreeX.Core.Model;
using FileDialogPickerTypeDescriptor = Free.Shared.IO.FileDialogPickerTypeDescriptor;

namespace FreeX.App.Services;

public sealed record ExportSaveDialogPlan(
    string SuggestedFileName,
    string DefaultExtensionWithDot,
    int DefaultFilterIndex);

public sealed record ExportSavePickerPlan(
    IReadOnlyList<FileDialogPickerTypeDescriptor> FileTypes,
    string SuggestedFileName,
    string DefaultExtensionWithoutDot);

public sealed record PortablePdfSaveTargetPlan(string Path, bool ShouldConfirmNormalizedOverwrite);

/// <summary>
/// UI-free file picker metadata for PDF/XPS export. Renderers still own native dialog construction and
/// localized titles; this planner owns stable extensions, suggested names, picker types, and format mapping.
/// </summary>
public static class ExportFilePickerPlanner
{
    public const string PdfExtensionWithDot = ExportFormatCatalog.PdfExtensionWithDot;
    public const string XpsExtensionWithDot = ExportFormatCatalog.XpsExtensionWithDot;
    public const string PdfPickerDisplayName = ExportFormatCatalog.PdfPickerDisplayName;
    public const string XpsPickerDisplayName = ExportFormatCatalog.XpsPickerDisplayName;
    public const string DefaultExportDisplayName = "FreeX";
    public const int PdfXpsDialogPdfFilterIndex = ExportFormatCatalog.PdfXpsDialogPdfFilterIndex;
    public const int PdfXpsDialogXpsFilterIndex = ExportFormatCatalog.PdfXpsDialogXpsFilterIndex;

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
        ExportFormatCatalog.FromPdfXpsFilterIndex(filterIndex).FileFormat;

    public static PortablePdfSaveTargetPlan BuildPortablePdfSaveTargetPlan(
        string requestedPath,
        Func<string, bool> pathExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        ArgumentNullException.ThrowIfNull(pathExists);

        var pathPlan = ExportPathPlanner.Plan(requestedPath, ExportFileFormat.Pdf);
        return new PortablePdfSaveTargetPlan(
            pathPlan.Path,
            ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, pathPlan, pathExists));
    }

    public static string BuildSuggestedExportFileName(
        string? sourceName,
        string fallbackDisplayName,
        ExportFileFormat format) =>
        BuildSuggestedExportBaseName(sourceName, fallbackDisplayName) + ExtensionFor(format);

    public static FileDialogPickerTypeDescriptor BuildPickerType(ExportFileFormat format) =>
        new(
            ExportFormatCatalog.Get(format).PickerDisplayName,
            ["*" + ExportFormatCatalog.Get(format).ExtensionWithDot]);

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
        ExportFormatCatalog.Get(format).ExtensionWithDot;
}
