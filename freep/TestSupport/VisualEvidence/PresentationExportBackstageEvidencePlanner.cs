using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationExportBackstageEvidencePlan(
    string SourceName,
    int SlideCount,
    bool RequiresPowerPointComForLocalEvidence,
    IReadOnlyList<PresentationExportBackstageEvidenceRow> Rows);

public sealed record PresentationExportBackstageEvidenceRow(
    string EvidenceId,
    string Area,
    string SharedPlanner,
    string Status,
    string WpfEvidence,
    string AvaloniaEvidence,
    string PowerPointBaseline,
    bool RequiresPowerPointComBaseline,
    string Detail);

/// <summary>
/// Shared no-COM evidence contract for FreeP Backstage export and print surfaces.
/// Hosts should remain thin adapters over these rows; PowerPoint visual baselines stay deferred.
/// </summary>
public static class PresentationExportBackstageEvidencePlanner
{
    public const string SharedPlannerEvidence = "shared-export-backstage-planner";
    public const string WpfEvidence = "wpf-shared-export-backstage-adapter";
    public const string AvaloniaEvidence = "avalonia-shared-export-backstage-adapter";
    public const string PowerPointBaselineDeferred = "n/a/deferred-powerpoint-com-baseline";

    private static readonly PresentationNativePrintHandoffHostCapabilities WpfPrintHostCapabilities =
        PresentationNativePrintHandoffHostCapabilities.Deferred(
            "WPF print host",
            "Native printer handoff adapter is not required for local shared evidence.");

    private static readonly PresentationNativePrintHandoffHostCapabilities AvaloniaPrintHostCapabilities =
        PresentationNativePrintHandoffHostCapabilities.Deferred(
            "Avalonia print host",
            "Native printer handoff adapter is not required for local shared evidence.");

    private static readonly PresentationVideoExportHandoffHostCapabilities WpfVideoHostCapabilities =
        PresentationVideoExportHandoffHostCapabilities.Deferred(
            "WPF video export host",
            "MP4 encoder, narration capture, and camera/media capture adapters are not required for local shared evidence.");

    private static readonly PresentationVideoExportHandoffHostCapabilities AvaloniaVideoHostCapabilities =
        PresentationVideoExportHandoffHostCapabilities.Deferred(
            "Avalonia video export host",
            "MP4 encoder, narration capture, and camera/media capture adapters are not required for local shared evidence.");

    public static PresentationExportBackstageEvidencePlan Build(Presentation presentation, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var normalizedSourceName = string.IsNullOrWhiteSpace(sourceName)
            ? "Presentation.pptx"
            : Path.GetFileName(sourceName.Trim());
        var rows = new List<PresentationExportBackstageEvidenceRow>
        {
            BuildFixedPdfExportRow(presentation.Slides.Count),
            BuildImageExportRow(presentation.Slides.Count),
            BuildPrintRow(
                "freep.export.backstage.print-full-page",
                "Backstage Print full-page slide package handoff",
                new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides),
                presentation,
                normalizedSourceName),
            BuildPrintRow(
                "freep.export.backstage.print-handouts-3",
                "Backstage Print 3-up handout package handoff",
                new PresentationPrintRequest(
                    PresentationPrintLayoutKind.Handouts,
                    HandoutSlidesPerPage: 3),
                presentation,
                normalizedSourceName),
            BuildVideoFramePackageRow(presentation, normalizedSourceName),
        };

