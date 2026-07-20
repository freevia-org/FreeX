using FreeW.App.Host.Editing;
using FreeW.App.Presentation.QuickParts;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using WpfTable = System.Windows.Documents.Table;

namespace FreeW.App.Host.Tests;

public sealed class FinalFiveCommandParityTests
{
    [StaFact]
    public void InsertTextCommands_UseSharedQuickPartAndFieldBehavior()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Properties.Title = "Parity Report";
        model.Blocks.Add(new Paragraph("Body"));
        var editor = new DocumentView();
        editor.LoadModel(model);

        editor.InsertComplexField(" TITLE ");
        editor.CommitToModel();
        model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Any(run => run.ComplexField is not null && run.ComplexField.Instruction == " TITLE ")
            .Should().BeTrue();
        editor.Undo();

        var library = QuickPartLibrary.LoadFromPath(null);
        var part = QuickPartCommandPlanner.CreateSelection("First\nSecond", "Greeting")!;
        library.Save(part);
        var quickPartModel = TextDocument.CreateEmpty();
        quickPartModel.Blocks.Clear();
        quickPartModel.Blocks.Add(new Paragraph("Body"));
        var quickPartEditor = new DocumentView();
        quickPartEditor.LoadModel(quickPartModel);
        quickPartEditor.CaretPosition = quickPartEditor.Document.ContentStart;
        quickPartEditor.InsertText(library.Get("greeting")!.Text);
        quickPartEditor.CommitToModel();
        string.Join("\n", quickPartModel.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText))
            .Should().Contain("First").And.Contain("Second");
    }

    [StaFact]
    public void TableDrawingCommands_MutateAndUndo()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("Body"));
        var editor = new DocumentView();
        editor.LoadModel(model);

        var dimensions = DrawTableCommandPlanner.Normalize("2", "3");
        editor.InsertTable(dimensions.Rows, dimensions.Columns);
        model.Blocks.OfType<Table>().Single().Rows.Should().HaveCount(2);
        editor.Undo();
        model.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>();

        var table = Table.Create(1, 2);
        model.Blocks.Add(table);
        editor.LoadModel(model);
        var wpfTable = editor.Document.Blocks.OfType<WpfTable>().Single();
        var firstCell = wpfTable.RowGroups[0].Rows[0].Cells[0];
        editor.Selection.Select(firstCell.ContentStart, firstCell.ContentStart);
        editor.CaretPosition = firstCell.ContentStart;

        editor.EraseTableBorderAtCaret();
        var merged = editor.Model.Blocks.OfType<Table>().Single();
        merged.Rows[0].Cells.Should().ContainSingle();
        merged.Rows[0].Cells[0].GridSpan.Should().Be(2);
        editor.Undo();
        editor.Model.Blocks.OfType<Table>().Single().Rows[0].Cells.Should().HaveCount(2);
    }
}
