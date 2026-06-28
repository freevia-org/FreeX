using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// Maps Quick Analysis conditional-format intents to the shared conditional-format preset contract.
/// Platform shells still execute the preset through their own command paths.
/// </summary>
public static class QuickAnalysisConditionalFormatPresetPlanner
{
    public static bool TryResolve(
        QuickAnalysisConditionalFormatCommand command,
        out ConditionalFormatPreset preset)
    {
        preset = command switch
        {
            QuickAnalysisConditionalFormatCommand.DataBar => ConditionalFormatPreset.DataBar,
            QuickAnalysisConditionalFormatCommand.ColorScale => ConditionalFormatPreset.ColorScale,
            QuickAnalysisConditionalFormatCommand.IconSet => ConditionalFormatPreset.IconSet,
            QuickAnalysisConditionalFormatCommand.GreaterThan => ConditionalFormatPreset.HighlightGreaterThan,
            QuickAnalysisConditionalFormatCommand.LessThan => ConditionalFormatPreset.HighlightLessThan,
            QuickAnalysisConditionalFormatCommand.Between => ConditionalFormatPreset.HighlightBetween,
            QuickAnalysisConditionalFormatCommand.EqualTo => ConditionalFormatPreset.HighlightEqualTo,
            QuickAnalysisConditionalFormatCommand.TextContains => ConditionalFormatPreset.HighlightTextContains,
            QuickAnalysisConditionalFormatCommand.DateOccurring => ConditionalFormatPreset.HighlightDateOccurring,
            QuickAnalysisConditionalFormatCommand.DuplicateValues => ConditionalFormatPreset.HighlightDuplicateValues,
            QuickAnalysisConditionalFormatCommand.Top10Items => ConditionalFormatPreset.Top10,
            QuickAnalysisConditionalFormatCommand.Top10Percent => ConditionalFormatPreset.Top10Percent,
            QuickAnalysisConditionalFormatCommand.Bottom10Items => ConditionalFormatPreset.Bottom10Items,
            QuickAnalysisConditionalFormatCommand.Bottom10Percent => ConditionalFormatPreset.Bottom10Percent,
            QuickAnalysisConditionalFormatCommand.AboveAverage => ConditionalFormatPreset.AboveAverage,
            QuickAnalysisConditionalFormatCommand.BelowAverage => ConditionalFormatPreset.BelowAverage,
            _ => default,
        };

        return Enum.IsDefined(command);
    }
}
