using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowTransitionPlaybackActionKind
{
    ShowInstant,
    Fade,
    Flash,
    Dissolve,
    Box,
    Reveal,
    Uncover,
    Cover,
    Split,
    Blinds,
    RandomBars,
    Strips,
    Wheel,
    Zoom,
    Pan,
    Gallery,
    Conveyor,
    Window,
    Morph,
    Flip,
    Cube,
    Rotate,
    Honeycomb,
    Switch,
    Orbit,
    Ferris,
    Flythrough,
    Glitter,
    Ripple,
    Wind,
    Curtains,
    Shred,
    Drape,
    Fracture,
    Crush,
    Prism,
    Prestige,
    Warp,
    Vortex,
    PageCurl,
    Push
}

public sealed record SlideShowTransitionPlaybackPlan(
    SlideShowTransitionPlaybackActionKind ActionKind,
    int DurationMs,
    double IncomingOffsetX,
    double IncomingOffsetY,
    SlideShowTransitionPlaybackKind SourceKind,
    bool SplitHorizontal,
    bool SplitOut,
    bool BlindsHorizontal,
    bool RandomBarsHorizontal,
    bool StripsSlopeDown,
    int WheelSpokeCount,
    bool WheelReverse,
    bool ZoomIn,
    bool BoxExpandsFromCenter,
    TransitionKind ResolvedKind,
    ulong? RandomSeed,
    SlideTransition EffectiveTransition);

public enum SlideShowShapeAnimationEffectKind
{
    Appear,
    Fade,
    FlyIn,
    Wipe,
    Split,
    RandomBars,
    Blinds,
    Box,
    Checkerboard,
    Circle,
    Diamond,
    Plus,
    Strips,
    Wedge,
    Wheel,
    Dissolve,
    Flash,
    Spiral,
    Swivel,
    Bounce,
    Float,
    Swoop,
    Boomerang,
    Peek,
    Crawl,
    Zoom,
    Pulse,
    GrowShrink,
    Spin,
    Teeter,
    Blink,
    FlashBulb,
    Flicker,
    ChangeLineColor,
    ColorPulse,
    ColorWave,
    ChangeColor,
    ChangeFontStyle,
    ChangeFillColor,
    GrowWithColor,
    Wave,
    Shimmer,
    Bold,
    Underline,
    MotionPath
}

public enum SlideShowAnimationRevealTiming
{
    None,
    AtStart,
    OnComplete
}

public sealed record SlideShowMotionPathKeyFrame(
    double Progress,
    double OffsetXFactor,
    double OffsetYFactor);

public enum SlideShowGeometricMaskKind
{
    None,
    Circle,
    Diamond,
    Plus,
    Strips,
    Wedge,
    Wheel
}

public sealed record SlideShowShapeAnimationPlaybackPlan(
    ShapeAnimation Animation,
    SlideShowShapeAnimationEffectKind EffectKind,
    int DurationMs,
    int DelayMs,
    SlideShowAnimationRevealTiming RevealTiming,
    double FromOpacity,
    double ToOpacity,
    double FromScale,
    double ToScale,
    double PeakScale,
    double RotationDegrees,
    double OffsetXFactor,
    double OffsetYFactor,
    bool WipeHorizontal,
    bool BlindsHorizontal,
    int BlindsBandCount,
    bool BoxExpandsFromCenter,
    SlideShowGeometricMaskKind GeometricMaskKind,
    bool GeometricMaskExpandsFromCenter,
    int GeometricMaskSpokeCount,
    int GeometricMaskStripCount,
    bool GeometricMaskStripsSlopeDown,
    bool CheckerboardHorizontal,
    int CheckerboardRowCount,
    int CheckerboardColumnCount,
    IReadOnlyList<SlideShowMotionPathKeyFrame> MotionKeyFrames)
{
    // Scalar scale members remain the compatibility surface for effects that have
    // historically been uniform. Grow/Shrink additionally carries the authored
    // X/Y scale trajectory so both hosts can render asymmetric animScale behavior.
    private double? _fromScaleX;
    private double? _fromScaleY;
    private double? _toScaleX;
    private double? _toScaleY;
    private double? _peakScaleX;
    private double? _peakScaleY;
    public double FromScaleX { get => _fromScaleX ?? FromScale; init => _fromScaleX = value; }
    public double FromScaleY { get => _fromScaleY ?? FromScale; init => _fromScaleY = value; }
    public double ToScaleX { get => _toScaleX ?? ToScale; init => _toScaleX = value; }
    public double ToScaleY { get => _toScaleY ?? ToScale; init => _toScaleY = value; }
    public double PeakScaleX { get => _peakScaleX ?? PeakScale; init => _peakScaleX = value; }
    public double PeakScaleY { get => _peakScaleY ?? PeakScale; init => _peakScaleY = value; }
    public bool SplitHorizontal { get; init; }
    public bool SplitFromCenter { get; init; }
    public int? RepeatCount { get; init; }
    public bool RepeatIndefinitely { get; init; }
    public bool AutoReverse { get; init; }
    public int? Acceleration { get; init; }
    public int? Deceleration { get; init; }
    /// <summary>Resolved authored RGB source color for a native color emphasis effect.</summary>
    public string? ColorFromHex { get; init; }
    /// <summary>Resolved authored RGB destination color for a native color emphasis effect.</summary>
    public string? ColorToHex { get; init; }
}

