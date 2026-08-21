using System.IO;
using System.Linq;
using System.Xml.Linq;
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
    public void RoundTrip_SplitTransition_PreservesAxisAndInOutDirection()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind = TransitionKind.Split,
            SplitOrientation = TransitionDirection.Vertical,
            Direction = TransitionDirection.Out,
            DurationMs = 600,
        };

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(TransitionKind.Split, t!.Kind);
        Assert.Equal(TransitionDirection.Vertical, t.SplitOrientation);
        Assert.Equal(TransitionDirection.Out, t.Direction);
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
        Assert.Equal(1000, anims[0].DurationMs); // AB1: preset animation DurationMs must survive round-trip
        // Animation 2: we wrote it as AfterPrevious so it should come back as AfterPrevious or similar
        Assert.Equal(2u, anims[1].ShapeId);
        Assert.Equal(750, anims[1].DurationMs);  // AB1: second preset animation DurationMs must survive round-trip
    }

    /// <summary>AB1 regression: preset animation DurationMs was silently reset to 500ms default.</summary>
    [Fact]
    public void RoundTrip_PresetAnimation_DurationMs_IsExact()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = slide.Shapes[0].Id,
            Kind       = AnimationKind.Entrance,
            Preset     = AnimationPreset.Appear,
            Trigger    = AnimationTrigger.OnClick,
            DurationMs = 2000, // non-default value that must survive round-trip
        });

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        Assert.Single(loaded.Slides[0].Animations);
        Assert.Equal(2000, loaded.Slides[0].Animations[0].DurationMs);
    }

    // ── AC1 regression: p14:dur in mc:AlternateContent (not bare dur on p:transition) ───────────

    private static readonly XNamespace MC  = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";
    private static readonly XNamespace P   = "http://schemas.openxmlformats.org/presentationml/2006/main";

    /// <summary>
    /// AC1: transition DurationMs=800 must survive round-trip AND be written as p14:dur inside
    /// mc:AlternateContent (not a bare "dur" attribute on p:transition, which is invalid per ECMA-376).
    /// A bare dur on CT_SlideTransition is flagged by OpenXmlValidator; p14:dur is the correct form.
    /// </summary>
    [Fact]
    public void RoundTrip_Transition_DurationMs_IsExact_And_Uses_P14Dur()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind       = TransitionKind.Fade,
            DurationMs = 800, // mid-bucket value that previously rounded to 750 (spd="med")
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        // ── XML structure assertion: must use mc:AlternateContent + p14:dur (not bare dur) ──
        ms.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        var slideEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        Assert.NotNull(slideEntry);
        XDocument slideXml;
        using (var entryStream = slideEntry!.Open())
            slideXml = XDocument.Load(entryStream);

        // There must be an mc:AlternateContent element at the slide root (not a bare p:transition)
        var altContent = slideXml.Root!.Elements(MC + "AlternateContent").FirstOrDefault();
        Assert.NotNull(altContent); // AC1: mc:AlternateContent must be present

        // The mc:Choice must have p:transition with p14:dur=800 (namespaced, not bare dur)
        var choice = altContent!.Element(MC + "Choice");
        Assert.NotNull(choice);
        Assert.Equal("p14", choice!.Attribute("Requires")?.Value);
        var choiceTrans = choice.Element(P + "transition");
        Assert.NotNull(choiceTrans);
        Assert.Equal("800", choiceTrans!.Attribute(P14 + "dur")?.Value); // p14:dur present and correct
        Assert.Null(choiceTrans.Attribute("dur"));                        // no bare dur on p:transition

        // The mc:Fallback must have a p:transition with only spd (legacy degradation)
        var fallback = altContent.Element(MC + "Fallback");
        Assert.NotNull(fallback);
        var fallbackTrans = fallback!.Element(P + "transition");
        Assert.NotNull(fallbackTrans);
        Assert.NotNull(fallbackTrans!.Attribute("spd")); // legacy spd present
        Assert.Null(fallbackTrans.Attribute("dur"));      // no bare dur in fallback either

        // ── Round-trip assertion: DurationMs=800 must come back exactly ──
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);
        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(TransitionKind.Fade, t!.Kind);
        Assert.Equal(800, t.DurationMs); // must NOT be quantized to 750 (spd="med")
    }

    /// <summary>AC1: a DurationMs that maps exactly to a legacy spd bucket still round-trips
    /// precisely (via p14:dur in mc:Choice, not just spd quantization).</summary>
    [Fact]
    public void ReadTransition_LegacySpdMappable_RoundTripsExactly()
    {
        // 750ms maps exactly to spd="med". Verify it round-trips as 750 via p14:dur, not as
        // a quantized spd value (both are 750 here, but p14:dur is the precision path).
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind       = TransitionKind.Fade,
            DurationMs = 750, // exactly maps to spd="med"
        };
        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);
        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(750, t!.DurationMs); // exact round-trip via p14:dur
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

    // ── Click-group anchor trigger correction (freep-animation F1; superseded round-160 T2) ──
    //
    // These two tests originally asserted that promoting a different animation into a
    // click-group's head slot (by removing or reordering) forced its stored Trigger to On Click.
    // Round 160 fixed PptxPackageWriter.BuildClickGroupEl to stop forcing a click-group head's
    // *authored* trigger to On Click, because real PowerPoint allows the very first animation of
    // a sequence to be authored as With/After Previous and have it auto-play with no click. A
    // promoted head is indistinguishable from an authored one at the file/model level -- and
    // PowerPoint itself does not rewrite a surviving animation's Start setting when the one ahead
    // of it in its group is deleted or reordered away. So forcing OnClick on promotion here would
    // have re-introduced, on the model side, exactly the bug the writer fix closed on the save
    // side. These tests now assert the opposite: a promoted head keeps its own stored trigger.

    [Fact]
    public void RemoveShapeAnimationCommand_PromotedHeadKeepsOwnTrigger_AndRevertRestoresList()
    {
        var pres = Presentation.CreateEmpty();
        var a0 = new ShapeAnimation { ShapeId = 10, Trigger = AnimationTrigger.OnClick };
        var a1 = new ShapeAnimation { ShapeId = 11, Trigger = AnimationTrigger.WithPrevious };
        pres.Slides[0].Animations.Add(a0);
        pres.Slides[0].Animations.Add(a1);

        // Removing the anchor (index 0) promotes a1 to be the new first main-sequence item.
        var cmd = new RemoveShapeAnimationCommand(0, 0);
        cmd.Apply(pres);

        var anims = pres.Slides[0].Animations;
        Assert.Single(anims);
        Assert.Equal(11u, anims[0].ShapeId);
        Assert.Equal(AnimationTrigger.WithPrevious, anims[0].Trigger);
        // The promoted animation instance itself was never mutated.
        Assert.Equal(AnimationTrigger.WithPrevious, a1.Trigger);

        cmd.Revert(pres);
        Assert.Equal(2, anims.Count);
        Assert.Equal(AnimationTrigger.OnClick,     anims[0].Trigger);
        Assert.Equal(AnimationTrigger.WithPrevious, anims[1].Trigger);
        Assert.Same(a1, anims[1]);
    }

    [Fact]
    public void RemoveShapeAnimationCommand_RemovingNonHeadItem_LeavesHeadTriggerUntouched()
    {
        var pres = Presentation.CreateEmpty();
        var a0 = new ShapeAnimation { ShapeId = 10, Trigger = AnimationTrigger.OnClick };
        var a1 = new ShapeAnimation { ShapeId = 11, Trigger = AnimationTrigger.WithPrevious };
        var a2 = new ShapeAnimation { ShapeId = 12, Trigger = AnimationTrigger.OnClick };
        pres.Slides[0].Animations.Add(a0);
        pres.Slides[0].Animations.Add(a1);
        pres.Slides[0].Animations.Add(a2);

        // Removing a2 (not the anchor) must not touch a0's or a1's stored trigger.
        var cmd = new RemoveShapeAnimationCommand(0, 2);
        cmd.Apply(pres);

        var anims = pres.Slides[0].Animations;
        Assert.Equal(2, anims.Count);
        Assert.Equal(AnimationTrigger.OnClick,      anims[0].Trigger);
        Assert.Equal(AnimationTrigger.WithPrevious, anims[1].Trigger);

        cmd.Revert(pres);
        Assert.Equal(3, anims.Count);
        Assert.Equal(AnimationTrigger.OnClick, anims[2].Trigger);
    }

    [Fact]
    public void ReorderShapeAnimationCommand_PromotedHeadKeepsOwnTrigger_AndRevertRestoresIt()
    {
        var pres = Presentation.CreateEmpty();
        var a0 = new ShapeAnimation { ShapeId = 10, Trigger = AnimationTrigger.OnClick };
        var a1 = new ShapeAnimation { ShapeId = 11, Trigger = AnimationTrigger.AfterPrevious };
        pres.Slides[0].Animations.Add(a0);
        pres.Slides[0].Animations.Add(a1);

        // Dragging a1 above a0 promotes it to the new first main-sequence item.
        var cmd = new ReorderShapeAnimationCommand(0, 1, 0);
        cmd.Apply(pres);

        var anims = pres.Slides[0].Animations;
        Assert.Equal(11u, anims[0].ShapeId);
        Assert.Equal(AnimationTrigger.AfterPrevious, anims[0].Trigger);
        Assert.Equal(10u, anims[1].ShapeId);
        Assert.Equal(AnimationTrigger.OnClick, anims[1].Trigger);

        cmd.Revert(pres);
        Assert.Equal(10u, anims[0].ShapeId);
        Assert.Equal(AnimationTrigger.OnClick, anims[0].Trigger);
        Assert.Equal(11u, anims[1].ShapeId);
        Assert.Equal(AnimationTrigger.AfterPrevious, anims[1].Trigger);
    }

    [Fact]
    public void ReorderShapeAnimationCommand_ReorderingAwayFromHead_LeavesTriggersUntouched()
    {
        var pres = Presentation.CreateEmpty();
        var a0 = new ShapeAnimation { ShapeId = 10, Trigger = AnimationTrigger.OnClick };
        var a1 = new ShapeAnimation { ShapeId = 11, Trigger = AnimationTrigger.WithPrevious };
        var a2 = new ShapeAnimation { ShapeId = 12, Trigger = AnimationTrigger.OnClick };
        pres.Slides[0].Animations.Add(a0);
        pres.Slides[0].Animations.Add(a1);
        pres.Slides[0].Animations.Add(a2);

        // Swap the two non-head items; the anchor at index 0 never moves.
        var cmd = new ReorderShapeAnimationCommand(0, 2, 1);
        cmd.Apply(pres);

        var anims = pres.Slides[0].Animations;
        Assert.Equal(10u, anims[0].ShapeId);
        Assert.Equal(AnimationTrigger.OnClick, anims[0].Trigger);
        Assert.Equal(12u, anims[1].ShapeId);
        Assert.Equal(AnimationTrigger.OnClick, anims[1].Trigger);
        Assert.Equal(11u, anims[2].ShapeId);
        Assert.Equal(AnimationTrigger.WithPrevious, anims[2].Trigger);
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

    // ── AC2: preset animation structural cTn dur — multi-behavior precision ──────

    /// <summary>
    /// AC2: ReadBuildItem must read the duration from the structural animCTn level (sibling of p:set),
    /// NOT from an arbitrary FirstOrDefault(dur&gt;1) descendant which could pick a sub-behavior's dur.
    /// This tests that multiple animations with different durations each survive round-trip exactly.
    /// </summary>
    [Fact]
    public void RoundTrip_PresetAnimation_MultipleDurations_EachExact()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 2, Name = "S2", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Ellipse,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
        });

        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = slide.Shapes[0].Id,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.FlyIn,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 300,  // distinct from default 500
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Spin,
            Trigger = AnimationTrigger.AfterPrevious,
            DelayMs = 100,
            DurationMs = 1200, // distinct and non-standard
        });

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var anims = loaded.Slides[0].Animations;
        Assert.Equal(2, anims.Count);
        Assert.Equal(300,  anims[0].DurationMs); // AC2: must not bleed from anim[1]
        Assert.Equal(1200, anims[1].DurationMs); // AC2: must not bleed from anim[0]
    }

    // ── AC3: DurationMs=1 must not be confused with the p:set sentinel dur="1" ───

    /// <summary>
    /// AC3: a legitimate preset animation with DurationMs=1 must round-trip as 1 (not default 500).
    /// The p:set sentinel cTn also has dur="1", but it is excluded by structural scoping (it lives
    /// inside a p:set element, not as a bare childTnLst child), so the 1ms animation is accepted.
    /// </summary>
    [Fact]
    public void RoundTrip_PresetAnimation_DurationMs_One_IsExact()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = slide.Shapes[0].Id,
            Kind       = AnimationKind.Entrance,
            Preset     = AnimationPreset.Appear,
            Trigger    = AnimationTrigger.OnClick,
            DurationMs = 1, // edge case: 1ms must not be filtered out by dur>1 sentinel guard
        });

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        Assert.Single(loaded.Slides[0].Animations);
        Assert.Equal(1, loaded.Slides[0].Animations[0].DurationMs); // AC3: not 500 (the old default)
    }

    // ── AF1: import from real PowerPoint — dur lives inside p:cBhvr, not bare p:cTn ──────────

    /// <summary>
    /// Wheel preset metadata emitted by PowerPoint on p:animEffect/filter must flow into ShapeAnimation.
    /// </summary>
    [Fact]
    public void Import_RealPowerPoint_WheelAnimation_SpokeCount_ReadFromAnimEffectFilter()
    {
        const string presNs = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace pNs = presNs;

        var buildPar = new XElement(pNs + "par",
            new XElement(pNs + "cTn",
                new XAttribute("presetClass", "entr"),
                new XAttribute("presetID", "19"),
                new XAttribute("presetSubtype", "0"),
                new XAttribute("fill", "hold"),
                new XAttribute("grpId", "0"),
                new XAttribute("nodeType", "withEffect"),
                new XElement(pNs + "stCondLst",
                    new XElement(pNs + "cond", new XAttribute("delay", "indefinite"))),
                new XElement(pNs + "childTnLst",
                    new XElement(pNs + "par",
                        new XElement(pNs + "cTn",
                            new XAttribute("fill", "hold"),
                            new XElement(pNs + "stCondLst",
                                new XElement(pNs + "cond", new XAttribute("delay", "0"))),
                            new XElement(pNs + "childTnLst",
                                new XElement(pNs + "animEffect",
                                    new XAttribute("filter", "wheel(spokes=8)"),
                                    new XElement(pNs + "cBhvr",
                                        new XElement(pNs + "cTn",
                                            new XAttribute("dur", "2000")),
                                        new XElement(pNs + "tgtEl",
                                            new XElement(pNs + "spTgt",
                                                new XAttribute("spid", "1")))))))))));

        var timingEl = BuildMinimalTimingWithBuildPar(pNs, buildPar);
        var pptxBytes = BuildMinimalPptxWithTiming(timingEl);
        using var ms = new MemoryStream(pptxBytes);

        var loaded = PptxPackageReader.Read(ms);

        Assert.Single(loaded.Slides[0].Animations);
        var anim = loaded.Slides[0].Animations[0];
        Assert.Equal(AnimationPreset.Wheel, anim.Preset);
        Assert.Equal(8, anim.WheelSpokeCount);
    }

    /// <summary>
    /// AF1: ReadBuildItem must read DurationMs from a real-PowerPoint-shaped preset animation where
    /// the inner childTnLst contains p:animEffect (not a bare p:cTn), and the actual duration is on
    /// the p:cTn inside p:animEffect &gt; p:cBhvr. The AC2 structural-primary path finds no direct
    /// p:cTn child; the AF1 bounded-descendant fallback must find the dur=2000 p:cTn (not the
    /// dur="1" sentinel inside p:set), and return DurationMs=2000 (not the 500ms default).
    /// </summary>
    [Fact]
    public void Import_RealPowerPoint_PresetAnimation_DurationMs_ReadFromCBhvr()
    {
        // Construct a minimal p:timing XML tree shaped like real PowerPoint emits for a
        // preset entrance animation with dur=2000 on the p:cTn inside p:animEffect/p:cBhvr.
        //
        // Structure (real PowerPoint nesting):
        //   p:par (buildPar)
        //     p:cTn [presetClass="entr" presetID="1" fill="hold" nodeType="withEffect"]
        //       p:stCondLst > p:cond delay="indefinite"
        //       p:childTnLst
        //         p:par
        //           p:cTn [fill="hold"]
        //             p:stCondLst > p:cond delay="0"
        //             p:childTnLst          ← innerChildTnLst
        //               p:animEffect        ← real PowerPoint behavior element (not bare p:cTn)
        //                 p:cBhvr
        //                   p:cTn [dur="2000"]   ← AF1 fallback target
        //                   p:tgtEl > p:spTgt spid="1"
        //               p:set               ← sentinel: p:cBhvr > p:cTn dur="1"
        //                 p:cBhvr
        //                   p:cTn [dur="1"]  ← must NOT be picked
        //                   p:tgtEl > p:spTgt spid="1"

        const string presNs  = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace  pNs      = presNs;

        var buildPar = new XElement(pNs + "par",
            new XElement(pNs + "cTn",
                new XAttribute("presetClass", "entr"),
                new XAttribute("presetID", "1"),
                new XAttribute("presetSubtype", "0"),
                new XAttribute("fill", "hold"),
                new XAttribute("grpId", "0"),
                new XAttribute("nodeType", "withEffect"),
                new XElement(pNs + "stCondLst",
                    new XElement(pNs + "cond", new XAttribute("delay", "indefinite"))),
                new XElement(pNs + "childTnLst",
                    new XElement(pNs + "par",
                        new XElement(pNs + "cTn",
                            new XAttribute("fill", "hold"),
                            new XElement(pNs + "stCondLst",
                                new XElement(pNs + "cond", new XAttribute("delay", "0"))),
                            new XElement(pNs + "childTnLst",
                                // Real PowerPoint: p:animEffect with dur on p:cBhvr/p:cTn
                                new XElement(pNs + "animEffect",
                                    new XElement(pNs + "cBhvr",
                                        new XElement(pNs + "cTn",
                                            new XAttribute("dur", "2000")),
                                        new XElement(pNs + "tgtEl",
                                            new XElement(pNs + "spTgt",
                                                new XAttribute("spid", "1"))))),
                                // p:set sentinel: dur="1" must NOT be picked by fallback
                                new XElement(pNs + "set",
                                    new XElement(pNs + "cBhvr",
                                        new XElement(pNs + "cTn",
                                            new XAttribute("dur", "1"),
                                            new XAttribute("fill", "hold")),
                                        new XElement(pNs + "tgtEl",
                                            new XElement(pNs + "spTgt",
                                                new XAttribute("spid", "1")))))))))));

        // Wrap in minimal p:timing > p:tnLst > ... > p:seq (mainSeq) > ... > click group
        var timingEl = BuildMinimalTimingWithBuildPar(pNs, buildPar);

        // ReadAnimations is internal; invoke via the full Read pipeline using a minimal in-memory PPTX.
        var pptxBytes = BuildMinimalPptxWithTiming(timingEl);
        using var ms = new System.IO.MemoryStream(pptxBytes);
        var loaded = PptxPackageReader.Read(ms);

        Assert.Single(loaded.Slides[0].Animations);
        var anim = loaded.Slides[0].Animations[0];
        // AF1: must read 2000 from the p:cBhvr/p:cTn under p:animEffect, NOT the 500ms default.
        Assert.Equal(2000, anim.DurationMs);
        // Sanity: sentinel p:set dur="1" was not mistakenly picked.
        Assert.NotEqual(1, anim.DurationMs);
    }

    /// <summary>
    /// AF2: real PowerPoint can put a preset effect's start offset on the behavior cTn, not
    /// the outer withEffect cTn. Import must preserve that DelayMs without mistaking the p:set
    /// sentinel timing for the animation behavior.
    /// </summary>
    [Fact]
    public void Import_RealPowerPoint_PresetAnimation_DelayMs_ReadFromCBhvr()
    {
        const string presNs = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace pNs = presNs;

        var buildPar = new XElement(pNs + "par",
            new XElement(pNs + "cTn",
                new XAttribute("presetClass", "entr"),
                new XAttribute("presetID", "1"),
                new XAttribute("presetSubtype", "0"),
                new XAttribute("fill", "hold"),
                new XAttribute("grpId", "0"),
                new XAttribute("nodeType", "withEffect"),
                new XElement(pNs + "stCondLst",
                    new XElement(pNs + "cond", new XAttribute("delay", "indefinite"))),
                new XElement(pNs + "childTnLst",
                    new XElement(pNs + "par",
                        new XElement(pNs + "cTn",
                            new XAttribute("fill", "hold"),
                            new XElement(pNs + "stCondLst",
                                new XElement(pNs + "cond", new XAttribute("delay", "0"))),
                            new XElement(pNs + "childTnLst",
                                new XElement(pNs + "animEffect",
                                    new XElement(pNs + "cBhvr",
                                        new XElement(pNs + "cTn",
                                            new XAttribute("dur", "2000"),
                                            new XElement(pNs + "stCondLst",
                                                new XElement(pNs + "cond", new XAttribute("delay", "750")))),
                                        new XElement(pNs + "tgtEl",
                                            new XElement(pNs + "spTgt",
                                                new XAttribute("spid", "1"))))),
                                new XElement(pNs + "set",
                                    new XElement(pNs + "cBhvr",
                                        new XElement(pNs + "cTn",
                                            new XAttribute("dur", "1"),
                                            new XAttribute("fill", "hold"),
                                            new XElement(pNs + "stCondLst",
                                                new XElement(pNs + "cond", new XAttribute("delay", "0")))),
                                        new XElement(pNs + "tgtEl",
                                            new XElement(pNs + "spTgt",
                                                new XAttribute("spid", "1")))))))))));

        var timingEl = BuildMinimalTimingWithBuildPar(pNs, buildPar);
        var pptxBytes = BuildMinimalPptxWithTiming(timingEl);
        using var ms = new MemoryStream(pptxBytes);

        var loaded = PptxPackageReader.Read(ms);

        Assert.Single(loaded.Slides[0].Animations);
        var anim = loaded.Slides[0].Animations[0];
        Assert.Equal(AnimationTrigger.OnClick, anim.Trigger);
        Assert.Equal(750, anim.DelayMs);
        Assert.Equal(2000, anim.DurationMs);
    }

    // ── AF1 test helpers ─────────────────────────────────────────────────────────────

    /// <summary>Wraps a build-par in the minimal p:timing tree ReadAnimations expects.</summary>
    private static XElement BuildMinimalTimingWithBuildPar(XNamespace pNs, XElement buildPar)
    {
        // p:timing > p:tnLst > p:par (interactive root) > p:cTn > p:childTnLst >
        //   p:seq (mainSeq) > p:cTn[nodeType=mainSeq] > p:childTnLst >
        //     p:par (click group) > p:cTn > p:stCondLst/p:cond + p:childTnLst >
        //       buildPar
        return new XElement(pNs + "timing",
            new XElement(pNs + "tnLst",
                new XElement(pNs + "par",
                    new XElement(pNs + "cTn",
                        new XElement(pNs + "childTnLst",
                            new XElement(pNs + "seq",
                                new XElement(pNs + "cTn",
                                    new XAttribute("nodeType", "mainSeq"),
                                    new XElement(pNs + "childTnLst",
                                        new XElement(pNs + "par",
                                            new XElement(pNs + "cTn",
                                                new XElement(pNs + "stCondLst",
                                                    new XElement(pNs + "cond",
                                                        new XAttribute("delay", "indefinite"))),
                                                new XElement(pNs + "childTnLst",
                                                    buildPar)))))))))));
    }

    /// <summary>
    /// Builds the smallest valid PPTX zip that contains one slide with the given p:timing element.
    /// PptxPackageReader.Read requires a real OPC/ZIP stream with slide1.xml, presentation.xml,
    /// [Content_Types].xml, and the required relationship files.
    /// </summary>
    private static byte[] BuildMinimalPptxWithTiming(XElement timingEl)
    {
        XNamespace pNs    = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace aNs    = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace rNs    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        // slide1.xml — minimal sld with one shape (spid=1) and the timing tree
        var slideXml = new XDocument(
            new XElement(pNs + "sld",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(pNs + "cSld",
                    new XElement(pNs + "spTree",
                        new XElement(pNs + "nvGrpSpPr",
                            new XElement(pNs + "cNvPr",
                                new XAttribute("id", "1"), new XAttribute("name", "Group 1")),
                            new XElement(pNs + "cNvGrpSpPr"),
                            new XElement(pNs + "nvPr")),
                        new XElement(pNs + "grpSpPr",
                            new XElement(aNs + "xfrm",
                                new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                                new XElement(aNs + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "chExt", new XAttribute("cx", "0"), new XAttribute("cy", "0")))),
                        new XElement(pNs + "sp",
                            new XElement(pNs + "nvSpPr",
                                new XElement(pNs + "cNvPr",
                                    new XAttribute("id", "1"), new XAttribute("name", "Shape1")),
                                new XElement(pNs + "cNvSpPr"),
                                new XElement(pNs + "nvPr")),
                            new XElement(pNs + "spPr",
                                new XElement(aNs + "xfrm",
                                    new XElement(aNs + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                    new XElement(aNs + "ext", new XAttribute("cx", "914400"), new XAttribute("cy", "914400"))),
                                new XElement(aNs + "prstGeom", new XAttribute("prst", "rect"))),
                            new XElement(pNs + "txBody",
                                new XElement(aNs + "bodyPr"),
                                new XElement(aNs + "p"))))),
                timingEl));

        // presentation.xml — minimal, references slide1
        var presXml = new XDocument(
            new XElement(pNs + "presentation",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(pNs + "sldSz",
                    new XAttribute("cx", "9144000"), new XAttribute("cy", "6858000")),
                new XElement(pNs + "notesSz",
                    new XAttribute("cx", "6858000"), new XAttribute("cy", "9144000")),
                new XElement(pNs + "sldIdLst",
                    new XElement(pNs + "sldId",
                        new XAttribute("id", "256"),
                        new XAttribute(rNs + "id", "rId1")))));

        // [Content_Types].xml
        XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypes = new XDocument(
            new XElement(ctNs + "Types",
                new XElement(ctNs + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ctNs + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ctNs + "Override",
                    new XAttribute("PartName", "/ppt/presentation.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml")),
                new XElement(ctNs + "Override",
                    new XAttribute("PartName", "/ppt/slides/slide1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml"))));

        // _rels/.rels
        var rootRels = new XDocument(
            new XElement(pkgRel + "Relationships",
                new XElement(pkgRel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "ppt/presentation.xml"))));

        // ppt/_rels/presentation.xml.rels
        var presRels = new XDocument(
            new XElement(pkgRel + "Relationships",
                new XElement(pkgRel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide"),
                    new XAttribute("Target", "slides/slide1.xml"))));

        // ppt/slides/_rels/slide1.xml.rels — empty (no slideLayout for this minimal test)
        var slideRels = new XDocument(
            new XElement(pkgRel + "Relationships"));

        using var outMs = new System.IO.MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(outMs, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", contentTypes);
            WriteEntry(zip, "_rels/.rels", rootRels);
            WriteEntry(zip, "ppt/presentation.xml", presXml);
            WriteEntry(zip, "ppt/_rels/presentation.xml.rels", presRels);
            WriteEntry(zip, "ppt/slides/slide1.xml", slideXml);
            WriteEntry(zip, "ppt/slides/_rels/slide1.xml.rels", slideRels);
        }
        return outMs.ToArray();
    }

    private static void WriteEntry(System.IO.Compression.ZipArchive zip, string path, XDocument doc)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        doc.Save(stream);
    }
}
