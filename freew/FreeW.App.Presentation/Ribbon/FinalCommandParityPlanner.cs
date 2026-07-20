using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public static class DrawTableCommandPlanner
{
    public const int DefaultRows = 3;
    public const int DefaultColumns = 3;
    public const int MaximumDimension = 63;

    public static (int Rows, int Columns) Normalize(string? rowsText, string? columnsText) =>
        (NormalizeDimension(rowsText, DefaultRows), NormalizeDimension(columnsText, DefaultColumns));

    public static (int Rows, int Columns) Normalize(int rows, int columns) =>
        (Math.Clamp(rows, 1, MaximumDimension), Math.Clamp(columns, 1, MaximumDimension));

    private static int NormalizeDimension(string? text, int fallback) =>
        int.TryParse(text?.Trim(), out var value)
            ? Math.Clamp(value, 1, MaximumDimension)
            : fallback;
}

public static class QuickPartCommandPlanner
{
    public const string EmptySelectionMessage =
        "Select some text first, then choose Save Selection to Quick Parts.";

    public static QuickPart? CreateSelection(string? selectedText, string? name)
    {
        if (string.IsNullOrEmpty(selectedText) || string.IsNullOrWhiteSpace(name))
            return null;

        return QuickPart.FromText(name.Trim(), selectedText);
    }
}

public sealed record TableEraserMergePlan(int RowIndex, int FirstCellIndex, int LastCellIndex);

public static class TableEraserCommandPlanner
{
    public static TableEraserMergePlan? PlanByCellIndex(Table table, int rowIndex, int cellIndex)
    {
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return null;
        var cells = table.Rows[rowIndex].Cells;
        return cellIndex >= 0 && cellIndex + 1 < cells.Count
            ? new TableEraserMergePlan(rowIndex, cellIndex, cellIndex + 1)
            : null;
    }

    public static TableEraserMergePlan? PlanByGridColumn(Table table, int rowIndex, int gridColumn)
    {
        if (rowIndex < 0 || rowIndex >= table.Rows.Count || gridColumn < 0)
            return null;

        var cells = table.Rows[rowIndex].Cells;
        var grid = 0;
        for (var cellIndex = 0; cellIndex < cells.Count; cellIndex++)
        {
            var span = Math.Max(1, cells[cellIndex].GridSpan);
            if (gridColumn >= grid && gridColumn < grid + span)
                return PlanByCellIndex(table, rowIndex, cellIndex);
            grid += span;
        }

        return null;
    }
}
