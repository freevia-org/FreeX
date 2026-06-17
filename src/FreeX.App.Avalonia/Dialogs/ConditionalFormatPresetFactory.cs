using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Dialogs;

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
            _ => new CfRuleInput { RuleType = CfRuleType.CellValue },
        };

    /// <summary>Builds the Core rule the preset applies over the given range.</summary>
    public static ConditionalFormat BuildRule(
        ConditionalFormatPreset preset,
        GridRange range,
        string? value = null) =>
        ConditionalFormatRuleBuilder.Build(BuildInput(preset, value), range);

    /// <summary>Builds the add command the preset applies through the session command path.</summary>
    public static ApplyConditionalFormatCommand BuildApplyCommand(
        ConditionalFormatPreset preset,
        SheetId sheetId,
        GridRange range,
        string? value = null) =>
        ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, BuildRule(preset, range, value));
}
