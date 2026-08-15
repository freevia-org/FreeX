using Free.Shared.Shell;
using Free.Shared.Localization;
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

public sealed record PresentationPrintOutputPackageValidation(
    int ByteCount,
    bool HasBytes,
    bool HasPdfHeader,
    bool HasPdfEofMarker,
    bool PlanCanBuildPackage,
    bool IsValid,
    string? FailureReason);

public sealed record PresentationPrintOutputPackageExecutionDescriptor(
    PresentationPrintOutputPackagePlan PackagePlan,
    PresentationNativePrintHandoffPlan HandoffPlan,
    PresentationPrintOutputPackageValidation Validation,
    string PackageKind,
    string ContentType,
    string DefaultExtensionWithDot,
    string SuggestedFileName,
    string SuggestedDocumentName,
    string SuggestedPrintJobName,
    int ByteCount,
    bool IsHostReadyPdfPackage,
    bool CanMaterialize,
    string? DisabledReason);

public sealed record PresentationPrintOutputPackageMaterializationResult(
    PresentationPrintOutputPackageExecutionDescriptor Descriptor,
    string TargetPath,
    bool Succeeded,
    string? FailureReason);

public enum PresentationNativePrintHandoffStatus
{
    PackageReadyHostHandoffRequired,
    NoSlides,
    HostPrinterUnavailableDeferredByHost,
}

public sealed record PresentationNativePrintHandoffHostCapabilities(
    string HostName,
    bool CanOpenNativePrintDialog,
    string? UnavailableReason,
    bool CanSubmitToNativePrinter = false)
{
    public static PresentationNativePrintHandoffHostCapabilities Available(string hostName) =>
        new(hostName, CanOpenNativePrintDialog: true, UnavailableReason: null);

    public static PresentationNativePrintHandoffHostCapabilities Deferred(string hostName, string unavailableReason) =>
        new(hostName, CanOpenNativePrintDialog: false, unavailableReason);

    public static PresentationNativePrintHandoffHostCapabilities NativePrinterSubmissionAvailable(
        string hostName) =>
        new(hostName, CanOpenNativePrintDialog: false, UnavailableReason: null, CanSubmitToNativePrinter: true);
}

public sealed record PresentationNativePrintSurfacePlan(
    LocalizedTextDescriptor SectionHeading,
    LocalizedTextDescriptor QueueLabel,
    LocalizedTextDescriptor NoQueuesStatus,
    LocalizedTextDescriptor NativeDialogLabel,
    string PrinterPickerAutomationId,
    string NativeDialogAutomationId)
{
    public LocalizedTextDescriptor BuildPrinterSelectedStatus(string printerName) =>
        PresentationShellTextCatalog.PrinterSelectedStatus(printerName);
}

public sealed record PresentationNativePrintHandoffPlan(
    PresentationPrintOutputPackagePlan PackagePlan,
    PresentationNativePrintHandoffStatus Status,
    string StatusText,
    string Reason,
    bool CanBuildPackage,
    bool IsPackageReady,
    bool CanOpenNativePrintDialog,
    bool RequiresHostHandoff,
    bool CanSubmitToNativePrinter,
    PresentationPrintOutputPackageRoute Route,
    int PageCount,
    string ContentType,
    string SuggestedTempFileName,
    string SuggestedDocumentName,
    string SuggestedPrintJobName,
    string DefaultExtensionWithDot,
    string LayoutSummary,
    string SlideRangeSummary,
    string OptionsSummary,
    IReadOnlyList<string> OptionSummaryLines,
    string? DisabledReason,
    PresentationNativePrintSurfacePlan Surface);

