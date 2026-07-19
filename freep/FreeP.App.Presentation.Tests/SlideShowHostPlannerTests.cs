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

    [Theory]
    [InlineData(TransitionKind.None, SlideShowTransitionPlaybackKind.Cut)]
    [InlineData(TransitionKind.Cut, SlideShowTransitionPlaybackKind.Cut)]
    [InlineData(TransitionKind.Fade, SlideShowTransitionPlaybackKind.Fade)]
    [InlineData(TransitionKind.Dissolve, SlideShowTransitionPlaybackKind.Dissolve)]
    [InlineData(TransitionKind.Box, SlideShowTransitionPlaybackKind.Box)]
    [InlineData(TransitionKind.Reveal, SlideShowTransitionPlaybackKind.Reveal)]
    [InlineData(TransitionKind.Wipe, SlideShowTransitionPlaybackKind.Reveal)]
    [InlineData(TransitionKind.Flash, SlideShowTransitionPlaybackKind.Fade)]
    [InlineData(TransitionKind.Split, SlideShowTransitionPlaybackKind.Split)]
    [InlineData(TransitionKind.Blinds, SlideShowTransitionPlaybackKind.Blinds)]
    [InlineData(TransitionKind.RandomBar, SlideShowTransitionPlaybackKind.RandomBars)]
    [InlineData(TransitionKind.Wheel, SlideShowTransitionPlaybackKind.Wheel)]
    [InlineData(TransitionKind.WheelReverse, SlideShowTransitionPlaybackKind.Wheel)]
    [InlineData(TransitionKind.Zoom, SlideShowTransitionPlaybackKind.Zoom)]
    [InlineData(TransitionKind.Push, SlideShowTransitionPlaybackKind.PushLike)]
    [InlineData(TransitionKind.Cover, SlideShowTransitionPlaybackKind.PushLike)]
    [InlineData(TransitionKind.Uncover, SlideShowTransitionPlaybackKind.PushLike)]
    [InlineData(TransitionKind.Gallery, SlideShowTransitionPlaybackKind.PushLike)]
    [InlineData(TransitionKind.Conveyor, SlideShowTransitionPlaybackKind.PushLike)]
    [InlineData(TransitionKind.Pan, SlideShowTransitionPlaybackKind.PushLike)]
    [InlineData(TransitionKind.Comb, SlideShowTransitionPlaybackKind.PushLike)]
    [InlineData(TransitionKind.Doors, SlideShowTransitionPlaybackKind.Split)]
    [InlineData(TransitionKind.Window, SlideShowTransitionPlaybackKind.PushLike)]
    [InlineData(TransitionKind.Morph, SlideShowTransitionPlaybackKind.FadeFallback)]
    [InlineData(TransitionKind.Cube, SlideShowTransitionPlaybackKind.FadeFallback)]
    [InlineData(TransitionKind.Fly, SlideShowTransitionPlaybackKind.FadeFallback)]
    [InlineData(TransitionKind.Other, SlideShowTransitionPlaybackKind.FadeFallback)]
    public void PlanTransition_GroupsKindsIntoRendererNeutralPlayback(
        TransitionKind kind,
        SlideShowTransitionPlaybackKind expected)
    {
        var plan = SlideShowTransitionPlanner.Plan(new SlideTransition { Kind = kind });

        plan.PlaybackKind.Should().Be(expected);
    }

    [Theory]
    [InlineData(TransitionDirection.Right, -1, 0)]
    [InlineData(TransitionDirection.Left, 1, 0)]
    [InlineData(TransitionDirection.Down, 0, -1)]
    [InlineData(TransitionDirection.Up, 0, 1)]
    [InlineData(TransitionDirection.Vertical, 1, 0)]
    public void PlanTransition_ResolvesPushIncomingOffsets(
        TransitionDirection direction,
        double expectedX,
        double expectedY)
    {
        var plan = SlideShowTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Push,
            Direction = direction
        });

        plan.IncomingOffsetX.Should().Be(expectedX);
        plan.IncomingOffsetY.Should().Be(expectedY);
    }

    [Theory]
    [InlineData(TransitionDirection.Horizontal, true, true)]
    [InlineData(TransitionDirection.Vertical, false, true)]
    [InlineData(TransitionDirection.In, true, false)]
    [InlineData(TransitionDirection.Out, true, true)]
    public void PlanTransition_ResolvesSplitAxisAndDirection(
        TransitionDirection direction,
        bool expectedHorizontal,
        bool expectedOut)
    {
        var plan = SlideShowTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Split,
            Direction = direction
        });

        plan.PlaybackKind.Should().Be(SlideShowTransitionPlaybackKind.Split);
        plan.SplitHorizontal.Should().Be(expectedHorizontal);
        plan.SplitOut.Should().Be(expectedOut);
    }

    [Theory]
    [InlineData(TransitionDirection.Horizontal, true)]
    [InlineData(TransitionDirection.Vertical, false)]
    public void PlanTransition_ResolvesBlindsAxis(
        TransitionDirection direction,
        bool expectedHorizontal)
    {
        var plan = SlideShowTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Blinds,
            Direction = direction
        });

        plan.PlaybackKind.Should().Be(SlideShowTransitionPlaybackKind.Blinds);
        plan.BlindsHorizontal.Should().Be(expectedHorizontal);
    }

    [Theory]
    [InlineData(TransitionDirection.Horizontal, true)]
    [InlineData(TransitionDirection.Vertical, false)]
    public void PlanTransition_ResolvesRandomBarsAxis(
        TransitionDirection direction,
        bool expectedHorizontal)
    {
        var plan = SlideShowTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.RandomBar,
            Direction = direction
        });

        plan.PlaybackKind.Should().Be(SlideShowTransitionPlaybackKind.RandomBars);
        plan.RandomBarsHorizontal.Should().Be(expectedHorizontal);
    }

    [Fact]
    public void PlanTransition_CoversEveryTransitionKind()
    {
        foreach (var kind in Enum.GetValues<TransitionKind>())
        {
            var plan = SlideShowTransitionPlanner.Plan(new SlideTransition { Kind = kind });

            Enum.IsDefined(plan.PlaybackKind).Should().BeTrue(kind.ToString());
        }
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
    public void BuildPresenterState_ExposesCurrentNextNotesElapsedAndDisplayIntent()
    {
        var pres = MakePresentation(3);
        pres.Slides[1].Notes = MakeTextBody("speaker notes\nsecond line");
        var controller = new SlideShowController(pres.Slides, startIndex: 1);
        var started = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        var now = started.AddSeconds(95);
        var displayIntent = new SlideShowPresenterDisplayIntent(
            IsFullScreenRequested: true,
            MonitorIndex: 1,
            MonitorName: "Presenter display");

        var state = SlideShowHostPlanner.BuildPresenterState(
            pres,
            controller,
            started,
            now,
            displayIntent);

        state.HostState.StatusText.Should().Be("Slide 2 of 3");
        state.CurrentSlide.Should().NotBeNull();
        state.CurrentSlide!.SlideIndex.Should().Be(1);
        state.CurrentSlide.Slide.Should().BeSameAs(pres.Slides[1]);
        state.NextSlide.Should().NotBeNull();
        state.NextSlide!.SlideIndex.Should().Be(2);
        state.NextSlide.Slide.Should().BeSameAs(pres.Slides[2]);
        state.NotesText.Should().Be("speaker notes\nsecond line");
        state.StartedAtUtc.Should().Be(started);
        state.Elapsed.Should().Be(TimeSpan.FromSeconds(95));
        state.DisplayIntent.Should().BeSameAs(displayIntent);
        state.ToolPlan.PointerInk.PointerMode.Should().Be(SlideShowPresenterPointerMode.Arrow);
    }

    [Fact]
    public void BuildPresenterState_HandlesEmptyDeckClampsNegativeElapsedAndAcceptsToolPlan()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides.Clear();
        var controller = new SlideShowController(pres.Slides, startIndex: 0);
        var started = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        var toolPlan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia,
            SlideShowPresenterPointerMode.Highlighter,
            "#ffee00",
            10,
            SlideShowInkRetentionDecision.ClearInk);

        var state = SlideShowHostPlanner.BuildPresenterState(
            pres,
            controller,
            started,
            started.AddSeconds(-1),
            toolPlan: toolPlan);

        state.HostState.StatusText.Should().Be(SlideShowHostPlanner.NoSlidesStatusText);
        state.CurrentSlide.Should().BeNull();
        state.NextSlide.Should().BeNull();
        state.NotesText.Should().BeEmpty();
        state.Elapsed.Should().Be(TimeSpan.Zero);
        state.DisplayIntent.Should().Be(SlideShowPresenterDisplayIntent.FullScreen);
        state.ToolPlan.Should().BeSameAs(toolPlan);
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
    public void PlanPointerClick_PrefersTriggerThenHyperlinkThenAdvance()
    {
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
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

        var inside = new SlideShowPoint(48, 48);
        var trigger = SlideShowHostPlanner.PlanPointerClick(slide, inside);
        trigger.Kind.Should().Be(SlideShowPointerClickIntentKind.Trigger);
        trigger.TriggerShapeId.Should().Be(42);
        trigger.Hyperlink.Should().BeNull();
        trigger.IsHandled.Should().BeTrue();

        slide.Animations.Clear();
        var hyperlink = SlideShowHostPlanner.PlanPointerClick(slide, inside);
        hyperlink.Kind.Should().Be(SlideShowPointerClickIntentKind.Hyperlink);
        hyperlink.Hyperlink.Should().BeSameAs(shape.Hyperlink);
        hyperlink.IsHandled.Should().BeTrue();

        var advance = SlideShowHostPlanner.PlanPointerClick(slide, new SlideShowPoint(900, 500));
        advance.Kind.Should().Be(SlideShowPointerClickIntentKind.Advance);
        advance.IsHandled.Should().BeFalse();
    }

    [Fact]
    public void PlanPointerClick_WithoutCurrentSlidePreservesAdvanceAndLeavesEventUnhandled()
    {
        var intent = SlideShowHostPlanner.PlanPointerClick(null, new SlideShowPoint(0, 0));

        intent.Kind.Should().Be(SlideShowPointerClickIntentKind.Advance);
        intent.IsHandled.Should().BeFalse();
    }

    [Fact]
    public void PlanPointerClick_Resolves_slide_zoom_before_hyperlink_fallback()
    {
        var presentation = MakePresentation(2);
        presentation.Slides[0].NumericId = 256;
        presentation.Slides[1].NumericId = 257;
        var shape = new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Zoom,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            PreservedObject = new PreservedObjectInfo
            {
                ObjectKind = PreservedObjectKind.Zoom,
                ZoomTargetSlideNumericId = 257,
            },
        };
        presentation.Slides[0].Shapes.Add(shape);

        var intent = SlideShowHostPlanner.PlanPointerClick(
            presentation.Slides[0],
            new SlideShowPoint(48, 48),
            presentation);

        intent.Kind.Should().Be(SlideShowPointerClickIntentKind.Zoom);
        intent.TargetSlideIndex.Should().Be(1);
        intent.IsHandled.Should().BeTrue();
    }

    [Fact]
    public void PlanZoomNavigation_jumps_controller_to_resolved_slide()
    {
        var presentation = MakePresentation(3);
        var controller = new SlideShowController(presentation.Slides, startIndex: 0);

        var command = SlideShowHostPlanner.PlanZoomNavigation(
            controller,
            presentation.Slides,
            targetSlideIndex: 2);

        command.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        command.SlideIndex.Should().Be(2);
        command.AnimateSlide.Should().BeFalse();
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

    private static TextBody MakeTextBody(string text)
    {
        var body = new TextBody();
        foreach (var line in text.Split('\n'))
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = line });
            body.Paragraphs.Add(paragraph);
        }

        return body;
    }
}
