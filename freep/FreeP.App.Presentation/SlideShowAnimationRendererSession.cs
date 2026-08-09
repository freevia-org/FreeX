using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowAnimationParagraphRangeOverlayPlan(
    ShapeAnimation Animation,
    SlideShape Shape,
    double InitialOpacity);

public sealed record SlideShowAnimationOverlayShapePlan(
    uint ShapeId,
    SlideShape PrimaryShape,
    double InitialOpacity,
    bool SuppressBaseShape,
    SlideShape? ParagraphBackgroundShape,
    IReadOnlyList<SlideShape> ParagraphShapes,
    IReadOnlyList<SlideShowAnimationParagraphRangeOverlayPlan> ParagraphRangeShapes,
    SlideShape? FillMaskShape,
    SlideShape? LineColorShape,
    SlideShape? FontStyleShape,
    SlideShape? FontSizeShape)
{
    public bool IsParagraphBuild => ParagraphShapes.Count > 0;
    public bool IsParagraphRangeBuild => ParagraphRangeShapes.Count > 0;
}

public sealed record SlideShowAnimationOverlayPlan(
    IReadOnlyList<SlideShowAnimationOverlayShapePlan> Shapes)
{
    public static SlideShowAnimationOverlayPlan Empty { get; } = new(
        Array.Empty<SlideShowAnimationOverlayShapePlan>());
}

public enum SlideShowAnimationPlaybackTargetKind
{
    Primary,
    Paragraph,
    ParagraphRange,
    Fill,
    Line,
    FontStyle,
    FontSize,
    Fallback
}

public sealed record SlideShowAnimationPlaybackTargetAvailability(
    IReadOnlySet<uint> PrimaryShapeIds,
    IReadOnlyDictionary<uint, int> ParagraphCounts,
    IReadOnlySet<uint> FillShapeIds,
    IReadOnlySet<uint> LineShapeIds,
    IReadOnlySet<uint> FontStyleShapeIds,
    IReadOnlySet<uint> FontSizeShapeIds,
    IReadOnlySet<ShapeAnimation>? ParagraphRangeAnimations = null);

public sealed record SlideShowAnimationPlaybackOperation(
    SlideShowAnimationPlaybackTargetKind TargetKind,
    uint ShapeId,
    int TargetIndex,
    SlideShowShapeAnimationPlaybackPlan Playback,
    bool SuppressBaseBeforePlayback,
    bool RevealBaseUsingPlaybackTiming,
    SlideShowAnimationFallbackVisibilityPlan? FallbackVisibility = null,
    SlideShowFallbackAnimationPlaybackPlan? FallbackAnimation = null)
{
    public bool IsFallback => TargetKind == SlideShowAnimationPlaybackTargetKind.Fallback;
}

public sealed record SlideShowAnimationStepRendererPlan(
    IReadOnlyList<SlideShowAnimationPlaybackOperation> Operations,
    IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> Checkpoints,
    SlideShowAnimationStepPlaybackReadinessPlan Readiness);

public sealed record SlideShowAnimationRepeatPlan(
    int? PassCount,
    bool RepeatIndefinitely,
    bool AutoReverse)
{
    public bool HasMultiplePasses => RepeatIndefinitely || PassCount is > 1;
}

public sealed record SlideShowAnimationDoubleKeyFrame(double Value, double Progress);

public sealed record SlideShowAnimationColorKeyFrame(SrgbColor Value, double Progress);

public sealed record SlideShowAnimationColorTrackPlan(
    IReadOnlyList<SlideShowAnimationColorKeyFrame> Colors,
    IReadOnlyList<SlideShowAnimationDoubleKeyFrame> Opacities);

/// <summary>
/// Owns renderer-neutral slideshow animation overlay state and playback routing.
/// Native hosts materialize the planned shape variants and execute framework animations.
/// </summary>
public sealed class SlideShowAnimationRendererSession
{
    private readonly Presentation _presentation;