public sealed record SlideShowFallbackAnimationPlaybackPlan(
    int DurationMs,
    int DelayMs,
    double FromOpacity,
    double FlashOpacity);

public sealed record SlideShowFontStylePlaybackPlan(
    bool? Italic,
    bool? Bold,
    bool? Underline);

/// <summary>
/// Logical visibility behavior for an animation whose visual overlay could not be built.
/// Hosts use this shared plan to preserve PowerPoint's step semantics without inventing
/// renderer-specific geometry for an unavailable surface.
/// </summary>
public sealed record SlideShowAnimationFallbackVisibilityPlan(
    bool SuppressAtStart,
    bool SuppressAtCompletion);

public static class SlideShowPlaybackPlanner
{
    public const int MinTransitionDurationMs = 50;
    public const int MinShapeAnimationDurationMs = 50;
    public const int MinFallbackAnimationDurationMs = 100;
    public const int MotionPathFrameCount = 30;
    public const int BlindsBandCount = 8;
    public const int RandomBarsBandCount = 8;
    public const int CheckerboardRowCount = 4;
    public const int CheckerboardColumnCount = 6;
    public const int WheelSpokeCount = 4;
    public const int StripsBandCount = 6;
    public const int DissolveRowCount = 12;
    public const int DissolveColumnCount = 16;
    public const double ZoomInStartScale = 0.65;
    public const double ZoomOutStartScale = 1.35;
    public const double PanStartScale = 1.12;
    public const double GalleryStartScale = 0.78;
    public const double GalleryOutgoingEndScale = 0.88;
    public const double GalleryTravelFactor = 0.55;
    public const double ConveyorStartScale = 0.90;
    public const double ConveyorOutgoingEndScale = 0.90;
    public const double ConveyorTravelFactor = 1.0;
    public const double ConveyorCrossAxisFactor = 0.08;
    public const double ConveyorTiltDegrees = 3.0;
    public const double WindowStartScale = 0.92;
    public const double WindowInitialOpenFactor = 0.18;

