using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Xunit;
using WpfTable = System.Windows.Documents.Table;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for the Table Properties dialog and its apply path. The dialog is a WPF
/// <see cref="System.Windows.Window"/> (STA); these tests construct it via the test seam (exercising its
/// control wiring without a modal loop), and verify <see cref="DocumentView.ApplyTableProperties"/> — the
/// same commit + re-render path the ribbon command uses — applies the dialog's values onto the caret's
/// table / row / cell.
/// </summary>
public sealed class TablePropertiesDialogTests
{
    private static TextDocument TableModel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0] = new TableCell("a");
        table.Rows[0].Cells[1] = new TableCell("b");
        table.Rows[1].Cells[0] = new TableCell("c");
        table.Rows[1].Cells[1] = new TableCell("d");
        doc.Blocks.Add(table);
        return doc;
    }

    private static void PlaceCaretInCell(DocumentView view, int rowIndex, int columnIndex)
    {
        var table = view.Document.Blocks.OfType<WpfTable>().First();
        var cell = table.RowGroups[0].Rows[rowIndex].Cells[columnIndex];
        view.CaretPosition = cell.ContentStart;
    }

    [StaFact]
    public void Dialog_SeedsControls_AndRoundTripsSeededValuesThroughAccept()
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0] = new TableCell("x");
        table.PreferredWidthPt = 300;
        table.Alignment = TableAlignment.Right;
        table.IndentFromLeftPt = 12;
        table.TextWrapping = true;
        table.FloatingTableAllowsOverlap = false;
        table.FloatingPosition = new TableFloatingPosition(
            HorizontalAnchor: TableHorizontalAnchor.Page,
            VerticalAnchor: TableVerticalAnchor.Margin,
            HorizontalAlignment: TableHorizontalPositionAlignment.Outside,
            VerticalOffsetPt: -18,
            LeftFromTextPt: 3,
            RightFromTextPt: 4,
            TopFromTextPt: 5,
            BottomFromTextPt: 6);
        table.CellSpacingPt = 2;
        table.Formatting = table.Formatting with { RepeatHeaderRow = true };
        var row = table.Rows[0];
        row.HeightPt = 36;
        row.HeightRule = TableRowHeightRule.Exact;
        row.AllowBreakAcrossPages = false;
        var cell = row.Cells[0];
        cell.WidthPt = 150;
        cell.VerticalAlignment = TableCellVerticalAlignment.Bottom;
        cell.Margins = new TableCellMargins(1, 7, 1, 7);
        cell.WrapText = false;
        cell.FitText = true;

        var dialog = TablePropertiesDialog.CreateForTest(new ModelTableContext(table, row, cell));
        var result = dialog.AcceptForTest();

        Assert.NotNull(result);
        Assert.Equal(300, result!.PreferredWidthPt);
        Assert.Equal(TableAlignment.Right, result.Alignment);
        Assert.True(result.TextWrapping);
        Assert.False(result.FloatingTableAllowsOverlap);
        Assert.Equal(table.FloatingPosition, result.FloatingPosition);
        Assert.Equal(12, result.IndentFromLeftPt);
        Assert.Equal(2, result.CellSpacingPt);
        Assert.True(result.RepeatHeaderRow);
        Assert.Equal(36, result.RowHeightPt);
        Assert.Equal(TableRowHeightRule.Exact, result.RowHeightRule);
        Assert.False(result.AllowRowBreak);
        Assert.Equal(TableCellVerticalAlignment.Bottom, result.CellVerticalAlignment);
        Assert.NotNull(result.CellMargins);
        Assert.Equal(7, result.CellMargins!.LeftPt);
        Assert.False(result.CellWrapText);
        Assert.True(result.CellFitText);
    }

    [StaFact]
    public void ApplyTableProperties_AppliesTableRowAndCellFields_ToModel()
    {
        var view = new DocumentView();
        view.LoadModel(TableModel());
        PlaceCaretInCell(view, rowIndex: 1, columnIndex: 1);

        view.ApplyTableProperties(new TablePropertiesValues(
            PreferredWidthPt: 400,
            Alignment: TableAlignment.Center,
            TextWrapping: true,
            IndentFromLeftPt: 9,
            DefaultCellMargins: new TableCellMargins(0, 6, 0, 6),
            CellSpacingPt: 1.5,
            RowHeightPt: 24,
            RowHeightRule: TableRowHeightRule.AtLeast,
            AllowRowBreak: false,
            RepeatHeaderRow: true,
            ColumnWidthPt: 120,
            CellPreferredWidthPt: 120,
            CellVerticalAlignment: TableCellVerticalAlignment.Center,
            CellMargins: new TableCellMargins(2, 8, 2, 8),
            CellWrapText: false,
            CellFitText: true));

        var table = view.Model.Blocks.OfType<Table>().Single();
        Assert.Equal(400, table.PreferredWidthPt);
        Assert.Equal(TableAlignment.Center, table.Alignment);
        Assert.True(table.TextWrapping);
        Assert.Equal(9, table.IndentFromLeftPt);
        Assert.Equal(1.5, table.CellSpacingPt);
        Assert.Equal(6, table.DefaultCellMargins!.LeftPt);
        Assert.True(table.Formatting.RepeatHeaderRow);

        var row = table.Rows[1];
        Assert.Equal(24, row.HeightPt);
        Assert.Equal(TableRowHeightRule.AtLeast, row.HeightRule);
        Assert.False(row.AllowBreakAcrossPages);

        // Column width is applied to every cell in column 1.
        Assert.Equal(120, table.Rows[0].Cells[1].WidthPt);
        Assert.Equal(120, table.Rows[1].Cells[1].WidthPt);

        var cell = row.Cells[1];
        Assert.Equal(TableCellVerticalAlignment.Center, cell.VerticalAlignment);
        Assert.Equal(8, cell.Margins!.LeftPt);
        Assert.False(cell.WrapText);
        Assert.True(cell.FitText);

        Assert.True(view.CanUndo);
        view.Undo();
        table = view.Model.Blocks.OfType<Table>().Single();
        Assert.Null(table.PreferredWidthPt);
        Assert.Equal(TableAlignment.Left, table.Alignment);
        Assert.Null(table.Rows[1].HeightPt);
        Assert.Null(table.Rows[1].Cells[1].WidthPt);
        Assert.Equal(TableCellVerticalAlignment.Top, table.Rows[1].Cells[1].VerticalAlignment);
        Assert.True(table.Rows[1].Cells[1].WrapText);
        Assert.False(table.Rows[1].Cells[1].FitText);

        view.Redo();
        table = view.Model.Blocks.OfType<Table>().Single();
        Assert.Equal(400, table.PreferredWidthPt);
        Assert.Equal(120, table.Rows[1].Cells[1].WidthPt);
        Assert.False(table.Rows[1].Cells[1].WrapText);
        Assert.True(table.Rows[1].Cells[1].FitText);
    }

    [StaFact]
    public void CaretTableContext_OutsideTable_ReturnsNull()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());

        Assert.Null(view.CaretTableContext());
    }
}
