using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

public sealed record ConditionalFormatIconSetOption(
    string Style,
    int IconCount,
    string LabelKey,
    string CategoryKey,
    string KeyTip);

public sealed record ConditionalFormatIconSetGalleryGroup(
    string CategoryKey,
    IReadOnlyList<ConditionalFormatIconSetOption> Options);

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
        // x14-extension icon sets (stored in extLst by Excel; rendered by FreeX via x14 reader)
        ("3Stars", 3),
        ("3Triangles", 3),
    ];

    /// <summary>Gallery-facing icon-set styles with portable localization keys and keytips, in Excel-style order.</summary>
    public static readonly IReadOnlyList<ConditionalFormatIconSetOption> GalleryOptions =
    [
        GalleryOption("3Arrows", "ConditionalFormatIconSet_3Arrows_Label", DirectionalCategory, "I3"),
        GalleryOption("3ArrowsGray", "ConditionalFormatIconSet_3ArrowsGray_Label", DirectionalCategory, "IG"),
        GalleryOption("4Arrows", "ConditionalFormatIconSet_4Arrows_Label", DirectionalCategory, "I4"),
        GalleryOption("4ArrowsGray", "ConditionalFormatIconSet_4ArrowsGray_Label", DirectionalCategory, "IH"),
        GalleryOption("5Arrows", "ConditionalFormatIconSet_5Arrows_Label", DirectionalCategory, "I5"),
        GalleryOption("5ArrowsGray", "ConditionalFormatIconSet_5ArrowsGray_Label", DirectionalCategory, "IJ"),
        GalleryOption("3TrafficLights1", "ConditionalFormatIconSet_3TrafficLights1_Label", ShapesCategory, "IT"),
        GalleryOption("3TrafficLights2", "ConditionalFormatIconSet_3TrafficLights2_Label", ShapesCategory, "IR"),
        GalleryOption("3Signs", "ConditionalFormatIconSet_3Signs_Label", ShapesCategory, "IS"),
        GalleryOption("3Symbols", "ConditionalFormatIconSet_3Symbols_Label", ShapesCategory, "IY"),
        GalleryOption("3Symbols2", "ConditionalFormatIconSet_3Symbols2_Label", ShapesCategory, "IU"),
        GalleryOption("3Flags", "ConditionalFormatIconSet_3Flags_Label", ShapesCategory, "IF"),
        GalleryOption("4TrafficLights", "ConditionalFormatIconSet_4TrafficLights_Label", IndicatorsCategory, "IL"),
        GalleryOption("4RedToBlack", "ConditionalFormatIconSet_4RedToBlack_Label", IndicatorsCategory, "IB"),
        GalleryOption("4Rating", "ConditionalFormatIconSet_4Rating_Label", RatingsCategory, "I9"),
        GalleryOption("5Rating", "ConditionalFormatIconSet_5Rating_Label", RatingsCategory, "IA"),
        GalleryOption("5Quarters", "ConditionalFormatIconSet_5Quarters_Label", RatingsCategory, "IQ"),
        GalleryOption("5Boxes", "ConditionalFormatIconSet_5Boxes_Label", RatingsCategory, "IX")
    ];

    /// <summary>Gallery options grouped by portable category key.</summary>
    public static readonly IReadOnlyList<ConditionalFormatIconSetGalleryGroup> GalleryGroups =
        GalleryOptions
            .GroupBy(option => option.CategoryKey)
            .Select(group => new ConditionalFormatIconSetGalleryGroup(group.Key, group.ToArray()))
            .ToArray();

    /// <summary>The icon-set style ids exposed in the preset gallery, excluding x14-only styles.</summary>
    public static IReadOnlyList<string> GalleryStyles => GalleryOptions.Select(option => option.Style).ToArray();

    /// <summary>The icon (bucket) count for a style, defaulting to 3 for unknown styles.</summary>
    public static int GetIconCount(string? style)
    {
        foreach (var (candidate, count) in Styles)
            if (string.Equals(candidate, style, StringComparison.Ordinal))
                return count;

        return 3;
    }

    /// <summary>
    /// The default evenly spaced percent thresholds for a style's bucket count, matching Excel's
    /// rounding (e.g. the 3-icon default is 33/67, not 33/66 from truncated integer division).
    /// </summary>
    public static IReadOnlyList<CfThresholdModel> CreateThresholds(string? style)
    {
        var iconCount = GetIconCount(style);
        return Enumerable.Range(0, iconCount)
            .Select(index => new CfThresholdModel(
                CfThresholdType.Percent,
                ((int)Math.Round(index * 100.0 / iconCount, MidpointRounding.AwayFromZero))
                    .ToString(CultureInfo.InvariantCulture)))
            .ToList();
    }

    /// <summary>Create the default icon-set rule for a gallery style, or <c>null</c> when the style is not in the gallery.</summary>
    public static ConditionalFormat? CreateRule(string? style, GridRange range)
    {
        var option = FindGalleryOption(style);
        if (option is null)
            return null;

        var rule = new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = option.Style,
            IconSetShowValue = true,
            IconSetReverse = false
        };
        rule.IconSetThresholds.AddRange(CreateThresholds(option.Style));
        return rule;
    }

    private static ConditionalFormatIconSetOption GalleryOption(
        string style,
        string labelKey,
        string categoryKey,
        string keyTip) =>
        new(
            style,
            GetIconCount(style),
            labelKey,
            categoryKey,
            keyTip);

    private static ConditionalFormatIconSetOption? FindGalleryOption(string? style)
    {
        foreach (var option in GalleryOptions)
        {
            if (string.Equals(option.Style, style, StringComparison.Ordinal))
                return option;
        }

        return null;
    }

    private const string DirectionalCategory = "ConditionalFormatIconSet_Category_Directional";
    private const string ShapesCategory = "ConditionalFormatIconSet_Category_Shapes";
    private const string IndicatorsCategory = "ConditionalFormatIconSet_Category_Indicators";
    private const string RatingsCategory = "ConditionalFormatIconSet_Category_Ratings";
}
