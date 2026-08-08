using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>The quick conditional-format presets the ribbon dropdown / Format menu offers.</summary>
public enum ConditionalFormatPreset
{
    /// <summary>Blue gradient data bar with min/max thresholds.</summary>
    DataBar,

    /// <summary>Three-color green-yellow-red color scale.</summary>
    ColorScale,

    /// <summary>3 traffic-lights icon set.</summary>
    IconSet,

    /// <summary>Highlight cells greater than a value (Light Red Fill with Dark Red Text).</summary>
    HighlightGreaterThan,

    /// <summary>Top 10 items (Light Red Fill with Dark Red Text).</summary>
    Top10,

    // ── Highlight Cells Rules detail items (ribbon dropdown). Value-bearing presets seed sensible
    // defaults so the one-click ribbon path matches Top 10 Items / Greater Than (no prompt). ──
    HighlightLessThan,
    HighlightBetween,
    HighlightEqualTo,
    HighlightTextContains,
    HighlightDateOccurring,
    HighlightDuplicateValues,

    // ── Top/Bottom Rules detail items. ──
    Top10Percent,
    Bottom10Items,
    Bottom10Percent,
    AboveAverage,
    BelowAverage,
}

/// <summary>
/// Builds Core conditional-format rules for the quick presets, with sensible defaults, so the ribbon
/// "Conditional Formatting" dropdown and the native Format menu can apply them through the same
/// command path the editor uses. Pure (no UI), so it is unit testable.
/// </summary>
public static class ConditionalFormatPresetFactory
{
    /// <summary>The display label for a preset, as shown in the dropdown / menu.</summary>
    public static string DisplayName(ConditionalFormatPreset preset) =>
        preset switch
        {
            ConditionalFormatPreset.DataBar => "Data Bar",
            ConditionalFormatPreset.ColorScale => "Color Scale",
            ConditionalFormatPreset.IconSet => "Icon Set",
            ConditionalFormatPreset.HighlightGreaterThan => "Highlight Cells Rules > Greater Than…",
            ConditionalFormatPreset.Top10 => "Top 10 Items…",
            ConditionalFormatPreset.HighlightLessThan => "Highlight Cells Rules > Less Than…",
            ConditionalFormatPreset.HighlightBetween => "Highlight Cells Rules > Between…",
            ConditionalFormatPreset.HighlightEqualTo => "Highlight Cells Rules > Equal To…",
            ConditionalFormatPreset.HighlightTextContains => "Highlight Cells Rules > Text that Contains…",
            ConditionalFormatPreset.HighlightDateOccurring => "Highlight Cells Rules > A Date Occurring…",
            ConditionalFormatPreset.HighlightDuplicateValues => "Highlight Cells Rules > Duplicate Values…",
            ConditionalFormatPreset.Top10Percent => "Top 10%…",
            ConditionalFormatPreset.Bottom10Items => "Bottom 10 Items…",
            ConditionalFormatPreset.Bottom10Percent => "Bottom 10%…",
            ConditionalFormatPreset.AboveAverage => "Above Average",
            ConditionalFormatPreset.BelowAverage => "Below Average",
            _ => preset.ToString(),
        };

