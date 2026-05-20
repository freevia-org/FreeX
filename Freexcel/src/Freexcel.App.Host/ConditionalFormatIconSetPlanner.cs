using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ConditionalFormatIconSetOption(string Style, int IconCount);

public static class ConditionalFormatIconSetPlanner
{
    public static readonly IReadOnlyList<ConditionalFormatIconSetOption> Options =
    [
        new("3Arrows", 3),
        new("3ArrowsGray", 3),
        new("3TrafficLights1", 3),
        new("3TrafficLights2", 3),
        new("3Signs", 3),
        new("3Symbols", 3),
        new("3Symbols2", 3),
        new("3Flags", 3),
        new("4Arrows", 4),
        new("4ArrowsGray", 4),
        new("4RedToBlack", 4),
        new("4Rating", 4),
        new("4TrafficLights", 4),
        new("5Arrows", 5),
        new("5ArrowsGray", 5),
        new("5Rating", 5),
        new("5Quarters", 5),
        new("5Boxes", 5)
    ];

    public static IReadOnlyList<string> Styles => Options.Select(option => option.Style).ToList();

    public static int GetIconCount(string? style) =>
        Options.FirstOrDefault(option => string.Equals(option.Style, style, StringComparison.Ordinal))?.IconCount ?? 3;

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
