using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class AdvancedFilterDialog
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

        if (!AdvancedFilterInputParser.TryParseRange(currentSheetId, criteriaRangeText, resolveSheetId, out var criteriaRange))
        {
            error = "Enter a valid criteria range.";
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

    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool uniqueRecordsOnly,
        out AdvancedFilterDialogResult result,
        out string? error) =>
        TryParse(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToCellText,
            uniqueRecordsOnly,
            resolveSheetId: null,
            out result,
            out error);

    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool copyToAnotherLocation,
        bool uniqueRecordsOnly,
        Func<string, SheetId?>? resolveSheetId,
        out AdvancedFilterDialogResult result,
        out string? error) =>
        TryParse(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToAnotherLocation ? copyToCellText : "",
            uniqueRecordsOnly,
            resolveSheetId,
            out result,
            out error);

    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool copyToAnotherLocation,
        bool uniqueRecordsOnly,
        out AdvancedFilterDialogResult result,
        out string? error) =>
        TryParse(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToAnotherLocation ? copyToCellText : "",
            uniqueRecordsOnly,
            resolveSheetId: null,
            out result,
            out error);

    public static AdvancedFilterRangeSelectionRequest CreateRangeSelectionRequest(
        AdvancedFilterRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);
}
