using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record ConditionalFormatIconSetOption(
    string Style,
    int IconCount,
    string Label,
    string Category,
    string KeyTip);

public sealed record ConditionalFormatIconSetGalleryGroup(
    string Name,
    IReadOnlyList<ConditionalFormatIconSetOption> Options);

public static class ConditionalFormatIconSetPlanner
{
    // Per-style icon (bucket) counts come from the shared, portable
    // ConditionalFormatIconSetCatalog; this host layer adds only the localized label,
    // category, and keytip decoration on top of that roster.
    public static readonly IReadOnlyList<ConditionalFormatIconSetOption> Options =
    [
        Decorate("3Arrows", "ConditionalFormatIconSet_3Arrows_Label", "ConditionalFormatIconSet_Category_Directional", "I3"),
        Decorate("3ArrowsGray", "ConditionalFormatIconSet_3ArrowsGray_Label", "ConditionalFormatIconSet_Category_Directional", "IG"),
        Decorate("4Arrows", "ConditionalFormatIconSet_4Arrows_Label", "ConditionalFormatIconSet_Category_Directional", "I4"),
        Decorate("4ArrowsGray", "ConditionalFormatIconSet_4ArrowsGray_Label", "ConditionalFormatIconSet_Category_Directional", "IH"),
        Decorate("5Arrows", "ConditionalFormatIconSet_5Arrows_Label", "ConditionalFormatIconSet_Category_Directional", "I5"),
        Decorate("5ArrowsGray", "ConditionalFormatIconSet_5ArrowsGray_Label", "ConditionalFormatIconSet_Category_Directional", "IJ"),
        Decorate("3TrafficLights1", "ConditionalFormatIconSet_3TrafficLights1_Label", "ConditionalFormatIconSet_Category_Shapes", "IT"),
        Decorate("3TrafficLights2", "ConditionalFormatIconSet_3TrafficLights2_Label", "ConditionalFormatIconSet_Category_Shapes", "IR"),
        Decorate("3Signs", "ConditionalFormatIconSet_3Signs_Label", "ConditionalFormatIconSet_Category_Shapes", "IS"),
        Decorate("3Symbols", "ConditionalFormatIconSet_3Symbols_Label", "ConditionalFormatIconSet_Category_Shapes", "IY"),
        Decorate("3Symbols2", "ConditionalFormatIconSet_3Symbols2_Label", "ConditionalFormatIconSet_Category_Shapes", "IU"),
        Decorate("3Flags", "ConditionalFormatIconSet_3Flags_Label", "ConditionalFormatIconSet_Category_Shapes", "IF"),
        Decorate("4TrafficLights", "ConditionalFormatIconSet_4TrafficLights_Label", "ConditionalFormatIconSet_Category_Indicators", "IL"),
        Decorate("4RedToBlack", "ConditionalFormatIconSet_4RedToBlack_Label", "ConditionalFormatIconSet_Category_Indicators", "IB"),
        Decorate("4Rating", "ConditionalFormatIconSet_4Rating_Label", "ConditionalFormatIconSet_Category_Ratings", "I9"),
        Decorate("5Rating", "ConditionalFormatIconSet_5Rating_Label", "ConditionalFormatIconSet_Category_Ratings", "IA"),
        Decorate("5Quarters", "ConditionalFormatIconSet_5Quarters_Label", "ConditionalFormatIconSet_Category_Ratings", "IQ"),
        Decorate("5Boxes", "ConditionalFormatIconSet_5Boxes_Label", "ConditionalFormatIconSet_Category_Ratings", "IX")
    ];

    public static readonly IReadOnlyList<ConditionalFormatIconSetGalleryGroup> GalleryGroups =
        Options
            .GroupBy(option => option.Category)
            .Select(group => new ConditionalFormatIconSetGalleryGroup(group.Key, group.ToList()))
            .ToList();

    public static IReadOnlyList<string> Styles => Options.Select(option => option.Style).ToList();

    public static int GetIconCount(string? style) =>
        ConditionalFormatIconSetCatalog.GetIconCount(style);

    public static ConditionalFormat? CreateRule(string? style, GridRange range)
    {
        var option = FindOption(style);
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

    public static IReadOnlyList<CfThresholdModel> CreateThresholds(string? style) =>
        ConditionalFormatIconSetCatalog.CreateThresholds(style);

    private static ConditionalFormatIconSetOption Decorate(string style, string labelKey, string categoryKey, string keyTip) =>
        new(
            style,
            ConditionalFormatIconSetCatalog.GetIconCount(style),
            UiText.Get(labelKey),
            UiText.Get(categoryKey),
            keyTip);

    private static ConditionalFormatIconSetOption? FindOption(string? style)
    {
        foreach (var option in Options)
        {
            if (string.Equals(option.Style, style, StringComparison.Ordinal))
                return option;
        }

        return null;
    }
}
