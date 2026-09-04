using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.IO.Tests;

public sealed class R387_CommandUndoRestoresTheDocumentTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    private static TextDocument BuildDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var first = new Paragraph("alpha");
        first.Runs.Add(new Run("beta", new RunFormatting { Bold = true }));
        document.Blocks.Add(first);
        document.Blocks.Add(new Paragraph("gamma"));

        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0] = new TableCell("A1");
        table.Rows[0].Cells[1] = new TableCell("B1");
        table.Rows[1].Cells[0] = new TableCell("A2");
        table.Rows[1].Cells[1] = new TableCell("B2");
        document.Blocks.Add(table);

        return document;
    }

    private static string Serialize(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(stream.ToArray()));
    }

    private static void Check(string label, Func<IDocumentCommand> factory)
    {
        var document = BuildDocument();
        var context = new Context(document);
        var before = Serialize(document);

        var command = factory();
        command.HasEffect(context).Should().BeTrue(label);

        command.Apply(context);
        Serialize(document).Should().NotBe(before,
            "{0} must actually change the document, or the undo assertion below proves nothing", label);

        command.Revert(context);
        Serialize(document).Should().Be(before,
            "{0}: undo must restore the document exactly", label);
    }

    [Fact]
    public void EveryCoveredCommandUndoesExactly()
    {
        Check("InsertParagraph", () => new InsertParagraphCommand(1, new Paragraph("inserted")));
        Check("DeleteParagraph", () => new DeleteParagraphCommand(1));
        Check("InsertBlock", () => new InsertBlockCommand(1, new Paragraph("block")));
        Check("SetParagraphStyle", () => new SetParagraphStyleCommand(0, "Heading1"));
        Check("SetParagraphFormatting", () => new SetParagraphFormattingCommand(
            0, ParagraphFormatting.Default with { Alignment = TextAlignment.Center }));
        Check("SetRunFormatting", () => new SetRunFormattingCommand(
            0, 0, new RunFormatting { Italic = true }));
        Check("ReplaceParagraphRuns", () => new ReplaceParagraphRunsCommand(0, p =>
        {
            p.Runs.Clear();
            p.Runs.Add(new Run("replaced"));
        }));
        Check("InsertTableRow", () => new InsertTableRowCommand(2, 1));
        Check("DeleteTableRow", () => new DeleteTableRowCommand(2, 1));
        Check("InsertTableColumn", () => new InsertTableColumnCommand(2, 1));
        Check("DeleteTableColumn", () => new DeleteTableColumnCommand(2, 1));
        Check("MergeCellsHorizontal", () => new MergeCellsHorizontalCommand(2, 0, 0, 1));
        Check("MergeCellsVertical", () => new MergeCellsVerticalCommand(2, 0, 0, 1));
        Check("ReorderBlocks", () =>
        {
            var document = BuildDocument();
            return new ReorderBlocksCommand([document.Blocks[1], document.Blocks[0], document.Blocks[2]]);
        });
    }
}
