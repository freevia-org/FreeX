using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
            error = "Enter a table range.";
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
                error = "Table range must include at least two rows.";
                return false;
            }

            result = new CreateTableDialogResult(range, firstRowHasHeaders, tableStyleName.Trim());
            return true;
        }
        catch (FormatException)
        {
            error = "Enter a valid table range.";
            return false;
        }
    }
}
