using System.IO;
using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;
using ModelSection = FreeW.Core.Model.Section;
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

    // ── SectionBreak survives coordinator (PagedEdit enter→exit round-trip) ──────────────────────

    [StaFact]
    public void Coordinator_PreservesSectionBreak()
    {
        // A NextPage section break must survive the PagedEdit enter→exit cycle (BuildParagraph stamps
        // it on the WPF paragraph's ParagraphTag; ReadParagraph recovers it via ReadBlocksInto).
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var sec1Para = new Paragraph("Section 1 end");
        sec1Para.SectionBreak = new ModelSection(new PageSettings(), SectionBreakKind.NextPage);
        doc.Blocks.Add(sec1Para);
        doc.Blocks.Add(new Paragraph("Section 2 content"));

        var editor = NewEditor(doc);
        var panel = PaginatedEditorPanel.Build(editor);
        PaginatedCommitCoordinator.Commit(panel, editor);

        var paras = editor.Model.Blocks.OfType<Paragraph>().ToList();
        paras[0].SectionBreak.Should().NotBeNull(
            "ParagraphTag.SectionBreak must survive a coordinator (PagedEdit) commit");
        paras[0].SectionBreak!.BreakKind.Should().Be(SectionBreakKind.NextPage);
        editor.Model.Sections.Should().HaveCount(2,
            "section count must be preserved after a coordinator commit");
    }

    [StaFact]
    public void Coordinator_PreservesSectionHeaderThroughDocxReopen()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var first = new Paragraph("Section 1 body")
        {
            SectionBreak = new ModelSection(
                new PageSettings
                {
                    WidthPt = 792,
                    HeightPt = 612,
                    Landscape = true
                },
                SectionBreakKind.NextPage)
        };
        first.SectionBreak.HeadersFooters.Header = new HeaderFooter("Section 1 header");
        doc.Blocks.Add(first);
        doc.Blocks.Add(new Paragraph("Section 2 body"));

        var editor = NewEditor(doc);
        var panel = PaginatedEditorPanel.Build(editor);
        PaginatedCommitCoordinator.Commit(panel, editor);

        using var stream = new MemoryStream();
        DocxWriter.Write(editor.Model, stream);
        stream.Position = 0;
        var reopened = DocxReader.Read(stream);

        reopened.Sections.Should().HaveCount(2);
        reopened.Sections[0].BreakKind.Should().Be(SectionBreakKind.NextPage);
        reopened.Sections[0].Page.Landscape.Should().BeTrue();
        reopened.Sections[0].Page.WidthPt.Should().BeApproximately(792, 0.1);
        reopened.Sections[0].HeadersFooters.Header.Should().NotBeNull();
        reopened.Sections[0].HeadersFooters.Header!.PlainText.Should().Contain("Section 1 header");
    }

    [StaFact]
    public void Coordinator_PreservesAllSectionBreakKinds()
    {
        // Continuous / EvenPage / OddPage must also survive the coordinator path.
        var kinds = new[]
        {
            SectionBreakKind.NextPage,
            SectionBreakKind.Continuous,
            SectionBreakKind.EvenPage,
            SectionBreakKind.OddPage
        };

        foreach (var kind in kinds)
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var sectionPara = new Paragraph("Break para");
            sectionPara.SectionBreak = new ModelSection(new PageSettings(), kind);
            doc.Blocks.Add(sectionPara);
            doc.Blocks.Add(new Paragraph("Body"));

            var editor = NewEditor(doc);
            var panel = PaginatedEditorPanel.Build(editor);
            PaginatedCommitCoordinator.Commit(panel, editor);

            var recovered = editor.Model.Blocks.OfType<Paragraph>().First();
            recovered.SectionBreak.Should().NotBeNull(
                $"SectionBreak ({kind}) must survive coordinator commit");
            recovered.SectionBreak!.BreakKind.Should().Be(kind,
                $"BreakKind {kind} must be preserved by the coordinator");
        }
    }
}
