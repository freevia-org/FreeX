using Free.Shared.IO;

namespace FreeP.App.Compositor;

public enum PresentationExportFormat
{
    Pdf,
    ImageSequence,
    Video,
    Print,
}

public enum PresentationPrintLayoutKind
{
    FullPageSlides,
    NotesPages,
    Handouts,
}

public enum PresentationSlideRangeKind
{
    AllSlides,
    CurrentSlide,
    SelectedSlides,
    CustomRange,
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

public sealed record PresentationPrintLayoutDescriptor(
    PresentationPrintLayoutKind Layout,
    string DisplayName,
    string Description,
    int SlidesPerPage,
    bool IncludesSpeakerNotes,
    bool IsHandout);

public sealed record PresentationSlideRangeRequest(
    PresentationSlideRangeKind Kind,
    int? CurrentSlideNumber = null,
    int? StartSlideNumber = null,
    int? EndSlideNumber = null,
    IReadOnlyList<int>? SelectedSlideNumbers = null);

public sealed record PresentationPrintRequest(
    PresentationPrintLayoutKind Layout,
    PresentationSlideRangeRequest? SlideRange = null,
    int? HandoutSlidesPerPage = null,
    bool PrintHiddenSlides = false);

public sealed record PresentationSlideRangePlan(
    PresentationSlideRangeKind Kind,
    IReadOnlyList<int> SlideNumbers,
    string DisplayName);

public sealed record PresentationPrintPlan(
    string CommandId,
    PresentationPrintLayoutDescriptor Layout,
    PresentationSlideRangePlan SlideRange,
    bool PrintHiddenSlides,
    bool IsImplemented);

public sealed record PresentationDeferredExportPlan(
    PresentationExportFormat Format,
    string CommandId,
    string DisplayName,
    string Description,
    string DefaultExtensionWithDot,
    PresentationSlideRangePlan SlideRange,
    bool IsImplemented);

public sealed record PresentationImageExportPlan(
    PresentationExportFormat Format,
    string CommandId,
    string DisplayName,
    string Description,
    string DefaultExtensionWithDot,
    PresentationSlideRangePlan SlideRange,
    int WidthPx,
    int HeightPx,
    bool IsImplemented);

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
    public const string ImageExportExtension = ".png";
    public const string VideoExportExtension = ".mp4";
    public const string PdfExportCommandId = "freep.file.export-pdf";
    public const string ImageExportCommandId = "freep.file.export-images";
    public const string VideoExportCommandId = "freep.file.export-video";
    public const string PrintCommandId = "freep.file.print";
    public const string PdfExportPickerTitle = "Export to PDF";
    public const string PdfExportCommandText = "Export to PDF";