    public static SlideShowFontStylePlaybackPlan ResolveFontStyleBehavior(ShapeAnimation animation)
    {
        if (string.IsNullOrWhiteSpace(animation.PreservedFontStyleBehaviorXml))
            return new(null, null, null);

        bool? italic = null;
        bool? bold = null;
        bool? underline = null;

        try
        {
            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
            var root = XElement.Parse(animation.PreservedFontStyleBehaviorXml, LoadOptions.PreserveWhitespace);
            foreach (var setter in root.Descendants(p + "set"))
            {
                var attrName = setter.Descendants(p + "attrName")
                    .Select(element => element.Value.Trim())
                    .FirstOrDefault();
                var value = setter.Descendants(p + "strVal")
                    .Select(element => element.Attribute("val")?.Value.Trim())
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(attrName) || value is null)
                    continue;

                switch (attrName)
                {
                    case "style.fontStyle":
                        italic = value.Equals("italic", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "style.fontWeight":
                        bold = value.Equals("bold", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "style.textDecorationUnderline":
                        underline = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                            || value == "1";
                        break;
                }
            }
        }
        catch (XmlException)
        {
            return new(null, null, null);
        }

        return new(italic, bold, underline);
    }

    public static SlideShowTransitionPlaybackPlan PlanTransition(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var transitionPlan = SlideShowTransitionPlanner.Plan(transition);
        return BuildTransitionPlaybackPlan(transition, transitionPlan);
    }

    public static SlideShowTransitionPlaybackPlan PlanTransition(
        Presentation presentation,
        Slide slide,
        SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(transition);

        var transitionPlan = SlideShowTransitionPlanner.Plan(presentation, slide, transition);
        return BuildTransitionPlaybackPlan(transition, transitionPlan);
    }

    private static SlideShowTransitionPlaybackPlan BuildTransitionPlaybackPlan(
        SlideTransition transition,
        SlideShowTransitionPlan transitionPlan)
    {
        var actionKind = transitionPlan.PlaybackKind switch
        {
            SlideShowTransitionPlaybackKind.Cut => SlideShowTransitionPlaybackActionKind.ShowInstant,
            SlideShowTransitionPlaybackKind.Flash => SlideShowTransitionPlaybackActionKind.Flash,
            SlideShowTransitionPlaybackKind.Dissolve => SlideShowTransitionPlaybackActionKind.Dissolve,
            SlideShowTransitionPlaybackKind.Box => SlideShowTransitionPlaybackActionKind.Box,
            SlideShowTransitionPlaybackKind.Reveal => SlideShowTransitionPlaybackActionKind.Reveal,
            SlideShowTransitionPlaybackKind.Uncover => SlideShowTransitionPlaybackActionKind.Uncover,
            SlideShowTransitionPlaybackKind.Cover => SlideShowTransitionPlaybackActionKind.Cover,
            SlideShowTransitionPlaybackKind.Push => SlideShowTransitionPlaybackActionKind.Push,
            SlideShowTransitionPlaybackKind.Split => SlideShowTransitionPlaybackActionKind.Split,
            SlideShowTransitionPlaybackKind.Blinds => SlideShowTransitionPlaybackActionKind.Blinds,
            SlideShowTransitionPlaybackKind.RandomBars => SlideShowTransitionPlaybackActionKind.RandomBars,
            SlideShowTransitionPlaybackKind.Strips => SlideShowTransitionPlaybackActionKind.Strips,
            SlideShowTransitionPlaybackKind.Wheel => SlideShowTransitionPlaybackActionKind.Wheel,
            SlideShowTransitionPlaybackKind.Zoom => SlideShowTransitionPlaybackActionKind.Zoom,
            SlideShowTransitionPlaybackKind.Pan => SlideShowTransitionPlaybackActionKind.Pan,
            SlideShowTransitionPlaybackKind.Gallery => SlideShowTransitionPlaybackActionKind.Gallery,
            SlideShowTransitionPlaybackKind.Conveyor => SlideShowTransitionPlaybackActionKind.Conveyor,
            SlideShowTransitionPlaybackKind.Window => SlideShowTransitionPlaybackActionKind.Window,
            SlideShowTransitionPlaybackKind.Morph => SlideShowTransitionPlaybackActionKind.Morph,
            SlideShowTransitionPlaybackKind.Flip => SlideShowTransitionPlaybackActionKind.Flip,
            SlideShowTransitionPlaybackKind.Cube => SlideShowTransitionPlaybackActionKind.Cube,
            SlideShowTransitionPlaybackKind.Rotate => SlideShowTransitionPlaybackActionKind.Rotate,
            SlideShowTransitionPlaybackKind.Honeycomb => SlideShowTransitionPlaybackActionKind.Honeycomb,
            SlideShowTransitionPlaybackKind.Switch => SlideShowTransitionPlaybackActionKind.Switch,
            SlideShowTransitionPlaybackKind.Orbit => SlideShowTransitionPlaybackActionKind.Orbit,
            SlideShowTransitionPlaybackKind.Ferris => SlideShowTransitionPlaybackActionKind.Ferris,
            SlideShowTransitionPlaybackKind.Flythrough => SlideShowTransitionPlaybackActionKind.Flythrough,
            SlideShowTransitionPlaybackKind.Glitter => SlideShowTransitionPlaybackActionKind.Glitter,
            SlideShowTransitionPlaybackKind.Ripple => SlideShowTransitionPlaybackActionKind.Ripple,
            SlideShowTransitionPlaybackKind.Wind => SlideShowTransitionPlaybackActionKind.Wind,
            SlideShowTransitionPlaybackKind.Curtains => SlideShowTransitionPlaybackActionKind.Curtains,
            SlideShowTransitionPlaybackKind.Shred => SlideShowTransitionPlaybackActionKind.Shred,
            SlideShowTransitionPlaybackKind.Drape => SlideShowTransitionPlaybackActionKind.Drape,
            SlideShowTransitionPlaybackKind.Vortex => SlideShowTransitionPlaybackActionKind.Vortex,
            SlideShowTransitionPlaybackKind.Warp => SlideShowTransitionPlaybackActionKind.Warp,
            SlideShowTransitionPlaybackKind.Fracture => SlideShowTransitionPlaybackActionKind.Fracture,
            SlideShowTransitionPlaybackKind.Crush => SlideShowTransitionPlaybackActionKind.Crush,
            SlideShowTransitionPlaybackKind.Prism => SlideShowTransitionPlaybackActionKind.Prism,
            SlideShowTransitionPlaybackKind.Prestige => SlideShowTransitionPlaybackActionKind.Prestige,
            SlideShowTransitionPlaybackKind.PageCurl => SlideShowTransitionPlaybackActionKind.PageCurl,
            SlideShowTransitionPlaybackKind.PushLike => SlideShowTransitionPlaybackActionKind.Cover,
            _ => SlideShowTransitionPlaybackActionKind.Fade
        };

        return new SlideShowTransitionPlaybackPlan(
            actionKind,
            Math.Max(MinTransitionDurationMs, transition.DurationMs),
            transitionPlan.IncomingOffsetX,
            transitionPlan.IncomingOffsetY,
            transitionPlan.PlaybackKind,
            transitionPlan.SplitHorizontal,
            transitionPlan.SplitOut,
            transitionPlan.BlindsHorizontal,
            transitionPlan.RandomBarsHorizontal,
            transitionPlan.StripsSlopeDown,
            transitionPlan.WheelSpokeCount,
            transitionPlan.WheelReverse,
            transitionPlan.ZoomIn,
            transitionPlan.BoxExpandsFromCenter,
            transitionPlan.ResolvedKind,
            transitionPlan.RandomSeed,
            BuildEffectiveTransition(transition, transitionPlan.ResolvedKind));
    }

    private static SlideTransition BuildEffectiveTransition(
        SlideTransition transition,
        TransitionKind resolvedKind)
    {
        if (transition.Kind == resolvedKind)
            return transition;

        return new SlideTransition
        {
            Kind = resolvedKind,
            Direction = transition.Direction,
            SplitOrientation = transition.SplitOrientation,
            DurationMs = transition.DurationMs,
            AdvanceOnClick = transition.AdvanceOnClick,
            AdvanceAfterMs = transition.AdvanceAfterMs,
            RawXml = transition.RawXml,
            MorphOption = transition.MorphOption,
            WheelSpokeCount = transition.WheelSpokeCount,
            Sound = transition.Sound
        };
    }

    public static IReadOnlyList<SlideShowShapeAnimationPlaybackPlan> PlanAnimationStep(
        AnimationStep step,
        Presentation? presentation = null,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        ArgumentNullException.ThrowIfNull(step);

        return step.Entries
            .Select(entry => PlanShapeAnimation(entry.Animation, entry.StartDelayMs, presentation, effectiveClrMap))
            .ToList();
    }

    public static SlideShowShapeAnimationPlaybackPlan PlanShapeAnimation(
        ShapeAnimation animation,
        int startDelayMs,
        Presentation? presentation = null,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        ArgumentNullException.ThrowIfNull(animation);

        var effectKind = ResolveEffectKind(animation);
        var splitDirection = animation.Preset == AnimationPreset.Split
            ? AnimationDirectionSemantics.ResolveSplitDirection(animation)
            : (AnimationDirection?)null;
        var (fromOpacity, toOpacity) = ResolveOpacity(animation);
        var (fromScale, toScale) = ResolveScale(animation);
        var (fromScaleX, fromScaleY, toScaleX, toScaleY, peakScaleX, peakScaleY) =
            ResolveScaleAxesForPlayback(animation, fromScale, toScale, ResolvePeakScale(animation));
        var (offsetX, offsetY) = ResolveFlyInOffset(animation.Direction);
        var (colorFromHex, colorToHex) = ResolveColorBehavior(animation, effectKind, presentation, effectiveClrMap);

        return new SlideShowShapeAnimationPlaybackPlan(
            animation,
            effectKind,
            Math.Max(MinShapeAnimationDurationMs, animation.DurationMs),
            Math.Max(0, startDelayMs),
            ResolveRevealTiming(animation, effectKind),
            fromOpacity,
            toOpacity,
            fromScale,
            toScale,
            ResolvePeakScale(animation),
            RotationDegrees: ResolveRotationDegrees(animation),
            offsetX,
            offsetY,
            IsHorizontalWipe(animation.Direction),
            IsHorizontalBlinds(animation.Direction),
            BlindsBandCount,
            BoxExpandsFromCenter(animation),
            ResolveGeometricMaskKind(animation),
            GeometricMaskExpandsFromCenter(animation),
            ResolveGeometricMaskSpokeCount(animation),
            ResolveGeometricMaskStripCount(animation),
            StripsSlopeDown(animation.Direction),
            IsHorizontalCheckerboard(animation.Direction),
            CheckerboardRowCount,
            CheckerboardColumnCount,
            BuildMotionKeyFrames(animation.Motion))
        {
            RepeatCount = animation.RepeatCount,
            RepeatIndefinitely = animation.RepeatIndefinitely,
            AutoReverse = animation.AutoReverse,
            Acceleration = animation.Acceleration,
            Deceleration = animation.Deceleration,
            ColorFromHex = colorFromHex,
            ColorToHex = colorToHex,
            FromScaleX = fromScaleX,
            FromScaleY = fromScaleY,
            ToScaleX = toScaleX,
            ToScaleY = toScaleY,
            PeakScaleX = peakScaleX,
            PeakScaleY = peakScaleY,
            SplitHorizontal = splitDirection is not null
                && AnimationDirectionSemantics.IsSplitHorizontal(splitDirection.Value),
            SplitFromCenter = splitDirection is not null
                && AnimationDirectionSemantics.IsSplitFromCenter(splitDirection.Value),
        };
    }

    public static IReadOnlyList<SlideShowMotionPathKeyFrame> ReverseMotionPathKeyFrames(
        IReadOnlyList<SlideShowMotionPathKeyFrame> keyFrames)
    {
        ArgumentNullException.ThrowIfNull(keyFrames);

        return keyFrames
            .Reverse()
            .Select(frame => new SlideShowMotionPathKeyFrame(
                1 - frame.Progress,
                frame.OffsetXFactor,
                frame.OffsetYFactor))
            .OrderBy(frame => frame.Progress)
            .ToArray();
    }

    public static SlideShowFallbackAnimationPlaybackPlan? PlanFallbackAnimation(
        ShapeAnimation animation,
        int startDelayMs)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return animation.Kind == AnimationKind.Emphasis
            ? new SlideShowFallbackAnimationPlaybackPlan(
                Math.Max(MinFallbackAnimationDurationMs, animation.DurationMs),
                Math.Max(0, startDelayMs),
                FromOpacity: 1,
                FlashOpacity: 0.5)
            : null;
    }

