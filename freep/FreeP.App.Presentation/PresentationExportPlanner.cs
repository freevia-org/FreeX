using Free.Shared.IO;
using Free.Shared.Drawing;

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

public enum PresentationVideoQualityKind
{
    UltraHd,
    FullHd,
    Hd,
    Standard,
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

public sealed record PresentationHandoutSlideSlot(
    int PageIndex,
    int SlotIndex,
    int SlideIndex,
    int SlideNumber,
    LayoutRect SlideBounds,
    LayoutRect? NotesOrLinesBounds,
    IReadOnlyList<LayoutRect> BlankLineBounds);

public sealed record PresentationHandoutPagePlan(
    int PageIndex,
    IReadOnlyList<PresentationHandoutSlideSlot> Slots);

public sealed record PresentationHandoutLayoutPlan(
    PresentationPrintPlan PrintPlan,
    double PageWidth,
    double PageHeight,
    int PageCount,
    IReadOnlyList<PresentationHandoutPagePlan> Pages);

public sealed record PresentationDeferredExportPlan(
    PresentationExportFormat Format,
    string CommandId,
    string DisplayName,
    string Description,
    string DefaultExtensionWithDot,
    PresentationSlideRangePlan SlideRange,
    bool IsImplemented);

public sealed record PresentationVideoQualityDescriptor(
    PresentationVideoQualityKind Quality,
    string DisplayName,
    int WidthPx,
    int HeightPx,
    int PixelsPerSecondHint);

public sealed record PresentationVideoExportRequest(
    PresentationSlideRangeRequest? SlideRange = null,
    PresentationVideoQualityKind Quality = PresentationVideoQualityKind.FullHd,
    double SecondsPerSlide = 5,
    bool UseRecordedTimings = true,
    bool IncludeNarration = true);

public sealed record PresentationVideoExportPlan(
    PresentationExportFormat Format,
    string CommandId,
    string DisplayName,
    string Description,
    string DefaultExtensionWithDot,
    PresentationSlideRangePlan SlideRange,
    PresentationVideoQualityDescriptor Quality,
    double SecondsPerSlide,
    bool UseRecordedTimings,
    bool IncludeNarration,
    TimeSpan EstimatedDuration,
    IReadOnlyList<PresentationVideoQualityDescriptor> QualityOptions,
    bool IsImplemented,
    bool CanExecute,
    string? DisabledReason);

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
    string DeferredGroupHeading,
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
    public const string ImageExportPickerTitle = "Export Slides as Images";
    public const string ImageExportCommandText = "Export slides as images";
    public const string VideoExportPickerTitle = "Export Video";
    public const string VideoExportCommandText = "Export video";
    public const string VideoExportDeferredMessage =
        "MP4 video export planning is available, but encoder, narration, and media capture execution are deferred.";
    public const double DefaultVideoSecondsPerSlide = 5;
    public const double DefaultPrintPageWidth = 612;
    public const double DefaultPrintPageHeight = 792;
    public const double DefaultHandoutMargin = 36;
    public const double DefaultHandoutGutter = 18;

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
            "MP4 video export with slide range, quality, timings, and narration intent.",
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

    public static PresentationHandoutLayoutPlan BuildHandoutLayoutPlan(
        PresentationPrintRequest? request,
        int slideCount,
        double slideWidth = 16,
        double slideHeight = 9,
        double pageWidth = DefaultPrintPageWidth,
        double pageHeight = DefaultPrintPageHeight)
    {
        request ??= new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts);
        var handoutRequest = request with
        {
            Layout = PresentationPrintLayoutKind.Handouts,
            HandoutSlidesPerPage = request.Layout == PresentationPrintLayoutKind.Handouts
                ? request.HandoutSlidesPerPage
                : null,
        };
        var printPlan = BuildPrintPlan(handoutRequest, slideCount);
        var slidesPerPage = printPlan.Layout.SlidesPerPage;
        var slideAspect = NormalizeAspectRatio(slideWidth, slideHeight);
        var pages = BuildHandoutPages(
            printPlan.SlideRange.SlideNumbers,
            slidesPerPage,
            Math.Max(1, pageWidth),
            Math.Max(1, pageHeight),
            slideAspect);

