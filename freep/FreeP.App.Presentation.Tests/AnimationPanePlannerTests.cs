using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;

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
        plan.Items[1].EffectOptions.CanApply.Should().BeTrue();
        plan.Items[1].EffectOptions.Options.Should().HaveCount(5);
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
    public void BuildTimelinePlan_ResolvesGroupedChildAnimationName()
    {
        var slide = new Slide();
        var group = new SlideShape { Id = 100u, Name = "Group" };
        group.Children.Add(new SlideShape { Id = 101u, Name = "Grouped Caption" });
        slide.Shapes.Add(group);
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 101u,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 500,
        });

        var plan = AnimationPanePlanner.BuildTimelinePlan(slide, displayCulture: Invariant);

        plan.Items.Should().ContainSingle();
        plan.Items[0].ShapeId.Should().Be(101u);
        plan.Items[0].ShapeName.Should().Be("Grouped Caption");
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
    public void BuildWorkflowViewPlan_ProjectsSharedPaneSurface()
    {
        var timeline = AnimationPanePlanner.BuildTimelinePlan(
            CreateSlideWithTimelineAnimations(),
            selectedAnimationIndex: 1,
            displayCulture: Invariant);

        var viewPlan = AnimationPanePlanner.BuildWorkflowViewPlan(timeline, slideIndex: 2);

        viewPlan.Heading.Should().Be("Animation Pane - slide 3 (3 animations)");
        viewPlan.Message.Should().Be("Selected: Content Box - Em: Pulse");
        viewPlan.EmptyMessage.Should().Be("No animations on this slide.");
        viewPlan.PlaybackControlSummaries.Should().Equal(
            "Preview: available",
            "Play From Selected: available",
            "Play All: available",
            "Stop: unavailable");
        viewPlan.RowSummaries.Should().HaveCount(3);
        viewPlan.RowSummaries[0].Should().Contain("1. Title Box - In: Appear")
            .And.Contain("On Click")
            .And.Contain("duration 0.5s")
            .And.Contain("move earlier unavailable")
            .And.Contain("move later available");
        viewPlan.RowSummaries[1].Should().Contain("2. Content Box - Em: Pulse")
            .And.Contain("With Previous")
            .And.Contain("delay 0.25s")
            .And.Contain("move earlier available")
            .And.Contain("move later available");
    }

    [Fact]
    public void BuildWorkflowEvidencePlan_ProjectsSharedPaneDepthContract()
    {
        var timeline = AnimationPanePlanner.BuildTimelinePlan(
            CreateSlideWithTimelineAnimations(),
            selectedAnimationIndex: 1,
            displayCulture: Invariant);

        var evidence = AnimationPanePlanner.BuildWorkflowEvidencePlan(timeline, slideIndex: 2);

        evidence.View.Heading.Should().Be("Animation Pane - slide 3 (3 animations)");
        evidence.RowCount.Should().Be(3);
        evidence.EditableTimingRowCount.Should().Be(3);
        evidence.EffectOptionRowCount.Should().Be(1);
        evidence.ReorderableRowCount.Should().Be(3);
        evidence.HasSelectedRow.Should().BeTrue();
        evidence.CanPreview.Should().BeTrue();
        evidence.CanPlayFromSelected.Should().BeTrue();
        evidence.EvidenceLines.Should().Equal(
            "Rows: 3; selected: 2; timing editors: 3; effect-option rows: 1; reorderable rows: 3",
            "Playback controls: Preview: available; Play From Selected: available; Play All: available; Stop: unavailable",
            "Selected row: Content Box - Em: Pulse; trigger With Previous; duration 1s; delay 0.25s");
    }

    [Fact]
    public void BuildVisualBaselineReadinessPlan_ProjectsPowerPointWpfAvaloniaCaptureMatrix()
    {
        var timeline = AnimationPanePlanner.BuildTimelinePlan(
            CreateSlideWithTimelineAnimations(),
            selectedAnimationIndex: 1,
            displayCulture: Invariant);
        var step = new AnimationStep(
        [
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 91,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.Wheel,
                    Direction = AnimationDirection.In,
                    WheelSpokeCount = 8,
                    DurationMs = 300
                },
                StartDelayMs: 0)
        ]);
        var checkpoints = SlideShowPlaybackFramePlanner.PlanAnimationStepCheckpoints(
            step,
            slideWidthDip: 960,
            slideHeightDip: 540);

        var readiness = AnimationPanePlanner.BuildVisualBaselineReadinessPlan(
            timeline,
            checkpoints,
            slideIndex: 2,
            scenarioId: "Advanced Effect Playback");

        readiness.ScenarioId.Should().Be("advanced-effect-playback");
        readiness.SlideIndex.Should().Be(2);
        readiness.AnimationRowCount.Should().Be(3);
        readiness.PlaybackCheckpointCount.Should().Be(3);
        readiness.CaptureRequests.Should().HaveCount(12);
        readiness.PowerPointRequestCount.Should().Be(4);
        readiness.SharedHostRequestCount.Should().Be(8);
        readiness.IsPowerPointAuthoritativeReady.Should().BeTrue();
        readiness.CaptureRequests.Select(request => request.Host)
            .Should()
            .ContainInOrder(
                AnimationPaneBaselineCaptureHost.PowerPoint,
                AnimationPaneBaselineCaptureHost.Wpf,
                AnimationPaneBaselineCaptureHost.Avalonia);

        var panePowerPoint = readiness.CaptureRequests.First(request =>
            request.Host == AnimationPaneBaselineCaptureHost.PowerPoint
            && request.Kind == AnimationPaneBaselineCaptureKind.PaneWorkflow);
        panePowerPoint.CaptureId.Should().Be("freep.advanced-effect-playback.slide-3.pane.workflow.powerpoint");
        panePowerPoint.SurfaceId.Should().Be("freep.advanced-effect-playback.slide-3.pane.workflow");
        panePowerPoint.RequiresPowerPointCom.Should().BeTrue();
        panePowerPoint.EvidenceSummary.Should().Be("Animation pane slide 3: 3 row(s); selected 2");

        var midpointWpf = readiness.CaptureRequests.Single(request =>
            request.Host == AnimationPaneBaselineCaptureHost.Wpf
            && request.Kind == AnimationPaneBaselineCaptureKind.PlaybackCheckpoint
            && request.Checkpoint == "midpoint");
        midpointWpf.CaptureId.Should().Be("freep.advanced-effect-playback.slide-3.playback.midpoint.wpf");
        midpointWpf.ElapsedMs.Should().Be(150);
        midpointWpf.RequiresPowerPointCom.Should().BeFalse();
        midpointWpf.EvidenceSummary.Should().Contain("Wheel Clip")
            .And.Contain("clip Wheel 0.5");

        readiness.EvidenceLines.Should().Equal(
            "Scenario advanced-effect-playback: slide 3; rows 3; playback checkpoints 3",
            "Capture requests: 12; PowerPoint 4; WPF 4; Avalonia 4",
            "PowerPoint requests are readiness contracts and require desktop PowerPoint COM on the baseline machine");
    }

    [Fact]
    public void BuildPlaybackWorkflowEvidencePlan_CombinesPaneSessionAndVisualCheckpointCoverage()
    {
        var timeline = AnimationPanePlanner.BuildTimelinePlan(
            CreateSlideWithTimelineAnimations(),
            selectedAnimationIndex: 1,
            displayCulture: Invariant);
        var session = AnimationPanePlanner.BuildPlaybackSessionPlan(
            timeline,
            AnimationPanePlaybackControlKind.PlayFromSelected);
        var step = new AnimationStep(
        [
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 81,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.Wheel,
                    Direction = AnimationDirection.In,
                    WheelSpokeCount = 6,
                    DurationMs = 300
                },
                StartDelayMs: 0),
            new AnimationEntry(
                new ShapeAnimation
                {
                    ShapeId = 82,
                    Kind = AnimationKind.Entrance,
                    Preset = AnimationPreset.FlyIn,
                    Direction = AnimationDirection.FromRight,
                    DurationMs = 250
                },
                StartDelayMs: 175)
        ]);
        var checkpoints = SlideShowPlaybackFramePlanner.PlanAnimationStepCheckpoints(
            step,
            slideWidthDip: 960,
            slideHeightDip: 540);

        var evidence = AnimationPanePlanner.BuildPlaybackWorkflowEvidencePlan(
            timeline,
            session,
            checkpoints,
            slideIndex: 2,
            scenarioId: "Pane Playback/Selected");

        evidence.ScenarioId.Should().Be("pane-playback-selected");
        evidence.SlideIndex.Should().Be(2);
        evidence.CommandKind.Should().Be(AnimationPanePlaybackControlKind.PlayFromSelected);
        evidence.SessionState.Should().Be(AnimationPanePlaybackSessionState.Running);
        evidence.StartAnimationIndex.Should().Be(1);
        evidence.SegmentCount.Should().Be(2);
        evidence.PlaybackCheckpointCount.Should().Be(3);
        evidence.TrackKinds.Should().Equal(
            SlideShowAnimationVisualTrackKind.Clip,
            SlideShowAnimationVisualTrackKind.Translate);
        evidence.ClipKinds.Should().Equal(SlideShowAnimationClipKind.Wheel);
        evidence.HasSharedNoComHostEvidence.Should().BeTrue();
        evidence.HostRows.Select(row => row.Host)
            .Should()
            .Equal(AnimationPanePlaybackWorkflowHost.Wpf, AnimationPanePlaybackWorkflowHost.Avalonia);
        evidence.HostRows.Should().OnlyContain(row =>
            row.RequiresPowerPointCom == false
            && row.SegmentCount == 2
            && row.PlaybackCheckpointCount == 3);
        evidence.HostRows[0].EvidenceId.Should().Be("pane-playback-selected-slide-3-playfromselected-wpf");
        evidence.EvidenceLines.Should().Equal(
            "Scenario pane-playback-selected: slide 3; command PlayFromSelected; state Running; segments 2; checkpoints 3",
            "Pane playback tracks: Clip, Translate; clips: Wheel; selected start: 2",
            "Shared host rows: WPF/Avalonia; PowerPoint COM required: false");
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
    public void BuildRemoveMutationPlan_AppliesUndoableSharedPaneRemoval()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        presentation.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = 10u, Preset = AnimationPreset.Appear });
        presentation.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = 20u, Preset = AnimationPreset.Fade });

        var plan = AnimationPanePlanner.BuildRemoveMutationPlan(
            editor.CurrentSlideAnimations,
            0);

        plan.Should().Be(new AnimationPaneRemoveMutationPlan(
            true,
            0,
            0,
            "Remove animation 1",
            null));
        AnimationPanePlanner.TryApplyRemoveMutation(editor, plan).Should().BeTrue();
        editor.CurrentSlideAnimations.Select(animation => animation.ShapeId)
            .Should()
            .Equal(20u);

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
    public void RepeatMutationPlan_ParsesFiniteAndIndefiniteValues()
    {
        var slide = CreateSlideWithTimelineAnimations();
        slide.Animations[1].RepeatCount = 2;

        var finite = AnimationPanePlanner.BuildRepeatMutationPlan(
            slide.Animations, 1, "3", autoReverse: true);
        finite.Should().Be(new AnimationPaneRepeatMutationPlan(
            true, 1, 3, false, true, "3", null));

        var indefinite = AnimationPanePlanner.BuildRepeatMutationPlan(
            slide.Animations, 1, "Indefinitely", autoReverse: false);
        indefinite.RepeatIndefinitely.Should().BeTrue();
        indefinite.RepeatCount.Should().BeNull();
        indefinite.AutoReverse.Should().BeFalse();

        AnimationPanePlanner.BuildRepeatMutationPlan(slide.Animations, 1, "0", false)
            .DisabledReason.Should().NotBeNullOrWhiteSpace();
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
            new AnimationPaneEffectOptionDescriptor("from-top", "From Top", AnimationDirection.FromTop, false),
            new AnimationPaneEffectOptionDescriptor("from-top-left", "From Top Left", AnimationDirection.FromTopLeft, false),
            new AnimationPaneEffectOptionDescriptor("from-top-right", "From Top Right", AnimationDirection.FromTopRight, false),
            new AnimationPaneEffectOptionDescriptor("from-bottom-left", "From Bottom Left", AnimationDirection.FromBottomLeft, false),
            new AnimationPaneEffectOptionDescriptor("from-bottom-right", "From Bottom Right", AnimationDirection.FromBottomRight, false));
    }

    [Fact]
    public void SplitEffectOptions_ExposeFourPowerPointDirectionsAndSelectLegacyAlias()
    {
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 10u,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Split,
            Direction = AnimationDirection.Horizontal,
        });

        var plan = AnimationPanePlanner.BuildEffectOptionsPlan(slide.Animations, 0);

        plan.Options.Select(option => option.DisplayText)
            .Should().Equal("Horizontal In", "Horizontal Out", "Vertical In", "Vertical Out");
        plan.SelectedOptionText.Should().Be("Horizontal Out");
        plan.Options.Should().ContainSingle(option =>
            option.IsSelected && option.Direction == AnimationDirection.HorizontalOut);

        var mutation = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            slide.Animations, 0, "vertical-in");
        mutation.ShouldApply.Should().BeTrue();
        mutation.Direction.Should().Be(AnimationDirection.VerticalIn);
    }

    [Theory]
    [InlineData("from-top-left", AnimationDirection.FromTopLeft)]
    [InlineData("from-top-right", AnimationDirection.FromTopRight)]
    [InlineData("from-bottom-left", AnimationDirection.FromBottomLeft)]
    [InlineData("from-bottom-right", AnimationDirection.FromBottomRight)]
    public void FlyInEffectOptionMutation_UpdatesDiagonalDirection(
        string optionId,
        AnimationDirection expectedDirection)
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = presentation.Slides[0].Shapes[0].Id,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.FlyIn,
            Direction = AnimationDirection.FromLeft,
        });

        var plan = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            editor.CurrentSlideAnimations,
            0,
            optionId);

        plan.ShouldApply.Should().BeTrue();
        plan.Direction.Should().Be(expectedDirection);
        AnimationPanePlanner.TryApplyEffectOptionMutation(editor, plan).Should().BeTrue();
        editor.CurrentSlideAnimations[0].Direction.Should().Be(expectedDirection);
    }

    [Fact]
    public void BuildEffectOptionsPlan_ProjectsWheelSpokeVariantsAndPreservesAuthoredCount()
    {
        var animations = new List<ShapeAnimation>
        {
            new()
            {
                Preset = AnimationPreset.Wheel,
                WheelSpokeCount = 6,
            }
        };

        var plan = AnimationPanePlanner.BuildEffectOptionsPlan(animations, 0);

        plan.CanApply.Should().BeTrue();
        plan.WheelSpokeOptions.Select(option => option.DisplayText)
            .Should()
            .Equal("1 spoke", "2 spokes", "3 spokes", "4 spokes", "6 spokes", "8 spokes");
        plan.WheelSpokeOptions.Should().ContainSingle(option =>
            option.IsSelected
            && option.Id == "spokes-6"
            && option.WheelSpokeCount == 6);
    }

    [Fact]
    public void BuildEffectOptionMutationPlan_UpdatesWheelSpokeCount()
    {
        var animations = new List<ShapeAnimation>
        {
            new()
            {
                Preset = AnimationPreset.Wheel,
                WheelSpokeCount = 4,
            }
        };

        var plan = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            animations,
            0,
            "spokes-8");

        plan.ShouldApply.Should().BeTrue();
        plan.WheelSpokeCount.Should().Be(8);
        plan.DisplayText.Should().Be("8 spokes");
    }

    [Theory]
    [InlineData(AnimationPreset.Blinds, AnimationDirection.Vertical, "Horizontal,Vertical", "Vertical")]
    [InlineData(AnimationPreset.Checkerboard, AnimationDirection.Horizontal, "Horizontal,Vertical", "Horizontal")]
    [InlineData(AnimationPreset.Wave, AnimationDirection.Vertical, "Horizontal,Vertical", "Vertical")]
    [InlineData(AnimationPreset.Box, AnimationDirection.Out, "In,Out", "Out")]
    [InlineData(AnimationPreset.Circle, AnimationDirection.In, "In,Out", "In")]
    [InlineData(AnimationPreset.Diamond, AnimationDirection.Out, "In,Out", "Out")]
    [InlineData(AnimationPreset.Plus, AnimationDirection.In, "In,Out", "In")]
    [InlineData(AnimationPreset.Wedge, AnimationDirection.Out, "In,Out", "Out")]
    [InlineData(AnimationPreset.Wheel, AnimationDirection.In, "In,Out", "In")]
    [InlineData(AnimationPreset.Peek, AnimationDirection.FromTop, "From Bottom,From Left,From Right,From Top", "From Top")]
    [InlineData(AnimationPreset.Crawl, AnimationDirection.FromRight, "From Bottom,From Left,From Right,From Top", "From Right")]
    [InlineData(AnimationPreset.Bounce, AnimationDirection.FromBottom, "From Bottom,From Left,From Right,From Top", "From Bottom")]
    [InlineData(AnimationPreset.Float, AnimationDirection.FromTop, "From Bottom,From Left,From Right,From Top", "From Top")]
    [InlineData(AnimationPreset.Swoop, AnimationDirection.FromRight, "From Bottom,From Left,From Right,From Top", "From Right")]
    [InlineData(AnimationPreset.Boomerang, AnimationDirection.FromLeft, "From Bottom,From Left,From Right,From Top", "From Left")]
    [InlineData(AnimationPreset.Strips, AnimationDirection.LeftDown, "Left Up,Left Down,Right Up,Right Down", "Left Down")]
    public void BuildEffectOptionsPlan_ProjectsAdvancedImportedEffectOptions(
        AnimationPreset preset,
        AnimationDirection direction,
        string expectedLabelsCsv,
        string expectedSelected)
    {
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 10u,
            Kind = AnimationKind.Entrance,
            Preset = preset,
            Direction = direction,
        });

        var plan = AnimationPanePlanner.BuildEffectOptionsPlan(slide.Animations, 0);

        plan.CanApply.Should().BeTrue();
        plan.SelectedOptionText.Should().Be(expectedSelected);
        plan.Options.Select(option => option.DisplayText)
            .Should()
            .Equal(expectedLabelsCsv.Split(','));
        plan.Options.Should().ContainSingle(option =>
            option.DisplayText == expectedSelected && option.IsSelected);
    }

    [Fact]
    public void BuildEffectOptionsPlan_ProjectsGrowWithColorAmountAndAppliesIt()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = presentation.Slides[0].Shapes[0].Id,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.GrowWithColor,
            ScaleBehavior = AnimationScaleBehavior.FromTo(1.5),
        });

        var options = AnimationPanePlanner.BuildEffectOptionsPlan(editor.CurrentSlideAnimations, 0);

        options.CanApply.Should().BeTrue();
        options.SelectedOptionText.Should().Be("Larger (150%)");
        options.Options.Should().ContainSingle(option =>
            option.Id == "amount-150" && option.IsSelected);

        var mutation = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            editor.CurrentSlideAnimations,
            0,
            "amount-400");

        mutation.ShouldApply.Should().BeTrue();
        AnimationPanePlanner.TryApplyEffectOptionMutation(editor, mutation).Should().BeTrue();
        editor.CurrentSlideAnimations[0].ScaleBehavior!.ToX
            .Should().Be(AnimationScaleBehavior.Format(4));
    }

    [Fact]
    public void NativeChangeFontSizeAmountEditPreservesNumericBehavior()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = presentation.Slides[0].Shapes[0].Id,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Grow,
            RawPresetClass = "emph",
            RawPresetId = 4,
            RawPresetSubtype = "2",
            PreservedNumericBehaviorXml = """
                <p:anim xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" valueType="num" to="1.5">
                  <p:cBhvr><p:attrNameLst><p:attrName>style.fontSize</p:attrName></p:attrNameLst></p:cBhvr>
                </p:anim>
                """
        });

        var options = AnimationPanePlanner.BuildEffectOptionsPlan(editor.CurrentSlideAnimations, 0);

        options.CanApply.Should().BeTrue();
        options.SelectedOptionText.Should().Be("Larger (150%)");
        options.Options.Should().HaveCount(4);
        options.Options.Should().OnlyContain(option =>
            option.PreservedNumericBehaviorXml != null);

        AnimationPanePlanner.BuildEffectOptionMutationPlan(
            editor.CurrentSlideAnimations,
            0,
            "amount-150").ShouldApply.Should().BeFalse();

        var mutation = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            editor.CurrentSlideAnimations,
            0,
            "amount-400");

        mutation.ShouldApply.Should().BeTrue();
        AnimationPanePlanner.TryApplyEffectOptionMutation(editor, mutation).Should().BeTrue();
        SlideShowPlaybackPlanner.ResolveFontSizeBehavior(editor.CurrentSlideAnimations[0])!
            .Multiplier.Should().Be(4);
        editor.CurrentSlideAnimations[0].PreservedNumericBehaviorXml
            .Should().Contain("to=\"4\"")
            .And.NotContain("animScale");

        using var output = new MemoryStream();
        PptxPackageWriter.Write(presentation, output);
        using var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        using var slideReader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(slideReader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "4");
        cTn.Descendants(p + "anim").Single().Attribute("to")!.Value.Should().Be("4");
        cTn.Descendants(p + "animScale").Should().BeEmpty();

        editor.Undo();
        SlideShowPlaybackPlanner.ResolveFontSizeBehavior(editor.CurrentSlideAnimations[0])!
            .Multiplier.Should().Be(1.5);
    }

    [Fact]
    public void BuildEffectOptionsPlan_ProjectsPulseAmountAndAppliesIt()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = presentation.Slides[0].Shapes[0].Id,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Pulse,
            ScaleBehavior = AnimationScaleBehavior.FromTo(1.5),
        });

        var options = AnimationPanePlanner.BuildEffectOptionsPlan(editor.CurrentSlideAnimations, 0);

        options.CanApply.Should().BeTrue();
        options.SelectedOptionText.Should().Be("Larger (150%)");
        options.Options.Should().ContainSingle(option =>
            option.Id == "amount-150" && option.IsSelected);

        var mutation = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            editor.CurrentSlideAnimations,
            0,
            "amount-400");

        mutation.ShouldApply.Should().BeTrue();
        AnimationPanePlanner.TryApplyEffectOptionMutation(editor, mutation).Should().BeTrue();
        editor.CurrentSlideAnimations[0].ScaleBehavior!.ToX
            .Should().Be(AnimationScaleBehavior.Format(4));
    }

    [Fact]
    public void BuildEffectOptionsPlan_ProjectsSpinAmountAndPreservesSelection()
    {
        var animations = new[]
        {
            new ShapeAnimation
            {
                ShapeId = 10u,
                Kind = AnimationKind.Emphasis,
                Preset = AnimationPreset.Spin,
                EffectSubtype = "halfSpin",
            },
        };

        var plan = AnimationPanePlanner.BuildEffectOptionsPlan(animations, 0);

        plan.CanApply.Should().BeTrue();
        plan.Options.Select(option => option.DisplayText)
            .Should().Equal("Quarter Spin", "Half Spin", "Full Spin", "Two Spins");
        plan.SelectedOptionText.Should().Be("Half Spin");
        plan.Options.Should().ContainSingle(option => option.IsSelected && option.EffectSubtype == "halfSpin");
    }

    [Theory]
    [InlineData(AnimationPreset.Grow, 1.5, "Larger (150%)")]
    [InlineData(AnimationPreset.Shrink, 0.5, "Smaller (50%)")]
    public void BuildEffectOptionsPlan_ProjectsPowerPointGrowShrinkAmounts(
        AnimationPreset preset,
        double scale,
        string selectedText)
    {
        var animations = new[]
        {
            new ShapeAnimation
            {
                ShapeId = 10u,
                Kind = AnimationKind.Emphasis,
                Preset = preset,
                ScaleBehavior = AnimationScaleBehavior.FromTo(scale),
            },
        };

        var plan = AnimationPanePlanner.BuildEffectOptionsPlan(animations, 0);

        plan.Options.Select(option => option.DisplayText)
            .Should().Equal("Tiny (25%)", "Smaller (50%)", "Larger (150%)", "Huge (400%)");
        plan.SelectedOptionText.Should().Be(selectedText);
        plan.Options.Should().ContainSingle(option => option.IsSelected && option.ScaleBehavior!.ToX == AnimationScaleBehavior.Format(scale));
    }

    [Fact]
    public void GrowShrinkCustomScale_IsShownHonestlyAndMutationIsUndoable()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = presentation.Slides[0].Shapes[0].Id,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Grow,
            ScaleBehavior = AnimationScaleBehavior.FromTo(1.35),
        });

        var options = AnimationPanePlanner.BuildEffectOptionsPlan(editor.CurrentSlideAnimations, 0);
        options.SelectedOptionText.Should().Be("Custom (135%)");
        options.Options.Should().ContainSingle(option =>
            option.IsSelected && option.ScaleBehavior!.ToX == "135000" && option.DisplayText == "Custom (135%)");

        var mutation = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            editor.CurrentSlideAnimations, 0, "amount-400");
        AnimationPanePlanner.TryApplyEffectOptionMutation(editor, mutation).Should().BeTrue();
        editor.CurrentSlideAnimations[0].ScaleBehavior!.ToX.Should().Be("400000");
        editor.Undo();
        editor.CurrentSlideAnimations[0].ScaleBehavior!.ToX.Should().Be("135000");
    }

    [Theory]
    [InlineData(AnimationPreset.Spiral, AnimationDirection.Out, "Out")]
    [InlineData(AnimationPreset.Swivel, AnimationDirection.In, "In")]
    public void BuildEffectOptionsPlan_ProjectsSpiralAndSwivelInOutOptions(
        AnimationPreset preset,
        AnimationDirection direction,
        string expectedSelected)
    {
        var animations = new[]
        {
            new ShapeAnimation
            {
                ShapeId = 10u,
                Kind = AnimationKind.Entrance,
                Preset = preset,
                Direction = direction,
            },
        };

        var plan = AnimationPanePlanner.BuildEffectOptionsPlan(animations, 0);

        plan.CanApply.Should().BeTrue();
        plan.Options.Select(option => option.DisplayText).Should().Equal("In", "Out");
        plan.SelectedOptionText.Should().Be(expectedSelected);
        plan.Options.Should().ContainSingle(option => option.IsSelected && option.DisplayText == expectedSelected);
    }

    [Theory]
    [InlineData(AnimationPreset.Wipe, "from-top", AnimationDirection.FromTop)]
    [InlineData(AnimationPreset.Zoom, "out", AnimationDirection.Out)]
    [InlineData(AnimationPreset.Split, "vertical-in", AnimationDirection.VerticalIn)]
    [InlineData(AnimationPreset.RandomBars, "horizontal", AnimationDirection.Horizontal)]
    [InlineData(AnimationPreset.Blinds, "vertical", AnimationDirection.Vertical)]
    [InlineData(AnimationPreset.Checkerboard, "horizontal", AnimationDirection.Horizontal)]
    [InlineData(AnimationPreset.Wave, "vertical", AnimationDirection.Vertical)]
    [InlineData(AnimationPreset.Circle, "out", AnimationDirection.Out)]
    [InlineData(AnimationPreset.Wheel, "in", AnimationDirection.In)]
    [InlineData(AnimationPreset.Spiral, "out", AnimationDirection.Out)]
    [InlineData(AnimationPreset.Swivel, "in", AnimationDirection.In)]
    [InlineData(AnimationPreset.Peek, "from-left", AnimationDirection.FromLeft)]
    [InlineData(AnimationPreset.Crawl, "from-top", AnimationDirection.FromTop)]
    [InlineData(AnimationPreset.Bounce, "from-bottom", AnimationDirection.FromBottom)]
    [InlineData(AnimationPreset.Float, "from-top", AnimationDirection.FromTop)]
    [InlineData(AnimationPreset.Swoop, "from-right", AnimationDirection.FromRight)]
    [InlineData(AnimationPreset.Boomerang, "from-left", AnimationDirection.FromLeft)]
    [InlineData(AnimationPreset.Strips, "right-up", AnimationDirection.RightUp)]
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
    public void MotionPathEffectOptions_ExposeReversePathAndReverseCubicGeometry()
    {
        var slide = new Slide();
        var motion = new MotionPath { Origin = "parent", PtsTypes = "F" };
        motion.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        motion.Segments.Add(MotionPathSegment.LineTo(0.25, 0.1));
        motion.Segments.Add(MotionPathSegment.CubicTo(0.4, 0.2, 0.6, 0.3, 0.8, 0.5));
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 10u,
            Kind = AnimationKind.Motion,
            Motion = motion,
        });

        var options = AnimationPanePlanner.BuildEffectOptionsPlan(slide.Animations, 0);
        options.CanApply.Should().BeTrue();
        options.Options.Should().ContainSingle(option =>
            option.Id == "reverse-path"
            && option.ReversesMotionPath
            && option.IsSelected);

        var mutation = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            slide.Animations,
            0,
            "reverse-path");
        mutation.ShouldApply.Should().BeTrue();
        mutation.ReversesMotionPath.Should().BeTrue();

        var reversed = MotionPath.ReversedClone(motion);
        reversed.Segments.Select(segment => segment.Kind)
            .Should()
            .Equal(
                MotionPathSegmentKind.Move,
                MotionPathSegmentKind.Cubic,
                MotionPathSegmentKind.Line);
        reversed.Segments[0].X.Should().BeApproximately(0.8, 1e-9);
        reversed.Segments[0].Y.Should().BeApproximately(0.5, 1e-9);
        reversed.Segments[1].X1.Should().BeApproximately(0.6, 1e-9);
        reversed.Segments[1].Y1.Should().BeApproximately(0.3, 1e-9);
        reversed.Segments[1].X2.Should().BeApproximately(0.4, 1e-9);
        reversed.Segments[1].Y2.Should().BeApproximately(0.2, 1e-9);
        reversed.Segments[1].X.Should().BeApproximately(0.25, 1e-9);
        reversed.Segments[1].Y.Should().BeApproximately(0.1, 1e-9);
    }

    [Fact]
    public void TryApplyMotionPathReverse_UsesAnimationCommandAndSupportsUndo()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var motion = new MotionPath();
        motion.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        motion.Segments.Add(MotionPathSegment.LineTo(0.5, 0.25));
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = presentation.Slides[0].Shapes[0].Id,
            Kind = AnimationKind.Motion,
            Motion = motion,
        });

        var plan = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            editor.CurrentSlideAnimations,
            0,
            "reverse-path");

        AnimationPanePlanner.TryApplyEffectOptionMutation(editor, plan).Should().BeTrue();
        editor.CurrentSlideAnimations[0].Motion!.Segments[0].X.Should().BeApproximately(0.5, 1e-9);
        editor.CurrentSlideAnimations[0].Motion!.Segments[0].Y.Should().BeApproximately(0.25, 1e-9);

        editor.Undo();
        editor.CurrentSlideAnimations[0].Motion!.Segments[0].X.Should().Be(0);
        editor.CurrentSlideAnimations[0].Motion!.Segments[0].Y.Should().Be(0);
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
    public void TryApplyEffectOptionMutation_UpdatesWheelSpokeCountAndSupportsUndo()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = presentation.Slides[0].Shapes[0].Id,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Wheel,
            WheelSpokeCount = 4,
        });

        var plan = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            editor.CurrentSlideAnimations,
            0,
            "spokes-8");

        AnimationPanePlanner.TryApplyEffectOptionMutation(editor, plan).Should().BeTrue();
        editor.CurrentSlideAnimations[0].WheelSpokeCount.Should().Be(8);
        editor.Undo();
        editor.CurrentSlideAnimations[0].WheelSpokeCount.Should().Be(4);
    }

    [Fact]
    public void TryApplyEffectOptionMutation_UpdatesSpinAmountAndSupportsUndo()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = presentation.Slides[0].Shapes[0].Id,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Spin,
            EffectSubtype = "quarterSpin",
        });

        var plan = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            editor.CurrentSlideAnimations,
            0,
            "two-spins");

        AnimationPanePlanner.TryApplyEffectOptionMutation(editor, plan).Should().BeTrue();
        editor.CurrentSlideAnimations[0].EffectSubtype.Should().Be("twoSpins");
        editor.CurrentSlideAnimations[0].Direction.Should().BeNull();
        editor.Undo();
        editor.CurrentSlideAnimations[0].EffectSubtype.Should().Be("quarterSpin");
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

    [Fact]
    public void BuildPlaybackSessionPlan_PlayFromSelectedScopesToSelectedTriggerSequence()
    {
        var slide = new Slide();
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 10u,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 300,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 20u,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick,
            TriggerShapeId = 99u,
            DurationMs = 400,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 30u,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Pulse,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 250,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 21u,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.FlyIn,
            Trigger = AnimationTrigger.WithPrevious,
            TriggerShapeId = 99u,
            DurationMs = 500,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 22u,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Zoom,
            Trigger = AnimationTrigger.OnClick,
            TriggerShapeId = 99u,
            DurationMs = 200,
        });

        var timeline = AnimationPanePlanner.BuildTimelinePlan(
            slide,
            selectedAnimationIndex: 1,
            displayCulture: Invariant);
        var session = AnimationPanePlanner.BuildPlaybackSessionPlan(
            timeline,
            AnimationPanePlaybackControlKind.PlayFromSelected);

        session.Segments.Select(segment => segment.ShapeId).Should().Equal(20u, 21u, 22u);
        session.Segments.Should().NotContain(segment => segment.ShapeId == 30u);
        session.Segments.Select(segment => segment.RelativeStartMs).Should().Equal(0, 0, 500);
        session.Segments.Select(segment => segment.RelativeEndMs).Should().Equal(400, 500, 700);
        session.TotalDurationMs.Should().Be(700);
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
