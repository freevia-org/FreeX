using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationPrintOutputPackageRoute
{
    FullPageSlidesRasterPdf,
    NotesPagePdf,
    HandoutPdf,
}

public enum PresentationPrintPreviewPageKind
{
    FullPageSlide,
    NotesPage,
    Handout,
}

public sealed record PresentationPrintPreviewPage(
    int PageIndex,
    int PageNumber,
    PresentationPrintPreviewPageKind Kind,
    IReadOnlyList<int> SlideNumbers,
    string ThumbnailLabel,
    string Detail);

public sealed record PresentationPrintPreviewPlan(
    int PageCount,
    string PageCountText,
    bool CanPreview,
    string? DisabledReason,
    IReadOnlyList<PresentationPrintPreviewPage> Pages);

public sealed record PresentationPrintOutputPackagePlan(
    PresentationPrintPlan PrintPlan,
    PresentationPrintOutputPackageRoute Route,
    string ContentType,
    string DefaultExtensionWithDot,
    int PageCount,
    string LayoutSummary,
    string SlideRangeSummary,
    PresentationPrintOptionsPlan Options,
    PresentationPrintPreviewPlan PreviewPlan,
    bool CanBuildPackage,
    bool NativePrinterDialogDeferred,
    string? DisabledReason);

public sealed record PresentationPrintOutputPackage(
    PresentationPrintOutputPackagePlan Plan,
    byte[] Bytes);

/// <summary>
/// Shared printable-output execution for FreeP. Hosts provide only raster slide rendering and
/// platform PDF writing for full-page slides; layout selection and notes/handout routing stay shared.
/// </summary>
public static class PresentationPrintOutputPackageExecutor
{
    public const string PdfContentType = "application/pdf";
    public const string NativePrinterDialogDeferredReason =
        "Printable PDF package execution is available; native printer dialog handoff is deferred.";

    public static PresentationPrintOutputPackagePlan BuildPackagePlan(
        PresentationPrintRequest? request,
        int slideCount)
    {
        var printPlan = PresentationExportPlanner.BuildPrintPlan(request, slideCount);
        return BuildPackagePlan(printPlan, notesPageCount: null);
    }

    public static PresentationPrintOutputPackagePlan BuildPackagePlan(
        PresentationPrintRequest? request,
        Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var printPlan = PresentationExportPlanner.BuildPrintPlan(request, presentation.Slides.Count);
        var notesRenderPlan = printPlan.Layout.Layout == PresentationPrintLayoutKind.NotesPages &&
            printPlan.SlideRange.SlideNumbers.Count > 0
                ? PresentationNotesPagePdfExporter.BuildRenderPlan(
                    presentation,
                    new PresentationNotesPagePdfExportRequest(ToPrintRequest(printPlan)))
                : null;

        return BuildPackagePlan(printPlan, notesRenderPlan?.Pages.Count, notesRenderPlan?.PreviewPlans);
    }

    private static PresentationPrintOutputPackagePlan BuildPackagePlan(
        PresentationPrintPlan printPlan,
        int? notesPageCount,
        IReadOnlyList<PresentationNotesPagePreviewPlan>? notesPreviewPlans = null)
    {
        var canBuild = printPlan.SlideRange.SlideNumbers.Count > 0;
        var route = ResolveRoute(printPlan.Layout.Layout);
        var pageCount = CalculatePageCount(printPlan, notesPageCount);
        var disabledReason = canBuild ? null : "Print output requires at least one slide.";
        var previewPlan = BuildPreviewPlan(printPlan, route, pageCount, canBuild, disabledReason, notesPreviewPlans);
        return new PresentationPrintOutputPackagePlan(
            printPlan,
            route,
            PdfContentType,
            PresentationExportPlanner.PdfExportExtension,
            pageCount,
            BuildLayoutSummary(printPlan, notesPageCount),
            printPlan.SlideRange.DisplayName,
            printPlan.Options,
            previewPlan,
            canBuild,
            NativePrinterDialogDeferred: true,
            disabledReason);
    }

