using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

public sealed record ConditionalFormatDataBarPreset(
    string Style,
    string LabelKey,
    string CategoryKey,
    string KeyTip,
    RgbColor Color,
    bool Gradient);

public sealed record ConditionalFormatColorScalePreset(
    string Style,
    string LabelKey,
    string CategoryKey,
    string KeyTip,
    RgbColor MinColor,
    RgbColor? MidColor,
    RgbColor MaxColor);

public sealed record ConditionalFormatPresetGalleryGroup<TPreset>(
    string CategoryKey,
    IReadOnlyList<TPreset> Options);

public enum ConditionalFormatPopupCatalogItemKind
{
    Preset,
    DataBarGallery,
    ColorScaleGallery,
    IconSetGallery,
    RuleDialog,
    ClearRules,
    ManageRules,
}

public sealed record ConditionalFormatPopupCatalogItem(
    string CommandId,
    ConditionalFormatPopupCatalogItemKind Kind,
    ConditionalFormatPreset? Preset = null,
    string? IconSetStyle = null);

public sealed record ConditionalFormatPopupCatalogGroup(
    string Name,
    IReadOnlyList<ConditionalFormatPopupCatalogItem> Items);

public static class ConditionalFormatPresetGalleryPlanner
{
    public static readonly IReadOnlyList<ConditionalFormatDataBarPreset> DataBarOptions =
    [
        DataBar("GradientBlue", "ConditionalFormatDataBar_Blue_Label", DataBarGradientCategory, "DB", 99, 142, 198, gradient: true),
        DataBar("GradientGreen", "ConditionalFormatDataBar_Green_Label", DataBarGradientCategory, "DG", 99, 190, 123, gradient: true),
        DataBar("GradientRed", "ConditionalFormatDataBar_Red_Label", DataBarGradientCategory, "DR", 248, 105, 107, gradient: true),
        DataBar("GradientOrange", "ConditionalFormatDataBar_Orange_Label", DataBarGradientCategory, "DO", 255, 182, 40, gradient: true),
        DataBar("GradientLightBlue", "ConditionalFormatDataBar_LightBlue_Label", DataBarGradientCategory, "DL", 91, 155, 213, gradient: true),
        DataBar("GradientPurple", "ConditionalFormatDataBar_Purple_Label", DataBarGradientCategory, "DP", 128, 100, 162, gradient: true),
        DataBar("SolidBlue", "ConditionalFormatDataBar_Blue_Label", DataBarSolidCategory, "SB", 99, 142, 198, gradient: false),
        DataBar("SolidGreen", "ConditionalFormatDataBar_Green_Label", DataBarSolidCategory, "SG", 99, 190, 123, gradient: false),
        DataBar("SolidRed", "ConditionalFormatDataBar_Red_Label", DataBarSolidCategory, "SR", 248, 105, 107, gradient: false),
        DataBar("SolidOrange", "ConditionalFormatDataBar_Orange_Label", DataBarSolidCategory, "SO", 255, 182, 40, gradient: false),
        DataBar("SolidLightBlue", "ConditionalFormatDataBar_LightBlue_Label", DataBarSolidCategory, "SL", 91, 155, 213, gradient: false),
        DataBar("SolidPurple", "ConditionalFormatDataBar_Purple_Label", DataBarSolidCategory, "SP", 128, 100, 162, gradient: false)
    ];