    public SlideShowAnimationRendererSession(Presentation presentation) =>
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));

    public SlideShowAnimationOverlayPlan OverlayPlan { get; private set; } =
        SlideShowAnimationOverlayPlan.Empty;

    public SlideShowShapeAnimationVisualFramePlan? LastFrame { get; private set; }

    public SlideShowAnimationStepRendererPlan? LastStep { get; private set; }

    public SlideShowAnimationOverlayPlan PlanOverlay(Slide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);
        OverlayPlan = SlideShowAnimationOverlayPlanner.Build(_presentation, slide);
        LastFrame = null;
        LastStep = null;
        return OverlayPlan;
    }

    public SlideShowAnimationStepRendererPlan PlanStep(
        AnimationStep step,
        int slideIndex,
        double slideWidthDip,
        double slideHeightDip,
        SlideShowAnimationPlaybackTargetAvailability targets,
        IReadOnlyDictionary<string, string>? effectiveColorMap = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(targets);

        var operations = SlideShowAnimationStepRendererPlanner.BuildOperations(
            step,
            _presentation,
            targets,
            effectiveColorMap);
        var checkpoints = SlideShowPlaybackFramePlanner.PlanAnimationStepCheckpoints(
            step,
            slideWidthDip,
            slideHeightDip);
        var readiness = SlideShowPlaybackFramePlanner.BuildAnimationStepPlaybackReadinessPlan(
            step,
            slideIndex,
            stepIndex: 0,
            slideWidthDip,
            slideHeightDip);
        LastStep = new SlideShowAnimationStepRendererPlan(operations, checkpoints, readiness);
        return LastStep;
    }

    public SlideShowShapeAnimationVisualFramePlan PlanFrame(
        SlideShowShapeAnimationPlaybackPlan plan,
        int elapsedMs,
        double slideWidthDip,
        double slideHeightDip)
    {
        LastFrame = SlideShowPlaybackFramePlanner.PlanFrame(
            plan,
            elapsedMs,
            slideWidthDip,
            slideHeightDip);
        return LastFrame;
    }

    public SlideShowAnimationEffectTrackPlan PlanEffectTracks(
        SlideShowShapeAnimationPlaybackPlan plan) =>
        SlideShowAnimationEffectTrackPlanner.Build(plan)
        ?? throw new ArgumentException(
            $"{plan.EffectKind} does not use portable scalar effect tracks.",
            nameof(plan));

    public SlideShowShapeAnimationPlaybackPlan PlanRepeatPass(
        SlideShowShapeAnimationPlaybackPlan plan,
        int passIndex,
        IReadOnlyDictionary<string, string>? effectiveColorMap = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var pass = passIndex == 0 ? plan : plan with { DelayMs = 0 };
        if (passIndex % 2 == 0 || !plan.AutoReverse)
        {
            return pass with
            {
                RepeatCount = null,
                RepeatIndefinitely = false,
                AutoReverse = false
            };
        }

        var reversedAnimation = PresentationAnimationCommandPlanner.CloneAnimation(plan.Animation);
        reversedAnimation.Kind = reversedAnimation.Kind switch
        {
            AnimationKind.Entrance => AnimationKind.Exit,
            AnimationKind.Exit => AnimationKind.Entrance,
            _ => AnimationKind.Emphasis
        };
        var reverse = SlideShowPlaybackPlanner.PlanShapeAnimation(
            reversedAnimation,
            0,
            _presentation,
            effectiveColorMap);
        return reverse with
        {
            RepeatCount = null,
            RepeatIndefinitely = false,
            AutoReverse = false,
            FromOpacity = plan.ToOpacity,
            ToOpacity = plan.FromOpacity,
            FromScale = plan.ToScale,
            ToScale = plan.FromScale,
            FromScaleX = plan.ToScaleX,
            FromScaleY = plan.ToScaleY,
            ToScaleX = plan.FromScaleX,
            ToScaleY = plan.FromScaleY,
            OffsetXFactor = -plan.OffsetXFactor,
            OffsetYFactor = -plan.OffsetYFactor,
            MotionKeyFrames = SlideShowPlaybackPlanner.ReverseMotionPathKeyFrames(plan.MotionKeyFrames)
        };
    }
}

