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
                PrintHiddenSlides: true),
            slideCount: 6,
            hostCapabilities: PresentationNativePrintHandoffHostCapabilities.Available("test host"));

        var surface = PresentationBackstagePrintSurfacePlanner.Build(plan);

        surface.Heading.Should().Be("Print");
        surface.Settings.Should().Contain(new Free.Shared.Shell.BackstageFieldRow("Layout", "Handouts (3 slides per page)"));
        surface.Settings.Should().Contain(new Free.Shared.Shell.BackstageFieldRow("Slides", "Slides 2, 4, 5"));
        surface.Settings.Should().Contain(new Free.Shared.Shell.BackstageFieldRow("Hidden slides", "Included"));
        surface.ChoiceGroups.Select(group => group.Heading).Should().Equal(
            "Output Options", "Preview", "Layouts", "Slide Range");
        surface.ChoiceGroups.Single(group => group.Heading == "Layouts")
            .Choices.Should().ContainSingle(choice => choice.IsSelected);
        surface.CustomRangeText.Should().Be("2,4-5");
        surface.CustomRangeInputAutomationId.Should().Be("FreePPrintCustomRangeInput");
        surface.PrintActions.Should().HaveCount(plan.LayoutChoices.Count);
        surface.PrintActions.Should().OnlyContain(action => action.IsEnabled);
        surface.PrintActions.Select(action => action.AutomationId)
            .Should().OnlyContain(id => id.StartsWith("BackstagePrint_"));
        surface.PrintActions.Single(action => action.Request.Layout == PresentationPrintLayoutKind.Handouts &&
            action.Request.HandoutSlidesPerPage == 3).Request.SlideRange.Should().Be(plan.SelectedRange.Request);
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