    public static SlideShowAnimationFallbackVisibilityPlan PlanFallbackVisibility(
        ShapeAnimation animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return animation.Kind switch
        {
            AnimationKind.Entrance or AnimationKind.Motion => new(true, false),
            AnimationKind.Exit => new(false, true),
            _ => new(false, false),
        };
    }

    private static SlideShowShapeAnimationEffectKind ResolveEffectKind(ShapeAnimation animation)
    {
        if (animation.Kind == AnimationKind.Motion && animation.Motion is not null)
        {
            return SlideShowShapeAnimationEffectKind.MotionPath;
        }

        return animation.Preset switch
        {
            AnimationPreset.Appear => SlideShowShapeAnimationEffectKind.Appear,
            AnimationPreset.Fade => SlideShowShapeAnimationEffectKind.Fade,
            AnimationPreset.FlyIn => SlideShowShapeAnimationEffectKind.FlyIn,
            AnimationPreset.Wipe => SlideShowShapeAnimationEffectKind.Wipe,
            AnimationPreset.Split => SlideShowShapeAnimationEffectKind.Split,
            AnimationPreset.RandomBars => SlideShowShapeAnimationEffectKind.RandomBars,
            AnimationPreset.Blinds => SlideShowShapeAnimationEffectKind.Blinds,
            AnimationPreset.Box => SlideShowShapeAnimationEffectKind.Box,
            AnimationPreset.Checkerboard => SlideShowShapeAnimationEffectKind.Checkerboard,
            AnimationPreset.Circle => SlideShowShapeAnimationEffectKind.Circle,
            AnimationPreset.Diamond => SlideShowShapeAnimationEffectKind.Diamond,
            AnimationPreset.Plus => SlideShowShapeAnimationEffectKind.Plus,
            AnimationPreset.Strips => SlideShowShapeAnimationEffectKind.Strips,
            AnimationPreset.Wedge => SlideShowShapeAnimationEffectKind.Wedge,
            AnimationPreset.Wheel => SlideShowShapeAnimationEffectKind.Wheel,
            AnimationPreset.Dissolve => SlideShowShapeAnimationEffectKind.Dissolve,
            AnimationPreset.Flash => SlideShowShapeAnimationEffectKind.Flash,
            AnimationPreset.Spiral => SlideShowShapeAnimationEffectKind.Spiral,
            AnimationPreset.Swivel => SlideShowShapeAnimationEffectKind.Swivel,
            AnimationPreset.Bounce => SlideShowShapeAnimationEffectKind.Bounce,
            AnimationPreset.Float => SlideShowShapeAnimationEffectKind.Float,
            AnimationPreset.Swoop => SlideShowShapeAnimationEffectKind.Swoop,
            AnimationPreset.Boomerang => SlideShowShapeAnimationEffectKind.Boomerang,
            AnimationPreset.Peek => SlideShowShapeAnimationEffectKind.Peek,
            AnimationPreset.Crawl => SlideShowShapeAnimationEffectKind.Crawl,
            AnimationPreset.Zoom => SlideShowShapeAnimationEffectKind.Zoom,
            AnimationPreset.Pulse => SlideShowShapeAnimationEffectKind.Pulse,
            AnimationPreset.Grow or AnimationPreset.Shrink => SlideShowShapeAnimationEffectKind.GrowShrink,
            AnimationPreset.Spin => SlideShowShapeAnimationEffectKind.Spin,
            AnimationPreset.Teeter => SlideShowShapeAnimationEffectKind.Teeter,
            AnimationPreset.Blink => SlideShowShapeAnimationEffectKind.Blink,
            AnimationPreset.FlashBulb => SlideShowShapeAnimationEffectKind.FlashBulb,
            AnimationPreset.Flicker => SlideShowShapeAnimationEffectKind.Flicker,
            AnimationPreset.ColorPulse => SlideShowShapeAnimationEffectKind.ColorPulse,
            AnimationPreset.ColorWave => SlideShowShapeAnimationEffectKind.ColorWave,
            AnimationPreset.ChangeColor => SlideShowShapeAnimationEffectKind.ChangeColor,
            AnimationPreset.ChangeFontStyle => SlideShowShapeAnimationEffectKind.ChangeFontStyle,
            AnimationPreset.ChangeLineColor => SlideShowShapeAnimationEffectKind.ChangeLineColor,
            AnimationPreset.ChangeFillColor => SlideShowShapeAnimationEffectKind.ChangeFillColor,
            AnimationPreset.GrowWithColor => SlideShowShapeAnimationEffectKind.GrowWithColor,
            AnimationPreset.Wave => SlideShowShapeAnimationEffectKind.Wave,
            AnimationPreset.Shimmer => SlideShowShapeAnimationEffectKind.Shimmer,
            AnimationPreset.Bold => SlideShowShapeAnimationEffectKind.Bold,
            AnimationPreset.Underline => SlideShowShapeAnimationEffectKind.Underline,
            _ => SlideShowShapeAnimationEffectKind.Appear
        };
    }

