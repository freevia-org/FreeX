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
    public static readonly IReadOnlyList<ConditionalFormatIconSetOption> Options =
    [
        new("3Arrows", 3, UiText.Get("ConditionalFormatIconSet_3Arrows_Label"), UiText.Get("ConditionalFormatIconSet_Category_Directional"), "I3"),
        new("3ArrowsGray", 3, UiText.Get("ConditionalFormatIconSet_3ArrowsGray_Label"), UiText.Get("ConditionalFormatIconSet_Category_Directional"), "IG"),
        new("4Arrows", 4, UiText.Get("ConditionalFormatIconSet_4Arrows_Label"), UiText.Get("ConditionalFormatIconSet_Category_Directional"), "I4"),
        new("4ArrowsGray", 4, UiText.Get("ConditionalFormatIconSet_4ArrowsGray_Label"), UiText.Get("ConditionalFormatIconSet_Category_Directional"), "IH"),
        new("5Arrows", 5, UiText.Get("ConditionalFormatIconSet_5Arrows_Label"), UiText.Get("ConditionalFormatIconSet_Category_Directional"), "I5"),
        new("5ArrowsGray", 5, UiText.Get("ConditionalFormatIconSet_5ArrowsGray_Label"), UiText.Get("ConditionalFormatIconSet_Category_Directional"), "IJ"),
        new("3TrafficLights1", 3, UiText.Get("ConditionalFormatIconSet_3TrafficLights1_Label"), UiText.Get("ConditionalFormatIconSet_Category_Shapes"), "IT"),
        new("3TrafficLights2", 3, UiText.Get("ConditionalFormatIconSet_3TrafficLights2_Label"), UiText.Get("ConditionalFormatIconSet_Category_Shapes"), "IR"),
        new("3Signs", 3, UiText.Get("ConditionalFormatIconSet_3Signs_Label"), UiText.Get("ConditionalFormatIconSet_Category_Shapes"), "IS"),
        new("3Symbols", 3, UiText.Get("ConditionalFormatIconSet_3Symbols_Label"), UiText.Get("ConditionalFormatIconSet_Category_Shapes"), "IY"),
        new("3Symbols2", 3, UiText.Get("ConditionalFormatIconSet_3Symbols2_Label"), UiText.Get("ConditionalFormatIconSet_Category_Shapes"), "IU"),
        new("3Flags", 3, UiText.Get("ConditionalFormatIconSet_3Flags_Label"), UiText.Get("ConditionalFormatIconSet_Category_Shapes"), "IF"),
        new("4TrafficLights", 4, UiText.Get("ConditionalFormatIconSet_4TrafficLights_Label"), UiText.Get("ConditionalFormatIconSet_Category_Indicators"), "IL"),
        new("4RedToBlack", 4, UiText.Get("ConditionalFormatIconSet_4RedToBlack_Label"), UiText.Get("ConditionalFormatIconSet_Category_Indicators"), "IB"),
        new("4Rating", 4, UiText.Get("ConditionalFormatIconSet_4Rating_Label"), UiText.Get("ConditionalFormatIconSet_Category_Ratings"), "I9"),
        new("5Rating", 5, UiText.Get("ConditionalFormatIconSet_5Rating_Label"), UiText.Get("ConditionalFormatIconSet_Category_Ratings"), "IA"),
        new("5Quarters", 5, UiText.Get("ConditionalFormatIconSet_5Quarters_Label"), UiText.Get("ConditionalFormatIconSet_Category_Ratings"), "IQ"),
        new("5Boxes", 5, UiText.Get("ConditionalFormatIconSet_5Boxes_Label"), UiText.Get("ConditionalFormatIconSet_Category_Ratings"), "IX")
    ];

    public static readonly IReadOnlyList<ConditionalFormatIconSetGalleryGroup> GalleryGroups =
        Options
            .GroupBy(option => option.Category)
            .Select(group => new ConditionalFormatIconSetGalleryGroup(group.Key, group.ToList()))
            .ToList();

    public static IReadOnlyList<string> Styles => Options.Select(option => option.Style).ToList();

    public static int GetIconCount(string? style) =>
        Options.FirstOrDefault(option => string.Equals(option.Style, style, StringComparison.Ordinal))?.IconCount ?? 3;

    public static ConditionalFormat? CreateRule(string? style, GridRange range)
    {
        var option = Options.FirstOrDefault(option => string.Equals(option.Style, style, StringComparison.Ordinal));
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

    public static IReadOnlyList<CfThresholdModel> CreateThresholds(string? style)
    {
        var iconCount = GetIconCount(style);
        if (iconCount <= 3)
        {
            return
            [
                new CfThresholdModel(CfThresholdType.Percent, "0"),
                new CfThresholdModel(CfThresholdType.Percent, "33"),
                new CfThresholdModel(CfThresholdType.Percent, "67")
            ];
        }

        var step = 100 / iconCount;
        return Enumerable.Range(0, iconCount)
            .Select(index => new CfThresholdModel(CfThresholdType.Percent, (index * step).ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
    }
}
