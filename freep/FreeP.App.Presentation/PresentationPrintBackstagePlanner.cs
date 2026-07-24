using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationPrintBackstageLayoutChoice(
    PresentationPrintLayoutDescriptor Layout,
    PresentationPrintOutputPackagePlan PackagePlan,
    bool IsSelected);

public sealed record PresentationPrintBackstageRangeChoice(
    PresentationSlideRangeKind Kind,
    string DisplayName,
    string Description,
    bool IsAvailable,
    PresentationSlideRangeRequest? Request);

public sealed record PresentationPrintBackstageOptionChoice(
    string OptionId,
    string Group,
    string DisplayName,
    string Description,
    bool IsSelected,
    bool IsAvailable);

public sealed record PresentationPrintBackstagePlan(
    string Heading,
    string Description,
    IReadOnlyList<PresentationPrintBackstageLayoutChoice> LayoutChoices,
    IReadOnlyList<PresentationPrintBackstageRangeChoice> RangeChoices,
    IReadOnlyList<PresentationPrintBackstageOptionChoice> OutputOptionChoices,
    PresentationPrintBackstageLayoutChoice SelectedLayout,
    PresentationPrintBackstageRangeChoice SelectedRange,
    bool PrintHiddenSlides,
    PresentationPrintOptionsPlan Options,
    int PageCount,
    string LayoutSummary,
    string SlideRangeSummary,
    PresentationPrintPreviewPlan PreviewPlan,
    bool CanBuildPackage,
    bool NativePrinterDialogDeferred,
    string NativePrinterDialogDeferredMessage,
    PresentationNativePrintHandoffPlan NativePrintHandoff,
    string? DisabledReason,
    PresentationPrintOutputPackagePlan PackagePlan);

/// <summary>
/// Shared PowerPoint-shaped Backstage Print pane policy. Hosts project this model to WPF/Avalonia UI
/// and keep native printer-dialog execution behind the host handoff plan.
/// </summary>
public static class PresentationPrintBackstagePlanner
{
    public static PresentationPrintBackstagePlan Build(
        PresentationPrintRequest? request,
        int slideCount,
        int? currentSlideNumber = null,
        IReadOnlyList<int>? selectedSlideNumbers = null,
        PresentationNativePrintHandoffHostCapabilities? hostCapabilities = null,
        string? suggestedBaseFileName = null)
    {
        var packagePlan = PresentationPrintOutputPackageExecutor.BuildPackagePlan(request, slideCount);
        return Build(
            slideCount,
            packagePlan,
            currentSlideNumber,
            selectedSlideNumbers,
            hostCapabilities,
            suggestedBaseFileName);
    }

    public static PresentationPrintBackstagePlan Build(
        PresentationPrintRequest? request,
        Presentation presentation,
        int? currentSlideNumber = null,
        IReadOnlyList<int>? selectedSlideNumbers = null,
        PresentationNativePrintHandoffHostCapabilities? hostCapabilities = null,
        string? suggestedBaseFileName = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var packagePlan = PresentationPrintOutputPackageExecutor.BuildPackagePlan(request, presentation);
        return Build(
            presentation.Slides.Count,
            packagePlan,
            currentSlideNumber,
            selectedSlideNumbers,
            hostCapabilities,
            suggestedBaseFileName,
            presentation: presentation);
    }

