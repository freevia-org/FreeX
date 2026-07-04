namespace FreeP.App.Compositor.Tests;

public sealed class PresentationPrintBackstagePlannerTests
{
    [Fact]
    public void Build_CoversPowerPointLayoutChoicesAndSelectedHandoutSummary()
    {
        var plan = PresentationPrintBackstagePlanner.Build(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.SelectedSlides,
                    SelectedSlideNumbers: [4, 2, 2, 99]),
                HandoutSlidesPerPage: 3,
                PrintHiddenSlides: true,
                Copies: 3,
                Collate: false,
                ColorMode: PresentationPrintColorMode.PureBlackAndWhite,
                FrameSlides: true,
                IncludeCommentsAndInkMarkup: true),
            slideCount: 6,
            currentSlideNumber: 5,
            selectedSlideNumbers: [2, 4]);

        plan.Heading.Should().Be("Print");
        plan.LayoutChoices.Select(choice => choice.Layout.DisplayName).Should().Equal(
            "Full Page Slides",
            "Notes Pages",
            "Handouts (1 slide per page)",
            "Handouts (2 slides per page)",
            "Handouts (3 slides per page)",
            "Handouts (4 slides per page)",
            "Handouts (6 slides per page)",
            "Handouts (9 slides per page)");
        plan.SelectedLayout.Layout.Layout.Should().Be(PresentationPrintLayoutKind.Handouts);
        plan.SelectedLayout.Layout.SlidesPerPage.Should().Be(3);
        plan.LayoutChoices.Single(choice => choice.IsSelected).Should().BeSameAs(plan.SelectedLayout);
        plan.PrintHiddenSlides.Should().BeTrue();
        plan.Options.DisplaySummary.Should().Be(
            "3 copies, Uncollated, Pure Black and White, Print hidden slides, Frame slides, Print comments and ink markup");
        plan.Options.SummaryLines.Should().Equal(
            "3 copies",
            "Uncollated",
            "Pure Black and White",
            "Print hidden slides",
            "Frame slides",
            "Print comments and ink markup");
        plan.OutputOptionChoices.Where(choice => choice.IsSelected).Select(choice => choice.DisplayName)
            .Should()
            .Equal(
                "3 copies",
                "Uncollated",
                "Pure Black and White",
                "Print hidden slides",
                "Frame slides",
                "Print comments and ink markup");
        plan.OutputOptionChoices.Select(choice => choice.Group).Should().Contain([
            "Copies",
            "Collation",
            "Color",
            "Content",
            "Output",
        ]);
        plan.OutputOptionChoices.Single(choice => choice.OptionId == "collated")
            .Description.Should().Contain("complete copy sets");
        plan.OutputOptionChoices.Single(choice => choice.OptionId == "skip-hidden-slides")
            .IsSelected.Should().BeFalse();
        plan.PageCount.Should().Be(1);
        plan.LayoutSummary.Should().Be("Handouts (3 slides per page) - Slides 2, 4, 1 page including hidden slides");
        plan.SlideRangeSummary.Should().Be("Slides 2, 4");
        plan.PreviewPlan.PageCount.Should().Be(1);
        plan.PreviewPlan.PageCountText.Should().Be("1 printable page");
        plan.PreviewPlan.Pages.Should().ContainSingle()
            .Which.Should().Match<PresentationPrintPreviewPage>(page =>
                page.Kind == PresentationPrintPreviewPageKind.Handout &&
                page.PageNumber == 1 &&
                page.SlideNumbers.SequenceEqual(new[] { 2, 4 }) &&
                page.Detail == "Handout with slides 2, 4");
        plan.CanBuildPackage.Should().BeTrue();
        plan.NativePrinterDialogDeferred.Should().BeFalse();
        plan.NativePrintHandoff.Status.Should().Be(PresentationNativePrintHandoffStatus.PackageReadyHostHandoffRequired);
        plan.NativePrintHandoff.StatusText.Should().Be("Ready for host handoff");
        plan.NativePrintHandoff.SuggestedTempFileName.Should().Be("Presentation-print.pdf");
        plan.NativePrintHandoff.OptionsSummary.Should().Be(plan.Options.DisplaySummary);
        plan.NativePrinterDialogDeferredMessage.Should().Be(
            PresentationPrintOutputPackageExecutor.NativePrintPackageReadyReason);
        plan.DisabledReason.Should().BeNull();
        plan.PackagePlan.Route.Should().Be(PresentationPrintOutputPackageRoute.HandoutPdf);
        plan.PackagePlan.Options.Should().BeSameAs(plan.Options);
    }

    [Fact]
    public void Build_ExposesRangeChoicesWithCustomDescriptorOnly()
    {
        var plan = PresentationPrintBackstagePlanner.Build(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.FullPageSlides,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CurrentSlide,
                    CurrentSlideNumber: 3)),
            slideCount: 5,
            currentSlideNumber: 3,
            selectedSlideNumbers: [5, 1, 1]);

        plan.RangeChoices.Select(choice => choice.Kind).Should().Equal(
            PresentationSlideRangeKind.AllSlides,
            PresentationSlideRangeKind.CurrentSlide,
            PresentationSlideRangeKind.SelectedSlides,
            PresentationSlideRangeKind.CustomRange);
        plan.SelectedRange.Kind.Should().Be(PresentationSlideRangeKind.CurrentSlide);
        plan.RangeChoices.Single(choice => choice.Kind == PresentationSlideRangeKind.AllSlides)
            .DisplayName.Should().Be("All Slides");
        plan.RangeChoices.Single(choice => choice.Kind == PresentationSlideRangeKind.CurrentSlide)
            .DisplayName.Should().Be("Current Slide (Slide 3)");
        plan.RangeChoices.Single(choice => choice.Kind == PresentationSlideRangeKind.SelectedSlides)
            .DisplayName.Should().Be("Selected Slides (Slides 1, 5)");
        var custom = plan.RangeChoices.Single(choice => choice.Kind == PresentationSlideRangeKind.CustomRange);
        custom.IsAvailable.Should().BeTrue();
        custom.Request.Should().BeNull("the Backstage pane records the descriptor but does not own a parser-heavy input UI");
    }

    [Fact]
    public void Build_WithDeferredHostCapabilitiesKeepsPackageReadyButDefersNativeDialog()
    {
        var plan = PresentationPrintBackstagePlanner.Build(
            request: null,
            slideCount: 2,
            hostCapabilities: PresentationNativePrintHandoffHostCapabilities.Deferred(
                "Unit test host",
                "No native print dialog in unit tests."),
            suggestedBaseFileName: "Deck.pptx");

        plan.CanBuildPackage.Should().BeTrue();
        plan.NativePrinterDialogDeferred.Should().BeTrue();
        plan.NativePrintHandoff.Status.Should().Be(PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost);
        plan.NativePrintHandoff.IsPackageReady.Should().BeTrue();
        plan.NativePrintHandoff.RequiresHostHandoff.Should().BeTrue();
        plan.NativePrintHandoff.CanOpenNativePrintDialog.Should().BeFalse();
        plan.NativePrintHandoff.SuggestedTempFileName.Should().Be("Deck-print.pdf");
        plan.NativePrintHandoff.Reason.Should().Contain("No native print dialog in unit tests.");
        plan.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void Build_OmitsSelectedSlidesWhenHostDoesNotProvideThemAndDisablesEmptyDeckPackage()
    {
        var plan = PresentationPrintBackstagePlanner.Build(
            request: null,
            slideCount: 0,
            currentSlideNumber: null,
            selectedSlideNumbers: null);

        plan.RangeChoices.Select(choice => choice.Kind).Should().Equal(
            PresentationSlideRangeKind.AllSlides,
            PresentationSlideRangeKind.CurrentSlide,
            PresentationSlideRangeKind.CustomRange);
        plan.PageCount.Should().Be(0);
        plan.SlideRangeSummary.Should().Be("No slides");
        plan.PreviewPlan.CanPreview.Should().BeFalse();
        plan.PreviewPlan.PageCountText.Should().Be("No printable pages");
        plan.PreviewPlan.Pages.Should().BeEmpty();
        plan.CanBuildPackage.Should().BeFalse();
        plan.DisabledReason.Should().Be("Print output requires at least one slide.");
        plan.NativePrintHandoff.Status.Should().Be(PresentationNativePrintHandoffStatus.NoSlides);
        plan.NativePrintHandoff.DisabledReason.Should().Be("Print output requires at least one slide.");
        plan.LayoutChoices.Should().OnlyContain(choice => !choice.PackagePlan.CanBuildPackage);
    }
}
