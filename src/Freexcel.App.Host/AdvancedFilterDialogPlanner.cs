using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
            error = "Enter a valid list range.";
            return false;
        }
        if (listRange.RowCount < 2)
        {
            error = "List range must include headers and at least one data row.";
            return false;
        }

        if (!AdvancedFilterInputParser.TryParseRange(currentSheetId, criteriaRangeText, resolveSheetId, out var criteriaRange))
        {
            error = "Enter a valid criteria range.";
            return false;
        }
        if (criteriaRange.RowCount < 2)
        {
            error = "Criteria range must include headers and at least one criteria row.";
            return false;
        }

        if (!AdvancedFilterInputParser.TryParseCopyDestinationRange(copyToCellText ?? "", currentSheetId, out var copyToRange))
        {
            error = "Enter a valid copy-to cell or one-row header range.";
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
