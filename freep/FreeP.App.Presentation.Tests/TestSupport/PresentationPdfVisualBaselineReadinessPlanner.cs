using System.Globalization;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationPdfVisualBaselineReadinessPlan(
    string SourceName,
    int SlideCount,
    bool RequiresPowerPointComForLocalEvidence,
    bool RequiresPowerPointComForAuthoritativeBaseline,
    string PowerPointBaselineStatus,
    IReadOnlyList<PresentationPdfVisualBaselineReadinessRow> Rows)
{
    public bool HasMatchingWpfAvaloniaContracts =>
        Rows.All(row => string.Equals(row.WpfManifestFingerprint, row.AvaloniaManifestFingerprint, StringComparison.Ordinal));
}

public sealed record PresentationPdfVisualBaselineReadinessRow(
    string EvidenceId,
    string OutputKind,
    string SharedPlanner,
    string SharedRoute,
    string Status,
    int PageCount,
    string SlideRangeSummary,
    string ContentType,
    string DefaultExtensionWithDot,
    string WpfManifestFingerprint,
    string AvaloniaManifestFingerprint,
    string BaselineManifestPath,
    string WpfArtifactPath,
    string AvaloniaArtifactPath,
    string PowerPointPdfArtifact,
    string PowerPointPngArtifactPattern,
    string WpfPngArtifactPattern,
    string AvaloniaPngArtifactPattern,
    string WpfAvaloniaDiffReportPath,
    string PowerPointWpfDiffReportPath,
    string PowerPointAvaloniaDiffReportPath,
    int RasterizationDpi,
    string DiffThresholdProfile,
    string BaselineArtifactPattern,
    string PowerPointBaseline,
    bool RequiresPowerPointComBaseline,
    string Detail);

/// <summary>
/// Host-neutral readiness contract for broader FreeP PDF visual baselines. WPF and Avalonia rows
/// intentionally carry the same manifest fingerprint; only a COM-capable PowerPoint host can fill
/// the authoritative baseline artifacts referenced by the rows.
/// </summary>
public static class PresentationPdfVisualBaselineReadinessPlanner
{
    public const string PortableSlidePdfEvidenceId = "freep.pdf.baseline.portable-slide-pdf";
    public const string FullPageRasterPdfEvidenceId = "freep.pdf.baseline.full-page-raster-pdf";
    public const string HandoutPdfEvidenceId = "freep.pdf.baseline.handout-3up-pdf";
    public const string NotesPagePdfEvidenceId = "freep.pdf.baseline.notes-page-pdf";
    public const string PowerPointBaselineDeferred = "n/a/deferred-powerpoint-com-pdf-and-png-baseline";
    public const int BaselineRasterizationDpi = 144;
    public const string DiffThresholdProfile = "pdf-visual-baseline-readiness-v1/manual-calibration-required";
    public const string PowerPointBaselineStatus =
        "Authoritative PowerPoint PDF/PNG baseline capture is deferred until PowerPoint.Application COM is available.";
    private const string PortableSlidePdfRoute = "PortableSlidePdf";
    private const string PortableSlidePdfPlanner = "PresentationPdfExporter.BuildDocument";
    private const string PrintPackagePlanner = "PresentationPrintOutputPackageExecutor.BuildPackagePlan";
    private const string BaselineManifestPattern = "manifest/{sourceStem}/{evidenceId}.json";
    private const string WpfArtifactPattern = "wpf-pdf/{sourceStem}/{evidenceId}.pdf";
    private const string AvaloniaArtifactPattern = "avalonia-pdf/{sourceStem}/{evidenceId}.pdf";
    private const string PowerPointPdfArtifactPattern = "powerpoint-pdf/{sourceStem}/{evidenceId}.pdf";
    private const string PowerPointPngArtifactPattern = "powerpoint-png/{sourceStem}/{evidenceId}/slide-NN.png";
    private const string WpfPngArtifactPattern = "wpf-png/{sourceStem}/{evidenceId}/page-NN.png";
    private const string AvaloniaPngArtifactPattern = "avalonia-png/{sourceStem}/{evidenceId}/page-NN.png";
    private const string WpfAvaloniaDiffReportPattern = "diff/wpf-vs-avalonia/{sourceStem}/{evidenceId}.json";
    private const string PowerPointWpfDiffReportPattern = "diff/powerpoint-vs-wpf/{sourceStem}/{evidenceId}.json";
    private const string PowerPointAvaloniaDiffReportPattern = "diff/powerpoint-vs-avalonia/{sourceStem}/{evidenceId}.json";
    private const string PowerPointBaselinePattern =
        PowerPointPdfArtifactPattern + " + " + PowerPointPngArtifactPattern;