/// <summary>
/// Shared printable-output execution for FreeP. Hosts provide only raster slide rendering and
/// platform PDF writing for full-page slides; layout selection and notes/handout routing stay shared.
/// </summary>
public static class PresentationPrintOutputPackageExecutor
{
    public const string PdfContentType = "application/pdf";
    public const string PrintOutputPackageKind = "FreePPrintablePdfPackage";
    public const string NativePrinterDialogDeferredReason =
        "Printable PDF package execution is available; native printer dialog handoff is deferred.";
    public const string NativePrintPackageReadyReason =
        "Printable PDF package is ready for native host printer handoff.";
    public const string NativePrintHostUnavailableReason =
        "Printable PDF package is ready, but the host printer adapter is unavailable.";
    public const string NativePrintNoSlidesReason =
        "Native print handoff requires at least one slide.";
    public const string InvalidPackageReason =
        "Native print handoff requires a valid host-ready PDF package.";

    public static PresentationNativePrintSurfacePlan NativePrintSurface { get; } = new(
        PresentationShellTextCatalog.WindowsPrinterHeading,
        PresentationShellTextCatalog.WindowsPrinterQueueLabel,
        PresentationShellTextCatalog.NoWindowsPrinterQueuesStatus,
        PresentationShellTextCatalog.WindowsPrinterDialogLabel,
        PrinterPickerAutomationId: "FreePWindowsPrinterPicker",
        NativeDialogAutomationId: "FreePWindowsPrinterDialog");

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

