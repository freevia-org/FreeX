using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Portable list of conditional-formatting icon-set styles, their per-bucket icon counts, and the
/// default evenly spaced percent thresholds each style uses. This is the single source of the style
/// roster and threshold math shared by every shell: the desktop host decorates each style with
/// localized labels/categories/keytips on top of this list, while the cross-platform port consumes
/// it directly. Depends only on the domain model, so it carries no UI framework references.
/// </summary>
public static class ConditionalFormatIconSetCatalog
{
    /// <summary>The default style applied when none is chosen.</summary>
    public const string DefaultStyle = "3TrafficLights1";

    /// <summary>Available icon-set styles paired with their icon (bucket) count, in gallery order.</summary>
    public static IReadOnlyList<(string Style, int IconCount)> Styles { get; } =
    [
        ("3Arrows", 3),
        ("3ArrowsGray", 3),
        ("4Arrows", 4),
        ("4ArrowsGray", 4),
        ("5Arrows", 5),
        ("5ArrowsGray", 5),
        ("3TrafficLights1", 3),
        ("3TrafficLights2", 3),
        ("3Signs", 3),
        ("3Symbols", 3),
        ("3Symbols2", 3),
        ("3Flags", 3),
        ("4TrafficLights", 4),
        ("4RedToBlack", 4),
        ("4Rating", 4),
        ("5Rating", 5),
        ("5Quarters", 5),
        ("5Boxes", 5),
    ];

    /// <summary>The icon (bucket) count for a style, defaulting to 3 for unknown styles.</summary>
    public static int GetIconCount(string? style)
    {
        foreach (var (candidate, count) in Styles)
            if (string.Equals(candidate, style, StringComparison.Ordinal))
                return count;

        return 3;
    }

    /// <summary>The default evenly spaced percent thresholds for a style's bucket count.</summary>
    public static IReadOnlyList<CfThresholdModel> CreateThresholds(string? style)
    {
        var iconCount = GetIconCount(style);
        var step = 100 / iconCount;
        return Enumerable.Range(0, iconCount)
            .Select(index => new CfThresholdModel(
                CfThresholdType.Percent,
                (index * step).ToString(CultureInfo.InvariantCulture)))
            .ToList();
    }
}
