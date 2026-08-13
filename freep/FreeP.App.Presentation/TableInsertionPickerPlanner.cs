namespace FreeP.App.Compositor;

public sealed record TableInsertionPickerChoice(
    int Rows,
    int Columns,
    string Label,
    bool IsDefault)
{
    public string DisplayLabel => IsDefault ? $"{Label} (default)" : Label;

    public string AutomationId => $"table-{Rows}x{Columns}";
}

public sealed record TableInsertionPickerPlan(
    int MaxRows,
    int MaxColumns,
    IReadOnlyList<TableInsertionPickerChoice> Choices);

public static class TableInsertionPickerPlanner
{
    public const int DefaultMaxRows = 5;
    public const int DefaultMaxColumns = 5;
    public const int DefaultRows = 3;
    public const int DefaultColumns = 3;
    public const string PickerHeading = "Insert Table";

    public static TableInsertionPickerPlan BuildPlan(
        int maxRows = DefaultMaxRows,
        int maxColumns = DefaultMaxColumns)
    {
        maxRows = Math.Clamp(maxRows, 1, 10);
        maxColumns = Math.Clamp(maxColumns, 1, 10);

        var choices = new List<TableInsertionPickerChoice>(maxRows * maxColumns);
        for (var rows = 1; rows <= maxRows; rows++)
        {
            for (var columns = 1; columns <= maxColumns; columns++)
            {
                choices.Add(new TableInsertionPickerChoice(
                    rows,
                    columns,
                    FormatChoiceLabel(rows, columns),
                    rows == DefaultRows && columns == DefaultColumns));
            }
        }

        return new TableInsertionPickerPlan(maxRows, maxColumns, choices);
    }

    public static bool TryApplyChoice(
        EditingSession editor,
        int rows,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (rows < 1 || columns < 1 || rows > 10 || columns > 10)
            return false;

        editor.InsertTable(rows, columns);
        return true;
    }

    public static string FormatChoiceLabel(int rows, int columns) =>
        $"{rows} x {columns} Table";
}
