using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SlideShowHostPolicySourceTests
{
    [Fact]
    public void WpfSlideShowWindow_DelegatesHostPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.App.Host",
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
        source.Should().Contain("SlideShowInkExecutionPlanner.CreateState(");
        source.Should().Contain("SlideShowInkExecutionPlanner.SelectPointerInk(");
        source.Should().Contain("SlideShowInkExecutionPlanner.Begin(");
        source.Should().Contain("SlideShowInkExecutionPlanner.Append(");
        source.Should().Contain("SlideShowInkExecutionPlanner.End(");
        source.Should().Contain("SlideShowInkExecutionPlanner.ClearCurrentSlide(");
        source.Should().Contain("SlideShowInkPersistencePlanner.ApplyRetentionOnExit(");
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
    public void WpfSlideShowWindow_ExecutesAnimationStepsThroughSharedPlaybackPlans()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.App.Host",
            "SlideShowWindow.cs"));

        source.Should().Contain("case SlideShowHostCommandKind.PlayAnimationStep when command.Step is not null:");
        source.Should().Contain("PlayAnimationStep(command.Step);");
        source.Should().Contain("private void PlayAnimationStep(AnimationStep step)");
        source.Should().Contain("foreach (var plan in SlideShowPlaybackPlanner.PlanAnimationStep(step))");
        source.Should().Contain("PlayShapeAnimation(element, plan);");
        source.Should().Contain("PlayFallbackAnimation(SlideShowPlaybackPlanner.PlanFallbackAnimation(anim, plan.DelayMs));");

        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Appear:");
        source.Should().Contain("AppearEffect(sb, element, plan.DelayMs);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Fade:");
        source.Should().Contain("FadeEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.FlyIn:");
        source.Should().Contain("FlyInEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Wipe:");
        source.Should().Contain("WipeEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Zoom:");
        source.Should().Contain("ZoomEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Pulse:");
        source.Should().Contain("PulseEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Spin:");
        source.Should().Contain("SpinEffect(sb, element, plan);");
        source.Should().Contain("MotionPathEffect(sb, element, plan);");
        source.Should().Contain("Storyboard.SetTarget(flashAnim, _slideCanvas);");
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
