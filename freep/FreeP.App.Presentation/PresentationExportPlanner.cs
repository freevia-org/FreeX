using Free.Shared.IO;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationExportFormat
{
    Pdf,
    NotesPagePdf,
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

public enum PresentationPrintColorMode
{
    Color,
    Grayscale,
    PureBlackAndWhite,
}

public enum PresentationVideoQualityKind
{
    UltraHd,
    FullHd,
    Hd,
    Standard,
}

public enum PresentationVideoTimingSource
{
    DefaultDuration,
    RecordedTransitionAdvance,
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
    IReadOnlyList<int>? SelectedSlideNumbers = null,
    string? CustomRangeText = null);

public sealed record PresentationSlideRangeParseResult(
    bool IsValid,
    IReadOnlyList<int> SlideNumbers,
    string? ErrorMessage);

public sealed record PresentationPrintRequest(
    PresentationPrintLayoutKind Layout,
    PresentationSlideRangeRequest? SlideRange = null,
    int? HandoutSlidesPerPage = null,
    bool PrintHiddenSlides = false,
    int Copies = 1,
    bool Collate = true,
    PresentationPrintColorMode ColorMode = PresentationPrintColorMode.Color,
    bool FrameSlides = false,
    bool IncludeCommentsAndInkMarkup = false);

public sealed record PresentationSlideRangePlan(
    PresentationSlideRangeKind Kind,
    IReadOnlyList<int> SlideNumbers,
    string DisplayName,
    string? CustomRangeText = null,
    string? ValidationMessage = null);

public sealed record PresentationPrintPlan(
    string CommandId,
    PresentationPrintLayoutDescriptor Layout,
    PresentationSlideRangePlan SlideRange,
    bool PrintHiddenSlides,
    bool IsImplemented,
    PresentationPrintOptionsPlan Options);

public sealed record PresentationPrintOptionsPlan(
    int Copies,
    bool Collate,
    PresentationPrintColorMode ColorMode,
    bool PrintHiddenSlides,
    bool FrameSlides,
    bool IncludeCommentsAndInkMarkup,
    string DisplaySummary,
    IReadOnlyList<string> SummaryLines);

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

public sealed record PresentationVideoStoryboardSlideSegment(
    int SlideIndex,
    int SlideNumber,
    string SlideTitle,
    TimeSpan StartTime,
    TimeSpan Duration,
    PresentationVideoTimingSource TimingSource);

public sealed record PresentationVideoStoryboardPlan(
    PresentationSlideRangePlan SlideRange,
    IReadOnlyList<PresentationVideoStoryboardSlideSegment> Segments,
    PresentationVideoQualityDescriptor Quality,
    int OutputWidthPx,
    int OutputHeightPx,
    int PixelsPerSecondHint,
    double FrameRateHint,
    bool UseRecordedTimings,
    bool IncludeNarration,
    TimeSpan TotalDuration);

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
    PresentationVideoStoryboardPlan Storyboard,
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

public sealed record PresentationNotesPagePdfExportPlan(
    PresentationExportFormat Format,
    string CommandId,
    string DisplayName,
    string Description,
    string DefaultExtensionWithDot,
    PresentationPrintPlan PrintPlan,
    bool IsImplemented,
    bool CanExecute,
    string? DisabledReason);

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
    public const string NotesPagePdfExportCommandId = "freep.file.export-notes-page-pdf";
    public const string ImageExportCommandId = "freep.file.export-images";
    public const string VideoExportCommandId = "freep.file.export-video";
    public const string PrintCommandId = "freep.file.print";
    public const string PdfExportPickerTitle = "Export to PDF";
    public const string PdfExportCommandText = "Export to PDF";
    public const string NotesPagePdfExportPickerTitle = "Export Notes Pages to PDF";
    public const string NotesPagePdfExportCommandText = "Export notes pages to PDF";
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

    private static readonly IReadOnlyList<FileDialogFormatDescriptor> VideoFormats =
    [
        new FileDialogFormatDescriptor(VideoExportExtension, "MPEG-4 videos"),
    ];

    public static IReadOnlyList<PresentationExportFormatDescriptor> BuildFormatDescriptors(
        bool videoExportAvailable = false) =>
    [
        new(
            PresentationExportFormat.Pdf,
            PdfExportCommandId,
            "PDF",
            "Fixed-layout PDF copy with one page per slide.",
            PdfExportExtension,
            IsImplemented: true),
        new(
            PresentationExportFormat.NotesPagePdf,
            NotesPagePdfExportCommandId,
            "Notes Page PDF",
            "Fixed-layout PDF copy with one notes page per selected slide.",
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
            IsImplemented: videoExportAvailable),
        new(
            PresentationExportFormat.Print,
            PrintCommandId,
            "Print",
            "PowerPoint-shaped slide, notes-page, and handout print layouts with native host handoff.",
            DefaultExtensionWithDot: null,
            IsImplemented: true),
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
            IsImplemented: true,
            BuildPrintOptionsPlan(request));
    }

    /// <summary>
    /// Builds a print plan with the presentation's hidden-slide policy applied. The count-only
    /// overload remains available for callers that do not have slide metadata; presentation-aware
    /// print paths must use this overload so the default PowerPoint behavior excludes hidden slides.
    /// </summary>
    public static PresentationPrintPlan BuildPrintPlan(
        PresentationPrintRequest? request,
        Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var plan = BuildPrintPlan(request, presentation.Slides.Count);
        if (plan.PrintHiddenSlides || plan.SlideRange.SlideNumbers.Count == 0)
            return plan;

        var visibleNumbers = plan.SlideRange.SlideNumbers
            .Where(slideNumber =>
                slideNumber >= 1 &&
                slideNumber <= presentation.Slides.Count &&
                !presentation.Slides[slideNumber - 1].IsHidden)
            .ToArray();

        return plan with
        {
            SlideRange = new PresentationSlideRangePlan(
                plan.SlideRange.Kind,
                visibleNumbers,
                FormatRangeDisplayName(
                    plan.SlideRange.Kind,
                    visibleNumbers,
                    presentation.Slides.Count),
                plan.SlideRange.CustomRangeText,
                plan.SlideRange.ValidationMessage),
        };
    }

    public static PresentationHandoutLayoutPlan BuildHandoutLayoutPlan(
        PresentationPrintRequest? request,
        Presentation presentation,
        double slideWidth = 16,
        double slideHeight = 9,
        double pageWidth = DefaultPrintPageWidth,
        double pageHeight = DefaultPrintPageHeight)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        request ??= new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts);
        var handoutRequest = request with
        {
            Layout = PresentationPrintLayoutKind.Handouts,
            HandoutSlidesPerPage = request.Layout == PresentationPrintLayoutKind.Handouts
                ? request.HandoutSlidesPerPage
                : null,
        };
        var printPlan = BuildPrintPlan(handoutRequest, presentation);
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

    public static PresentationNotesPagePdfExportPlan BuildNotesPagePdfExportPlan(
        PresentationSlideRangeRequest? range,
        int slideCount)
    {
        var descriptor = BuildFormatDescriptors().Single(d => d.Format == PresentationExportFormat.NotesPagePdf);
        var printPlan = BuildPrintPlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                range),
            slideCount);
        var canExecute = descriptor.IsImplemented && printPlan.SlideRange.SlideNumbers.Count > 0;

        return new PresentationNotesPagePdfExportPlan(
            descriptor.Format,
            descriptor.CommandId,
            descriptor.DisplayName,
            "Exports PowerPoint-style notes pages to PDF through the shared notes-page render plan.",
            descriptor.DefaultExtensionWithDot ?? PdfExportExtension,
            printPlan,
            descriptor.IsImplemented,
            canExecute,
            canExecute ? null : "Notes-page PDF export requires at least one slide.");
    }

    public static PresentationVideoExportPlan BuildVideoExportPlan(
        PresentationSlideRangeRequest? range,
        int slideCount) =>
        BuildVideoExportPlan(new PresentationVideoExportRequest(range), slideCount);

    public static PresentationVideoExportPlan BuildVideoExportPlan(
        PresentationVideoExportRequest? request,
        int slideCount,
        PresentationVideoExportHandoffHostCapabilities? hostCapabilities = null)
    {
        return BuildVideoExportPlanCore(
            request,
            Math.Max(0, slideCount),
            slides: null,
            hostCapabilities);
    }

    public static PresentationVideoExportPlan BuildVideoExportPlan(
        PresentationVideoExportRequest? request,
        Presentation presentation,
        PresentationVideoExportHandoffHostCapabilities? hostCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return BuildVideoExportPlanCore(
            request,
            presentation.Slides.Count,
            presentation.Slides,
            hostCapabilities);
    }

    public static PresentationVideoStoryboardPlan BuildVideoStoryboardPlan(
        PresentationVideoExportRequest? request,
        int slideCount)
    {
        request ??= new PresentationVideoExportRequest();
        var range = BuildSlideRangePlan(request.SlideRange, slideCount);
        var quality = ResolveVideoQuality(request.Quality);
        var secondsPerSlide = NormalizeSecondsPerSlide(request.SecondsPerSlide);

        return BuildVideoStoryboardPlan(request, range, quality, secondsPerSlide, slides: null);
    }

    public static PresentationVideoStoryboardPlan BuildVideoStoryboardPlan(
        PresentationVideoExportRequest? request,
        Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        request ??= new PresentationVideoExportRequest();
        var range = BuildSlideRangePlan(request.SlideRange, presentation.Slides.Count);
        var quality = ResolveVideoQuality(request.Quality);
        var secondsPerSlide = NormalizeSecondsPerSlide(request.SecondsPerSlide);

        return BuildVideoStoryboardPlan(request, range, quality, secondsPerSlide, presentation.Slides);
    }

    private static PresentationVideoExportPlan BuildVideoExportPlanCore(
        PresentationVideoExportRequest? request,
        int slideCount,
        IReadOnlyList<Slide>? slides,
        PresentationVideoExportHandoffHostCapabilities? hostCapabilities)
    {
        request ??= new PresentationVideoExportRequest();
        var descriptor = BuildFormatDescriptors().Single(d => d.Format == PresentationExportFormat.Video);
        var range = BuildSlideRangePlan(request.SlideRange, slideCount);
        var qualityOptions = BuildVideoQualityDescriptors();
        var quality = ResolveVideoQuality(request.Quality, qualityOptions);
        var secondsPerSlide = NormalizeSecondsPerSlide(request.SecondsPerSlide);
        var storyboard = BuildVideoStoryboardPlan(request, range, quality, secondsPerSlide, slides);
        var isImplemented = hostCapabilities?.CanEncodeMp4 == true;
        var disabledReason = range.SlideNumbers.Count == 0
            ? "Video export requires at least one slide."
            : isImplemented
                ? null
                : hostCapabilities?.UnavailableReason ?? VideoExportDeferredMessage;

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
            storyboard.TotalDuration,
            storyboard,
            qualityOptions,
            isImplemented,
            CanExecute: isImplemented && range.SlideNumbers.Count > 0,
            disabledReason);
    }

    public static IReadOnlyList<PresentationVideoQualityDescriptor> BuildVideoQualityDescriptors() =>
    [
        new(PresentationVideoQualityKind.UltraHd, "Ultra HD (4K)", 3840, 2160, 60),
        new(PresentationVideoQualityKind.FullHd, "Full HD (1080p)", 1920, 1080, 30),
        new(PresentationVideoQualityKind.Hd, "HD (720p)", 1280, 720, 30),
        new(PresentationVideoQualityKind.Standard, "Standard (480p)", 852, 480, 24),
    ];

    private static PresentationVideoQualityDescriptor ResolveVideoQuality(
        PresentationVideoQualityKind requestedQuality,
        IReadOnlyList<PresentationVideoQualityDescriptor>? qualityOptions = null)
    {
        qualityOptions ??= BuildVideoQualityDescriptors();
        return qualityOptions.SingleOrDefault(option => option.Quality == requestedQuality)
            ?? qualityOptions.Single(option => option.Quality == PresentationVideoQualityKind.FullHd);
    }

    private static PresentationVideoStoryboardPlan BuildVideoStoryboardPlan(
        PresentationVideoExportRequest request,
        PresentationSlideRangePlan range,
        PresentationVideoQualityDescriptor quality,
        double secondsPerSlide,
        IReadOnlyList<Slide>? slides)
    {
        var segments = new List<PresentationVideoStoryboardSlideSegment>(range.SlideNumbers.Count);
        var cursor = TimeSpan.Zero;
        foreach (var slideNumber in range.SlideNumbers)
        {
            var slideIndex = slideNumber - 1;
            var slide = slides is not null && slideIndex >= 0 && slideIndex < slides.Count
                ? slides[slideIndex]
                : null;
            var (duration, timingSource) = ResolveVideoSegmentDuration(
                slide,
                request.UseRecordedTimings,
                secondsPerSlide);

            segments.Add(new PresentationVideoStoryboardSlideSegment(
                slideIndex,
                slideNumber,
                NormalizeSlideTitle(slide, slideNumber),
                cursor,
                duration,
                timingSource));
            cursor += duration;
        }

        return new PresentationVideoStoryboardPlan(
            range,
            segments,
            quality,
            quality.WidthPx,
            quality.HeightPx,
            quality.PixelsPerSecondHint,
            quality.PixelsPerSecondHint,
            request.UseRecordedTimings,
            request.IncludeNarration,
            cursor);
    }

    private static (TimeSpan Duration, PresentationVideoTimingSource TimingSource) ResolveVideoSegmentDuration(
        Slide? slide,
        bool useRecordedTimings,
        double secondsPerSlide)
    {
        if (useRecordedTimings &&
            slide?.Transition?.AdvanceAfterMs is int recordedAdvanceMs &&
            recordedAdvanceMs > 0)
        {
            return (
                TimeSpan.FromMilliseconds(recordedAdvanceMs),
                PresentationVideoTimingSource.RecordedTransitionAdvance);
        }

        return (
            TimeSpan.FromSeconds(secondsPerSlide),
            PresentationVideoTimingSource.DefaultDuration);
    }

    private static string NormalizeSlideTitle(Slide? slide, int slideNumber)
    {
        var title = slide?.Title?.Trim();
        return string.IsNullOrEmpty(title) ? $"Slide {slideNumber}" : title;
    }

    public static PresentationSlideRangePlan BuildSlideRangePlan(
        PresentationSlideRangeRequest? request,
        int slideCount)
    {
        var count = Math.Max(0, slideCount);
        request ??= new PresentationSlideRangeRequest(PresentationSlideRangeKind.AllSlides);
        PresentationSlideRangeParseResult? parsedCustomRange = null;

        var numbers = request.Kind switch
        {
            PresentationSlideRangeKind.CurrentSlide => count == 0
                ? []
                : [ClampSlideNumber(request.CurrentSlideNumber ?? 1, count)],
            PresentationSlideRangeKind.SelectedSlides => NormalizeSelectedSlides(request.SelectedSlideNumbers, count),
            PresentationSlideRangeKind.CustomRange => request.CustomRangeText is null
                ? NormalizeCustomRange(request.StartSlideNumber, request.EndSlideNumber, count)
                : (parsedCustomRange = ParseCustomSlideRange(request.CustomRangeText, count)).SlideNumbers,
            _ => BuildAllSlides(count),
        };

        var validationMessage = parsedCustomRange?.ErrorMessage;

        return new PresentationSlideRangePlan(
            request.Kind,
            numbers,
            validationMessage is null
                ? FormatRangeDisplayName(request.Kind, numbers, count)
                : "Invalid custom range",
            request.CustomRangeText,
            validationMessage);
    }

    public static PresentationSlideRangeParseResult ParseCustomSlideRange(
        string? rangeText,
        int slideCount)
    {
        var count = Math.Max(0, slideCount);
        if (string.IsNullOrWhiteSpace(rangeText))
        {
            return new(
                IsValid: false,
                SlideNumbers: [],
                "Enter one or more slide numbers, for example 1,3-5.");
        }

        if (count == 0)
        {
            return new(
                IsValid: false,
                SlideNumbers: [],
                "No slides are available for the custom range.");
        }

        var numbers = new List<int>();
        var seen = new HashSet<int>();
        foreach (var rawToken in rangeText.Replace('\u2013', '-').Split(',', ';'))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
                return InvalidCustomRange("Custom ranges cannot contain empty entries.");

            var separator = token.IndexOf('-');
            if (separator >= 0)
            {
                if (separator == 0 || separator == token.Length - 1 ||
                    token.IndexOf('-', separator + 1) >= 0 ||
                    !int.TryParse(token[..separator].Trim(), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var start) ||
                    !int.TryParse(token[(separator + 1)..].Trim(), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var end))
                {
                    return InvalidCustomRange($"Invalid slide range '{token}'. Use entries such as 3-5.");
                }

                if (start < 1 || end > count || start > end)
                    return InvalidCustomRange($"Slide range '{token}' is outside slides 1-{count}.");

                for (var number = start; number <= end; number++)
                {
                    if (seen.Add(number))
                        numbers.Add(number);
                }

                continue;
            }

            if (!int.TryParse(token, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var slideNumber) ||
                slideNumber < 1 || slideNumber > count)
            {
                return InvalidCustomRange($"Slide '{token}' is outside slides 1-{count}.");
            }

            if (seen.Add(slideNumber))
                numbers.Add(slideNumber);
        }

        return new(IsValid: true, numbers, ErrorMessage: null);

        static PresentationSlideRangeParseResult InvalidCustomRange(string message) =>
            new(IsValid: false, SlideNumbers: [], message);
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

    public static FileSaveDialogPlan BuildNotesPagePdfExportDialogPlan(string? sourceName) =>
        FileDialogRequestPlanner.BuildPerFormatSaveDialogPlan(
            PdfFormats,
            BuildNotesPagePdfSuggestedFileName(sourceName),
            PdfExportExtension);

    public static FileSavePickerPlan BuildNotesPagePdfExportPickerPlan(string? sourceName) =>
        FileDialogRequestPlanner.BuildSavePickerPlan(
            PdfFormats,
            BuildNotesPagePdfSuggestedFileName(sourceName),
            FallbackPresentationName,
            PdfExportExtension,
            preferredFirstExtension: PdfExportExtension);

    public static FileSavePickerPlan BuildVideoExportPickerPlan(string? sourceName) =>
        FileDialogRequestPlanner.BuildSavePickerPlan(
            VideoFormats,
            FileDialogRequestPlanner.BuildSuggestedSaveAsFileName(
                sourceName,
                FallbackPresentationName,
                VideoExportExtension),
            FallbackPresentationName,
            VideoExportExtension,
            preferredFirstExtension: VideoExportExtension);

    public static FileSaveDialogPlan BuildVideoExportDialogPlan(string? sourceName) =>
        FileDialogRequestPlanner.BuildPerFormatSaveDialogPlan(
            VideoFormats,
            FileDialogRequestPlanner.BuildSuggestedSaveAsFileName(
                sourceName,
                FallbackPresentationName,
                VideoExportExtension),
            VideoExportExtension);

    public static PresentationBackstageExportPlan BuildBackstageExportPlan(
        bool videoExportAvailable = false)
    {
        var formats = BuildFormatDescriptors(videoExportAvailable);
        var pdf = formats.Single(format => format.Format == PresentationExportFormat.Pdf);
        var notesPagePdf = formats.Single(format => format.Format == PresentationExportFormat.NotesPagePdf);

        return new PresentationBackstageExportPlan(
            Heading: "Export",
            Description: "Create a fixed-layout copy for sharing or presenting.",
            FixedLayoutGroupHeading: "Create PDF Copy",
            DeferredGroupHeading: "Other File Types",
            FixedLayoutActions:
            [
                ToActionPlan(pdf, "Export to PDF...", pdf.Description),
                ToActionPlan(notesPagePdf, "Notes Page PDF...", notesPagePdf.Description),
            ],
            DeferredActions: formats
                .Where(format => format.Format is not PresentationExportFormat.Pdf
                    and not PresentationExportFormat.NotesPagePdf
                    and not PresentationExportFormat.Print)
                .Select(format => ToActionPlan(format, format.DisplayName, format.Description))
                .ToArray());
    }

    private static string BuildNotesPagePdfSuggestedFileName(string? sourceName)
    {
        var fileName = FileDialogRequestPlanner.BuildSuggestedSaveAsFileName(
            sourceName,
            FallbackPresentationName,
            PdfExportExtension);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return $"{baseName}-notes{PdfExportExtension}";
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

    private static PresentationPrintOptionsPlan BuildPrintOptionsPlan(PresentationPrintRequest request)
    {
        var copies = Math.Clamp(request.Copies, 1, 999);
        var colorMode = NormalizePrintColorMode(request.ColorMode);
        var lines = new List<string>
        {
            copies == 1 ? "1 copy" : $"{copies} copies",
            request.Collate ? "Collated" : "Uncollated",
            GetPrintColorModeDisplayName(colorMode),
        };

        if (request.PrintHiddenSlides)
            lines.Add("Print hidden slides");
        if (request.FrameSlides)
            lines.Add("Frame slides");
        if (request.IncludeCommentsAndInkMarkup)
            lines.Add("Print comments and ink markup");

        return new PresentationPrintOptionsPlan(
            copies,
            request.Collate,
            colorMode,
            request.PrintHiddenSlides,
            request.FrameSlides,
            request.IncludeCommentsAndInkMarkup,
            string.Join(", ", lines),
            lines);
    }

    private static PresentationPrintColorMode NormalizePrintColorMode(PresentationPrintColorMode colorMode) =>
        Enum.IsDefined(colorMode) ? colorMode : PresentationPrintColorMode.Color;

    private static string GetPrintColorModeDisplayName(PresentationPrintColorMode colorMode) =>
        colorMode switch
        {
            PresentationPrintColorMode.Grayscale => "Grayscale",
            PresentationPrintColorMode.PureBlackAndWhite => "Pure Black and White",
            _ => "Color",
        };

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
