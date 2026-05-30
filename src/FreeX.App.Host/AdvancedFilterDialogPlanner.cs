using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class AdvancedFilterDialogPlanner
{
    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool uniqueRecordsOnly,
        Func<string, SheetId?>? resolveSheetId,
        out AdvancedFilterDialogResult result,
        out string? error)
    {
        result = default!;
        error = null;
        resolveSheetId ??= _ => null;

        if (!AdvancedFilterInputParser.TryParseRange(currentSheetId, listRangeText, resolveSheetId, out var listRange))
        {
            error = UiText.Get("AdvancedFilter_EnterValidListRange");
            return false;
        }
        if (listRange.RowCount < 2)
        {
            error = UiText.Get("AdvancedFilter_ListRangeMustIncludeHeaders");
            return false;
        }

        if (!AdvancedFilterInputParser.TryParseRange(currentSheetId, criteriaRangeText, resolveSheetId, out var criteriaRange))
        {
            error = UiText.Get("AdvancedFilter_EnterValidCriteriaRange");
            return false;
        }
        if (criteriaRange.RowCount < 2)
        {
            error = UiText.Get("AdvancedFilter_CriteriaRangeMustIncludeHeaders");
            return false;
        }

        if (!AdvancedFilterInputParser.TryParseCopyDestinationRange(copyToCellText ?? "", currentSheetId, out var copyToRange))
        {
            error = UiText.Get("AdvancedFilter_EnterValidCopyToRange");
            return false;
        }

        result = new AdvancedFilterDialogResult(listRange, criteriaRange, copyToRange?.Start, uniqueRecordsOnly, copyToRange);
        return true;
    }

    public static AdvancedFilterRangeSelectionRequest CreateRangeSelectionRequest(
        AdvancedFilterRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);
}
