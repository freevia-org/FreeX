using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowHostIntent
{
    None,
    Close,
    Advance,
    Back,
    FirstSlide,
    LastSlide
}

public enum SlideShowHostCommandKind
{
    None,
    Close,
    PlayAnimationStep,
    NavigateToSlide
}

public enum SlideShowPointerClickIntentKind
{
    NoOp,
    Trigger,
    Zoom,
    Hyperlink,
    Advance
}

public sealed record SlideShowPointerClickIntent(
    SlideShowPointerClickIntentKind Kind,
    uint? TriggerShapeId = null,
    Hyperlink? Hyperlink = null,
    int? TargetSlideIndex = null,
    bool ReturnToParent = false,
    int? TransitionDurationMs = null,
    bool ShowBackground = true)
{
    public bool IsHandled => Kind is
        SlideShowPointerClickIntentKind.Trigger or
        SlideShowPointerClickIntentKind.Zoom or
        SlideShowPointerClickIntentKind.Hyperlink;
}

public sealed record SlideShowSlideMetrics(double WidthDip, double HeightDip)
{
    public static readonly SlideShowSlideMetrics Default = new(960, 540);
}

public sealed record SlideShowPoint(double X, double Y);

public sealed record SlideShowHostState(
    int SlideCount,
    int CurrentSlideIndex,
    bool HasSlides,
    bool IsFirstSlide,
    bool IsLastSlide,
    bool HasPendingSteps,
    string StatusText);

public sealed record SlideShowHostDisplayPlan(
    Slide? Slide,
    SlideShowSlideMetrics Metrics,
    SlideTransition? Transition,
    int? AutoAdvanceAfterMs,
    bool UseDestinationBackground = true);

public sealed record SlideShowPresenterDisplayIntent(
    bool IsFullScreenRequested,
    int? MonitorIndex = null,
    string? MonitorName = null)
{
    public static SlideShowPresenterDisplayIntent FullScreen { get; } = new(true);
}

public sealed record SlideShowPresenterSlideState(
    int SlideIndex,
    int PresentationSlideIndex,
    string SlideId,
    string Title,
    Slide Slide);

public sealed record SlideShowPresenterState(
    SlideShowHostState HostState,
    SlideShowPresenterSlideState? CurrentSlide,
    SlideShowPresenterSlideState? NextSlide,
    string NotesText,
    DateTimeOffset StartedAtUtc,
    TimeSpan Elapsed,
    SlideShowPresenterDisplayIntent DisplayIntent,
    SlideShowPresenterToolPlan ToolPlan);

public sealed record SlideShowHiddenSlideTarget(Slide Slide, int SourceSlideIndex);

public sealed record SlideShowHostCommand
{
    private SlideShowHostCommand(
        SlideShowHostCommandKind kind,
        bool isHandled,
        bool stopAutoAdvance,
        bool animateSlide,
        int slideIndex,
        Slide? slide,
        AnimationStep? step,
        AdvanceResult? advanceResult,
        BackResult? backResult,
        int? transitionDurationMs,
        bool useDestinationBackground)
    {
        Kind = kind;
        IsHandled = isHandled;
        StopAutoAdvance = stopAutoAdvance;
        AnimateSlide = animateSlide;
        SlideIndex = slideIndex;
        Slide = slide;
        Step = step;
        AdvanceResult = advanceResult;
        BackResult = backResult;
        TransitionDurationMs = transitionDurationMs;
        UseDestinationBackground = useDestinationBackground;
    }

    public SlideShowHostCommandKind Kind { get; }

    public bool IsHandled { get; }

    public bool StopAutoAdvance { get; }

    public bool AnimateSlide { get; }

    public int SlideIndex { get; }

    public Slide? Slide { get; }

    public AnimationStep? Step { get; }

    public AdvanceResult? AdvanceResult { get; }

    public BackResult? BackResult { get; }

    /// <summary>Authored Zoom transition duration override in milliseconds.</summary>
    public int? TransitionDurationMs { get; }

    /// <summary>
    /// Whether the destination slide background participates in an authored Zoom transition.
    /// PowerPoint's omitted showBg attribute defaults to true.
    /// </summary>
    public bool UseDestinationBackground { get; }

