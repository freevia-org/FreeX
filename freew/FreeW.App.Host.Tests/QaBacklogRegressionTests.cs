using System.Linq;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for the four MED/LOW QA-backlog defects fixed under roadmap item Y3, each as a
/// model → Render → CommitToModel round-trip that asserts the previously-lost property survives:
/// <list type="number">
///   <item>a field run inside a hyperlink keeps its link;</item>
///   <item>author-set cell shading equal to the header/banded style fill survives;</item>
///   <item>an emptied content-control / comment run keeps its marker;</item>
///   <item>a MultiLevel list does not degrade to Number after an in-editor edit cycle.</item>
/// </list>
/// These run on an STA thread (<c>[StaFact]</c>, via Xunit.StaFact) because the RichTextBox/FlowDocument
/// need STA + a Dispatcher.
/// </summary>
public sealed class QaBacklogRegressionTests
{
    // Load the model into a fresh DocumentView, commit straight back, and return the recovered model.
    private static TextDocument RoundTrip(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadModel(document);
        view.CommitToModel();
        return view.Model;
    }

    // --- [MED] Field run inside a hyperlink renders un-linked --------------------------------------

    [StaFact]
    public void FieldRun_InsideExternalHyperlink_KeepsLink()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("3")
        {
            FieldKind = RunFieldKind.PageNumber,
            HyperlinkUrl = "https://example.com/"
        });
        doc.Blocks.Add(para);

        var run = ((Paragraph)RoundTrip(doc).Blocks[0]).Runs[0];

        run.FieldKind.Should().Be(RunFieldKind.PageNumber, "the run must stay a field");
        run.HyperlinkUrl.Should().Be("https://example.com/", "a field placed inside a hyperlink must keep its link on commit");
    }

    [StaFact]
    public void FieldRun_InsideInternalHyperlink_KeepsAnchor()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Title")
        {
            FieldKind = RunFieldKind.FileName,
            HyperlinkAnchor = "bookmark1"
        });
        doc.Blocks.Add(para);

        var run = ((Paragraph)RoundTrip(doc).Blocks[0]).Runs[0];

        run.FieldKind.Should().Be(RunFieldKind.FileName);
        run.HyperlinkAnchor.Should().Be("bookmark1", "a field placed inside an internal link must keep its anchor on commit");
    }

    // --- [MED] Real cell shading equal to the style fill stripped on commit -----------------------

    [StaFact]
    public void CellShading_EqualToHeaderFill_SurvivesCommit()
    {
        // The header-row style fill is #D9E2F3 (HeaderRowFill). An author who shades the header cell that
        // same colour must not have the shading stripped by the colour-equality heuristic.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(2, 2);
        table.Formatting = table.Formatting with { HeaderRow = true };
        table.Rows[0].Cells[0].ShadingColorHex = "#D9E2F3";
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);
        var cell = result.Blocks.OfType<Table>().Single().Rows[0].Cells[0];

        cell.ShadingColorHex.Should().Be("#D9E2F3", "author-set shading must survive even when it equals the header style fill");
    }

    [StaFact]
    public void CellShading_EqualToBandedFill_SurvivesCommit()
    {
        // The banded-row style fill is #F2F2F2 (BandedRowFill). An author shading a banded body cell that
        // same colour must keep the explicit shading.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(3, 2);
        table.Formatting = table.Formatting with { BandedRows = true };
        // Row index 1 is a banded (shaded) body row under the default banding.
        table.Rows[1].Cells[0].ShadingColorHex = "#F2F2F2";
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);
        var cell = result.Blocks.OfType<Table>().Single().Rows[1].Cells[0];

        cell.ShadingColorHex.Should().Be("#F2F2F2", "author-set shading must survive even when it equals the banded style fill");
    }

    [StaFact]
    public void StyleFill_WithoutAuthorShading_DoesNotBecomeCellShading()
    {
        // Guard the other direction: a header cell the author did NOT shade must stay unshaded in the model
        // (the style fill is rendered chrome, re-derived from the HeaderRow toggle, not captured as shading).
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(2, 2);
        table.Formatting = table.Formatting with { HeaderRow = true };
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);
        var cell = result.Blocks.OfType<Table>().Single().Rows[0].Cells[0];

        cell.ShadingColorHex.Should().BeNull("a header style fill must not be captured back as explicit cell shading");
    }

    // --- [LOW] Emptied content-control / comment run dropped on commit ----------------------------

    [StaFact]
    public void EmptiedContentControlRun_KeepsMarker()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        // A plain-text content control whose text the user has emptied.
        para.Runs.Add(new Run(string.Empty)
        {
            Control = new ContentControl(ContentControlKind.PlainText, Alias: "Name")
        });
        doc.Blocks.Add(para);

        var paragraph = (Paragraph)RoundTrip(doc).Blocks[0];

        paragraph.Runs.Should().ContainSingle("the emptied content-control run must be kept, not dropped");
        paragraph.Runs[0].Control.Should().NotBeNull();
        paragraph.Runs[0].Control!.Alias.Should().Be("Name");
    }

    [StaFact]
    public void EmptiedCommentRun_KeepsMarker()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        // A commented run whose text the user has emptied (not the textless comment-reference anchor).
        para.Runs.Add(new Run(string.Empty) { CommentId = 7 });
        doc.Blocks.Add(para);
        doc.Comments[7] = new Comment(7) { Author = "QA" };

        var paragraph = (Paragraph)RoundTrip(doc).Blocks[0];

        paragraph.Runs.Should().ContainSingle("the emptied commented run must be kept, not dropped");
        paragraph.Runs[0].CommentId.Should().Be(7);
    }

    // --- [LOW] MultiLevel list degrades to Number after an in-editor edit -------------------------

    [StaFact]
    public void MultiLevelList_SurvivesRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var (text, level) in new[] { ("One", 0), ("One.One", 1), ("Two", 0) })
        {
            doc.Blocks.Add(new Paragraph(text)
            {
                Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = level }
            });
        }

        var result = RoundTrip(doc);
        var listParas = result.Blocks.OfType<Paragraph>().ToList();

        listParas.Select(p => p.PlainText).Should().Equal("One", "One.One", "Two");
        listParas.Should().OnlyContain(p => p.Formatting.ListKind == ListKind.MultiLevel,
            "a MultiLevel list must not degrade to Number across a Render/CommitToModel cycle");
    }

    [StaFact]
    public void NumberList_StillRoundTripsAsNumber()
    {
        // Guard that the MultiLevel fix did not change a plain Number list (both render with a Decimal marker).
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var text in new[] { "First", "Second" })
        {
            doc.Blocks.Add(new Paragraph(text)
            {
                Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number }
            });
        }

        var result = RoundTrip(doc);
        var listParas = result.Blocks.OfType<Paragraph>().ToList();

        listParas.Should().OnlyContain(p => p.Formatting.ListKind == ListKind.Number);
    }
}
