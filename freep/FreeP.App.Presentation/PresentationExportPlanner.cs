using Free.Shared.IO;

namespace FreeP.App.Compositor;

public enum PresentationExportFormat
{
    Pdf,
    ImageSequence,
    Print,
}

public sealed record PresentationExportFormatDescriptor(
    PresentationExportFormat Format,
    string CommandId,
    string DisplayName,
    string Description,
    string? DefaultExtensionWithDot,
    bool IsImplemented);

public sealed record PresentationBackstageExportActionPlan(
    PresentationExportFormat Format,
    string CommandId,
    string Label,
    string Description,
    bool IsEnabled);

public sealed record PresentationBackstageExportPlan(
    string Heading,
    string Description,
    string FixedLayoutGroupHeading,
    IReadOnlyList<PresentationBackstageExportActionPlan> FixedLayoutActions,
    IReadOnlyList<PresentationBackstageExportActionPlan> DeferredActions);

/// <summary>
/// Shared export policy for FreeP. Hosts adapt these plans to native dialogs, Backstage panes, and command routes.
/// </summary>
public static class PresentationExportPlanner
{
    public const string PdfExportExtension = ".pdf";
    public const string PdfExportCommandId = "freep.file.export-pdf";
    public const string ImageExportCommandId = "freep.file.export-images";
    public const string PrintCommandId = "freep.file.print";
    public const string PdfExportPickerTitle = "Export to PDF";
    public const string PdfExportCommandText = "Export to PDF";

    private const string FallbackPresentationName = "Presentation";

    private static readonly IReadOnlyList<FileDialogFormatDescriptor> PdfFormats =
    [
        new FileDialogFormatDescriptor(PdfExportExtension, "PDF documents"),
    ];

    public static IReadOnlyList<PresentationExportFormatDescriptor> BuildFormatDescriptors() =>
    [
        new(
            PresentationExportFormat.Pdf,
            PdfExportCommandId,
            "PDF",
            "Fixed-layout PDF copy with one page per slide.",
            PdfExportExtension,
            IsImplemented: true),
        new(
            PresentationExportFormat.ImageSequence,
            ImageExportCommandId,
            "Images",
            "One image per slide.",
            DefaultExtensionWithDot: null,
            IsImplemented: false),
        new(
            PresentationExportFormat.Print,
            PrintCommandId,
            "Print",
            "Send slides through the platform print surface.",
            DefaultExtensionWithDot: null,
            IsImplemented: false),
    ];

    public static FileSaveDialogPlan BuildPdfExportDialogPlan(string? sourceName) =>
        FileDialogRequestPlanner.BuildPerFormatSaveDialogPlanFromSourceName(
            PdfFormats,
            sourceName,
            FallbackPresentationName,
            PdfExportExtension);

    public static FileSavePickerPlan BuildPdfExportPickerPlan(string? sourceName) =>
        FileDialogRequestPlanner.BuildSavePickerPlan(
            PdfFormats,
            sourceName,
            FallbackPresentationName,
            PdfExportExtension,
            preferredFirstExtension: PdfExportExtension);

    public static PresentationBackstageExportPlan BuildBackstageExportPlan()
    {
        var formats = BuildFormatDescriptors();
        var pdf = formats.Single(format => format.Format == PresentationExportFormat.Pdf);

        return new PresentationBackstageExportPlan(
            Heading: "Export",
            Description: "Create a fixed-layout copy for sharing or presenting.",
            FixedLayoutGroupHeading: "Create PDF Copy",
            FixedLayoutActions:
            [
                ToActionPlan(pdf, "Export to PDF...", pdf.Description),
            ],
            DeferredActions: formats
                .Where(format => format.Format is not PresentationExportFormat.Pdf)
                .Select(format => ToActionPlan(format, format.DisplayName, format.Description))
                .ToArray());
    }

    private static PresentationBackstageExportActionPlan ToActionPlan(
        PresentationExportFormatDescriptor format,
        string label,
        string description) =>
        new(format.Format, format.CommandId, label, description, format.IsImplemented);
}
