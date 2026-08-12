using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowPresenterViewHostCoordinatorTests
{
    [Fact]
    public void Refresh_OwnsDirtyNotesAndFocusSensitiveCommitState()
    {
        var committed = new List<(int SlideIndex, string? Text)>();
        var coordinator = CreateCoordinator(
            setNotesText: (slideIndex, text) => committed.Add((slideIndex, text)));

        coordinator.Refresh(new(false, string.Empty, false), _ => { });
        coordinator.NotifyNotesTextChanged();

        SlideShowPresenterViewRefreshPlan? focusedPlan = null;
        coordinator.Refresh(new(true, "Pending", true), plan => focusedPlan = plan);

        focusedPlan.Should().NotBeNull();
        focusedPlan!.NotesCommitted.Should().BeFalse();
        focusedPlan.ShouldUpdateNotesText.Should().BeFalse();
        focusedPlan.ShouldUpdateSlideNumber.Should().BeFalse();
        committed.Should().BeEmpty();

        SlideShowPresenterViewRefreshPlan? committedPlan = null;
        coordinator.Refresh(new(false, "Committed", false), plan => committedPlan = plan);

        committedPlan.Should().NotBeNull();
        committedPlan!.NotesCommitted.Should().BeTrue();
        committedPlan.ShouldUpdateNotesText.Should().BeTrue();
        committedPlan.ShouldUpdateSlideNumber.Should().BeTrue();
        committed.Should().Equal((0, "Committed"));

        coordinator.CommitNotes("Committed again");
        committed.Should().HaveCount(1);
    }

    [Fact]
    public void ExecuteAction_CommitsDirtyNotesAndOwnsConditionalRefreshDispatch()
    {
        var events = new List<string>();
        var refreshCount = 0;
        var coordinator = CreateCoordinator(
            goNext: () => events.Add("next"),
            setNotesText: (slideIndex, text) => events.Add($"notes:{slideIndex}:{text}"));
        coordinator.Refresh(new(false, string.Empty, false), _ => { });
        coordinator.NotifyNotesTextChanged();

        coordinator.ExecuteAction(
            SlideShowPresenterViewAction.Next,
            new(null, "Updated notes"),
            () => refreshCount++);

        events.Should().Equal("notes:0:Updated notes", "next");
        refreshCount.Should().Be(1);
        coordinator.CommitNotes("Duplicate commit");
        events.Should().HaveCount(2);

        coordinator.ExecuteAction(
            SlideShowPresenterViewAction.GoToSlide,
            new("not a slide", "Updated notes"),
            () => refreshCount++);
        refreshCount.Should().Be(1);
    }

    [Fact]
    public void Refresh_SuppressesControlEventsRaisedWhileApplyingThePlan()
    {
        var selectedModes = new List<SlideShowPresenterPointerMode>();
        var refreshCount = 0;
        var notesCommitCount = 0;
        var coordinator = CreateCoordinator(
            selectPointerMode: selectedModes.Add,
            setNotesText: (_, _) => notesCommitCount++);

        coordinator.Refresh(new(false, string.Empty, false), _ =>
        {
            coordinator.NotifyNotesTextChanged();
            coordinator.SelectPointerMode(
                SlideShowPresenterPointerMode.Pen,
                () => refreshCount++);
        });

        coordinator.CommitNotes("Renderer projection");
        selectedModes.Should().BeEmpty();
        refreshCount.Should().Be(0);
        notesCommitCount.Should().Be(0);

        coordinator.SelectPointerMode(
            SlideShowPresenterPointerMode.Pen,
            () => refreshCount++);
        selectedModes.Should().Equal(SlideShowPresenterPointerMode.Pen);
        refreshCount.Should().Be(1);
    }

    [Fact]
    public void Refresh_RestoresEventDispatchAfterRendererThrows()
    {
        var selectedModes = new List<SlideShowPresenterPointerMode>();
        var coordinator = CreateCoordinator(selectPointerMode: selectedModes.Add);

        var action = () => coordinator.Refresh(
            new(false, string.Empty, false),
            _ => throw new InvalidOperationException("projection failed"));

        action.Should().Throw<InvalidOperationException>();
        coordinator.SelectPointerMode(SlideShowPresenterPointerMode.LaserPointer, () => { });
        selectedModes.Should().Equal(SlideShowPresenterPointerMode.LaserPointer);
    }

    [Fact]
    public void Coordinator_ExposesCanonicalSurfaceCapabilitiesAndCadence()
    {
        var coordinator = CreateCoordinator();

        coordinator.Surface.Should().BeSameAs(SlideShowPresenterViewSurfaceCatalog.Surface);
        coordinator.CanGoToSlide.Should().BeTrue();
        coordinator.CanSetScreenMode.Should().BeTrue();
        coordinator.CanSelectPointerMode.Should().BeTrue();
        coordinator.CanClearInk.Should().BeTrue();
        coordinator.CanSetNotes.Should().BeTrue();
        SlideShowPresenterViewHostCoordinator.RefreshInterval.Should()
            .Be(TimeSpan.FromMilliseconds(250));
    }

    private static SlideShowPresenterViewHostCoordinator CreateCoordinator(
        Action? goNext = null,
        Action<SlideShowPresenterPointerMode>? selectPointerMode = null,
        Action<int, string?>? setNotesText = null)
    {
        var session = new SlideShowPresenterViewSession(
            CreateState,
            goBack: () => { },
            goNext: goNext ?? (() => { }),
            setScreenMode: _ => { },
            selectPointerMode: selectPointerMode ?? (_ => { }),
            clearInk: () => { },
            setTimingIntent: _ => { },
            setMediaIntent: _ => { },
            goToSlide: _ => { },
            setNotesText: setNotesText ?? ((_, _) => { }));
        return new SlideShowPresenterViewHostCoordinator(session);
    }

    private static SlideShowPresenterState CreateState()
    {
        var slide = new Slide { Id = "slide", Title = "Slide" };
        return new SlideShowPresenterState(
            new SlideShowHostState(1, 0, true, true, true, false, "Slide 1 of 1"),
            new SlideShowPresenterSlideState(0, 0, slide.Id, slide.Title, slide),
            null,
            "Notes",
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            SlideShowPresenterDisplayIntent.FullScreen,
            SlideShowPresenterToolPlanner.BuildPlan());
    }
}
