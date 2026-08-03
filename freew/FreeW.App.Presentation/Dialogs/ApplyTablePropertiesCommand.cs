using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// Applies the shared Table Properties payload as one undoable edit. The snapshot covers every field the
/// planner can touch, including widths propagated to other rows in the selected column.
/// </summary>
public sealed class ApplyTablePropertiesCommand(
    int blockIndex,
    int rowIndex,
    int cellIndex,
    TablePropertiesValues values) : IDocumentCommand
{
    private Snapshot? _previous;

    public string Label => "Table Properties";

    public void Apply(IDocumentCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!TryGetContext(context.Document, out var tableContext))
            return;

        _previous = Snapshot.Capture(tableContext.Table);
        TablePropertiesDialogPlanner.ApplyValues(tableContext, values);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null
            || blockIndex < 0
            || blockIndex >= context.Document.Blocks.Count
            || context.Document.Blocks[blockIndex] is not Table table)
        {
            return;
        }

        _previous.Restore(table);
        _previous = null;
    }

    private bool TryGetContext(TextDocument document, out ModelTableContext tableContext)
    {
        tableContext = null!;
        if (blockIndex < 0
            || blockIndex >= document.Blocks.Count
            || document.Blocks[blockIndex] is not Table table
            || rowIndex < 0
            || rowIndex >= table.Rows.Count
            || cellIndex < 0
            || cellIndex >= table.Rows[rowIndex].Cells.Count)
        {
            return false;
        }

        var row = table.Rows[rowIndex];
        tableContext = new ModelTableContext(table, row, row.Cells[cellIndex]);
        return true;
    }

    private sealed record RowSnapshot(
        TableRow Row,
        double? HeightPt,
        TableRowHeightRule HeightRule,
        bool AllowBreakAcrossPages);

    private sealed record CellSnapshot(
        TableCell Cell,
        double? WidthPt,
        TableCellVerticalAlignment VerticalAlignment,
        TableCellMargins? Margins);

    private sealed record Snapshot(
        double? PreferredWidthPt,
        TableAlignment Alignment,
        double? IndentFromLeftPt,
        bool TextWrapping,
        TableCellMargins? DefaultCellMargins,
        double? CellSpacingPt,
        TableFormatting Formatting,
        double[] ColumnWidthsPt,
        RowSnapshot[] Rows,
        CellSnapshot[] Cells)
    {
        public static Snapshot Capture(Table table) => new(
            table.PreferredWidthPt,
            table.Alignment,
            table.IndentFromLeftPt,
            table.TextWrapping,
            table.DefaultCellMargins,
            table.CellSpacingPt,
            table.Formatting,
            [.. table.ColumnWidthsPt],
            [.. table.Rows.Select(row => new RowSnapshot(
                row,
                row.HeightPt,
                row.HeightRule,
                row.AllowBreakAcrossPages))],
            [.. table.Rows.SelectMany(row => row.Cells).Select(cell => new CellSnapshot(
                cell,
                cell.WidthPt,
                cell.VerticalAlignment,
                cell.Margins))]);

        public void Restore(Table table)
        {
            table.PreferredWidthPt = PreferredWidthPt;
            table.Alignment = Alignment;
            table.IndentFromLeftPt = IndentFromLeftPt;
            table.TextWrapping = TextWrapping;
            table.DefaultCellMargins = DefaultCellMargins;
            table.CellSpacingPt = CellSpacingPt;
            table.Formatting = Formatting;
            table.ColumnWidthsPt.Clear();
            table.ColumnWidthsPt.AddRange(ColumnWidthsPt);

            foreach (var row in Rows)
            {
                row.Row.HeightPt = row.HeightPt;
                row.Row.HeightRule = row.HeightRule;
                row.Row.AllowBreakAcrossPages = row.AllowBreakAcrossPages;
            }

            foreach (var cell in Cells)
            {
                cell.Cell.WidthPt = cell.WidthPt;
                cell.Cell.VerticalAlignment = cell.VerticalAlignment;
                cell.Cell.Margins = cell.Margins;
            }
        }
    }
}