        return new PresentationHandoutLayoutPlan(
            printPlan,
            Math.Max(1, pageWidth),
            Math.Max(1, pageHeight),
            pages.Count,
            pages);
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

    public static PresentationVideoExportPlan BuildVideoExportPlan(
        PresentationSlideRangeRequest? range,
        int slideCount) =>
        BuildVideoExportPlan(new PresentationVideoExportRequest(range), slideCount);

    public static PresentationVideoExportPlan BuildVideoExportPlan(
        PresentationVideoExportRequest? request,
        int slideCount)
    {
        request ??= new PresentationVideoExportRequest();
        var descriptor = BuildFormatDescriptors().Single(d => d.Format == PresentationExportFormat.Video);
        var range = BuildSlideRangePlan(request.SlideRange, slideCount);
        var qualityOptions = BuildVideoQualityDescriptors();
        var quality = qualityOptions.SingleOrDefault(option => option.Quality == request.Quality)
            ?? qualityOptions.Single(option => option.Quality == PresentationVideoQualityKind.FullHd);
        var secondsPerSlide = NormalizeSecondsPerSlide(request.SecondsPerSlide);
        var estimatedDuration = TimeSpan.FromSeconds(range.SlideNumbers.Count * secondsPerSlide);
        var disabledReason = range.SlideNumbers.Count == 0
            ? "Video export requires at least one slide."
            : VideoExportDeferredMessage;

        return new PresentationVideoExportPlan(
            descriptor.Format,
            descriptor.CommandId,
            descriptor.DisplayName,
            "Plans a PowerPoint-style MP4 export workflow with normalized slide range, output quality, recorded timings, narration intent, and duration estimate.",
            descriptor.DefaultExtensionWithDot ?? VideoExportExtension,
            range,
            quality,
            secondsPerSlide,
            request.UseRecordedTimings,
            request.IncludeNarration,
            estimatedDuration,
            qualityOptions,
            descriptor.IsImplemented,
            CanExecute: descriptor.IsImplemented && range.SlideNumbers.Count > 0,
            disabledReason);
    }

    public static IReadOnlyList<PresentationVideoQualityDescriptor> BuildVideoQualityDescriptors() =>
    [
        new(PresentationVideoQualityKind.UltraHd, "Ultra HD (4K)", 3840, 2160, 60),
        new(PresentationVideoQualityKind.FullHd, "Full HD (1080p)", 1920, 1080, 30),
        new(PresentationVideoQualityKind.Hd, "HD (720p)", 1280, 720, 30),
        new(PresentationVideoQualityKind.Standard, "Standard (480p)", 852, 480, 24),
    ];

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
            DeferredGroupHeading: "Other File Types",
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

    private static IReadOnlyList<PresentationHandoutPagePlan> BuildHandoutPages(
        IReadOnlyList<int> slideNumbers,
        int slidesPerPage,
        double pageWidth,
        double pageHeight,
        double slideAspect)
    {
        if (slideNumbers.Count == 0)
            return [];

        var cellCount = slidesPerPage;
        var cellBounds = BuildHandoutCellBounds(cellCount, pageWidth, pageHeight);
        var pageCount = (int)Math.Ceiling(slideNumbers.Count / (double)slidesPerPage);
        var pages = new List<PresentationHandoutPagePlan>(pageCount);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var slots = new List<PresentationHandoutSlideSlot>(slidesPerPage);
            for (var slotIndex = 0; slotIndex < slidesPerPage; slotIndex++)
            {
                var rangeIndex = (pageIndex * slidesPerPage) + slotIndex;
                if (rangeIndex >= slideNumbers.Count)
                    break;

                var slideNumber = slideNumbers[rangeIndex];
                var cell = cellBounds[slotIndex];
                var (slideCell, notesBounds) = SplitHandoutCell(cell, slidesPerPage);
                var slideBounds = FitAspect(slideCell, slideAspect);
                var blankLines = notesBounds is null
                    ? []
                    : BuildBlankLineBounds(notesBounds.Value);

                slots.Add(new PresentationHandoutSlideSlot(
                    pageIndex,
                    slotIndex,
                    slideNumber - 1,
                    slideNumber,
                    slideBounds,
                    notesBounds,
                    blankLines));
            }

            pages.Add(new PresentationHandoutPagePlan(pageIndex, slots));
        }

