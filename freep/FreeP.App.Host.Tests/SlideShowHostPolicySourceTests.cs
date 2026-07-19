using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SlideShowHostPolicySourceTests
{
    [Fact]
    public void WpfSlideShowWindow_DelegatesHostPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
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
        source.Should().Contain("SlideShowPresenterSessionSummaryPlanner.BuildSummary(");
        source.Should().Contain("SlideShowInkExecutionPlanner.BuildOverlayRenderPlan(");
        source.Should().Contain("SlideShowSessionController");
        source.Should().Contain("_session.ApplyPresenterToolIntent(");
        source.Should().Contain("_session.MoveToSlide(");
        source.Should().Contain("_session.Close(");
        source.Should().Contain("_session.BeginInkStroke(");
        source.Should().Contain("_session.AppendInkStroke(");
        source.Should().Contain("_session.EndInkStroke(");
        source.Should().Contain("_session.ClearInkStrokes(");
        source.Should().Contain("_session.UndoLastInkStroke(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildBlindsBand(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildRandomBars(");
        source.Should().Contain("SlideShowPlaybackPlanner.RandomBarsBandCount");
        source.Should().Contain("var bars = new GeometryGroup();");
        source.Should().Contain("RectangleGeometry.RectProperty");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildCheckerboardCell(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildCircle(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildDiamondPoint(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildPlusRects(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildStrips(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildWedge(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildWheel(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.IsSecondCheckerboardPhase(");
        source.Should().Contain("SlideShowHostPlanner.MapCanvasPointToSlide(");
        source.Should().Contain("SlideShowHostPlanner.HitTestHyperlink(");
        source.Should().Contain("SlideShowHostPlanner.HitTestTriggerShape(");
        source.Should().Contain("SlideShowHostPlanner.PlanPointerClick(");
        source.Should().Contain("SlideShowPlaybackPlanner.PlanTransition(");
        source.Should().Contain("PlaySplitTransition(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildSplitRects(");
        source.Should().Contain("SlideShowPlaybackPlanner.PlanAnimationStep(");
        source.Should().Contain("SlideShowPlaybackPlanner.PlanFallbackAnimation(");
        source.Should().Contain("SlideShowPlaybackFramePlanner.PlanFrame(");
        source.Should().Contain("SlideShowPlaybackFramePlanner.PlanAnimationStepCheckpoints(");
        source.Should().Contain("SlideShowPlaybackFramePlanner.BuildAnimationStepPlaybackReadinessPlan(");
        source.Should().Contain("LastAnimationFramePlanForTest");
        source.Should().Contain("LastAnimationStepFrameEvidenceForTest");
        source.Should().Contain("LastAnimationStepPlaybackReadinessPlanForTest");

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
        source.Should().NotContain("private static (Rect Closed, Rect Open) BuildBlindsBand");
        source.Should().NotContain("private static (Rect Closed, Rect Open) BuildCheckerboardCell");
        source.Should().NotContain("private static Point BuildDiamondPoint");
        source.Should().NotContain("private static (Rect Vertical, Rect Horizontal) BuildPlusRects");
        source.Should().NotContain("private static Point PointOnWedgeRadius");
        source.Should().NotContain("private SlideShowTimingRecorderState _timingRecorderState");
        source.Should().NotContain("private SlideShowRecordingExecutionState _recordingExecutionState");
        source.Should().NotContain("private SlideShowInkExecutionState _inkExecutionState");
    }

    [Fact]
    public void WpfSlideShowWindow_ExecutesAnimationStepsThroughSharedPlaybackPlans()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "SlideShowWindow.cs"));

        source.Should().Contain("case SlideShowHostCommandKind.PlayAnimationStep when command.Step is not null:");
        source.Should().Contain("PlayAnimationStep(command.Step);");
        source.Should().Contain("private void PlayAnimationStep(AnimationStep step)");
        source.Should().Contain("_lastAnimationStepFrameEvidence = SlideShowPlaybackFramePlanner.PlanAnimationStepCheckpoints(step, _slideDipW, _slideDipH);");
        source.Should().Contain("_lastAnimationStepPlaybackReadinessPlan =");
        source.Should().Contain("SlideShowPlaybackFramePlanner.BuildAnimationStepPlaybackReadinessPlan(");
        source.Should().Contain("foreach (var plan in SlideShowPlaybackPlanner.PlanAnimationStep(step))");
        source.Should().Contain("_lastAnimationFramePlan = SlideShowPlaybackFramePlanner.PlanFrame(plan, 0, _slideDipW, _slideDipH);");
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
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Split:");
        source.Should().Contain("SplitEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.RandomBars:");
        source.Should().Contain("RandomBarsEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Blinds:");
        source.Should().Contain("BlindsEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Box:");
        source.Should().Contain("BoxEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Checkerboard:");
        source.Should().Contain("CheckerboardEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Circle:");
        source.Should().Contain("GeometricMaskEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Diamond:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Plus:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Strips:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Wedge:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Wheel:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Dissolve:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Flash:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Spiral:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Swivel:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Bounce:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Float:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Swoop:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Boomerang:");
        source.Should().Contain("case SlideShowGeometricMaskKind.Circle:");
        source.Should().Contain("CircleEffect(sb, el, plan);");
        source.Should().Contain("case SlideShowGeometricMaskKind.Diamond:");
        source.Should().Contain("DiamondEffect(sb, el, plan);");
        source.Should().Contain("case SlideShowGeometricMaskKind.Plus:");
        source.Should().Contain("PlusEffect(sb, el, plan);");
        source.Should().Contain("case SlideShowGeometricMaskKind.Strips:");
        source.Should().Contain("StripsEffect(sb, el, plan);");
        source.Should().Contain("case SlideShowGeometricMaskKind.Wedge:");
        source.Should().Contain("WedgeEffect(sb, el, plan);");
        source.Should().Contain("case SlideShowGeometricMaskKind.Wheel:");
        source.Should().Contain("WheelEffect(sb, el, plan);");
        source.Should().Contain("BuildWedgeGeometry(w, h, fromProgress)");
        source.Should().Contain("BuildWheelGeometry(w, h, fromProgress, plan.GeometricMaskSpokeCount)");
        source.Should().Contain("BuildStripsGeometry(");
        source.Should().Contain("plan.GeometricMaskStripsSlopeDown");
        source.Should().Contain("new GeometryGroup { FillRule = FillRule.Nonzero }");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Peek:");
        source.Should().Contain("PeekEffect(sb, element, plan);");
        source.Should().Contain("private void PeekEffect(Storyboard sb, FrameworkElement el,");
        source.Should().Contain("el.RenderTransform = translate;");
        source.Should().Contain("el.Clip = new RectangleGeometry(new Rect(0, 0, w, h));");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Crawl:");
        source.Should().Contain("CrawlEffect(sb, element, plan);");
        source.Should().Contain("private void CrawlEffect(Storyboard sb, FrameworkElement el,");
        source.Should().Contain("PeekEffect(sb, el, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Zoom:");
        source.Should().Contain("ZoomEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Pulse:");
        source.Should().Contain("PulseEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.GrowShrink:");
        source.Should().Contain("GrowShrinkEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Spin:");
        source.Should().Contain("SpinEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Teeter:");
        source.Should().Contain("TeeterEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Blink:");
        source.Should().Contain("BlinkEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Wave:");
        source.Should().Contain("WaveEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ColorPulse:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ChangeColor:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.GrowWithColor:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Shimmer:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Bold:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Underline:");
        source.Should().Contain("EmphasisPulseEffect(sb, element, plan);");
        source.Should().Contain("AnimationKind.Emphasis");
        source.Should().Contain("a.Kind == AnimationKind.Exit");
        source.Should().Contain("|| a.Kind == AnimationKind.Exit");
        source.Should().Contain("|| (a.Kind == AnimationKind.Entrance || a.Kind == AnimationKind.Motion)");
        source.Should().Contain("_slideCanvas.SuppressedShapeIds.Add(anim.ShapeId);");
        source.Should().Contain("_slideCanvas.SuppressedShapeIds.Add(shapeId);");
        source.Should().Contain("AttachEntranceCompletion(sb, plan);");
        source.Should().Contain("RevealShape(plan.Animation.ShapeId)");
        source.Should().Contain("DisappearEffect(sb, element, plan.DelayMs);");
        source.Should().Contain("var isExit = plan.Animation.Kind == AnimationKind.Exit;");
        source.Should().Contain("var randomBars = SlideShowMaskGeometryPlanner.BuildRandomBars(");
        source.Should().Contain("var closed = ToRect(randomBar.Geometry.Closed);");
        source.Should().Contain("var from = isExit ? open : closed;");
        source.Should().Contain("opacityAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(plan.FromOpacity, KeyTime.FromPercent(0)))");
        source.Should().Contain("isExit ? 0 : dx, isExit ? dx : 0");
        source.Should().Contain("var fromX = isExit ? 0 : dx;");
        source.Should().Contain("var toX = isExit ? dx : 0;");
        source.Should().Contain("_entranceShapeIds.Contains(shapeId) ? 0 : 1");
        source.Should().Contain("MotionPathEffect(sb, element, plan);");
        source.Should().Contain("Storyboard.SetTarget(flashAnim, _slideCanvas);");
    }

}
