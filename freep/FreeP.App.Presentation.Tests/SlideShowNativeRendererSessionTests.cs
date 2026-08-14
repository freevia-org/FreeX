using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowNativeRendererSessionTests
{
    [Fact]
    public void PresenterBinding_AppliesRefreshActionsAndLifecycleInPortableOrder()
    {
        var events = new List<string>();
        var coordinator = CreatePresenterCoordinator(
            goNext: () => events.Add("next"),
            setNotesText: (_, text) => events.Add($"notes:{text}"));
        var controls = CreatePresenterControls();
        var binding = CreatePresenterBinding(coordinator, controls);

        binding.Open(() => events.Add("timer:start"));
        controls.Status.Text.Should().Be("Slide 1 of 1");
        controls.Elapsed.Text.Should().Be("Elapsed 00:00");
        controls.CurrentPreview.Slide.Should().NotBeNull();
        controls.CurrentPreview.RefreshCount.Should().Be(1);
        controls.Actions[SlideShowPresenterViewAction.Next].IsEnabled.Should().BeFalse();
        events.Should().Equal("timer:start");

        controls.Notes.Text = "Updated";
        binding.NotifyNotesTextChanged();
        binding.ExecuteAction(SlideShowPresenterViewAction.Next);
        binding.Close(() => events.Add("timer:stop"));

        events.Should().Equal("timer:start", "notes:Updated", "next", "timer:stop");
    }

    [Fact]
    public void HeaderComposition_OwnsStableOrderingAndInitialCapabilityState()
    {
        var coordinator = CreatePresenterCoordinator();
        var order = new List<string>();

        var buttons = SlideShowPresenterViewHeaderComposition.Compose(
            coordinator,
            () => order.Add("SlideNumber"),
            () => order.Add("PointerMode"),
            (plan, action, enabled) =>
            {
                order.Add(action.ToString());
                return (plan.Label, enabled);
            },
            _ => { });

        order.Should().Equal(
            "Previous", "Next", "SlideNumber", "GoToSlide", "RecordTimings",
            "RehearseTimings", "Narration", "NarrationAndMedia", "ApplyRecording",
            "ShowScreen", "BlackScreen", "WhiteScreen", "ClearInk", "PointerMode");
        buttons.Should().ContainKey(SlideShowPresenterViewAction.GoToSlide);
        buttons[SlideShowPresenterViewAction.GoToSlide].enabled.Should().BeTrue();
    }

    [Fact]
    public void MediaInteractionSession_OwnsEntryGeometryPolicyAndActiveSlideIdentity()
    {
        var first = new Slide { Id = "first" };
        var second = new Slide { Id = "second" };
        var session = new SlideShowMediaNativeInteractionSession();

        var entry = session.Enter(new(first, 960, 540, 1280, 720, ShowMediaControls: false));

        entry.Items.Should().BeEmpty();
        session.ActiveSlide.Should().BeSameAs(first);
        session.CanvasWidth.Should().Be(1280);
        session.UpdateLayout(first, 1920, 1080).Should().BeTrue();
        session.UpdateLayout(second, 1920, 1080).Should().BeFalse();
        session.PlanClick(20, 20).Should().BeSameAs(SlideShowMediaClickPlan.NotMedia);

        session.Clear();
        session.ActiveSlide.Should().BeNull();
    }

    [Fact]
    public void WindowLaunchPlan_OwnsFullPresentationRouteAndCaptionPreferences()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide { Id = "hidden", IsHidden = true });

        var plan = SlideShowWindowLaunchPlan.FullPresentation(presentation, startIndex: 1) with
        {
            PreferredCaptionSlideIndex = 4,
            PreferredCaptionShapeId = 9,
            PreferredCaptionTrackIndex = 2,
        };

        plan.Presentation.Should().BeSameAs(presentation);
        plan.PlaybackRoute.Slides.Should().NotContain(slide => slide.Id == "hidden");
        plan.PreferredCaptionSlideIndex.Should().Be(4);
        plan.PreferredCaptionShapeId.Should().Be(9);
        plan.PreferredCaptionTrackIndex.Should().Be(2);
    }

    [Fact]
    public void InkProjectionSession_ClearsSizesAndDispatchesNativePrimitives()
    {
        var state = SlideShowInkExecutionPlanner.CreateState(
            committedStrokes:
            [
                new(
                    "stroke",
                    0,
                    SlideShowPresenterPointerMode.Pen,
                    new("#336699", 5, 0.75),
                    [new(10, 20), new(30, 40)]),
            ]);
        var events = new List<string>();

        SlideShowInkNativeProjectionSession.Apply(
            state,
            960,
            540,
            new(960, 540),
            () => events.Add("clear"),
            (width, height) => events.Add($"size:{width}:{height}"),
            primitive => events.Add($"stroke:{primitive.Points.Count}"),
            _ => events.Add("laser"));

        events.Should().Equal("clear", "size:960:540", "stroke:2");
    }

    private static SlideShowPresenterViewHostCoordinator CreatePresenterCoordinator(
        Action? goNext = null,
        Action<int, string?>? setNotesText = null)
    {
        var slide = new Slide { Id = "slide", Title = "Slide" };
        var operations = new SlideShowPresenterViewOperations(
            () => new(
                new(1, 0, true, true, true, false, "Slide 1 of 1"),
                new(0, 0, slide.Id, slide.Title, slide),
                null,
                "Notes",
                DateTimeOffset.UtcNow,
                TimeSpan.Zero,
                SlideShowPresenterDisplayIntent.FullScreen,
                SlideShowPresenterToolPlanner.BuildPlan()),
            () => { },
            goNext ?? (() => { }),
            _ => { },
            _ => { },
            () => { },
            _ => { },
            _ => { },
            () => null!,
            () => null!,
            _ => { },
            setNotesText ?? ((_, _) => { }));
        return new(operations);
    }

    private static PresenterControls CreatePresenterControls()
    {
        var actions = Enum.GetValues<SlideShowPresenterViewAction>()
            .ToDictionary(action => action, _ => new FakeControl());
        return new(
            new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), actions);
    }

    private static SlideShowPresenterViewNativeBinding<FakeControl, FakeControl, FakeControl, FakeControl, FakeControl>
        CreatePresenterBinding(
            SlideShowPresenterViewHostCoordinator coordinator,
            PresenterControls controls) =>
        new(
            coordinator,
            new(
                controls.Status,
                controls.Elapsed,
                controls.CurrentLabel,
                controls.NextLabel,
                controls.RecordingStatus,
                controls.Notes,
                controls.SlideNumber,
                controls.PointerMode,
                controls.CurrentPreview,
                controls.NextPreview,
                controls.Actions),
            new(
                control => control.Text,
                control => control.IsFocused,
                (control, value) => control.Text = value,
                (control, value) => control.Text = value,
                (control, value) => control.Text = value,
                (control, value) => control.IsEnabled = value,
                (control, value) => control.PointerMode = value,
                (control, value) => control.Slide = value,
                control => control.RefreshCount++));

    private sealed record PresenterControls(
        FakeControl Status,
        FakeControl Elapsed,
        FakeControl CurrentLabel,
        FakeControl NextLabel,
        FakeControl RecordingStatus,
        FakeControl Notes,
        FakeControl SlideNumber,
        FakeControl PointerMode,
        FakeControl CurrentPreview,
        FakeControl NextPreview,
        IReadOnlyDictionary<SlideShowPresenterViewAction, FakeControl> Actions);

    private sealed class FakeControl
    {
        public string? Text { get; set; }
        public bool IsFocused { get; set; }
        public bool IsEnabled { get; set; }
        public SlideShowPresenterPointerMode PointerMode { get; set; }
        public Slide? Slide { get; set; }
        public int RefreshCount { get; set; }
    }
}
