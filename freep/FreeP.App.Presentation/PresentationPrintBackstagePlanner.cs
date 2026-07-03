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

public sealed record PresentationPrintBackstagePlan(
    string Heading,
    string Description,
    IReadOnlyList<PresentationPrintBackstageLayoutChoice> LayoutChoices,
    IReadOnlyList<PresentationPrintBackstageRangeChoice> RangeChoices,
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
    string? DisabledReason,
    PresentationPrintOutputPackagePlan PackagePlan);

/// <summary>
/// Shared PowerPoint-shaped Backstage Print pane policy. Hosts project this model to WPF/Avalonia UI
/// and keep native printer-dialog handoff deferred.
/// </summary>
public static class PresentationPrintBackstagePlanner
{
    public static PresentationPrintBackstagePlan Build(
        PresentationPrintRequest? request,
        int slideCount,
        int? currentSlideNumber = null,
        IReadOnlyList<int>? selectedSlideNumbers = null)
    {
        var packagePlan = PresentationPrintOutputPackageExecutor.BuildPackagePlan(request, slideCount);
        return Build(slideCount, packagePlan, currentSlideNumber, selectedSlideNumbers);
    }

    public static PresentationPrintBackstagePlan Build(
        PresentationPrintRequest? request,
        Presentation presentation,
        int? currentSlideNumber = null,
        IReadOnlyList<int>? selectedSlideNumbers = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var packagePlan = PresentationPrintOutputPackageExecutor.BuildPackagePlan(request, presentation);
        return Build(presentation.Slides.Count, packagePlan, currentSlideNumber, selectedSlideNumbers, presentation);
    }

    private static PresentationPrintBackstagePlan Build(
        int slideCount,
        PresentationPrintOutputPackagePlan packagePlan,
        int? currentSlideNumber,
        IReadOnlyList<int>? selectedSlideNumbers,
        Presentation? presentation = null)
    {
        var printPlan = packagePlan.PrintPlan;
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
            "Choose a PowerPoint-style print layout and slide range. Native printer selection remains a host handoff after a printable package is built.",
            layouts,
            ranges,
            selectedLayout,
            selectedRange,
            printPlan.PrintHiddenSlides,
            packagePlan.Options,
            packagePlan.PageCount,
            packagePlan.LayoutSummary,
            packagePlan.SlideRangeSummary,
            packagePlan.PreviewPlan,
            packagePlan.CanBuildPackage,
            packagePlan.NativePrinterDialogDeferred,
            PresentationPrintOutputPackageExecutor.NativePrinterDialogDeferredReason,
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
