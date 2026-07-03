using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationPrintOutputPackageRoute
{
    FullPageSlidesRasterPdf,
    NotesPagePdf,
    HandoutPdf,
}

public sealed record PresentationPrintOutputPackagePlan(
    PresentationPrintPlan PrintPlan,
    PresentationPrintOutputPackageRoute Route,
    string ContentType,
    string DefaultExtensionWithDot,
    int PageCount,
    string LayoutSummary,
    string SlideRangeSummary,
    PresentationPrintOptionsPlan Options,
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
        var canBuild = printPlan.SlideRange.SlideNumbers.Count > 0;

        return new PresentationPrintOutputPackagePlan(
            printPlan,
            ResolveRoute(printPlan.Layout.Layout),
            PdfContentType,
            PresentationExportPlanner.PdfExportExtension,
            CalculatePageCount(printPlan),
            BuildLayoutSummary(printPlan),
            printPlan.SlideRange.DisplayName,
            printPlan.Options,
            canBuild,
            NativePrinterDialogDeferred: true,
            canBuild ? null : "Print output requires at least one slide.");
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

        var plan = BuildPackagePlan(request, presentation.Slides.Count);
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

    private static int CalculatePageCount(PresentationPrintPlan plan)
    {
        var slideCount = plan.SlideRange.SlideNumbers.Count;
        if (slideCount == 0)
            return 0;

        return plan.Layout.IsHandout
            ? (int)Math.Ceiling(slideCount / (double)plan.Layout.SlidesPerPage)
            : slideCount;
    }

    private static string BuildLayoutSummary(PresentationPrintPlan plan)
    {
        var pageCount = CalculatePageCount(plan);
        var pageText = pageCount == 1 ? "1 page" : $"{pageCount} pages";
        var hiddenText = plan.PrintHiddenSlides ? " including hidden slides" : string.Empty;
        return $"{plan.Layout.DisplayName} - {plan.SlideRange.DisplayName}, {pageText}{hiddenText}";
    }

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
