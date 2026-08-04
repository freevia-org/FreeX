using System.Globalization;

namespace FreeP.Core.Model;

/// <summary>
/// Renderer-neutral amount semantics for PowerPoint's Grow/Shrink, Pulse, and Grow With Color emphasis effects.
/// The amount authority is p:animScale, never p:cTn/@presetSubtype.
/// </summary>
public static class AnimationAmountSemantics
{
    public const string DefaultGrowScaleText = "120%";
    public const string DefaultShrinkScaleText = "80%";

    private static readonly IReadOnlyList<AnimationAmountChoice> Choices =
    [
        new("25", "Tiny (25%)", 0.25),
        new("50", "Smaller (50%)", 0.50),
        new("150", "Larger (150%)", 1.50),
        new("400", "Huge (400%)", 4.00),
    ];

    public static IReadOnlyList<AnimationAmountChoice> SupportedChoices => Choices;

    public static bool IsGrowShrink(AnimationPreset preset) =>
        preset is AnimationPreset.Grow
            or AnimationPreset.Shrink
            or AnimationPreset.Pulse
            or AnimationPreset.GrowWithColor;

    public static (double X, double Y) ResolveScaleAxes(
        AnimationPreset preset,
        AnimationScaleBehavior? behavior)
    {
        if (!IsGrowShrink(preset))
            return (1, 1);

        var fallback = preset == AnimationPreset.Shrink ? 0.8 : 1.2;
        if (behavior is null)
            return (fallback, fallback);

        var x = ResolveAxis(behavior.FromX, behavior.ToX, behavior.ByX) ?? fallback;
        var y = ResolveAxis(behavior.FromY, behavior.ToY, behavior.ByY) ?? x;
        return (x, y);
    }

    public static double ResolveScale(AnimationPreset preset, AnimationScaleBehavior? behavior) =>
        ResolveScaleAxes(preset, behavior).X;

    public static AnimationScaleBehavior CreateChoiceBehavior(AnimationPreset preset, double scale) =>
        AnimationScaleBehavior.FromTo(scale);

    public static AnimationPreset ResolvePreset(
        AnimationPreset mappedPreset,
        AnimationScaleBehavior? behavior)
    {
        if (mappedPreset != AnimationPreset.Grow || behavior is null)
            return mappedPreset;

        var (x, y) = ResolveScaleAxes(mappedPreset, behavior);
        return x < 1 && y < 1 ? AnimationPreset.Shrink : AnimationPreset.Grow;
    }

    public static string Describe(AnimationPreset preset, AnimationScaleBehavior? behavior)
    {
        if (behavior is null)
        {
            var fallback = preset == AnimationPreset.Shrink
                ? DefaultShrinkScaleText
                : DefaultGrowScaleText;
            return $"Default ({fallback})";
        }

        var resolvedX = ResolveAxis(behavior.FromX, behavior.ToX, behavior.ByX);
        var resolvedY = ResolveAxis(behavior.FromY, behavior.ToY, behavior.ByY);
        if (!resolvedX.HasValue || !resolvedY.HasValue)
        {
            return $"Custom (ScaleX {DescribeValue(behavior, behavior.ToX, behavior.ByX)}, "
                   + $"ScaleY {DescribeValue(behavior, behavior.ToY, behavior.ByY)})";
        }

        var x = resolvedX.Value;
        var y = resolvedY.Value;
        var choice = Choices.FirstOrDefault(candidate =>
            NearlyEqual(candidate.Scale, x) && NearlyEqual(candidate.Scale, y));
        if (choice is not null)
            return choice.DisplayText;
        if (NearlyEqual(x, y))
            return $"Custom ({x * 100:0.##}%)";
        return $"Custom (ScaleX {DescribeValue(behavior, behavior.ToX, behavior.ByX)}, "
               + $"ScaleY {DescribeValue(behavior, behavior.ToY, behavior.ByY)})";
    }

    public static bool IsSupportedScale(AnimationScaleBehavior? behavior, double scale)
    {
        var (x, y) = behavior is null ? (double.NaN, double.NaN) : ResolveScaleAxes(AnimationPreset.Grow, behavior);
        return NearlyEqual(x, scale) && NearlyEqual(y, scale);
    }

    private static double? ResolveAxis(string? from, string? to, string? by)
    {
        var fromValue = TryParseScaleValue(from, out var parsedFrom) ? parsedFrom : (double?)null;
        var toValue = TryParseScaleValue(to, out var parsedTo) ? parsedTo : (double?)null;
        var byValue = TryParseScaleValue(by, out var parsedBy) ? parsedBy : (double?)null;

        if (fromValue.HasValue && toValue.HasValue)
            return toValue;
        if (fromValue.HasValue && byValue.HasValue)
            return fromValue + byValue;
        if (toValue.HasValue)
            return toValue;
        if (byValue.HasValue)
            return 1 + byValue;
        return null;
    }

    public static bool TryParseScaleValue(string? raw, out double scale)
    {
        scale = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var text = raw.Trim();
        if (text.EndsWith('%'))
        {
            if (!double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                return false;
            scale = percent / 100d;
            return scale >= 0;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fixedPercent))
            return false;
        scale = fixedPercent / 100000d;
        return scale >= 0;
    }

    private static string DescribeValue(AnimationScaleBehavior behavior, string? to, string? by)
    {
        var raw = to ?? by;
        return raw is not null && TryParseScaleValue(raw, out var scale)
            ? $"{scale * 100:0.##}%"
            : raw ?? "?";
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) < 0.000001;
}

public sealed record AnimationAmountChoice(string Token, string DisplayText, double Scale);