    private static (string? From, string? To) ResolveColorBehavior(
        ShapeAnimation animation,
        SlideShowShapeAnimationEffectKind effectKind,
        Presentation? presentation,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        if (effectKind == SlideShowShapeAnimationEffectKind.ChangeFillColor)
            return ResolveFillColorBehavior(animation, presentation, effectiveClrMap);

        if (effectKind == SlideShowShapeAnimationEffectKind.ChangeLineColor)
        {
            return ResolveColorBehaviorXml(
                animation.PreservedLineBehaviorXml,
                presentation,
                effectiveClrMap);
        }

        if (effectKind is not (SlideShowShapeAnimationEffectKind.ColorPulse
            or SlideShowShapeAnimationEffectKind.ColorWave
            or SlideShowShapeAnimationEffectKind.ChangeColor
            or SlideShowShapeAnimationEffectKind.GrowWithColor
            or SlideShowShapeAnimationEffectKind.Shimmer)
            || string.IsNullOrWhiteSpace(animation.PreservedColorBehaviorXml))
        {
            return (null, null);
        }

        return ResolveColorBehaviorXml(
            animation.PreservedColorBehaviorXml,
            presentation,
            effectiveClrMap);
    }

    private static (string? From, string? To) ResolveColorBehaviorXml(
        string? behaviorXml,
        Presentation? presentation,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        if (string.IsNullOrWhiteSpace(behaviorXml))
            return (null, null);

        try
        {
            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var root = XElement.Parse(behaviorXml, LoadOptions.PreserveWhitespace);
            var from = ResolveAnimationColor(root.Element(p + "clrFrom"), presentation, effectiveClrMap, a);
            var to = ResolveAnimationColor(
                root.Element(p + "clrTo") ?? root.Descendants(p + "to").LastOrDefault(),
                presentation,
                effectiveClrMap,
                a);
            return (from, to);
        }
        catch (XmlException)
        {
            return (null, null);
        }
    }

