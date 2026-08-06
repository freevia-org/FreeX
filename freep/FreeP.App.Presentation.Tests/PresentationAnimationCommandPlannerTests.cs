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
    [InlineData("freep.anim.entrance.blinds", AnimationKind.Entrance, AnimationPreset.Blinds)]
    [InlineData("freep.anim.entrance.checkerboard", AnimationKind.Entrance, AnimationPreset.Checkerboard)]
    [InlineData("freep.anim.entrance.box", AnimationKind.Entrance, AnimationPreset.Box)]
    [InlineData("freep.anim.entrance.circle", AnimationKind.Entrance, AnimationPreset.Circle)]
    [InlineData("freep.anim.entrance.diamond", AnimationKind.Entrance, AnimationPreset.Diamond)]
    [InlineData("freep.anim.entrance.plus", AnimationKind.Entrance, AnimationPreset.Plus)]
    [InlineData("freep.anim.entrance.strips", AnimationKind.Entrance, AnimationPreset.Strips)]
    [InlineData("freep.anim.entrance.wedge", AnimationKind.Entrance, AnimationPreset.Wedge)]
    [InlineData("freep.anim.entrance.wheel", AnimationKind.Entrance, AnimationPreset.Wheel)]
    [InlineData("freep.anim.entrance.random-bars", AnimationKind.Entrance, AnimationPreset.RandomBars)]
    [InlineData("freep.anim.emphasis.pulse", AnimationKind.Emphasis, AnimationPreset.Pulse)]
    [InlineData("freep.anim.emphasis.spin", AnimationKind.Emphasis, AnimationPreset.Spin)]
    [InlineData("freep.anim.emphasis.grow-shrink", AnimationKind.Emphasis, AnimationPreset.Grow)]
    [InlineData("freep.anim.emphasis.teeter", AnimationKind.Emphasis, AnimationPreset.Teeter)]
    [InlineData("freep.anim.emphasis.blink", AnimationKind.Emphasis, AnimationPreset.Blink)]
    [InlineData("freep.anim.emphasis.color-pulse", AnimationKind.Emphasis, AnimationPreset.ColorPulse)]
    [InlineData("freep.anim.emphasis.change-color", AnimationKind.Emphasis, AnimationPreset.ChangeColor)]
    [InlineData("freep.anim.emphasis.change-fill-color", AnimationKind.Emphasis, AnimationPreset.ChangeFillColor)]
    [InlineData("freep.anim.emphasis.change-font-color", AnimationKind.Emphasis, AnimationPreset.ChangeColor)]
    [InlineData("freep.anim.emphasis.change-font-size", AnimationKind.Emphasis, AnimationPreset.Grow)]
    [InlineData("freep.anim.emphasis.change-line-color", AnimationKind.Emphasis, AnimationPreset.ChangeLineColor)]
    [InlineData("freep.anim.emphasis.grow-with-color", AnimationKind.Emphasis, AnimationPreset.GrowWithColor)]
    [InlineData("freep.anim.emphasis.wave", AnimationKind.Emphasis, AnimationPreset.Wave)]
    [InlineData("freep.anim.emphasis.shimmer", AnimationKind.Emphasis, AnimationPreset.Shimmer)]
    [InlineData("freep.anim.emphasis.bold", AnimationKind.Emphasis, AnimationPreset.Bold)]
    [InlineData("freep.anim.emphasis.underline", AnimationKind.Emphasis, AnimationPreset.Underline)]
    [InlineData("freep.anim.exit.disappear", AnimationKind.Exit, AnimationPreset.Appear)]
    [InlineData("freep.anim.exit.fade-out", AnimationKind.Exit, AnimationPreset.Fade)]
    [InlineData("freep.anim.exit.fly-out", AnimationKind.Exit, AnimationPreset.FlyIn)]
    [InlineData("freep.anim.exit.wipe", AnimationKind.Exit, AnimationPreset.Wipe)]
    [InlineData("freep.anim.exit.split", AnimationKind.Exit, AnimationPreset.Split)]
    [InlineData("freep.anim.exit.zoom-out", AnimationKind.Exit, AnimationPreset.Zoom)]
    [InlineData("freep.anim.exit.blinds", AnimationKind.Exit, AnimationPreset.Blinds)]
    [InlineData("freep.anim.exit.checkerboard", AnimationKind.Exit, AnimationPreset.Checkerboard)]
    [InlineData("freep.anim.exit.box", AnimationKind.Exit, AnimationPreset.Box)]
    [InlineData("freep.anim.exit.circle", AnimationKind.Exit, AnimationPreset.Circle)]
    [InlineData("freep.anim.exit.diamond", AnimationKind.Exit, AnimationPreset.Diamond)]
    [InlineData("freep.anim.exit.plus", AnimationKind.Exit, AnimationPreset.Plus)]
    [InlineData("freep.anim.exit.strips", AnimationKind.Exit, AnimationPreset.Strips)]
    [InlineData("freep.anim.exit.wedge", AnimationKind.Exit, AnimationPreset.Wedge)]
    [InlineData("freep.anim.exit.wheel", AnimationKind.Exit, AnimationPreset.Wheel)]
    [InlineData("freep.anim.exit.random-bars", AnimationKind.Exit, AnimationPreset.RandomBars)]
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
    [InlineData("freep.anim.motion.reverse", PresentationAnimationCommandIntentKind.ReverseMotionPath)]
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

    [Theory]
    [InlineData("freep.anim.motion.right", PresentationMotionPathPreset.Right)]
    [InlineData("freep.anim.motion.left", PresentationMotionPathPreset.Left)]
    [InlineData("freep.anim.motion.up", PresentationMotionPathPreset.Up)]
    [InlineData("freep.anim.motion.down", PresentationMotionPathPreset.Down)]
    [InlineData("freep.anim.motion.arc-right", PresentationMotionPathPreset.ArcRight)]
    [InlineData("freep.anim.motion.arc-left", PresentationMotionPathPreset.ArcLeft)]
    [InlineData("freep.anim.motion.arc-up", PresentationMotionPathPreset.ArcUp)]
    [InlineData("freep.anim.motion.arc-down", PresentationMotionPathPreset.ArcDown)]
    [InlineData("freep.anim.motion.circle", PresentationMotionPathPreset.Circle)]
    [InlineData("freep.anim.motion.loop", PresentationMotionPathPreset.Loop)]
    [InlineData("freep.anim.motion.s", PresentationMotionPathPreset.S)]
    [InlineData("freep.anim.motion.figure-eight", PresentationMotionPathPreset.FigureEight)]
    public void TryPlan_MapsMotionCommandsToTypedPathPresets(
        string commandId,
        PresentationMotionPathPreset expectedPreset)
    {
        PresentationAnimationCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        plan.Intent.Should().Be(PresentationAnimationCommandIntentKind.AddMotionPath);
        plan.Kind.Should().Be(AnimationKind.Motion);
        plan.Preset.Should().BeNull();
        plan.MotionPathPreset.Should().Be(expectedPreset);
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
    public void TryApply_ChangeFillColorAuthorsNativeFillTargetAndSupportsUndo()
    {
        var editor = MakeSession(out _, out var shapeId);
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.emphasis.change-fill-color", out var plan)
            .Should().BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        var animation = editor.CurrentSlideAnimations.Should().ContainSingle().Subject;
        animation.ShapeId.Should().Be(shapeId);
        animation.Preset.Should().Be(AnimationPreset.ChangeFillColor);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(1);
        animation.RawPresetSubtype.Should().Be("2");
        animation.PreservedFillBehaviorXml.Should().Contain("fillcolor");
        animation.PreservedFillBehaviorXml.Should().Contain($"spid=\"{shapeId}\"");
        animation.PreservedFillBehaviorXml.Should().Contain("accent2");

        editor.Bus.CanUndo.Should().BeTrue();
        editor.Bus.Undo();
        editor.CurrentSlideAnimations.Should().BeEmpty();
        editor.Bus.Redo();
        editor.CurrentSlideAnimations.Should().ContainSingle()
            .Which.Preset.Should().Be(AnimationPreset.ChangeFillColor);
    }

    [Fact]
    public void TryApply_ChangeFontColorAuthorsNativeStyleColorTargetAndSupportsUndo()
    {
        var editor = MakeSession(out _, out var shapeId);
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.emphasis.change-font-color", out var plan)
            .Should().BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        var animation = editor.CurrentSlideAnimations.Should().ContainSingle().Subject;
        animation.ShapeId.Should().Be(shapeId);
        animation.Preset.Should().Be(AnimationPreset.ChangeColor);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(3);
        animation.RawPresetSubtype.Should().Be("0");
        animation.PreservedColorBehaviorXml.Should().Contain("style.color");
        animation.PreservedColorBehaviorXml.Should().Contain($"spid=\"{shapeId}\"");
        animation.PreservedColorBehaviorXml.Should().Contain("accent2");

        editor.Bus.CanUndo.Should().BeTrue();
        editor.Bus.Undo();
        editor.CurrentSlideAnimations.Should().BeEmpty();
        editor.Bus.Redo();
        editor.CurrentSlideAnimations.Should().ContainSingle()
            .Which.RawPresetId.Should().Be(3);
    }

    [Fact]
    public void TryApply_ChangeFontSizeAuthorsNativeStyleFontSizeTargetAndSupportsUndo()
    {
        var editor = MakeSession(out _, out var shapeId);
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.emphasis.change-font-size", out var plan)
            .Should().BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        var animation = editor.CurrentSlideAnimations.Should().ContainSingle().Subject;
        animation.ShapeId.Should().Be(shapeId);
        animation.Preset.Should().Be(AnimationPreset.Grow);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(4);
        animation.RawPresetSubtype.Should().Be("2");
        animation.PreservedNumericBehaviorXml.Should().Contain("style.fontSize");
        animation.PreservedNumericBehaviorXml.Should().Contain("to=\"1.5\"");
        animation.PreservedNumericBehaviorXml.Should().Contain($"spid=\"{shapeId}\"");

        editor.Bus.CanUndo.Should().BeTrue();
        editor.Bus.Undo();
        editor.CurrentSlideAnimations.Should().BeEmpty();
        editor.Bus.Redo();
        editor.CurrentSlideAnimations.Should().ContainSingle()
            .Which.RawPresetId.Should().Be(4);
    }

    [Fact]
    public void TryApply_ChangeLineColorAuthorsNativeStrokeTargetAndSupportsUndo()
    {
        var editor = MakeSession(out _, out var shapeId);
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.emphasis.change-line-color", out var plan)
            .Should().BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        var animation = editor.CurrentSlideAnimations.Should().ContainSingle().Subject;
        animation.ShapeId.Should().Be(shapeId);
        animation.Preset.Should().Be(AnimationPreset.ChangeLineColor);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(7);
        animation.RawPresetSubtype.Should().Be("2");
        animation.PreservedLineBehaviorXml.Should().Contain("stroke.color");
        animation.PreservedLineBehaviorXml.Should().Contain("stroke.on");
        animation.PreservedLineBehaviorXml.Should().Contain($"spid=\"{shapeId}\"");
        animation.PreservedLineBehaviorXml.Should().Contain("accent2");

        editor.Bus.CanUndo.Should().BeTrue();
        editor.Bus.Undo();
        editor.CurrentSlideAnimations.Should().BeEmpty();
        editor.Bus.Redo();
        editor.CurrentSlideAnimations.Should().ContainSingle()
            .Which.Preset.Should().Be(AnimationPreset.ChangeLineColor);
    }

    [Fact]
    public void TryApply_EffectCommand_RejectsMissingSelectionWithoutUndoEntry()
    {
        var editor = MakeSession(out _, out _);
        editor.ClearSelection();
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.entrance.fade", out var plan)
            .Should().BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan).Should().BeFalse();
        editor.CurrentSlideAnimations.Should().BeEmpty();
        editor.Bus.CanUndo.Should().BeFalse();
    }

    [Theory]
    [InlineData("freep.anim.motion.right", MotionPathSegmentKind.Line, 0.5, 0)]
    [InlineData("freep.anim.motion.arc-right", MotionPathSegmentKind.Cubic, 0.5, 0)]
    [InlineData("freep.anim.motion.arc-left", MotionPathSegmentKind.Cubic, -0.5, 0)]
    [InlineData("freep.anim.motion.arc-up", MotionPathSegmentKind.Cubic, 0, -0.5)]
    [InlineData("freep.anim.motion.arc-down", MotionPathSegmentKind.Cubic, 0, 0.5)]
    public void TryApply_MotionCommand_AddsUndoableMotionPath(
        string commandId,
        MotionPathSegmentKind expectedSegmentKind,
        double expectedEndX,
        double expectedEndY)
    {
        var editor = MakeSession(out _, out var shapeId);
        PresentationAnimationCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        var animation = editor.CurrentSlideAnimations.Should().ContainSingle().Subject;
        animation.ShapeId.Should().Be(shapeId);
        animation.Kind.Should().Be(AnimationKind.Motion);
        animation.Motion.Should().NotBeNull();
        animation.Motion!.Segments.Should().HaveCount(2);
        animation.Motion.Segments[0].Kind.Should().Be(MotionPathSegmentKind.Move);
        animation.Motion.Segments[1].Kind.Should().Be(expectedSegmentKind);
        animation.Motion.Segments[1].X.Should().Be(expectedEndX);
        animation.Motion.Segments[1].Y.Should().Be(expectedEndY);
        editor.Bus.CanUndo.Should().BeTrue();

        editor.Bus.Undo();
        editor.CurrentSlideAnimations.Should().BeEmpty();
        editor.Bus.Redo();
        editor.CurrentSlideAnimations.Should().ContainSingle().Which.Kind.Should().Be(AnimationKind.Motion);
    }

    [Theory]
    [InlineData("freep.anim.motion.circle", 5)]
    [InlineData("freep.anim.motion.loop", 3)]
    [InlineData("freep.anim.motion.s", 5)]
    [InlineData("freep.anim.motion.figure-eight", 5)]
    public void TryApply_GalleryMotionCommand_BuildsMultiSegmentPath(
        string commandId,
        int expectedSegmentCount)
    {
        var editor = MakeSession(out _, out _);
        PresentationAnimationCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        var motion = editor.CurrentSlideAnimations.Should().ContainSingle().Subject.Motion;
        motion.Should().NotBeNull();
        motion!.Segments.Should().HaveCount(expectedSegmentCount);
        motion.Segments[0].Kind.Should().Be(MotionPathSegmentKind.Move);
        motion.Segments.Skip(1).Should().OnlyContain(segment => segment.Kind == MotionPathSegmentKind.Cubic);
        motion.Segments[^1].X.Should().Be(0);
        motion.Segments[^1].Y.Should().Be(0);
    }

    [Fact]
    public void TryApply_ReverseMotionPath_ReversesSelectedPathAndSupportsUndo()
    {
        var editor = MakeSession(out _, out var shapeId);
        var motion = new MotionPath { Origin = "parent", PtsTypes = "F" };
        motion.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        motion.Segments.Add(MotionPathSegment.LineTo(0.5, 0.25));
        motion.Segments.Add(MotionPathSegment.CubicTo(0.6, 0.3, 0.8, 0.4, 1, 0.5));
        editor.AddAnimation(0, new ShapeAnimation
        {
            ShapeId = shapeId,
            Kind = AnimationKind.Motion,
            Motion = motion,
            Trigger = AnimationTrigger.OnClick,
        });
        PresentationAnimationCommandPlanner.TryPlan("freep.anim.motion.reverse", out var plan)
            .Should().BeTrue();

        PresentationAnimationCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        var reversed = editor.CurrentSlideAnimations.Should().ContainSingle().Subject.Motion;
        reversed.Should().NotBeNull();
        reversed!.Origin.Should().Be("parent");
        reversed.PtsTypes.Should().Be("F");
        reversed.Segments.Select(segment => segment.Kind)
            .Should().Equal(MotionPathSegmentKind.Move, MotionPathSegmentKind.Cubic, MotionPathSegmentKind.Line);
        reversed.Segments[0].X.Should().Be(1);
        reversed.Segments[0].Y.Should().Be(0.5);
        reversed.Segments[1].X.Should().Be(0.5);
        reversed.Segments[1].Y.Should().Be(0.25);
        reversed.Segments[2].X.Should().Be(0);
        reversed.Segments[2].Y.Should().Be(0);
        editor.Bus.CanUndo.Should().BeTrue();

        editor.Bus.Undo();
        editor.CurrentSlideAnimations.Single().Motion!.Segments[0].X.Should().Be(0);
        editor.Bus.Redo();
        editor.CurrentSlideAnimations.Single().Motion!.Segments[0].X.Should().Be(1);
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
