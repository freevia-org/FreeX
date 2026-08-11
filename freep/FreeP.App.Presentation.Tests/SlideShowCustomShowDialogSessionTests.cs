using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowCustomShowDialogSessionTests
{
    [Fact]
    public void DomainSession_CreatesDialogSessionWithoutRendererForwarders()
    {
        var presentation = MakePresentation();
        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        var customShows = new SlideShowCustomShowSession(() => editor);
        var session = customShows.CreateDialogSession(_ => false);

        var created = session.Create(
            "Review",
            new[] { presentation.Slides[2].Id, presentation.Slides[0].Id });

        created.MutationResult!.Succeeded.Should().BeTrue();
        created.Plan.SelectedShow!.Name.Should().Be("Review");
        created.Plan.SelectedSlideIds.Should().Equal("appendix", "intro");
        presentation.CustomShows.Should().ContainSingle();
    }

    [Fact]
    public void Session_OwnsSelectionAndButtonAvailability()
    {
        var presentation = MakePresentation();
        AddShow(
            presentation,
            "Review",
            presentation.Slides[2].Id,
            presentation.Slides[0].Id,
            presentation.Slides[2].Id);
        var session = CreateSession(presentation, out _);

        session.Plan.SelectedShow!.Name.Should().Be("Review");
        session.Plan.CustomShows.Single().ToString().Should().Be("Review (3 slides)");
        session.Plan.AvailableSlides[0].DisplayText.Should().Be("Slide 1: Intro");
        session.Plan.AvailableSlides[0].ToString().Should().Be("Slide 1: Intro");
        session.Plan.SelectedSlides[0].ToString().Should().Be("Slide 3: Appendix");
        session.Plan.SelectedSlideIndex.Should().Be(0);
        session.Plan.CanMoveUp.Should().BeFalse();
        session.Plan.CanMoveDown.Should().BeTrue();
        session.Plan.CanRemove.Should().BeTrue();

        var selectedLast = session.SelectSlide(2);

        selectedLast.RenderScope.Should().Be(SlideShowCustomShowDialogRenderScope.SlideSelection);
        selectedLast.Plan.SelectedSlideIndex.Should().Be(2);
        selectedLast.Plan.CanMoveUp.Should().BeTrue();
        selectedLast.Plan.CanMoveDown.Should().BeFalse();
        selectedLast.Plan.CanRemove.Should().BeTrue();

        var cleared = session.SelectSlide(-1);

        cleared.Plan.SelectedSlideIndex.Should().Be(-1);
        cleared.Plan.CanMoveUp.Should().BeFalse();
        cleared.Plan.CanMoveDown.Should().BeFalse();
        cleared.Plan.CanRemove.Should().BeFalse();
    }

    [Fact]
    public void Session_PreservesDuplicateOccurrencesAcrossMoveAddRemoveAndDragRequests()
    {
        var presentation = MakePresentation();
        var firstSlideId = presentation.Slides[0].Id;
        var secondSlideId = presentation.Slides[1].Id;
        AddShow(presentation, "Review", firstSlideId, secondSlideId, firstSlideId);
        var session = CreateSession(presentation, out var requests);

        var moved = session.MoveSelectedSlide(1);

        moved.MutationRequest!.Kind.Should().Be(SlideShowCustomShowDialogMutationKind.MoveSlide);
        moved.MutationRequest.SourceSlideIndex.Should().Be(0);
        moved.MutationRequest.SourceSlideId.Should().Be(firstSlideId);
        moved.MutationRequest.TargetSlideIndex.Should().Be(1);
        moved.Plan.SelectedSlideIds.Should().Equal(secondSlideId, firstSlideId, firstSlideId);
        moved.Plan.SelectedSlideIndex.Should().Be(1);

        var added = session.AddSlideOccurrence(firstSlideId);

        added.MutationRequest!.Kind.Should().Be(SlideShowCustomShowDialogMutationKind.UpdateSlides);
        added.MutationRequest.SlideIds.Should().Equal(
            secondSlideId,
            firstSlideId,
            firstSlideId,
            firstSlideId);
        added.Plan.SelectedSlideIds.Should().Equal(added.MutationRequest.SlideIds!);
        added.Plan.SelectedSlideIndex.Should().Be(3);

        session.SelectSlide(1);
        var removed = session.RemoveSelectedSlide();

        removed.MutationRequest!.SlideIds.Should().Equal(secondSlideId, firstSlideId, firstSlideId);
        removed.Plan.SelectedSlideIds.Should().Equal(secondSlideId, firstSlideId, firstSlideId);
        removed.Plan.SelectedSlideIndex.Should().Be(1);

        var reordered = session.Reorder(sourceSlideIndex: 1, targetDropIndex: 3);

        reordered.ReorderPlan.ShouldApplyMutation.Should().BeTrue();
        reordered.ReorderPlan.SourceSlideId.Should().Be(firstSlideId);
        reordered.ReorderPlan.TargetSlideIndex.Should().Be(2);
        reordered.SessionTransition.Plan.SelectedSlideIds.Should().Equal(
            secondSlideId,
            firstSlideId,
            firstSlideId);
        reordered.SessionTransition.Plan.SelectedSlideIndex.Should().Be(2);
        requests.Select(request => request.Kind).Should().Equal(
            SlideShowCustomShowDialogMutationKind.MoveSlide,
            SlideShowCustomShowDialogMutationKind.UpdateSlides,
            SlideShowCustomShowDialogMutationKind.UpdateSlides,
            SlideShowCustomShowDialogMutationKind.MoveSlide);
    }

    [Fact]
    public void Session_OwnsValidationMutationRefreshDeleteFallbackAndStartTransitions()
    {
        var presentation = MakePresentation();
        AddShow(presentation, "First", presentation.Slides[0].Id);
        AddShow(presentation, "Second", presentation.Slides[1].Id);
        var startedNames = new List<string?>();
        var session = CreateSession(
            presentation,
            out var requests,
            name =>
            {
                startedNames.Add(name);
                return true;
            },
            new SlideShowCustomShowSessionState(1));

        var duplicateRename = session.Rename("First");

        duplicateRename.RenderScope.Should().Be(SlideShowCustomShowDialogRenderScope.None);
        duplicateRename.MutationResult!.Succeeded.Should().BeFalse();
        duplicateRename.ValidationMessage.Should().Be(
            SlideShowCustomShowPlanner.DuplicateCustomShowNameMessage);
        duplicateRename.Plan.SelectedShow!.Name.Should().Be("Second");

        var updated = session.UpdateSlides(new[] { presentation.Slides[2].Id });

        updated.RenderScope.Should().Be(SlideShowCustomShowDialogRenderScope.Full);
        updated.ValidationMessage.Should().BeNull();
        updated.Plan.SelectedSlideIds.Should().Equal(presentation.Slides[2].Id);
        updated.Plan.SelectedSlideIndex.Should().Be(0);

        var deleted = session.Delete();

        deleted.MutationRequest!.Kind.Should().Be(SlideShowCustomShowDialogMutationKind.Delete);
        deleted.Plan.SelectedShow!.Index.Should().Be(0);
        deleted.Plan.SelectedShow.Name.Should().Be("First");

        var started = session.StartShow();

        started.ShouldClose.Should().BeTrue();
        started.ValidationMessage.Should().BeNull();
        startedNames.Should().Equal("First");
        requests.Should().HaveCount(3);
    }

    [Fact]
    public void Session_RejectsSelectionMutationsAndStartWhenNoShowExists()
    {
        var presentation = MakePresentation();
        var session = CreateSession(presentation, out var requests, _ => false);

        session.Rename("Missing").ValidationMessage.Should().Be(
            SlideShowCustomShowPlanner.MissingCustomShowMessage);
        session.UpdateSlides(Array.Empty<string?>()).ValidationMessage.Should().Be(
            SlideShowCustomShowPlanner.MissingCustomShowMessage);
        session.RemoveSelectedSlide().ValidationMessage.Should().Be(
            SlideShowCustomShowPlanner.MissingCustomShowMessage);
        session.MoveSelectedSlide(1).ValidationMessage.Should().Be(
            SlideShowCustomShowPlanner.MissingCustomShowMessage);
        session.Delete().ValidationMessage.Should().Be(
            SlideShowCustomShowPlanner.MissingCustomShowMessage);
        session.StartShow().ValidationMessage.Should().Be(
            SlideShowCustomShowPlanner.MissingCustomShowMessage);
        requests.Should().BeEmpty();
    }

    [Fact]
    public void TransitionDispatcher_OwnsRenderValidationAndCloseRouting()
    {
        var presentation = MakePresentation();
        var session = CreateSession(presentation, out _);
        var calls = new List<string>();

        SlideShowCustomShowDialogTransitionDispatcher.Dispatch(
            session.InitialTransition with
            {
                ValidationMessage = "Ready",
                ShouldClose = true,
            },
            _ => calls.Add("full"),
            _ => calls.Add("selected"),
            _ => calls.Add("slide"),
            message => calls.Add($"validation:{message}"),
            () => calls.Add("close"));

        calls.Should().Equal("full", "validation:Ready", "close");

        calls.Clear();
        SlideShowCustomShowDialogTransitionDispatcher.Dispatch(
            session.SelectShow(-1),
            _ => calls.Add("full"),
            _ => calls.Add("selected"),
            _ => calls.Add("slide"),
            message => calls.Add($"validation:{message ?? "none"}"),
            () => calls.Add("close"));
        calls.Should().Equal("selected", "validation:none");
    }

    private static SlideShowCustomShowDialogSession CreateSession(
        Presentation presentation,
        out List<SlideShowCustomShowDialogMutationRequest> requests,
        Func<string?, bool>? tryStartShow = null,
        SlideShowCustomShowSessionState? initialState = null)
    {
        requests = new List<SlideShowCustomShowDialogMutationRequest>();
        var capturedRequests = requests;
        return new SlideShowCustomShowDialogSession(
            new SlideShowCustomShowDialogSessionCallbacks(
                state => SlideShowCustomShowSessionPlanner.BuildPlan(
                    SlideShowCustomShowPlanner.BuildAuthoringPlan(presentation),
                    state),
                request =>
                {
                    capturedRequests.Add(request);
                    return request.Apply(presentation);
                },
                tryStartShow ?? (_ => true)),
            initialState);
    }

    private static Presentation MakePresentation()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "intro", Title = "Intro" });
        presentation.Slides.Add(new Slide { Id = "deep", Title = "Deep dive" });
        presentation.Slides.Add(new Slide { Id = "appendix", Title = "Appendix" });
        return presentation;
    }

    private static void AddShow(
        Presentation presentation,
        string name,
        params string[] slideIds)
    {
        var show = new PresentationCustomShow { Name = name };
        show.SlideIds.AddRange(slideIds);
        presentation.CustomShows.Add(show);
    }
}
