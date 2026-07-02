using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record AnimationPaneDurationEditPlan(
    bool ShouldUpdate,
    int DurationMs,
    string DisplayText);

public enum AnimationPaneTimingEditKind
{
    Trigger,
    Duration,
    Delay,
}

public sealed record AnimationPaneEffectOptionDescriptor(
    string Id,
    string DisplayText,
    AnimationDirection? Direction,
    bool IsSelected);

public sealed record AnimationPaneEffectOptionsPlan(
    bool CanApply,
    int AnimationIndex,
    string EffectText,
    string SelectedOptionText,
    IReadOnlyList<AnimationPaneEffectOptionDescriptor> Options,
    string? DisabledReason);

public sealed record AnimationPaneEffectOptionMutationPlan(
    bool ShouldApply,
    int AnimationIndex,
    AnimationDirection? Direction,
    string DisplayText,
    string? DisabledReason);

public sealed record AnimationPaneTimingMutationPlan(
    bool ShouldApply,
    int AnimationIndex,
    AnimationPaneTimingEditKind Kind,
    AnimationTrigger Trigger,
    int DurationMs,
    int DelayMs,
    string DisplayText,
    string? DisabledReason);

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
    bool IsSelected,
    AnimationPaneEffectOptionsPlan EffectOptions);

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
    public const string MissingAnimationMessage = "Select an animation to edit timing.";
    public const string InvalidTriggerMessage = "Choose a valid animation trigger.";
    public const string InvalidDurationMessage = "Enter a duration greater than 0 seconds.";
    public const string InvalidDelayMessage = "Enter a delay of 0 seconds or greater.";
    public const string MissingEffectOptionMessage = "Select an animation to edit effect options.";
    public const string UnsupportedEffectOptionMessage = "This effect has no shared effect options yet.";
    public const string InvalidEffectOptionMessage = "Choose a valid effect option.";

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
            var effectOptions = BuildEffectOptionsPlan(animations, i);
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
                i == selectedIndex,
                effectOptions));
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
        text = text.Trim();
        if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^1].Trim();
        }

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

    public static AnimationPaneTimingMutationPlan BuildTriggerMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        int selectedTriggerIndex)
    {
        if (!TryGetAnimation(animations, animationIndex, out var animation))
        {
            return DisabledMutationPlan(
                animationIndex,
                AnimationPaneTimingEditKind.Trigger,
                MissingAnimationMessage);
        }

        if (!TryGetTrigger(selectedTriggerIndex, out var trigger))
        {
            return new AnimationPaneTimingMutationPlan(
                false,
                animationIndex,
                AnimationPaneTimingEditKind.Trigger,
                animation.Trigger,
                animation.DurationMs,
                animation.DelayMs,
                TriggerLabelValues[ToTriggerIndex(animation.Trigger)],
                InvalidTriggerMessage);
        }

        return new AnimationPaneTimingMutationPlan(
            animation.Trigger != trigger,
            animationIndex,
            AnimationPaneTimingEditKind.Trigger,
            trigger,
            animation.DurationMs,
            animation.DelayMs,
            TriggerLabelValues[ToTriggerIndex(trigger)],
            null);
    }

    public static AnimationPaneTimingMutationPlan BuildDurationMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        string text,
        CultureInfo? displayCulture = null)
    {
        if (!TryGetAnimation(animations, animationIndex, out var animation))
        {
            return DisabledMutationPlan(
                animationIndex,
                AnimationPaneTimingEditKind.Duration,
                MissingAnimationMessage,
                displayCulture);
        }

        var editPlan = BuildDurationEditPlan(text, animation.DurationMs, displayCulture);
        return new AnimationPaneTimingMutationPlan(
            editPlan.ShouldUpdate,
            animationIndex,
            AnimationPaneTimingEditKind.Duration,
            animation.Trigger,
            editPlan.DurationMs,
            animation.DelayMs,
            editPlan.DisplayText,
            TryParseDuration(text, out _) ? null : InvalidDurationMessage);
    }

    public static AnimationPaneTimingMutationPlan BuildDelayMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        string text,
        CultureInfo? displayCulture = null)
    {
        if (!TryGetAnimation(animations, animationIndex, out var animation))
        {
            return DisabledMutationPlan(
                animationIndex,
                AnimationPaneTimingEditKind.Delay,
                MissingAnimationMessage,
                displayCulture);
        }

        var editPlan = BuildDelayEditPlan(text, animation.DelayMs, displayCulture);
        return new AnimationPaneTimingMutationPlan(
            editPlan.ShouldUpdate,
            animationIndex,
            AnimationPaneTimingEditKind.Delay,
            animation.Trigger,
            animation.DurationMs,
            editPlan.DurationMs,
            editPlan.DisplayText,
            TryParseDelay(text, out _) ? null : InvalidDelayMessage);
    }

    public static AnimationPaneEffectOptionsPlan BuildEffectOptionsPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex)
    {
        if (!TryGetAnimation(animations, animationIndex, out var animation))
        {
            return DisabledEffectOptionsPlan(
                animationIndex,
                string.Empty,
                MissingEffectOptionMessage);
        }

        var descriptors = BuildSupportedEffectOptions(animation).ToArray();
        if (descriptors.Length == 0)
        {
            return DisabledEffectOptionsPlan(
                animationIndex,
                FormatEffect(animation),
                UnsupportedEffectOptionMessage);
        }

        var selected = descriptors.FirstOrDefault(option => option.Direction == animation.Direction)
            ?? descriptors[0];
        var normalized = descriptors
            .Select(option => option with { IsSelected = option.Direction == selected.Direction })
            .ToArray();

        return new AnimationPaneEffectOptionsPlan(
            true,
            animationIndex,
            FormatEffect(animation),
            selected.DisplayText,
            normalized,
            null);
    }

    public static AnimationPaneEffectOptionMutationPlan BuildEffectOptionMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        string optionId)
    {
        var optionsPlan = BuildEffectOptionsPlan(animations, animationIndex);
        if (!optionsPlan.CanApply)
        {
            return new AnimationPaneEffectOptionMutationPlan(
                false,
                animationIndex,
                null,
                string.Empty,
                optionsPlan.DisabledReason);
        }

        var option = optionsPlan.Options.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, optionId));
        if (option is null)
        {
            return new AnimationPaneEffectOptionMutationPlan(
                false,
                animationIndex,
                optionsPlan.Options.First(option => option.IsSelected).Direction,
                optionsPlan.SelectedOptionText,
                InvalidEffectOptionMessage);
        }

        var animation = animations[animationIndex];
        return new AnimationPaneEffectOptionMutationPlan(
            animation.Direction != option.Direction,
            animationIndex,
            option.Direction,
            option.DisplayText,
            null);
    }

    public static bool TryApplyEffectOptionMutation(
        EditingSession editor,
        AnimationPaneEffectOptionMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.ShouldApply
            || !TryGetAnimation(editor.CurrentSlideAnimations, plan.AnimationIndex, out var current))
        {
            return false;
        }

        var updated = PresentationAnimationCommandPlanner.CloneAnimation(current);
        updated.Direction = plan.Direction;
        editor.SetAnimation(plan.AnimationIndex, updated);
        return true;
    }

    public static bool TryApplyTimingMutation(
        EditingSession editor,
        AnimationPaneTimingMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.ShouldApply
            || !TryGetAnimation(editor.CurrentSlideAnimations, plan.AnimationIndex, out var current))
        {
            return false;
        }

        var updated = PresentationAnimationCommandPlanner.CloneAnimation(current);
        updated.Trigger = plan.Trigger;
        updated.DurationMs = plan.DurationMs;
        updated.DelayMs = plan.DelayMs;
        editor.SetAnimation(plan.AnimationIndex, updated);
        return true;
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

    private static bool TryGetAnimation(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        out ShapeAnimation animation)
    {
        if (animationIndex >= 0 && animationIndex < animations.Count)
        {
            animation = animations[animationIndex];
            return true;
        }

        animation = default!;
        return false;
    }

    private static AnimationPaneTimingMutationPlan DisabledMutationPlan(
        int animationIndex,
        AnimationPaneTimingEditKind kind,
        string disabledReason,
        CultureInfo? displayCulture = null)
        => new(
            false,
            animationIndex,
            kind,
            AnimationTrigger.OnClick,
            0,
            0,
            FormatDuration(0, displayCulture),
            disabledReason);

    private static AnimationPaneEffectOptionsPlan DisabledEffectOptionsPlan(
        int animationIndex,
        string effectText,
        string disabledReason)
        => new(
            false,
            animationIndex,
            effectText,
            string.Empty,
            Array.Empty<AnimationPaneEffectOptionDescriptor>(),
            disabledReason);

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> BuildSupportedEffectOptions(
        ShapeAnimation animation)
    {
        if (animation.Kind == AnimationKind.Motion)
        {
            yield break;
        }

        switch (animation.Preset)
        {
            case AnimationPreset.FlyIn:
            case AnimationPreset.Wipe:
                yield return EffectOption("from-bottom", "From Bottom", AnimationDirection.FromBottom);
                yield return EffectOption("from-left", "From Left", AnimationDirection.FromLeft);
                yield return EffectOption("from-right", "From Right", AnimationDirection.FromRight);
                yield return EffectOption("from-top", "From Top", AnimationDirection.FromTop);
                break;

            case AnimationPreset.Zoom:
                yield return EffectOption("in", "In", AnimationDirection.In);
                yield return EffectOption("out", "Out", AnimationDirection.Out);
                break;

            case AnimationPreset.Split:
            case AnimationPreset.RandomBars:
                yield return EffectOption("horizontal", "Horizontal", AnimationDirection.Horizontal);
                yield return EffectOption("vertical", "Vertical", AnimationDirection.Vertical);
                break;
        }
    }

    private static AnimationPaneEffectOptionDescriptor EffectOption(
        string id,
        string displayText,
        AnimationDirection direction)
        => new(id, displayText, direction, false);
}