    public static SlideShowHostCommand Ignored { get; } = new(
        SlideShowHostCommandKind.None,
        isHandled: false,
        stopAutoAdvance: false,
        animateSlide: false,
        slideIndex: -1,
        slide: null,
        step: null,
        advanceResult: null,
        backResult: null,
        transitionDurationMs: null,
        useDestinationBackground: true);

    public static SlideShowHostCommand HandledNoOp(
        bool stopAutoAdvance = false,
        AdvanceResult? advanceResult = null,
        BackResult? backResult = null) => new(
            SlideShowHostCommandKind.None,
            isHandled: true,
            stopAutoAdvance,
            animateSlide: false,
            slideIndex: -1,
            slide: null,
            step: null,
            advanceResult,
            backResult,
            transitionDurationMs: null,
            useDestinationBackground: true);

    public static SlideShowHostCommand Close(
        bool stopAutoAdvance = false,
        AdvanceResult? advanceResult = null) => new(
            SlideShowHostCommandKind.Close,
            isHandled: true,
            stopAutoAdvance,
            animateSlide: false,
            slideIndex: -1,
            slide: null,
            step: null,
            advanceResult,
            backResult: null,
            transitionDurationMs: null,
            useDestinationBackground: true);

    public static SlideShowHostCommand PlayStep(
        AnimationStep step,
        bool stopAutoAdvance = false,
        AdvanceResult? advanceResult = null) => new(
            SlideShowHostCommandKind.PlayAnimationStep,
            isHandled: true,
            stopAutoAdvance,
            animateSlide: false,
            slideIndex: -1,
            slide: null,
            step,
            advanceResult,
            backResult: null,
            transitionDurationMs: null,
            useDestinationBackground: true);

    public static SlideShowHostCommand Navigate(
        Slide slide,
        int slideIndex,
        bool animateSlide,
        bool stopAutoAdvance = false,
        AdvanceResult? advanceResult = null,
        BackResult? backResult = null,
        int? transitionDurationMs = null,
        bool useDestinationBackground = true) => new(
            SlideShowHostCommandKind.NavigateToSlide,
            isHandled: true,
            stopAutoAdvance,
            animateSlide,
            slideIndex,
            slide,
            step: null,
            advanceResult,
            backResult,
            transitionDurationMs,
            useDestinationBackground);
}

public static class SlideShowHostPlanner
{
    public const double EmusPerDip = DrawingMlCoordinateUnits.EmuPerPixel;
    public const string NoSlidesStatusText = "No slides";

    public static SlideShowHiddenSlideTarget? FindNextHiddenSlide(
        Presentation presentation,
        SlideShowPlaybackRoute playbackRoute,
        int currentSourceSlideIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(playbackRoute);

        var routeSources = playbackRoute.CustomShowName is null
            ? null
            : playbackRoute.SourceSlideIndices.ToHashSet();
        var firstCandidate = Math.Max(-1, currentSourceSlideIndex) + 1;
        for (var sourceIndex = firstCandidate; sourceIndex < presentation.Slides.Count; sourceIndex++)
        {
            if (routeSources is not null && !routeSources.Contains(sourceIndex))
                continue;

            var slide = presentation.Slides[sourceIndex];
            if (slide.IsHidden)
                return new SlideShowHiddenSlideTarget(slide, sourceIndex);
        }

        return null;
    }

    /// <summary>
    /// Resolves an explicit hyperlink's target slide id against the FULL presentation
    /// (not the playback route), but only when that slide is hidden. Normal advance and
    /// <see cref="PlanInternalSlideJump"/> deliberately only see the route's visible
    /// slides so hidden slides are skipped during ordinary playback; PowerPoint still
    /// lets an authored hyperlink jump straight to a hidden slide, so callers use this
    /// as the fallback once a route-relative lookup comes up empty.
    /// </summary>
    public static SlideShowHiddenSlideTarget? FindHiddenSlideById(
        Presentation presentation,
        string? targetSlideId)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (string.IsNullOrEmpty(targetSlideId))
            return null;

        for (var sourceIndex = 0; sourceIndex < presentation.Slides.Count; sourceIndex++)
        {
            var slide = presentation.Slides[sourceIndex];
            if (slide.IsHidden && slide.Id == targetSlideId)
                return new SlideShowHiddenSlideTarget(slide, sourceIndex);
        }

