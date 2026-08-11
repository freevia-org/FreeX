using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record TableFormulaDialogInitialState(
    string FormulaText,
    int NumberFormatIndex);

public sealed record TableFormulaDialogInput(
    string? FormulaText,
    string? NumberFormatText);

public sealed record TableFormulaPasteResult(
    string Text,
    int CaretIndex);

public sealed record TableFormulaDialogAcceptance(
    TableFormulaField? Result,
    string? ValidationMessage)
{
    public bool IsAccepted => Result is not null;
}

public sealed class TableFormulaDialogSession
{
    public TableFormulaDialogSession(TableFormulaDialogInitialState initialState)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        InitialState = initialState;
    }

    public TableFormulaDialogInitialState InitialState { get; }

    public IReadOnlyList<string> NumberFormats => TableFormulaDialogPlanner.NumberFormats;

    public IReadOnlyList<string> Functions => TableFormulaDialogPlanner.Functions;

    public TableFormulaPasteResult PasteFunction(string? formulaText, string functionName) =>
        TableFormulaDialogPlanner.PasteFunction(formulaText, functionName);

    public TableFormulaDialogAcceptance PlanAcceptance(TableFormulaDialogInput input) =>
        TableFormulaDialogPlanner.TryBuildResult(input, out var result, out var error)
            ? new TableFormulaDialogAcceptance(result, ValidationMessage: null)
            : new TableFormulaDialogAcceptance(null, error ?? TableFormulaDialogPlanner.ValidationMessage);
}

public static class TableFormulaDialogPlanner
{
    public const string Title = "Formula";
    public const string FormulaLabel = "Formula:";
    public const string NumberFormatLabel = "Number format:";
    public const string PasteFunctionLabel = "Paste function:";
    public const string ValidationMessage = "Please enter a formula.";
    public const string SumAboveFormula = "=SUM(ABOVE)";
    public const string SumLeftFormula = "=SUM(LEFT)";
    public const string AcceptButtonLabel = "OK";
    public const string CancelButtonLabel = "Cancel";
    public const string AutomationId = "TableFormulaDialog";
    public const string NumberFormatAutomationId = "TableFormulaNumberFormatBox";
    public const string PasteFunctionAutomationId = "TableFormulaPasteFunctionBox";
    public const string ValidationAutomationId = "TableFormulaValidationText";
    public const string AcceptButtonAutomationId = "TableFormulaOkButton";
    public const string CancelButtonAutomationId = "TableFormulaCancelButton";
    private static readonly ResourceTextDescriptor CursorOutsideTableMessage = new(
        "TableFormula_CursorOutsideTable_Message",
        "The cursor must be inside a table cell to insert a formula.");

    public static string ResolveCursorOutsideTableMessage(Func<string, string?>? getText = null) =>
        CursorOutsideTableMessage.Resolve(getText);

    public static string CursorOutsideTableResourceKey => CursorOutsideTableMessage.ResourceKey;

    public static readonly IReadOnlyList<string> Functions =
        ["SUM", "AVERAGE", "COUNT", "PRODUCT", "MIN", "MAX"];

    public static readonly IReadOnlyList<string> NumberFormats =
        ["", "0", "0.00", "#,##0", "#,##0.00", "0%", "$#,##0.00;($#,##0.00)"];

    public static TableFormulaDialogInitialState BuildInitialState(
        Table table,
        int rowIndex,
        int columnIndex)
    {
        ArgumentNullException.ThrowIfNull(table);

        return new TableFormulaDialogInitialState(
            BuildDefaultFormula(table, rowIndex, columnIndex),
            NumberFormatIndex: 0);
    }

    public static string BuildDefaultFormula(Table table, int rowIndex, int columnIndex)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (HasNumberAbove(table, rowIndex, columnIndex))
            return SumAboveFormula;
        if (HasNumberLeft(table, rowIndex, columnIndex))
            return SumLeftFormula;
        return SumAboveFormula;
    }

    public static TableFormulaPasteResult PasteFunction(string? formulaText, string functionName)
    {
        var name = NormalizeFunctionName(functionName);
        var text = formulaText ?? string.Empty;
        if (!text.TrimStart().StartsWith('='))
            text = "=" + text.Trim();

        text += name + "()";
        return new TableFormulaPasteResult(text, Math.Max(0, text.Length - 1));
    }

    public static bool TryBuildResult(
        TableFormulaDialogInput input,
        out TableFormulaField? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(input);

        result = null;
        errorMessage = null;

        var expression = (input.FormulaText ?? string.Empty).Trim();
        if (expression.Length == 0)
        {
            errorMessage = ValidationMessage;
            return false;
        }

        var format = (input.NumberFormatText ?? string.Empty).Trim();
        result = new TableFormulaField(
            expression,
            string.IsNullOrEmpty(format) ? null : format);
        return true;
    }

    private static bool HasNumberAbove(Table table, int rowIndex, int columnIndex)
    {
        if (columnIndex < 0)
            return false;

        var startRow = Math.Min(rowIndex - 1, table.Rows.Count - 1);
        for (var r = startRow; r >= 0; r--)
        {
            var cells = table.Rows[r].Cells;
            if (columnIndex < cells.Count &&
                TableFormulaEvaluator.TryParseCellNumber(cells[columnIndex].PlainText, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNumberLeft(Table table, int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return false;

        var cells = table.Rows[rowIndex].Cells;
        var startColumn = Math.Min(columnIndex - 1, cells.Count - 1);
        for (var c = startColumn; c >= 0; c--)
        {
            if (TableFormulaEvaluator.TryParseCellNumber(cells[c].PlainText, out _))
                return true;
        }

        return false;
    }

    private static string NormalizeFunctionName(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            throw new ArgumentException("Function name is required.", nameof(functionName));

        var normalized = functionName.Trim().ToUpperInvariant();
        if (!Functions.Contains(normalized, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(functionName), functionName, "Unknown table function.");

        return normalized;
    }
}