        return pages;
    }

    private static IReadOnlyList<LayoutRect> BuildHandoutCellBounds(
        int slidesPerPage,
        double pageWidth,
        double pageHeight)
    {
        var (columns, rows) = slidesPerPage switch
        {
            1 => (1, 1),
            2 => (1, 2),
            3 => (1, 3),
            4 => (2, 2),
            6 => (2, 3),
            9 => (3, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(slidesPerPage), slidesPerPage, "Unsupported handout slide count."),
        };

        var content = GetHandoutContentBounds(pageWidth, pageHeight);
        var cellWidth = (content.Width - (DefaultHandoutGutter * (columns - 1))) / columns;
        var cellHeight = (content.Height - (DefaultHandoutGutter * (rows - 1))) / rows;
        var cells = new List<LayoutRect>(slidesPerPage);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                cells.Add(new LayoutRect(
                    content.X + (column * (cellWidth + DefaultHandoutGutter)),
                    content.Y + (row * (cellHeight + DefaultHandoutGutter)),
                    cellWidth,
                    cellHeight));
            }
        }

        return cells;
    }

    private static LayoutRect GetHandoutContentBounds(double pageWidth, double pageHeight)
    {
        var horizontalMargin = Math.Min(DefaultHandoutMargin, pageWidth / 4);
        var verticalMargin = Math.Min(DefaultHandoutMargin, pageHeight / 4);
        return new LayoutRect(
            horizontalMargin,
            verticalMargin,
            Math.Max(1, pageWidth - (horizontalMargin * 2)),
            Math.Max(1, pageHeight - (verticalMargin * 2)));
    }

    private static (LayoutRect SlideCell, LayoutRect? NotesBounds) SplitHandoutCell(
        LayoutRect cell,
        int slidesPerPage)
    {
        if (slidesPerPage != 3)
            return (cell, null);

        var slideWidth = (cell.Width - DefaultHandoutGutter) * 0.58;
        var notesX = cell.X + slideWidth + DefaultHandoutGutter;
        return (
            new LayoutRect(cell.X, cell.Y, slideWidth, cell.Height),
            new LayoutRect(notesX, cell.Y, Math.Max(1, cell.Right - notesX), cell.Height));
    }

    private static LayoutRect FitAspect(LayoutRect bounds, double aspect)
    {
        var width = bounds.Width;
        var height = width / aspect;
        if (height > bounds.Height)
        {
            height = bounds.Height;
            width = height * aspect;
        }

        return new LayoutRect(
            bounds.X + ((bounds.Width - width) / 2),
            bounds.Y + ((bounds.Height - height) / 2),
            width,
            height);
    }

    private static IReadOnlyList<LayoutRect> BuildBlankLineBounds(LayoutRect notesBounds)
    {
        const int lineCount = 5;
        var lines = new List<LayoutRect>(lineCount);
        var left = notesBounds.X;
        var rightPadding = Math.Min(8, notesBounds.Width / 5);
        var width = Math.Max(1, notesBounds.Width - rightPadding);
        var top = notesBounds.Y + (notesBounds.Height * 0.2);
        var step = notesBounds.Height * 0.15;

        for (var index = 0; index < lineCount; index++)
            lines.Add(new LayoutRect(left, top + (step * index), width, 0));

        return lines;
    }

    private static double NormalizeAspectRatio(double slideWidth, double slideHeight)
    {
        if (slideWidth <= 0 || slideHeight <= 0)
            return 16d / 9d;

        return slideWidth / slideHeight;
    }

    private static double NormalizeSecondsPerSlide(double secondsPerSlide)
    {
        if (double.IsNaN(secondsPerSlide) || double.IsInfinity(secondsPerSlide))
            return DefaultVideoSecondsPerSlide;

        return Math.Clamp(secondsPerSlide, 1, 60);
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
