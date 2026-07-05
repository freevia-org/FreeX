using System.Globalization;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class AnimationPanePlannerTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void BuildTimelinePlan_ProjectsSelectedSlideAnimationRows()
    {
        var slide = CreateSlideWithTimelineAnimations();

        var plan = AnimationPanePlanner.BuildTimelinePlan(
            slide,
            selectedShapeIds: [20u],
            displayCulture: Invariant);

        plan.HasAnimations.Should().BeTrue();
        plan.SelectedIndex.Should().Be(1);
        plan.SelectedItem.Should().BeSameAs(plan.Items[1]);
        plan.PreviewIntent.Should().Be(new AnimationPanePlaybackIntent(
            AnimationPanePlaybackIntentKind.PreviewCurrentSlide,
            true,
            1,
            1850,
            "Preview current slide animations"));
        plan.PlaybackControls.Should().Equal(
            new AnimationPanePlaybackControlDescriptor(
                "freep.anim.pane.preview",
                AnimationPanePlaybackControlKind.PreviewCurrentSlide,
                "Preview",
                true,
                null,
                1850,
                "Preview current slide animations",
                null),
            new AnimationPanePlaybackControlDescriptor(
                "freep.anim.pane.play-selected",
                AnimationPanePlaybackControlKind.PlayFromSelected,
                "Play From Selected",
                true,
                1,
                1850,
                "Play animation preview from the selected row",
                null),
            new AnimationPanePlaybackControlDescriptor(
                "freep.anim.pane.play-slide",
                AnimationPanePlaybackControlKind.PlayCurrentSlide,
                "Play All",
                true,
                null,
                1850,
                "Play all animations on the current slide",
                null),
            new AnimationPanePlaybackControlDescriptor(
                "freep.anim.pane.stop",
                AnimationPanePlaybackControlKind.Stop,
                "Stop",
                false,
                null,
                1850,
                "No animation preview is currently running",
                "No animation preview is currently running"));

        plan.Items.Should().HaveCount(3);
        plan.Items[0].Should().BeEquivalentTo(new
        {
            Index = 0,
            OrderText = "1",
            ShapeId = 10u,
            ShapeName = "Title Box",
            EffectText = "In: Appear",
            Trigger = AnimationTrigger.OnClick,
            TriggerIndex = 0,
            TriggerText = "On Click",
            DelayMs = 0,
            DelayText = "0",
            DurationMs = 500,
            DurationText = "0.5",
            StartMs = 0,
            StartText = "0",
            EndMs = 500,
            CanMoveEarlier = false,
            CanMoveLater = true,
            IsSelected = false,
        });
        plan.Items[1].Should().BeEquivalentTo(new
        {
            Index = 1,
            ShapeId = 20u,
            ShapeName = "Content Box",
            EffectText = "Em: Pulse",
            Trigger = AnimationTrigger.WithPrevious,
            TriggerIndex = 1,
            TriggerText = "With Previous",
            DelayMs = 250,
            DurationMs = 1000,
            StartMs = 250,
            EndMs = 1250,
            CanMoveEarlier = true,
            CanMoveLater = true,
            IsSelected = true,
        });
        plan.Items[1].EffectOptions.CanApply.Should().BeFalse();
        plan.Items[1].EffectOptions.DisabledReason.Should().Be(AnimationPanePlanner.UnsupportedEffectOptionMessage);
        plan.Items[2].Should().BeEquivalentTo(new
        {
            Index = 2,
            ShapeId = 30u,
            ShapeName = "Shape 30",
            EffectText = "Out: Fade",
            Trigger = AnimationTrigger.AfterPrevious,
            TriggerIndex = 2,
            TriggerText = "After Previous",
            DelayMs = 100,
            DurationMs = 500,
            StartMs = 1350,
            EndMs = 1850,
            CanMoveEarlier = true,
            CanMoveLater = false,
            IsSelected = false,
        });
    }

    [Fact]
    public void BuildTimelinePlan_ExplicitSelectedIndexWinsOverShapeSelection()
    {
        var slide = CreateSlideWithTimelineAnimations();

        var plan = AnimationPanePlanner.BuildTimelinePlan(
            slide,
            selectedShapeIds: [20u],
            selectedAnimationIndex: 0,
            displayCulture: Invariant);

        plan.SelectedIndex.Should().Be(0);
        plan.Items[0].IsSelected.Should().BeTrue();
        plan.Items[1].IsSelected.Should().BeFalse();
    }

    [Fact]
    public void BuildTimelinePlan_EmptySlideDisablesPreview()
    {
        var plan = AnimationPanePlanner.BuildTimelinePlan(new Slide(), displayCulture: Invariant);

        plan.HasAnimations.Should().BeFalse();
        plan.SelectedIndex.Should().Be(-1);
        plan.PreviewIntent.CanExecute.Should().BeFalse();
        plan.PreviewIntent.Kind.Should().Be(AnimationPanePlaybackIntentKind.None);
        plan.PlaybackControls.Should().HaveCount(4);
        plan.PlaybackControls.Should().OnlyContain(control => !control.IsEnabled);
        plan.PlaybackControls.Single(control => control.Kind == AnimationPanePlaybackControlKind.PlayFromSelected)
            .DisabledReason
            .Should()
            .Be("Select an animation row to play from it");
    }

    [Fact]
    public void BuildPlaybackControls_EnablesSlidePlaybackButRequiresSelectedRow()
    {
        var controls = AnimationPanePlanner.BuildPlaybackControls(-1, 2, 900);

        controls.Should().ContainSingle(control =>
            control.Kind == AnimationPanePlaybackControlKind.PreviewCurrentSlide
            && control.IsEnabled
            && control.TotalDurationMs == 900);
        controls.Should().ContainSingle(control =>
            control.Kind == AnimationPanePlaybackControlKind.PlayCurrentSlide
            && control.IsEnabled);
        controls.Should().ContainSingle(control =>
            control.Kind == AnimationPanePlaybackControlKind.PlayFromSelected
            && !control.IsEnabled
            && control.StartAnimationIndex == null);
        controls.Should().ContainSingle(control =>
            control.Kind == AnimationPanePlaybackControlKind.Stop
            && !control.IsEnabled);
    }

    [Theory]
    [InlineData(0, 3, -1, false, 0, -1)]
    [InlineData(1, 3, -1, true, 1, 0)]
    [InlineData(1, 3, 1, true, 1, 2)]
    [InlineData(2, 3, 1, false, 2, 3)]
    public void BuildReorderIntent_ReportsMoveAvailability(
        int index,
        int count,
        int offset,
        bool canMove,
        int fromIndex,
        int toIndex)
    {
        var intent = AnimationPanePlanner.BuildReorderIntent(index, count, offset);

        intent.Should().Be(new AnimationPaneReorderIntent(canMove, fromIndex, toIndex));
    }

    [Fact]
    public void BuildReorderMutationPlan_AppliesUndoableSharedPaneReorder()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        presentation.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = 10u, Preset = AnimationPreset.Appear });
        presentation.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = 20u, Preset = AnimationPreset.Fade });

        var plan = AnimationPanePlanner.BuildReorderMutationPlan(
            editor.CurrentSlideAnimations,
            1,
            -1);

        plan.Should().Be(new AnimationPaneReorderMutationPlan(
            true,
            1,
            0,
            0,
            "Move animation 2 earlier",
            null));
        AnimationPanePlanner.TryApplyReorderMutation(editor, plan).Should().BeTrue();

        editor.CurrentSlideAnimations.Select(animation => animation.ShapeId)
            .Should()
            .Equal(20u, 10u);

        editor.Undo();
        editor.CurrentSlideAnimations.Select(animation => animation.ShapeId)
            .Should()
            .Equal(10u, 20u);
    }

    [Fact]
    public void BuildReorderMutationPlan_DisablesOutOfRangePaneMoves()
    {
        var slide = CreateSlideWithTimelineAnimations();

        var firstEarlier = AnimationPanePlanner.BuildReorderMutationPlan(slide.Animations, 0, -1);
        var missing = AnimationPanePlanner.BuildReorderMutationPlan(slide.Animations, 9, 1);

        firstEarlier.Should().Be(new AnimationPaneReorderMutationPlan(
            false,
            0,
            -1,
            0,
            "Cannot move animation",
            AnimationPanePlanner.InvalidReorderMessage));
        missing.Should().Be(new AnimationPaneReorderMutationPlan(
            false,
            9,
            10,
            2,
            "Cannot move animation",
            AnimationPanePlanner.InvalidReorderMessage));
    }

    [Theory]
    [InlineData(AnimationKind.Entrance, AnimationPreset.Appear, "In: Appear")]
    [InlineData(AnimationKind.Exit, AnimationPreset.Fade, "Out: Fade")]
    [InlineData(AnimationKind.Emphasis, AnimationPreset.Pulse, "Em: Pulse")]
    [InlineData(AnimationKind.Motion, AnimationPreset.Fade, "Mv: Motion")]
    public void FormatEffect_ReturnsPaneLabel(
        AnimationKind kind,
        AnimationPreset preset,
        string expected)
    {
        var label = AnimationPanePlanner.FormatEffect(new ShapeAnimation
        {
            Kind = kind,
            Preset = preset
        });

        label.Should().Be(expected);
    }

    [Fact]
    public void TriggerLabels_MatchTriggerIndexes()
    {
        AnimationPanePlanner.TriggerLabels.Should().Equal(
            "On Click",
            "With Previous",
            "After Previous");

        AnimationPanePlanner.ToTriggerIndex(AnimationTrigger.OnClick).Should().Be(0);
        AnimationPanePlanner.ToTriggerIndex(AnimationTrigger.WithPrevious).Should().Be(1);
        AnimationPanePlanner.ToTriggerIndex(AnimationTrigger.AfterPrevious).Should().Be(2);
    }

    [Theory]
    [InlineData(0, AnimationTrigger.OnClick)]
    [InlineData(1, AnimationTrigger.WithPrevious)]
    [InlineData(2, AnimationTrigger.AfterPrevious)]
    public void TryGetTrigger_MapsValidIndexes(int index, AnimationTrigger expected)
    {
        AnimationPanePlanner.TryGetTrigger(index, out var trigger).Should().BeTrue();
        trigger.Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void TryGetTrigger_RejectsInvalidIndexes(int index)
    {
        AnimationPanePlanner.TryGetTrigger(index, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(500, "0.5")]
    [InlineData(1000, "1")]
    [InlineData(1250, "1.25")]
    public void FormatDuration_FormatsSeconds(int durationMs, string expected)
    {
        AnimationPanePlanner.FormatDuration(durationMs, Invariant).Should().Be(expected);
    }

    [Theory]
    [InlineData("0.75", 750)]
    [InlineData("0.75s", 750)]
    [InlineData("1.2345", 1234)]
    public void TryParseDuration_AcceptsPositiveInvariantSeconds(
        string text,
        int expectedMs)
    {
        AnimationPanePlanner.TryParseDuration(text, out int ms).Should().BeTrue();
        ms.Should().Be(expectedMs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    public void TryParseDuration_RejectsInvalidOrNonPositiveSeconds(string text)
    {
        AnimationPanePlanner.TryParseDuration(text, out _).Should().BeFalse();
    }

    [Fact]
    public void BuildDurationEditPlan_ChangedValidText_RequestsUpdate()
    {
        var plan = AnimationPanePlanner.BuildDurationEditPlan("1.25", 500, Invariant);

        plan.Should().Be(new AnimationPaneDurationEditPlan(true, 1250, "1.25"));
    }

    [Fact]
    public void BuildDurationEditPlan_SameValue_NormalizesDisplayWithoutUpdate()
    {
        var plan = AnimationPanePlanner.BuildDurationEditPlan("1.0", 1000, Invariant);

        plan.Should().Be(new AnimationPaneDurationEditPlan(false, 1000, "1"));
    }

    [Fact]
    public void BuildDurationEditPlan_InvalidText_RevertsToCurrentDisplay()
    {
        var plan = AnimationPanePlanner.BuildDurationEditPlan("oops", 500, Invariant);

        plan.Should().Be(new AnimationPaneDurationEditPlan(false, 500, "0.5"));
    }

    [Fact]
    public void BuildDelayEditPlan_AllowsZeroDelay()
    {
        var plan = AnimationPanePlanner.BuildDelayEditPlan("0", 250, Invariant);

        plan.Should().Be(new AnimationPaneDurationEditPlan(true, 0, "0"));
    }

    [Fact]
    public void BuildTimingMutationPlans_ProjectTriggerDurationAndDelayEdits()
    {
        var slide = CreateSlideWithTimelineAnimations();

        var trigger = AnimationPanePlanner.BuildTriggerMutationPlan(
            slide.Animations,
            1,
            AnimationPanePlanner.ToTriggerIndex(AnimationTrigger.AfterPrevious));
        var duration = AnimationPanePlanner.BuildDurationMutationPlan(
            slide.Animations,
            1,
            "1.75s",
            Invariant);
        var delay = AnimationPanePlanner.BuildDelayMutationPlan(
            slide.Animations,
            1,
            "0.50s",
            Invariant);

        trigger.Should().Be(new AnimationPaneTimingMutationPlan(
            true,
            1,
            AnimationPaneTimingEditKind.Trigger,
            AnimationTrigger.AfterPrevious,
            1000,
            250,
            "After Previous",
            null));
        duration.Should().Be(new AnimationPaneTimingMutationPlan(
            true,
            1,
            AnimationPaneTimingEditKind.Duration,
            AnimationTrigger.WithPrevious,
            1750,
            250,
            "1.75",
            null));
        delay.Should().Be(new AnimationPaneTimingMutationPlan(
            true,
            1,
            AnimationPaneTimingEditKind.Delay,
            AnimationTrigger.WithPrevious,
            1000,
            500,
            "0.5",
            null));
    }

    [Fact]
    public void BuildTimingMutationPlans_DisableInvalidOrNoOpEdits()
    {
        var slide = CreateSlideWithTimelineAnimations();

        AnimationPanePlanner.BuildTriggerMutationPlan(slide.Animations, 9, 0)
            .DisabledReason
            .Should()
            .Be(AnimationPanePlanner.MissingAnimationMessage);
        AnimationPanePlanner.BuildTriggerMutationPlan(slide.Animations, 0, 9)
            .DisabledReason
            .Should()
            .Be(AnimationPanePlanner.InvalidTriggerMessage);
        AnimationPanePlanner.BuildDurationMutationPlan(slide.Animations, 0, "0", Invariant)
            .DisabledReason
            .Should()
            .Be(AnimationPanePlanner.InvalidDurationMessage);
        AnimationPanePlanner.BuildDelayMutationPlan(slide.Animations, 0, "bad", Invariant)
            .DisabledReason
            .Should()
            .Be(AnimationPanePlanner.InvalidDelayMessage);
        AnimationPanePlanner.BuildDelayMutationPlan(slide.Animations, 0, "0", Invariant)
            .ShouldApply
            .Should()
            .BeFalse("the current delay is already zero");
    }

    [Fact]
    public void TryApplyTimingMutation_UpdatesSelectedAnimation()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shapeId = presentation.Slides[0].Shapes[0].Id;
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = shapeId,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 500,
            DelayMs = 0,
        });
        var plan = AnimationPanePlanner.BuildDelayMutationPlan(
            editor.CurrentSlideAnimations,
            0,
            "0.25",
            Invariant);

        AnimationPanePlanner.TryApplyTimingMutation(editor, plan).Should().BeTrue();

        editor.CurrentSlideAnimations[0].DelayMs.Should().Be(250);
        editor.Undo();
        editor.CurrentSlideAnimations[0].DelayMs.Should().Be(0);
    }

    [Fact]
    public void BuildEffectOptionsPlan_ProjectsSupportedDirectionVariants()
    {
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 10u,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.FlyIn,
            Direction = AnimationDirection.FromLeft,
        });

        var plan = AnimationPanePlanner.BuildEffectOptionsPlan(slide.Animations, 0);

        plan.Should().BeEquivalentTo(new
        {
            CanApply = true,
            AnimationIndex = 0,
            EffectText = "In: FlyIn",
            SelectedOptionText = "From Left",
            DisabledReason = (string?)null,
        });
        plan.Options.Should().Equal(
            new AnimationPaneEffectOptionDescriptor("from-bottom", "From Bottom", AnimationDirection.FromBottom, false),
            new AnimationPaneEffectOptionDescriptor("from-left", "From Left", AnimationDirection.FromLeft, true),
            new AnimationPaneEffectOptionDescriptor("from-right", "From Right", AnimationDirection.FromRight, false),
            new AnimationPaneEffectOptionDescriptor("from-top", "From Top", AnimationDirection.FromTop, false));
    }

    [Theory]
    [InlineData(AnimationPreset.Wipe, "from-top", AnimationDirection.FromTop)]
    [InlineData(AnimationPreset.Zoom, "out", AnimationDirection.Out)]
    [InlineData(AnimationPreset.Split, "vertical", AnimationDirection.Vertical)]
    [InlineData(AnimationPreset.RandomBars, "horizontal", AnimationDirection.Horizontal)]
    public void BuildEffectOptionMutationPlan_MapsSupportedOptionIds(
        AnimationPreset preset,
        string optionId,
        AnimationDirection expectedDirection)
    {
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 10u,
            Kind = AnimationKind.Entrance,
            Preset = preset,
        });

        var plan = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            slide.Animations,
            0,
            optionId);

        plan.ShouldApply.Should().BeTrue();
        plan.Direction.Should().Be(expectedDirection);
        plan.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void BuildEffectOptionMutationPlan_DisablesMissingUnsupportedAndInvalidOptions()
    {
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation { Preset = AnimationPreset.Fade });
        slide.Animations.Add(new ShapeAnimation
        {
            Preset = AnimationPreset.Wipe,
            Direction = AnimationDirection.FromBottom,
        });

        AnimationPanePlanner.BuildEffectOptionMutationPlan(slide.Animations, 9, "from-left")
            .DisabledReason
            .Should()
            .Be(AnimationPanePlanner.MissingEffectOptionMessage);
        AnimationPanePlanner.BuildEffectOptionMutationPlan(slide.Animations, 0, "from-left")
            .DisabledReason
            .Should()
            .Be(AnimationPanePlanner.UnsupportedEffectOptionMessage);
        AnimationPanePlanner.BuildEffectOptionMutationPlan(slide.Animations, 1, "sideways")
            .DisabledReason
            .Should()
            .Be(AnimationPanePlanner.InvalidEffectOptionMessage);
        AnimationPanePlanner.BuildEffectOptionMutationPlan(slide.Animations, 1, "from-bottom")
            .ShouldApply
            .Should()
            .BeFalse("the current effect option is already selected");
    }

    [Fact]
    public void TryApplyEffectOptionMutation_UpdatesSelectedAnimation()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shapeId = presentation.Slides[0].Shapes[0].Id;
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = shapeId,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Wipe,
            Direction = AnimationDirection.FromBottom,
        });
        var plan = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            editor.CurrentSlideAnimations,
            0,
            "from-left");

        AnimationPanePlanner.TryApplyEffectOptionMutation(editor, plan).Should().BeTrue();

        editor.CurrentSlideAnimations[0].Direction.Should().Be(AnimationDirection.FromLeft);
        editor.Undo();
        editor.CurrentSlideAnimations[0].Direction.Should().Be(AnimationDirection.FromBottom);
    }

    [Fact]
    public void BuildTimelinePlan_RunningPlaybackEnablesStopAndDisablesStartControls()
    {
        var timeline = AnimationPanePlanner.BuildTimelinePlan(
            CreateSlideWithTimelineAnimations(),
            selectedAnimationIndex: 1,
            displayCulture: Invariant,
            isPlaybackRunning: true);

        timeline.PlaybackControls.Should().ContainSingle(control =>
            control.Kind == AnimationPanePlaybackControlKind.Stop
            && control.IsEnabled
            && control.DisabledReason == null);
        timeline.PlaybackControls.Should().Contain(control =>
            control.Kind == AnimationPanePlaybackControlKind.PreviewCurrentSlide
            && !control.IsEnabled
            && control.DisabledReason == "Stop the running animation preview before starting another");
        timeline.PlaybackControls.Should().Contain(control =>
            control.Kind == AnimationPanePlaybackControlKind.PlayFromSelected
            && !control.IsEnabled
            && control.StartAnimationIndex == 1);
        timeline.PreviewIntent.CanExecute.Should().BeFalse();
        timeline.PreviewIntent.Description.Should().Be("Stop the running animation preview before starting another");
    }

    [Fact]
    public void BuildPlaybackSessionPlan_PlayFromSelectedCreatesRelativeTimeline()
    {
        var timeline = AnimationPanePlanner.BuildTimelinePlan(
            CreateSlideWithTimelineAnimations(),
            selectedAnimationIndex: 1,
            displayCulture: Invariant);

        var session = AnimationPanePlanner.BuildPlaybackSessionPlan(
            timeline,
            AnimationPanePlaybackControlKind.PlayFromSelected,
            elapsedMs: 250);

        session.State.Should().Be(AnimationPanePlaybackSessionState.Running);
        session.IsRunning.Should().BeTrue();
        session.CommandKind.Should().Be(AnimationPanePlaybackControlKind.PlayFromSelected);
        session.StartAnimationIndex.Should().Be(1);
        session.ElapsedMs.Should().Be(250);
        session.TotalDurationMs.Should().Be(1600);
        session.RemainingDurationMs.Should().Be(1350);
        session.StatusText.Should().Be("Playing from animation 2");
        session.Segments.Should().Equal(
            new AnimationPanePlaybackSegmentPlan(
                1,
                20u,
                "Content Box",
                "Em: Pulse",
                AnimationTrigger.WithPrevious,
                250,
                0,
                1000,
                1250,
                1000),
            new AnimationPanePlaybackSegmentPlan(
                2,
                30u,
                "Shape 30",
                "Out: Fade",
                AnimationTrigger.AfterPrevious,
                1350,
                1100,
                500,
                1850,
                1600));
        session.PlaybackControls.Should().ContainSingle(control =>
            control.Kind == AnimationPanePlaybackControlKind.Stop && control.IsEnabled);
        session.PlaybackControls.Should().Contain(control =>
            control.Kind == AnimationPanePlaybackControlKind.PlayCurrentSlide && !control.IsEnabled);
    }

    [Theory]
    [InlineData(AnimationPanePlaybackControlKind.PreviewCurrentSlide)]
    [InlineData(AnimationPanePlaybackControlKind.PlayCurrentSlide)]
    public void BuildPlaybackSessionPlan_PreviewAndPlayAllQueueWholeSlide(
        AnimationPanePlaybackControlKind commandKind)
    {
        var timeline = AnimationPanePlanner.BuildTimelinePlan(
            CreateSlideWithTimelineAnimations(),
            selectedAnimationIndex: 1,
            displayCulture: Invariant);

        var session = AnimationPanePlanner.BuildPlaybackSessionPlan(
            timeline,
            commandKind,
            elapsedMs: 5000);

        session.State.Should().Be(AnimationPanePlaybackSessionState.Running);
        session.StartAnimationIndex.Should().Be(0);
        session.ElapsedMs.Should().Be(1850);
        session.TotalDurationMs.Should().Be(1850);
        session.RemainingDurationMs.Should().Be(0);
        session.Segments.Select(segment => segment.RelativeStartMs)
            .Should()
            .Equal(0, 250, 1350);
        session.Segments.Select(segment => segment.RelativeEndMs)
            .Should()
            .Equal(500, 1250, 1850);
        session.StatusText.Should().Be("Playing all current slide animations");
    }

    [Fact]
    public void BuildPlaybackSessionPlan_StopReturnsIdleControls()
    {
        var timeline = AnimationPanePlanner.BuildTimelinePlan(
            CreateSlideWithTimelineAnimations(),
            selectedAnimationIndex: 1,
            displayCulture: Invariant,
            isPlaybackRunning: true);

        var session = AnimationPanePlanner.BuildPlaybackSessionPlan(
            timeline,
            AnimationPanePlaybackControlKind.Stop);

        session.State.Should().Be(AnimationPanePlaybackSessionState.Stopped);
        session.IsRunning.Should().BeFalse();
        session.Segments.Should().BeEmpty();
        session.StatusText.Should().Be("Animation preview stopped");
        session.PlaybackControls.Should().Contain(control =>
            control.Kind == AnimationPanePlaybackControlKind.PlayFromSelected
            && control.IsEnabled
            && control.StartAnimationIndex == 1);
        session.PlaybackControls.Should().Contain(control =>
            control.Kind == AnimationPanePlaybackControlKind.Stop
            && !control.IsEnabled);
    }

    private static Slide CreateSlideWithTimelineAnimations()
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape { Id = 10u, Name = "Title Box" });
        slide.Shapes.Add(new SlideShape { Id = 20u, Name = "Content Box" });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 10u,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 500,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 20u,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Pulse,
            Trigger = AnimationTrigger.WithPrevious,
            DelayMs = 250,
            DurationMs = 1000,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 30u,
            Kind = AnimationKind.Exit,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.AfterPrevious,
            DelayMs = 100,
            DurationMs = 500,
        });
        return slide;
    }
}
