using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        new("3Arrows", 3, "3 Arrows", "Directional", "I3"),
        new("3ArrowsGray", 3, "3 Arrows (Gray)", "Directional", "IG"),
        new("4Arrows", 4, "4 Arrows", "Directional", "I4"),
        new("4ArrowsGray", 4, "4 Arrows (Gray)", "Directional", "IH"),
        new("5Arrows", 5, "5 Arrows", "Directional", "I5"),
        new("5ArrowsGray", 5, "5 Arrows (Gray)", "Directional", "IJ"),
        new("3TrafficLights1", 3, "3 Traffic Lights", "Shapes", "IT"),
        new("3TrafficLights2", 3, "3 Traffic Lights (Rimmed)", "Shapes", "IR"),
        new("3Signs", 3, "3 Signs", "Shapes", "IS"),
        new("3Symbols", 3, "3 Symbols", "Shapes", "IY"),
        new("3Symbols2", 3, "3 Symbols (Uncircled)", "Shapes", "IU"),
        new("3Flags", 3, "3 Flags", "Shapes", "IF"),
        new("4TrafficLights", 4, "4 Traffic Lights", "Indicators", "IL"),
        new("4RedToBlack", 4, "4 Red To Black", "Indicators", "IB"),
        new("4Rating", 4, "4 Ratings", "Ratings", "I9"),
        new("5Rating", 5, "5 Ratings", "Ratings", "IA"),
        new("5Quarters", 5, "5 Quarters", "Ratings", "IQ"),
        new("5Boxes", 5, "5 Boxes", "Ratings", "IX")
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
