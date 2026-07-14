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
    public const string PowerPointBaselineStatus =
        "Authoritative PowerPoint PDF/PNG baseline capture is deferred until PowerPoint.Application COM is available.";
    private const string PortableSlidePdfRoute = "PortableSlidePdf";
    private const string PortableSlidePdfPlanner = "PresentationPdfExporter.BuildDocument";
    private const string PrintPackagePlanner = "PresentationPrintOutputPackageExecutor.BuildPackagePlan";
    private const string PowerPointBaselinePattern = "powerpoint-pdf/{sourceName}.pdf + powerpoint-png/slide-NN.png";

    public static PresentationPdfVisualBaselineReadinessPlan Build(
        Presentation presentation,
        string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var normalizedSourceName = NormalizeSourceName(sourceName);
        var rows = new[]
        {
            BuildPortableSlidePdfRow(presentation),
            BuildPrintPackageRow(
                FullPageRasterPdfEvidenceId,
                "Full-page slide raster PDF",
                new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides),
                presentation),
            BuildPrintPackageRow(
                HandoutPdfEvidenceId,
                "3-up handout PDF",
                new PresentationPrintRequest(
                    PresentationPrintLayoutKind.Handouts,
                    HandoutSlidesPerPage: 3),
                presentation),
            BuildPrintPackageRow(
                NotesPagePdfEvidenceId,
                "Notes-page PDF",
                new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages),
                presentation),
        };

        return new PresentationPdfVisualBaselineReadinessPlan(
            normalizedSourceName,
            presentation.Slides.Count,
            RequiresPowerPointComForLocalEvidence: false,
            RequiresPowerPointComForAuthoritativeBaseline: true,
            PowerPointBaselineStatus,
            rows);
    }

    private static PresentationPdfVisualBaselineReadinessRow BuildPortableSlidePdfRow(Presentation presentation)
    {
        var document = PresentationPdfExporter.BuildDocument(presentation);
        var pageCount = document.Pages.Count;
        var slideRangeSummary = presentation.Slides.Count == 0
            ? "No slides (portable placeholder page)"
            : PresentationExportPlanner.BuildSlideRangePlan(null, presentation.Slides.Count).DisplayName;
        var firstPage = document.Pages[0];
        var fingerprint = BuildFingerprint(
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
        Presentation presentation)
    {
        var packagePlan = PresentationPrintOutputPackageExecutor.BuildPackagePlan(request, presentation);
        var fingerprint = BuildFingerprint(
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
            PowerPointBaselinePattern,
            PowerPointBaselineDeferred,
            RequiresPowerPointComBaseline: true,
            $"layout={packagePlan.LayoutSummary}; preview={packagePlan.PreviewPlan.PageCountText}; options={packagePlan.Options.DisplaySummary}");
    }

    private static string BuildFingerprint(
        string route,
        string contentType,
        string extensionWithDot,
        int pageCount,
        string slideRangeSummary) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"route={route};contentType={contentType};extension={extensionWithDot};pages={pageCount};range={slideRangeSummary}");

    private static string NormalizeSourceName(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return "Presentation.pptx";

        var fileName = Path.GetFileName(sourceName.Trim());
        return string.IsNullOrWhiteSpace(fileName) ? "Presentation.pptx" : fileName;
    }
}