    private static (string? From, string? To) ResolveFillColorBehavior(
        ShapeAnimation animation,
        Presentation? presentation,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        if (presentation is null || string.IsNullOrWhiteSpace(animation.PreservedFillBehaviorXml))
            return (null, null);

        try
        {
            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var root = XElement.Parse(animation.PreservedFillBehaviorXml, LoadOptions.PreserveWhitespace);
            var animClr = root.Descendants(p + "animClr").FirstOrDefault();
            var to = ResolveAnimationColor(animClr?.Element(p + "to"), presentation, effectiveClrMap, a);
            var shape = FindSlideShape(presentation, animation.ShapeId);
            var from = shape?.Fill is ShapeFill.Solid solid
                ? solid.Color.Resolved.ToString().TrimStart('#')
                : null;
            return (from, to);
        }
        catch (XmlException)
        {
            return (null, null);
        }
    }

    private static SlideShape? FindSlideShape(Presentation presentation, uint shapeId)
    {
        foreach (var slide in presentation.Slides)
        {
            if (FindSlideShape(slide.Shapes, shapeId) is { } shape)
                return shape;
        }

        return null;
    }

    private static SlideShape? FindSlideShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (FindSlideShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    private static string? ResolveAnimationColor(
        XElement? colorContainer,
        Presentation? presentation,
        IReadOnlyDictionary<string, string>? effectiveClrMap,
        XNamespace drawingNamespace)
    {
        var color = colorContainer?.Elements().FirstOrDefault(element => element.Name.Namespace == drawingNamespace);
        if (color is null)
            return null;

        var transforms = ReadColorTransforms(color);
        SrgbColor resolved;
        if (color.Name.LocalName.Equals("schemeClr", StringComparison.Ordinal))
        {
            var roleName = color.Attribute("val")?.Value.Trim();
            var slot = ThemeColorResolver.MapRoleToSlot(roleName, effectiveClrMap);
            var scheme = new SchemeColorRef
            {
                RoleName = roleName,
                Slot = slot,
                LumMod = transforms.LumMod,
                LumOff = transforms.LumOff,
                Tint = transforms.Tint,
                Shade = transforms.Shade
            };
            resolved = ThemeColorResolver.Resolve(
                new ThemeAwareColor(SrgbColor.Black, scheme),
                presentation?.Theme ?? PresentationTheme.CreateDefault(),
                effectiveClrMap);
        }
        else if (color.Name.LocalName.Equals("srgbClr", StringComparison.Ordinal) &&
                 TryReadRgb(color.Attribute("val")?.Value, out var rgb))
        {
            resolved = ThemeColorTransform.Apply(
                rgb,
                transforms.LumMod,
                transforms.LumOff,
                transforms.Tint,
                transforms.Shade);
        }
        else
        {
            return null;
        }

        return $"{resolved.R:X2}{resolved.G:X2}{resolved.B:X2}";
    }

    private static (double LumMod, double LumOff, double Tint, double Shade) ReadColorTransforms(XElement color)
    {
        return (
            ReadPercentage(color.Element(color.Name.Namespace + "lumMod")?.Attribute("val")?.Value, 1),
            ReadPercentage(color.Element(color.Name.Namespace + "lumOff")?.Attribute("val")?.Value, 0),
            ReadPercentage(color.Element(color.Name.Namespace + "tint")?.Attribute("val")?.Value, 1),
            ReadPercentage(color.Element(color.Name.Namespace + "shade")?.Attribute("val")?.Value, 1));
    }

    private static double ReadPercentage(string? value, double fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw)
            ? Math.Clamp(raw / 100000d, 0, 2)
            : fallback;
    }