    public static PresentationPdfVisualBaselineReadinessPlan Build(
        Presentation presentation,
        string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var normalizedSourceName = NormalizeSourceName(sourceName);
        var sourceStem = BuildArtifactStem(normalizedSourceName);
        var rows = new[]
        {
            BuildPortableSlidePdfRow(presentation, normalizedSourceName, sourceStem),
            BuildPrintPackageRow(
                FullPageRasterPdfEvidenceId,
                "Full-page slide raster PDF",
                new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides),
                presentation,
                normalizedSourceName,
                sourceStem),
            BuildPrintPackageRow(
                HandoutPdfEvidenceId,
                "3-up handout PDF",
                new PresentationPrintRequest(
                    PresentationPrintLayoutKind.Handouts,
                    HandoutSlidesPerPage: 3),
                presentation,
                normalizedSourceName,
                sourceStem),
            BuildPrintPackageRow(
                NotesPagePdfEvidenceId,
                "Notes-page PDF",
                new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages),
                presentation,
                normalizedSourceName,
                sourceStem),
        };

        return new PresentationPdfVisualBaselineReadinessPlan(
            normalizedSourceName,
            presentation.Slides.Count,
            RequiresPowerPointComForLocalEvidence: false,
            RequiresPowerPointComForAuthoritativeBaseline: true,
            PowerPointBaselineStatus,
            rows);
    }

    private static PresentationPdfVisualBaselineReadinessRow BuildPortableSlidePdfRow(
        Presentation presentation,
        string sourceName,
        string sourceStem)
    {
        var document = PresentationPdfExporter.BuildDocument(presentation);
        var pageCount = document.Pages.Count;
        var slideRangeSummary = presentation.Slides.Count == 0
            ? "No slides (portable placeholder page)"
            : PresentationExportPlanner.BuildSlideRangePlan(null, presentation.Slides.Count).DisplayName;
        var firstPage = document.Pages[0];
        var fingerprint = BuildFingerprint(
            sourceName,
            sourceStem,
            PortableSlidePdfEvidenceId,
            PortableSlidePdfRoute,
            PresentationPrintOutputPackageExecutor.PdfContentType,
            PresentationExportPlanner.PdfExportExtension,
            pageCount,
            slideRangeSummary);

        return new PresentationPdfVisualBaselineReadinessRow(
            PortableSlidePdfEvidenceId,
            "Portable slide PDF",
            PortableSlidePdfPlanner,
            PortableSlidePdfRoute,
            presentation.Slides.Count == 0 ? "shared-portable-placeholder-ready" : "shared-pdf-plan-ready",
            pageCount,
            slideRangeSummary,
            PresentationPrintOutputPackageExecutor.PdfContentType,
            PresentationExportPlanner.PdfExportExtension,
            fingerprint,
            fingerprint,
            BuildArtifactPath(BaselineManifestPattern, sourceStem, PortableSlidePdfEvidenceId),
            BuildArtifactPath(WpfArtifactPattern, sourceStem, PortableSlidePdfEvidenceId),
            BuildArtifactPath(AvaloniaArtifactPattern, sourceStem, PortableSlidePdfEvidenceId),
            BuildArtifactPath(PowerPointPdfArtifactPattern, sourceStem, PortableSlidePdfEvidenceId),
            BuildArtifactPath(PowerPointPngArtifactPattern, sourceStem, PortableSlidePdfEvidenceId),
            BuildArtifactPath(WpfPngArtifactPattern, sourceStem, PortableSlidePdfEvidenceId),
            BuildArtifactPath(AvaloniaPngArtifactPattern, sourceStem, PortableSlidePdfEvidenceId),
            BuildArtifactPath(WpfAvaloniaDiffReportPattern, sourceStem, PortableSlidePdfEvidenceId),
            BuildArtifactPath(PowerPointWpfDiffReportPattern, sourceStem, PortableSlidePdfEvidenceId),
            BuildArtifactPath(PowerPointAvaloniaDiffReportPattern, sourceStem, PortableSlidePdfEvidenceId),
            BaselineRasterizationDpi,
            DiffThresholdProfile,
            PowerPointBaselinePattern,
            PowerPointBaselineDeferred,
            RequiresPowerPointComBaseline: true,
            string.Create(
                CultureInfo.InvariantCulture,
                $"pages={pageCount}; firstPage={firstPage.WidthPoints:0.###}x{firstPage.HeightPoints:0.###}pt; drawOps={firstPage.Ops.Count}"));
    }

    private static PresentationPdfVisualBaselineReadinessRow BuildPrintPackageRow(
        string evidenceId,
        string outputKind,
        PresentationPrintRequest request,
        Presentation presentation,
        string sourceName,
        string sourceStem)
    {
        var packagePlan = PresentationPrintOutputPackageExecutor.BuildPackagePlan(request, presentation);
        var fingerprint = BuildFingerprint(
            sourceName,
            sourceStem,
            evidenceId,
            packagePlan.Route.ToString(),
            packagePlan.ContentType,
            packagePlan.DefaultExtensionWithDot,
            packagePlan.PageCount,
            packagePlan.SlideRangeSummary);
        var status = packagePlan.CanBuildPackage && packagePlan.PageCount > 0
            ? "shared-package-plan-ready"
            : "no-slides";

        return new PresentationPdfVisualBaselineReadinessRow(
            evidenceId,
            outputKind,
            PrintPackagePlanner,
            packagePlan.Route.ToString(),
            status,
            packagePlan.PageCount,
            packagePlan.SlideRangeSummary,
            packagePlan.ContentType,
            packagePlan.DefaultExtensionWithDot,
            fingerprint,
            fingerprint,
            BuildArtifactPath(BaselineManifestPattern, sourceStem, evidenceId),
            BuildArtifactPath(WpfArtifactPattern, sourceStem, evidenceId),
            BuildArtifactPath(AvaloniaArtifactPattern, sourceStem, evidenceId),
            BuildArtifactPath(PowerPointPdfArtifactPattern, sourceStem, evidenceId),
            BuildArtifactPath(PowerPointPngArtifactPattern, sourceStem, evidenceId),
            BuildArtifactPath(WpfPngArtifactPattern, sourceStem, evidenceId),
            BuildArtifactPath(AvaloniaPngArtifactPattern, sourceStem, evidenceId),
            BuildArtifactPath(WpfAvaloniaDiffReportPattern, sourceStem, evidenceId),
            BuildArtifactPath(PowerPointWpfDiffReportPattern, sourceStem, evidenceId),
            BuildArtifactPath(PowerPointAvaloniaDiffReportPattern, sourceStem, evidenceId),
            BaselineRasterizationDpi,
            DiffThresholdProfile,
            PowerPointBaselinePattern,
            PowerPointBaselineDeferred,
            RequiresPowerPointComBaseline: true,
            $"layout={packagePlan.LayoutSummary}; preview={packagePlan.PreviewPlan.PageCountText}; options={packagePlan.Options.DisplaySummary}");
    }

    private static string BuildFingerprint(
        string sourceName,
        string sourceStem,
        string evidenceId,
        string route,
        string contentType,
        string extensionWithDot,
        int pageCount,
        string slideRangeSummary) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"source={sourceName};sourceStem={sourceStem};evidenceId={evidenceId};route={route};contentType={contentType};extension={extensionWithDot};pages={pageCount};range={slideRangeSummary};rasterDpi={BaselineRasterizationDpi};diffProfile={DiffThresholdProfile}");

    private static string BuildArtifactPath(
        string pattern,
        string sourceStem,
        string evidenceId) =>
        pattern
            .Replace("{sourceStem}", sourceStem, StringComparison.Ordinal)
            .Replace("{evidenceId}", evidenceId, StringComparison.Ordinal);

    private static string BuildArtifactStem(string sourceName)
    {
        var stem = Path.GetFileNameWithoutExtension(sourceName.Trim());
        if (string.IsNullOrWhiteSpace(stem))
            return "Presentation";

        var builder = new System.Text.StringBuilder(stem.Length);
        foreach (var ch in stem)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        }

        var normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "Presentation" : normalized;
    }

    private static string NormalizeSourceName(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return "Presentation.pptx";

        var fileName = Path.GetFileName(sourceName.Trim());
        return string.IsNullOrWhiteSpace(fileName) ? "Presentation.pptx" : fileName;
    }
}
