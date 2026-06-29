using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowHostPlannerTests
{
    [Theory]
    [InlineData("Escape", SlideShowHostIntent.Close)]
    [InlineData("Right", SlideShowHostIntent.Advance)]
    [InlineData("Space", SlideShowHostIntent.Advance)]
    [InlineData("PageDown", SlideShowHostIntent.Advance)]
    [InlineData("Enter", SlideShowHostIntent.Advance)]
    [InlineData("Return", SlideShowHostIntent.Advance)]
    [InlineData("Left", SlideShowHostIntent.Back)]
    [InlineData("PageUp", SlideShowHostIntent.Back)]
    [InlineData("Back", SlideShowHostIntent.Back)]
    [InlineData("Home", SlideShowHostIntent.FirstSlide)]
    [InlineData("End", SlideShowHostIntent.LastSlide)]
    [InlineData("Tab", SlideShowHostIntent.None)]
    public void IntentFromKeyName_MapsHostKeysToSharedIntent(string keyName, SlideShowHostIntent expected)
    {
        SlideShowHostPlanner.IntentFromKeyName(keyName).Should().Be(expected);
    }

    [Fact]
    public void PlanKey_FirstAndLastSlide_OwnsNoSlideAndJumpPolicy()
    {
        var pres = MakePresentation(3);
        var controller = new SlideShowController(pres.Slides, startIndex: 1);

        var first = SlideShowHostPlanner.PlanKey("Home", controller, pres.Slides);
        first.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        first.SlideIndex.Should().Be(0);
        first.AnimateSlide.Should().BeFalse();
        first.StopAutoAdvance.Should().BeTrue();
        controller.CurrentSlideIndex.Should().Be(0);

        var last = SlideShowHostPlanner.PlanKey("End", controller, pres.Slides);
        last.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        last.SlideIndex.Should().Be(2);
        last.AnimateSlide.Should().BeFalse();
        last.StopAutoAdvance.Should().BeTrue();
        controller.CurrentSlideIndex.Should().Be(2);

        var empty = Presentation.CreateEmpty();
        empty.Slides.Clear();
        var emptyController = new SlideShowController(empty.Slides, startIndex: 0);

        var emptyLast = SlideShowHostPlanner.PlanKey("End", emptyController, empty.Slides);
        emptyLast.Kind.Should().Be(SlideShowHostCommandKind.None);
        emptyLast.IsHandled.Should().BeTrue();
        emptyLast.StopAutoAdvance.Should().BeTrue();
        emptyController.CurrentSlideIndex.Should().Be(-1);
    }

    [Fact]
    public void PlanAdvanceAndBack_TranslateControllerResultsToHostCommands()
    {
        var pres = MakePresentation(2);
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 99,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400
        });
        pres.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 99,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick
        });

        var controller = new SlideShowController(pres.Slides, startIndex: 0);

        var step = SlideShowHostPlanner.PlanAdvance(controller, stopAutoAdvance: true);
        step.Kind.Should().Be(SlideShowHostCommandKind.PlayAnimationStep);
        step.Step.Should().NotBeNull();
        step.AdvanceResult.Should().BeOfType<AdvanceResult.PlayStep>();
        step.StopAutoAdvance.Should().BeTrue();

        var next = SlideShowHostPlanner.PlanAdvance(controller, stopAutoAdvance: true);
        next.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        next.SlideIndex.Should().Be(1);
        next.AnimateSlide.Should().BeTrue();
        next.AdvanceResult.Should().BeOfType<AdvanceResult.NavigateToSlide>();

        var back = SlideShowHostPlanner.PlanBack(controller, stopAutoAdvance: true);
        back.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        back.SlideIndex.Should().Be(0);
        back.AnimateSlide.Should().BeTrue();
        back.BackResult.Should().BeOfType<BackResult.NavigateToSlide>();
    }

    [Fact]
    public void PlanAdvance_AtEnd_ClosesHost()
    {
        var pres = MakePresentation(1);
        var controller = new SlideShowController(pres.Slides, startIndex: 0);

        var command = SlideShowHostPlanner.PlanAdvance(controller, stopAutoAdvance: true);

        command.Kind.Should().Be(SlideShowHostCommandKind.Close);
        command.IsHandled.Should().BeTrue();
        command.StopAutoAdvance.Should().BeTrue();
        command.AdvanceResult.Should().BeOfType<AdvanceResult.AtEnd>();
    }

    [Fact]
    public void BuildDisplayPlan_OwnsSlideMetricsTransitionAndAutoAdvancePolicy()
    {
        var pres = MakePresentation(1);
        pres.SlideSizeCxEmu = 9144000;
        pres.SlideSizeCyEmu = 6858000;
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind = TransitionKind.Fade,
            DurationMs = 400,
            AdvanceAfterMs = 2500
        };

        var controller = new SlideShowController(pres.Slides, startIndex: 0);

        var animated = SlideShowHostPlanner.BuildDisplayPlan(pres, controller, animated: true);
        animated.Metrics.WidthDip.Should().Be(960);
        animated.Metrics.HeightDip.Should().Be(720);
        animated.Transition.Should().BeSameAs(pres.Slides[0].Transition);
        animated.AutoAdvanceAfterMs.Should().Be(2500);

        var instant = SlideShowHostPlanner.BuildDisplayPlan(pres, controller, animated: false);
        instant.Transition.Should().BeNull();
        instant.AutoAdvanceAfterMs.Should().Be(2500);
    }

    [Fact]
    public void BuildState_FormatsSharedStatusText()
    {
        var pres = MakePresentation(3);
        var controller = new SlideShowController(pres.Slides, startIndex: 1);

        var state = SlideShowHostPlanner.BuildState(controller, pres.Slides.Count);

        state.HasSlides.Should().BeTrue();
        state.IsFirstSlide.Should().BeFalse();
        state.IsLastSlide.Should().BeFalse();
        state.StatusText.Should().Be("Slide 2 of 3");

        var empty = Presentation.CreateEmpty();
        empty.Slides.Clear();
        var emptyState = SlideShowHostPlanner.BuildState(
            new SlideShowController(empty.Slides, 0),
            empty.Slides.Count);
        emptyState.StatusText.Should().Be(SlideShowHostPlanner.NoSlidesStatusText);
    }

    [Fact]
    public void HitTesting_UsesSharedSlideGeometryForTriggersAndHyperlinks()
    {
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Hyperlink = new Hyperlink { Url = "https://example.com" }
        };
        slide.Shapes.Add(shape);
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 100,
            TriggerShapeId = 42,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick
        });

        var slidePoint = SlideShowHostPlanner.MapCanvasPointToSlide(
            canvasX: 48,
            canvasY: 48,
            canvasWidth: 960,
            canvasHeight: 540,
            new SlideShowSlideMetrics(960, 540));

        SlideShowHostPlanner.HitTestHyperlink(slide, slidePoint)
            .Should().BeSameAs(shape.Hyperlink);
        SlideShowHostPlanner.HitTestTriggerShape(slide, slidePoint)
            .Should().Be(42);
    }

    [Fact]
    public void PlanInternalSlideJump_NavigatesBySlideIdWithoutHostLookup()
    {
        var pres = MakePresentation(3);
        var controller = new SlideShowController(pres.Slides, startIndex: 0);

        var command = SlideShowHostPlanner.PlanInternalSlideJump(
            controller,
            pres.Slides,
            pres.Slides[2].Id);

        command.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        command.SlideIndex.Should().Be(2);
        command.AnimateSlide.Should().BeFalse();
        command.StopAutoAdvance.Should().BeTrue();
        controller.CurrentSlideIndex.Should().Be(2);

        var missing = SlideShowHostPlanner.PlanInternalSlideJump(
            controller,
            pres.Slides,
            "missing");
        missing.Kind.Should().Be(SlideShowHostCommandKind.None);
        missing.IsHandled.Should().BeTrue();
    }

    private static Presentation MakePresentation(int slideCount)
    {
        var pres = Presentation.CreateEmpty();
        while (pres.Slides.Count < slideCount)
        {
            pres.Slides.Add(new Slide { Title = $"Slide {pres.Slides.Count + 1}" });
        }

        while (pres.Slides.Count > slideCount)
        {
            pres.Slides.RemoveAt(pres.Slides.Count - 1);
        }

        return pres;
    }
}
