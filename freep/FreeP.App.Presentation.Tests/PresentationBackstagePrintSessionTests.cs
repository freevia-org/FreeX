namespace FreeP.App.Compositor.Tests;

public sealed class PresentationBackstagePrintSessionTests
{
    [Fact]
    public void SetRequest_NormalizesOptionsAndPreservesThemAcrossLayoutActions()
    {
        var session = CreateSession(slideCount: 6, out var executedRequests);

        var state = session.SetRequest(new PresentationPrintRequest(
            PresentationPrintLayoutKind.Handouts,
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CustomRange,
                CustomRangeText: "2,4-5"),
            HandoutSlidesPerPage: 5,
            PrintHiddenSlides: true,
            Copies: 5_000,
            Collate: false,
            ColorMode: (PresentationPrintColorMode)999,
            FrameSlides: true,
            IncludeCommentsAndInkMarkup: true));

        state.Request.HandoutSlidesPerPage.Should().Be(4);
        state.Request.Copies.Should().Be(999);
        state.Request.ColorMode.Should().Be(PresentationPrintColorMode.Color);
        state.Request.PrintHiddenSlides.Should().BeTrue();
        state.Request.Collate.Should().BeFalse();
        state.Request.FrameSlides.Should().BeTrue();
        state.Request.IncludeCommentsAndInkMarkup.Should().BeTrue();

        var notesAction = state.Surface.PrintActions.Single(action =>
            action.Request.Layout == PresentationPrintLayoutKind.NotesPages);
        notesAction.Request.HandoutSlidesPerPage.Should().BeNull();
        notesAction.Request.SlideRange.Should().Be(state.Plan.SelectedRange.Request);
        notesAction.Request.Copies.Should().Be(999);
        notesAction.Request.ColorMode.Should().Be(PresentationPrintColorMode.Color);
        notesAction.Request.PrintHiddenSlides.Should().BeTrue();
        notesAction.Request.Collate.Should().BeFalse();
        notesAction.Request.FrameSlides.Should().BeTrue();
        notesAction.Request.IncludeCommentsAndInkMarkup.Should().BeTrue();

        session.TryExecutePrint(notesAction.AutomationId).Should().BeTrue();
        executedRequests.Should().ContainSingle().Which.Should().Be(notesAction.Request);
    }

    [Fact]
    public void ApplyCustomRange_TrimsInputAndRetainsCurrentLayoutAndOptions()
    {
        var session = CreateSession(slideCount: 6, out _);
        session.SetRequest(new PresentationPrintRequest(
            PresentationPrintLayoutKind.Handouts,
            HandoutSlidesPerPage: 3,
            PrintHiddenSlides: true,
            Copies: 4,
            Collate: false,
            ColorMode: PresentationPrintColorMode.Grayscale,
            FrameSlides: true));

        session.ApplyCustomRange(" 2, 4-5 ");
        var state = session.Refresh();

        state.Request.Layout.Should().Be(PresentationPrintLayoutKind.Handouts);
        state.Request.HandoutSlidesPerPage.Should().Be(3);
        state.Request.SlideRange!.Kind.Should().Be(PresentationSlideRangeKind.CustomRange);
        state.Request.SlideRange.CustomRangeText.Should().Be("2, 4-5");
        state.Request.Copies.Should().Be(4);
        state.Request.Collate.Should().BeFalse();
        state.Request.ColorMode.Should().Be(PresentationPrintColorMode.Grayscale);
        state.Request.PrintHiddenSlides.Should().BeTrue();
        state.Request.FrameSlides.Should().BeTrue();
        state.Plan.SlideRangeSummary.Should().Be("Slides 2, 4, 5");
    }

    [Fact]
    public void PreviewNavigation_ClampsAndProjectsSelectedPage()
    {
        var session = CreateSession(slideCount: 3, out _);

        var first = session.Refresh();
        var last = session.GoToPreviewPage(99);
        var previous = session.GoToPreviousPreviewPage();
        var firstAgain = session.GoToPreviewPage(-10);

        first.Preview.SelectedPageIndex.Should().Be(0);
        first.Preview.CanGoToPreviousPage.Should().BeFalse();
        first.Preview.CanGoToNextPage.Should().BeTrue();
        last.Preview.SelectedPageIndex.Should().Be(2);
        last.Preview.SelectedPage!.SlideNumbers.Should().Equal(3);
        last.Surface.ChoiceGroups.Single(group =>
                group.Kind == PresentationBackstagePrintChoiceGroupKind.Preview)
            .Choices.Single(choice => choice.IsSelected).Label.Should().Be("Slide 3");
        previous.Preview.SelectedPageIndex.Should().Be(1);
        firstAgain.Preview.SelectedPageIndex.Should().Be(0);
    }

    [Fact]
    public void InvalidCustomRange_BlocksCommandDispatchWithPortableValidation()
    {
        var session = CreateSession(slideCount: 3, out var executedRequests);

        var state = session.ApplyCustomRange("99");

        state.Validation.CanBuildPackage.Should().BeFalse();
        state.Validation.CanPrint.Should().BeFalse();
        state.Validation.FailureReason.Should().NotBeNullOrWhiteSpace();
        state.Surface.PrintActions.Should().OnlyContain(action => !action.IsEnabled);
        session.TryExecutePrint(state.Surface.PrintActions[0].AutomationId).Should().BeFalse();
        executedRequests.Should().BeEmpty();
    }

    [Fact]
    public void NoSlides_HasNoPreviewSelectionOrNavigation()
    {
        var session = CreateSession(slideCount: 0, out _);

        var state = session.GoToNextPreviewPage();

        state.Preview.PageCount.Should().Be(0);
        state.Preview.SelectedPageIndex.Should().BeNull();
        state.Preview.SelectedPage.Should().BeNull();
        state.Preview.CanGoToPreviousPage.Should().BeFalse();
        state.Preview.CanGoToNextPage.Should().BeFalse();
        state.Validation.CanPrint.Should().BeFalse();
    }

    [Fact]
    public void UnknownOrUnavailableCommand_DoesNotReachHost()
    {
        var executedRequests = new List<PresentationPrintRequest>();
        var session = new PresentationBackstagePrintSession(
            request => PresentationPrintBackstagePlanner.Build(
                request,
                slideCount: 2,
                hostCapabilities: PresentationNativePrintHandoffHostCapabilities.Deferred(
                    "test host",
                    "No printer is available.")),
            executedRequests.Add);

        var state = session.Refresh();

        session.CanExecutePrint("missing").Should().BeFalse();
        session.TryExecutePrint("missing").Should().BeFalse();
        session.TryExecutePrint(state.Surface.PrintActions[0].AutomationId).Should().BeFalse();
        executedRequests.Should().BeEmpty();
    }

    private static PresentationBackstagePrintSession CreateSession(
        int slideCount,
        out List<PresentationPrintRequest> executedRequests)
    {
        executedRequests = [];
        var requests = executedRequests;
        return new PresentationBackstagePrintSession(
            request => PresentationPrintBackstagePlanner.Build(
                request,
                slideCount,
                hostCapabilities: PresentationNativePrintHandoffHostCapabilities.Available("test host")),
            requests.Add);
    }
}
