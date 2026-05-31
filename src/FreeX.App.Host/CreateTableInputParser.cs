using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class CreateTableInputParser
{
    public static bool TryParse(
        SheetId sheetId,
        string rangeText,
        bool firstRowHasHeaders,
        string tableStyleName,
        out CreateTableDialogResult result,
        out string? error)
    {
        result = default!;
        error = null;
        if (string.IsNullOrWhiteSpace(rangeText))
        {
            error = UiText.Get("CreateTable_MissingRangeMessage");
            return false;
        }

        try
        {
            var trimmedRangeText = rangeText.Trim();
            var range = trimmedRangeText.Contains(':', StringComparison.Ordinal)
                ? GridRange.Parse(trimmedRangeText, sheetId)
                : new GridRange(CellAddress.Parse(trimmedRangeText, sheetId), CellAddress.Parse(trimmedRangeText, sheetId));

            if (range.End.Row <= range.Start.Row)
            {
                error = UiText.Get("CreateTable_MinimumRowsMessage");
                return false;
            }

            result = new CreateTableDialogResult(range, firstRowHasHeaders, tableStyleName.Trim());
            return true;
        }
        catch (FormatException)
        {
            error = UiText.Get("CreateTable_InvalidRangeMessage");
            return false;
        }
    }
}