    public static PresentationPrintOutputPackage BuildPackage(
        Presentation presentation,
        PresentationPrintRequest? request,
        PresentationSlideImageRenderer renderSlideToPng,
        PresentationRasterPdfWriter writeRasterPdf)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(renderSlideToPng);
        ArgumentNullException.ThrowIfNull(writeRasterPdf);

        var plan = BuildPackagePlan(request, presentation);
        if (!plan.CanBuildPackage)
            return new PresentationPrintOutputPackage(plan, []);

        var normalizedRequest = ToPrintRequest(plan.PrintPlan);
        var bytes = plan.Route switch
        {
            PresentationPrintOutputPackageRoute.FullPageSlidesRasterPdf =>
                PresentationRasterPdfExporter.ExportToBytes(
                    presentation,
                    new PresentationRasterPdfExportRequest(normalizedRequest.SlideRange),
                    renderSlideToPng,
                    writeRasterPdf),
            PresentationPrintOutputPackageRoute.NotesPagePdf =>
                PresentationNotesPagePdfExporter.ExportToBytes(
                    presentation,
                    new PresentationNotesPagePdfExportRequest(normalizedRequest)),
            PresentationPrintOutputPackageRoute.HandoutPdf =>
                PresentationHandoutPdfExporter.ExportToBytes(
                    presentation,
                    new PresentationHandoutPdfExportRequest(normalizedRequest)),
            _ => throw new ArgumentOutOfRangeException(nameof(request), plan.Route, "Unsupported print output route."),
        };

