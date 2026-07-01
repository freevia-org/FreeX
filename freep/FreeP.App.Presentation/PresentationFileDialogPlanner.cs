using Free.Shared.IO;

namespace FreeP.App.Compositor;

/// <summary>
/// Shared FreeP document picker/dialog policy. Platform hosts adapt these neutral plans to WPF or Avalonia pickers.
/// </summary>
public static class PresentationFileDialogPlanner
{
    public const string DefaultPresentationExtension = PresentationFilePersistenceWorkflow.DefaultPresentationExtension;
    public const string LegacyFxpExtension = PresentationFilePersistenceWorkflow.LegacyFxpExtension;
    public const string PdfExportExtension = PresentationExportPlanner.PdfExportExtension;

    private const string FallbackPresentationName = "Presentation";

    private static readonly IReadOnlyList<FileDialogFormatDescriptor> PresentationFormats =
    [
        new FileDialogFormatDescriptor(DefaultPresentationExtension, "PowerPoint presentations"),
        new FileDialogFormatDescriptor(LegacyFxpExtension, "FreeP legacy presentations"),
    ];

    public static FileOpenDialogPlan BuildOpenDialogPlan() =>
        FileDialogRequestPlanner.BuildPerFormatOpenDialogPlan(PresentationFormats);

    public static FileSaveDialogPlan BuildSaveAsDialogPlan(string? sourceName) =>
        FileDialogRequestPlanner.BuildPerFormatSaveDialogPlanFromSourceName(
            PresentationFormats,
            sourceName,
            FallbackPresentationName,
            DefaultPresentationExtension);

    public static FileSaveDialogPlan BuildPdfExportDialogPlan(string? sourceName) =>
        PresentationExportPlanner.BuildPdfExportDialogPlan(sourceName);

    public static FileOpenPickerPlan BuildOpenPickerPlan() =>
        FileDialogRequestPlanner.BuildOpenPickerPlan(
            PresentationFormats,
            allSupportedName: "All supported presentations");

    public static FileSavePickerPlan BuildSavePickerPlan(string? sourceName) =>
        FileDialogRequestPlanner.BuildSavePickerPlan(
            PresentationFormats,
            sourceName,
            FallbackPresentationName,
            DefaultPresentationExtension,
            preferredFirstExtension: DefaultPresentationExtension);

    public static FileSavePickerPlan BuildPdfExportPickerPlan(string? sourceName) =>
        PresentationExportPlanner.BuildPdfExportPickerPlan(sourceName);

    public static bool IsLegacyPresentationPath(string path) =>
        PresentationFilePersistenceWorkflow.IsLegacyPresentationPath(path);
}
