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
        source.Should().Contain("SlideShowInkExecutionPlanner.CreateState(");
        source.Should().Contain("SlideShowInkExecutionPlanner.SelectPointerInk(");
        source.Should().Contain("SlideShowInkExecutionPlanner.Begin(");
        source.Should().Contain("SlideShowInkExecutionPlanner.Append(");
        source.Should().Contain("SlideShowInkExecutionPlanner.End(");
        source.Should().Contain("SlideShowInkExecutionPlanner.ClearCurrentSlide(");
        source.Should().Contain("SlideShowInkExecutionPlanner.ApplyRetentionOnExit(");
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
