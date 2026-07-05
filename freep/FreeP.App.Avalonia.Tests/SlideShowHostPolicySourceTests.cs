using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlideShowHostPolicySourceTests
{
    [Fact]
    public void AvaloniaSlideShowWindow_DelegatesHostPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));

        source.Should().Contain("SlideShowHostPlanner.PlanKey(");
        source.Should().Contain("SlideShowHostPlanner.PlanAdvance(");
        source.Should().Contain("SlideShowHostPlanner.PlanBack(");
        source.Should().Contain("SlideShowHostPlanner.PlanTrigger(");
        source.Should().Contain("SlideShowHostPlanner.PlanInternalSlideJump(");
        source.Should().Contain("SlideShowHostPlanner.BuildDisplayPlan(");
        source.Should().Contain("SlideShowHostPlanner.BuildPresenterState(");
        source.Should().Contain("SlideShowPresenterToolPlanner.BuildPlan(");
        source.Should().Contain("SlideShowPresenterSessionSummaryPlanner.BuildSummary(");
        source.Should().Contain("SlideShowRecordingReviewPlanner.ApplyPersistableMediaArtifacts(");
        source.Should().Contain("SlideShowInkExecutionPlanner.CreateState(");
        source.Should().Contain("SlideShowInkExecutionPlanner.SelectPointerInk(");
        source.Should().Contain("SlideShowInkExecutionPlanner.Begin(");
        source.Should().Contain("SlideShowInkExecutionPlanner.Append(");
        source.Should().Contain("SlideShowInkExecutionPlanner.End(");
        source.Should().Contain("SlideShowInkExecutionPlanner.ClearCurrentSlide(");
        source.Should().Contain("SlideShowInkExecutionPlanner.UndoLastStroke(");
        source.Should().Contain("SlideShowInkPersistencePlanner.ApplyRetentionOnExit(");
        source.Should().Contain("_playbackRoute).State");
        source.Should().Contain("SlideShowInkExecutionPlanner.BuildOverlayRenderPlan(");
        source.Should().Contain("SlideShowHostPlanner.MapCanvasPointToSlide(");
        source.Should().Contain("SlideShowHostPlanner.HitTestHyperlink(");
        source.Should().Contain("SlideShowHostPlanner.HitTestTriggerShape(");
        source.Should().Contain("SlideShowPlaybackPlanner.PlanTransition(");
        source.Should().Contain("SlideShowPlaybackPlanner.PlanAnimationStep(");
        source.Should().Contain("SlideShowPlaybackPlanner.PlanFallbackAnimation(");

        source.Should().NotContain("case Key.Right");
        source.Should().NotContain("case Key.Left");
        source.Should().NotContain("case TransitionKind.");
        source.Should().NotContain("SlideShowTransitionPlanner.Plan(");
        source.Should().NotContain("_controller.GoToSlide(0)");
        source.Should().NotContain("_presentation.Slides.Count - 1");
        source.Should().NotContain("HitTestHyperlinkInShapes(");
        source.Should().NotContain("double sx  = shape.OffsetXEmu / 9525.0");
        source.Should().NotContain("var result = _controller.Advance();");
        source.Should().NotContain("var result = _controller.Back();");
        source.Should().NotContain("_controller.AdvanceTrigger(");
        source.Should().NotContain("new SlideShowPresenterToolPlan(");
        source.Should().NotContain("BuildOverlayPlan(_inkExecutionState)");
        source.Should().NotContain("stroke.InkState.ThicknessDip * scale");
    }

    [Fact]
    public void AvaloniaSlideShowWindow_ExecutesAnimationStepsThroughSharedPlaybackPlans()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));

        source.Should().Contain("case SlideShowHostCommandKind.PlayAnimationStep when command.Step is not null:");
        source.Should().Contain("PlayAnimationStep(command.Step);");
        source.Should().Contain("private void PlayAnimationStep(AnimationStep step)");
        source.Should().Contain("foreach (var plan in SlideShowPlaybackPlanner.PlanAnimationStep(step))");
        source.Should().Contain("PlayShapeAnimation(element, plan, onReveal: () =>");
        source.Should().Contain("PlayFallbackAnimation(SlideShowPlaybackPlanner.PlanFallbackAnimation(anim, plan.DelayMs));");

        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Appear:");
        source.Should().Contain("AppearEffect(element, plan.DelayMs, CompleteReveal(plan, onReveal));");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Fade:");
        source.Should().Contain("FadeEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.FlyIn:");
        source.Should().Contain("FlyInEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Wipe:");
        source.Should().Contain("WipeEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Split:");
        source.Should().Contain("SplitEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.RandomBars:");
        source.Should().Contain("RandomBarsEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Blinds:");
        source.Should().Contain("BlindsEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Box:");
        source.Should().Contain("BoxEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Checkerboard:");
        source.Should().Contain("CheckerboardEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Circle:");
        source.Should().Contain("GeometricMaskEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Diamond:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Plus:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Strips:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Wedge:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Wheel:");
        source.Should().Contain("case SlideShowGeometricMaskKind.Circle:");
        source.Should().Contain("case SlideShowGeometricMaskKind.Diamond:");
        source.Should().Contain("case SlideShowGeometricMaskKind.Plus:");
        source.Should().Contain("case SlideShowGeometricMaskKind.Strips:");
        source.Should().Contain("case SlideShowGeometricMaskKind.Wedge:");
        source.Should().Contain("case SlideShowGeometricMaskKind.Wheel:");
        source.Should().Contain("GeometricMaskClipEffect(el, plan, onReveal);");
        source.Should().Contain("SlideShowGeometricMaskKind.Plus => BuildPlusGeometry(width, height, progress),");
        source.Should().Contain("SlideShowGeometricMaskKind.Strips => BuildStripsGeometry(width, height, progress, stripCount, stripsSlopeDown),");
        source.Should().Contain("SlideShowGeometricMaskKind.Wedge => BuildWedgeGeometry(width, height, progress),");
        source.Should().Contain("SlideShowGeometricMaskKind.Wheel => BuildWheelGeometry(width, height, progress, spokeCount),");
        source.Should().Contain("plan.GeometricMaskStripsSlopeDown");
        source.Should().Contain("new GeometryGroup { FillRule = FillRule.NonZero }");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Peek:");
        source.Should().Contain("PeekEffect(element, plan, onReveal);");
        source.Should().Contain("private void PeekEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)");
        source.Should().Contain("el.RenderTransform = new TranslateTransform(dx, dy);");
        source.Should().Contain("el.Clip = new RectangleGeometry(new Rect(0, 0, w, h));");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Zoom:");
        source.Should().Contain("ZoomEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Pulse:");
        source.Should().Contain("PulseEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.GrowShrink:");
        source.Should().Contain("GrowShrinkEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Spin:");
        source.Should().Contain("SpinEffect(element, plan);");
        source.Should().Contain("MotionPathEffect(element, plan, onReveal);");
        source.Should().Contain("AnimateOpacity(_slideCanvas, plan.FromOpacity, plan.FlashOpacity, plan.DurationMs / 2");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
