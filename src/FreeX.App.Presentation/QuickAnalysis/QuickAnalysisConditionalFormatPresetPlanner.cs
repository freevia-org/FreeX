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
        if (QuickAnalysisConditionalFormatCatalog.TryForCommand(command, out var descriptor))
        {
            preset = descriptor.Preset;
            return true;
        }

        preset = default;
        return false;
    }
}
