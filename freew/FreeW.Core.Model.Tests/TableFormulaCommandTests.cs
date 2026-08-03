namespace FreeW.Core.Model.Tests;

public sealed class TableFormulaCommandTests
{
    [Fact]
    public void InsertTableCellFormula_PreservesSiblingsAndUndoRedoRestoresExactField()
    {
        var document = BuildDocument();
        var table = (Table)document.Blocks[0];
        var paragraph = table.Rows[2].Cells[0].Paragraphs.Single();
        var original = paragraph.Runs.Single();
        var bus = new DocumentCommandBus(new Context(document));

        var command = new InsertTableCellFormulaCommand(
            0, 2, 0, 0, 7, new TableFormulaField("=SUM(ABOVE)", "#,##0.00"));
        bus.Execute(command);

        paragraph.Runs.Select(run => run.Text).Should().Equal("before ", "30.00", "after");
        var inserted = paragraph.Runs.Single(run => run.TableFormula is not null);
        inserted.TableFormula.Should().Be(new TableFormulaField("=SUM(ABOVE)", "#,##0.00"));
        inserted.Formatting.Bold.Should().BeFalse();
        paragraph.Runs[0].Formatting.Bold.Should().BeTrue();
        paragraph.Runs[2].Formatting.Bold.Should().BeTrue();
        command.InsertedTextLength.Should().Be(5);

        bus.Undo().Should().BeTrue();
        paragraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(original);
        paragraph.PlainText.Should().Be("before after");

        bus.Redo().Should().BeTrue();
        paragraph.Runs.Select(run => run.Text).Should().Equal("before ", "30.00", "after");
        var redone = paragraph.Runs.Single(run => run.TableFormula is not null);
        redone.Should().BeSameAs(inserted);
        redone.TableFormula.Should().Be(new TableFormulaField("=SUM(ABOVE)", "#,##0.00"));
    }

    [Fact]
    public void InsertTableCellFormula_InvalidAddressLeavesDocumentUnchangedAcrossUndoRedo()
    {
        var document = BuildDocument();
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new InsertTableCellFormulaCommand(
            0, 7, 0, 0, 0, new TableFormulaField("=SUM(ABOVE)")));

        bus.Undo().Should().BeTrue();
        bus.Redo().Should().BeTrue();
        ((Table)document.Blocks[0]).Rows[2].Cells[0].Paragraphs.Single().PlainText
            .Should().Be("before after");
    }

    private static TextDocument BuildDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = new Table();
        table.Rows.Add(Row("10"));
        table.Rows.Add(Row("20"));
        table.Rows.Add(Row("before after", bold: true));
        document.Blocks.Add(table);
        return document;
    }

    private static TableRow Row(string text, bool bold = false)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text)
        {
            Formatting = RunFormatting.Default with { Bold = bold }
        });
        var cell = new TableCell();
        cell.Paragraphs.Add(paragraph);
        var row = new TableRow();
        row.Cells.Add(cell);
        return row;
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