    private static PresentationPrintBackstagePlan Build(
        int slideCount,
        PresentationPrintOutputPackagePlan packagePlan,
        int? currentSlideNumber,
        IReadOnlyList<int>? selectedSlideNumbers,
        PresentationNativePrintHandoffHostCapabilities? hostCapabilities = null,
        string? suggestedBaseFileName = null,
        Presentation? presentation = null)
    {
        var printPlan = packagePlan.PrintPlan;
        var nativePrintHandoff = PresentationPrintOutputPackageExecutor.BuildNativePrintHandoffPlan(
            packagePlan,
            hostCapabilities,
            suggestedBaseFileName);
        var normalizedRequest = ToRequest(printPlan);
        var layouts = PresentationExportPlanner.BuildPrintLayoutDescriptors()
            .Select(layout => BuildLayoutChoice(layout, normalizedRequest, slideCount, printPlan, presentation))
            .ToArray();
        var selectedLayout = layouts.Single(choice =>
            choice.Layout.Layout == printPlan.Layout.Layout &&
            choice.Layout.SlidesPerPage == printPlan.Layout.SlidesPerPage);
        var ranges = BuildRangeChoices(
            slideCount,
            printPlan.SlideRange.Kind,
            currentSlideNumber,
            selectedSlideNumbers);
        var selectedRange = ranges.FirstOrDefault(choice => choice.Kind == printPlan.SlideRange.Kind)
            ?? ranges[0];

        return new PresentationPrintBackstagePlan(
            "Print",
            "Choose a PowerPoint-style print layout and slide range. The host will open a print dialog or submit the package directly to its native printer queue when supported.",
            layouts,
            ranges,
            BuildOutputOptionChoices(printPlan.Options),
            selectedLayout,
            selectedRange,
            printPlan.PrintHiddenSlides,
            packagePlan.Options,
            packagePlan.PageCount,
            packagePlan.LayoutSummary,
            packagePlan.SlideRangeSummary,
            packagePlan.PreviewPlan,
            packagePlan.CanBuildPackage,
            nativePrintHandoff.Status == PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost,
            nativePrintHandoff.Reason,
            nativePrintHandoff,
            packagePlan.DisabledReason,
            packagePlan);
    }

    private static PresentationPrintBackstageLayoutChoice BuildLayoutChoice(
        PresentationPrintLayoutDescriptor layout,
        PresentationPrintRequest normalizedRequest,
        int slideCount,
        PresentationPrintPlan selectedPrintPlan,
        Presentation? presentation)
    {
        var layoutRequest = normalizedRequest with
            {
                Layout = layout.Layout,
                HandoutSlidesPerPage = layout.IsHandout ? layout.SlidesPerPage : null,
            };
        var packagePlan = presentation is null
            ? PresentationPrintOutputPackageExecutor.BuildPackagePlan(layoutRequest, slideCount)
            : PresentationPrintOutputPackageExecutor.BuildPackagePlan(layoutRequest, presentation);

        return new PresentationPrintBackstageLayoutChoice(
            layout,
            packagePlan,
            layout.Layout == selectedPrintPlan.Layout.Layout &&
                layout.SlidesPerPage == selectedPrintPlan.Layout.SlidesPerPage);
    }

    private static IReadOnlyList<PresentationPrintBackstageOptionChoice> BuildOutputOptionChoices(
        PresentationPrintOptionsPlan options)
    {
        var copiesLabel = options.Copies == 1 ? "1 copy" : $"{options.Copies} copies";
        return
        [
            new(
                "copies",
                "Copies",
                copiesLabel,
                "Set the number of copies from 1 to 999 before handing the package to the native printer host.",
                IsSelected: true,
                IsAvailable: true),
            new(
                "collated",
                "Collation",
                "Collated",
                "Print complete copy sets in page order.",
                options.Collate,
                IsAvailable: true),
            new(
                "uncollated",
                "Collation",
                "Uncollated",
                "Print all copies of each page before moving to the next page.",
                !options.Collate,
                IsAvailable: true),
            new(
                "color",
                "Color",
                "Color",
                "Preserve slide colors in the printable output.",
                options.ColorMode == PresentationPrintColorMode.Color,
                IsAvailable: true),
            new(
                "grayscale",
                "Color",
                "Grayscale",
                "Convert slide content to grayscale for print output.",
                options.ColorMode == PresentationPrintColorMode.Grayscale,
                IsAvailable: true),
            new(
                "pure-black-and-white",
                "Color",
                "Pure Black and White",
                "Use a high-contrast black-and-white print intent.",
                options.ColorMode == PresentationPrintColorMode.PureBlackAndWhite,
                IsAvailable: true),
            new(
                "include-hidden-slides",
                "Content",
                "Print hidden slides",
                "Include hidden slides in the normalized print range.",
                options.PrintHiddenSlides,
                IsAvailable: true),
            new(
                "skip-hidden-slides",
                "Content",
                "Do not print hidden slides",
                "Keep hidden slides out of the printable package.",
                !options.PrintHiddenSlides,
                IsAvailable: true),
            new(
                "frame-slides",
                "Output",
                "Frame slides",
                "Draw a frame around each slide thumbnail/page.",
                options.FrameSlides,
                IsAvailable: true),
            new(
                "comments-and-ink",
                "Output",
                "Print comments and ink markup",
                "Reserve print intent for comments and ink markup.",
                options.IncludeCommentsAndInkMarkup,
                IsAvailable: true),
        ];
    }

