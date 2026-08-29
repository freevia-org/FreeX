namespace FreeP.App.Compositor.Tests;

public sealed class PresentationBackstagePrintSurfacePlannerTests
{
    [Fact]
    public void Build_OwnsSettingsChoicesStatusAndPrintActions()
    {
        var plan = PresentationPrintBackstagePlanner.Build(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    CustomRangeText: "2,4-5"),
                HandoutSlidesPerPage: 3,
                PrintHiddenSlides: true,
                Copies: 4,
                Collate: false,
                ColorMode: PresentationPrintColorMode.Grayscale,
                FrameSlides: true,
                IncludeCommentsAndInkMarkup: true),
            slideCount: 6,
            hostCapabilities: PresentationNativePrintHandoffHostCapabilities.Available("test host"));

        var surface = PresentationBackstagePrintSurfacePlanner.Build(plan);

        surface.Heading.Should().Be("Print");
        surface.Settings.Should().Contain(new Free.Shared.Shell.BackstageFieldRow("Layout", "Handouts (3 slides per page)"));
        surface.Settings.Should().Contain(new Free.Shared.Shell.BackstageFieldRow("Slides", "Slides 2, 4, 5"));
        surface.Settings.Should().Contain(new Free.Shared.Shell.BackstageFieldRow("Hidden slides", "Included"));
        surface.ChoiceGroups.Select(group => group.StableId).Should().Equal(
            "output-options", "preview", "layouts", "slide-range");
        surface.ChoiceGroups.Select(group => group.Kind).Should().Equal(
            PresentationBackstagePrintChoiceGroupKind.OutputOptions,
            PresentationBackstagePrintChoiceGroupKind.Preview,
            PresentationBackstagePrintChoiceGroupKind.Layouts,
            PresentationBackstagePrintChoiceGroupKind.SlideRange);
        surface.ChoiceGroups.Select(group => group.Heading).Should().Equal(
            "Output options", "Preview", "Layouts", "Slide range");
        surface.ChoiceGroups.Single(group =>
                group.Kind == PresentationBackstagePrintChoiceGroupKind.Layouts)
            .Choices.Should().ContainSingle(choice => choice.IsSelected);
        surface.CustomRangeHeading.Should().Be("Custom range");
        surface.CustomRangeText.Should().Be("2,4-5");
        PresentationShellTextCatalog.Resolve(surface.CustomRangeApplyHelpText)
            .Should().Be("Apply the custom slide range to the print preview and output.");
        surface.CustomRangeInputAutomationId.Should().Be("FreePPrintCustomRangeInput");
        surface.NativePrint.Should().Be(plan.NativePrintHandoff.Surface);
        PresentationShellTextCatalog.Resolve(surface.NativePrint.NativeDialogLabel)
            .Should().Be("Windows printer dialog");
        surface.PrintActions.Should().HaveCount(plan.LayoutChoices.Count);
        surface.PrintActions.Should().OnlyContain(action => action.IsEnabled);
        surface.PrintActions.Select(action => action.AutomationId)
            .Should().OnlyContain(id => id.StartsWith("BackstagePrint_"));
        var handoutRequest = surface.PrintActions.Single(action =>
            action.Request.Layout == PresentationPrintLayoutKind.Handouts &&
            action.Request.HandoutSlidesPerPage == 3).Request;
        handoutRequest.SlideRange.Should().Be(plan.SelectedRange.Request);
        handoutRequest.PrintHiddenSlides.Should().BeTrue();
        handoutRequest.Copies.Should().Be(4);
        handoutRequest.Collate.Should().BeFalse();
        handoutRequest.ColorMode.Should().Be(PresentationPrintColorMode.Grayscale);
        handoutRequest.FrameSlides.Should().BeTrue();
        handoutRequest.IncludeCommentsAndInkMarkup.Should().BeTrue();
    }

    [Fact]
    public void PrintActions_WarnWhenLayoutDiffersFromCurrentlyPreviewedLayout()
    {
        var plan = PresentationPrintBackstagePlanner.Build(
            new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides),
            slideCount: 6,
            hostCapabilities: PresentationNativePrintHandoffHostCapabilities.Available("test host"));

        var surface = PresentationBackstagePrintSurfacePlanner.Build(plan);

        // Full Page Slides is the selected layout, so it is what plan.PreviewPlan.Pages (and
        // therefore the on-screen Preview group) is currently showing. Its own Print action
        // matches that preview and must NOT carry a mismatch warning -- this is the sibling
        // no-regression case for the fix below.
        var selectedAction = surface.PrintActions.Single(action =>
            action.Request.Layout == PresentationPrintLayoutKind.FullPageSlides);
        selectedAction.HelpText.Should().NotContain("differs from the layout currently shown in Preview");

        // Handouts (6 per page) is a DIFFERENT layout choice. Clicking its Print button submits
        // that layout immediately (BuildRequest uses the choice, not the selected/previewed
        // layout), even though the Preview group is still rendering Full Page Slides thumbnails.
        // The action must say so explicitly, since there is no other production signal that the
        // two disagree before the click.
        var handoutAction = surface.PrintActions.Single(action =>
            action.Request.Layout == PresentationPrintLayoutKind.Handouts &&
            action.Request.HandoutSlidesPerPage == 6);
        handoutAction.HelpText.Should().Contain(
            "differs from the layout currently shown in Preview above (Full Page Slides)");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData(" 2, 4-6 ", "2, 4-6")]
    public void BuildCustomRangeRequest_NormalizesRendererInput(string? input, string? expected)
    {
        var request = PresentationBackstagePrintSurfacePlanner.BuildCustomRangeRequest(input);

        if (expected is null)
        {
            request.Should().BeNull();
            return;
        }

        request.Should().NotBeNull();
        request!.Layout.Should().Be(PresentationPrintLayoutKind.FullPageSlides);
        request.SlideRange.Should().NotBeNull();
        request.SlideRange!.Kind.Should().Be(PresentationSlideRangeKind.CustomRange);
        request.SlideRange.CustomRangeText.Should().Be(expected);
    }
}
