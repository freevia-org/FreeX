using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// GATE: load a document → enter PagedEdit → exit PagedEdit → model must be identical.
///
/// <para>
/// Phase 3a round-trip proof: the <see cref="PaginatedEditorPanel"/> builds page boxes by
/// <em>moving</em> Tag-bearing WPF Block elements from a scratch Render pass, and
/// <see cref="PaginatedCommitCoordinator"/> reassembles them in order via
/// <see cref="DocumentView.ReadBlocksInto"/>.  Each test asserts that one class of model data
/// (text, style ids, list, table, footnote) survives the cycle.
/// </para>
///
/// <para>Runs on STA because it builds real WPF RichTextBox / FlowDocument instances.</para>
/// </summary>
public sealed class PagedEditRoundTripTests
{
    // ── helper: perform a full enter-PagedEdit → exit-PagedEdit cycle ─────────────────────────────

    private static TextDocument Cycle(TextDocument document)
    {
        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CommitToModel();

        // Enter PagedEdit: builds PaginatedEditorPanel from committed model.
        var panel = PaginatedEditorPanel.Build(editor);

        // Exit PagedEdit: coordinator reassembles model from all page boxes.
        PaginatedCommitCoordinator.Commit(panel, editor);

        return editor.Model;
    }

    // ── plain text ────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void PlainParagraphs_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("First paragraph"));
        doc.Blocks.Add(new Paragraph("Second paragraph"));
        doc.Blocks.Add(new Paragraph("Third paragraph"));

        var result = Cycle(doc);

