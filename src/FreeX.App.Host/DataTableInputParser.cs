using FreeX.Core.Model;
using FreeX.Core.Commands;
using SharedDataTableInputMode = FreeX.App.Presentation.DataTools.DataTableInputMode;
using SharedDataTableInputParseIssue = FreeX.App.Presentation.DataTools.DataTableInputParseIssue;
using SharedDataTableInputParser = FreeX.App.Presentation.DataTools.DataTableInputParser;

namespace FreeX.App.Host;

public static class DataTableInputParser
{
    public static bool IsTwoVariableMode(string input) =>
        SharedDataTableInputParser.IsTwoVariableMode(input);

    public static CellAddress GetDefaultFormulaCell(GridRange range, bool twoVariable) =>
        GetDefaultFormulaCell(range, DataTableInputOrientation.Column, twoVariable);

    public static CellAddress GetDefaultFormulaCell(
        GridRange range,
        DataTableInputOrientation orientation,
        bool twoVariable = false) =>
        SharedDataTableInputParser.GetDefaultFormulaCell(range, orientation, twoVariable);

    public static bool TryParseCell(string input, SheetId sheetId, out CellAddress address) =>
        SharedDataTableInputParser.TryParseCell(input, sheetId, out address);

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
            result = new DataTableDialogResult(
                ToHostMode(parsed.Mode),
                parsed.Orientation,
                parsed.FormulaCell,
                parsed.RowInputCell,
                parsed.ColumnInputCell);
            error = null;
            return true;
        }

        result = default!;
        error = DescribeIssue(issue);
        return false;
    }

    private static DataTableMode ToHostMode(SharedDataTableInputMode mode) =>
        mode == SharedDataTableInputMode.TwoVariable
            ? DataTableMode.TwoVariable
            : DataTableMode.OneVariable;

    private static string? DescribeIssue(SharedDataTableInputParseIssue issue) =>
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
