using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlideShowHostPolicySourceTests
{
    [Fact]
    public void AvaloniaMediaPlaybackPassesWebVttRegionsToSharedCaptionPlacement()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Avalonia",
            "AvaloniaSlideShowMediaController.cs"));

        source.Should().Contain("slot.CaptionTrack?.Regions");
        source.Should().Contain("ComputeCaptionPlacement(");
    }

    [Fact]
    public void AvaloniaSlideShowWindow_ConsumesBrowseScrollbarAndKioskRestartState()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));

        source.Should().Contain("HorizontalScrollBarVisibility = windowPlan.ShowBrowseScrollbars");
        source.Should().Contain("VerticalScrollBarVisibility = windowPlan.ShowBrowseScrollbars");
        source.Should().Contain("_runtime.StartRendererSession();");
        source.Should().Contain("_runtime.HandleKioskRestartElapsed();");
        source.Should().Contain("ISlideShowDisplayRenderer.RequestKioskRestart() => _runtime.RestartKioskShow();");
        source.Should().NotContain("private void StartKioskRestartTimer()");
        source.Should().NotContain("_presentation.ShowBrowseScrollbar");
        source.Should().NotContain("SlideShowKioskRestartPlanner");
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

        source.Should().Contain("_runtime.HandleKeyboardInput(");
        source.Should().Contain("_runtime.ExecuteSlideNumberJump(");
        source.Should().Contain("ExecuteSlideNumberJump");
        source.Should().Contain("_runtime.SetScreenMode(mode);");
        source.Should().Contain("_runtime.ExecuteAdvance(");
        source.Should().Contain("_runtime.ExecuteBack(");
        source.Should().Contain("navigation.UseDestinationBackground");
        source.Should().Contain("navigation => DisplayCurrentSlide(");
        source.Should().Contain("_runtime.HandlePointerInput(");
        source.Should().Contain("_runtime.ActivateHyperlink(");
        source.Should().Contain("_runtime.DisplayCurrentSlide(");
        source.Should().Contain("_runtime.CreatePresenterState(");
        source.Should().Contain("_runtime.PresenterSummary");
        source.Should().Contain("SlideShowInkExecutionPlanner.BuildOverlayRenderPlan(");
        source.Should().Contain("SlideShowRuntimeApplication");
        source.Should().NotContain("SlideShowDisplayCoordinator _displayCoordinator");
        source.Should().Contain("ISlideShowDisplayRenderer");
        source.Should().Contain("_runtime.ApplyPresenterToolIntent(");
        source.Should().Contain("_runtime.CloseRendererSession(");
        source.Should().Contain("_runtime.BeginPointerInk(");
        source.Should().Contain("_runtime.AppendPointerInk(");
        source.Should().Contain("_runtime.EndPointerInk(");
        source.Should().Contain("_runtime.ClearInkStrokes(");
        source.Should().Contain("_runtime.UndoLastInkStroke(");
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
        source.Should().Contain("_runtime.HitTestHyperlink(");
        source.Should().NotContain("SlideShowSessionController");
        source.Should().NotContain("SlideShowSessionInputExecutionCallbacks");
        source.Should().NotContain("SlideShowHostExecutionCallbacks");
        source.Should().NotContain("PresentationMediaTranscriptPlanner");
        source.Should().NotContain("SlideShowHostPlanner.MapCanvasPointToSlide(");
        source.Should().NotContain("case SlideShowPointerClickIntentKind.");
        source.Should().NotContain("if (e.Key == Key.P");
        source.Should().NotContain("if (hlink.IsExternal)");
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
        source.Should().NotContain("SlideShowPlaybackPlanner.ConveyorStartScale");
        source.Should().NotContain("SlideShowPlaybackPlanner.ConveyorTiltDegrees");
        source.Should().Contain("PlayWindowTransition(");
        source.Should().Contain("PlayMorphTransition(");
        source.Should().Contain("SlideShowMorphPlanner.BuildRendererPlan(");
        source.Should().NotContain("SlideShowMorphPlanner.Plan(");
        source.Should().NotContain("MorphTokenScreenRect(");
        source.Should().Contain("PlayFlipTransition(");
        source.Should().Contain("PlayCubeTransition(");
        source.Should().Contain("PlayRotateTransition(");
        source.Should().Contain("SlideShowPerspectiveTransitionPlanner.Plan(");
        source.Should().Contain("BuildPerspectiveMatrix(");
        source.Should().Contain("ISlideShowTransitionPlaybackRenderer.PlayPolygonClip(");
        source.Should().Contain("PlayPolygonClipTransition(");
        source.Should().Contain("BuildPolygonClipGeometry(");
        source.Should().Contain("SlideShowPolygonClipTransitionPlanner.ResolveFrameProgress(");
        source.Should().NotContain("PlayHoneycombTransition(");
        source.Should().NotContain("SlideShowHoneycombTransitionPlanner.Plan(");
        source.Should().Contain("PlaySwitchTransition(");
        source.Should().Contain("PlayOrbitTransition(");
        source.Should().Contain("PlayFerrisTransition(");
        source.Should().Contain("PlayFlythroughTransition(");
        source.Should().Contain("PlayPageCurlTransition(");
        source.Should().Contain("SlideShowPageCurlTransitionPlanner.Plan(");
        source.Should().Contain("BuildPageCurlGeometry(");
        source.Should().Contain("AnimateWindowTransition(");
        source.Should().Contain("BuildWindowTransitionGeometry(");
        source.Should().Contain("SlideShowTransformTransitionPlan transformPlan");
        source.Should().Contain("transformPlan.ResolveIncoming(");
        source.Should().Contain("transformPlan.ResolveOutgoing(");
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
        source.Should().NotContain("plan.ZoomIn");
        source.Should().NotContain("SlideShowPlaybackPlanner.ZoomInStartScale");
        source.Should().NotContain("SlideShowPlaybackPlanner.GalleryTravelFactor");
        source.Should().NotContain("SlideShowPlaybackPlanner.ConveyorTravelFactor");
        source.Should().NotContain("SlideShowPlaybackPlanner.WindowInitialOpenFactor");
        source.Should().Contain("BuildDissolveTransitionGeometry(");
        source.Should().Contain("BuildBoxTransitionGeometry(");
        source.Should().Contain("BuildRevealTransitionGeometry(");
        source.Should().Contain("BuildUncoverTransitionGeometry(");
        source.Should().Contain("SlideShowPlaybackPlanner.DissolveRowCount");
        source.Should().Contain("SlideShowPlaybackPlanner.DissolveColumnCount");
        source.Should().Contain("_runtime.AnimationRendererSession.PlanStep(");
        source.Should().Contain("_runtime.AnimationRendererSession.PlanFrame(");
        source.Should().NotContain("SlideShowPlaybackPlanner.PlanAnimationStep(");
        source.Should().NotContain("SlideShowPlaybackPlanner.PlanFallbackAnimation(");
        source.Should().Contain("_slideCanvas.SuppressedShapeIds.Add(animation.ShapeId);");
        source.Should().Contain("RevealShape(animation.ShapeId);");
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
        source.Should().NotContain("private readonly SlideShowController _controller");
        source.Should().NotContain("private string _slideNumberBuffer");
    }

    [Fact]
    public void AvaloniaSlideShowWindow_DelegatesDisplaySequencingToPortableCoordinator()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));

        var displayStart = source.IndexOf("private void DisplayCurrentSlide(", StringComparison.Ordinal);
        var adapterStart = source.IndexOf(
            "void ISlideShowDisplayRenderer.ApplyDisplayState(",
            displayStart,
            StringComparison.Ordinal);
        displayStart.Should().BeGreaterThanOrEqualTo(0);
        adapterStart.Should().BeGreaterThan(displayStart);
        var displayMethod = source[displayStart..adapterStart];

        displayMethod.Should().Contain("_runtime.DisplayCurrentSlide(");
        displayMethod.Should().NotContain("PrepareAnimationOverlay(");
        displayMethod.Should().NotContain("_mediaController.EnterSlide(");
        displayMethod.Should().NotContain("_autoAdvanceTimer");
        source.Should().Contain("_runtime.HandleAutoAdvanceElapsed(");
        source.Should().Contain("_runtime.TogglePresenterView();");
        source.Should().Contain("_runtime.CloseRendererSession(nowUtc);");
        source.Should().NotContain("SlideShowDisplayCoordinator _displayCoordinator");
        source.Should().Contain("void ISlideShowDisplayRenderer.CancelVisualOperations() => CancelActiveTimers();");
        source.Should().Contain("void ISlideShowDisplayRenderer.EnterMediaSlide(");
    }

    [Fact]
    public void AvaloniaSlideShowWindow_ExecutesAnimationStepsThroughSharedPlaybackPlans()
    {
        var workspaceDirectory = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            workspaceDirectory,
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));
        var effectTrackSource = File.ReadAllText(Path.Combine(
            workspaceDirectory,
            "freep",
            "FreeP.App.Presentation",
            "SlideShowAnimationEffectTrackPlanner.cs"));

        source.Should().Contain("new SlideShowRuntimeRendererCallbacks(");
        source.Should().Contain("PlayAnimationStep,");
        source.Should().Contain("private void PlayAnimationStep(AnimationStep step)");
        source.Should().Contain("_runtime.AnimationRendererSession.PlanStep(");
        source.Should().Contain("BuildAnimationTargetAvailability()");
        source.Should().Contain("SlideShowAnimationTargetRegistry<Control>");
        source.Should().Contain("_animationTargets.BuildAvailability()");
        source.Should().Contain("_animationTargets.Resolve(operation)");
        source.Should().NotContain("Dictionary<uint, Control> _anim");
        source.Should().Contain("foreach (var operation in rendererPlan.Operations)");
        source.Should().Contain("ResolveAnimationTarget(operation)");
        source.Should().Contain("_runtime.AnimationRendererSession.PlanFrame(");
        source.Should().Contain("if (operation.SuppressBaseBeforePlayback)");
        source.Should().Contain("operation.RevealBaseUsingPlaybackTiming");
        source.Should().Contain("PlayShapeAnimationWithTiming(");
        source.Should().Contain("private void PlayShapeAnimationWithTiming(");
        source.Should().Contain("PlayShapeAnimationPass(element, plan, onReveal, repeat.PassCount, 0);");
        source.Should().Contain("_runtime.AnimationRendererSession.PlanRepeatPass(");
        source.Should().Contain("PlayShapeAnimationPass(element, basePlan, onReveal, passCount, passIndex + 1)");
        source.Should().Contain("PlayFallbackAnimation(operation);");
        source.Should().Contain("PlayFallbackAnimation(operation.FallbackAnimation);");
        source.Should().NotContain("BuildReverseAnimationPlan(");
        source.Should().NotContain("SlideShowPlaybackPlanner.PlanAnimationStep(");
        source.Should().NotContain("SlideShowPlaybackPlanner.PlanFallbackAnimation(");
        source.Should().NotContain("_lastAnimationFramePlan");

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
        source.Should().Contain("ScalarTrackEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Swivel:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Bounce:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Float:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Swoop:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Boomerang:");
        source.Should().Contain("TrajectoryEffect(element, plan, onReveal);");
        source.Should().Contain("SlideShowAnimationEffectFramePlanner.Build(");
        source.Should().Contain("SlideShowAnimationEffectFramePlanner.SampleSmooth(");
        source.Should().NotContain("private void BounceEffect(");
        source.Should().NotContain("private void FloatEffect(");
        source.Should().NotContain("private void SwoopEffect(");
        source.Should().NotContain("private void BoomerangEffect(");
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
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.GrowShrink:");
        source.Should().Contain("ScalarTrackEffect(element, plan);");
        source.Should().Contain("_runtime.AnimationRendererSession.PlanEffectTracks(playback)");
        effectTrackSource.Should().Contain("SlideShowShapeAnimationEffectKind.GrowShrink");
        effectTrackSource.Should().Contain("ScaleX: Lerp(plan.FromScaleX, plan.PeakScaleX, phaseProgress)");
        effectTrackSource.Should().Contain("ScaleY: Lerp(plan.FromScaleY, plan.PeakScaleY, phaseProgress)");
        effectTrackSource.Should().Contain("ScaleX: Lerp(plan.PeakScaleX, plan.ToScaleX, phaseProgress)");
        effectTrackSource.Should().Contain("ScaleY: Lerp(plan.PeakScaleY, plan.ToScaleY, phaseProgress)");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Spin:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Teeter:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Blink:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.FlashBulb:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Flicker:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Wave:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ColorPulse:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ChangeColor:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ColorWave:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ChangeLineColor:");
        source.Should().Contain("LineColorEffect(element, plan);");
        source.Should().Contain("foreach (var layerPlan in shapePlan.AuxiliaryLayers)");
        source.Should().Contain("_animationTargets.Register(shapeId, layerPlan.TargetKind, layer);");
        source.Should().NotContain("shapePlan.FillMaskShape");
        source.Should().NotContain("shapePlan.LineColorShape");
        source.Should().NotContain("_animFontStyleElements");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ChangeFontStyle:");
        source.Should().Contain("FontStyleEffect(element, plan);");
        source.Should().NotContain("_animFontSizeElements");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.ChangeFontSize:");
        source.Should().Contain("FontSizeEffect(element, plan);");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.GrowWithColor:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Shimmer:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Bold:");
        source.Should().Contain("case SlideShowShapeAnimationEffectKind.Underline:");
        source.Should().Contain("_runtime.AnimationRendererSession.PlanEffectTracks(playback)");
        source.Should().Contain("SlideShowAnimationEffectTrackPlanner.Sample(track, progress)");
        source.Should().Contain("SlideShowAnimationEffectTrackPlanner.ResolveTimerStepCount(plan.DurationMs)");
        source.Should().Contain("AddAuthoredColorOverlay(element, playback);");
        source.Should().NotContain("private void TeeterEffect(");
        source.Should().NotContain("private void BlinkEffect(");
        source.Should().NotContain("private void FlashBulbEffect(");
        source.Should().NotContain("private void FlickerEffect(");
        source.Should().NotContain("private void WaveEffect(");
        source.Should().NotContain("private void EmphasisPulseEffect(");
        source.Should().NotContain("private void ColorWaveEffect(");
        source.Should().Contain("DisappearEffect(element, plan.DelayMs);");
        source.Should().Contain("var isExit = plan.Animation.Kind == AnimationKind.Exit;");
        source.Should().Contain("var randomBars = SlideShowMaskGeometryPlanner.BuildRandomBars(");
        source.Should().Contain("var closed = ToRect(randomBar.Geometry.Closed);");
        source.Should().Contain("var from = isExit ? open : closed;");
        source.Should().Contain("el.Opacity = isExit ? plan.FromOpacity : 0;");
        source.Should().Contain("AnimateTranslate(el, isExit ? 0 : dx, isExit ? 0 : dy,");
        source.Should().Contain("var fromX = isExit ? 0 : dx;");
        source.Should().Contain("var toX = isExit ? dx : 0;");
        source.Should().Contain("MotionPathEffect(element, plan, onReveal);");
        source.Should().Contain("AnimateOpacity(_slideCanvas, plan.FromOpacity, plan.FlashOpacity, plan.DurationMs / 2");
    }

    [Fact]
    public void AvaloniaShapePlayback_UsesAuthoredAccelerationAndDecelerationEasing()
    {
        var workspaceDirectory = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            workspaceDirectory,
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));
        var effectTrackSource = File.ReadAllText(Path.Combine(
            workspaceDirectory,
            "freep",
            "FreeP.App.Presentation",
            "SlideShowAnimationEffectTrackPlanner.cs"));
        var framePlannerSource = File.ReadAllText(Path.Combine(
            workspaceDirectory,
            "freep",
            "FreeP.App.Presentation",
            "SlideShowPlaybackFramePlanner.cs"));

        source.Should().Contain("_runtime.AnimationRendererSession.PlanEffectTracks(playback)");
        source.Should().Contain("_runtime.AnimationRendererSession.PlanFrame(");
        effectTrackSource.Should().Contain("SlideShowPlaybackPlanner.ApplyTimingEasing(");
        effectTrackSource.Should().Contain("plan.Acceleration");
        effectTrackSource.Should().Contain("plan.Deceleration");
        framePlannerSource.Should().Contain("SlideShowPlaybackPlanner.ApplyTimingEasing(");
        framePlannerSource.Should().Contain("plan.Acceleration");
        framePlannerSource.Should().Contain("plan.Deceleration");
    }

}