        result.Blocks.Should().HaveCount(3, "block count must be identical after paged-edit cycle");
        result.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("First paragraph", "Second paragraph", "Third paragraph");
    }

    // ── style ids (ParagraphTag.StyleId is the critical Tag payload) ──────────────────────────────

    [StaFact]
    public void StyleIds_SurviveRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Heading") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Body text") { StyleId = "Normal" });

        var result = Cycle(doc);

        var paras = result.Blocks.OfType<Paragraph>().ToList();
        paras[0].StyleId.Should().Be("Heading1",
            "ParagraphTag.StyleId must survive the PagedEdit round-trip");
        paras[1].StyleId.Should().Be("Normal");
    }

    // ── bulleted list (ListKind Tag on WpfList + ParagraphTag.ListLevel) ──────────────────────────

    [StaFact]
    public void BulletList_SurvivesRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var text in new[] { "Alpha", "Beta", "Gamma" })
        {
            doc.Blocks.Add(new Paragraph(text)
            {
                Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
            });
        }

        var result = Cycle(doc);

        var listParas = result.Blocks.OfType<Paragraph>().ToList();
        listParas.Select(p => p.PlainText).Should()
            .Equal(new[] { "Alpha", "Beta", "Gamma" }, "list paragraph texts must be preserved");
        listParas.Should().OnlyContain(p => p.Formatting.ListKind == ListKind.Bullet,
            "all list paragraphs must round-trip as Bullet");
    }

    // ── table (TableCellTag, column widths, cell text) ────────────────────────────────────────────

    [StaFact]
    public void Table_SurvivesRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(2, 3);
        table.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("R0C0");
        table.Rows[0].Cells[1].Paragraphs[0] = new Paragraph("R0C1");
        table.Rows[1].Cells[2].Paragraphs[0] = new Paragraph("R1C2");
        doc.Blocks.Add(table);

        var result = Cycle(doc);

        var resultTable = result.Blocks.OfType<Table>().Single();
        resultTable.Rows.Should().HaveCount(2, "row count must be preserved");
        resultTable.Rows[0].Cells.Should().HaveCount(3, "column count must be preserved");
        resultTable.Rows[0].Cells[0].PlainText.Should().Be("R0C0");
        resultTable.Rows[0].Cells[1].PlainText.Should().Be("R0C1");
        resultTable.Rows[1].Cells[2].PlainText.Should().Be("R1C2");
    }

    // ── footnote reference (FootnoteMarker Tag on Run) ────────────────────────────────────────────

    [StaFact]
    public void FootnoteReference_SurvivesRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph("Body text");
        para.Runs.Add(Run.FootnoteReference(1));
        para.Runs.Add(new Run(" after note"));
        doc.Blocks.Add(para);
        doc.Footnotes[1] = new Footnote(1, "The footnote body");

        var result = Cycle(doc);

        var resultPara = result.Blocks.OfType<Paragraph>().First();
        resultPara.Runs.Should().Contain(r => r.FootnoteId == 1,
            "FootnoteMarker Tag must survive the PagedEdit round-trip so the footnote reference is recovered");
        // The footnote dictionary is on the model, not in the FlowDocument; it is untouched.
        result.Footnotes.Should().ContainKey(1);
        result.Footnotes[1].Content[0].PlainText.Should().Contain("The footnote body");
    }

    // ── multi-paragraph document: block count identity ────────────────────────────────────────────

    [StaFact]
    public void NoteReferenceMarkers_UseTransformsWithoutExpandingTheLineBox()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var footnoteParagraph = new Paragraph("Footnote");
        footnoteParagraph.Runs.Add(Run.FootnoteReference(1));
        doc.Blocks.Add(footnoteParagraph);
        doc.Footnotes[1] = new Footnote(1, "Footnote body");

        var endnoteParagraph = new Paragraph("Endnote");
        endnoteParagraph.Runs.Add(Run.EndnoteReference(2));
        doc.Blocks.Add(endnoteParagraph);
        doc.Endnotes[2] = new Endnote(2, "Endnote body");

        var editor = new DocumentView();
        editor.LoadModel(doc);
        var markers = editor.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<System.Windows.Documents.Run>())
            .Where(run => run.Text is "1" or "2")
            .ToList();

        markers.Should().HaveCount(2);
        foreach (var marker in markers)
        {
            marker.BaselineAlignment.Should().Be(BaselineAlignment.Baseline);
            marker.TextEffects.Should().ContainSingle();
            var translation = marker.TextEffects[0].Transform.Should().BeOfType<TranslateTransform>().Which;
            translation.Y.Should().Be(-5.0);
        }
    }

    [StaTheory]
    [InlineData(3.0, -4.0)]
    [InlineData(-2.25, 3.0)]
    public void RunBaselinePosition_UsesAVisualTransformAndSurvivesRoundTrip(
        double positionPt,
        double expectedOffsetDip)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Positioned", new RunFormatting { PositionPt = positionPt }));
        doc.Blocks.Add(paragraph);

        var editor = new DocumentView();
        editor.LoadModel(doc);
        var rendered = editor.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(block => block.Inlines.OfType<System.Windows.Documents.Run>())
            .Single(run => run.Text == "Positioned");

        rendered.TextEffects.Should().ContainSingle();
        var translation = rendered.TextEffects[0].Transform.Should().BeOfType<TranslateTransform>().Which;
        translation.X.Should().Be(0);
        translation.Y.Should().BeApproximately(expectedOffsetDip, 0.001);

        editor.CommitToModel();
        doc.Blocks.OfType<Paragraph>().Single().Runs.Single().Formatting.PositionPt
            .Should().BeApproximately(positionPt, 0.001);
    }

    [StaFact]
    public void MultiBlock_BlockCountIdentical_AfterRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        // styled heading
        doc.Blocks.Add(new Paragraph("Chapter 1") { StyleId = "Heading1" });

        // body paragraph
        doc.Blocks.Add(new Paragraph("First body paragraph."));

        // numbered list
        foreach (var text in new[] { "Item 1", "Item 2", "Item 3" })
            doc.Blocks.Add(new Paragraph(text)
            {
                Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number }
            });

        // table
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("A");
        table.Rows[0].Cells[1].Paragraphs[0] = new Paragraph("B");
        doc.Blocks.Add(table);

        // footnote reference
        var footnotePara = new Paragraph("See note");
        footnotePara.Runs.Add(Run.FootnoteReference(1));
        doc.Blocks.Add(footnotePara);
        doc.Footnotes[1] = new Footnote(1, "Footnote text");

        var before = doc.Blocks.Count;
        var result = Cycle(doc);

        result.Blocks.Count.Should().Be(before,
            "block count must be identical after enter→exit PagedEdit");
    }

    // ── round-trip does NOT mutate the default continuous editor modes ────────────────────────────

    [StaFact]
    public void PagedEditCycle_DoesNotAlterContinuousEditorBehaviour()
    {
        // After the cycle, the editor must reload cleanly from the model (PrintLayout unchanged).
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Unchanged"));

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor);
        PaginatedCommitCoordinator.Commit(panel, editor);

        // Reload continuous editor — must not throw and mode stays PrintLayout.
        editor.LoadModel(editor.Model);

        editor.ViewMode.Should().Be(DocumentViewMode.PrintLayout,
            "the continuous editor default mode must be untouched by the PagedEdit cycle");
        editor.Model.Blocks.Should().HaveCount(1);
        editor.Model.Blocks.OfType<Paragraph>().First().PlainText.Should().Be("Unchanged");
    }
}
