using FreeX.App.Presentation.PivotUI;

namespace FreeX.App.Host;

public sealed partial class PivotTableOptionsDialog
{
    private const string PageFieldLayoutDownThenOver = "Down, then over";
    private const string PageFieldLayoutOverThenDown = "Over, then down";
    private static readonly string[] PageFieldLayoutLabels = [PageFieldLayoutDownThenOver, PageFieldLayoutOverThenDown];

    private static bool PageFieldLayoutForLabel(string? label) =>
        string.Equals(label, PageFieldLayoutOverThenDown, StringComparison.OrdinalIgnoreCase);

    private const string MissingItemsAutomatic = "Automatic";
    private const string MissingItemsNone = "None";
    private const string MissingItemsMaximum = "Maximum";
    private static readonly string[] MissingItemsLimitLabels = [MissingItemsAutomatic, MissingItemsNone, MissingItemsMaximum];

    private static string LabelForMissingItemsLimit(int? value) =>
        PivotOptionsPlanner.NormalizeMissingItemsLimit(value) switch
        {
            null => MissingItemsAutomatic,
            <= 0 => MissingItemsNone,
            _ => MissingItemsMaximum
        };

    private static int? MissingItemsLimitForLabel(string? label) =>
        string.Equals(label, MissingItemsNone, StringComparison.OrdinalIgnoreCase)
            ? 0
            : string.Equals(label, MissingItemsMaximum, StringComparison.OrdinalIgnoreCase)
                ? PivotOptionsPlanner.MaxMissingItemsLimit
                : null;
}