public static class SlideShowAnimationOverlayPlanner
{
    public static SlideShowAnimationOverlayPlan Build(Presentation presentation, Slide slide)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slide);

        var entranceShapeIds = slide.Animations
            .Where(animation =>
                (animation.Kind is AnimationKind.Entrance or AnimationKind.Motion)
                && animation.TriggerShapeId is null)
            .Select(animation => animation.ShapeId)
            .ToHashSet();
        var animatedShapeIds = slide.Animations
            .Where(animation => animation.Kind is AnimationKind.Entrance
                or AnimationKind.Motion
                or AnimationKind.Emphasis
                or AnimationKind.Exit)
            .Select(animation => animation.ShapeId)
            .Distinct()
            .ToArray();
        var plans = new List<SlideShowAnimationOverlayShapePlan>(animatedShapeIds.Length);

        foreach (var shapeId in animatedShapeIds)
        {
            var shape = SlideShapeTraversal.FindById(slide, shapeId);
            if (shape is null)
            {
                continue;
            }

            var rangedAnimations = slide.Animations
                .Where(animation => animation.ShapeId == shapeId && animation.ParagraphRangeStart.HasValue)
                .ToArray();
            var useParagraphRanges = rangedAnimations.Length > 0
                && SlideShowAnimationBuildPlanner.ParagraphRangesCoverWholeShape(shape, rangedAnimations);
            var paragraphRangeShapes = useParagraphRanges
                ? rangedAnimations
                    .Select(animation =>
                    {
                        var start = animation.ParagraphRangeStart!.Value;
                        var rangeShape = SlideShowAnimationBuildPlanner.CreateParagraphRangeShape(
                            shape,
                            start,
                            animation.ParagraphRangeEnd ?? start);
                        return rangeShape is null
                            ? null
                            : new SlideShowAnimationParagraphRangeOverlayPlan(
                                animation,
                                rangeShape,
                                entranceShapeIds.Contains(shapeId) ? 0 : 1);
                    })
                    .Where(plan => plan is not null)
                    .Select(plan => plan!)
                    .ToArray()
                : Array.Empty<SlideShowAnimationParagraphRangeOverlayPlan>();
            var paragraphShapes = SlideShowAnimationBuildPlanner.IsParagraphBuild(slide, shapeId)
                ? SlideShowAnimationBuildPlanner.CreateParagraphShapes(shape)
                : Array.Empty<SlideShape>();
            SlideShape? paragraphBackground = null;
            if (paragraphRangeShapes.Length > 0 || paragraphShapes.Count > 0)
            {
                paragraphBackground = SlideCloner.CloneShape(shape);
                paragraphBackground.TextBody = null;
            }

            plans.Add(new SlideShowAnimationOverlayShapePlan(
                shapeId,
                shape,
                entranceShapeIds.Contains(shapeId) ? 0 : 1,
                SuppressBaseShape: slide.Animations.Any(animation =>
                    animation.ShapeId == shapeId
                    && animation.Kind is AnimationKind.Entrance or AnimationKind.Motion),
                paragraphBackground,
                paragraphShapes,
                paragraphRangeShapes,
                BuildFillMaskShape(slide, shape),
                BuildLineColorShape(presentation, slide, shape),
                BuildFontStyleShape(slide, shape),
                BuildFontSizeShape(slide, shape)));
        }

        return plans.Count == 0
            ? SlideShowAnimationOverlayPlan.Empty
            : new SlideShowAnimationOverlayPlan(plans);
    }

    public static bool TryParseColor(string? value, out SrgbColor color)
    {
        if (DrawingMlRgbColor.TryParseHexRgb(value, out var rgb))
        {
            color = new SrgbColor(rgb.R, rgb.G, rgb.B);
            return true;
        }

        color = SrgbColor.Black;
        return false;
    }

    private static SlideShape? BuildFillMaskShape(Slide slide, SlideShape shape)
    {
        if (shape.Fill is ShapeFill.None
            || !slide.Animations.Any(animation =>
                animation.ShapeId == shape.Id
                && animation.Preset == AnimationPreset.ChangeFillColor))
        {
            return null;
        }

        var fillMask = SlideCloner.CloneShape(shape);
        fillMask.TextBody = null;
        fillMask.Outline = null;
        return fillMask;
    }

    private static SlideShape? BuildLineColorShape(
        Presentation presentation,
        Slide slide,
        SlideShape shape)
    {
        var animation = slide.Animations.FirstOrDefault(candidate =>
            candidate.ShapeId == shape.Id
            && candidate.Preset == AnimationPreset.ChangeLineColor);
        if (animation is null
            || shape.TextBody is not null
            || shape.Outline is not ShapeOutline.Visible outline)
        {
            return null;
        }

        var playback = SlideShowPlaybackPlanner.PlanShapeAnimation(
            animation,
            startDelayMs: 0,
            presentation,
            slide.ColorMapOverride);
        if (!TryParseColor(playback.ColorToHex, out var lineColor))
        {
            return null;
        }

        var lineShape = SlideCloner.CloneShape(shape);
        lineShape.Outline = new ShapeOutline.Visible(
            lineColor,
            outline.WidthPt,
            outline.Dash,
            outline.BeginLineEnd,
            outline.EndLineEnd);
        return lineShape;
    }

    private static SlideShape? BuildFontStyleShape(Slide slide, SlideShape shape)
    {
        var animation = slide.Animations.FirstOrDefault(candidate =>
            candidate.ShapeId == shape.Id
            && candidate.Preset is AnimationPreset.ChangeFontStyle
                or AnimationPreset.Bold
                or AnimationPreset.Underline);
        var runs = shape.TextBody?.Paragraphs.SelectMany(paragraph => paragraph.Runs).ToArray();
        if (animation is null || runs is not { Length: > 0 })
        {
            return null;
        }

        var style = SlideShowPlaybackPlanner.ResolveFontStyleBehavior(animation);
        if (style.Italic is null && style.Bold is null && style.Underline is null)
        {
            return null;
        }

        var fontStyleShape = SlideCloner.CloneShape(shape);
        foreach (var run in fontStyleShape.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs))
        {
            if (style.Italic is bool italic)
            {
                run.Italic = italic;
            }
            if (style.Bold is bool bold)
            {
                run.Bold = bold;
            }
            if (style.Underline is bool underline)
            {
                run.Underline = underline;
            }
        }

        return fontStyleShape;
    }

    private static SlideShape? BuildFontSizeShape(Slide slide, SlideShape shape)
    {
        var animation = slide.Animations.FirstOrDefault(candidate =>
            candidate.ShapeId == shape.Id
            && candidate.Preset is AnimationPreset.Grow or AnimationPreset.Shrink
            && SlideShowPlaybackPlanner.ResolveFontSizeBehavior(candidate) is not null);
        var runs = shape.TextBody?.Paragraphs.SelectMany(paragraph => paragraph.Runs).ToArray();
        var size = animation is null
            ? null
            : SlideShowPlaybackPlanner.ResolveFontSizeBehavior(animation);
        if (animation is null
            || size is null
            || runs is not { Length: > 0 }
            || runs.Any(run => run.FontSizePt is not > 0))
        {
            return null;
        }

        var fontSizeShape = SlideCloner.CloneShape(shape);
        foreach (var run in fontSizeShape.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs))
        {
            run.FontSizePt = run.FontSizePt!.Value * size.Multiplier;
        }

        return fontSizeShape;
    }
}