    public static readonly IReadOnlyList<ConditionalFormatColorScalePreset> ColorScaleOptions =
    [
        ColorScale("GreenYellowRed", "ConditionalFormatColorScale_GreenYellowRed_Label", ColorScaleThreeColorCategory, "C1", 99, 190, 123, 255, 235, 132, 248, 105, 107),
        ColorScale("RedYellowGreen", "ConditionalFormatColorScale_RedYellowGreen_Label", ColorScaleThreeColorCategory, "C2", 248, 105, 107, 255, 235, 132, 99, 190, 123),
        ColorScale("GreenWhiteRed", "ConditionalFormatColorScale_GreenWhiteRed_Label", ColorScaleThreeColorCategory, "C3", 99, 190, 123, 255, 255, 255, 248, 105, 107),
        ColorScale("RedWhiteGreen", "ConditionalFormatColorScale_RedWhiteGreen_Label", ColorScaleThreeColorCategory, "C4", 248, 105, 107, 255, 255, 255, 99, 190, 123),
        ColorScale("BlueWhiteRed", "ConditionalFormatColorScale_BlueWhiteRed_Label", ColorScaleThreeColorCategory, "C5", 91, 155, 213, 255, 255, 255, 248, 105, 107),
        ColorScale("RedWhiteBlue", "ConditionalFormatColorScale_RedWhiteBlue_Label", ColorScaleThreeColorCategory, "C6", 248, 105, 107, 255, 255, 255, 91, 155, 213),
        ColorScale("WhiteRed", "ConditionalFormatColorScale_WhiteRed_Label", ColorScaleTwoColorCategory, "CR", 255, 255, 255, null, null, null, 248, 105, 107),
        ColorScale("RedWhite", "ConditionalFormatColorScale_RedWhite_Label", ColorScaleTwoColorCategory, "CW", 248, 105, 107, null, null, null, 255, 255, 255),
        ColorScale("GreenWhite", "ConditionalFormatColorScale_GreenWhite_Label", ColorScaleTwoColorCategory, "CG", 99, 190, 123, null, null, null, 255, 255, 255),
        ColorScale("WhiteGreen", "ConditionalFormatColorScale_WhiteGreen_Label", ColorScaleTwoColorCategory, "CH", 255, 255, 255, null, null, null, 99, 190, 123)
    ];

    public static readonly IReadOnlyList<ConditionalFormatPopupCatalogGroup> PopupGroups =
    [
        new(
            "Highlight Cells Rules",
            [
                PopupPreset("Greater Than", ConditionalFormatPreset.HighlightGreaterThan),
                PopupPreset("Less Than", ConditionalFormatPreset.HighlightLessThan),
                PopupPreset("Between", ConditionalFormatPreset.HighlightBetween),
                PopupPreset("Equal To", ConditionalFormatPreset.HighlightEqualTo),
                PopupPreset("Text that Contains", ConditionalFormatPreset.HighlightTextContains),
                PopupPreset("A Date Occurring", ConditionalFormatPreset.HighlightDateOccurring),
                PopupPreset("Duplicate Values", ConditionalFormatPreset.HighlightDuplicateValues),
            ]),
        new(
            "Top/Bottom Rules",
            [
                PopupPreset("Top 10 Items", ConditionalFormatPreset.Top10),
                PopupPreset("Top 10%", ConditionalFormatPreset.Top10Percent),
                PopupPreset("Bottom 10 Items", ConditionalFormatPreset.Bottom10Items),
                PopupPreset("Bottom 10%", ConditionalFormatPreset.Bottom10Percent),
                PopupPreset("Above Average", ConditionalFormatPreset.AboveAverage),
                PopupPreset("Below Average", ConditionalFormatPreset.BelowAverage),
            ]),
        new(
            "Gallery Families",
            [
                new("Data Bars", ConditionalFormatPopupCatalogItemKind.DataBarGallery),
                new("Color Scales", ConditionalFormatPopupCatalogItemKind.ColorScaleGallery),
            ]),
        new(
            "Icon Sets",
            [
                PopupIconSet("3 Arrows", "3Arrows"),
                PopupIconSet("3 Arrows (Gray)", "3ArrowsGray"),
                PopupIconSet("4 Arrows", "4Arrows"),
                PopupIconSet("4 Arrows (Gray)", "4ArrowsGray"),
                PopupIconSet("5 Arrows", "5Arrows"),
                PopupIconSet("5 Arrows (Gray)", "5ArrowsGray"),
                PopupIconSet("3 Traffic Lights", "3TrafficLights1"),
                PopupIconSet("3 Traffic Lights (Rimmed)", "3TrafficLights2"),
                PopupIconSet("3 Signs", "3Signs"),
                PopupIconSet("3 Symbols", "3Symbols"),
                PopupIconSet("3 Symbols (Uncircled)", "3Symbols2"),
                PopupIconSet("3 Flags", "3Flags"),
                PopupIconSet("4 Traffic Lights", "4TrafficLights"),
                PopupIconSet("4 Red To Black", "4RedToBlack"),
                PopupIconSet("4 Ratings", "4Rating"),
                PopupIconSet("5 Ratings", "5Rating"),
                PopupIconSet("5 Quarters", "5Quarters"),
                PopupIconSet("5 Boxes", "5Boxes"),
            ]),
        new(
            "Rules",
            [
                new("More Rules", ConditionalFormatPopupCatalogItemKind.RuleDialog),
                new("New Rule", ConditionalFormatPopupCatalogItemKind.RuleDialog),
                new("New Formula Rule", ConditionalFormatPopupCatalogItemKind.RuleDialog),
                new("Clear Rules", ConditionalFormatPopupCatalogItemKind.ClearRules),
                new("Manage Rules", ConditionalFormatPopupCatalogItemKind.ManageRules),
            ]),
    ];