    public static readonly IReadOnlyList<int> HandoutSlidesPerPageOptions = [1, 2, 3, 4, 6, 9];

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
            "One PNG image per selected slide.",
            ImageExportExtension,
            IsImplemented: true),
        new(
            PresentationExportFormat.Video,
            VideoExportCommandId,
            "Video",
            "MP4 video export with timings and narration is planned but not implemented.",
            VideoExportExtension,
            IsImplemented: false),
        new(
            PresentationExportFormat.Print,
            PrintCommandId,
            "Print",
            "PowerPoint-shaped slide, notes-page, and handout print layouts.",
            DefaultExtensionWithDot: null,
            IsImplemented: false),
    ];

    public static IReadOnlyList<PresentationPrintLayoutDescriptor> BuildPrintLayoutDescriptors()
    {
        List<PresentationPrintLayoutDescriptor> descriptors =
        [
            new(
                PresentationPrintLayoutKind.FullPageSlides,
                "Full Page Slides",
                "One full-page slide per printed page.",
                SlidesPerPage: 1,
                IncludesSpeakerNotes: false,
                IsHandout: false),
            new(
                PresentationPrintLayoutKind.NotesPages,
                "Notes Pages",
                "One slide per page with speaker notes.",
                SlidesPerPage: 1,
                IncludesSpeakerNotes: true,
                IsHandout: false),
        ];

        descriptors.AddRange(HandoutSlidesPerPageOptions.Select(slidesPerPage => new PresentationPrintLayoutDescriptor(
            PresentationPrintLayoutKind.Handouts,
            $"Handouts ({slidesPerPage} slide{(slidesPerPage == 1 ? string.Empty : "s")} per page)",
            $"Handout pages with {slidesPerPage} slide{(slidesPerPage == 1 ? string.Empty : "s")} per page.",
            slidesPerPage,
            IncludesSpeakerNotes: false,
            IsHandout: true)));

        return descriptors;
    }

    public static PresentationPrintPlan BuildPrintPlan(PresentationPrintRequest? request, int slideCount)
    {
        request ??= new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides);
        var layout = NormalizePrintLayout(request.Layout, request.HandoutSlidesPerPage);
        var range = BuildSlideRangePlan(request.SlideRange, slideCount);

        return new PresentationPrintPlan(
            PrintCommandId,
            layout,
            range,
            request.PrintHiddenSlides,
            IsImplemented: false);
    }

    public static PresentationImageExportPlan BuildImageExportPlan(
        PresentationSlideRangeRequest? range,
        int slideCount,
        int widthPx = PresentationImageExportExecutor.DefaultWidthPx,
        int heightPx = PresentationImageExportExecutor.DefaultHeightPx)
    {
        var descriptor = BuildFormatDescriptors().Single(d => d.Format == PresentationExportFormat.ImageSequence);

        return new PresentationImageExportPlan(
            descriptor.Format,
            descriptor.CommandId,
            descriptor.DisplayName,
            "Exports one PNG image per selected slide using the shared slide-range policy and host render callback.",
            descriptor.DefaultExtensionWithDot ?? ImageExportExtension,
            BuildSlideRangePlan(range, slideCount),
            Math.Max(1, widthPx),
            Math.Max(1, heightPx),
            descriptor.IsImplemented);
    }

    public static PresentationDeferredExportPlan BuildVideoExportPlan(
        PresentationSlideRangeRequest? range,
        int slideCount) =>
        BuildDeferredExportPlan(
            PresentationExportFormat.Video,
            range,
            slideCount,
            "Planned MP4 export; timings, narration, and encoder integration are deferred.");

    public static PresentationSlideRangePlan BuildSlideRangePlan(
        PresentationSlideRangeRequest? request,
        int slideCount)
    {
        var count = Math.Max(0, slideCount);
        request ??= new PresentationSlideRangeRequest(PresentationSlideRangeKind.AllSlides);

        var numbers = request.Kind switch
        {
            PresentationSlideRangeKind.CurrentSlide => count == 0
                ? []
                : [ClampSlideNumber(request.CurrentSlideNumber ?? 1, count)],
            PresentationSlideRangeKind.SelectedSlides => NormalizeSelectedSlides(request.SelectedSlideNumbers, count),
            PresentationSlideRangeKind.CustomRange => NormalizeCustomRange(
                request.StartSlideNumber,
                request.EndSlideNumber,
                count),
            _ => BuildAllSlides(count),
        };

        return new PresentationSlideRangePlan(
            request.Kind,
            numbers,
            FormatRangeDisplayName(request.Kind, numbers, count));
    }

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

    private static PresentationPrintLayoutDescriptor NormalizePrintLayout(
        PresentationPrintLayoutKind layout,
        int? requestedHandoutSlidesPerPage)
    {
        if (layout is not PresentationPrintLayoutKind.Handouts)
            return BuildPrintLayoutDescriptors().Single(descriptor => descriptor.Layout == layout);

        var slidesPerPage = NormalizeHandoutSlidesPerPage(requestedHandoutSlidesPerPage);
        return BuildPrintLayoutDescriptors().Single(descriptor =>
            descriptor.Layout == PresentationPrintLayoutKind.Handouts &&
            descriptor.SlidesPerPage == slidesPerPage);
    }

    private static int NormalizeHandoutSlidesPerPage(int? requested)
    {
        if (requested is null)
            return 6;

        return HandoutSlidesPerPageOptions
            .OrderBy(option => Math.Abs(option - requested.Value))
            .ThenBy(option => option)
            .First();
    }

    private static PresentationDeferredExportPlan BuildDeferredExportPlan(
        PresentationExportFormat format,
        PresentationSlideRangeRequest? range,
        int slideCount,
        string deferredDescription)
    {
        var descriptor = BuildFormatDescriptors().Single(d => d.Format == format);

        return new PresentationDeferredExportPlan(
            descriptor.Format,
            descriptor.CommandId,
            descriptor.DisplayName,
            deferredDescription,
            descriptor.DefaultExtensionWithDot ?? string.Empty,
            BuildSlideRangePlan(range, slideCount),
            descriptor.IsImplemented);
    }

    private static IReadOnlyList<int> BuildAllSlides(int slideCount) =>
        slideCount == 0 ? [] : Enumerable.Range(1, slideCount).ToArray();

    private static IReadOnlyList<int> NormalizeSelectedSlides(IReadOnlyList<int>? selectedSlideNumbers, int slideCount)
    {
        if (slideCount == 0 || selectedSlideNumbers is null)
            return [];

        return selectedSlideNumbers
            .Where(number => number >= 1 && number <= slideCount)
            .Distinct()
            .OrderBy(number => number)
            .ToArray();
    }

    private static IReadOnlyList<int> NormalizeCustomRange(int? requestedStart, int? requestedEnd, int slideCount)
    {
        if (slideCount == 0)
            return [];

        var start = ClampSlideNumber(requestedStart ?? 1, slideCount);
        var end = ClampSlideNumber(requestedEnd ?? start, slideCount);
        if (end < start)
            (start, end) = (end, start);

        return Enumerable.Range(start, end - start + 1).ToArray();
    }

    private static int ClampSlideNumber(int value, int slideCount) =>
        Math.Clamp(value, 1, slideCount);

    private static string FormatRangeDisplayName(
        PresentationSlideRangeKind kind,
        IReadOnlyList<int> slideNumbers,
        int slideCount) =>
        slideNumbers.Count switch
        {
            0 => "No slides",
            _ when kind == PresentationSlideRangeKind.AllSlides && slideNumbers.Count == slideCount => "All slides",
            1 => $"Slide {slideNumbers[0]}",
            _ when slideNumbers[^1] - slideNumbers[0] + 1 == slideNumbers.Count =>
                $"Slides {slideNumbers[0]}-{slideNumbers[^1]}",
            _ => $"Slides {string.Join(", ", slideNumbers)}",
        };

    private static PresentationBackstageExportActionPlan ToActionPlan(
        PresentationExportFormatDescriptor format,
        string label,
        string description) =>
        new(format.Format, format.CommandId, label, description, format.IsImplemented);
}