        return new PresentationPrintOutputPackage(plan, bytes);
    }

    private static PresentationPrintOutputPackageRoute ResolveRoute(PresentationPrintLayoutKind layout) =>
        layout switch
        {
            PresentationPrintLayoutKind.FullPageSlides => PresentationPrintOutputPackageRoute.FullPageSlidesRasterPdf,
            PresentationPrintLayoutKind.NotesPages => PresentationPrintOutputPackageRoute.NotesPagePdf,
            PresentationPrintLayoutKind.Handouts => PresentationPrintOutputPackageRoute.HandoutPdf,
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unsupported print layout."),
        };

    private static int CalculatePageCount(PresentationPrintPlan plan, int? notesPageCount = null)
    {
        var slideCount = plan.SlideRange.SlideNumbers.Count;
        if (slideCount == 0)
            return 0;

        if (plan.Layout.Layout == PresentationPrintLayoutKind.NotesPages && notesPageCount is { } count)
            return count;

        return plan.Layout.IsHandout
            ? (int)Math.Ceiling(slideCount / (double)plan.Layout.SlidesPerPage)
            : slideCount;
    }

    private static string BuildLayoutSummary(PresentationPrintPlan plan, int? notesPageCount = null)
    {
        var pageCount = CalculatePageCount(plan, notesPageCount);
        var pageText = pageCount == 1 ? "1 page" : $"{pageCount} pages";
        var hiddenText = plan.PrintHiddenSlides ? " including hidden slides" : string.Empty;
        return $"{plan.Layout.DisplayName} - {plan.SlideRange.DisplayName}, {pageText}{hiddenText}";
    }

    private static PresentationPrintPreviewPlan BuildPreviewPlan(
        PresentationPrintPlan printPlan,
        PresentationPrintOutputPackageRoute route,
        int pageCount,
        bool canBuild,
        string? disabledReason,
        IReadOnlyList<PresentationNotesPagePreviewPlan>? notesPreviewPlans)
    {
        if (!canBuild)
        {
            return new PresentationPrintPreviewPlan(
                0,
                "No printable pages",
                CanPreview: false,
                disabledReason,
                []);
        }

        var pages = route switch
        {
            PresentationPrintOutputPackageRoute.FullPageSlidesRasterPdf =>
                BuildFullPagePreviewPages(printPlan.SlideRange.SlideNumbers),
            PresentationPrintOutputPackageRoute.NotesPagePdf =>
                notesPreviewPlans is { Count: > 0 }
                    ? BuildNotesPreviewPages(notesPreviewPlans)
                    : BuildNotesPreviewPages(printPlan.SlideRange.SlideNumbers, pageCount),
            PresentationPrintOutputPackageRoute.HandoutPdf =>
                BuildHandoutPreviewPages(printPlan.SlideRange.SlideNumbers, printPlan.Layout.SlidesPerPage),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unsupported print preview route."),
        };

        return new PresentationPrintPreviewPlan(
            pageCount,
            pageCount == 1 ? "1 printable page" : $"{pageCount} printable pages",
            CanPreview: pages.Count > 0,
            DisabledReason: null,
            pages);
    }

    private static IReadOnlyList<PresentationPrintPreviewPage> BuildFullPagePreviewPages(
        IReadOnlyList<int> slideNumbers) =>
        slideNumbers
            .Select((slideNumber, index) => new PresentationPrintPreviewPage(
                index,
                index + 1,
                PresentationPrintPreviewPageKind.FullPageSlide,
                [slideNumber],
                $"Slide {slideNumber}",
                $"Full-page slide {slideNumber}"))
            .ToArray();

    private static IReadOnlyList<PresentationPrintPreviewPage> BuildNotesPreviewPages(
        IReadOnlyList<int> slideNumbers,
        int pageCount)
    {
        var pages = new List<PresentationPrintPreviewPage>(pageCount);
        for (var index = 0; index < pageCount; index++)
        {
            var slideNumber = slideNumbers[Math.Min(index, slideNumbers.Count - 1)];
            var isContinuation = index >= slideNumbers.Count;
            pages.Add(new PresentationPrintPreviewPage(
                index,
                index + 1,
                PresentationPrintPreviewPageKind.NotesPage,
                [slideNumber],
                isContinuation ? $"Slide {slideNumber} notes continued" : $"Slide {slideNumber} notes",
                isContinuation
                    ? $"Notes continuation page for slide {slideNumber}"
                    : $"Notes page for slide {slideNumber}"));
        }

        return pages;
    }

    private static IReadOnlyList<PresentationPrintPreviewPage> BuildNotesPreviewPages(
        IReadOnlyList<PresentationNotesPagePreviewPlan> previewPlans)
    {
        var pages = new List<PresentationPrintPreviewPage>();
        foreach (var previewPlan in previewPlans)
        {
            var slideNumber = previewPlan.SlideNumber ?? 0;
            foreach (var renderedPage in previewPlan.RenderPages)
            {
                pages.Add(new PresentationPrintPreviewPage(
                    pages.Count,
                    pages.Count + 1,
                    PresentationPrintPreviewPageKind.NotesPage,
                    slideNumber > 0 ? [slideNumber] : [],
                    renderedPage.ThumbnailLabel,
                    renderedPage.Detail));
            }
        }

        return pages;
    }

    private static IReadOnlyList<PresentationPrintPreviewPage> BuildHandoutPreviewPages(
        IReadOnlyList<int> slideNumbers,
        int slidesPerPage)
    {
        var pages = new List<PresentationPrintPreviewPage>(
            (int)Math.Ceiling(slideNumbers.Count / (double)slidesPerPage));
        for (var index = 0; index < slideNumbers.Count; index += slidesPerPage)
        {
            var pageSlideNumbers = slideNumbers
                .Skip(index)
                .Take(slidesPerPage)
                .ToArray();
            pages.Add(new PresentationPrintPreviewPage(
                pages.Count,
                pages.Count + 1,
                PresentationPrintPreviewPageKind.Handout,
                pageSlideNumbers,
                $"Handout page {pages.Count + 1}",
                $"Handout with {FormatSlideSet(pageSlideNumbers)}"));
        }

        return pages;
    }

    private static string FormatSlideSet(IReadOnlyList<int> slideNumbers) =>
        slideNumbers.Count == 1
            ? $"slide {slideNumbers[0]}"
            : $"slides {string.Join(", ", slideNumbers)}";

    private static PresentationPrintRequest ToPrintRequest(PresentationPrintPlan plan) =>
        new(
            plan.Layout.Layout,
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.SelectedSlides,
                SelectedSlideNumbers: plan.SlideRange.SlideNumbers),
            plan.Layout.IsHandout ? plan.Layout.SlidesPerPage : null,
            plan.PrintHiddenSlides,
            plan.Options.Copies,
            plan.Options.Collate,
            plan.Options.ColorMode,
            plan.Options.FrameSlides,
            plan.Options.IncludeCommentsAndInkMarkup);
}