    public static IReadOnlyList<ConditionalFormatPopupCatalogItem> PopupItems =>
        PopupGroups.SelectMany(group => group.Items).ToArray();

    public static IReadOnlyList<ConditionalFormatPresetGalleryGroup<ConditionalFormatDataBarPreset>> DataBarGroups =>
        DataBarOptions
            .GroupBy(option => option.CategoryKey)
            .Select(group => new ConditionalFormatPresetGalleryGroup<ConditionalFormatDataBarPreset>(group.Key, group.ToArray()))
            .ToArray();

    public static IReadOnlyList<ConditionalFormatPresetGalleryGroup<ConditionalFormatColorScalePreset>> ColorScaleGroups =>
        ColorScaleOptions
            .GroupBy(option => option.CategoryKey)
            .Select(group => new ConditionalFormatPresetGalleryGroup<ConditionalFormatColorScalePreset>(group.Key, group.ToArray()))
            .ToArray();

    public static ConditionalFormat? CreateDataBarRule(string? style, GridRange range)
    {
        var option = FindDataBarOption(style);
        if (option is null)
            return null;

        return new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.DataBar,
            DataBarColor = option.Color,
            DataBarGradient = option.Gradient,
            DataBarShowValue = true
        };
    }

    public static ConditionalFormat? CreateColorScaleRule(string? style, GridRange range)
    {
        var option = FindColorScaleOption(style);
        if (option is null)
            return null;

        return new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.ColorScale,
            MinColor = option.MinColor,
            MidColor = option.MidColor ?? new RgbColor(255, 235, 132),
            MaxColor = option.MaxColor,
            UseThreeColorScale = option.MidColor is not null
        };
    }

    private static ConditionalFormatDataBarPreset DataBar(
        string style,
        string labelKey,
        string categoryKey,
        string keyTip,
        byte red,
        byte green,
        byte blue,
        bool gradient) =>
        new(style, labelKey, categoryKey, keyTip, new RgbColor(red, green, blue), gradient);

    private static ConditionalFormatColorScalePreset ColorScale(
        string style,
        string labelKey,
        string categoryKey,
        string keyTip,
        byte minRed,
        byte minGreen,
        byte minBlue,
        byte? midRed,
        byte? midGreen,
        byte? midBlue,
        byte maxRed,
        byte maxGreen,
        byte maxBlue) =>
        new(
            style,
            labelKey,
            categoryKey,
            keyTip,
            new RgbColor(minRed, minGreen, minBlue),
            midRed is null || midGreen is null || midBlue is null ? null : new RgbColor(midRed.Value, midGreen.Value, midBlue.Value),
            new RgbColor(maxRed, maxGreen, maxBlue));

    private static ConditionalFormatPopupCatalogItem PopupPreset(
        string commandId,
        ConditionalFormatPreset preset) =>
        new(commandId, ConditionalFormatPopupCatalogItemKind.Preset, preset);

    private static ConditionalFormatPopupCatalogItem PopupIconSet(
        string commandId,
        string style) =>
        new(commandId, ConditionalFormatPopupCatalogItemKind.IconSetGallery, IconSetStyle: style);

    private static ConditionalFormatDataBarPreset? FindDataBarOption(string? style)
    {
        foreach (var option in DataBarOptions)
        {
            if (string.Equals(option.Style, style, StringComparison.Ordinal))
                return option;
        }

        return null;
    }

    private static ConditionalFormatColorScalePreset? FindColorScaleOption(string? style)
    {
        foreach (var option in ColorScaleOptions)
        {
            if (string.Equals(option.Style, style, StringComparison.Ordinal))
                return option;
        }

        return null;
    }

    private const string DataBarGradientCategory = "ConditionalFormatDataBar_Category_GradientFill";
    private const string DataBarSolidCategory = "ConditionalFormatDataBar_Category_SolidFill";
    private const string ColorScaleThreeColorCategory = "ConditionalFormatColorScale_Category_ThreeColor";
    private const string ColorScaleTwoColorCategory = "ConditionalFormatColorScale_Category_TwoColor";
}
