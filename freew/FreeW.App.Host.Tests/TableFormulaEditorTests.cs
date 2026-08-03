using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;
using WpfTable = System.Windows.Documents.Table;
using TableRow = FreeW.Core.Model.TableRow;
using TableCell = FreeW.Core.Model.TableCell;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Editor-level coverage for <see cref="DocumentView.InsertTableFormula"/> (Word's Table &gt; Data &gt;
/// Formula). Runs on STA because it drives the real WPF <see cref="DocumentView"/>. A formula inserted into
/// a table cell must compute its value from the cell values, survive a commit cycle as a model formula run,
/// and be a no-op outside a table.
/// </summary>
public sealed class TableFormulaEditorTests
{
    // A model with a single 3x1 table: "10", "20", and an empty cell to receive the formula.
    private static TextDocument TableModel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = new Table();
        table.Rows.Add(Row("10"));
        table.Rows.Add(Row("20"));
        table.Rows.Add(Row(string.Empty));
        doc.Blocks.Add(table);
        return doc;
    }

    private static TableRow Row(string text)
    {
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph(text));
        var row = new TableRow();
        row.Cells.Add(cell);
        return row;
    }

    // Place the caret in the WPF cell at (rowIndex, columnIndex) of the document's first table.
    private static void PlaceCaretInCell(DocumentView view, int rowIndex, int columnIndex, int textOffset = 0)
    {
        var table = view.Document.Blocks.OfType<WpfTable>().First();
        var cell = table.RowGroups[0].Rows[rowIndex].Cells[columnIndex];
        var paragraph = cell.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        var run = paragraph.Inlines.OfType<System.Windows.Documents.Run>().FirstOrDefault();
        view.CaretPosition = run?.ContentStart.GetPositionAtOffset(textOffset, LogicalDirection.Forward)
            ?? cell.ContentStart;
    }

    private static Paragraph FormulaParagraph(DocumentView view) =>
        ((Table)view.Model.Blocks[0]).Rows[2].Cells[0].Paragraphs.Single();

    [StaFact]
    public void InsertTableFormula_ComputesSumAboveAndRoundTrips()
    {
        var view = new DocumentView();
        view.LoadModel(TableModel());
        PlaceCaretInCell(view, rowIndex: 2, columnIndex: 0);

        view.InsertTableFormula(new TableFormulaField("=SUM(ABOVE)"));
        view.CommitToModel();

        var modelTable = view.Model.Blocks.OfType<Table>().Single();
        var run = modelTable.Rows[2].Cells[0].Paragraphs
            .SelectMany(p => p.Runs)
            .Single(r => r.TableFormula is not null);

        run.TableFormula!.Expression.Should().Be("=SUM(ABOVE)");
        // 10 + 20, computed from the cells above and cached as the run's text.
        run.Text.Should().Be("30");
    }

    [StaFact]
    public void InsertTableFormula_WithNumberFormat_FormatsResult()
    {
        var view = new DocumentView();
        view.LoadModel(TableModel());
        PlaceCaretInCell(view, rowIndex: 2, columnIndex: 0);

        view.InsertTableFormula(new TableFormulaField("=SUM(ABOVE)", "#,##0.00"));
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Table>().Single().Rows[2].Cells[0].Paragraphs
            .SelectMany(p => p.Runs).Single(r => r.TableFormula is not null);

        run.Text.Should().Be("30.00");
    }

    [StaFact]
    public void InsertTableFormula_IsOneUndoableEditAndRedoRestoresCachedField()
    {
        var document = TableModel();
        var target = ((Table)document.Blocks[0]).Rows[2].Cells[0].Paragraphs.Single();
        target.Runs.Clear();
        target.Runs.Add(new Run("before after")
        {
            Formatting = RunFormatting.Default with { Bold = true }
        });
        var view = new DocumentView();
        view.LoadModel(document);
        PlaceCaretInCell(view, rowIndex: 2, columnIndex: 0, textOffset: 7);

        view.InsertTableFormula(new TableFormulaField("=SUM(ABOVE)", "#,##0.00"));

        FormulaParagraph(view).Runs.Select(run => run.Text).Should().Equal("before ", "30.00", "after");
        view.CanUndo.Should().BeTrue();
        view.Undo();
        FormulaParagraph(view).PlainText.Should().Be("before after");
        FormulaParagraph(view).Runs.Should().NotContain(run => run.TableFormula != null);

        view.CanRedo.Should().BeTrue();
        view.Redo();
        var formulaRun = FormulaParagraph(view).Runs.Single(run => run.TableFormula is not null);
        formulaRun.Text.Should().Be("30.00");
        formulaRun.TableFormula.Should().Be(new TableFormulaField("=SUM(ABOVE)", "#,##0.00"));
        FormulaParagraph(view).Runs[0].Formatting.Bold.Should().BeTrue();
        FormulaParagraph(view).Runs[2].Formatting.Bold.Should().BeTrue();
    }

    [StaFact]
    public void InsertTableFormula_OutsideTable_IsNoOp()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Not a table"));

        var view = new DocumentView();
        view.LoadModel(doc);
        view.InsertTableFormula(new TableFormulaField("=SUM(ABOVE)"));
        view.CommitToModel();

        var hasFormula = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs).Any(r => r.TableFormula is not null);
        hasFormula.Should().BeFalse();
    }

    [StaFact]
    public void CaretTableCell_OutsideTable_ReturnsNull()
    {
        var doc = TextDocument.CreateEmpty();
        var view = new DocumentView();
        view.LoadModel(doc);

        view.CaretTableCell().Should().BeNull();
    }
}
