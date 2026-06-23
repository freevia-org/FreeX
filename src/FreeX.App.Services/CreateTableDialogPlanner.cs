using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record CreateTableDialogPlan(GridRange Range, bool FirstRowHasHeaders, string TableStyleName);

public static class CreateTableDialogPlanner
{
    public const string TitleKey = "CreateTable_Title";
    public const string RangeLabelKey = "CreateTable_RangeLabel";
    public const string RangeAutomationNameKey = "CreateTable_RangeAutomationName";
    public const string RangeAutomationHelpTextKey = "CreateTable_RangeAutomationHelpText";
    public const string RangePickerAutomationNameKey = "CreateTable_RangePickerAutomationName";
    public const string HeadersCheckBoxKey = "CreateTable_HeadersCheckBox";
    public const string HeadersAutomationNameKey = "CreateTable_HeadersAutomationName";
    public const string HeadersAutomationHelpTextKey = "CreateTable_HeadersAutomationHelpText";
    public const string MissingRangeMessageKey = "CreateTable_MissingRangeMessage";
    public const string MinimumRowsMessageKey = "CreateTable_MinimumRowsMessage";
    public const string InvalidRangeMessageKey = "CreateTable_InvalidRangeMessage";

    public const string DialogAutomationId = "CreateTableDialog";
    public const string RangeBoxAutomationId = "CreateTableRangeBox";
    public const string HeadersBoxAutomationId = "CreateTableHeadersBox";

    public const double Width = 360;
    public const double Height = 190;
    public const double ButtonWidth = 76;

    public static bool TryParse(
        SheetId sheetId,
        string rangeText,
        bool firstRowHasHeaders,
        string tableStyleName,
        out CreateTableDialogPlan plan,
        out string? errorKey)
    {
        plan = default!;
        errorKey = null;
        if (string.IsNullOrWhiteSpace(rangeText))
        {
            errorKey = MissingRangeMessageKey;
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
                errorKey = MinimumRowsMessageKey;
                return false;
            }

            plan = new CreateTableDialogPlan(range, firstRowHasHeaders, tableStyleName.Trim());
            return true;
        }
        catch (FormatException)
        {
            errorKey = InvalidRangeMessageKey;
            return false;
        }
    }
}
