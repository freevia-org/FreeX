using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowHostPlannerTests
{
    [Fact]
    public void KioskRestartPlanner_UsesPresentationMillisecondsOnlyForKioskMode()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.ShowType = PresentationShowType.BrowsedAtKiosk;
        presentation.KioskRestartAfterMilliseconds = 20_000;

        SlideShowKioskRestartPlanner.TryGetInterval(presentation, out var interval)
            .Should().BeTrue();
        interval.Should().Be(TimeSpan.FromSeconds(20));

        presentation.ShowType = PresentationShowType.PresentedBySpeaker;
        SlideShowKioskRestartPlanner.TryGetInterval(presentation, out _)
            .Should().BeFalse();
    }

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
    public void BuildDisplayPlan_UseSlideTimingsFalseDisablesAutomaticAdvance()
    {
        var pres = MakePresentation(1);
        pres.UseSlideTimings = false;
        pres.Slides[0].Transition = new SlideTransition { AdvanceAfterMs = 2500 };

        var plan = SlideShowHostPlanner.BuildDisplayPlan(
            pres,
            new SlideShowController(pres.Slides, startIndex: 0),
            animated: true);

        plan.AutoAdvanceAfterMs.Should().BeNull();
    }

    [Fact]
    public void SlideShowController_RespectsAnimationAndLoopSettings()
    {
        var pres = MakePresentation(2);
        pres.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 1,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick,
        });

        var noAnimation = new SlideShowController(
            pres.Slides,
            startIndex: 0,
            showWithAnimation: false);
        noAnimation.HasPendingSteps.Should().BeFalse();

        var loop = new SlideShowController(
            pres.Slides,
            startIndex: 0,
            showWithAnimation: false,
            loopUntilStopped: true);
        loop.Advance().Should().BeOfType<AdvanceResult.NavigateToSlide>()
            .Which.SlideIndex.Should().Be(1);
        loop.Advance().Should().BeOfType<AdvanceResult.NavigateToSlide>()
            .Which.SlideIndex.Should().Be(0);
        loop.IsAtEnd.Should().BeFalse();
    }

    [Theory]
    [InlineData(TransitionKind.None, SlideShowTransitionPlaybackKind.Cut)]
    [InlineData(TransitionKind.Cut, SlideShowTransitionPlaybackKind.Cut)]
    [InlineData(TransitionKind.Fade, SlideShowTransitionPlaybackKind.Fade)]
    [InlineData(TransitionKind.Dissolve, SlideShowTransitionPlaybackKind.Dissolve)]
    [InlineData(TransitionKind.Box, SlideShowTransitionPlaybackKind.Box)]
    [InlineData(TransitionKind.Reveal, SlideShowTransitionPlaybackKind.Reveal)]
    [InlineData(TransitionKind.Wipe, SlideShowTransitionPlaybackKind.Reveal)]
    [InlineData(TransitionKind.Uncover, SlideShowTransitionPlaybackKind.Uncover)]
    [InlineData(TransitionKind.Flash, SlideShowTransitionPlaybackKind.Flash)]
    [InlineData(TransitionKind.Split, SlideShowTransitionPlaybackKind.Split)]
    [InlineData(TransitionKind.Blinds, SlideShowTransitionPlaybackKind.Blinds)]
    [InlineData(TransitionKind.Comb, SlideShowTransitionPlaybackKind.Blinds)]
    [InlineData(TransitionKind.RandomBar, SlideShowTransitionPlaybackKind.RandomBars)]
    [InlineData(TransitionKind.Wheel, SlideShowTransitionPlaybackKind.Wheel)]
    [InlineData(TransitionKind.WheelReverse, SlideShowTransitionPlaybackKind.Wheel)]
    [InlineData(TransitionKind.Zoom, SlideShowTransitionPlaybackKind.Zoom)]
    [InlineData(TransitionKind.Push, SlideShowTransitionPlaybackKind.Push)]
    [InlineData(TransitionKind.Fly, SlideShowTransitionPlaybackKind.Push)]
    [InlineData(TransitionKind.Cover, SlideShowTransitionPlaybackKind.Cover)]
    [InlineData(TransitionKind.Gallery, SlideShowTransitionPlaybackKind.Gallery)]
    [InlineData(TransitionKind.Conveyor, SlideShowTransitionPlaybackKind.Conveyor)]
    [InlineData(TransitionKind.Pan, SlideShowTransitionPlaybackKind.Pan)]
    [InlineData(TransitionKind.Doors, SlideShowTransitionPlaybackKind.Split)]
    [InlineData(TransitionKind.Window, SlideShowTransitionPlaybackKind.Window)]
    [InlineData(TransitionKind.Morph, SlideShowTransitionPlaybackKind.Morph)]
    [InlineData(TransitionKind.Flip, SlideShowTransitionPlaybackKind.Flip)]
    [InlineData(TransitionKind.Cube, SlideShowTransitionPlaybackKind.Cube)]
    [InlineData(TransitionKind.Rotate, SlideShowTransitionPlaybackKind.Rotate)]
    [InlineData(TransitionKind.Honeycomb, SlideShowTransitionPlaybackKind.Honeycomb)]
    [InlineData(TransitionKind.Switch, SlideShowTransitionPlaybackKind.Switch)]
    [InlineData(TransitionKind.Orbit, SlideShowTransitionPlaybackKind.Orbit)]
    [InlineData(TransitionKind.Ferris, SlideShowTransitionPlaybackKind.Ferris)]
    [InlineData(TransitionKind.Flythrough, SlideShowTransitionPlaybackKind.Flythrough)]
    [InlineData(TransitionKind.Glitter, SlideShowTransitionPlaybackKind.Glitter)]
    [InlineData(TransitionKind.Ripple, SlideShowTransitionPlaybackKind.Ripple)]
    [InlineData(TransitionKind.Wind, SlideShowTransitionPlaybackKind.Wind)]
    [InlineData(TransitionKind.Curtains, SlideShowTransitionPlaybackKind.Curtains)]
    [InlineData(TransitionKind.Shred, SlideShowTransitionPlaybackKind.Shred)]
    [InlineData(TransitionKind.PeelOff, SlideShowTransitionPlaybackKind.PageCurl)]
    [InlineData(TransitionKind.Drape, SlideShowTransitionPlaybackKind.Drape)]
    [InlineData(TransitionKind.Airplane, SlideShowTransitionPlaybackKind.Flythrough)]
    [InlineData(TransitionKind.Origami, SlideShowTransitionPlaybackKind.PageCurl)]
    [InlineData(TransitionKind.Vortex, SlideShowTransitionPlaybackKind.Vortex)]
    [InlineData(TransitionKind.Warp, SlideShowTransitionPlaybackKind.Warp)]
    [InlineData(TransitionKind.Fracture, SlideShowTransitionPlaybackKind.Fracture)]
    [InlineData(TransitionKind.Crush, SlideShowTransitionPlaybackKind.Crush)]
    [InlineData(TransitionKind.Prism, SlideShowTransitionPlaybackKind.Prism)]
    [InlineData(TransitionKind.Prestige, SlideShowTransitionPlaybackKind.Prestige)]
    [InlineData(TransitionKind.PageCurlSingle, SlideShowTransitionPlaybackKind.PageCurl)]
    [InlineData(TransitionKind.PageCurlDouble, SlideShowTransitionPlaybackKind.PageCurl)]
    [InlineData(TransitionKind.Other, SlideShowTransitionPlaybackKind.FadeFallback)]
    public void PlanTransition_GroupsKindsIntoRendererNeutralPlayback(
        TransitionKind kind,
        SlideShowTransitionPlaybackKind expected)
    {
        var plan = SlideShowTransitionPlanner.Plan(new SlideTransition { Kind = kind });

        plan.PlaybackKind.Should().Be(expected);
    }

    [Fact]
    public void RandomCandidateKinds_ContainEachDedicatedPlaybackFamilyExactlyOnce()
    {
        var expected = new[]
        {
            TransitionKind.Cut,
            TransitionKind.Fade,
            TransitionKind.Flash,
            TransitionKind.Dissolve,
            TransitionKind.Box,
            TransitionKind.Reveal,
            TransitionKind.Uncover,
            TransitionKind.Cover,
            TransitionKind.Push,
            TransitionKind.Split,
            TransitionKind.Blinds,
            TransitionKind.RandomBar,
            TransitionKind.Strips,
            TransitionKind.Wheel,
            TransitionKind.Zoom,
            TransitionKind.Pan,
            TransitionKind.Gallery,
            TransitionKind.Conveyor,
            TransitionKind.Window,
            TransitionKind.Morph,
            TransitionKind.Flip,
            TransitionKind.Cube,
            TransitionKind.Rotate,
            TransitionKind.Honeycomb,
            TransitionKind.Switch,
            TransitionKind.Orbit,
            TransitionKind.Ferris,
            TransitionKind.Flythrough,
            TransitionKind.Glitter,
            TransitionKind.Ripple,
            TransitionKind.Wind,
            TransitionKind.Curtains,
            TransitionKind.Shred,
            TransitionKind.Drape,
            TransitionKind.Fracture,
            TransitionKind.Crush,
            TransitionKind.Prism,
            TransitionKind.Prestige,
            TransitionKind.Warp,
            TransitionKind.Vortex,
            TransitionKind.PageCurlSingle
        };

        SlideShowTransitionPlanner.RandomCandidateKinds.Should().Equal(expected);
        SlideShowTransitionPlanner.RandomCandidateKinds.Should().OnlyHaveUniqueItems();

        var playbackFamilies = SlideShowTransitionPlanner.RandomCandidateKinds
            .Select(kind => SlideShowTransitionPlanner.Plan(
                new SlideTransition { Kind = kind }).PlaybackKind)
            .ToArray();
        playbackFamilies.Should().OnlyHaveUniqueItems();
        playbackFamilies.Should().NotContain(SlideShowTransitionPlaybackKind.FadeFallback);
        playbackFamilies.Should().NotContain(SlideShowTransitionPlaybackKind.PushLike);
    }

    [Fact]
    public void PlanTransition_RandomIsStableAcrossEquivalentRendererModels()
    {
        var firstPresentation = MakeStableRandomPresentation();
        var secondPresentation = MakeStableRandomPresentation();
        var firstSlide = firstPresentation.Slides[1];
        var secondSlide = secondPresentation.Slides[1];
        var firstTransition = firstSlide.Transition!;
        var secondTransition = secondSlide.Transition!;

        var first = SlideShowPlaybackPlanner.PlanTransition(
            firstPresentation,
            firstSlide,
            firstTransition);
        var second = SlideShowPlaybackPlanner.PlanTransition(
            secondPresentation,
            secondSlide,
            secondTransition);

        first.RandomSeed.Should().Be(second.RandomSeed);
        first.ResolvedKind.Should().Be(second.ResolvedKind);
        first.ActionKind.Should().Be(second.ActionKind);
        first.SourceKind.Should().Be(second.SourceKind);
        first.SourceKind.Should().NotBe(SlideShowTransitionPlaybackKind.FadeFallback);
        SlideShowTransitionPlanner.RandomCandidateKinds.Should().Contain(first.ResolvedKind);
        first.ResolvedKind.Should().NotBe(TransitionKind.Random);
        first.EffectiveTransition.Kind.Should().Be(first.ResolvedKind);
        first.EffectiveTransition.DurationMs.Should().Be(firstTransition.DurationMs);
        first.EffectiveTransition.Direction.Should().Be(firstTransition.Direction);
        first.EffectiveTransition.SplitOrientation.Should().Be(firstTransition.SplitOrientation);
        first.EffectiveTransition.WheelSpokeCount.Should().Be(firstTransition.WheelSpokeCount);
        first.EffectiveTransition.MorphOption.Should().Be(firstTransition.MorphOption);
        firstTransition.Kind.Should().Be(TransitionKind.Random);
    }

    [Fact]
    public void ComputeRandomSeed_IncludesPresentationSlideAndTransitionState()
    {
        var presentation = MakeStableRandomPresentation();
        var slide = presentation.Slides[1];
        var transition = slide.Transition!;
        var original = SlideShowTransitionPlanner.ComputeRandomSeed(
            presentation,
            slide,
            transition);

        presentation.SlideSizeCxEmu++;
        var presentationChanged = SlideShowTransitionPlanner.ComputeRandomSeed(
            presentation,
            slide,
            transition);
        presentation.SlideSizeCxEmu--;

        slide.Id = "rIdChanged";
        var slideChanged = SlideShowTransitionPlanner.ComputeRandomSeed(
            presentation,
            slide,
            transition);
        slide.Id = "rId3";

        transition.DurationMs++;
        var transitionChanged = SlideShowTransitionPlanner.ComputeRandomSeed(
            presentation,
            slide,
            transition);

        presentationChanged.Should().NotBe(original);
        slideChanged.Should().NotBe(original);
        transitionChanged.Should().NotBe(original);
    }

    [Fact]
    public void PlanTransition_RandomWithoutHostContextStillUsesCandidateNotFallback()
    {
        var plan = SlideShowTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Random,
            DurationMs = 625
        });

        SlideShowTransitionPlanner.RandomCandidateKinds.Should().Contain(plan.ResolvedKind);
        plan.ResolvedKind.Should().NotBe(TransitionKind.Random);
        plan.PlaybackKind.Should().NotBe(SlideShowTransitionPlaybackKind.FadeFallback);
        plan.RandomSeed.Should().NotBeNull();
        SlideShowTransitionPlanner.PlanPlaybackKind(TransitionKind.Random)
            .Should().NotBe(SlideShowTransitionPlaybackKind.FadeFallback);
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
    public void PlanTransition_ResolvesCombAsDirectionalBarWipe(
        TransitionDirection direction,
        bool expectedHorizontal)
    {
        var plan = SlideShowTransitionPlanner.Plan(new SlideTransition
        {
            Kind = TransitionKind.Comb,
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
    public void BuildPresenterState_UsesAuthoredPenColorWhenNoSessionToolPlanIsProvided()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.PresenterPenColor = new ThemeAwareColor(SrgbColor.FromRgb(0x123456));
        var controller = new SlideShowController(presentation.Slides, startIndex: 0);
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

        var state = SlideShowHostPlanner.BuildPresenterState(presentation, controller, now, now);

        state.ToolPlan.PointerInk.InkState.ColorHex.Should().Be("#123456");
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
    public void HitTestTriggerShape_ResolvesGroupedTriggerShape()
    {
        var slide = new Slide();
        var trigger = new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
        };
        var group = new SlideShape { Id = 41, Kind = SlideShapeKind.Group };
        group.Children.Add(trigger);
        slide.Shapes.Add(group);
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 100,
            TriggerShapeId = trigger.Id,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick,
        });

        SlideShowHostPlanner.HitTestTriggerShape(slide, new SlideShowPoint(48, 48))
            .Should().Be(trigger.Id);
    }

    [Fact]
    public void HitTestTriggerShape_ResolvesGroupedChildTrigger()
    {
        var child = new SlideShape
        {
            Id = 84,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = (long)(120 * SlideShowHostPlanner.EmusPerDip),
            OffsetYEmu = (long)(80 * SlideShowHostPlanner.EmusPerDip),
            ExtentCxEmu = (long)(160 * SlideShowHostPlanner.EmusPerDip),
            ExtentCyEmu = (long)(90 * SlideShowHostPlanner.EmusPerDip)
        };
        var group = new SlideShape
        {
            Id = 83,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = (long)(400 * SlideShowHostPlanner.EmusPerDip),
            ExtentCyEmu = (long)(300 * SlideShowHostPlanner.EmusPerDip)
        };
        group.Children.Add(child);

        var slide = new Slide();
        slide.Shapes.Add(group);
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 100,
            TriggerShapeId = child.Id,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick
        });

        var intent = SlideShowHostPlanner.PlanPointerClick(
            slide,
            new SlideShowPoint(160, 100));

        intent.Kind.Should().Be(SlideShowPointerClickIntentKind.Trigger);
        intent.TriggerShapeId.Should().Be(child.Id);
    }

    [Fact]
    public void HitTestHyperlink_ResolvesTheRunUnderThePointer()
    {
        var first = new Hyperlink { Url = "https://first.example.com" };
        var second = new Hyperlink { Url = "https://second.example.com" };
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "First", Hyperlink = first, FontSizePt = 18 });
        paragraph.Runs.Add(new Run { Text = "Second", Hyperlink = second, FontSizePt = 18 });
        body.Paragraphs.Add(paragraph);

        var slide = new Slide();
        var shape = new SlideShape
        {
            OffsetXEmu = (long)(100 * 9525),
            OffsetYEmu = (long)(100 * 9525),
            ExtentCxEmu = (long)(300 * 9525),
            ExtentCyEmu = (long)(100 * 9525),
            TextBody = body
        };
        slide.Shapes.Add(shape);

        SlideShowHostPlanner.HitTestHyperlink(slide, new SlideShowPoint(125, 110))
            .Should().BeSameAs(first);
        SlideShowHostPlanner.HitTestHyperlink(slide, new SlideShowPoint(205, 110))
            .Should().BeSameAs(second);
        SlideShowHostPlanner.HitTestHyperlink(slide, new SlideShowPoint(380, 110))
            .Should().BeNull("empty shape space must not activate a run hyperlink");
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
                ZoomProperties = new ZoomObjectProperties(
                    TransitionDuration: "1200",
                    ShowBackground: false),
            },
        };
        presentation.Slides[0].Shapes.Add(shape);

        var intent = SlideShowHostPlanner.PlanPointerClick(
            presentation.Slides[0],
            new SlideShowPoint(48, 48),
            presentation);

        intent.Kind.Should().Be(SlideShowPointerClickIntentKind.Zoom);
        intent.TargetSlideIndex.Should().Be(1);
        intent.ReturnToParent.Should().BeTrue();
        intent.TransitionDurationMs.Should().Be(1200);
        intent.ShowBackground.Should().BeFalse();
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
    public void PlanZoomNavigation_with_duration_requests_zoom_transition()
    {
        var presentation = MakePresentation(2);
        var controller = new SlideShowController(presentation.Slides, startIndex: 0);

        var command = SlideShowHostPlanner.PlanZoomNavigation(
            controller,
            presentation.Slides,
            targetSlideIndex: 1,
            transitionDurationMs: 1200,
            showBackground: false);

        command.AnimateSlide.Should().BeTrue();
        command.TransitionDurationMs.Should().Be(1200);
        command.UseDestinationBackground.Should().BeFalse();

        var display = SlideShowHostPlanner.BuildDisplayPlan(
            presentation,
            controller,
            animated: true,
            zoomTransitionDurationMs: command.TransitionDurationMs,
            zoomShowBackground: command.UseDestinationBackground);

        display.Transition.Should().NotBeNull();
        display.Transition!.Kind.Should().Be(TransitionKind.Zoom);
        display.Transition.DurationMs.Should().Be(1200);
        display.UseDestinationBackground.Should().BeFalse();
    }

    [Fact]
    public void Show_without_animation_suppresses_slide_and_zoom_transitions()
    {
        var presentation = MakePresentation(3);
        var controller = new SlideShowController(
            presentation.Slides,
            startIndex: 0,
            showWithAnimation: false);

        var advance = SlideShowHostPlanner.PlanAdvance(controller);
        advance.AnimateSlide.Should().BeFalse();

        var zoom = SlideShowHostPlanner.PlanZoomNavigation(
            controller,
            presentation.Slides,
            targetSlideIndex: 2,
            transitionDurationMs: 1200);
        zoom.AnimateSlide.Should().BeFalse();

        var back = SlideShowHostPlanner.PlanBack(controller);
        back.AnimateSlide.Should().BeFalse();
    }

    [Fact]
    public void PlanZoomNavigation_ReturnToParent_returns_to_parent_on_next_advance()
    {
        var presentation = MakePresentation(3);
        var controller = new SlideShowController(presentation.Slides, startIndex: 0);

        SlideShowHostPlanner.PlanZoomNavigation(
            controller,
            presentation.Slides,
            targetSlideIndex: 2,
            returnToParent: true);

        controller.CurrentSlideIndex.Should().Be(2);
        controller.HasZoomReturnPath.Should().BeTrue();

        var command = SlideShowHostPlanner.PlanAdvance(controller);

        command.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        command.SlideIndex.Should().Be(0);
        controller.HasZoomReturnPath.Should().BeFalse();
    }

    [Fact]
    public void PlanZoomNavigation_ReturnToParent_preserves_transition_on_advance()
    {
        var presentation = MakePresentation(3);
        var controller = new SlideShowController(presentation.Slides, startIndex: 0);

        SlideShowHostPlanner.PlanZoomNavigation(
            controller,
            presentation.Slides,
            targetSlideIndex: 2,
            returnToParent: true,
            transitionDurationMs: 1400,
            showBackground: false);

        var command = SlideShowHostPlanner.PlanAdvance(controller);

        command.SlideIndex.Should().Be(0);
        command.AnimateSlide.Should().BeTrue();
        command.TransitionDurationMs.Should().Be(1400);
        command.UseDestinationBackground.Should().BeFalse();
    }

    [Fact]
    public void PlanZoomNavigation_ReturnToParent_preserves_transition_on_back()
    {
        var presentation = MakePresentation(3);
        var controller = new SlideShowController(presentation.Slides, startIndex: 0);

        SlideShowHostPlanner.PlanZoomNavigation(
            controller,
            presentation.Slides,
            targetSlideIndex: 2,
            returnToParent: true,
            transitionDurationMs: 850,
            showBackground: false);

        var command = SlideShowHostPlanner.PlanBack(controller);

        command.SlideIndex.Should().Be(0);
        command.AnimateSlide.Should().BeTrue();
        command.TransitionDurationMs.Should().Be(850);
        command.UseDestinationBackground.Should().BeFalse();
    }

    [Fact]
    public void PlanZoomNavigation_without_return_to_parent_keeps_normal_advance()
    {
        var presentation = MakePresentation(3);
        var controller = new SlideShowController(presentation.Slides, startIndex: 0);

        SlideShowHostPlanner.PlanZoomNavigation(
            controller,
            presentation.Slides,
            targetSlideIndex: 2,
            returnToParent: false);

        var command = SlideShowHostPlanner.PlanAdvance(controller);

        command.Kind.Should().Be(SlideShowHostCommandKind.Close);
        controller.CurrentSlideIndex.Should().Be(2);
    }

    [Fact]
    public void PlanInternalSlideJump_clears_an_active_zoom_return_path()
    {
        var presentation = MakePresentation(3);
        var controller = new SlideShowController(presentation.Slides, startIndex: 0);

        controller.EnterZoomNavigation(2, returnToParent: true);
        SlideShowHostPlanner.PlanInternalSlideJump(
            controller,
            presentation.Slides,
            presentation.Slides[1].Id);

        controller.CurrentSlideIndex.Should().Be(1);
        controller.HasZoomReturnPath.Should().BeFalse();
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

    private static Presentation MakeStableRandomPresentation()
    {
        var presentation = MakePresentation(3);
        presentation.SlideSizeCxEmu = 12_192_000;
        presentation.SlideSizeCyEmu = 6_858_000;
        presentation.Properties.Title = "Deterministic Random Deck";
        presentation.Properties.Author = "FreeP";

        for (var index = 0; index < presentation.Slides.Count; index++)
        {
            presentation.Slides[index].Id = $"rId{index + 2}";
            presentation.Slides[index].NumericId = (uint)(256 + index);
        }

        presentation.Slides[1].Transition = new SlideTransition
        {
            Kind = TransitionKind.Random,
            Direction = TransitionDirection.Right,
            SplitOrientation = TransitionDirection.Vertical,
            DurationMs = 875,
            AdvanceOnClick = false,
            AdvanceAfterMs = 2_400,
            MorphOption = "byWord",
            WheelSpokeCount = 8
        };
        return presentation;
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
