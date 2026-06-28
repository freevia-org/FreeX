using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Presentation;

namespace FreeX.App.Presentation.DataTools;

public enum DataTableInputParseIssue
{
    None,
    MissingInputCell,
    InvalidRowInputCell,
    InvalidColumnInputCell,
    RowInputCellInsideTableRange,
    ColumnInputCellInsideTableRange,
    InputCellsMustBeDifferent
}

public static class DataTableInputParser
{
    public static bool IsTwoVariableMode(string? input)
    {
        var normalized = input?.Trim() ?? "";
        return normalized.Equals("two", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("2", StringComparison.OrdinalIgnoreCase);
    }

    public static CellAddress GetDefaultFormulaCell(GridRange range, bool twoVariable) =>
        GetDefaultFormulaCell(range, DataTableInputOrientation.Column, twoVariable);

    public static CellAddress GetDefaultFormulaCell(
        GridRange range,
        DataTableInputOrientation orientation,
        bool twoVariable = false) =>
        new(
            range.Start.Sheet,
            twoVariable || orientation == DataTableInputOrientation.Column ? range.Start.Row : range.Start.Row + 1,
            twoVariable || orientation == DataTableInputOrientation.Row ? range.Start.Col : range.Start.Col + 1);

    public static bool TryParseCell(string input, SheetId sheetId, out CellAddress address) =>
        CellReferenceInputParser.TryParseCell(input, sheetId, out address);

    public static DataTableRangeSelectionRequest CreateRangeSelectionRequest(
        DataTableRangeSelectionTarget target,
        string? currentText) =>
        new(target, NormalizeInput(currentText), CollapseDialog: true);

    public static DataTableRangeSelectionTarget GetErrorFocusTarget(DataTableInputParseIssue issue) =>
        issue switch
        {
            DataTableInputParseIssue.InvalidColumnInputCell => DataTableRangeSelectionTarget.ColumnInputCell,
            DataTableInputParseIssue.ColumnInputCellInsideTableRange => DataTableRangeSelectionTarget.ColumnInputCell,
            DataTableInputParseIssue.InputCellsMustBeDifferent => DataTableRangeSelectionTarget.ColumnInputCell,
            _ => DataTableRangeSelectionTarget.RowInputCell
        };

    public static bool TryParse(
        SheetId currentSheetId,
        GridRange range,
        string? rowInputCellText,
        string? columnInputCellText,
        out DataTableDialogResult result,
        out DataTableInputParseIssue issue)
    {
        result = default!;
        issue = DataTableInputParseIssue.None;

        var hasRowInput = !string.IsNullOrWhiteSpace(rowInputCellText);
        var hasColumnInput = !string.IsNullOrWhiteSpace(columnInputCellText);
        if (!TryParseOptionalCell(currentSheetId, rowInputCellText, hasRowInput, out var rowInputCell))
        {
            issue = DataTableInputParseIssue.InvalidRowInputCell;
            return false;
        }

        if (!TryParseOptionalCell(currentSheetId, columnInputCellText, hasColumnInput, out var columnInputCell))
        {
            issue = DataTableInputParseIssue.InvalidColumnInputCell;
            return false;
        }

        if (!hasRowInput && !hasColumnInput)
        {
            issue = DataTableInputParseIssue.MissingInputCell;
            return false;
        }

        if (rowInputCell is { } rowCell && range.Contains(rowCell))
        {
            issue = DataTableInputParseIssue.RowInputCellInsideTableRange;
            return false;
        }

        if (columnInputCell is { } columnCell && range.Contains(columnCell))
        {
            issue = DataTableInputParseIssue.ColumnInputCellInsideTableRange;
            return false;
        }

        if (rowInputCell is { } rowInput && columnInputCell is { } columnInput && rowInput == columnInput)
        {
            issue = DataTableInputParseIssue.InputCellsMustBeDifferent;
            return false;
        }

        var mode = hasRowInput && hasColumnInput
            ? DataTableMode.TwoVariable
            : DataTableMode.OneVariable;
        var orientation = hasRowInput && !hasColumnInput
            ? DataTableInputOrientation.Row
            : DataTableInputOrientation.Column;
        var formulaCell = GetDefaultFormulaCell(range, orientation, mode == DataTableMode.TwoVariable);

        result = new DataTableDialogResult(mode, orientation, formulaCell, rowInputCell, columnInputCell);
        return true;
    }

    private static bool TryParseOptionalCell(
        SheetId sheetId,
        string? text,
        bool shouldParse,
        out CellAddress? address)
    {
        address = null;
        if (!shouldParse)
            return true;

        if (!TryParseCell(text!, sheetId, out var parsed))
            return false;

        address = parsed;
        return true;
    }

    private static string NormalizeInput(string? input) => input?.Trim() ?? "";
}
