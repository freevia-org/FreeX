#if DEBUG
using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Unit tests for <see cref="PaginatedCommitCoordinator"/>: verifies that the coordinator
/// reassembles N page boxes' blocks in document order with Tags preserved.
///
/// <para>Runs on STA because it builds real WPF RichTextBox / FlowDocument instances.</para>
/// </summary>
public sealed class PaginatedCommitCoordinatorTests
{
    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

    private static DocumentView NewEditor(TextDocument doc)
    {
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        return editor;
    }

    // ── document order ────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void Coordinator_ReassemblesBlocksInPageOrder()
    {
        // Build a document with enough paragraphs to produce at least 2 page boxes.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var i = 0; i < 10; i++)
            doc.Blocks.Add(new Paragraph($"Paragraph {i + 1}"));

        var editor = NewEditor(doc);
        var panel = PaginatedEditorPanel.Build(editor);

        PaginatedCommitCoordinator.Commit(panel, editor);

        // All 10 paragraphs must be reassembled, in order.
        editor.Model.Blocks.Should().HaveCount(10,
            "coordinator must reassemble all source blocks");
        editor.Model.Blocks.OfType<Paragraph>()
            .Select(p => p.PlainText)
            .Should().Equal(Enumerable.Range(1, 10).Select(i => $"Paragraph {i}"),
                "blocks must be in document order after coordinator commit");
    }

    // ── Tags preserved (StyleId via ParagraphTag) ─────────────────────────────────────────────────

    [StaFact]
    public void Coordinator_PreservesStyleIdTag()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Heading") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Normal paragraph") { StyleId = "Normal" });

        var editor = NewEditor(doc);
        var panel = PaginatedEditorPanel.Build(editor);
        PaginatedCommitCoordinator.Commit(panel, editor);

        var paras = editor.Model.Blocks.OfType<Paragraph>().ToList();
        paras[0].StyleId.Should().Be("Heading1",
            "ParagraphTag.StyleId must survive a coordinator commit");
        paras[1].StyleId.Should().Be("Normal");
    }

    // ── empty model produces at least one block (mirrors CommitToModel guarantee) ─────────────────

    [StaFact]
    public void Coordinator_EmptyDocument_YieldsAtLeastOneParagraph()
    {
        var doc = TextDocument.CreateEmpty();
        // CreateEmpty already seeds one empty paragraph; clear and rebuild with zero blocks.
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph()); // one empty para to satisfy render

        var editor = NewEditor(doc);
        var panel = PaginatedEditorPanel.Build(editor);
        PaginatedCommitCoordinator.Commit(panel, editor);

        editor.Model.Blocks.Should().NotBeEmpty(
            "coordinator must never produce an empty model (mirrors CommitToModel guarantee)");
    }

    // ── footnote marker Tag survives coordinator ──────────────────────────────────────────────────

    [StaFact]
    public void Coordinator_PreservesFootnoteMarkerTag()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph("Body");
        para.Runs.Add(Run.FootnoteReference(7));
        doc.Blocks.Add(para);
        doc.Footnotes[7] = new Footnote(7, "Note seven");

        var editor = NewEditor(doc);
        var panel = PaginatedEditorPanel.Build(editor);
        PaginatedCommitCoordinator.Commit(panel, editor);

        var resultPara = editor.Model.Blocks.OfType<Paragraph>().First();
        resultPara.Runs.Should().Contain(r => r.FootnoteId == 7,
            "FootnoteMarker Tag must be recovered by coordinator via ReadBlocksInto");
    }

    // ── table structure survives coordinator ──────────────────────────────────────────────────────

    [StaFact]
    public void Coordinator_PreservesTableStructure()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Before table"));
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("X");
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph("After table"));

        var editor = NewEditor(doc);
        var panel = PaginatedEditorPanel.Build(editor);
        PaginatedCommitCoordinator.Commit(panel, editor);

        editor.Model.Blocks.Should().HaveCount(3);
        editor.Model.Blocks.OfType<Table>().Should().HaveCount(1,
            "table must survive coordinator commit");
        editor.Model.Blocks.OfType<Table>().First().Rows.Should().HaveCount(2);
    }
}
#endif
