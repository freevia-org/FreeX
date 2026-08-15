using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SlideShowHostPolicySourceTests
{
    [Fact]
    public void WpfMediaPlaybackPassesWebVttRegionsToSharedCaptionPlacement()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "SlideShowMediaController.cs"));

        source.Should().Contain("slot.CaptionTrack?.Regions");
        source.Should().Contain("PresentationMediaTranscriptPlanner.PlanOverlayPlacement(");
    }

    [Fact]
    public void WpfSlideShowWindow_ConsumesBrowseScrollbarAndKioskRestartState()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
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
    public void WpfSlideShowWindow_DelegatesHostPolicyToPresentationPlanner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "SlideShowWindow.cs"));
        var testAccessSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "TestSupport",
            "SlideShow.Wpf",
            "SlideShowWindow.TestAccess.cs"));
        var runtimeSessionSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "SlideShowRuntimeSession.cs"));
        var sharedWindowApiSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "RendererShared",
            "SlideShowWindow.RuntimeSession.cs"));
        var portableSurfaceSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "RendererShared",
            "SlideShowWindow.PortableSurface.cs"));

        source.Should().Contain("_runtime.HandleKeyboardInput(");
        runtimeSessionSource.Should().Contain("_runtime.ExecuteSlideNumberJump(");
        sharedWindowApiSource.Should().Contain("ExecuteSlideNumberJump");
        runtimeSessionSource.Should().Contain("_runtime.SetScreenMode(mode);");
        runtimeSessionSource.Should().Contain("_runtime.ExecuteAdvance(");
        runtimeSessionSource.Should().Contain("_runtime.ExecuteBack(");
        source.Should().Contain("_runtime.HandlePointerInput(");
        source.Should().Contain("_runtime.ActivateHyperlink(");
        portableSurfaceSource.Should().Contain("_runtime.DisplayCurrentSlide(");
        runtimeSessionSource.Should().Contain("_runtime.CreatePresenterState(");
        runtimeSessionSource.Should().Contain("_runtime.PresenterSummary");
        source.Should().Contain("SlideShowInkNativeProjectionSession.Apply(");
        source.Should().Contain("_inkOverlay.Children.Clear");
        source.Should().Contain("AddInkStroke,");
        source.Should().Contain("AddLaserOverlay");
        source.Should().NotContain("SlideShowInkExecutionPlanner.BuildOverlayRenderPlan(");
        source.Should().Contain("SlideShowRuntimeApplication");
        source.Should().NotContain("SlideShowDisplayCoordinator _displayCoordinator");
        source.Should().Contain("ISlideShowDisplayRenderer");
        runtimeSessionSource.Should().Contain("_runtime.ApplyPresenterToolIntent(");
        source.Should().Contain("_runtime.CloseRendererSession(");
        runtimeSessionSource.Should().Contain("_runtime.BeginPointerInk(");
        runtimeSessionSource.Should().Contain("_runtime.AppendPointerInk(");
        runtimeSessionSource.Should().Contain("_runtime.EndPointerInk(");
        runtimeSessionSource.Should().Contain("_runtime.ClearInkStrokes(");
        runtimeSessionSource.Should().Contain("_runtime.UndoLastInkStroke(");
        source.Should().NotContain("public AdvanceResult ExecuteAdvance(");
        source.Should().NotContain("public SlideShowPresenterToolPlan ApplyPresenterToolIntent(");
        source.Should().Contain("SlideShowMaskTimelinePlanner.BuildBlindsRendererPlan(");
        source.Should().Contain("SlideShowMaskTimelinePlanner.BuildRandomBarsRendererPlan(");
        source.Should().Contain("SlideShowMaskTimelinePlanner.BuildCheckerboardRendererPlan(");
        source.Should().Contain("var bars = new GeometryGroup();");
        source.Should().Contain("RectangleGeometry.RectProperty");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildCircle(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildDiamondPoint(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildPlusRects(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildStrips(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildWedge(");
        source.Should().Contain("SlideShowMaskGeometryPlanner.BuildWheel(");
        source.Should().NotContain("SlideShowMaskGeometryPlanner.BuildBlindsBand(");
        source.Should().NotContain("SlideShowMaskGeometryPlanner.BuildRandomBars(");
        source.Should().NotContain("SlideShowMaskGeometryPlanner.BuildCheckerboardCell(");
        source.Should().NotContain("SlideShowMaskGeometryPlanner.IsSecondCheckerboardPhase(");
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
        source.Should().Contain("PlayConveyorTransition(");
        source.Should().NotContain("SlideShowPlaybackPlanner.ConveyorStartScale");
        source.Should().NotContain("SlideShowPlaybackPlanner.ConveyorTiltDegrees");
        source.Should().Contain("PlayWindowTransition(");
        source.Should().Contain("PlayMorphTransition(");
        source.Should().Contain("SlideShowMorphPlanner.BuildRendererPlan(");
        source.Should().NotContain("SlideShowMorphPlanner.Plan(");
        source.Should().NotContain("MorphTokenScreenRect(");
        source.Should().Contain("ISlideShowTransitionPlaybackRenderer.PlayPerspective(");
        source.Should().Contain("PlayPerspectiveTransition(");
        source.Should().Contain("SlideShowPerspectiveTransitionPlan perspective");
        source.Should().NotContain("SlideShowPerspectiveTransitionPlanner.Plan(");
        source.Should().NotContain("PlayFlipTransition(");
        source.Should().NotContain("PlayCubeTransition(");
        source.Should().NotContain("PlayRotateTransition(");
        source.Should().Contain("ISlideShowTransitionPlaybackRenderer.PlayPolygonClip(");
        source.Should().Contain("PlayPolygonClipTransition(");
        source.Should().Contain("BuildPolygonClipGeometry(");
        source.Should().Contain("SlideShowPolygonClipTransitionPlanner.ResolveFrameProgress(");
        source.Should().NotContain("PlayHoneycombTransition(");
        source.Should().NotContain("SlideShowHoneycombTransitionPlanner.Plan(");
        source.Should().NotContain("PlaySwitchTransition(");
        source.Should().NotContain("PlayOrbitTransition(");
        source.Should().NotContain("PlayFerrisTransition(");
        source.Should().NotContain("PlayFlythroughTransition(");
        source.Should().Contain("PlayPageCurlTransition(");
        source.Should().Contain("SlideShowPageCurlTransitionPlanner.Plan(");
        source.Should().Contain("BuildPageCurlGeometry(");
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
        source.Should().Contain("outgoingTranslate.BeginAnimation(");
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
        source.Should().NotContain("LastAnimationFramePlanForTest");
        source.Should().NotContain("LastAnimationStepFrameEvidenceForTest");
        source.Should().NotContain("LastAnimationStepPlaybackReadinessPlanForTest");
        testAccessSource.Should().Contain("LastAnimationFramePlanForTest");
        testAccessSource.Should().Contain("LastAnimationStepFrameEvidenceForTest");
        testAccessSource.Should().Contain("LastAnimationStepPlaybackReadinessPlanForTest");

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
    public void WpfHost_UsesCanonicalSlideshowTypesWithoutCompatibilityAliases()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");

        File.Exists(Path.Combine(root, "freep", "FreeP.App.Host", "SlideShowController.cs"))
            .Should().BeFalse();
        File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "GlobalUsings.cs"))
            .Should().Contain("global using FreeP.App.Compositor;")
            .And.NotContain("global using SlideShowController =")
            .And.NotContain("global using AnimationStep =");
    }

    [Fact]
    public void WpfSlideShowWindow_ExecutesAnimationStepsThroughSharedPlaybackPlans()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "SlideShowWindow.cs"));
        var portableSurfaceSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "RendererShared",
            "SlideShowWindow.PortableSurface.cs"));

        source.Should().Contain("new SlideShowRuntimeRendererCallbacks(");
        source.Should().Contain("PlayAnimationStep,");
        source.Should().Contain("private void PlayAnimationStep(AnimationStep step)");
        source.Should().Contain("_runtime.AnimationRendererSession.PlanStep(");
        source.Should().Contain("BuildAnimationTargetAvailability()");
        source.Should().Contain("SlideShowAnimationTargetRegistry<FrameworkElement>");
        portableSurfaceSource.Should().Contain("_animationTargets.BuildAvailability()");
        source.Should().Contain("_animationTargets.Resolve(operation)");
        source.Should().NotContain("Dictionary<uint, FrameworkElement> _anim");
        source.Should().Contain("_runtime.AnimationRendererSession.ExecuteStep(");
        source.Should().Contain("ResolveAnimationTarget,");
        source.Should().Contain("_runtime.AnimationRendererSession.PlanFrame(");
        source.Should().Contain("PlayShapeAnimation);");
        source.Should().NotContain("foreach (var operation in rendererPlan.Operations)");
        source.Should().NotContain("if (operation.SuppressBaseBeforePlayback)");
        source.Should().Contain("AttachReveal(sb, operation, route);");
        source.Should().Contain("PlayFallbackAnimation,");
        source.Should().Contain("var visibilityPlan = operation.FallbackVisibility");
        source.Should().Contain("if (visibilityPlan.SuppressAtStart || visibilityPlan.SuppressAtCompletion)");
        source.Should().Contain("_slideCanvas.SuppressedShapeIds.Add(animation.ShapeId);");
        source.Should().Contain("RevealShape(animation.ShapeId);");
        source.Should().Contain("PlayFallbackAnimation(operation.FallbackAnimation);");
        source.Should().Contain("private static void ApplyRepeatTiming(");
        source.Should().Contain("SlideShowAnimationStepRendererPlanner.BuildRepeatPlan(plan)");
        source.Should().Contain("RepeatBehavior.Forever");
        source.Should().Contain("timeline.RepeatBehavior = repeatBehavior;");
        source.Should().Contain("timeline.AutoReverse = true;");
        source.Should().NotContain("SlideShowPlaybackPlanner.PlanAnimationStep(");
        source.Should().NotContain("SlideShowPlaybackPlanner.PlanFallbackAnimation(");
        source.Should().NotContain("_lastAnimationFramePlan");

        source.Should().Contain("var route = SlideShowAnimationRendererRoutePlanner.Build(plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.Instant:");
        source.Should().Contain("route.InstantVisibility == SlideShowAnimationInstantVisibilityKind.Hide");
        source.Should().Contain("AppearEffect(sb, element, plan.DelayMs);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.Opacity:");
        source.Should().Contain("FadeEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.Fly:");
        source.Should().Contain("FlyInEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.WipeMask:");
        source.Should().Contain("WipeEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.SplitMask:");
        source.Should().Contain("SplitEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.RandomBarsMask:");
        source.Should().Contain("RandomBarsEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.BlindsMask:");
        source.Should().Contain("BlindsEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.BoxMask:");
        source.Should().Contain("BoxEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.CheckerboardMask:");
        source.Should().Contain("CheckerboardEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.GeometricMask:");
        source.Should().Contain("GeometricMaskEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.DissolveMask:");
        source.Should().Contain("DissolveEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.Flash:");
        source.Should().Contain("FlashEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.ScalarTrack:");
        source.Should().Contain("ScalarTrackEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.Trajectory:");
        source.Should().Contain("TrajectoryEffect(sb, element, plan);");
        source.Should().Contain("SlideShowAnimationEffectFramePlanner.Build(");
        source.Should().Contain("AddTrajectoryAxisAnimation(");
        source.Should().NotContain("private void BounceEffect(");
        source.Should().NotContain("private void FloatEffect(");
        source.Should().NotContain("private void SwoopEffect(");
        source.Should().NotContain("private void BoomerangEffect(");
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
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.Peek:");
        source.Should().Contain("PeekEffect(sb, element, plan);");
        source.Should().Contain("private void PeekEffect(Storyboard sb, FrameworkElement el,");
        source.Should().Contain("el.RenderTransform = translate;");
        source.Should().Contain("el.Clip = new RectangleGeometry(new Rect(0, 0, w, h));");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.Crawl:");
        source.Should().Contain("CrawlEffect(sb, element, plan);");
        source.Should().Contain("private void CrawlEffect(Storyboard sb, FrameworkElement el,");
        source.Should().Contain("PeekEffect(sb, el, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.Zoom:");
        source.Should().Contain("ZoomEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.LineColor:");
        source.Should().Contain("LineColorEffect(sb, element, plan);");
        source.Should().NotContain("shapePlan.FillMaskShape");
        source.Should().NotContain("shapePlan.LineColorShape");
        source.Should().NotContain("_animFontStyleElements");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.TextStyle:");
        source.Should().Contain("FontStyleEffect(sb, element, plan);");
        source.Should().NotContain("_animFontSizeElements");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.FontSize:");
        source.Should().Contain("FontSizeEffect(sb, element, plan);");
        source.Should().Contain("case SlideShowAnimationRendererRouteKind.FillColor:");
        source.Should().Contain("FillColorEffect(sb, element, plan);");
        source.Should().Contain("_runtime.AnimationRendererSession.PlanEffectTracks(playback)");
        source.Should().Contain("SlideShowAnimationScalarPropertyKind.TranslateXFactor");
        source.Should().Contain("keyFrame.InterpolationKind == SlideShowAnimationScalarInterpolationKind.Discrete");
        source.Should().Contain("AddAuthoredColorOverlay(storyboard, element, playback);");
        source.Should().NotContain("private static void TeeterEffect(");
        source.Should().NotContain("private static void BlinkEffect(");
        source.Should().NotContain("private static void FlashBulbEffect(");
        source.Should().NotContain("private static void FlickerEffect(");
        source.Should().NotContain("private static void WaveEffect(");
        source.Should().NotContain("private static void EmphasisPulseEffect(");
        source.Should().NotContain("private static void ColorWaveEffect(");
        source.Should().Contain("DisappearEffect(sb, element, plan.DelayMs);");
        source.Should().Contain("var isExit = plan.Animation.Kind == AnimationKind.Exit;");
        source.Should().Contain("SlideShowMaskTimelinePlanner.BuildRandomBarsRendererPlan(plan, w, h)");
        source.Should().Contain("var from = ToRect(elementPlan.From);");
        source.Should().Contain("rendererPlan.OpacityTrack!.KeyFrames");
        source.Should().Contain("isExit ? 0 : dx, isExit ? dx : 0");
        source.Should().Contain("var fromX = isExit ? 0 : dx;");
        source.Should().Contain("var toX = isExit ? dx : 0;");
        source.Should().Contain("MotionPathEffect(sb, element, plan);");
        source.Should().Contain("Storyboard.SetTarget(flashAnim, _slideCanvas);");
    }

    [Fact]
    public void WpfSlideShowWindow_DelegatesOverlaySequencingToSharedMaterializer()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "SlideShowWindow.cs"));

        source.Should().Contain("SlideShowAnimationOverlayMaterializer.Materialize<FrameworkElement, BitmapSource>(");
        source.Should().Contain("CreateAnimationOverlayElement(bitmap, w, h, elementPlan)");
        source.Should().Contain("_animationTargets,");
        source.Should().Contain("_slideCanvas.SuppressedShapeIds);");
        source.Should().NotContain("foreach (var layerPlan in shapePlan.AuxiliaryLayers)");
    }

    [Fact]
    public void WpfSlideShowWindow_DelegatesDisplaySequencingToPortableCoordinator()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "SlideShowWindow.cs"));
        var portableSurface = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "RendererShared",
            "SlideShowWindow.PortableSurface.cs"));

        portableSurface.Should().Contain("private void DisplayCurrentSlide(");
        portableSurface.Should().Contain("_runtime.DisplayCurrentSlide(");
        portableSurface.Should().NotContain("PrepareAnimationOverlay(");
        portableSurface.Should().NotContain("_mediaController.EnterSlide(");
        portableSurface.Should().NotContain("_autoAdvanceTimer");
        source.Should().NotContain("private void DisplayCurrentSlide(");
        source.Should().Contain("_runtime.HandleAutoAdvanceElapsed(");
        source.Should().Contain("TogglePresenterView,");
        source.Should().NotContain("_runtime.TogglePresenterView();");
        source.Should().Contain("_runtime.CloseRendererSession(nowUtc);");
        source.Should().NotContain("SlideShowDisplayCoordinator _displayCoordinator");
        source.Should().Contain("void ISlideShowDisplayRenderer.CancelVisualOperations()");
        source.Should().Contain("void ISlideShowDisplayRenderer.EnterMediaSlide(");
    }

    [Fact]
    public void WpfShapePlayback_UsesAuthoredAccelerationAndDecelerationEasing()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var windowSource = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Host", "SlideShowWindow.cs"));
        var easingSource = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Host", "PowerPointAnimationEasing.cs"));

        var animationStart = windowSource.IndexOf(
            "private void PlayShapeAnimation(",
            StringComparison.Ordinal);
        var teardownStart = windowSource.IndexOf(
            "private void PlayFallbackAnimation(",
            animationStart,
            StringComparison.Ordinal);
        animationStart.Should().BeGreaterThanOrEqualTo(0);
        teardownStart.Should().BeGreaterThan(animationStart);

        var animationSource = windowSource[animationStart..teardownStart];
        animationSource.Should().Contain("CreateAnimationEasing(plan)");
        animationSource.Should().NotContain("new CubicEase");
        easingSource.Should().Contain("SlideShowPlaybackPlanner.ApplyHostTimingEasing");
        easingSource.Should().Contain("AccelerationProperty");
        easingSource.Should().Contain("DecelerationProperty");
        animationSource.Should().Contain("ApplyHostTimingEasing(sb, plan);");
        animationSource.Should().Contain("storyboard.Children.OfType<DoubleAnimation>()");
        animationSource.Should().Contain("storyboard.Children.OfType<DoubleAnimationUsingKeyFrames>()");
        animationSource.Should().Contain("InvertHostTimingEasing");
        animationSource.Should().Contain("KeyTimeType.Percent");
    }

}
