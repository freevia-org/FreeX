using FreeX.App.Presentation;
using FreeX.App.Presentation.DataTools;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum DataTablePlanMode
{
    OneVariable,
    TwoVariable
}

public enum DataTablePlanStatus
{
    Ready,
    NoWorkbook,
    NoWorksheet,
    InvalidTableRange,
    TableRangeTooSmall,
    MissingInputCell,
    InvalidRowInputCell,
    InvalidColumnInputCell,
    InputCellSheetMismatch,
    RowInputCellInsideTableRange,
    ColumnInputCellInsideTableRange,
    InputCellsMustBeDifferent,
    FormulaCellMustContainFormula
}

public sealed record DataTablePlan(
    DataTablePlanMode Mode,
    GridRange TableRange,
    CellAddress FormulaCell,
    DataTableInputOrientation Orientation,
    CellAddress? RowInputCell,
    CellAddress? ColumnInputCell,
    GridRange OutputRange)
{
    public bool IsTwoVariable => Mode == DataTablePlanMode.TwoVariable;

    public long OutputCellCount => OutputRange.CellCount;

    public IWorkbookCommand CreateCommand() => CreateCommand(TableRange);

    public IWorkbookCommand CreateCommand(GridRange tableRange) =>
        Mode == DataTablePlanMode.TwoVariable
            ? new TwoVariableDataTableCommand(
                tableRange,
                FormulaCell,
                RowInputCell ?? throw new InvalidOperationException("Two-variable Data Table plan requires a row input cell."),
                ColumnInputCell ?? throw new InvalidOperationException("Two-variable Data Table plan requires a column input cell."))
            : new OneVariableDataTableCommand(
                tableRange,
                FormulaCell,
                GetOneVariableInputCell(),
                Orientation);

    private CellAddress GetOneVariableInputCell() =>
        Orientation == DataTableInputOrientation.Row
            ? RowInputCell ?? throw new InvalidOperationException("Row-oriented Data Table plan requires a row input cell.")
            : ColumnInputCell ?? throw new InvalidOperationException("Column-oriented Data Table plan requires a column input cell.");
}

public sealed record DataTablePlanResult(
    DataTablePlan? Plan,
    DataTablePlanStatus Status,
    string StatusText,
    string InvalidText)
{
    public bool IsReady => Status == DataTablePlanStatus.Ready;

    public bool Success => IsReady;

    public static DataTablePlanResult Ready(DataTablePlan plan) =>
        new(plan, DataTablePlanStatus.Ready, FormatReadyStatus(plan), "");

    public static DataTablePlanResult Invalid(
        DataTablePlanStatus status,
        string statusText,
        string invalidText = "") =>
        new(null, status, statusText, invalidText);

    private static string FormatReadyStatus(DataTablePlan plan)
    {
        var mode = plan.Mode == DataTablePlanMode.TwoVariable ? "two-variable" : "one-variable";
        return $"Ready to create a {mode} Data Table for {plan.TableRange}.";
    }
}

public static class DataTablePlanner
{
    public static DataTablePlan CreatePlan(GridRange tableRange, DataTableDialogResult dialogResult)
    {
        ArgumentNullException.ThrowIfNull(dialogResult);

        return new DataTablePlan(
            dialogResult.Mode == DataTableMode.TwoVariable
                ? DataTablePlanMode.TwoVariable
                : DataTablePlanMode.OneVariable,
            tableRange,
            dialogResult.FormulaCell,
            dialogResult.Orientation,
            dialogResult.RowInputCell,
            dialogResult.ColumnInputCell,
            GetOutputRange(tableRange));
    }

    public static DataTablePlanResult CreatePlan(
        Workbook? workbook,
        SheetId currentSheetId,
        string? tableRangeText,
        string? rowInputCellText,
        string? columnInputCellText)
    {
        if (workbook is null)
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.NoWorkbook,
                "Data Table requires an open workbook.");

