using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record AnimationPaneDurationEditPlan(
    bool ShouldUpdate,
    int DurationMs,
    string DisplayText);

public static class AnimationPanePlanner
{
    private static readonly string[] TriggerLabelValues =
    [
        "On Click",
        "With Previous",
        "After Previous"
    ];

    public static IReadOnlyList<string> TriggerLabels => TriggerLabelValues;

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
    {
        if (double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double seconds)
            && seconds > 0)
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
}
