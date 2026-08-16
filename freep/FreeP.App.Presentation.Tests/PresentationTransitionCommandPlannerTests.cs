using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationTransitionCommandPlannerTests
{
    private static EditingSession MakeSession(out Presentation presentation)
    {
        presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    [Theory]
    [InlineData("freep.transition.none", TransitionKind.None)]
    [InlineData("freep.transition.fade", TransitionKind.Fade)]
    [InlineData("freep.transition.push", TransitionKind.Push)]
    [InlineData("freep.transition.wipe", TransitionKind.Wipe)]
    [InlineData("freep.transition.split", TransitionKind.Split)]
    [InlineData("freep.transition.box", TransitionKind.Box)]
    [InlineData("freep.transition.doors", TransitionKind.Doors)]
    [InlineData("freep.transition.reveal", TransitionKind.Reveal)]
    [InlineData("freep.transition.flash", TransitionKind.Flash)]
    [InlineData("freep.transition.morph", TransitionKind.Morph)]
    [InlineData("freep.transition.cut", TransitionKind.Cut)]
    [InlineData("freep.transition.cover", TransitionKind.Cover)]
    [InlineData("freep.transition.uncover", TransitionKind.Uncover)]
    [InlineData("freep.transition.blinds", TransitionKind.Blinds)]
    [InlineData("freep.transition.comb", TransitionKind.Comb)]
    [InlineData("freep.transition.random-bars", TransitionKind.RandomBar)]
    [InlineData("freep.transition.strips", TransitionKind.Strips)]
    [InlineData("freep.transition.wheel-reverse", TransitionKind.WheelReverse)]
    [InlineData("freep.transition.gallery", TransitionKind.Gallery)]
    [InlineData("freep.transition.conveyor", TransitionKind.Conveyor)]
    [InlineData("freep.transition.pan", TransitionKind.Pan)]
    [InlineData("freep.transition.window", TransitionKind.Window)]
    [InlineData("freep.transition.dissolve", TransitionKind.Dissolve)]
    [InlineData("freep.transition.zoom", TransitionKind.Zoom)]
    [InlineData("freep.transition.wheel", TransitionKind.Wheel)]
    public void TryPlan_MapsGalleryCommandIdsToTransitionKinds(
        string commandId,
        TransitionKind expectedKind)
    {
        PresentationTransitionCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        plan.CommandId.Should().Be(commandId);
        plan.Intent.Should().Be(PresentationTransitionCommandIntentKind.SetKind);
        plan.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData("freep.transition.duration", PresentationTransitionCommandIntentKind.SetDuration)]
    [InlineData("freep.transition.advance-on-click", PresentationTransitionCommandIntentKind.ToggleAdvanceOnClick)]
    [InlineData("freep.transition.advance-after", PresentationTransitionCommandIntentKind.SetAdvanceAfter)]
    [InlineData("freep.transition.apply-all", PresentationTransitionCommandIntentKind.ApplyToAllSlides)]
    [InlineData("freep.transition.sound", PresentationTransitionCommandIntentKind.RequestSoundPicker)]
    [InlineData("freep.transition.sound-none", PresentationTransitionCommandIntentKind.ClearSound)]
    [InlineData("freep.transition.sound-loop", PresentationTransitionCommandIntentKind.ToggleSoundLoop)]
    public void TryPlan_MapsTimingAndApplyAllCommandIdsToTypedIntents(
        string commandId,
        PresentationTransitionCommandIntentKind expectedIntent)
    {
        PresentationTransitionCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        plan.CommandId.Should().Be(commandId);
        plan.Intent.Should().Be(expectedIntent);
        plan.Kind.Should().BeNull();
    }

    [Fact]
    public void TryPlan_RejectsUnknownCommandId()
    {
        PresentationTransitionCommandPlanner.TryPlan("freep.transition.missing", out var plan)
            .Should().BeFalse();

        plan.Should().BeNull();
    }

    [Fact]
    public void BuildTransitionForKind_NoneClearsTransition()
    {
        var current = new SlideTransition
        {
            Kind = TransitionKind.Push,
            Direction = TransitionDirection.Left,
            DurationMs = 1500,
        };

        PresentationTransitionCommandPlanner.BuildTransitionForKind(current, TransitionKind.None)
            .Should().BeNull();
    }

    [Fact]
    public void BuildTransitionForKind_NewTransitionUsesPowerPointFastDefault()
    {
        var transition = PresentationTransitionCommandPlanner.BuildTransitionForKind(null, TransitionKind.Fade);

        transition.Should().NotBeNull();
        transition!.Kind.Should().Be(TransitionKind.Fade);
        transition.Direction.Should().BeNull();
        transition.DurationMs.Should().Be(PresentationTransitionCommandPlanner.DefaultDurationMs);
        transition.AdvanceOnClick.Should().BeTrue();
        transition.AdvanceAfterMs.Should().BeNull();
    }

    [Fact]
    public void BuildTransitionForKind_PreservesExistingTimingAndDirection()
    {
        var current = new SlideTransition
        {
            Kind = TransitionKind.Wipe,
            Direction = TransitionDirection.Left,
            DurationMs = 1250,
            AdvanceOnClick = false,
            AdvanceAfterMs = 3000,
            RawXml = "<p:transition />",
            MorphOption = "byWord",
        };

        var transition = PresentationTransitionCommandPlanner.BuildTransitionForKind(
            current,
            TransitionKind.Push);

        transition.Should().NotBeSameAs(current);
        transition!.Kind.Should().Be(TransitionKind.Push);
        transition.Direction.Should().Be(TransitionDirection.Left);
        transition.DurationMs.Should().Be(1250);
        transition.AdvanceOnClick.Should().BeFalse();
        transition.AdvanceAfterMs.Should().Be(3000);
        transition.RawXml.Should().BeNull();
        transition.MorphOption.Should().BeNull();
    }

    [Theory]
    [InlineData("0.50s", false, 500)]
    [InlineData("1.25 sec", false, 1250)]
    [InlineData("0s", true, 0)]
    public void TryParseSeconds_MapsRibbonTimingValues(
        string value,
        bool allowZero,
        int expectedMs)
    {
        PresentationTransitionCommandPlanner.TryParseSeconds(value, allowZero, out int ms)
            .Should().BeTrue();

        ms.Should().Be(expectedMs);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0s", false)]
    [InlineData("-1s", true)]
    [InlineData("fast", false)]
    public void TryParseSeconds_RejectsInvalidValues(string? value, bool allowZero)
    {
        PresentationTransitionCommandPlanner.TryParseSeconds(value, allowZero, out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("(none)")]
    [InlineData("none")]
    [InlineData("0s")]
    public void TryParseAdvanceAfterValue_MapsNoneValuesToZero(string value)
    {
        PresentationTransitionCommandPlanner.TryParseAdvanceAfterValue(value, out int ms)
            .Should()
            .BeTrue();

        ms.Should().Be(0);
    }

    [Fact]
    public void BuildApplyToAllTransitions_ClonesSourceForEachSlide()
    {
        var source = new SlideTransition
        {
            Kind = TransitionKind.Zoom,
            Direction = TransitionDirection.In,
            DurationMs = 2000,
            AdvanceAfterMs = 5000,
        };

        var transitions = PresentationTransitionCommandPlanner.BuildApplyToAllTransitions(3, source);

        transitions.Should().HaveCount(3);
        foreach (var transition in transitions)
        {
            transition.Should().NotBeNull();
            transition!.Kind.Should().Be(TransitionKind.Zoom);
            transition.DurationMs.Should().Be(2000);
        }

        transitions[0].Should().NotBeSameAs(source);
        transitions[1].Should().NotBeSameAs(transitions[0]);
    }

    [Fact]
    public void BuildApplyToAllTransitions_NullSourcePlansClearForEachSlide()
    {
        PresentationTransitionCommandPlanner.BuildApplyToAllTransitions(2, null)
            .Should()
            .Equal(new SlideTransition?[] { null, null });
    }

    [Fact]
    public void TryApply_SetKindCommand_UsesSharedKindBuilder()
    {
        var editor = MakeSession(out _);
        PresentationTransitionCommandPlanner.TryPlan("freep.transition.fade", out var plan)
            .Should()
            .BeTrue();

        PresentationTransitionCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        editor.CurrentSlideTransition.Should().NotBeNull();
        editor.CurrentSlideTransition!.Kind.Should().Be(TransitionKind.Fade);
        editor.CurrentSlideTransition.DurationMs.Should().Be(PresentationTransitionCommandPlanner.DefaultDurationMs);
    }

    [Fact]
    public void TryApply_DurationCommand_UsesSelectedRibbonValue()
    {
        var editor = MakeSession(out _);
        editor.SetTransition(new SlideTransition { Kind = TransitionKind.Push });
        PresentationTransitionCommandPlanner.TryPlan("freep.transition.duration", out var plan)
            .Should()
            .BeTrue();

        PresentationTransitionCommandPlanner.TryApply(editor, plan, "1.50s").Should().BeTrue();

        editor.CurrentSlideTransition!.Kind.Should().Be(TransitionKind.Push);
        editor.CurrentSlideTransition.DurationMs.Should().Be(1500);
    }

    [Fact]
    public void TryApply_DurationCommand_RejectsMissingRibbonValue()
    {
        var editor = MakeSession(out _);
        PresentationTransitionCommandPlanner.TryPlan("freep.transition.duration", out var plan)
            .Should()
            .BeTrue();

        PresentationTransitionCommandPlanner.TryApply(editor, plan).Should().BeFalse();

        editor.CurrentSlideTransition.Should().BeNull();
    }

    [Theory]
    [InlineData("0s")]
    [InlineData("(none)")]
    public void TryApply_AdvanceAfterNoneValueClearsAutoAdvance(string selectedValue)
    {
        var editor = MakeSession(out _);
        editor.SetTransition(new SlideTransition
        {
            Kind = TransitionKind.Wipe,
            AdvanceAfterMs = 3000,
        });
        PresentationTransitionCommandPlanner.TryPlan("freep.transition.advance-after", out var plan)
            .Should()
            .BeTrue();

        PresentationTransitionCommandPlanner.TryApply(editor, plan, selectedValue).Should().BeTrue();

        editor.CurrentSlideTransition!.Kind.Should().Be(TransitionKind.Wipe);
        editor.CurrentSlideTransition.AdvanceAfterMs.Should().BeNull();
    }

    [Fact]
    public void TryApply_TransitionSoundCommandsUseHostPickerAndUndoableClear()
    {
        var editor = MakeSession(out _);
        editor.SetTransition(new SlideTransition { Kind = TransitionKind.Fade });

        PresentationTransitionCommandPlanner.TryPlan("freep.transition.sound", out var choosePlan)
            .Should().BeTrue();
        bool pickerInvoked = false;
        PresentationTransitionCommandPlanner.TryApply(
                editor,
                choosePlan,
                onSoundPicker: () => pickerInvoked = true)
            .Should().BeTrue();
        pickerInvoked.Should().BeTrue();

        var sound = new TransitionSound
        {
            AudioBytes = [0x52, 0x49, 0x46, 0x46],
            ContentType = "audio/wav",
        };
        editor.SetCurrentSlideTransitionSound(sound);

        PresentationTransitionCommandPlanner.TryPlan("freep.transition.sound-none", out var clearPlan)
            .Should().BeTrue();
        PresentationTransitionCommandPlanner.TryApply(editor, clearPlan).Should().BeTrue();
        editor.CurrentSlideTransition!.Sound.Should().BeNull();

        editor.Undo();
        editor.CurrentSlideTransition!.Sound.Should().NotBeNull();
        editor.CurrentSlideTransition.Sound!.AudioBytes.Should().Equal(sound.AudioBytes);
    }

    [Fact]
    public void TryApply_TransitionSoundLoop_TogglesOnlyLoopAndIsUndoable()
    {
        var editor = MakeSession(out _);
        var sound = new TransitionSound
        {
            AudioBytes = [0x52, 0x49, 0x46, 0x46],
            ContentType = "audio/wav",
            RelId = "rIdSound",
            PartPath = "ppt/media/transition.wav",
            Loop = false,
        };
        editor.SetTransition(new SlideTransition
        {
            Kind = TransitionKind.Fade,
            DurationMs = 900,
            AdvanceOnClick = false,
            Sound = sound,
        });

        PresentationTransitionCommandPlanner.TryPlan("freep.transition.sound-loop", out var plan)
            .Should().BeTrue();
        PresentationTransitionCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        editor.CurrentSlideTransition!.DurationMs.Should().Be(900);
        editor.CurrentSlideTransition.AdvanceOnClick.Should().BeFalse();
        editor.CurrentSlideTransition.Sound!.Loop.Should().BeTrue();
        editor.CurrentSlideTransition.Sound.RelId.Should().Be("rIdSound");
        editor.CurrentSlideTransition.Sound.AudioBytes.Should().Equal(sound.AudioBytes);

        editor.Undo();
        editor.CurrentSlideTransition!.Sound!.Loop.Should().BeFalse();
    }

    [Fact]
    public void GetToggleState_SoundLoopRequiresSoundAndReflectsLoopFlag()
    {
        PresentationTransitionCommandPlanner.GetToggleState(
                null,
                PresentationTransitionCommandIntentKind.ToggleSoundLoop)
            .Should().Be((false, false));

        PresentationTransitionCommandPlanner.GetToggleState(
                new SlideTransition(),
                PresentationTransitionCommandIntentKind.ToggleSoundLoop)
            .Should().Be((false, false));

        PresentationTransitionCommandPlanner.GetToggleState(
                new SlideTransition { Sound = new TransitionSound { Loop = true } },
                PresentationTransitionCommandIntentKind.ToggleSoundLoop)
            .Should().Be((true, true));
    }

    [Fact]
    public void TryApply_ApplyAllCommand_ClonesCurrentTransitionAcrossSlides()
    {
        var editor = MakeSession(out var presentation);
        editor.InsertSlide();
        editor.SetTransition(new SlideTransition
        {
            Kind = TransitionKind.Zoom,
            RawXml = "<p:transition />",
            Sound = new TransitionSound { ContentType = "audio/mpeg", RelId = "rId1" },
        });
        PresentationTransitionCommandPlanner.TryPlan("freep.transition.apply-all", out var plan)
            .Should()
            .BeTrue();

        PresentationTransitionCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        presentation.Slides.Select(slide => slide.Transition?.Kind)
            .Should()
            .OnlyContain(kind => kind == TransitionKind.Zoom);
        presentation.Slides[0].Transition.Should().NotBeSameAs(presentation.Slides[1].Transition);
        presentation.Slides[1].Transition!.RawXml.Should().Be("<p:transition />");
        presentation.Slides[1].Transition!.Sound.Should().NotBeNull();
    }

    [Fact]
    public void TryApply_ApplyAllCommand_IsOneUndoableStep()
    {
        // R137: "Apply to All Slides" must go through the command bus as a single
        // undoable step, not mutate slides directly (which Ctrl+Z can't see).
        var editor = MakeSession(out var presentation);
        editor.InsertSlide();
        editor.InsertSlide();
        editor.SelectSlide(0);

        // Establish a known transition on slide 0 via a separate, already-undoable command
        // BEFORE the apply-all step, so we can tell the two undo entries apart.
        editor.SetTransition(new SlideTransition { Kind = TransitionKind.Zoom });
        presentation.Slides[1].Transition.Should().BeNull();
        presentation.Slides[2].Transition.Should().BeNull();

        PresentationTransitionCommandPlanner.TryPlan("freep.transition.apply-all", out var plan)
            .Should()
            .BeTrue();
        PresentationTransitionCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        presentation.Slides.Should().OnlyContain(
            slide => slide.Transition != null && slide.Transition.Kind == TransitionKind.Zoom);
        editor.CanUndo.Should().BeTrue();

        // A single Undo() must revert the WHOLE apply-all step in one shot, leaving only
        // slide 0's original (pre-apply-all) transition in place.
        editor.Undo();

        presentation.Slides[0].Transition.Should().NotBeNull();
        presentation.Slides[0].Transition!.Kind.Should().Be(TransitionKind.Zoom);
        presentation.Slides[1].Transition.Should().BeNull();
        presentation.Slides[2].Transition.Should().BeNull();

        // And redo restores the all-slides state again.
        editor.Redo();
        presentation.Slides.Should().OnlyContain(
            slide => slide.Transition != null && slide.Transition.Kind == TransitionKind.Zoom);
    }

    [Fact]
    public void TryApply_ApplyAllCommand_WithNoAdditionalSlides_StillClonesOntoCurrentSlide()
    {
        // Sibling no-regression: a single-slide presentation (the common case) must keep
        // working exactly as before — apply-all still sets the current slide's transition
        // and remains undoable (as its own, separate undo step from the prior SetTransition).
        var editor = MakeSession(out var presentation);
        editor.SetTransition(new SlideTransition { Kind = TransitionKind.Push });

        PresentationTransitionCommandPlanner.TryPlan("freep.transition.apply-all", out var plan)
            .Should()
            .BeTrue();
        PresentationTransitionCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        presentation.Slides.Should().ContainSingle();
        presentation.Slides[0].Transition!.Kind.Should().Be(TransitionKind.Push);

        // First undo reverts only the apply-all step, back to its pre-apply-all value.
        editor.Undo();
        presentation.Slides[0].Transition!.Kind.Should().Be(TransitionKind.Push);

        // Second undo reverts the original SetTransition, back to no transition at all.
        editor.Undo();
        presentation.Slides[0].Transition.Should().BeNull();
    }
}