        if (workbook.GetSheet(currentSheetId) is null)
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.NoWorksheet,
                "Data Table requires an active worksheet.");

        var tableRangeInput = NormalizeInput(tableRangeText);
        if (!TryParseRange(
                currentSheetId,
                tableRangeInput,
                sheetName => workbook.GetSheet(sheetName)?.Id,
                out var tableRange))
        {
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.InvalidTableRange,
                "Enter a valid Data Table range.",
                tableRangeInput);
        }

        var sheet = workbook.GetSheet(tableRange.Start.Sheet);
        if (sheet is null)
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.NoWorksheet,
                "Data Table range must refer to a worksheet in this workbook.",
                tableRangeInput);

        return CreatePlan(
            sheet,
            tableRange,
            rowInputCellText,
            columnInputCellText,
            sheetName => workbook.GetSheet(sheetName)?.Id);
    }

    public static DataTablePlanResult CreatePlan(
        Sheet? sheet,
        GridRange tableRange,
        string? rowInputCellText,
        string? columnInputCellText,
        Func<string, SheetId?>? resolveSheetId = null)
    {
        if (sheet is null)
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.NoWorksheet,
                "Data Table requires an active worksheet.");

        resolveSheetId ??= static _ => null;

        if (tableRange.Start.Sheet != sheet.Id)
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.NoWorksheet,
                "Data Table range must be on the provided worksheet.",
                tableRange.ToString());

        if (tableRange.RowCount < 2 || tableRange.ColCount < 2)
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.TableRangeTooSmall,
                "Data Table requires at least two rows and two columns.",
                tableRange.ToString());

        var rowInput = NormalizeInput(rowInputCellText);
        var columnInput = NormalizeInput(columnInputCellText);
        var hasRowInput = rowInput.Length != 0;
        var hasColumnInput = columnInput.Length != 0;

        if (!TryParseOptionalCell(tableRange.Start.Sheet, rowInput, hasRowInput, resolveSheetId, out var rowInputCell))
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.InvalidRowInputCell,
                "Enter a valid row input cell.",
                rowInput);

        if (!TryParseOptionalCell(tableRange.Start.Sheet, columnInput, hasColumnInput, resolveSheetId, out var columnInputCell))
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.InvalidColumnInputCell,
                "Enter a valid column input cell.",
                columnInput);

        if (!hasRowInput && !hasColumnInput)
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.MissingInputCell,
                "Enter either a row input cell or a column input cell.");

        if (rowInputCell is { } rowCell && rowCell.Sheet != tableRange.Start.Sheet)
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.InputCellSheetMismatch,
                "Data Table input cells must be on the table worksheet.",
                rowInput);

        if (columnInputCell is { } columnCell && columnCell.Sheet != tableRange.Start.Sheet)
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.InputCellSheetMismatch,
                "Data Table input cells must be on the table worksheet.",
                columnInput);

        if (rowInputCell is { } rowAddress && tableRange.Contains(rowAddress))
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.RowInputCellInsideTableRange,
                "Row input cell cannot be inside the Data Table range.",
                rowInput);

        if (columnInputCell is { } columnAddress && tableRange.Contains(columnAddress))
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.ColumnInputCellInsideTableRange,
                "Column input cell cannot be inside the Data Table range.",
                columnInput);

        if (rowInputCell is { } row && columnInputCell is { } column && row == column)
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.InputCellsMustBeDifferent,
                "Row and column input cells must be different.",
                columnInput);

        var mode = hasRowInput && hasColumnInput
            ? DataTablePlanMode.TwoVariable
            : DataTablePlanMode.OneVariable;
        var orientation = hasRowInput && !hasColumnInput
            ? DataTableInputOrientation.Row
            : DataTableInputOrientation.Column;
        var formulaCell = DataTableInputParser.GetDefaultFormulaCell(
            tableRange,
            orientation,
            mode == DataTablePlanMode.TwoVariable);
        if (string.IsNullOrWhiteSpace(sheet.GetCell(formulaCell)?.FormulaText))
            return DataTablePlanResult.Invalid(
                DataTablePlanStatus.FormulaCellMustContainFormula,
                $"Data Table formula cell {formulaCell.ToA1()} must contain a formula.",
                formulaCell.ToA1());

        return DataTablePlanResult.Ready(new DataTablePlan(
            mode,
            tableRange,
            formulaCell,
            orientation,
            rowInputCell,
            columnInputCell,
            GetOutputRange(tableRange)));
    }

    public static bool TryCreatePlan(
        Workbook? workbook,
        SheetId currentSheetId,
        string? tableRangeText,
        string? rowInputCellText,
        string? columnInputCellText,
        out DataTablePlan plan,
        out DataTablePlanResult result)
    {
        result = CreatePlan(
            workbook,
            currentSheetId,
            tableRangeText,
            rowInputCellText,
            columnInputCellText);

        if (result.Plan is { } readyPlan)
        {
            plan = readyPlan;
            return true;
        }

        plan = default!;
        return false;
    }

    public static bool TryParseRange(
        SheetId defaultSheetId,
        string? input,
        Func<string, SheetId?>? resolveSheetId,
        out GridRange range) =>
        WorkbookReferenceNavigator.TryParseReferenceRange(
            NormalizeInput(input),
            defaultSheetId,
            resolveSheetId ?? (static _ => null),
            definedNames: null,
            out range);

    private static bool TryParseOptionalCell(
        SheetId defaultSheetId,
        string input,
        bool shouldParse,
        Func<string, SheetId?> resolveSheetId,
        out CellAddress? address)
    {
        address = null;
        if (!shouldParse)
            return true;

        if (!TryParseCell(defaultSheetId, input, resolveSheetId, out var parsed))
            return false;

        address = parsed;
        return true;
    }

    private static bool TryParseCell(
        SheetId defaultSheetId,
        string input,
        Func<string, SheetId?> resolveSheetId,
        out CellAddress address)
    {
        address = default;
        if (input.Contains(':', StringComparison.Ordinal))
            return false;

        if (!WorkbookReferenceNavigator.TryParseReferenceRange(
                input,
                defaultSheetId,
                resolveSheetId,
                definedNames: null,
                out var range) ||
            range.CellCount != 1)
        {
            return false;
        }

        address = range.Start;
        return true;
    }

    private static GridRange GetOutputRange(GridRange range) =>
        new(
            new CellAddress(range.Start.Sheet, range.Start.Row + 1, range.Start.Col + 1),
            range.End);

    private static string NormalizeInput(string? input) => input?.Trim() ?? "";
}