    /// <summary>
    /// Builds the rule input the preset applies. <paramref name="value"/> seeds the value-bearing
    /// presets (Greater Than's threshold, defaulting to <c>0</c>); it is ignored otherwise.
    /// </summary>
    public static CfRuleInput BuildInput(ConditionalFormatPreset preset, string? value = null) =>
        preset switch
        {
            ConditionalFormatPreset.DataBar => new CfRuleInput { RuleType = CfRuleType.DataBar },
            ConditionalFormatPreset.ColorScale => new CfRuleInput
            {
                RuleType = CfRuleType.ColorScale,
                UseThreeColorScale = true,
                MinColor = "99,190,123",
                MidColor = "255,235,132",
                MaxColor = "248,105,107",
            },
            ConditionalFormatPreset.IconSet => new CfRuleInput
            {
                RuleType = CfRuleType.IconSet,
                IconSetStyle = ConditionalFormatIconSetCatalog.DefaultStyle,
            },
            ConditionalFormatPreset.HighlightGreaterThan => new CfRuleInput
            {
                RuleType = CfRuleType.CellValue,
                Operator = CfOperator.GreaterThan,
                Value1 = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim(),
            },
            ConditionalFormatPreset.Top10 => new CfRuleInput
            {
                RuleType = CfRuleType.Top10,
                Rank = "10",
                IsPercent = false,
            },
            ConditionalFormatPreset.HighlightLessThan => new CfRuleInput
            {
                RuleType = CfRuleType.CellValue,
                Operator = CfOperator.LessThan,
                Value1 = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim(),
            },
            ConditionalFormatPreset.HighlightBetween => new CfRuleInput
            {
                RuleType = CfRuleType.CellValue,
                Operator = CfOperator.Between,
                Value1 = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim(),
                Value2 = "100",
            },
            ConditionalFormatPreset.HighlightEqualTo => new CfRuleInput
            {
                RuleType = CfRuleType.CellValue,
                Operator = CfOperator.Equal,
                Value1 = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim(),
            },
            ConditionalFormatPreset.HighlightTextContains => new CfRuleInput
            {
                RuleType = CfRuleType.ContainsText,
                Text = string.IsNullOrWhiteSpace(value) ? "a" : value.Trim(),
            },
            ConditionalFormatPreset.HighlightDateOccurring => new CfRuleInput
            {
                RuleType = CfRuleType.DateOccurring,
                Text = string.IsNullOrWhiteSpace(value) ? "today" : value.Trim(),
            },
            ConditionalFormatPreset.HighlightDuplicateValues => new CfRuleInput
            {
                RuleType = CfRuleType.DuplicateValues,
            },
            ConditionalFormatPreset.Top10Percent => new CfRuleInput
            {
                RuleType = CfRuleType.Top10,
                Rank = "10",
                IsPercent = true,
                IsTop = true,
            },
            ConditionalFormatPreset.Bottom10Items => new CfRuleInput
            {
                RuleType = CfRuleType.Top10,
                Rank = "10",
                IsPercent = false,
                IsTop = false,
            },
            ConditionalFormatPreset.Bottom10Percent => new CfRuleInput
            {
                RuleType = CfRuleType.Top10,
                Rank = "10",
                IsPercent = true,
                IsTop = false,
            },
            ConditionalFormatPreset.AboveAverage => new CfRuleInput
            {
                RuleType = CfRuleType.AboveAverage,
            },
            ConditionalFormatPreset.BelowAverage => new CfRuleInput
            {
                RuleType = CfRuleType.AboveAverage,
            },
            _ => new CfRuleInput { RuleType = CfRuleType.CellValue },
        };

    /// <summary>Builds the Core rule the preset applies over the given range.</summary>
    public static ConditionalFormat BuildRule(
        ConditionalFormatPreset preset,
        GridRange range,
        string? value = null)
    {
        var rule = ConditionalFormatRuleBuilder.Build(BuildInput(preset, value), range);

        // The rule builder has no AboveAverage input field (the model reuses the AboveAverage bool for
        // direction), so set the Below-Average direction here. Above-Average is the model default.
        if (preset == ConditionalFormatPreset.BelowAverage)
            rule.AboveAverage = false;

        return rule;
    }

    /// <summary>Builds the add command the preset applies through the session command path.</summary>
    public static ApplyConditionalFormatCommand BuildApplyCommand(
        ConditionalFormatPreset preset,
        SheetId sheetId,
        GridRange range,
        string? value = null) =>
        ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, BuildRule(preset, range, value));

    /// <summary>
    /// Maps a Home ▸ Conditional Formatting ▸ Icon Sets ribbon menu id (e.g. "3 Arrows", "5 Boxes")
    /// to its <see cref="ConditionalFormatIconSetCatalog"/> style id, or <c>null</c> when the id is not
    /// a known icon-set menu item.
    /// </summary>
    public static string? IconSetStyleForMenuId(string menuId) => menuId switch
    {
        "3 Arrows" => "3Arrows",
        "3 Arrows (Gray)" => "3ArrowsGray",
        "4 Arrows" => "4Arrows",
        "4 Arrows (Gray)" => "4ArrowsGray",
        "5 Arrows" => "5Arrows",
        "5 Arrows (Gray)" => "5ArrowsGray",
        "3 Traffic Lights" => "3TrafficLights1",
        "3 Traffic Lights (Rimmed)" => "3TrafficLights2",
        "3 Signs" => "3Signs",
        "3 Symbols" => "3Symbols",
        "3 Symbols (Uncircled)" => "3Symbols2",
        "3 Flags" => "3Flags",
        "4 Traffic Lights" => "4TrafficLights",
        "4 Red To Black" => "4RedToBlack",
        "4 Ratings" => "4Rating",
        "5 Ratings" => "5Rating",
        "5 Quarters" => "5Quarters",
        "5 Boxes" => "5Boxes",
        _ => null,
    };

    /// <summary>Builds the Core icon-set rule the ribbon icon-set gallery applies over the given range.</summary>
    public static ConditionalFormat BuildIconSetRule(string iconSetStyle, GridRange range) =>
        ConditionalFormatRuleBuilder.Build(
            new CfRuleInput { RuleType = CfRuleType.IconSet, IconSetStyle = iconSetStyle },
            range);

    /// <summary>Builds the add command for an icon-set rule of the given catalog style.</summary>
    public static ApplyConditionalFormatCommand BuildIconSetApplyCommand(
        string iconSetStyle,
        SheetId sheetId,
        GridRange range) =>
        ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, BuildIconSetRule(iconSetStyle, range));
}
