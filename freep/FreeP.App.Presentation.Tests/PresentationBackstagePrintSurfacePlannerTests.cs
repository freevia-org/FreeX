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
        surface.CustomRangeInputAutomationId.Should().Be("FreePPrintCustomRangeInput");
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