public static class SlideShowAnimationStepRendererPlanner
{
    public static IReadOnlyList<SlideShowAnimationPlaybackOperation> BuildOperations(
        AnimationStep step,
        Presentation presentation,
        SlideShowAnimationPlaybackTargetAvailability targets,
        IReadOnlyDictionary<string, string>? effectiveColorMap = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(targets);

        var operations = new List<SlideShowAnimationPlaybackOperation>();
        foreach (var playback in SlideShowPlaybackPlanner.PlanAnimationStep(
            step,
            presentation,
            effectiveColorMap))
        {
            var shapeId = playback.Animation.ShapeId;
            if (targets.ParagraphRangeAnimations?.Contains(playback.Animation) == true)
            {
                operations.Add(BuildOperation(
                    SlideShowAnimationPlaybackTargetKind.ParagraphRange,
                    playback));
                continue;
            }

            if (targets.ParagraphCounts.TryGetValue(shapeId, out var paragraphCount)
                && paragraphCount > 0)
            {
                for (var index = 0; index < paragraphCount; index++)
                {
                    operations.Add(BuildOperation(
                        SlideShowAnimationPlaybackTargetKind.Paragraph,
                        playback with { DelayMs = playback.DelayMs + index * playback.DurationMs },
                        index));
                }
                continue;
            }

            if (!targets.PrimaryShapeIds.Contains(shapeId))
            {
                operations.Add(new SlideShowAnimationPlaybackOperation(
                    SlideShowAnimationPlaybackTargetKind.Fallback,
                    shapeId,
                    TargetIndex: 0,
                    playback,
                    SuppressBaseBeforePlayback: false,
                    RevealBaseUsingPlaybackTiming: false,
                    SlideShowPlaybackPlanner.PlanFallbackVisibility(playback.Animation),
                    SlideShowPlaybackPlanner.PlanFallbackAnimation(
                        playback.Animation,
                        playback.DelayMs)));
                continue;
            }

            if (playback.EffectKind == SlideShowShapeAnimationEffectKind.ChangeFillColor
                && targets.FillShapeIds.Contains(shapeId))
            {
                operations.Add(BuildOperation(SlideShowAnimationPlaybackTargetKind.Fill, playback));
                continue;
            }
            if (playback.EffectKind == SlideShowShapeAnimationEffectKind.ChangeLineColor
                && targets.LineShapeIds.Contains(shapeId))
            {
                operations.Add(BuildOperation(SlideShowAnimationPlaybackTargetKind.Line, playback));
                continue;
            }
            if ((playback.EffectKind is SlideShowShapeAnimationEffectKind.ChangeFontStyle
                    or SlideShowShapeAnimationEffectKind.Bold
                    or SlideShowShapeAnimationEffectKind.Underline)
                && targets.FontStyleShapeIds.Contains(shapeId))
            {
                operations.Add(BuildOperation(SlideShowAnimationPlaybackTargetKind.FontStyle, playback));
                continue;
            }
            if (playback.EffectKind == SlideShowShapeAnimationEffectKind.ChangeFontSize)
            {
                operations.Add(targets.FontSizeShapeIds.Contains(shapeId)
                    ? BuildOperation(SlideShowAnimationPlaybackTargetKind.FontSize, playback)
                    : BuildOperation(
                        SlideShowAnimationPlaybackTargetKind.Primary,
                        playback with { EffectKind = SlideShowShapeAnimationEffectKind.GrowShrink }));
                continue;
            }

            operations.Add(BuildOperation(
                SlideShowAnimationPlaybackTargetKind.Primary,
                playback,
                suppressBase: playback.Animation.Kind is AnimationKind.Entrance
                    or AnimationKind.Motion
                    or AnimationKind.Exit,
                revealBase: playback.Animation.Kind is AnimationKind.Entrance or AnimationKind.Motion));
        }

        return operations;
    }