        var printPlan = PresentationExportPlanner.BuildPrintPlan(request, presentation);
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
        var disabledReason = canBuild
            ? null
            : printPlan.SlideRange.ValidationMessage ?? "Print output requires at least one slide.";
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
            NativePrinterDialogDeferred: false,
            disabledReason);
    }

    public static PresentationNativePrintHandoffPlan BuildNativePrintHandoffPlan(
        PresentationPrintOutputPackagePlan packagePlan,
        PresentationNativePrintHandoffHostCapabilities? hostCapabilities = null,
        string? suggestedBaseFileName = null)
    {
        ArgumentNullException.ThrowIfNull(packagePlan);

        hostCapabilities ??= PresentationNativePrintHandoffHostCapabilities.Available("Host");
        var isPackageReady = packagePlan.CanBuildPackage && packagePlan.PageCount > 0;
        var status = ResolveHandoffStatus(packagePlan, hostCapabilities);
        var suggestedDocumentName = BuildSuggestedDocumentName(suggestedBaseFileName);
        var suggestedTempFileName = BuildSuggestedTempFileName(suggestedDocumentName, packagePlan.DefaultExtensionWithDot);
        var suggestedPrintJobName = BuildSuggestedPrintJobName(suggestedDocumentName, packagePlan);
        var reason = status switch
        {
            PresentationNativePrintHandoffStatus.PackageReadyHostHandoffRequired => NativePrintPackageReadyReason,
            PresentationNativePrintHandoffStatus.NoSlides => packagePlan.DisabledReason ?? NativePrintNoSlidesReason,
            PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost =>
                string.IsNullOrWhiteSpace(hostCapabilities.UnavailableReason)
                    ? NativePrintHostUnavailableReason
                    : $"{NativePrintHostUnavailableReason} {hostCapabilities.UnavailableReason}",
            _ => throw new ArgumentOutOfRangeException(nameof(packagePlan), status, "Unsupported native print handoff status."),
        };

        return new PresentationNativePrintHandoffPlan(
            packagePlan,
            status,
            FormatHandoffStatus(status),
            reason,
            packagePlan.CanBuildPackage,
            isPackageReady,
            isPackageReady && hostCapabilities.CanOpenNativePrintDialog,
            isPackageReady,
            isPackageReady && hostCapabilities.CanSubmitToNativePrinter,
            packagePlan.Route,
            packagePlan.PageCount,
            packagePlan.ContentType,
            suggestedTempFileName,
            suggestedDocumentName,
            suggestedPrintJobName,
            packagePlan.DefaultExtensionWithDot,
            packagePlan.LayoutSummary,
            packagePlan.SlideRangeSummary,
            packagePlan.Options.DisplaySummary,
            packagePlan.Options.SummaryLines,
            status == PresentationNativePrintHandoffStatus.NoSlides ? reason : null,
            NativePrintSurface);
    }

    public static PresentationPrintOutputPackage BuildPackage(
        Presentation presentation,
        PresentationPrintRequest? request,
        PresentationSlideImageRenderer renderSlideToPng,
        PresentationRasterPdfWriter writeRasterPdf,
        PresentationPdfContentWriter? writeVectorPdf = null,
        PresentationSlideImageRendererWithPrintMarkup? renderSlideWithMarkup = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(renderSlideToPng);
        ArgumentNullException.ThrowIfNull(writeRasterPdf);

        var plan = BuildPackagePlan(request, presentation);
        if (!plan.CanBuildPackage)
            return new PresentationPrintOutputPackage(plan, []);

        var normalizedRequest = ToPrintRequest(plan.PrintPlan);
        var bytes = BuildPackageBytes(
            plan,
            presentation,
            normalizedRequest,
            renderSlideToPng,
            writeRasterPdf,
            writeVectorPdf,
            renderSlideWithMarkup);
        return new PresentationPrintOutputPackage(plan, bytes);
    }

    /// <summary>
    /// Like <see cref="BuildPackage"/> but captures image-decode diagnostics from both the slide
    /// composite render pass and the PDF writer, mirroring the PDF/Image/Video export paths (see
    /// <see cref="SlideImageRenderDiagnostics"/>). Print silently dropped undecodable pictures with
    /// no way for the caller to learn a page rendered incomplete until this overload existed.
    /// </summary>
    public static PresentationPrintOutputPackage BuildPackageWithDiagnostics(
        Presentation presentation,
        PresentationPrintRequest? request,
        IPresentationFileRenderPort render,
        ICollection<string> imageDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(imageDiagnostics);

        var plan = BuildPackagePlan(request, presentation);
        if (!plan.CanBuildPackage)
            return new PresentationPrintOutputPackage(plan, []);

        var normalizedRequest = ToPrintRequest(plan.PrintPlan);
        using var capture = SlideImageRenderDiagnostics.Capture(imageDiagnostics);
        var bytes = BuildPackageBytes(
            plan,
            presentation,
            normalizedRequest,
            render.RenderSlideToPng,
            document => render.WriteRasterPdfWithDiagnostics(document, imageDiagnostics),
            document => render.WriteVectorPdfWithDiagnostics(document, imageDiagnostics),
            render.RenderSlideToPngWithPrintMarkup);
        return new PresentationPrintOutputPackage(plan, bytes);
    }

    private static byte[] BuildPackageBytes(
        PresentationPrintOutputPackagePlan plan,
        Presentation presentation,
        PresentationPrintRequest normalizedRequest,
        PresentationSlideImageRenderer renderSlideToPng,
        PresentationRasterPdfWriter writeRasterPdf,
        PresentationPdfContentWriter? writeVectorPdf,
        PresentationSlideImageRendererWithPrintMarkup? renderSlideWithMarkup) => plan.Route switch
        {
            PresentationPrintOutputPackageRoute.FullPageSlidesRasterPdf =>
                PresentationRasterPdfExporter.ExportToBytes(
                    presentation,
                    new PresentationRasterPdfExportRequest(normalizedRequest.SlideRange),
                    renderSlideToPng,
                    writeRasterPdf,
                    normalizedRequest.IncludeCommentsAndInkMarkup ? renderSlideWithMarkup : null),
            PresentationPrintOutputPackageRoute.NotesPagePdf =>
                writeVectorPdf is null
                    ? PresentationNotesPagePdfExporter.ExportToBytes(
                        presentation,
                        new PresentationNotesPagePdfExportRequest(normalizedRequest))
                    : PresentationNotesPagePdfExporter.ExportToBytes(
                        presentation,
                        new PresentationNotesPagePdfExportRequest(normalizedRequest),
                        writeVectorPdf),
            PresentationPrintOutputPackageRoute.HandoutPdf =>
                writeVectorPdf is null
                    ? PresentationHandoutPdfExporter.ExportToBytes(
                        presentation,
                        new PresentationHandoutPdfExportRequest(normalizedRequest))
                    : PresentationHandoutPdfExporter.ExportToBytes(
                        presentation,
                        new PresentationHandoutPdfExportRequest(normalizedRequest),
                        writeVectorPdf),
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan.Route, "Unsupported print output route."),
        };

    public static PresentationPrintOutputPackageValidation ValidatePackage(
        PresentationPrintOutputPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var bytes = package.Bytes;
        var hasBytes = bytes.Length > 0;
        var hasPdfHeader = StartsWith(bytes, "%PDF-"u8);
        var hasPdfEofMarker = Contains(bytes, "%%EOF"u8);
        var planCanBuild = package.Plan.CanBuildPackage && package.Plan.PageCount > 0;
        var failureReason =
            !planCanBuild ? package.Plan.DisabledReason ?? NativePrintNoSlidesReason :
            !hasBytes ? "Printable PDF package contains no bytes." :
            !hasPdfHeader ? "Printable PDF package does not start with a PDF header." :
            !hasPdfEofMarker ? "Printable PDF package does not contain a PDF EOF marker." :
            null;

        return new PresentationPrintOutputPackageValidation(
            bytes.Length,
            hasBytes,
            hasPdfHeader,
            hasPdfEofMarker,
            planCanBuild,
            failureReason is null,
            failureReason);
    }

    public static PresentationPrintOutputPackageExecutionDescriptor BuildExecutionDescriptor(
        PresentationPrintOutputPackage package,
        PresentationNativePrintHandoffHostCapabilities? hostCapabilities = null,
        string? suggestedBaseFileName = null)
    {
        ArgumentNullException.ThrowIfNull(package);

        var handoffPlan = BuildNativePrintHandoffPlan(
            package.Plan,
            hostCapabilities,
            suggestedBaseFileName);
        var validation = ValidatePackage(package);
        var isHostReady = validation.IsValid &&
            package.Plan.ContentType == PdfContentType &&
            string.Equals(package.Plan.DefaultExtensionWithDot, PresentationExportPlanner.PdfExportExtension, StringComparison.OrdinalIgnoreCase);
        var disabledReason = isHostReady ? null : validation.FailureReason ?? InvalidPackageReason;

        return new PresentationPrintOutputPackageExecutionDescriptor(
            package.Plan,
            handoffPlan,
            validation,
            PrintOutputPackageKind,
            package.Plan.ContentType,
            package.Plan.DefaultExtensionWithDot,
            handoffPlan.SuggestedTempFileName,
            handoffPlan.SuggestedDocumentName,
            handoffPlan.SuggestedPrintJobName,
            validation.ByteCount,
            isHostReady,
            isHostReady,
            disabledReason);
    }

    public static PresentationPrintOutputPackageMaterializationResult MaterializePackageForHandoff(
        PresentationPrintOutputPackage package,
        string targetPath,
        PresentationNativePrintHandoffHostCapabilities? hostCapabilities = null,
        string? suggestedBaseFileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var descriptor = BuildExecutionDescriptor(package, hostCapabilities, suggestedBaseFileName);
        if (!descriptor.CanMaterialize)
        {
            return new PresentationPrintOutputPackageMaterializationResult(
                descriptor,
                targetPath,
                Succeeded: false,
                descriptor.DisabledReason);
        }

        AtomicFileWriter.WriteAllBytes(targetPath, package.Bytes);
        return new PresentationPrintOutputPackageMaterializationResult(
            descriptor,
            targetPath,
            Succeeded: true,
            FailureReason: null);
    }

    private static PresentationPrintOutputPackageRoute ResolveRoute(PresentationPrintLayoutKind layout) =>
        layout switch
        {
            PresentationPrintLayoutKind.FullPageSlides => PresentationPrintOutputPackageRoute.FullPageSlidesRasterPdf,
            PresentationPrintLayoutKind.NotesPages => PresentationPrintOutputPackageRoute.NotesPagePdf,
            PresentationPrintLayoutKind.Handouts => PresentationPrintOutputPackageRoute.HandoutPdf,
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unsupported print layout."),
        };

    private static PresentationNativePrintHandoffStatus ResolveHandoffStatus(
        PresentationPrintOutputPackagePlan packagePlan,
        PresentationNativePrintHandoffHostCapabilities hostCapabilities)
    {
        if (!packagePlan.CanBuildPackage || packagePlan.PageCount == 0)
            return PresentationNativePrintHandoffStatus.NoSlides;

        return hostCapabilities.CanOpenNativePrintDialog || hostCapabilities.CanSubmitToNativePrinter
            ? PresentationNativePrintHandoffStatus.PackageReadyHostHandoffRequired
            : PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost;
    }

    private static string FormatHandoffStatus(PresentationNativePrintHandoffStatus status) =>
        status switch
        {
            PresentationNativePrintHandoffStatus.PackageReadyHostHandoffRequired => "Ready for host handoff",
            PresentationNativePrintHandoffStatus.NoSlides => "No printable slides",
            PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost => "Deferred by host",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported native print handoff status."),
        };

    private static string BuildSuggestedDocumentName(string? suggestedBaseFileName)
    {
        var baseName = string.IsNullOrWhiteSpace(suggestedBaseFileName)
            ? "Presentation"
            : Path.GetFileNameWithoutExtension(suggestedBaseFileName.Trim());
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(baseName.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Presentation" : sanitized;
    }

    private static string BuildSuggestedTempFileName(string suggestedDocumentName, string extensionWithDot) =>
        $"{suggestedDocumentName}-print{extensionWithDot}";

    private static string BuildSuggestedPrintJobName(
        string suggestedDocumentName,
        PresentationPrintOutputPackagePlan packagePlan) =>
        $"{suggestedDocumentName} - {packagePlan.LayoutSummary}";

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
            plan.SlideRange.Kind switch
            {
                PresentationSlideRangeKind.CurrentSlide => new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CurrentSlide,
                    CurrentSlideNumber: plan.SlideRange.SlideNumbers.Count == 0
                        ? null
                        : plan.SlideRange.SlideNumbers[0]),
                PresentationSlideRangeKind.CustomRange => new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    StartSlideNumber: plan.SlideRange.SlideNumbers.Count == 0
                        ? null
                        : plan.SlideRange.SlideNumbers[0],
                    EndSlideNumber: plan.SlideRange.SlideNumbers.Count == 0
                        ? null
                        : plan.SlideRange.SlideNumbers[^1],
                    CustomRangeText: plan.SlideRange.CustomRangeText),
                PresentationSlideRangeKind.SelectedSlides => new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.SelectedSlides,
                    SelectedSlideNumbers: plan.SlideRange.SlideNumbers),
                _ => new PresentationSlideRangeRequest(PresentationSlideRangeKind.AllSlides),
            },
            plan.Layout.IsHandout ? plan.Layout.SlidesPerPage : null,
            plan.PrintHiddenSlides,
            plan.Options.Copies,
            plan.Options.Collate,
            plan.Options.ColorMode,
            plan.Options.FrameSlides,
            plan.Options.IncludeCommentsAndInkMarkup);

    private static bool StartsWith(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> marker) =>
        bytes.Length >= marker.Length && bytes[..marker.Length].SequenceEqual(marker);

    private static bool Contains(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> marker)
    {
        if (marker.Length == 0)
            return true;

        if (bytes.Length < marker.Length)
            return false;

        for (var index = 0; index <= bytes.Length - marker.Length; index++)
        {
            if (bytes.Slice(index, marker.Length).SequenceEqual(marker))
                return true;
        }

        return false;
    }
}
