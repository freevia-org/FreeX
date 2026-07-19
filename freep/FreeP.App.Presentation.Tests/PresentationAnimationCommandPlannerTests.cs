using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationAnimationCommandPlannerTests
{
    private static EditingSession MakeSession(out Presentation presentation, out uint shapeId)
    {
        presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        shapeId = presentation.Slides[0].Shapes[0].Id;
        editor.Select(shapeId);
        return editor;
    }

    [Theory]
    [InlineData("freep.anim.entrance.appear", AnimationKind.Entrance, AnimationPreset.Appear)]
    [InlineData("freep.anim.entrance.fade", AnimationKind.Entrance, AnimationPreset.Fade)]
    [InlineData("freep.anim.entrance.fly-in", AnimationKind.Entrance, AnimationPreset.FlyIn)]
    [InlineData("freep.anim.entrance.wipe", AnimationKind.Entrance, AnimationPreset.Wipe)]
    [InlineData("freep.anim.entrance.zoom", AnimationKind.Entrance, AnimationPreset.Zoom)]
    [InlineData("freep.anim.entrance.split", AnimationKind.Entrance, AnimationPreset.Split)]
    [InlineData("freep.anim.emphasis.pulse", AnimationKind.Emphasis, AnimationPreset.Pulse)]
    [InlineData("freep.anim.emphasis.spin", AnimationKind.Emphasis, AnimationPreset.Spin)]
    [InlineData("freep.anim.emphasis.grow-shrink", AnimationKind.Emphasis, AnimationPreset.Grow)]
    [InlineData("freep.anim.exit.disappear", AnimationKind.Exit, AnimationPreset.Appear)]
    [InlineData("freep.anim.exit.fade-out", AnimationKind.Exit, AnimationPreset.Fade)]
    [InlineData("freep.anim.exit.fly-out", AnimationKind.Exit, AnimationPreset.FlyIn)]
    [InlineData("freep.anim.exit.wipe", AnimationKind.Exit, AnimationPreset.Wipe)]
    [InlineData("freep.anim.exit.split", AnimationKind.Exit, AnimationPreset.Split)]
    [InlineData("freep.anim.exit.zoom-out", AnimationKind.Exit, AnimationPreset.Zoom)]
    public void TryPlan_MapsEffectCommandIdsToTypedEffectIntents(
        string commandId,
        AnimationKind expectedKind,
        AnimationPreset expectedPreset)
    {
        PresentationAnimationCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        plan.Intent.Should().Be(PresentationAnimationCommandIntentKind.AddEffect);
        plan.Kind.Should().Be(expectedKind);
        plan.Preset.Should().Be(expectedPreset);
    }

    [Theory]
    [InlineData("freep.anim.none", PresentationAnimationCommandIntentKind.RemoveSelectedShapeAnimations)]
    [InlineData("freep.anim.trigger", PresentationAnimationCommandIntentKind.SetTrigger)]
    [InlineData("freep.anim.duration", PresentationAnimationCommandIntentKind.SetDuration)]
    [InlineData("freep.anim.delay", PresentationAnimationCommandIntentKind.SetDelay)]
    [InlineData("freep.anim.move-earlier", PresentationAnimationCommandIntentKind.MoveEarlier)]
    [InlineData("freep.anim.move-later", PresentationAnimationCommandIntentKind.MoveLater)]
    [InlineData("freep.anim.pane", PresentationAnimationCommandIntentKind.TogglePane)]
    public void TryPlan_MapsNonEffectCommandIdsToTypedIntents(
        string commandId,
        PresentationAnimationCommandIntentKind expectedIntent)
    {
        PresentationAnimationCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        plan.Intent.Should().Be(expectedIntent);
        plan.Kind.Should().BeNull();
        plan.Preset.Should().BeNull();
    }

    [Fact]
    public void TryPlan_RejectsUnknownCommandId()
    {
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.missing", out var plan)
            .Should()
            .BeFalse();

        plan.Should().BeNull();
    }

    [Fact]
    public void TryApply_EffectCommand_AddsAnimationForSelectedShape()
    {
        var editor = MakeSession(out _, out var shapeId);
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.entrance.fly-in", out var plan)
            .Should()
            .BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        editor.CurrentSlideAnimations.Should().ContainSingle();
        var animation = editor.CurrentSlideAnimations[0];
        animation.ShapeId.Should().Be(shapeId);
        animation.Kind.Should().Be(AnimationKind.Entrance);
        animation.Preset.Should().Be(AnimationPreset.FlyIn);
        animation.Trigger.Should().Be(AnimationTrigger.OnClick);
        animation.DurationMs.Should().Be(PresentationAnimationCommandPlanner.DefaultDurationMs);
    }

    [Fact]
    public void TryApply_NoneCommand_RemovesSelectedShapeAnimationsOnly()
    {
        var editor = MakeSession(out var presentation, out var shapeId);
        presentation.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = shapeId, Preset = AnimationPreset.Appear });
        presentation.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = shapeId, Preset = AnimationPreset.Fade });
        presentation.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = shapeId + 1, Preset = AnimationPreset.Zoom });
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.none", out var plan).Should().BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        editor.CurrentSlideAnimations.Should().ContainSingle();
        editor.CurrentSlideAnimations[0].ShapeId.Should().Be(shapeId + 1);
    }

    [Theory]
    [InlineData("On Click", AnimationTrigger.OnClick)]
    [InlineData("With Previous", AnimationTrigger.WithPrevious)]
    [InlineData("After Previous", AnimationTrigger.AfterPrevious)]
    public void TryApply_TriggerCommand_UsesSelectedRibbonValue(
        string selectedValue,
        AnimationTrigger expectedTrigger)
    {
        var editor = MakeSession(out var presentation, out var shapeId);
        presentation.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = shapeId, Trigger = AnimationTrigger.OnClick });
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.trigger", out var plan).Should().BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan, selectedValue).Should().BeTrue();

        editor.CurrentSlideAnimations[0].Trigger.Should().Be(expectedTrigger);
    }

    [Fact]
    public void TryApply_TimingCommands_UpdateLastSelectedShapeAnimation()
    {
        var editor = MakeSession(out var presentation, out var shapeId);
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = shapeId,
            Preset = AnimationPreset.Appear,
            DurationMs = 500,
            DelayMs = 0,
        });
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.duration", out var durationPlan)
            .Should()
            .BeTrue();
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.delay", out var delayPlan)
            .Should()
            .BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, durationPlan, "1.50s").Should().BeTrue();
        PresentationAnimationCommandPlanner.TryApply(editor, delayPlan, "0.25s").Should().BeTrue();

        editor.CurrentSlideAnimations[0].DurationMs.Should().Be(1500);
        editor.CurrentSlideAnimations[0].DelayMs.Should().Be(250);
    }

    [Fact]
    public void TryApply_TimingCommand_RejectsMissingValueWithoutChangingAnimation()
    {
        var editor = MakeSession(out var presentation, out var shapeId);
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = shapeId,
            DurationMs = 500,
        });
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.duration", out var plan).Should().BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan).Should().BeFalse();

        editor.CurrentSlideAnimations[0].DurationMs.Should().Be(500);
    }

    [Fact]
    public void TryApply_MoveCommands_ReorderLastSelectedShapeAnimation()
    {
        var editor = MakeSession(out var presentation, out var shapeId);
        presentation.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = shapeId, Preset = AnimationPreset.Appear });
        presentation.Slides[0].Animations.Add(new ShapeAnimation { ShapeId = shapeId + 1, Preset = AnimationPreset.Fade });
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.move-later", out var laterPlan)
            .Should()
            .BeTrue();
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.move-earlier", out var earlierPlan)
            .Should()
            .BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, laterPlan).Should().BeTrue();
        editor.CurrentSlideAnimations[0].Preset.Should().Be(AnimationPreset.Fade);
        editor.CurrentSlideAnimations[1].Preset.Should().Be(AnimationPreset.Appear);

        PresentationAnimationCommandPlanner.TryApply(editor, earlierPlan).Should().BeTrue();
        editor.CurrentSlideAnimations[0].Preset.Should().Be(AnimationPreset.Appear);
        editor.CurrentSlideAnimations[1].Preset.Should().Be(AnimationPreset.Fade);
    }

    [Fact]
    public void TryApply_PaneCommand_IsCallbackIntent()
    {
        var editor = MakeSession(out _, out _);
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.pane", out var plan).Should().BeTrue();
        PresentationAnimationCommandPlan? callbackPlan = null;

        PresentationAnimationCommandPlanner.TryApply(editor, plan, onAnimationPane: p => callbackPlan = p)
            .Should()
            .BeTrue();

        callbackPlan.Should().Be(plan);
    }
}
