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
