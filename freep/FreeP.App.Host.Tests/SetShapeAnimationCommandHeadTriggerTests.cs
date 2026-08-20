using FreeP.App.Compositor;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// freep-animation-timing F1: editing the main sequence's own head animation's Start/Trigger
/// through the Animation Pane (AnimationPanePlanner.TryApplyTimingMutation -> EditingSession.SetAnimation
/// -> SetShapeAnimationCommand) must not be able to leave the stored Trigger on With/After Previous.
/// The head is always played On Click by both live playback (SlideShowController.BuildSteps:
/// "current is null" forces a new click-step) and the package writer
/// (PptxPackageWriter.BuildClickGroupEl: the first item of every click-group is force-written as
/// OnClick), so a stored With/After Previous on the head is unplayable and gets silently discarded
/// on save. RemoveShapeAnimationCommand and ReorderShapeAnimationCommand already correct a newly
/// promoted head back to On Click via ShapeAnimationAnchorFix.NormalizeMainSequenceHead;
/// SetShapeAnimationCommand must do the same.
/// </summary>
public class SetShapeAnimationCommandHeadTriggerTests
{
    private static Presentation BuildTestPresentation()
    {
        var pres = Presentation.CreateEmpty();
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

    [Fact]
    public void SetShapeAnimationCommand_SettingHeadTriggerToAfterPrevious_IsNormalizedBackToOnClick()
    {
        var pres = Presentation.CreateEmpty();
        var head = new ShapeAnimation { ShapeId = 1, Trigger = AnimationTrigger.OnClick };
        pres.Slides[0].Animations.Add(head);

        // User picks "After Previous" for the slide's very first (only) animation.
        var updated = new ShapeAnimation { ShapeId = 1, Trigger = AnimationTrigger.AfterPrevious };
        var cmd = new SetShapeAnimationCommand(0, 0, updated);
        cmd.Apply(pres);

        var anims = pres.Slides[0].Animations;
        Assert.Single(anims);
        // The model must agree with what will actually play and what will actually be saved: On Click.
        Assert.Equal(AnimationTrigger.OnClick, anims[0].Trigger);
        // The stored animation instance itself was corrected in place (the pane reads this field directly).
        Assert.Equal(AnimationTrigger.OnClick, updated.Trigger);

        cmd.Revert(pres);
        Assert.Equal(AnimationTrigger.OnClick, anims[0].Trigger);

        // Redo must re-force On Click, not resurrect the discarded After Previous choice.
        cmd.Apply(pres);
        Assert.Equal(AnimationTrigger.OnClick, anims[0].Trigger);
    }

    [Fact]
    public void SetShapeAnimationCommand_SettingHeadTriggerToWithPrevious_IsNormalizedBackToOnClick()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = 1, Trigger = AnimationTrigger.OnClick });

        var updated = new ShapeAnimation { ShapeId = 1, Trigger = AnimationTrigger.WithPrevious };
        var cmd = new SetShapeAnimationCommand(0, 0, updated);
        cmd.Apply(pres);

        Assert.Equal(AnimationTrigger.OnClick, pres.Slides[0].Animations[0].Trigger);
    }

    [Fact]
    public void SetShapeAnimationCommand_SettingNonHeadTrigger_LeavesHeadAndUserChoiceUntouched()
    {
        // Sibling/no-regression case: a *non*-head animation's own Trigger edit must be honored
        // as-is, and must not perturb the unrelated head's stored trigger.
        var pres = Presentation.CreateEmpty();
        var head = new ShapeAnimation { ShapeId = 1, Trigger = AnimationTrigger.OnClick };
        var second = new ShapeAnimation { ShapeId = 2, Trigger = AnimationTrigger.OnClick };
        pres.Slides[0].Animations.Add(head);
        pres.Slides[0].Animations.Add(second);

        var updatedSecond = new ShapeAnimation { ShapeId = 2, Trigger = AnimationTrigger.AfterPrevious };
        var cmd = new SetShapeAnimationCommand(0, 1, updatedSecond);
        cmd.Apply(pres);

        var anims = pres.Slides[0].Animations;
        Assert.Equal(AnimationTrigger.OnClick, anims[0].Trigger);
        Assert.Equal(AnimationTrigger.AfterPrevious, anims[1].Trigger);

        cmd.Revert(pres);
        Assert.Equal(AnimationTrigger.OnClick, anims[0].Trigger);
        Assert.Equal(AnimationTrigger.OnClick, anims[1].Trigger);
    }

    [Fact]
    public void AnimationPanePlanner_SettingFirstAnimationStartToAfterPrevious_ModelAndPaneAgreeOnClick()
    {
        // End-to-end through the real production call path the finding names:
        // AnimationPanePlanner.BuildTriggerMutationPlan/TryApplyTimingMutation -> EditingSession.SetAnimation
        // -> SetShapeAnimationCommand, exactly what runs when the user changes the Start dropdown
        // on the topmost row of the Animation Pane.
        var pres = BuildTestPresentation();
        pres.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = 1, Trigger = AnimationTrigger.OnClick });
        var bus = new PresentationCommandBus(pres);
        var session = new EditingSession(pres, bus);

        // selectedTriggerIndex 2 == AnimationTrigger.AfterPrevious (AnimationPanePlanner.TryGetTrigger).
        var plan = AnimationPanePlanner.BuildTriggerMutationPlan(session.CurrentSlideAnimations, 0, 2);
        Assert.True(plan.ShouldApply);
        Assert.Equal(AnimationTrigger.AfterPrevious, plan.Trigger);

        var applied = AnimationPanePlanner.TryApplyTimingMutation(session, plan);
        Assert.True(applied);

        // Both the underlying model and whatever the pane reads next must show On Click --
        // never a value that silently reverts on save without telling the user.
        Assert.Equal(AnimationTrigger.OnClick, session.CurrentSlideAnimations[0].Trigger);
        Assert.Equal(AnimationTrigger.OnClick, pres.Slides[0].Animations[0].Trigger);
    }
}
