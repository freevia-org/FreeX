using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlideShowHostPolicySourceTests
{
    [Fact]
    public void AvaloniaSlideShowWindow_ConsumesBrowseScrollbarAndKioskRestartState()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));

        source.Should().Contain("HorizontalScrollBarVisibility = _presentation.ShowBrowseScrollbar");
        source.Should().Contain("VerticalScrollBarVisibility = _presentation.ShowBrowseScrollbar");
        source.Should().Contain("SlideShowKioskRestartPlanner.TryGetInterval(");
        source.Should().Contain("StartKioskRestartTimer");
        source.Should().Contain("RestartKioskShow");
    }

    [Fact]
    public void Avalonia_named_custom_show_preserves_wpf_owner_handoff()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));

        var launchStart = source.IndexOf("internal bool TryStartCustomSlideShow", StringComparison.Ordinal);
        launchStart.Should().BeGreaterThanOrEqualTo(0);
        var launchSource = source[launchStart..];
        launchSource.Should().Contain("if (IsVisible)");
        launchSource.Should().Contain("slideShow.Show(this);");
        launchSource.Should().Contain("slideShow.Show();");
    }

    [Fact]
    public void AvaloniaSlideShowWindow_DelegatesHostPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));

        source.Should().Contain("SlideShowHostPlanner.PlanKey(");
        source.Should().Contain("SlideShowSlideNumberPlanner.TryGetDigit(");
        source.Should().Contain("SlideShowHostPlanner.PlanSlideNumberJump(");
        source.Should().Contain("ExecuteSlideNumberJump");
        source.Should().Contain("SlideShowScreenModePlanner.TryPlanKey(");
        source.Should().Contain("SetScreenMode(screenMode);");
        source.Should().Contain("SlideShowHostPlanner.PlanAdvance(");
        source.Should().Contain("SlideShowHostPlanner.PlanBack(");
        source.Should().Contain("command.UseDestinationBackground");
        source.Should().Contain("DisplayCurrentSlide(animated, zoomTransitionDurationMs, zoomShowBackground);");
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
        source.Should().Contain("AnimateRandomBarsClip(");
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
        source.Should().Contain("SlideShowTransitionPlaybackCoordinator.Play(_presentation, slide, t, this);");
        source.Should().Contain("ISlideShowTransitionPlaybackRenderer");
        source.Should().Contain("PlaySplitTransition(");
        source.Should().Contain("PlayBlindsTransition(");
        source.Should().Contain("SlideShowPlaybackPlanner.BlindsBandCount");
        source.Should().Contain("PlayRandomBarsTransition(");
        source.Should().Contain("PlayStripsTransition(");
        source.Should().Contain("PlayWheelTransition(");
        source.Should().Contain("PlayZoomTransition(");
        source.Should().Contain("PlayPanTransition(");
        source.Should().Contain("PlayGalleryTransition(");
        source.Should().Contain("AnimateGalleryTransition(");
        source.Should().Contain("PlayConveyorTransition(");
        source.Should().Contain("AnimateConveyorTransition(");
        source.Should().Contain("SlideShowPlaybackPlanner.ConveyorStartScale");
        source.Should().Contain("SlideShowPlaybackPlanner.ConveyorTiltDegrees");
        source.Should().Contain("PlayWindowTransition(");
        source.Should().Contain("PlayMorphTransition(");
        source.Should().Contain("SlideShowMorphPlanner.Plan(");
        source.Should().Contain("SlideShowMorphPlanner.CreateTokenShape(");
        source.Should().Contain("MorphTokenScreenRect(");
        source.Should().Contain("PlayFlipTransition(");
        source.Should().Contain("PlayCubeTransition(");
        source.Should().Contain("PlayRotateTransition(");
        source.Should().Contain("SlideShowPerspectiveTransitionPlanner.Plan(");
        source.Should().Contain("BuildPerspectiveMatrix(");
        source.Should().Contain("PlayHoneycombTransition(");
        source.Should().Contain("SlideShowHoneycombTransitionPlanner.Plan(");
        source.Should().Contain("BuildHoneycombTransitionGeometry(");
        source.Should().Contain("PlayGlitterTransition(");
        source.Should().Contain("SlideShowGlitterTransitionPlanner.Plan(");
        source.Should().Contain("BuildGlitterTransitionGeometry(");
        source.Should().Contain("PlayRippleTransition(");
        source.Should().Contain("SlideShowRippleTransitionPlanner.Plan(");
        source.Should().Contain("BuildRippleTransitionGeometry(");
        source.Should().Contain("PlayWindTransition(");
        source.Should().Contain("SlideShowWindTransitionPlanner.Plan(");
        source.Should().Contain("BuildWindTransitionGeometry(");
        source.Should().Contain("PlayCurtainsTransition(");
        source.Should().Contain("SlideShowCurtainsTransitionPlanner.Plan(");
        source.Should().Contain("BuildCurtainsTransitionGeometry(");
        source.Should().Contain("PlayShredTransition(");
        source.Should().Contain("SlideShowShredTransitionPlanner.Plan(");
        source.Should().Contain("BuildShredTransitionGeometry(");
        source.Should().Contain("PlayDrapeTransition(");
        source.Should().Contain("SlideShowDrapeTransitionPlanner.Plan(");
        source.Should().Contain("BuildDrapeTransitionGeometry(");
        source.Should().Contain("PlayVortexTransition(");
        source.Should().Contain("SlideShowVortexTransitionPlanner.Plan(");
        source.Should().Contain("BuildVortexTransitionGeometry(");
        source.Should().Contain("PlayWarpTransition(");
        source.Should().Contain("SlideShowWarpTransitionPlanner.Plan(");
        source.Should().Contain("BuildWarpTransitionGeometry(");
        source.Should().Contain("PlayFractureTransition(");
        source.Should().Contain("SlideShowFractureTransitionPlanner.Plan(");
        source.Should().Contain("BuildFractureTransitionGeometry(");
        source.Should().Contain("PlayCrushTransition(");
        source.Should().Contain("SlideShowCrushTransitionPlanner.Plan(");
        source.Should().Contain("BuildCrushTransitionGeometry(");
        source.Should().Contain("PlayPrismTransition(");
        source.Should().Contain("SlideShowPrismTransitionPlanner.Plan(");
        source.Should().Contain("BuildPrismTransitionGeometry(");
        source.Should().Contain("PlayPrestigeTransition(");
        source.Should().Contain("SlideShowPrestigeTransitionPlanner.Plan(");
        source.Should().Contain("BuildPrestigeTransitionGeometry(");
        source.Should().Contain("PlaySwitchTransition(");
        source.Should().Contain("PlayOrbitTransition(");
        source.Should().Contain("PlayFerrisTransition(");
        source.Should().Contain("PlayFlythroughTransition(");
        source.Should().Contain("PlayPageCurlTransition(");
        source.Should().Contain("SlideShowPageCurlTransitionPlanner.Plan(");
        source.Should().Contain("BuildPageCurlGeometry(");
        source.Should().Contain("AnimateWindowTransition(");
        source.Should().Contain("BuildWindowTransitionGeometry(");
        source.Should().Contain("SlideShowPlaybackPlanner.WindowInitialOpenFactor");
        source.Should().Contain("SlideShowPlaybackPlanner.GalleryStartScale");
        source.Should().Contain("SlideShowPlaybackPlanner.GalleryOutgoingEndScale");
        source.Should().Contain("ISlideShowTransitionPlaybackRenderer.PlayMorph");
        source.Should().Contain("PlayDissolveTransition(");
        source.Should().Contain("PlayFlashTransition(");
        source.Should().Contain("PlayBoxTransition(");
        source.Should().Contain("PlayRevealTransition(");
        source.Should().Contain("PlayUncoverTransition(");
        source.Should().Contain("PlayCoverTransition(");
        source.Should().Contain("AnimateTranslate(_transitionBackImage");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildSplitRects(");
        source.Should().Contain("plan.SplitHorizontal");
        source.Should().Contain("plan.SplitFromCenter");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildBlindsTransitionRects(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildRandomBarsTransitionRects(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildStrips(");
        source.Should().Contain("BuildStripsTransitionGeometry(");
        source.Should().Contain("plan.StripsSlopeDown");
        source.Should().Contain("BuildWheelTransitionGeometry(");
        source.Should().Contain("plan.WheelSpokeCount");
        source.Should().Contain("plan.WheelReverse");
        source.Should().Contain("plan.ZoomIn");
        source.Should().Contain("SlideShowPlaybackPlanner.ZoomInStartScale");
        source.Should().Contain("SlideShowPlaybackPlanner.ZoomOutStartScale");
        source.Should().Contain("BuildDissolveTransitionGeometry(");
        source.Should().Contain("BuildBoxTransitionGeometry(");
        source.Should().Contain("BuildRevealTransitionGeometry(");
        source.Should().Contain("BuildUncoverTransitionGeometry(");
        source.Should().Contain("SlideShowPlaybackPlanner.DissolveRowCount");
        source.Should().Contain("SlideShowPlaybackPlanner.DissolveColumnCount");
        source.Should().Contain("SlideShowPlaybackPlanner.PlanAnimationStep(");
        source.Should().Contain("SlideShowPlaybackPlanner.PlanFallbackAnimation(");
        source.Should().Contain("SlideShowPlaybackPlanner.PlanFallbackVisibility(animation);");
        source.Should().Contain("_slideCanvas.SuppressedShapeIds.Add(animation.ShapeId);");
        source.Should().Contain("RevealShape(animation.ShapeId);");
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
        source.Should().NotContain("SlideShowPlaybackPlanner.PlanTransition(t)");
        source.Should().NotContain("switch (plan.ActionKind)");
        source.Should().NotContain("TransitionKind.Random");
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
    public void AvaloniaSlideShowWindow_cancels_timers_before_preparing_next_slide_overlay()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));

        var displayStart = source.IndexOf("private void DisplayCurrentSlide(", StringComparison.Ordinal);
        displayStart.Should().BeGreaterThanOrEqualTo(0);
        var displaySource = source[displayStart..];
        var cancelIndex = displaySource.IndexOf("CancelActiveTimers();", StringComparison.Ordinal);
        var overlayIndex = displaySource.IndexOf("PrepareAnimationOverlay(slide);", StringComparison.Ordinal);

        cancelIndex.Should().BeGreaterThanOrEqualTo(0);
        overlayIndex.Should().BeGreaterThanOrEqualTo(0);
        cancelIndex.Should().BeLessThan(overlayIndex,
            "navigation must stop stale animation timers before preparing the new slide overlay");
    }

    [Fact]
    public void AvaloniaSlideShowWindow_ExecutesAnimationStepsThroughSharedPlaybackPlans()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));

        source.Should().Contain("case SlideShowHostCommandKind.PlayAnimationStep when command.Step is not null:");
        source.Should().Contain("PlayAnimationStep(command.Step);");
        source.Should().Contain("private void PlayAnimationStep(AnimationStep step)");
        source.Should().Contain("_lastAnimationStepFrameEvidence = SlideShowPlaybackFramePlanner.PlanAnimationStepCheckpoints(step, _slideDipW, _slideDipH);");
        source.Should().Contain("_lastAnimationStepPlaybackReadinessPlan =");
        source.Should().Contain("SlideShowPlaybackFramePlanner.BuildAnimationStepPlaybackReadinessPlan(");
        source.Should().Contain("foreach (var plan in SlideShowPlaybackPlanner.PlanAnimationStep(step, _presentation, effectiveColorMap))");
        source.Should().Contain("plan.DelayMs + index * plan.DurationMs");
        source.Should().Contain("effectiveColorMap);");
        source.Should().Contain("_lastAnimationFramePlan = SlideShowPlaybackFramePlanner.PlanFrame(plan, 0, _slideDipW, _slideDipH);");
        source.Should().Contain("PlayShapeAnimationWithTiming(element, plan, onReveal: anim.Kind == AnimationKind.Exit ? null : () =>");
        source.Should().Contain("private void PlayShapeAnimationWithTiming(");
        source.Should().Contain("PlayShapeAnimationPass(element, basePlan, onReveal, passCount, 0);");
        source.Should().Contain("BuildReverseAnimationPlan(currentPlan)");
        source.Should().Contain("PlayShapeAnimationPass(element, basePlan, onReveal, passCount, passIndex + 1)");
        source.Should().Contain("MotionKeyFrames = SlideShowPlaybackPlanner.ReverseMotionPathKeyFrames(plan.MotionKeyFrames)");
        source.Should().Contain("PlayFallbackAnimation(anim, plan.DelayMs, plan.DurationMs);");
        source.Should().Contain("PlayFallbackAnimation(SlideShowPlaybackPlanner.PlanFallbackAnimation(animation, delayMs));");

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
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Dissolve:");
        source.Should().Contain("DissolveEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Flash:");
        source.Should().Contain("FlashEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Spiral:");
        source.Should().Contain("SpiralEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Swivel:");
        source.Should().Contain("SwivelEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Bounce:");
        source.Should().Contain("BounceEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Float:");
        source.Should().Contain("FloatEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Swoop:");
        source.Should().Contain("SwoopEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Boomerang:");
        source.Should().Contain("BoomerangEffect(element, plan, onReveal);");
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
        source.Should().Contain("var fromX = isExit ? 0 : dx;");
        source.Should().Contain("var toX = isExit ? dx : 0;");
        source.Should().Contain("el.Clip = new RectangleGeometry(new Rect(0, 0, w, h));");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Crawl:");
        source.Should().Contain("CrawlEffect(element, plan, onReveal);");
        source.Should().Contain("private void CrawlEffect(Control el, SlideShowShapeAnimationPlaybackPlan plan, Action? onReveal = null)");
        source.Should().Contain("PeekEffect(el, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Zoom:");
        source.Should().Contain("ZoomEffect(element, plan, onReveal);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Pulse:");
        source.Should().Contain("PulseEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.GrowShrink:");
        source.Should().Contain("GrowShrinkEffect(element, plan);");
        source.Should().Contain("plan.FromScaleX");
        source.Should().Contain("plan.FromScaleY");
        source.Should().Contain("plan.PeakScaleX");
        source.Should().Contain("plan.PeakScaleY");
        source.Should().Contain("plan.ToScaleX");
        source.Should().Contain("plan.ToScaleY");
        source.Should().Contain("AnimateScaleAxes(scale, plan.FromScaleX, plan.FromScaleY, plan.PeakScaleX, plan.PeakScaleY");
        source.Should().Contain("AnimateScaleAxes(scale, plan.PeakScaleX, plan.PeakScaleY, plan.ToScaleX, plan.ToScaleY");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Spin:");
        source.Should().Contain("SpinEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Teeter:");
        source.Should().Contain("TeeterEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Blink:");
        source.Should().Contain("BlinkEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.FlashBulb:");
        source.Should().Contain("FlashBulbEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Flicker:");
        source.Should().Contain("FlickerEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Wave:");
        source.Should().Contain("WaveEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ColorPulse:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ChangeColor:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ColorWave:");
        source.Should().Contain("ColorWaveEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ChangeLineColor:");
        source.Should().Contain("LineColorEffect(element, plan);");
        source.Should().Contain("_animFontStyleElements");
        source.Should().Contain("ResolveFontStyleBehavior(fontStyleAnimation)");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ChangeFontStyle:");
        source.Should().Contain("FontStyleEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.GrowWithColor:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Shimmer:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Bold:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Underline:");
        source.Should().Contain("EmphasisPulseEffect(element, plan);");
        source.Should().Contain("AddAuthoredColorOverlay(el, plan);");
        source.Should().Contain("ColorFromHex");
        source.Should().Contain("AnimationKind.Emphasis");
        source.Should().Contain("a.Kind == AnimationKind.Exit");
        source.Should().Contain("|| a.Kind == AnimationKind.Exit");
        source.Should().Contain("|| (a.Kind == AnimationKind.Entrance || a.Kind == AnimationKind.Motion)");
        source.Should().Contain("onReveal: anim.Kind == AnimationKind.Exit ? null : () =>");
        source.Should().Contain("DisappearEffect(element, plan.DelayMs);");
        source.Should().Contain("var isExit = plan.Animation.Kind == AnimationKind.Exit;");
        source.Should().Contain("var randomBars = SlideShowMaskGeometryPlanner.BuildRandomBars(");
        source.Should().Contain("var closed = ToRect(randomBar.Geometry.Closed);");
        source.Should().Contain("var from = isExit ? open : closed;");
        source.Should().Contain("el.Opacity = isExit ? plan.FromOpacity : 0;");
        source.Should().Contain("AnimateTranslate(el, isExit ? 0 : dx, isExit ? 0 : dy,");
        source.Should().Contain("var fromX = isExit ? 0 : dx;");
        source.Should().Contain("var toX = isExit ? dx : 0;");
        source.Should().Contain("_entranceShapeIds.Contains(shapeId) ? 0 : 1");
        source.Should().Contain("MotionPathEffect(element, plan, onReveal);");
        source.Should().Contain("AnimateOpacity(_slideCanvas, plan.FromOpacity, plan.FlashOpacity, plan.DurationMs / 2");
    }

}
