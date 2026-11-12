using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelTable = FreeW.Core.Model.Table;
using ModelTableCell = FreeW.Core.Model.TableCell;
using ModelTableRow = FreeW.Core.Model.TableRow;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTable = System.Windows.Documents.Table;

namespace FreeW.App.Host.Tests;

/// <summary>
/// A table nested inside a cell (w:tc/w:tbl) used to be carried on the cell Tag only and never drawn,
/// so layout-table documents lost whole headings. It now renders at its authored position between the
/// cell's paragraphs, in a read-only host, and survives a commit unchanged. Runs on STA.
/// </summary>
public sealed class NestedTableRenderingTests
{
    private static TextDocument BuildDocument()
    {
        var nested = new ModelTable();
        var nestedRow = new ModelTableRow();
        var nestedCell = new ModelTableCell();
        nestedCell.Paragraphs.Add(new ModelParagraph("inner heading"));
        nestedRow.Cells.Add(nestedCell);
        nested.Rows.Add(nestedRow);

        var outer = new ModelTable { DefaultCellMargins = new TableCellMargins(0, 0, 0, 0) };
        var row = new ModelTableRow();
        var cell = new ModelTableCell();
        cell.Paragraphs.Add(new ModelParagraph("before"));
        cell.Paragraphs.Add(new ModelParagraph("after"));
        cell.NestedTables.Add(nested);
        cell.NestedTableParagraphOffsets.Add(1);
        row.Cells.Add(cell);
        outer.Rows.Add(row);

        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(outer);
        doc.Blocks.Add(new ModelParagraph("tail"));
        return doc;
    }

    [StaFact]
    public void NestedTable_RendersBetweenTheCellParagraphsAndSurvivesCommit()
    {
        var view = new DocumentView();
        view.LoadModel(BuildDocument());

        var wpfCell = view.Document.Blocks.OfType<WpfTable>().First().RowGroups[0].Rows[0].Cells[0];
        var blocks = wpfCell.Blocks.ToList();
        Assert.Equal(3, blocks.Count);
        Assert.IsType<WpfParagraph>(blocks[0]);
        var host = Assert.IsType<BlockUIContainer>(blocks[1]);
        Assert.IsType<WpfParagraph>(blocks[2]);
        var editor = Assert.IsType<RichTextBox>(host.Child);
        Assert.True(editor.IsReadOnly);
        var nestedWpf = Assert.IsType<WpfTable>(editor.Document.Blocks.Single());
        var nestedText = new TextRange(nestedWpf.ContentStart, nestedWpf.ContentEnd).Text;
        Assert.Contains("inner heading", nestedText);
        // An authored zero cell margin is not widened to the default 4-DIP inset.
        Assert.Equal(0, wpfCell.Padding.Left);

        view.CommitToModel();
        var committed = view.Model.Blocks.OfType<ModelTable>().First().Rows[0].Cells[0];
        Assert.Equal(new[] { "before", "after" }, committed.Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text))).ToArray());
        Assert.Single(committed.NestedTables);
        Assert.Equal(new[] { 1 }, committed.NestedTableParagraphOffsets.ToArray());
        Assert.Equal("inner heading", string.Concat(committed.NestedTables[0].Rows[0].Cells[0].Paragraphs[0].Runs.Select(r => r.Text)));
    }

    [StaFact]
    public void PathologicallyDeepNesting_LoadsWithoutExhaustingTheStack()
    {
        ModelTable Level(int depth)
        {
            var table = new ModelTable();
            var row = new ModelTableRow();
            var cell = new ModelTableCell();
            cell.Paragraphs.Add(new ModelParagraph("Nested level " + depth));
            if (depth < 40)
            {
                cell.NestedTables.Add(Level(depth + 1));
                cell.NestedTableParagraphOffsets.Add(1);
                cell.Paragraphs.Add(new ModelParagraph());
            }
            row.Cells.Add(cell);
            table.Rows.Add(row);
            return table;
        }

        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(Level(0));
        var view = new DocumentView();
        view.LoadModel(doc);
        view.Measure(new System.Windows.Size(800, 1000));

        view.CommitToModel();
        var depth = 0;
        for (var cell = view.Model.Blocks.OfType<ModelTable>().First().Rows[0].Cells[0];
             cell.NestedTables.Count > 0;
             cell = cell.NestedTables[0].Rows[0].Cells[0])
        {
            depth++;
        }
        Assert.Equal(40, depth);
    }
}