    private static bool TryReadRgb(string? value, out SrgbColor color)
    {
        if (value is { Length: 6 } &&
            int.TryParse(value.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = SrgbColor.FromRgb(rgb);
            return true;
        }

        color = SrgbColor.Black;
        return false;
    }

    private static SlideShowAnimationRevealTiming ResolveRevealTiming(
        ShapeAnimation animation,
        SlideShowShapeAnimationEffectKind effectKind)
    {
        if (effectKind == SlideShowShapeAnimationEffectKind.MotionPath ||
            effectKind is SlideShowShapeAnimationEffectKind.Spiral or SlideShowShapeAnimationEffectKind.Swivel ||
            animation.Kind is AnimationKind.Emphasis or AnimationKind.Exit)
        {
            return SlideShowAnimationRevealTiming.AtStart;
        }

        return SlideShowAnimationRevealTiming.OnComplete;
    }

    private static (double From, double To) ResolveOpacity(ShapeAnimation animation) =>
        animation.Kind == AnimationKind.Exit
            ? (1, 0)
            : (0, 1);

    private static (double From, double To) ResolveScale(ShapeAnimation animation)
    {
        if (animation.Kind == AnimationKind.Exit)
        {
            return (1, 0);
        }

        return AnimationAmountSemantics.IsGrowShrink(animation.Preset)
            ? (1, 1)
            : (0, 1);
    }

    private static double ResolvePeakScale(ShapeAnimation animation) =>
        AnimationAmountSemantics.IsGrowShrink(animation.Preset)
            ? AnimationAmountSemantics.ResolveScale(animation.Preset, animation.ScaleBehavior)
            : 1.2;

    private static (double FromX, double FromY, double ToX, double ToY, double PeakX, double PeakY)
        ResolveScaleAxesForPlayback(ShapeAnimation animation, double fromScale, double toScale, double peakScale)
    {
        if (!AnimationAmountSemantics.IsGrowShrink(animation.Preset))
        {
            return (fromScale, fromScale, toScale, toScale, peakScale, peakScale);
        }

        var (peakX, peakY) = AnimationAmountSemantics.ResolveScaleAxes(animation.Preset, animation.ScaleBehavior);
        return (fromScale, fromScale, toScale, toScale, peakX, peakY);
    }

    private static double ResolveRotationDegrees(ShapeAnimation animation) =>
        animation.Direction == AnimationDirection.Out
            && animation.Preset is AnimationPreset.Spiral or AnimationPreset.Swivel
            ? -360
            : 360;

    /// <summary>
    /// Applies the authored PowerPoint acceleration/deceleration envelope to normalized
    /// effect time. Overlapping malformed values are proportionally reduced while keeping
    /// the effect duration and endpoints intact.
    /// </summary>
    public static double ApplyTimingEasing(double progress, int? acceleration, int? deceleration)
    {
        progress = Math.Clamp(progress, 0, 1);
        var accel = Math.Clamp((acceleration ?? 0) / 100000d, 0, 1);
        var decel = Math.Clamp((deceleration ?? 0) / 100000d, 0, 1);
        if (accel + decel > 1)
        {
            var scale = 1 / (accel + decel);
            accel *= scale;
            decel *= scale;
        }

        if (accel > 0 && progress < accel)
        {
            var t = progress / accel;
            return accel * t * t * (3 - 2 * t);
        }

        var decelStart = 1 - decel;
        if (decel > 0 && progress > decelStart)
        {
            var t = (progress - decelStart) / decel;
            return decelStart + decel * t * t * (3 - 2 * t);
        }

        return progress;
    }

    private static (double X, double Y) ResolveFlyInOffset(AnimationDirection? direction) =>
        direction switch
        {
            AnimationDirection.FromLeft => (-1, 0),
            AnimationDirection.FromRight => (1, 0),
            AnimationDirection.FromTop => (0, -1),
            AnimationDirection.FromBottom => (0, 1),
            AnimationDirection.FromTopLeft => (-1, -1),
            AnimationDirection.FromTopRight => (1, -1),
            AnimationDirection.FromBottomLeft => (-1, 1),
            AnimationDirection.FromBottomRight => (1, 1),
            AnimationDirection.Left => (-1, 0),
            AnimationDirection.Right => (1, 0),
            AnimationDirection.Up => (0, -1),
            AnimationDirection.Down => (0, 1),
            _ => (0, 1)
        };

    private static bool IsHorizontalWipe(AnimationDirection? direction) =>
        direction is AnimationDirection.Left
            or AnimationDirection.Right
            or AnimationDirection.FromLeft
            or AnimationDirection.FromRight
            or AnimationDirection.Horizontal
            or AnimationDirection.HorizontalIn
            or AnimationDirection.HorizontalOut
            or null;

    private static bool IsHorizontalBlinds(AnimationDirection? direction) =>
        direction is not AnimationDirection.Vertical;

    private static bool IsHorizontalCheckerboard(AnimationDirection? direction) =>
        direction is not AnimationDirection.Vertical;

    private static bool BoxExpandsFromCenter(ShapeAnimation animation) =>
        ExpandsFromCenter(animation);

    private static SlideShowGeometricMaskKind ResolveGeometricMaskKind(ShapeAnimation animation) =>
        animation.Preset switch
        {
            AnimationPreset.Circle => SlideShowGeometricMaskKind.Circle,
            AnimationPreset.Diamond => SlideShowGeometricMaskKind.Diamond,
            AnimationPreset.Plus => SlideShowGeometricMaskKind.Plus,
            AnimationPreset.Strips => SlideShowGeometricMaskKind.Strips,
            AnimationPreset.Wedge => SlideShowGeometricMaskKind.Wedge,
            AnimationPreset.Wheel => SlideShowGeometricMaskKind.Wheel,
            _ => SlideShowGeometricMaskKind.None
        };

    private static bool GeometricMaskExpandsFromCenter(ShapeAnimation animation) =>
        ExpandsFromCenter(animation);

    private static int ResolveGeometricMaskSpokeCount(ShapeAnimation animation) =>
        animation.Preset == AnimationPreset.Wheel
            ? animation.WheelSpokeCount is > 0
                ? animation.WheelSpokeCount.Value
                : WheelSpokeCount
            : 0;

    private static int ResolveGeometricMaskStripCount(ShapeAnimation animation) =>
        animation.Preset == AnimationPreset.Strips ? StripsBandCount : 0;

    private static bool StripsSlopeDown(AnimationDirection? direction) =>
        direction is AnimationDirection.LeftDown or AnimationDirection.RightUp or AnimationDirection.FromTopRight
            or AnimationDirection.FromBottomLeft;

    private static bool ExpandsFromCenter(ShapeAnimation animation) =>
        animation.Direction switch
        {
            AnimationDirection.In => true,
            AnimationDirection.Out => false,
            _ => animation.Kind != AnimationKind.Exit
        };

    private static IReadOnlyList<SlideShowMotionPathKeyFrame> BuildMotionKeyFrames(MotionPath? path)
    {
        if (path is null)
        {
            return Array.Empty<SlideShowMotionPathKeyFrame>();
        }

        var frames = new List<SlideShowMotionPathKeyFrame>(MotionPathFrameCount + 1);
        for (var frame = 0; frame <= MotionPathFrameCount; frame++)
        {
            var progress = frame / (double)MotionPathFrameCount;
            var (dx, dy) = MotionPathEvaluator.Sample(path, progress);
            frames.Add(new SlideShowMotionPathKeyFrame(progress, dx, dy));
        }

        return frames;
    }
}
