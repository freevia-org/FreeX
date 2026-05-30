using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class ManageConditionalFormatsDialog
{
    public static IReadOnlyList<ConditionalFormat> BuildResultRules(
        IReadOnlyList<ConditionalFormat> sheetRules,
        GridRange? selection,
        bool filterToSelection,
        IReadOnlyList<ConditionalFormat> editedRules) =>
        ManageConditionalFormatsPlanner.BuildResultRules(sheetRules, selection, filterToSelection, editedRules);

    private static IReadOnlyList<ConditionalFormat> Reprioritize(IReadOnlyList<ConditionalFormat> rules) =>
        ManageConditionalFormatsPlanner.Reprioritize(rules);

    private static ConditionalFormat CloneWithPriority(ConditionalFormat src, int priority, Guid? id = null) =>
        ManageConditionalFormatsPlanner.CloneWithPriority(src, priority, id);

    private static bool RangesOverlap(GridRange a, GridRange b) =>
        ManageConditionalFormatsPlanner.RangesOverlap(a, b);

    public static ConditionalFormatAppliesToRangeSelectionRequest CreateAppliesToRangeSelectionRequest(
        Guid ruleId,
        string currentText) =>
        new(ruleId, currentText.Trim(), CollapseDialog: true);
}
