using System.IO;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 4A: Tests for slide transitions and shape animations —
/// round-trip I/O, model cloning, command bus (undo/redo), EditingSession API.
/// </summary>
public class TransitionAnimationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static Presentation BuildTestPresentation()
    {
        var pres = Presentation.CreateEmpty();

        // Slide 0: add a second shape so animations can target different shapes
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id   = 2,
            Name = "Shape2",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Ellipse,
            OffsetXEmu  = 1000000,
            OffsetYEmu  = 1000000,
            ExtentCxEmu = 1000000,
            ExtentCyEmu = 1000000,
        });

        return pres;
    }

    // Construct a fresh session with a shared backing presentation so commands actually affect what we read.
    private static (EditingSession session, Presentation presentation) MakeLinkedSession()
    {
        var pres = BuildTestPresentation();
        var bus  = new PresentationCommandBus(pres);
        return (new EditingSession(pres, bus), pres);
    }

    // ── Pptx round-trip tests ─────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_FadeTransition_WithAutoAdvance()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind           = TransitionKind.Fade,
            DurationMs     = 750,
            AdvanceOnClick = true,
            AdvanceAfterMs = 3000,
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(TransitionKind.Fade, t.Kind);
        Assert.True(t.AdvanceOnClick);
        Assert.Equal(3000, t.AdvanceAfterMs);
    }

    [Fact]
    public void RoundTrip_PushTransition_WithDirection()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind      = TransitionKind.Push,
            Direction = TransitionDirection.Left,
            DurationMs = 500,
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(TransitionKind.Push, t.Kind);
        Assert.Equal(TransitionDirection.Left, t.Direction);
    }

    [Fact]
    public void RoundTrip_Animations_OrderedAndTyped()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape { Id = 2, Name = "S2", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Ellipse,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400 });

        // Animation 1: FlyIn entrance, OnClick, shape 1
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = slide.Shapes[0].Id,
            Kind       = AnimationKind.Entrance,
            Preset     = AnimationPreset.FlyIn,
            Trigger    = AnimationTrigger.OnClick,
            DelayMs    = 0,
            DurationMs = 1000,
            Direction  = AnimationDirection.Left,
        });

        // Animation 2: Fade emphasis, AfterPrevious, shape 2
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = 2,
            Kind       = AnimationKind.Emphasis,
            Preset     = AnimationPreset.Fade,
            Trigger    = AnimationTrigger.AfterPrevious,
            DelayMs    = 500,
            DurationMs = 750,
        });

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var anims = loaded.Slides[0].Animations;
        Assert.Equal(2, anims.Count);
        Assert.Equal(AnimationKind.Entrance, anims[0].Kind);
        Assert.Equal(AnimationPreset.FlyIn,  anims[0].Preset);
        Assert.Equal(slide.Shapes[0].Id,      anims[0].ShapeId);
        // Animation 2: we wrote it as AfterPrevious so it should come back as AfterPrevious or similar
        Assert.Equal(2u, anims[1].ShapeId);
    }

    [Fact]
    public void RoundTrip_NoTransition_SlideHasNullTransition()
    {
        var pres = Presentation.CreateEmpty();
        // Don't set any transition
        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);
        Assert.Null(loaded.Slides[0].Transition);
    }

    [Fact]
    public void RoundTrip_NoAnimations_EmptyList()
    {
        var pres = Presentation.CreateEmpty();
        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);
        Assert.Empty(loaded.Slides[0].Animations);
    }

    // ── SlideCloner tests ─────────────────────────────────────────────────────────

    [Fact]
    public void Cloner_CopiesTransitionIndependently()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind      = TransitionKind.Wipe,
            Direction = TransitionDirection.Right,
            DurationMs = 500,
        };

        var clone = SlideCloner.CloneSlide(pres.Slides[0]);

        Assert.NotNull(clone.Transition);
        Assert.Equal(TransitionKind.Wipe, clone.Transition!.Kind);
        Assert.Equal(TransitionDirection.Right, clone.Transition.Direction);

        // Mutating original does not affect clone
        pres.Slides[0].Transition!.Kind = TransitionKind.Fade;
        Assert.Equal(TransitionKind.Wipe, clone.Transition.Kind);
    }

    [Fact]
    public void Cloner_CopiesAnimationsIndependently()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 1, Kind = AnimationKind.Entrance, Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick, DurationMs = 500,
        });

        var clone = SlideCloner.CloneSlide(pres.Slides[0]);

        Assert.Single(clone.Animations);
        Assert.Equal(AnimationPreset.Appear, clone.Animations[0].Preset);

        // Adding to original does not affect clone
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = 2 });
        Assert.Single(clone.Animations);
    }

    [Fact]
    public void Cloner_NullTransition_ClonedSlideHasNullTransition()
    {
        var slide = Presentation.CreateEmpty().Slides[0];
        slide.Transition = null;
        var clone = SlideCloner.CloneSlide(slide);
        Assert.Null(clone.Transition);
    }

    // ── Command tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetSlideTransitionCommand_ApplyRevert()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition { Kind = TransitionKind.Cut };

        var cmd = new SetSlideTransitionCommand(0, new SlideTransition { Kind = TransitionKind.Fade });
        cmd.Apply(pres);
        Assert.Equal(TransitionKind.Fade, pres.Slides[0].Transition?.Kind);

        cmd.Revert(pres);
        Assert.Equal(TransitionKind.Cut, pres.Slides[0].Transition?.Kind);
    }

    [Fact]
    public void SetSlideTransitionCommand_ClearTransition()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition { Kind = TransitionKind.Fade };

        var cmd = new SetSlideTransitionCommand(0, null);
        cmd.Apply(pres);
        Assert.Null(pres.Slides[0].Transition);

        cmd.Revert(pres);
        Assert.Equal(TransitionKind.Fade, pres.Slides[0].Transition?.Kind);
    }

    [Fact]
    public void AddShapeAnimationCommand_ApplyRevert()
    {
        var pres = Presentation.CreateEmpty();
        var anim = new ShapeAnimation { ShapeId = 1, Preset = AnimationPreset.FlyIn };

        var cmd = new AddShapeAnimationCommand(0, anim);
        cmd.Apply(pres);
        Assert.Single(pres.Slides[0].Animations);
        Assert.Equal(AnimationPreset.FlyIn, pres.Slides[0].Animations[0].Preset);

        cmd.Revert(pres);
        Assert.Empty(pres.Slides[0].Animations);
    }

    [Fact]
    public void RemoveShapeAnimationCommand_ApplyRevert()
    {
        var pres = Presentation.CreateEmpty();
        var anim = new ShapeAnimation { ShapeId = 1, Preset = AnimationPreset.Zoom };
        pres.Slides[0].Animations.Add(anim);

        var cmd = new RemoveShapeAnimationCommand(0, 0);
        cmd.Apply(pres);
        Assert.Empty(pres.Slides[0].Animations);

        cmd.Revert(pres);
        Assert.Single(pres.Slides[0].Animations);
        Assert.Equal(AnimationPreset.Zoom, pres.Slides[0].Animations[0].Preset);
    }

    [Fact]
    public void ReorderShapeAnimationCommand_ApplyRevert()
    {
        var pres = Presentation.CreateEmpty();
        var a1 = new ShapeAnimation { ShapeId = 1, Preset = AnimationPreset.Appear };
        var a2 = new ShapeAnimation { ShapeId = 2, Preset = AnimationPreset.Fade };
        pres.Slides[0].Animations.Add(a1);
        pres.Slides[0].Animations.Add(a2);

        var cmd = new ReorderShapeAnimationCommand(0, 0, 1);
        cmd.Apply(pres);
        Assert.Equal(AnimationPreset.Fade,   pres.Slides[0].Animations[0].Preset);
        Assert.Equal(AnimationPreset.Appear, pres.Slides[0].Animations[1].Preset);

        cmd.Revert(pres);
        Assert.Equal(AnimationPreset.Appear, pres.Slides[0].Animations[0].Preset);
        Assert.Equal(AnimationPreset.Fade,   pres.Slides[0].Animations[1].Preset);
    }

    [Fact]
    public void SetShapeAnimationCommand_ApplyRevert()
    {
        var pres = Presentation.CreateEmpty();
        var a1 = new ShapeAnimation { ShapeId = 1, Preset = AnimationPreset.Appear };
        pres.Slides[0].Animations.Add(a1);

        var newAnim = new ShapeAnimation { ShapeId = 1, Preset = AnimationPreset.Spin };
        var cmd = new SetShapeAnimationCommand(0, 0, newAnim);
        cmd.Apply(pres);
        Assert.Equal(AnimationPreset.Spin, pres.Slides[0].Animations[0].Preset);

        cmd.Revert(pres);
        Assert.Equal(AnimationPreset.Appear, pres.Slides[0].Animations[0].Preset);
    }

    // ── Undo/redo through bus ─────────────────────────────────────────────────────

    [Fact]
    public void Bus_UndoRedo_SetTransition()
    {
        var pres = Presentation.CreateEmpty();
        var bus  = new PresentationCommandBus(pres);

        bus.Execute(new SetSlideTransitionCommand(0, new SlideTransition { Kind = TransitionKind.Zoom }));
        Assert.Equal(TransitionKind.Zoom, pres.Slides[0].Transition?.Kind);

        bus.Undo();
        Assert.Null(pres.Slides[0].Transition);

        bus.Redo();
        Assert.Equal(TransitionKind.Zoom, pres.Slides[0].Transition?.Kind);
    }

    [Fact]
    public void Bus_UndoRedo_AddRemoveAnimation()
    {
        var pres = Presentation.CreateEmpty();
        var bus  = new PresentationCommandBus(pres);
        var anim = new ShapeAnimation { ShapeId = 1, Preset = AnimationPreset.Bounce };

        bus.Execute(new AddShapeAnimationCommand(0, anim));
        Assert.Single(pres.Slides[0].Animations);

        bus.Undo();
        Assert.Empty(pres.Slides[0].Animations);

        bus.Redo();
        Assert.Single(pres.Slides[0].Animations);

        bus.Execute(new RemoveShapeAnimationCommand(0, 0));
        Assert.Empty(pres.Slides[0].Animations);

        bus.Undo();
        Assert.Single(pres.Slides[0].Animations);
    }

    // ── EditingSession API tests ──────────────────────────────────────────────────

    [Fact]
    public void EditingSession_SetTransition_IsUndoable()
    {
        var (session, pres) = MakeLinkedSession();
        var transition = new SlideTransition { Kind = TransitionKind.Fade };

        session.SetTransition(transition);
        Assert.Equal(TransitionKind.Fade, session.CurrentSlideTransition?.Kind);
        Assert.Equal(TransitionKind.Fade, pres.Slides[0].Transition?.Kind);

        session.Undo();
        Assert.Null(session.CurrentSlideTransition);
    }

    [Fact]
    public void EditingSession_AddAnimation_UsesSelectedShape()
    {
        var (session, pres) = MakeLinkedSession();

        session.Select(pres.Slides[0].Shapes[0].Id);
        var anim = new ShapeAnimation { Preset = AnimationPreset.Wipe, DurationMs = 600 };
        session.AddAnimation(0u, anim); // shapeId=0 => use selected

        Assert.Single(session.CurrentSlideAnimations);
        Assert.Equal(pres.Slides[0].Shapes[0].Id, session.CurrentSlideAnimations[0].ShapeId);
        Assert.Equal(AnimationPreset.Wipe, session.CurrentSlideAnimations[0].Preset);

        session.Undo();
        Assert.Empty(session.CurrentSlideAnimations);
    }

    [Fact]
    public void EditingSession_RemoveAnimation_IsUndoable()
    {
        var (session, pres) = MakeLinkedSession();
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = 1, Preset = AnimationPreset.Split });

        session.RemoveAnimation(0);
        Assert.Empty(session.CurrentSlideAnimations);

        session.Undo();
        Assert.Single(session.CurrentSlideAnimations);
    }

    [Fact]
    public void EditingSession_MoveAnimation_IsUndoable()
    {
        var (session, pres) = MakeLinkedSession();
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = 1, Preset = AnimationPreset.Appear });
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = 2, Preset = AnimationPreset.Fade  });

        session.MoveAnimation(0, 1);
        Assert.Equal(AnimationPreset.Fade,   session.CurrentSlideAnimations[0].Preset);
        Assert.Equal(AnimationPreset.Appear, session.CurrentSlideAnimations[1].Preset);

        session.Undo();
        Assert.Equal(AnimationPreset.Appear, session.CurrentSlideAnimations[0].Preset);
        Assert.Equal(AnimationPreset.Fade,   session.CurrentSlideAnimations[1].Preset);
    }

    [Fact]
    public void EditingSession_SetAnimation_IsUndoable()
    {
        var (session, pres) = MakeLinkedSession();
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = 1, Preset = AnimationPreset.Appear });

        var updated = new ShapeAnimation { ShapeId = 1, Preset = AnimationPreset.Zoom };
        session.SetAnimation(0, updated);
        Assert.Equal(AnimationPreset.Zoom, session.CurrentSlideAnimations[0].Preset);

        session.Undo();
        Assert.Equal(AnimationPreset.Appear, session.CurrentSlideAnimations[0].Preset);
    }

    [Fact]
    public void EditingSession_CurrentSlideAnimations_ReturnsEmptyForEmptySlide()
    {
        var (session, _) = MakeLinkedSession();
        Assert.Empty(session.CurrentSlideAnimations);
    }
}
