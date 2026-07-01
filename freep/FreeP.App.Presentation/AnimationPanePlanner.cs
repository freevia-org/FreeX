using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record AnimationPaneDurationEditPlan(
    bool ShouldUpdate,
    int DurationMs,
    string DisplayText);

public sealed record AnimationPaneTimelinePlan(
    IReadOnlyList<AnimationPaneTimelineItemPlan> Items,
    int SelectedIndex,
    AnimationPanePlaybackIntent PreviewIntent)
{
    public bool HasAnimations => Items.Count > 0;
    public AnimationPaneTimelineItemPlan? SelectedItem =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
}

public sealed record AnimationPaneTimelineItemPlan(
    int Index,
    string OrderText,
    uint ShapeId,
    string ShapeName,
    string EffectText,
    AnimationKind Kind,
    AnimationPreset Preset,
    AnimationTrigger Trigger,
    int TriggerIndex,
    string TriggerText,
    int DelayMs,
    string DelayText,
    int DurationMs,
    string DurationText,
    int StartMs,
    string StartText,
    int EndMs,
    bool CanMoveEarlier,
    bool CanMoveLater,
    bool IsSelected);

public enum AnimationPanePlaybackIntentKind
{
    None,
    PreviewCurrentSlide,
}

public sealed record AnimationPanePlaybackIntent(
    AnimationPanePlaybackIntentKind Kind,
    bool CanExecute,
    int? SelectedAnimationIndex,
    int TotalDurationMs,
    string Description);

public sealed record AnimationPaneReorderIntent(
    bool CanMove,
    int FromIndex,
    int ToIndex);

public static class AnimationPanePlanner
{
    private static readonly string[] TriggerLabelValues =
    [
        "On Click",
        "With Previous",
        "After Previous"
    ];

    public static IReadOnlyList<string> TriggerLabels => TriggerLabelValues;

    public static AnimationPaneTimelinePlan BuildTimelinePlan(
        Slide? slide,
        IReadOnlyList<uint>? selectedShapeIds = null,
        int selectedAnimationIndex = -1,
        CultureInfo? displayCulture = null)
    {
        displayCulture ??= CultureInfo.CurrentCulture;
        var animations = slide?.Animations;
        if (animations is null || animations.Count == 0)
        {
            return new AnimationPaneTimelinePlan(
                Array.Empty<AnimationPaneTimelineItemPlan>(),
                -1,
                new AnimationPanePlaybackIntent(
                    AnimationPanePlaybackIntentKind.None,
                    false,
                    null,
                    0,
                    "No animations to preview"));
        }

        var selectedIndex = NormalizeSelectedIndex(animations, selectedShapeIds, selectedAnimationIndex);
        var items = new List<AnimationPaneTimelineItemPlan>(animations.Count);
        var sequenceAnchorMs = 0;
        var previousEndMs = 0;
        var totalDurationMs = 0;

        for (int i = 0; i < animations.Count; i++)
        {
            var animation = animations[i];
            if (animation.Trigger == AnimationTrigger.OnClick)
            {
                sequenceAnchorMs = previousEndMs;
            }

            var startMs = animation.Trigger switch
            {
                AnimationTrigger.AfterPrevious => previousEndMs + Math.Max(0, animation.DelayMs),
                _ => sequenceAnchorMs + Math.Max(0, animation.DelayMs),
            };
            var durationMs = Math.Max(0, animation.DurationMs);
            var endMs = startMs + durationMs;
            previousEndMs = endMs;
            totalDurationMs = Math.Max(totalDurationMs, endMs);

            var triggerIndex = ToTriggerIndex(animation.Trigger);
            items.Add(new AnimationPaneTimelineItemPlan(
                i,
                (i + 1).ToString(CultureInfo.InvariantCulture),
                animation.ShapeId,
                ResolveShapeName(slide!, animation.ShapeId),
                FormatEffect(animation),
                animation.Kind,
                animation.Preset,
                animation.Trigger,
                triggerIndex,
                TriggerLabelValues[triggerIndex],
                animation.DelayMs,
                FormatDuration(Math.Max(0, animation.DelayMs), displayCulture),
                animation.DurationMs,
                FormatDuration(Math.Max(0, animation.DurationMs), displayCulture),
                startMs,
                FormatDuration(startMs, displayCulture),
                endMs,
                i > 0,
                i < animations.Count - 1,
                i == selectedIndex));
        }

        return new AnimationPaneTimelinePlan(
            items,
            selectedIndex,
            new AnimationPanePlaybackIntent(
                AnimationPanePlaybackIntentKind.PreviewCurrentSlide,
                true,
                selectedIndex >= 0 ? selectedIndex : null,
                totalDurationMs,
                "Preview current slide animations"));
    }