    private static IReadOnlyList<PresentationPrintBackstageRangeChoice> BuildRangeChoices(
        int slideCount,
        PresentationSlideRangeKind selectedKind,
        int? currentSlideNumber,
        IReadOnlyList<int>? selectedSlideNumbers)
    {
        var choices = new List<PresentationPrintBackstageRangeChoice>
        {
            BuildRangeChoice(
                new PresentationSlideRangeRequest(PresentationSlideRangeKind.AllSlides),
                slideCount,
                "Print the full deck."),
            BuildRangeChoice(
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CurrentSlide,
                    CurrentSlideNumber: currentSlideNumber),
                slideCount,
                "Print only the current slide."),
        };

        if (selectedSlideNumbers is not null)
        {
            choices.Add(BuildRangeChoice(
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.SelectedSlides,
                    SelectedSlideNumbers: selectedSlideNumbers),
                slideCount,
                "Print the slides selected in the host."));
        }

        choices.Add(new PresentationPrintBackstageRangeChoice(
            PresentationSlideRangeKind.CustomRange,
            selectedKind == PresentationSlideRangeKind.CustomRange
                ? "Custom Range"
                : "Custom Range...",
            "Enter a custom slide range in the host UI. Parsing and validation are deferred to a later input surface.",
            slideCount > 0,
            Request: null));

        return choices;
    }

    private static PresentationPrintBackstageRangeChoice BuildRangeChoice(
        PresentationSlideRangeRequest request,
        int slideCount,
        string description)
    {
        var plan = PresentationExportPlanner.BuildSlideRangePlan(request, slideCount);
        return new PresentationPrintBackstageRangeChoice(
            request.Kind,
            LabelRange(request.Kind, plan.DisplayName),
            description,
            plan.SlideNumbers.Count > 0,
            request);
    }

    private static string LabelRange(PresentationSlideRangeKind kind, string summary) =>
        kind switch
        {
            PresentationSlideRangeKind.AllSlides => "All Slides",
            PresentationSlideRangeKind.CurrentSlide => summary == "No slides" ? "Current Slide" : $"Current Slide ({summary})",
            PresentationSlideRangeKind.SelectedSlides => summary == "No slides" ? "Selected Slides" : $"Selected Slides ({summary})",
            _ => summary,
        };

    private static PresentationPrintRequest ToRequest(PresentationPrintPlan plan) =>
        new(
            plan.Layout.Layout,
            ToRangeRequest(plan.SlideRange),
            plan.Layout.IsHandout ? plan.Layout.SlidesPerPage : null,
            plan.PrintHiddenSlides,
            plan.Options.Copies,
            plan.Options.Collate,
            plan.Options.ColorMode,
            plan.Options.FrameSlides,
            plan.Options.IncludeCommentsAndInkMarkup);

    private static PresentationSlideRangeRequest ToRangeRequest(PresentationSlideRangePlan range) =>
        range.Kind switch
        {
            PresentationSlideRangeKind.CurrentSlide => new(
                PresentationSlideRangeKind.CurrentSlide,
                CurrentSlideNumber: range.SlideNumbers.Count == 0 ? null : range.SlideNumbers[0]),
            PresentationSlideRangeKind.CustomRange => new(
                PresentationSlideRangeKind.CustomRange,
                StartSlideNumber: range.SlideNumbers.Count == 0 ? null : range.SlideNumbers[0],
                EndSlideNumber: range.SlideNumbers.Count == 0 ? null : range.SlideNumbers[^1]),
            PresentationSlideRangeKind.SelectedSlides => new(
                PresentationSlideRangeKind.SelectedSlides,
                SelectedSlideNumbers: range.SlideNumbers),
            _ => new(PresentationSlideRangeKind.AllSlides),
        };
}