    public static SlideShowAnimationRepeatPlan BuildRepeatPlan(
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new SlideShowAnimationRepeatPlan(
            plan.RepeatIndefinitely ? null : Math.Max(1, plan.RepeatCount ?? 1),
            plan.RepeatIndefinitely,
            plan.AutoReverse);
    }

    private static SlideShowAnimationPlaybackOperation BuildOperation(
        SlideShowAnimationPlaybackTargetKind targetKind,
        SlideShowShapeAnimationPlaybackPlan playback,
        int targetIndex = 0,
        bool suppressBase = false,
        bool revealBase = false) =>
        new(
            targetKind,
            playback.Animation.ShapeId,
            targetIndex,
            playback,
            suppressBase,
            revealBase);
}

public static class SlideShowAnimationColorTrackPlanner
{
    public static SlideShowAnimationColorTrackPlan? BuildAuthoredColorOverlay(
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!SlideShowAnimationOverlayPlanner.TryParseColor(plan.ColorFromHex, out var from)
            || !SlideShowAnimationOverlayPlanner.TryParseColor(plan.ColorToHex, out var to))
        {
            return null;
        }

        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.ColorWave)
        {
            return new SlideShowAnimationColorTrackPlan(
                [
                    new(from, 0),
                    new(to, 0.25),
                    new(from, 0.5),
                    new(to, 0.75),
                    new(from, 1)
                ],
                [
                    new(0, 0),
                    new(0.65, 0.25),
                    new(0, 0.5),
                    new(0.65, 0.75),
                    new(0, 1)
                ]);
        }

        var persistent = plan.EffectKind == SlideShowShapeAnimationEffectKind.ChangeColor;
        return new SlideShowAnimationColorTrackPlan(
            [new(from, 0), new(to, 0.5), new(persistent ? to : from, 1)],
            [new(0, 0), new(0.65, 0.5), new(persistent ? 0.65 : 0, 1)]);
    }

    public static SlideShowAnimationColorTrackPlan? BuildFillColor(
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return SlideShowAnimationOverlayPlanner.TryParseColor(plan.ColorFromHex, out var from)
            && SlideShowAnimationOverlayPlanner.TryParseColor(plan.ColorToHex, out var to)
            ? new SlideShowAnimationColorTrackPlan(
                [new(from, 0), new(to, 1)],
                [new(0, 0), new(1, 1)])
            : null;
    }
}