    public static AnimationPaneReorderIntent BuildReorderIntent(
        int animationIndex,
        int animationCount,
        int offset)
    {
        var toIndex = animationIndex + offset;
        var canMove = animationIndex >= 0
            && animationIndex < animationCount
            && toIndex >= 0
            && toIndex < animationCount
            && offset != 0;
        return new AnimationPaneReorderIntent(canMove, animationIndex, toIndex);
    }

    public static string FormatEffect(ShapeAnimation animation)
    {
        var kindPrefix = animation.Kind switch
        {
            AnimationKind.Entrance => "In",
            AnimationKind.Exit => "Out",
            AnimationKind.Emphasis => "Em",
            AnimationKind.Motion => "Mv",
            _ => "?"
        };

        return animation.Kind == AnimationKind.Motion
            ? "Mv: Motion"
            : $"{kindPrefix}: {animation.Preset}";
    }

    public static int ToTriggerIndex(AnimationTrigger trigger)
    {
        return trigger switch
        {
            AnimationTrigger.OnClick => 0,
            AnimationTrigger.WithPrevious => 1,
            AnimationTrigger.AfterPrevious => 2,
            _ => 0
        };
    }

    public static bool TryGetTrigger(int selectedIndex, out AnimationTrigger trigger)
    {
        switch (selectedIndex)
        {
            case 0:
                trigger = AnimationTrigger.OnClick;
                return true;
            case 1:
                trigger = AnimationTrigger.WithPrevious;
                return true;
            case 2:
                trigger = AnimationTrigger.AfterPrevious;
                return true;
            default:
                trigger = AnimationTrigger.OnClick;
                return false;
        }
    }

    public static string FormatDuration(int ms, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        double seconds = ms / 1000.0;
        return seconds.ToString("0.##", culture);
    }

    public static bool TryParseDuration(string text, out int ms)
        => TryParseTimingSeconds(text, allowZero: false, out ms);

    public static bool TryParseDelay(string text, out int ms)
        => TryParseTimingSeconds(text, allowZero: true, out ms);

    private static bool TryParseTimingSeconds(string text, bool allowZero, out int ms)
    {
        if (double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double seconds)
            && (allowZero ? seconds >= 0 : seconds > 0))
        {
            ms = (int)(seconds * 1000.0);
            return true;
        }

        ms = 0;
        return false;
    }

    public static AnimationPaneDurationEditPlan BuildDurationEditPlan(
        string text,
        int currentDurationMs,
        CultureInfo? displayCulture = null)
    {
        if (TryParseDuration(text, out int parsedDurationMs)
            && parsedDurationMs != currentDurationMs)
        {
            return new(true, parsedDurationMs, FormatDuration(parsedDurationMs, displayCulture));
        }

        return new(false, currentDurationMs, FormatDuration(currentDurationMs, displayCulture));
    }

    public static AnimationPaneDurationEditPlan BuildDelayEditPlan(
        string text,
        int currentDelayMs,
        CultureInfo? displayCulture = null)
    {
        if (TryParseDelay(text, out int parsedDelayMs)
            && parsedDelayMs != currentDelayMs)
        {
            return new(true, parsedDelayMs, FormatDuration(parsedDelayMs, displayCulture));
        }

        return new(false, currentDelayMs, FormatDuration(currentDelayMs, displayCulture));
    }

    private static int NormalizeSelectedIndex(
        IReadOnlyList<ShapeAnimation> animations,
        IReadOnlyList<uint>? selectedShapeIds,
        int selectedAnimationIndex)
    {
        if (selectedAnimationIndex >= 0 && selectedAnimationIndex < animations.Count)
        {
            return selectedAnimationIndex;
        }

        if (selectedShapeIds is null || selectedShapeIds.Count == 0)
        {
            return -1;
        }

        for (int i = animations.Count - 1; i >= 0; i--)
        {
            if (selectedShapeIds.Contains(animations[i].ShapeId))
            {
                return i;
            }
        }

        return -1;
    }

    private static string ResolveShapeName(Slide slide, uint shapeId)
    {
        var shape = slide.Shapes.FirstOrDefault(s => s.Id == shapeId);
        return string.IsNullOrWhiteSpace(shape?.Name)
            ? $"Shape {shapeId.ToString(CultureInfo.InvariantCulture)}"
            : shape!.Name;
    }
}