        return null;
    }

    public static SlideShowHostIntent IntentFromKeyName(string? keyName) =>
        keyName?.Trim() switch
        {
            "Escape" => SlideShowHostIntent.Close,
            "Right" or "Space" or "PageDown" or "Enter" or "Return" => SlideShowHostIntent.Advance,
            "Left" or "PageUp" or "Back" => SlideShowHostIntent.Back,
            "Home" => SlideShowHostIntent.FirstSlide,
            "End" => SlideShowHostIntent.LastSlide,
            _ => SlideShowHostIntent.None
        };

    public static SlideShowHostCommand PlanKey(
        string? keyName,
        SlideShowController controller,
        IReadOnlyList<Slide> slides) =>
        PlanIntent(IntentFromKeyName(keyName), controller, slides, stopAutoAdvance: true);

    public static SlideShowHostCommand PlanIntent(
        SlideShowHostIntent intent,
        SlideShowController controller,
        IReadOnlyList<Slide> slides,
        bool stopAutoAdvance)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(slides);

        return intent switch
        {
            SlideShowHostIntent.Close => SlideShowHostCommand.Close(stopAutoAdvance),
            SlideShowHostIntent.Advance => PlanAdvance(controller, stopAutoAdvance),
            SlideShowHostIntent.Back => PlanBack(controller, stopAutoAdvance),
            SlideShowHostIntent.FirstSlide => PlanJump(controller, slides, 0, animateSlide: false, stopAutoAdvance),
            SlideShowHostIntent.LastSlide => PlanJump(controller, slides, slides.Count - 1, animateSlide: false, stopAutoAdvance),
            _ => SlideShowHostCommand.Ignored
        };
    }

    public static SlideShowHostCommand PlanAdvance(
        SlideShowController controller,
        bool stopAutoAdvance = false)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var result = controller.Advance();
        return result switch
        {
            AdvanceResult.PlayStep play => SlideShowHostCommand.PlayStep(
                play.Step,
                stopAutoAdvance,
                result),
            AdvanceResult.NavigateToSlide nav => SlideShowHostCommand.Navigate(
                nav.Slide,
                nav.SlideIndex,
                animateSlide: controller.ShowWithAnimation,
                stopAutoAdvance,
                result,
                transitionDurationMs: nav.TransitionDurationMs,
                useDestinationBackground: nav.UseDestinationBackground),
            AdvanceResult.AtEnd => SlideShowHostCommand.Close(stopAutoAdvance, result),
            _ => SlideShowHostCommand.HandledNoOp(stopAutoAdvance, advanceResult: result)
        };
    }

    public static SlideShowHostCommand PlanBack(
        SlideShowController controller,
        bool stopAutoAdvance = false)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var result = controller.Back();
        return result switch
        {
            BackResult.NavigateToSlide nav => SlideShowHostCommand.Navigate(
                nav.Slide,
                nav.SlideIndex,
                animateSlide: controller.ShowWithAnimation,
                stopAutoAdvance,
                backResult: result,
                transitionDurationMs: nav.TransitionDurationMs,
                useDestinationBackground: nav.UseDestinationBackground),
            BackResult.AtStart => SlideShowHostCommand.HandledNoOp(stopAutoAdvance, backResult: result),
            _ => SlideShowHostCommand.HandledNoOp(stopAutoAdvance, backResult: result)
        };
    }

    public static SlideShowHostCommand PlanTrigger(
        SlideShowController controller,
        uint triggerShapeId)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var step = controller.AdvanceTrigger(triggerShapeId);
        return step is null
            ? SlideShowHostCommand.HandledNoOp()
            : SlideShowHostCommand.PlayStep(step);
    }

    public static SlideShowPointerClickIntent PlanPointerClick(
        Slide? slide,
        SlideShowPoint slidePoint,
        Presentation? presentation = null)
    {
        ArgumentNullException.ThrowIfNull(slidePoint);

        if (slide is null)
        {
            return new SlideShowPointerClickIntent(SlideShowPointerClickIntentKind.Advance);
        }

        var triggerShapeId = HitTestTriggerShape(slide, slidePoint);
        if (triggerShapeId is not null)
        {
            return new SlideShowPointerClickIntent(
                SlideShowPointerClickIntentKind.Trigger,
                TriggerShapeId: triggerShapeId);
        }

        if (presentation is not null &&
            TryGetZoomTargetSlideIndex(
                presentation,
                slide,
                slidePoint,
                out var targetSlideIndex,
                out var returnToParent,
                out var transitionDurationMs,
                out var showBackground))
        {
            return new SlideShowPointerClickIntent(
                SlideShowPointerClickIntentKind.Zoom,
                TargetSlideIndex: targetSlideIndex,
                ReturnToParent: returnToParent,
                TransitionDurationMs: transitionDurationMs,
                ShowBackground: showBackground);
        }

        var hyperlink = HitTestHyperlink(slide, slidePoint);
        return hyperlink is null
            ? new SlideShowPointerClickIntent(SlideShowPointerClickIntentKind.Advance)
            : new SlideShowPointerClickIntent(
                SlideShowPointerClickIntentKind.Hyperlink,
                Hyperlink: hyperlink);
    }

    public static SlideShowHostCommand PlanZoomNavigation(
        SlideShowController controller,
        IReadOnlyList<Slide> slides,
        int targetSlideIndex,
        bool returnToParent = false,
        int? transitionDurationMs = null,
        bool showBackground = true)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(slides);

        if (slides.Count == 0 || targetSlideIndex < 0)
            return SlideShowHostCommand.HandledNoOp(stopAutoAdvance: true);

        controller.EnterZoomNavigation(
            targetSlideIndex,
            returnToParent,
            transitionDurationMs,
            showBackground);
        var slide = controller.CurrentSlide;
        return slide is null
            ? SlideShowHostCommand.HandledNoOp(stopAutoAdvance: true)
            : SlideShowHostCommand.Navigate(
                slide,
                controller.CurrentSlideIndex,
                animateSlide: controller.ShowWithAnimation && transitionDurationMs is > 0,
                stopAutoAdvance: true,
                transitionDurationMs: transitionDurationMs is > 0
                    ? transitionDurationMs
                    : null,
                useDestinationBackground: showBackground);
    }

    public static SlideShowHostCommand PlanInternalSlideJump(
        SlideShowController controller,
        IReadOnlyList<Slide> slides,
        string? targetSlideId)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(slides);

        if (string.IsNullOrEmpty(targetSlideId))
        {
            return SlideShowHostCommand.HandledNoOp();
        }

        var targetIndex = FindSlideIndex(slides, targetSlideId);
        if (targetIndex < 0)
        {
            return SlideShowHostCommand.HandledNoOp();
        }

        return PlanJump(controller, slides, targetIndex, animateSlide: false, stopAutoAdvance: true);
    }

    public static SlideShowHostCommand PlanSlideNumberJump(
        SlideShowController controller,
        IReadOnlyList<Slide> slides,
        int oneBasedSlideNumber,
        IReadOnlyList<int>? sourceSlideIndices = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(slides);

        if (oneBasedSlideNumber <= 0)
            return SlideShowHostCommand.HandledNoOp(stopAutoAdvance: true);

        var targetIndex = oneBasedSlideNumber - 1;
        if (sourceSlideIndices is not null)
        {
            if (sourceSlideIndices.Count != slides.Count)
                return SlideShowHostCommand.HandledNoOp(stopAutoAdvance: true);

            targetIndex = -1;
            var sourceSlideIndex = oneBasedSlideNumber - 1;
            for (var routeIndex = 0; routeIndex < sourceSlideIndices.Count; routeIndex++)
            {
                if (sourceSlideIndices[routeIndex] == sourceSlideIndex)
                {
                    targetIndex = routeIndex;
                    break;
                }
            }
            if (targetIndex < 0)
                return SlideShowHostCommand.HandledNoOp(stopAutoAdvance: true);
        }
        else if (targetIndex >= slides.Count)
        {
            return SlideShowHostCommand.HandledNoOp(stopAutoAdvance: true);
        }

        return PlanJump(
            controller,
            slides,
            targetIndex,
            animateSlide: false,
            stopAutoAdvance: true);
    }

    public static SlideShowHostDisplayPlan BuildDisplayPlan(
        Presentation presentation,
        SlideShowController controller,
        bool animated,
        int? zoomTransitionDurationMs = null,
        bool zoomShowBackground = true)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(controller);

        var metrics = BuildSlideMetrics(presentation.SlideSizeCxEmu, presentation.SlideSizeCyEmu);
        var slide = controller.CurrentSlide;
        if (slide is null)
        {
            return new SlideShowHostDisplayPlan(null, metrics, null, null, true);
        }

        var transition = animated && slide.Transition is { Kind: not TransitionKind.None }
            ? slide.Transition
            : null;
        if (animated && zoomTransitionDurationMs is > 0)
        {
            transition = new SlideTransition
            {
                Kind = TransitionKind.Zoom,
                DurationMs = Math.Max(
                    SlideShowPlaybackPlanner.MinTransitionDurationMs,
                    zoomTransitionDurationMs.Value),
                AdvanceAfterMs = slide.Transition?.AdvanceAfterMs,
            };
        }
        int? autoAdvanceAfterMs = presentation.UseSlideTimings &&
            slide.Transition?.AdvanceAfterMs is int advMs && advMs > 0
            ? advMs
            : null;

        var useDestinationBackground = animated
            && zoomTransitionDurationMs is > 0
            ? zoomShowBackground
            : true;
        return new SlideShowHostDisplayPlan(
            slide,
            metrics,
            transition,
            autoAdvanceAfterMs,
            useDestinationBackground);
    }

    public static SlideShowHostState BuildState(
        SlideShowController controller,
        int slideCount)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var hasSlides = slideCount > 0 && controller.CurrentSlideIndex >= 0;
        var currentIndex = hasSlides
            ? Math.Clamp(controller.CurrentSlideIndex, 0, slideCount - 1)
            : -1;

        return new SlideShowHostState(
            slideCount,
            currentIndex,
            hasSlides,
            hasSlides && currentIndex == 0,
            hasSlides && currentIndex == slideCount - 1,
            controller.HasPendingSteps,
            FormatStatusText(currentIndex, slideCount));
    }

    public static SlideShowPresenterState BuildPresenterState(
        Presentation presentation,
        SlideShowController controller,
        DateTimeOffset startedAtUtc,
        DateTimeOffset nowUtc,
        SlideShowPresenterDisplayIntent? displayIntent = null,
        SlideShowPresenterToolPlan? toolPlan = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        return BuildPresenterState(
            presentation,
            controller,
            presentation.Slides,
            startedAtUtc,
            nowUtc,
            displayIntent,
            toolPlan);
    }

    public static SlideShowPresenterState BuildPresenterState(
        Presentation presentation,
        SlideShowController controller,
        IReadOnlyList<Slide> slides,
        DateTimeOffset startedAtUtc,
        DateTimeOffset nowUtc,
        SlideShowPresenterDisplayIntent? displayIntent = null,
        SlideShowPresenterToolPlan? toolPlan = null,
        IReadOnlyList<int>? sourceSlideIndices = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(slides);

        if (sourceSlideIndices is not null && sourceSlideIndices.Count != slides.Count)
        {
            throw new ArgumentException(
                "The presenter slide list and source slide index list must have the same count.",
                nameof(sourceSlideIndices));
        }

        var hostState = BuildState(controller, slides.Count);
        var currentSlide = hostState.HasSlides
            ? BuildPresenterSlideState(slides, hostState.CurrentSlideIndex, sourceSlideIndices)
            : null;
        var nextSlide = hostState.HasSlides
            ? BuildPresenterSlideState(slides, hostState.CurrentSlideIndex + 1, sourceSlideIndices)
            : null;
        var elapsed = nowUtc >= startedAtUtc
            ? nowUtc - startedAtUtc
            : TimeSpan.Zero;

        return new SlideShowPresenterState(
            hostState,
            currentSlide,
            nextSlide,
            InCanvasTextEditPlanner.ExtractPlainText(currentSlide?.Slide.Notes),
            startedAtUtc,
            elapsed,
            displayIntent ?? SlideShowPresenterDisplayIntent.FullScreen,
            toolPlan ?? SlideShowPresenterToolPlanner.BuildPlan(
                inkColorHex: presentation.PresenterPenColor?.Resolved.ToString()));
    }

    public static string FormatStatusText(int currentSlideIndex, int slideCount) =>
        slideCount <= 0 || currentSlideIndex < 0
            ? NoSlidesStatusText
            : $"Slide {Math.Clamp(currentSlideIndex, 0, slideCount - 1) + 1} of {slideCount}";

    public static SlideShowSlideMetrics BuildSlideMetrics(long widthEmu, long heightEmu)
    {
        var widthDip = widthEmu > 0 ? widthEmu / EmusPerDip : SlideShowSlideMetrics.Default.WidthDip;
        var heightDip = heightEmu > 0 ? heightEmu / EmusPerDip : SlideShowSlideMetrics.Default.HeightDip;
        return new SlideShowSlideMetrics(widthDip, heightDip);
    }

    public static SlideShowPoint MapCanvasPointToSlide(
        double canvasX,
        double canvasY,
        double canvasWidth,
        double canvasHeight,
        SlideShowSlideMetrics metrics)
    {
        var effectiveWidth = canvasWidth > 0 ? canvasWidth : metrics.WidthDip;
        var effectiveHeight = canvasHeight > 0 ? canvasHeight : metrics.HeightDip;
        return new SlideShowPoint(
            canvasX * (metrics.WidthDip / effectiveWidth),
            canvasY * (metrics.HeightDip / effectiveHeight));
    }

    public static Hyperlink? HitTestHyperlink(Slide slide, SlideShowPoint slidePoint)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(slidePoint);

        return HitTestHyperlinkInShapes(slide.Shapes, slidePoint);
    }

    public static uint? HitTestTriggerShape(Slide slide, SlideShowPoint slidePoint)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(slidePoint);

        var triggerShapeIds = slide.Animations
            .Where(a => a.TriggerShapeId is not null)
            .Select(a => a.TriggerShapeId!.Value)
            .Distinct();

        foreach (var shapeId in triggerShapeIds)
        {
            var shape = FindShapeById(slide.Shapes, shapeId);
            if (shape is not null && HitTestShape(shape, slidePoint))
            {
                return shapeId;
            }
        }

        return null;
    }

    private static SlideShape? FindShapeById(
        IReadOnlyList<SlideShape> shapes,
        uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;

            if (shape.Children.Count > 0)
            {
                var child = FindShapeById(shape.Children, shapeId);
                if (child is not null)
                    return child;
            }
        }

        return null;
    }

    private static bool TryGetZoomTargetSlideIndex(
        Presentation presentation,
        Slide slide,
        SlideShowPoint slidePoint,
        out int targetSlideIndex,
        out bool returnToParent,
        out int? transitionDurationMs,
        out bool showBackground)
    {
        foreach (var shape in slide.Shapes)
        {
            if (!HitTestShape(shape, slidePoint))
                continue;

            if (shape.Kind == SlideShapeKind.Zoom &&
                ZoomNavigationService.TryGetTargetSlideIndex(
                    presentation,
                    shape.PreservedObject,
                    RelativeShapeX(shape, slidePoint),
                    RelativeShapeY(shape, slidePoint),
                    out targetSlideIndex,
                    out returnToParent,
                    out transitionDurationMs,
                    out showBackground))
            {
                return true;
            }

            if (shape.Children.Count > 0 && TryGetZoomTargetSlideIndexInShapes(
                    presentation,
                    shape.Children,
                    slidePoint,
                    out targetSlideIndex,
                    out returnToParent,
                    out transitionDurationMs,
                    out showBackground))
            {
                return true;
            }
        }

        targetSlideIndex = -1;
        returnToParent = false;
        transitionDurationMs = null;
        showBackground = true;
        return false;
    }

    private static bool TryGetZoomTargetSlideIndexInShapes(
        Presentation presentation,
        IReadOnlyList<SlideShape> shapes,
        SlideShowPoint slidePoint,
        out int targetSlideIndex,
        out bool returnToParent,
        out int? transitionDurationMs,
        out bool showBackground)
    {
        foreach (var shape in shapes)
        {
            if (!HitTestShape(shape, slidePoint))
                continue;

            if (shape.Kind == SlideShapeKind.Zoom &&
                ZoomNavigationService.TryGetTargetSlideIndex(
                    presentation,
                    shape.PreservedObject,
                    RelativeShapeX(shape, slidePoint),
                    RelativeShapeY(shape, slidePoint),
                    out targetSlideIndex,
                    out returnToParent,
                    out transitionDurationMs,
                    out showBackground))
            {
                return true;
            }

            if (shape.Children.Count > 0 && TryGetZoomTargetSlideIndexInShapes(
                    presentation,
                    shape.Children,
                    slidePoint,
                    out targetSlideIndex,
                    out returnToParent,
                    out transitionDurationMs,
                    out showBackground))
            {
                return true;
            }
        }

        targetSlideIndex = -1;
        returnToParent = false;
        transitionDurationMs = null;
        showBackground = true;
        return false;
    }

    private static SlideShowHostCommand PlanJump(
        SlideShowController controller,
        IReadOnlyList<Slide> slides,
        int slideIndex,
        bool animateSlide,
        bool stopAutoAdvance)
    {
        if (slides.Count == 0 || slideIndex < 0)
        {
            return SlideShowHostCommand.HandledNoOp(stopAutoAdvance);
        }

        controller.ClearZoomReturnPath();
        controller.GoToSlide(slideIndex);
        var slide = controller.CurrentSlide;
        return slide is null
            ? SlideShowHostCommand.HandledNoOp(stopAutoAdvance)
            : SlideShowHostCommand.Navigate(
                slide,
                controller.CurrentSlideIndex,
                animateSlide,
                stopAutoAdvance);
    }

    private static int FindSlideIndex(IReadOnlyList<Slide> slides, string targetSlideId)
    {
        for (var i = 0; i < slides.Count; i++)
        {
            if (slides[i].Id == targetSlideId)
            {
                return i;
            }
        }

        return -1;
    }

    private static SlideShowPresenterSlideState? BuildPresenterSlideState(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        IReadOnlyList<int>? sourceSlideIndices)
    {
        if (slideIndex < 0 || slideIndex >= slides.Count)
        {
            return null;
        }

        var slide = slides[slideIndex];
        return new SlideShowPresenterSlideState(
            slideIndex,
            sourceSlideIndices?[slideIndex] ?? slideIndex,
            slide.Id,
            slide.Title,
            slide);
    }

    private static Hyperlink? HitTestHyperlinkInShapes(
        IReadOnlyList<SlideShape> shapes,
        SlideShowPoint slidePoint)
    {
        foreach (var shape in shapes)
        {
            if (!HitTestShape(shape, slidePoint))
            {
                continue;
            }

            if (shape.Hyperlink is not null)
            {
                return shape.Hyperlink;
            }

            if (shape.Children.Count > 0)
            {
                var groupResult = HitTestHyperlinkInShapes(shape.Children, slidePoint);
                if (groupResult is not null)
                {
                    return groupResult;
                }
            }

            if (shape.TextBody is null)
            {
                continue;
            }

            var textResult = SlideShowTextHyperlinkHitTestPlanner.HitTest(shape, slidePoint);
            if (textResult is not null)
            {
                return textResult;
            }
        }

        return null;
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;

            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    private static bool HitTestShape(SlideShape shape, SlideShowPoint slidePoint)
    {
        var shapeX = shape.OffsetXEmu / EmusPerDip;
        var shapeY = shape.OffsetYEmu / EmusPerDip;
        var shapeWidth = shape.ExtentCxEmu / EmusPerDip;
        var shapeHeight = shape.ExtentCyEmu / EmusPerDip;

        return slidePoint.X >= shapeX
            && slidePoint.X <= shapeX + shapeWidth
            && slidePoint.Y >= shapeY
            && slidePoint.Y <= shapeY + shapeHeight;
    }

    private static double RelativeShapeX(SlideShape shape, SlideShowPoint point)
    {
        var width = shape.ExtentCxEmu / EmusPerDip;
        return width <= 0 ? 0 : (point.X - shape.OffsetXEmu / EmusPerDip) / width;
    }

    private static double RelativeShapeY(SlideShape shape, SlideShowPoint point)
    {
        var height = shape.ExtentCyEmu / EmusPerDip;
        return height <= 0 ? 0 : (point.Y - shape.OffsetYEmu / EmusPerDip) / height;
    }
}