        return new PresentationExportBackstageEvidencePlan(
            normalizedSourceName,
            presentation.Slides.Count,
            RequiresPowerPointComForLocalEvidence: false,
            rows);
    }

    private static PresentationExportBackstageEvidenceRow BuildFixedPdfExportRow(int slideCount)
    {
        var backstage = PresentationExportPlanner.BuildBackstageExportPlan();
        var pdfAction = backstage.FixedLayoutActions.Single(action =>
            action.CommandId == PresentationExportPlanner.PdfExportCommandId);
        var printPlan = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides),
            slideCount);

        return new PresentationExportBackstageEvidenceRow(
            "freep.export.backstage.fixed-layout-pdf",
            "Backstage Export fixed-layout PDF action",
            SharedPlannerEvidence,
            printPlan.CanBuildPackage ? "shared-export-plan" : "no-slides",
            WpfEvidence,
            AvaloniaEvidence,
            PowerPointBaselineDeferred,
            RequiresPowerPointComBaseline: true,
            $"{pdfAction.Label}: {pdfAction.Description}; route={printPlan.Route}; pages={printPlan.PageCount}; range={printPlan.SlideRangeSummary}");
    }

    private static PresentationExportBackstageEvidenceRow BuildImageExportRow(int slideCount)
    {
        var backstage = PresentationExportPlanner.BuildBackstageExportPlan();
        var imageAction = backstage.DeferredActions.Single(action =>
            action.CommandId == PresentationExportPlanner.ImageExportCommandId);
        var imagePlan = PresentationExportPlanner.BuildImageExportPlan(null, slideCount);

        return new PresentationExportBackstageEvidenceRow(
            "freep.export.backstage.image-sequence",
            "Backstage Export image sequence action",
            SharedPlannerEvidence,
            imagePlan.IsImplemented ? "shared-export-plan" : "deferred",
            WpfEvidence,
            AvaloniaEvidence,
            PowerPointBaselineDeferred,
            RequiresPowerPointComBaseline: true,
            $"{imageAction.Label}: {imageAction.Description}; size={imagePlan.WidthPx}x{imagePlan.HeightPx}; range={imagePlan.SlideRange.DisplayName}");
    }

    private static PresentationExportBackstageEvidenceRow BuildPrintRow(
        string evidenceId,
        string area,
        PresentationPrintRequest request,
        Presentation presentation,
        string sourceName)
    {
        var wpf = PresentationPrintBackstagePlanner.Build(
            request,
            presentation,
            hostCapabilities: WpfPrintHostCapabilities,
            suggestedBaseFileName: sourceName);
        var avalonia = PresentationPrintBackstagePlanner.Build(
            request,
            presentation,
            hostCapabilities: AvaloniaPrintHostCapabilities,
            suggestedBaseFileName: sourceName);
        var status = ClassifyPrintStatus(wpf, avalonia);

        return new PresentationExportBackstageEvidenceRow(
            evidenceId,
            area,
            SharedPlannerEvidence,
            status,
            FormatPrintEvidence("WPF", wpf),
            FormatPrintEvidence("Avalonia", avalonia),
            PowerPointBaselineDeferred,
            RequiresPowerPointComBaseline: true,
            $"route={wpf.PackagePlan.Route}; pages={wpf.PageCount}; layout={wpf.LayoutSummary}; nativePrint={wpf.NativePrintHandoff.Status}");
    }

    private static PresentationExportBackstageEvidenceRow BuildVideoFramePackageRow(
        Presentation presentation,
        string sourceName)
    {
        var packagePlan = PresentationVideoFramePackageExecutor.BuildPackagePlan(
            new PresentationVideoExportRequest(
                Quality: PresentationVideoQualityKind.Hd,
                SecondsPerSlide: 4,
                UseRecordedTimings: true,
                IncludeNarration: true),
            presentation);
        var wpf = PresentationVideoFramePackageExecutor.BuildHandoffPlan(packagePlan, WpfVideoHostCapabilities);
        var avalonia = PresentationVideoFramePackageExecutor.BuildHandoffPlan(packagePlan, AvaloniaVideoHostCapabilities);
        var status = ClassifyVideoStatus(wpf, avalonia);

        return new PresentationExportBackstageEvidenceRow(
            "freep.export.backstage.video-frame-package",
            "Backstage Export video frame-package handoff",
            SharedPlannerEvidence,
            status,
            FormatVideoEvidence(wpf),
            FormatVideoEvidence(avalonia),
            PowerPointBaselineDeferred,
            RequiresPowerPointComBaseline: false,
            $"source={sourceName}; frames={packagePlan.ExportPlan.Storyboard.Segments.Count}; duration={packagePlan.ExportPlan.EstimatedDuration}; materialization={status}; encoder={wpf.Status}");
    }

    private static string ClassifyPrintStatus(
        PresentationPrintBackstagePlan wpf,
        PresentationPrintBackstagePlan avalonia)
    {
        if (!wpf.CanBuildPackage || !avalonia.CanBuildPackage)
            return "no-slides";

        if (wpf.PackagePlan.Route == avalonia.PackagePlan.Route &&
            wpf.PageCount == avalonia.PageCount &&
            wpf.NativePrintHandoff.Status == PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost &&
            avalonia.NativePrintHandoff.Status == PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost)
        {
            return "shared-package-ready-host-deferred";
        }

        return "mismatched-host-evidence";
    }

    private static string ClassifyVideoStatus(
        PresentationVideoExportHandoffPlan wpf,
        PresentationVideoExportHandoffPlan avalonia)
    {
        if (!wpf.IsFramePackageReady || !avalonia.IsFramePackageReady)
            return "no-slides";

        if (wpf.PackagePlan.ExportPlan.Storyboard.Segments.Count == avalonia.PackagePlan.ExportPlan.Storyboard.Segments.Count &&
            wpf.Status == PresentationVideoExportHandoffStatus.EncoderInputPackageReadyHostDeferred &&
            avalonia.Status == PresentationVideoExportHandoffStatus.EncoderInputPackageReadyHostDeferred)
        {
            return "package-materialization-ready/host-deferred";
        }

        return "mismatched-host-evidence";
    }

    private static string FormatPrintEvidence(string host, PresentationPrintBackstagePlan plan) =>
        $"{host}:{plan.PackagePlan.Route}:{plan.PageCount}:{plan.NativePrintHandoff.Status}";

    private static string FormatVideoEvidence(PresentationVideoExportHandoffPlan plan) =>
        $"{plan.HostCapabilities.HostName}:{plan.PackagePlan.ExportPlan.Storyboard.Segments.Count}:{plan.Status}";
}
