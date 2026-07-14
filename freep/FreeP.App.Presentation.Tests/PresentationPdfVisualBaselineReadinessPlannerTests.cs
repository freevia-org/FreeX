using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationPdfVisualBaselineReadinessPlannerTests
{
    [Fact]
    public void Build_ReportsMatchingWpfAvaloniaContractsForPdfBaselineRows()
    {
        var presentation = BuildDeck(5);

        var plan = PresentationPdfVisualBaselineReadinessPlanner.Build(
            presentation,
            @"C:\Decks\Quarter Review.pptx");

        plan.SourceName.Should().Be("Quarter Review.pptx");
        plan.SlideCount.Should().Be(5);
        plan.RequiresPowerPointComForLocalEvidence.Should().BeFalse();
        plan.RequiresPowerPointComForAuthoritativeBaseline.Should().BeTrue();
        plan.PowerPointBaselineStatus.Should().Contain("deferred");
        plan.HasMatchingWpfAvaloniaContracts.Should().BeTrue();
        plan.Rows.Select(row => row.EvidenceId).Should().Equal(
            PresentationPdfVisualBaselineReadinessPlanner.PortableSlidePdfEvidenceId,
            PresentationPdfVisualBaselineReadinessPlanner.FullPageRasterPdfEvidenceId,
            PresentationPdfVisualBaselineReadinessPlanner.HandoutPdfEvidenceId,
            PresentationPdfVisualBaselineReadinessPlanner.NotesPagePdfEvidenceId);
        plan.Rows.Should().OnlyContain(row =>
            row.PowerPointBaseline == PresentationPdfVisualBaselineReadinessPlanner.PowerPointBaselineDeferred &&
            row.RequiresPowerPointComBaseline &&
            row.ContentType == PresentationPrintOutputPackageExecutor.PdfContentType &&
            row.DefaultExtensionWithDot == PresentationExportPlanner.PdfExportExtension &&
            row.WpfManifestFingerprint == row.AvaloniaManifestFingerprint &&
            row.BaselineManifestPath.StartsWith("manifest/Quarter-Review/", StringComparison.Ordinal) &&
            row.WpfArtifactPath.StartsWith("wpf-pdf/Quarter-Review/", StringComparison.Ordinal) &&
            row.AvaloniaArtifactPath.StartsWith("avalonia-pdf/Quarter-Review/", StringComparison.Ordinal) &&
            row.PowerPointPdfArtifact.StartsWith("powerpoint-pdf/Quarter-Review/", StringComparison.Ordinal) &&
            row.PowerPointPngArtifactPattern.StartsWith("powerpoint-png/Quarter-Review/", StringComparison.Ordinal) &&
            row.WpfPngArtifactPattern.StartsWith("wpf-png/Quarter-Review/", StringComparison.Ordinal) &&
            row.AvaloniaPngArtifactPattern.StartsWith("avalonia-png/Quarter-Review/", StringComparison.Ordinal) &&
            row.WpfAvaloniaDiffReportPath.StartsWith("diff/wpf-vs-avalonia/Quarter-Review/", StringComparison.Ordinal) &&
            row.PowerPointWpfDiffReportPath.StartsWith("diff/powerpoint-vs-wpf/Quarter-Review/", StringComparison.Ordinal) &&
            row.PowerPointAvaloniaDiffReportPath.StartsWith("diff/powerpoint-vs-avalonia/Quarter-Review/", StringComparison.Ordinal) &&
            row.RasterizationDpi == PresentationPdfVisualBaselineReadinessPlanner.BaselineRasterizationDpi &&
            row.DiffThresholdProfile == PresentationPdfVisualBaselineReadinessPlanner.DiffThresholdProfile);

        var portable = plan.Rows.Single(row =>
            row.EvidenceId == PresentationPdfVisualBaselineReadinessPlanner.PortableSlidePdfEvidenceId);
        portable.SharedPlanner.Should().Be("PresentationPdfExporter.BuildDocument");
        portable.SharedRoute.Should().Be("PortableSlidePdf");
        portable.Status.Should().Be("shared-pdf-plan-ready");
        portable.PageCount.Should().Be(5);
        portable.SlideRangeSummary.Should().Be("All slides");
        portable.WpfManifestFingerprint.Should().Contain("route=PortableSlidePdf");
        portable.WpfManifestFingerprint.Should().Contain("source=Quarter Review.pptx");
        portable.WpfManifestFingerprint.Should().Contain("sourceStem=Quarter-Review");
        portable.WpfManifestFingerprint.Should().Contain("rasterDpi=144");
        portable.WpfManifestFingerprint.Should().Contain("diffProfile=pdf-visual-baseline-readiness-v1/manual-calibration-required");
        portable.BaselineManifestPath.Should().Be(
            "manifest/Quarter-Review/freep.pdf.baseline.portable-slide-pdf.json");
        portable.WpfArtifactPath.Should().Be(
            "wpf-pdf/Quarter-Review/freep.pdf.baseline.portable-slide-pdf.pdf");
        portable.AvaloniaArtifactPath.Should().Be(
            "avalonia-pdf/Quarter-Review/freep.pdf.baseline.portable-slide-pdf.pdf");
        portable.PowerPointPdfArtifact.Should().Be(
            "powerpoint-pdf/Quarter-Review/freep.pdf.baseline.portable-slide-pdf.pdf");
        portable.PowerPointPngArtifactPattern.Should().Be(
            "powerpoint-png/Quarter-Review/freep.pdf.baseline.portable-slide-pdf/slide-NN.png");
        portable.WpfPngArtifactPattern.Should().Be(
            "wpf-png/Quarter-Review/freep.pdf.baseline.portable-slide-pdf/page-NN.png");
        portable.AvaloniaPngArtifactPattern.Should().Be(
            "avalonia-png/Quarter-Review/freep.pdf.baseline.portable-slide-pdf/page-NN.png");
        portable.WpfAvaloniaDiffReportPath.Should().Be(
            "diff/wpf-vs-avalonia/Quarter-Review/freep.pdf.baseline.portable-slide-pdf.json");
        portable.PowerPointWpfDiffReportPath.Should().Be(
            "diff/powerpoint-vs-wpf/Quarter-Review/freep.pdf.baseline.portable-slide-pdf.json");
        portable.PowerPointAvaloniaDiffReportPath.Should().Be(
            "diff/powerpoint-vs-avalonia/Quarter-Review/freep.pdf.baseline.portable-slide-pdf.json");

        var fullPage = plan.Rows.Single(row =>
            row.EvidenceId == PresentationPdfVisualBaselineReadinessPlanner.FullPageRasterPdfEvidenceId);
        fullPage.SharedRoute.Should().Be(nameof(PresentationPrintOutputPackageRoute.FullPageSlidesRasterPdf));
        fullPage.Status.Should().Be("shared-package-plan-ready");
        fullPage.PageCount.Should().Be(5);
        fullPage.WpfManifestFingerprint.Should().Contain("range=All slides");
        fullPage.BaselineManifestPath.Should().Be(
            "manifest/Quarter-Review/freep.pdf.baseline.full-page-raster-pdf.json");

        var handout = plan.Rows.Single(row =>
            row.EvidenceId == PresentationPdfVisualBaselineReadinessPlanner.HandoutPdfEvidenceId);
        handout.SharedRoute.Should().Be(nameof(PresentationPrintOutputPackageRoute.HandoutPdf));
        handout.PageCount.Should().Be(2);
        handout.Detail.Should().Contain("3 slides");

        var notes = plan.Rows.Single(row =>
            row.EvidenceId == PresentationPdfVisualBaselineReadinessPlanner.NotesPagePdfEvidenceId);
        notes.SharedRoute.Should().Be(nameof(PresentationPrintOutputPackageRoute.NotesPagePdf));
        notes.PageCount.Should().Be(5);
        notes.Detail.Should().Contain("Notes Pages");
    }

    [Fact]
    public void Build_EmptyDeckKeepsPortablePlaceholderButDefersPackageRows()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var plan = PresentationPdfVisualBaselineReadinessPlanner.Build(
            presentation,
            "  Q3 board deck!.pptx  ");

        plan.SourceName.Should().Be("Q3 board deck!.pptx");
        plan.SlideCount.Should().Be(0);
        plan.HasMatchingWpfAvaloniaContracts.Should().BeTrue();

        var portable = plan.Rows.Single(row =>
            row.EvidenceId == PresentationPdfVisualBaselineReadinessPlanner.PortableSlidePdfEvidenceId);
        portable.Status.Should().Be("shared-portable-placeholder-ready");
        portable.PageCount.Should().Be(1);
        portable.SlideRangeSummary.Should().Be("No slides (portable placeholder page)");
        portable.BaselineManifestPath.Should().Be(
            "manifest/Q3-board-deck/freep.pdf.baseline.portable-slide-pdf.json");

        plan.Rows
            .Where(row => row.EvidenceId != PresentationPdfVisualBaselineReadinessPlanner.PortableSlidePdfEvidenceId)
            .Should()
            .OnlyContain(row =>
                row.Status == "no-slides" &&
                row.PageCount == 0 &&
                row.SlideRangeSummary == "No slides");
    }

    private static Presentation BuildDeck(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        for (var i = 1; i <= slideCount; i++)
        {
            var slide = new Slide { Title = $"Slide {i}" };
            slide.Shapes.Add(new SlideShape
            {
                Kind = SlideShapeKind.AutoShape,
                Text = $"Body {i}",
            });
            slide.Notes = MakeTextBody($"Speaker note {i}.");
            presentation.Slides.Add(slide);
        }

        presentation.Properties.Title = "PDF Baseline Deck";
        presentation.Properties.Author = "Parity";
        return presentation;
    }

    private static TextBody MakeTextBody(params string[] paragraphs)
    {
        var body = new TextBody();
        foreach (var text in paragraphs)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = text });
            body.Paragraphs.Add(paragraph);
        }

        return body;
    }
}
