using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationExportBackstageEvidencePlannerTests
{
    [Fact]
    public void Build_ReportsSharedWpfAvaloniaEvidenceWithoutPowerPointCom()
    {
        var presentation = BuildDeck(4);

        var plan = PresentationExportBackstageEvidencePlanner.Build(presentation, "Quarter Review.pptx");

        plan.SourceName.Should().Be("Quarter Review.pptx");
        plan.SlideCount.Should().Be(4);
        plan.RequiresPowerPointComForLocalEvidence.Should().BeFalse();
        plan.Rows.Select(row => row.EvidenceId).Should().Equal(
            "freep.export.backstage.fixed-layout-pdf",
            "freep.export.backstage.image-sequence",
            "freep.export.backstage.print-full-page",
            "freep.export.backstage.print-handouts-3",
            "freep.export.backstage.video-frame-package");
        plan.Rows.Should().OnlyContain(row =>
            row.SharedPlanner == PresentationExportBackstageEvidencePlanner.SharedPlannerEvidence &&
            row.PowerPointBaseline == PresentationExportBackstageEvidencePlanner.PowerPointBaselineDeferred &&
            row.RequiresPowerPointComBaseline);

        var fullPage = plan.Rows.Single(row => row.EvidenceId == "freep.export.backstage.print-full-page");
        fullPage.Status.Should().Be("shared-package-ready-host-deferred");
        fullPage.WpfEvidence.Should().Be("WPF:FullPageSlidesRasterPdf:4:HostPrinterUnavailableDeferredByHost");
        fullPage.AvaloniaEvidence.Should().Be("Avalonia:FullPageSlidesRasterPdf:4:HostPrinterUnavailableDeferredByHost");
        fullPage.Detail.Should().Contain("nativePrint=HostPrinterUnavailableDeferredByHost");

        var handout = plan.Rows.Single(row => row.EvidenceId == "freep.export.backstage.print-handouts-3");
        handout.Status.Should().Be("shared-package-ready-host-deferred");
        handout.WpfEvidence.Should().Be("WPF:HandoutPdf:2:HostPrinterUnavailableDeferredByHost");
        handout.AvaloniaEvidence.Should().Be("Avalonia:HandoutPdf:2:HostPrinterUnavailableDeferredByHost");

        var video = plan.Rows.Single(row => row.EvidenceId == "freep.export.backstage.video-frame-package");
        video.Status.Should().Be("shared-frame-package-ready-host-deferred");
        video.WpfEvidence.Should().Contain("WPF video export host:4:EncoderInputPackageReadyHostDeferred");
        video.AvaloniaEvidence.Should().Contain("Avalonia video export host:4:EncoderInputPackageReadyHostDeferred");
    }

    [Fact]
    public void Build_EmptyDeckKeepsNoComBaselineDeferredAndClassifiesNoSlides()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var plan = PresentationExportBackstageEvidencePlanner.Build(presentation);

        plan.RequiresPowerPointComForLocalEvidence.Should().BeFalse();
        plan.Rows.Should().OnlyContain(row =>
            row.PowerPointBaseline == PresentationExportBackstageEvidencePlanner.PowerPointBaselineDeferred);
        plan.Rows.Single(row => row.EvidenceId == "freep.export.backstage.print-full-page")
            .Status.Should().Be("no-slides");
        plan.Rows.Single(row => row.EvidenceId == "freep.export.backstage.print-handouts-3")
            .Status.Should().Be("no-slides");
        plan.Rows.Single(row => row.EvidenceId == "freep.export.backstage.video-frame-package")
            .Status.Should().Be("no-slides");
        plan.Rows.Single(row => row.EvidenceId == "freep.export.backstage.fixed-layout-pdf")
            .Status.Should().Be("no-slides");
        plan.Rows.Single(row => row.EvidenceId == "freep.export.backstage.image-sequence")
            .Status.Should().Be("shared-export-plan");
    }

    private static Presentation BuildDeck(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        for (var i = 1; i <= slideCount; i++)
        {
            presentation.Slides.Add(new Slide
            {
                Title = $"Slide {i}",
                Transition = new SlideTransition { AdvanceAfterMs = i * 1000 },
            });
        }

        return presentation;
    }
}
