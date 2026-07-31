using System.IO;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 4C: Tests for the Transitions and Animations ribbon tabs.
///
/// Verifies:
/// - FreePRibbon.Build() includes the expected tabs, groups and command ids.
/// - FreePRibbonCommands: invoking transition commands sets Editor.CurrentSlideTransition correctly.
/// - Invoking an animation command adds to Editor.CurrentSlideAnimations.
/// - "Apply To All" propagates the current slide's transition to every slide.
/// - Reorder (Move Earlier / Move Later) reorders the animation list.
/// - SlideShow commands fire the provided Action callbacks.
///
/// These tests run plain (no STA required) because they exercise the command layer
/// directly without constructing WPF controls.
/// </summary>
public class RibbonTransitionsAnimationsTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────────

    /// <summary>Creates a fresh EditingSession with one slide that has one shape (id=1).</summary>
    private static (EditingSession editor, Presentation pres) MakeSession()
    {
        var pres = Presentation.CreateEmpty();
        var bus  = new PresentationCommandBus(pres);
        var ed   = new EditingSession(pres, bus);
        // Ensure shape id=1 exists for animation targeting.
        ed.Select(pres.Slides[0].Shapes.Count > 0 ? pres.Slides[0].Shapes[0].Id : 0);
        return (ed, pres);
    }

    /// <summary>Builds a command registry with the given session (no slideshow Actions).</summary>
    private static RibbonCommandRegistry MakeRegistry(EditingSession editor,
        Action? onStart = null, Action? onCurrent = null, Action? onCustomShows = null,
        Action? onRehearseTimings = null, Action? onRecordTimings = null,
        Action? onTransitionSound = null)
        => FreePRibbonCommands.Build(
            new RibbonStateStore(),
            editor,
            onStart,
            onCurrent,
            onRehearseTimings,
            onRecordTimings,
            onCustomShows: onCustomShows,
            onTransitionSound: onTransitionSound);

    /// <summary>Executes a registered command by id.</summary>
    private static void Exec(RibbonCommandRegistry registry, string id, RibbonCommandContext? context = null)
    {
        bool found = registry.TryGet(id, out var cmd);
        Assert.True(found, $"Command '{id}' was not registered.");
        cmd!.Execute(context ?? RibbonCommandContext.Empty);
    }

    // ── Ribbon definition structure ────────────────────────────────────────────────

    [Fact]
    public void RibbonBuild_ContainsTransitionsTab()
    {
        var def = FreePRibbon.Build();
        Assert.Contains(def.Tabs, t => t.Id == "transitions");
    }

    [Fact]
    public void RibbonBuild_ContainsAnimationsTab()
    {
        var def = FreePRibbon.Build();
        Assert.Contains(def.Tabs, t => t.Id == "animations");
    }

    [Fact]
    public void TransitionsTab_ContainsTransitionGalleryGroup()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "transitions");
        Assert.Contains(tab.Groups, g => g.Id == "transition-gallery");
    }

    [Fact]
    public void TransitionsTab_ContainsTimingGroup()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "transitions");
        Assert.Contains(tab.Groups, g => g.Id == "transition-timing");
    }

    [Fact]
    public void TransitionTimingGroup_ContainsSoundAuthoringCommands()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "transitions");
        var group = tab.Groups.Single(g => g.Id == "transition-timing");

        Assert.Contains(group.Controls, c => c.CommandId.Value == "freep.transition.sound");
        Assert.Contains(group.Controls, c => c.CommandId.Value == "freep.transition.sound-none");
    }

    [Fact]
    public void TransitionsTab_ContainsSlideShowGroup()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "transitions");
        Assert.Contains(tab.Groups, g => g.Id == "slideshow-from-transitions");
    }

    [Fact]
    public void AnimationsTab_ContainsAnimationEffectsGroup()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "animations");
        Assert.Contains(tab.Groups, g => g.Id == "animation-effects");
    }

    [Fact]
    public void AnimationsTab_ContainsTimingGroup()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "animations");
        Assert.Contains(tab.Groups, g => g.Id == "animation-timing");
    }

    [Fact]
    public void TransitionGalleryGroup_ContainsFadeCommandId()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "transitions");
        var group = tab.Groups.Single(g => g.Id == "transition-gallery");
        // At least one control must carry the freep.transition.fade id.
        Assert.Contains(group.Controls, c => c.CommandId.Value == "freep.transition.fade");
    }

    [Fact]
    public void SlideShowGroup_ContainsFromBeginningAndFromCurrent()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "transitions");
        var group = tab.Groups.Single(g => g.Id == "slideshow-from-transitions");
        Assert.Contains(group.Controls, c => c.CommandId.Value == "freep.slideshow.from-beginning");
        Assert.Contains(group.Controls, c => c.CommandId.Value == "freep.slideshow.from-current-slide");
        Assert.Contains(group.Controls, c => c.CommandId.Value == "freep.slideshow.custom-shows");
    }

    // ── Transition commands ────────────────────────────────────────────────────────

    [Fact]
    public void Cmd_TransitionFade_SetsKindOnCurrentSlide()
    {
        var (ed, _) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.transition.fade");
        Assert.Equal(TransitionKind.Fade, ed.CurrentSlideTransition?.Kind);
    }

    [Fact]
    public void Cmd_TransitionSound_DelegatesPickerAndNoSoundClearsUndoably()
    {
        var (ed, pres) = MakeSession();
        pres.Slides[0].Transition = new SlideTransition { Kind = TransitionKind.Fade };
        bool pickerInvoked = false;
        var reg = MakeRegistry(ed, onTransitionSound: () => pickerInvoked = true);

        Exec(reg, "freep.transition.sound");
        Assert.True(pickerInvoked);

        ed.SetCurrentSlideTransitionSound(new TransitionSound
        {
            AudioBytes = [1, 2, 3],
            ContentType = "audio/mpeg",
        });
        Exec(reg, "freep.transition.sound-none");
        Assert.Null(ed.CurrentSlideTransition?.Sound);

        ed.Undo();
        Assert.Equal(new byte[] { 1, 2, 3 }, ed.CurrentSlideTransition?.Sound?.AudioBytes);
    }

    [Fact]
    public void Cmd_TransitionSoundLoop_TogglesLoopAndUndoRestoresIt()
    {
        var (ed, pres) = MakeSession();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind = TransitionKind.Fade,
            Sound = new TransitionSound { AudioBytes = [1, 2, 3], ContentType = "audio/mpeg" },
        };
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.transition.sound-loop");
        Assert.True(ed.CurrentSlideTransition?.Sound?.Loop);

        ed.Undo();
        Assert.False(ed.CurrentSlideTransition?.Sound?.Loop);
    }

    [Fact]
    public void Cmd_TransitionNone_ClearsTransition()
    {
        var (ed, pres) = MakeSession();
        pres.Slides[0].Transition = new SlideTransition { Kind = TransitionKind.Push };
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.transition.none");
        Assert.Null(ed.CurrentSlideTransition);
    }

    [Fact]
    public void Cmd_TransitionPush_SetsKindPush()
    {
        var (ed, _) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.transition.push");
        Assert.Equal(TransitionKind.Push, ed.CurrentSlideTransition?.Kind);
    }

    [Fact]
    public void Cmd_TransitionWipe_SetsKindWipe()
    {
        var (ed, _) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.transition.wipe");
        Assert.Equal(TransitionKind.Wipe, ed.CurrentSlideTransition?.Kind);
    }

    [Fact]
    public void Cmd_TransitionDissolve_SetsKindDissolve()
    {
        var (ed, _) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.transition.dissolve");
        Assert.Equal(TransitionKind.Dissolve, ed.CurrentSlideTransition?.Kind);
    }

    [Fact]
    public void Cmd_ExtendedTransitions_ExposeEveryWriterSupportedKind()
    {
        var expected = new (string CommandId, TransitionKind Kind)[]
        {
            ("freep.transition.fly", TransitionKind.Fly),
            ("freep.transition.random", TransitionKind.Random),
            ("freep.transition.cube", TransitionKind.Cube),
            ("freep.transition.rotate", TransitionKind.Rotate),
            ("freep.transition.flip", TransitionKind.Flip),
            ("freep.transition.ferris", TransitionKind.Ferris),
            ("freep.transition.flythrough", TransitionKind.Flythrough),
            ("freep.transition.switch", TransitionKind.Switch),
            ("freep.transition.orbit", TransitionKind.Orbit),
            ("freep.transition.honeycomb", TransitionKind.Honeycomb),
            ("freep.transition.glitter", TransitionKind.Glitter),
            ("freep.transition.vortex", TransitionKind.Vortex),
            ("freep.transition.shred", TransitionKind.Shred),
            ("freep.transition.wind", TransitionKind.Wind),
            ("freep.transition.ripple", TransitionKind.Ripple),
            ("freep.transition.warp", TransitionKind.Warp),
            ("freep.transition.fracture", TransitionKind.Fracture),
            ("freep.transition.crush", TransitionKind.Crush),
            ("freep.transition.peel-off", TransitionKind.PeelOff),
            ("freep.transition.page-curl-double", TransitionKind.PageCurlDouble),
            ("freep.transition.page-curl-single", TransitionKind.PageCurlSingle),
            ("freep.transition.airplane", TransitionKind.Airplane),
            ("freep.transition.origami", TransitionKind.Origami),
            ("freep.transition.prism", TransitionKind.Prism),
            ("freep.transition.curtains", TransitionKind.Curtains),
            ("freep.transition.drape", TransitionKind.Drape),
            ("freep.transition.prestige", TransitionKind.Prestige)
        };

        var (ed, _) = MakeSession();
        var reg = MakeRegistry(ed);
        foreach (var (commandId, kind) in expected)
        {
            Exec(reg, commandId);
            Assert.Equal(kind, ed.CurrentSlideTransition?.Kind);
        }
    }

    [Fact]
    public void TransitionMoreMenu_ContainsEveryExtendedKind()
    {
        var tab = FreePRibbon.Build().Tabs.Single(t => t.Id == "transitions");
        var group = tab.Groups.Single(g => g.Id == "transition-more");
        var dropdown = Assert.IsType<RibbonDropdown>(
            group.Controls.Single(control => control.CommandId.Value == "freep.transition.more"));

        Assert.Equal(27, dropdown.Menu.Items.Count);
        Assert.Contains(dropdown.Menu.Items, item => item.CommandId?.Value == "freep.transition.page-curl-double");
        Assert.Contains(dropdown.Menu.Items, item => item.CommandId?.Value == "freep.transition.prestige");
    }

    [Fact]
    public void Cmd_TransitionDuration_UsesRibbonContextSelectedValue()
    {
        var (ed, _) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.transition.fade");

        Exec(reg, "freep.transition.duration", RibbonCommandContext.ForSelectedValue("1.50s"));

        Assert.Equal(TransitionKind.Fade, ed.CurrentSlideTransition?.Kind);
        Assert.Equal(1500, ed.CurrentSlideTransition?.DurationMs);
    }

    [Fact]
    public void Cmd_TransitionAdvanceAfter_UsesRibbonContextSelectedValue()
    {
        var (ed, _) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.transition.fade");

        Exec(reg, "freep.transition.advance-after", RibbonCommandContext.ForSelectedValue("3s"));

        Assert.Equal(3000, ed.CurrentSlideTransition?.AdvanceAfterMs);
    }

    [Fact]
    public void FreePRibbonCommands_source_routes_transitions_through_shared_planner()
    {
        var source = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Host", "FreePRibbonCommands.cs"));

        Assert.Contains("PresentationTransitionCommandPlanner.BuiltInPlans", source);
        Assert.Contains("PresentationTransitionCommandPlanner.TryApply", source);
        Assert.DoesNotContain("RegisterTransitionKind(", source);
        Assert.DoesNotContain("freep.transition.duration\", new ActionRibbonCommand", source);
    }

    [Fact]
    public void FreePRibbonCommands_source_routes_animations_through_shared_planner()
    {
        var source = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Host", "FreePRibbonCommands.cs"));

        Assert.Contains("PresentationAnimationCommandPlanner.BuiltInPlans", source);
        Assert.Contains("PresentationAnimationCommandPlanner.TryApply", source);
        Assert.DoesNotContain("RegisterEntranceAnim(", source);
        Assert.DoesNotContain("freep.anim.duration\", new ActionRibbonCommand", source);
        Assert.DoesNotContain("freep.anim.delay\",    new ActionRibbonCommand", source);
    }

    // ── Transition Apply To All ────────────────────────────────────────────────────

    [Fact]
    public void Cmd_ApplyToAll_SetsTransitionOnEverySlide()
    {
        var (ed, pres) = MakeSession();
        // Add two more slides.
        ed.InsertSlide();
        ed.InsertSlide();
        Assert.Equal(3, pres.Slides.Count);

        // Set a transition on slide 0 (current) via the command.
        ed.SelectSlide(0);
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.transition.zoom");
        Assert.Equal(TransitionKind.Zoom, pres.Slides[0].Transition?.Kind);

        // Apply to all.
        Exec(reg, "freep.transition.apply-all");

        // Every slide must have Zoom.
        foreach (var slide in pres.Slides)
            Assert.Equal(TransitionKind.Zoom, slide.Transition?.Kind);
    }

    [Fact]
    public void Cmd_ApplyToAll_WithNullTransition_ClearsAllSlides()
    {
        var (ed, pres) = MakeSession();
        ed.InsertSlide();
        pres.Slides[0].Transition = new SlideTransition { Kind = TransitionKind.Fade };
        pres.Slides[1].Transition = new SlideTransition { Kind = TransitionKind.Cut };

        // Navigate to slide 0 which has no transition set via the session (it was set directly).
        // Clear slide 0's transition first.
        ed.SelectSlide(0);
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.transition.none");   // clears slide 0
        Exec(reg, "freep.transition.apply-all");

        Assert.Null(pres.Slides[0].Transition);
        Assert.Null(pres.Slides[1].Transition);
    }

    // ── Animation commands ─────────────────────────────────────────────────────────

    [Fact]
    public void Cmd_EntranceAppear_AddsAnimationToCurrentSlide()
    {
        var (ed, pres) = MakeSession();
        // Select the shape first so AddAnimation(0, ...) can target it.
        ed.Select(pres.Slides[0].Shapes[0].Id);
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.anim.entrance.appear");
        Assert.Single(ed.CurrentSlideAnimations);
        Assert.Equal(AnimationKind.Entrance, ed.CurrentSlideAnimations[0].Kind);
        Assert.Equal(AnimationPreset.Appear, ed.CurrentSlideAnimations[0].Preset);
    }

    [Fact]
    public void Cmd_EntranceFlyIn_SetsPresetFlyIn()
    {
        var (ed, pres) = MakeSession();
        ed.Select(pres.Slides[0].Shapes[0].Id);
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.anim.entrance.fly-in");
        Assert.Equal(AnimationPreset.FlyIn, ed.CurrentSlideAnimations[0].Preset);
    }

    [Fact]
    public void Cmd_EmphasisPulse_SetsKindEmphasis()
    {
        var (ed, pres) = MakeSession();
        ed.Select(pres.Slides[0].Shapes[0].Id);
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.anim.emphasis.pulse");
        Assert.Single(ed.CurrentSlideAnimations);
        Assert.Equal(AnimationKind.Emphasis, ed.CurrentSlideAnimations[0].Kind);
        Assert.Equal(AnimationPreset.Pulse,  ed.CurrentSlideAnimations[0].Preset);
    }

    [Fact]
    public void Cmd_EmphasisSpin_SetsPresetSpin()
    {
        var (ed, pres) = MakeSession();
        ed.Select(pres.Slides[0].Shapes[0].Id);
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.anim.emphasis.spin");
        Assert.Equal(AnimationPreset.Spin, ed.CurrentSlideAnimations[0].Preset);
    }

    [Fact]
    public void Cmd_ExitDisappear_SetsKindExit()
    {
        var (ed, pres) = MakeSession();
        ed.Select(pres.Slides[0].Shapes[0].Id);
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.anim.exit.disappear");
        Assert.Single(ed.CurrentSlideAnimations);
        Assert.Equal(AnimationKind.Exit, ed.CurrentSlideAnimations[0].Kind);
    }

    [Fact]
    public void Cmd_AnimNone_RemovesAnimationsForSelectedShape()
    {
        var (ed, pres) = MakeSession();
        var shapeId = pres.Slides[0].Shapes[0].Id;
        ed.Select(shapeId);
        // Pre-populate two animations for this shape.
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = shapeId, Preset = AnimationPreset.Appear });
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = shapeId, Preset = AnimationPreset.Fade });
        Assert.Equal(2, ed.CurrentSlideAnimations.Count);

        var reg = MakeRegistry(ed);
        Exec(reg, "freep.anim.none");
        Assert.Empty(ed.CurrentSlideAnimations);
    }

    [Fact]
    public void Cmd_AnimTiming_UsesSelectedRibbonValues()
    {
        var (ed, pres) = MakeSession();
        var shapeId = pres.Slides[0].Shapes[0].Id;
        ed.Select(shapeId);
        pres.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = shapeId,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 500,
            DelayMs = 0,
        });

        var reg = MakeRegistry(ed);
        Exec(reg, "freep.anim.trigger", RibbonCommandContext.ForSelectedValue("After Previous"));
        Exec(reg, "freep.anim.duration", RibbonCommandContext.ForSelectedValue("1.50s"));
        Exec(reg, "freep.anim.delay", RibbonCommandContext.ForSelectedValue("0.25s"));

        Assert.Equal(AnimationTrigger.AfterPrevious, ed.CurrentSlideAnimations[0].Trigger);
        Assert.Equal(1500, ed.CurrentSlideAnimations[0].DurationMs);
        Assert.Equal(250, ed.CurrentSlideAnimations[0].DelayMs);
    }

    [Fact]
    public void Cmd_AnimTiming_PreservesWheelSpokeCount()
    {
        var (ed, pres) = MakeSession();
        var shapeId = pres.Slides[0].Shapes[0].Id;
        ed.Select(shapeId);
        pres.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = shapeId,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Wheel,
            WheelSpokeCount = 8,
            DurationMs = 500,
        });

        var reg = MakeRegistry(ed);
        Exec(reg, "freep.anim.duration", RibbonCommandContext.ForSelectedValue("1.50s"));

        Assert.Equal(1500, ed.CurrentSlideAnimations[0].DurationMs);
        Assert.Equal(8, ed.CurrentSlideAnimations[0].WheelSpokeCount);
    }

    // ── Move Earlier / Move Later ──────────────────────────────────────────────────

    [Fact]
    public void Cmd_MoveEarlier_ReordersAnimation()
    {
        var (ed, pres) = MakeSession();
        // Two shapes.
        var id1 = pres.Slides[0].Shapes[0].Id;
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = id1 + 1, Name = "S2", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Ellipse,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
        });
        var id2 = id1 + 1;

        // Animations: [Appear on S1, Fade on S2]. We want to move S2's animation earlier.
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = id1, Preset = AnimationPreset.Appear });
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = id2, Preset = AnimationPreset.Fade  });

        ed.Select(id2);
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.anim.move-earlier");

        // S2's Fade animation should now be at index 0.
        Assert.Equal(AnimationPreset.Fade,   ed.CurrentSlideAnimations[0].Preset);
        Assert.Equal(AnimationPreset.Appear, ed.CurrentSlideAnimations[1].Preset);
    }

    [Fact]
    public void Cmd_MoveLater_ReordersAnimation()
    {
        var (ed, pres) = MakeSession();
        var id1 = pres.Slides[0].Shapes[0].Id;
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = id1 + 1, Name = "S2", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Ellipse,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
        });
        var id2 = id1 + 1;

        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = id1, Preset = AnimationPreset.Appear });
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = id2, Preset = AnimationPreset.Fade  });

        ed.Select(id1);
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.anim.move-later");

        Assert.Equal(AnimationPreset.Fade,   ed.CurrentSlideAnimations[0].Preset);
        Assert.Equal(AnimationPreset.Appear, ed.CurrentSlideAnimations[1].Preset);
    }

    // ── Slide Show callbacks ──────────────────────────────────────────────────────

    [Fact]
    public void Cmd_FromBeginning_InvokesOnStartFromStart()
    {
        var (ed, _) = MakeSession();
        bool fired = false;
        var reg = MakeRegistry(ed, onStart: () => fired = true);
        Exec(reg, "freep.slideshow.from-beginning");
        Assert.True(fired);
    }

    [Fact]
    public void Cmd_FromCurrentSlide_InvokesOnStartFromCurrent()
    {
        var (ed, _) = MakeSession();
        bool fired = false;
        var reg = MakeRegistry(ed, onCurrent: () => fired = true);
        Exec(reg, "freep.slideshow.from-current-slide");
        Assert.True(fired);
    }

    [Fact]
    public void Cmd_CustomShows_InvokesOnCustomShows()
    {
        var (ed, _) = MakeSession();
        bool fired = false;
        var reg = MakeRegistry(ed, onCustomShows: () => fired = true);
        Exec(reg, "freep.slideshow.custom-shows");
        Assert.True(fired);
    }

    [Fact]
    public void Cmd_RehearseTimings_InvokesOnRehearseTimings()
    {
        var (ed, _) = MakeSession();
        bool fired = false;
        var reg = MakeRegistry(ed, onRehearseTimings: () => fired = true);
        Exec(reg, "freep.slideshow.rehearse-timings");
        Assert.True(fired);
    }

    [Fact]
    public void Cmd_RecordTimings_InvokesOnRecordTimings()
    {
        var (ed, _) = MakeSession();
        bool fired = false;
        var reg = MakeRegistry(ed, onRecordTimings: () => fired = true);
        Exec(reg, "freep.slideshow.record-timings");
        Assert.True(fired);
    }

    [Fact]
    public void Cmd_FromBeginning_NullAction_DoesNotThrow()
    {
        var (ed, _) = MakeSession();
        // onStartFromStart is null — should be a no-op, not a NullReferenceException.
        var reg = MakeRegistry(ed, onStart: null, onCurrent: null);
        var ex = Record.Exception(() => Exec(reg, "freep.slideshow.from-beginning"));
        Assert.Null(ex);
    }

    [Fact]
    public void Cmd_AdvanceOnClick_StateFollowsModelDefaultSlideSwitchAndUndo()
    {
        var (editor, presentation) = MakeSession();
        editor.InsertSlide();
        editor.SelectSlide(0);
        var registry = MakeRegistry(editor);
        Assert.True(registry.TryGet("freep.transition.advance-on-click", out var command));
        var stateful = Assert.IsAssignableFrom<IRibbonStatefulCommand>(command);

        Assert.True(stateful.GetState().IsChecked);
        command!.Execute(RibbonCommandContext.Empty);
        Assert.False(editor.CurrentSlideTransition!.AdvanceOnClick);
        Assert.False(stateful.GetState().IsChecked);

        editor.SelectSlide(1);
        Assert.True(stateful.GetState().IsChecked);
        editor.SelectSlide(0);
        Assert.False(stateful.GetState().IsChecked);

        editor.Undo();
        Assert.True(stateful.GetState().IsChecked);
        Assert.Null(editor.CurrentSlideTransition);
        Assert.True(presentation.Slides[0].Transition is null);
    }

    // ── All expected ids are registered ───────────────────────────────────────────

    [Theory]
    [InlineData("freep.transition.none")]
    [InlineData("freep.transition.fade")]
    [InlineData("freep.transition.push")]
    [InlineData("freep.transition.wipe")]
    [InlineData("freep.transition.split")]
    [InlineData("freep.transition.box")]
    [InlineData("freep.transition.doors")]
    [InlineData("freep.transition.reveal")]
    [InlineData("freep.transition.flash")]
    [InlineData("freep.transition.morph")]
    [InlineData("freep.transition.cut")]
    [InlineData("freep.transition.cover")]
    [InlineData("freep.transition.uncover")]
    [InlineData("freep.transition.blinds")]
    [InlineData("freep.transition.comb")]
    [InlineData("freep.transition.random-bars")]
    [InlineData("freep.transition.strips")]
    [InlineData("freep.transition.wheel-reverse")]
    [InlineData("freep.transition.gallery")]
    [InlineData("freep.transition.conveyor")]
    [InlineData("freep.transition.pan")]
    [InlineData("freep.transition.window")]
    [InlineData("freep.transition.dissolve")]
    [InlineData("freep.transition.zoom")]
    [InlineData("freep.transition.wheel")]
    [InlineData("freep.transition.duration")]
    [InlineData("freep.transition.advance-on-click")]
    [InlineData("freep.transition.advance-after")]
    [InlineData("freep.transition.apply-all")]
    [InlineData("freep.slideshow.from-beginning")]
    [InlineData("freep.slideshow.from-current-slide")]
    [InlineData("freep.slideshow.rehearse-timings")]
    [InlineData("freep.slideshow.record-timings")]
    [InlineData("freep.slideshow.custom-shows")]
    [InlineData("freep.anim.entrance.appear")]
    [InlineData("freep.anim.entrance.fade")]
    [InlineData("freep.anim.entrance.fly-in")]
    [InlineData("freep.anim.entrance.wipe")]
    [InlineData("freep.anim.entrance.zoom")]
    [InlineData("freep.anim.entrance.split")]
    [InlineData("freep.anim.entrance.blinds")]
    [InlineData("freep.anim.entrance.checkerboard")]
    [InlineData("freep.anim.entrance.box")]
    [InlineData("freep.anim.entrance.circle")]
    [InlineData("freep.anim.entrance.diamond")]
    [InlineData("freep.anim.entrance.plus")]
    [InlineData("freep.anim.entrance.strips")]
    [InlineData("freep.anim.entrance.wedge")]
    [InlineData("freep.anim.entrance.wheel")]
    [InlineData("freep.anim.entrance.random-bars")]
    [InlineData("freep.anim.emphasis.pulse")]
    [InlineData("freep.anim.emphasis.spin")]
    [InlineData("freep.anim.emphasis.grow-shrink")]
    [InlineData("freep.anim.exit.disappear")]
    [InlineData("freep.anim.exit.fade-out")]
    [InlineData("freep.anim.exit.fly-out")]
    [InlineData("freep.anim.exit.wipe")]
    [InlineData("freep.anim.exit.split")]
    [InlineData("freep.anim.exit.zoom-out")]
    [InlineData("freep.anim.exit.blinds")]
    [InlineData("freep.anim.exit.checkerboard")]
    [InlineData("freep.anim.exit.box")]
    [InlineData("freep.anim.exit.circle")]
    [InlineData("freep.anim.exit.diamond")]
    [InlineData("freep.anim.exit.plus")]
    [InlineData("freep.anim.exit.strips")]
    [InlineData("freep.anim.exit.wedge")]
    [InlineData("freep.anim.exit.wheel")]
    [InlineData("freep.anim.exit.random-bars")]
    [InlineData("freep.anim.none")]
    [InlineData("freep.anim.trigger")]
    [InlineData("freep.anim.duration")]
    [InlineData("freep.anim.delay")]
    [InlineData("freep.anim.move-earlier")]
    [InlineData("freep.anim.move-later")]
    [InlineData("freep.anim.pane")]
    public void AllNewCommandIds_AreRegistered(string commandId)
    {
        var (ed, _) = MakeSession();
        var reg = MakeRegistry(ed);
        bool found = reg.TryGet(commandId, out _);
        Assert.True(found, $"Command '{commandId}' was not registered.");
    }

    private static string FindRepoFile(params string[] parts) =>
        Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"), Path.Combine(parts));

}
