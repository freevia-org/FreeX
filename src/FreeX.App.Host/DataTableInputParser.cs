using FreeX.App.Presentation;
using FreeX.Core.Model;
using SharedDataTableInputParseIssue = FreeX.App.Presentation.DataTools.DataTableInputParseIssue;
using SharedDataTableInputParser = FreeX.App.Presentation.DataTools.DataTableInputParser;

namespace FreeX.App.Host;

public static class DataTableInputParser
{
    public static bool TryParse(
        SheetId currentSheetId,
        GridRange range,
        string? rowInputCellText,
        string? columnInputCellText,
        out DataTableDialogResult result,
        out string? error)
    {
        if (SharedDataTableInputParser.TryParse(
                currentSheetId,
                range,
                rowInputCellText,
                columnInputCellText,
                out var parsed,
                out var issue))
        {
            result = parsed;
            error = null;
            return true;
        }

        result = default!;
        error = DescribeIssue(issue);
        return false;
    }

    internal static string? DescribeIssue(SharedDataTableInputParseIssue issue) =>
        issue switch
        {
            SharedDataTableInputParseIssue.InvalidRowInputCell => UiText.Get("DataTable_InvalidRowInputMessage"),
            SharedDataTableInputParseIssue.InvalidColumnInputCell => UiText.Get("DataTable_InvalidColumnInputMessage"),
            SharedDataTableInputParseIssue.MissingInputCell => UiText.Get("DataTable_MissingInputMessage"),
            SharedDataTableInputParseIssue.RowInputCellInsideTableRange => UiText.Get("DataTable_RowInputInsideRangeMessage"),
            SharedDataTableInputParseIssue.ColumnInputCellInsideTableRange => UiText.Get("DataTable_ColumnInputInsideRangeMessage"),
            SharedDataTableInputParseIssue.InputCellsMustBeDifferent => UiText.Get("DataTable_SameInputCellMessage"),
            _ => null
        };
}
