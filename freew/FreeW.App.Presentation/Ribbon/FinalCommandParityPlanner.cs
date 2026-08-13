using Free.Shared.AppServices;
using FreeW.Core.Model;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Ribbon;

public enum DrawTableDimensionDialogKind
{
    DrawTable,
    SplitCells,
}

public sealed record DrawTableDimensionDialogPlan(
    string Title,
    string RowsLabel,
    string ColumnsLabel,
    string OkLabel,
    string CancelLabel,
    int DefaultRows,
    int DefaultColumns);

public static class DrawTableCommandPlanner
{
    public const int DefaultRows = 3;
    public const int DefaultColumns = 3;
    public const int SplitDefaultRows = 1;
    public const int SplitDefaultColumns = 2;
    public const int MaximumDimension = 63;

    private static readonly ResourceTextDescriptor[] DialogTexts =
    [
        new("DrawTable_Dialog_Title", "Draw Table"),
        new("SplitCells_Dialog_Title", "Split Cells"),
        new("TableDimensions_Rows_Label", "Number of rows:"),
        new("TableDimensions_Columns_Label", "Number of columns:"),
        new("Common_Ok", "OK"),
        new("Common_Cancel", "Cancel"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        DialogTexts.Select(text => text.ResourceKey).ToArray();

    public static DrawTableDimensionDialogPlan BuildDialog(
        DrawTableDimensionDialogKind kind,
        Func<string, string?>? getText = null) =>
        new(
            DialogTexts[kind == DrawTableDimensionDialogKind.DrawTable ? 0 : 1].Resolve(getText),
            DialogTexts[2].Resolve(getText),
            DialogTexts[3].Resolve(getText),
            DialogTexts[4].Resolve(getText),
            DialogTexts[5].Resolve(getText),
            kind == DrawTableDimensionDialogKind.DrawTable ? DefaultRows : SplitDefaultRows,
            kind == DrawTableDimensionDialogKind.DrawTable ? DefaultColumns : SplitDefaultColumns);

    public static (int Rows, int Columns) Normalize(string? rowsText, string? columnsText) =>
        (NormalizeDimension(rowsText, DefaultRows), NormalizeDimension(columnsText, DefaultColumns));

    public static (int Rows, int Columns) Normalize(int rows, int columns) =>
        (Math.Clamp(rows, 1, MaximumDimension), Math.Clamp(columns, 1, MaximumDimension));

    private static int NormalizeDimension(string? text, int fallback) =>
        int.TryParse(text?.Trim(), out var value)
            ? Math.Clamp(value, 1, MaximumDimension)
            : fallback;
}

public sealed record QuickPartCommandText(
    string SaveTitle,
    string NameLabel,
    string EmptySelectionMessage,
    string EmptyLibraryMessage,
    string InsertTitle,
    string ItemLabel,
    string InsertButton,
    string OkButton,
    string CancelButton);

public static class QuickPartCommandPlanner
{
    public const string EmptySelectionMessage =
        "Select some text first, then choose Save Selection to Quick Parts.";

    public const string EmptyLibraryMessage =
        "No Quick Parts saved yet. Select some text and choose Save Selection to Quick Parts first.";

    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("QuickParts_Save_Title", "Save to Quick Parts"),
        new("QuickParts_Name_Label", "Name:"),
        new("QuickParts_EmptySelection_Message", EmptySelectionMessage),
        new("QuickParts_EmptyLibrary_Message", EmptyLibraryMessage),
        new("QuickParts_Insert_Title", "Insert Quick Part"),
        new("QuickParts_Item_Label", "Quick Part:"),
        new("QuickParts_Insert_Button", "Insert"),
        new("Common_Ok", "OK"),
        new("Common_Cancel", "Cancel"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static QuickPartCommandText ResolveText(Func<string, string?>? getText = null) =>
        new(
            Texts[0].Resolve(getText),
            Texts[1].Resolve(getText),
            Texts[2].Resolve(getText),
            Texts[3].Resolve(getText),
            Texts[4].Resolve(getText),
            Texts[5].Resolve(getText),
            Texts[6].Resolve(getText),
            Texts[7].Resolve(getText),
            Texts[8].Resolve(getText));

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

        var projected = TableGridProjection.At(table.Rows[rowIndex], gridColumn);
        return projected is { } cell
            ? PlanByCellIndex(table, rowIndex, cell.CellIndex)
            : null;
    }
}
