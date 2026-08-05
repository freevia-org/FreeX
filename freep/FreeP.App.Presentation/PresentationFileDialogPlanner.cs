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
    public const string UnsupportedSavePathMessage =
        "Choose a PowerPoint presentation, template, slide show, or .fxp file.";

    private const string FallbackPresentationName = "Presentation";

    private static readonly IReadOnlyList<FileDialogFormatDescriptor> PresentationFormats =
    [
        new FileDialogFormatDescriptor(DefaultPresentationExtension, "PowerPoint presentations"),
        new FileDialogFormatDescriptor(PresentationFilePersistenceWorkflow.MacroEnabledPresentationExtension, "PowerPoint macro-enabled presentations"),
        new FileDialogFormatDescriptor(PresentationFilePersistenceWorkflow.TemplateExtension, "PowerPoint templates"),
        new FileDialogFormatDescriptor(PresentationFilePersistenceWorkflow.MacroEnabledTemplateExtension, "PowerPoint macro-enabled templates"),
        new FileDialogFormatDescriptor(PresentationFilePersistenceWorkflow.SlideShowExtension, "PowerPoint slide shows"),
        new FileDialogFormatDescriptor(PresentationFilePersistenceWorkflow.MacroEnabledSlideShowExtension, "PowerPoint macro-enabled slide shows"),
        new FileDialogFormatDescriptor(LegacyFxpExtension, "FreeP legacy presentations"),
    ];

    public static FileOpenDialogPlan BuildOpenDialogPlan() =>
        FileDialogRequestPlanner.BuildPerFormatOpenDialogPlan(PresentationFormats);

    public static FileSaveDialogPlan BuildSaveAsDialogPlan(string? sourceName) =>
        FileDialogRequestPlanner.BuildPerFormatSaveDialogPlanFromSourceName(
            PresentationFormats,
            sourceName,
            FallbackPresentationName,
            GetSaveAsDefaultExtension(sourceName));

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
            GetSaveAsDefaultExtension(sourceName),
            preferredFirstExtension: GetSaveAsDefaultExtension(sourceName));

    public static bool TryResolveSavePickerPath(string path, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (!FilePathPolicy.TryGetFileName(path, out _))
            return false;

        resolvedPath = FilePathPolicy.TryGetExtension(path, out _)
            ? path
            : path + DefaultPresentationExtension;
        if (!PresentationFilePersistenceWorkflow.IsSupportedPresentationPath(resolvedPath))
        {
            resolvedPath = string.Empty;
            return false;
        }

        return true;
    }

    public static FileSavePickerPlan BuildPdfExportPickerPlan(string? sourceName) =>
        PresentationExportPlanner.BuildPdfExportPickerPlan(sourceName);

    public static bool IsLegacyPresentationPath(string path) =>
        PresentationFilePersistenceWorkflow.IsLegacyPresentationPath(path);

    private static string GetSaveAsDefaultExtension(string? sourceName)
    {
        var sourceExtension = FilePathPolicy.GetExtensionOrEmpty(sourceName);
        return PresentationFilePersistenceWorkflow.IsPowerPointPackagePath(sourceName ?? string.Empty)
            ? sourceExtension.ToLowerInvariant()
            : DefaultPresentationExtension;
    }
}
