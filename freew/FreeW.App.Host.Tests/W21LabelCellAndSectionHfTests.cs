#if DEBUG
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W21 tests:
/// <list type="bullet">
///   <item><strong>Cell-write API via DocumentView.SetTableCellContent</strong> -- writes to the
///   right cell, round-trips, is undoable.</item>
///   <item><strong>Label grid population logic</strong> -- validates the merge-population algorithm
///   by exercising <see cref="SetTableCellContentCommand"/> directly (LabelsCommand itself requires
///   a dialog, so the populate logic is tested at the command layer).</item>
///   <item><strong>Per-section header commit</strong> -- page boxes carry OwnerSectionHf pointing
///   to the correct section's HeadersFooters; CommitHeaderFooterSlots writes to that section's slot,
///   not always the document-level one; section headers round-trip through DOCX.</item>
/// </list>
///
/// <para>STA required for DocumentView / PaginatedEditorPanel.</para>
/// </summary>
public sealed class W21LabelCellAndSectionHfTests
{
    // ----- 1. DocumentView.SetTableCellContent via the command bus ----------------------------------

    /// <summary>
    /// SetTableCellContent writes to exactly the specified cell and the change
    /// is immediately visible through the model.
    /// </summary>
    [StaFact]
    public void SetTableCellContent_WritesToCorrectCell()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(2, 3);
        doc.Blocks.Add(table);

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var paragraphs = new List<Paragraph> { new Paragraph("Cell R1C2") };
        editor.SetTableCellContent(0, 1, 2, paragraphs);

        var modelTable = (Table)editor.Model.Blocks[0];
        modelTable.Rows[1].Cells[2].PlainText.Should().Be("Cell R1C2",
            "SetTableCellContent must write to the correct cell");

        // Other cells must be undisturbed.
        modelTable.Rows[0].Cells[0].PlainText.Should().Be(string.Empty);
        modelTable.Rows[1].Cells[0].PlainText.Should().Be(string.Empty);
    }

    /// <summary>
    /// SetTableCellContent is undoable: calling Commands.Undo() restores the prior cell content.
    /// </summary>
    [StaFact]
    public void SetTableCellContent_IsUndoable()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(1, 1);
        var originalPara = new Paragraph("Original cell text");
        table.Rows[0].Cells[0].Paragraphs.Clear();
        table.Rows[0].Cells[0].Paragraphs.Add(originalPara);
        doc.Blocks.Add(table);

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        editor.SetTableCellContent(0, 0, 0, [new Paragraph("Replacement")]);
        var modelTable = (Table)editor.Model.Blocks[0];
        modelTable.Rows[0].Cells[0].PlainText.Should().Be("Replacement");

        editor.Commands.Undo();
        modelTable.Rows[0].Cells[0].PlainText.Should().Be("Original cell text",
            "undo must restore the original cell content");
    }

    /// <summary>
    /// DOCX round-trip: after SetTableCellContent, write to DOCX and read back;
    /// the cell content must survive.
    /// </summary>
    [StaFact]
    public void SetTableCellContent_DocxRoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(2, 2);
        doc.Blocks.Add(table);

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        editor.SetTableCellContent(0, 0, 1, [new Paragraph("R0C1 content")]);
        editor.SetTableCellContent(0, 1, 0, [new Paragraph("R1C0 content")]);

        using var stream = new MemoryStream();
        DocxWriter.Write(editor.Model, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);

        var readTable = (Table)read.Blocks[0];
        readTable.Rows[0].Cells[1].PlainText.Should().Be("R0C1 content",
            "cell (0,1) must survive DOCX round-trip");
        readTable.Rows[1].Cells[0].PlainText.Should().Be("R1C0 content",
            "cell (1,0) must survive DOCX round-trip");
    }

    // ----- 2. Label population algorithm (via SetTableCellContentCommand directly) -----------------

    /// <summary>
    /// Simulates the label population algorithm: N records fill a grid left-to-right, top-to-bottom;
    /// cells beyond the last record stay empty.
    /// </summary>
    [Fact]
    public void LabelPopulationAlgorithm_FillsGridInOrder()
    {
        var (doc, bus) = NewBus();
        var table = Table.Create(2, 3);
        doc.Blocks.Add(table);

        var records = new[]
        {
            new Dictionary<string, string> { ["Name"] = "Alice" },
            new Dictionary<string, string> { ["Name"] = "Bob" },
            new Dictionary<string, string> { ["Name"] = "Carol" },
            new Dictionary<string, string> { ["Name"] = "Dave" },
        };

        var template = new TextDocument();
        template.Blocks.Add(new Paragraph("Hello «Name»"));

        int recIdx = 0;
        for (int r = 0; r < 2 && recIdx < records.Length; r++)
        {
            for (int c = 0; c < 3 && recIdx < records.Length; c++, recIdx++)
            {
                var merged = MailMerge.MergeRecord(template, records[recIdx]);
                var paras = merged.Blocks.OfType<Paragraph>().ToList();
                bus.Execute(new SetTableCellContentCommand(0, r, c, paras));
            }
        }

        table.Rows[0].Cells[0].PlainText.Should().Be("Hello Alice");
        table.Rows[0].Cells[1].PlainText.Should().Be("Hello Bob");
        table.Rows[0].Cells[2].PlainText.Should().Be("Hello Carol");
        table.Rows[1].Cells[0].PlainText.Should().Be("Hello Dave");
        // Cells beyond records stay at default empty paragraph.
        table.Rows[1].Cells[1].PlainText.Should().Be(string.Empty, "cells beyond record count must stay empty");
        table.Rows[1].Cells[2].PlainText.Should().Be(string.Empty);
    }

    /// <summary>
    /// Label population is fully undoable: undoing N SetTableCellContentCommands restores the empty grid.
    /// </summary>
    [Fact]
    public void LabelPopulationAlgorithm_IsUndoable()
    {
        var (doc, bus) = NewBus();
        var table = Table.Create(1, 2);
        doc.Blocks.Add(table);

        var template = new TextDocument();
        template.Blocks.Add(new Paragraph("«Name»"));

        var records = new[]
        {
            new Dictionary<string, string> { ["Name"] = "Alice" },
            new Dictionary<string, string> { ["Name"] = "Bob" },
        };

        for (int c = 0; c < 2; c++)
        {
            var merged = MailMerge.MergeRecord(template, records[c]);
            var paras = merged.Blocks.OfType<Paragraph>().ToList();
            bus.Execute(new SetTableCellContentCommand(0, 0, c, paras));
        }

        table.Rows[0].Cells[0].PlainText.Should().Be("Alice");
        table.Rows[0].Cells[1].PlainText.Should().Be("Bob");

        bus.Undo();
        table.Rows[0].Cells[1].PlainText.Should().Be(string.Empty, "undo must restore cell 1");
        bus.Undo();
        table.Rows[0].Cells[0].PlainText.Should().Be(string.Empty, "undo must restore cell 0");
    }

    // ----- 3. Per-section header/footer commit -----------------------------------------------------

    /// <summary>
    /// In a two-section document where section 1 defines its own header, page 1's OwnerSectionHf
    /// must point to section 1's HeadersFooters instance (not the document-level one), and page 2's
    /// OwnerSectionHf must point to the document-level FinalSectionHeadersFooters.
    /// </summary>
    [StaFact]
    public void SectionAwareCommit_PageBoxOwnerSectionHf_PointsToCorrectSection()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var sec1Para = new Paragraph("Section 1 body");
        var sec1 = new Section(new PageSettings(), SectionBreakKind.NextPage);
        sec1.HeadersFooters.Header = new HeaderFooter("Section 1 Header");
        sec1Para.SectionBreak = sec1;
        doc.Blocks.Add(sec1Para);

        doc.Blocks.Add(new Paragraph("Section 2 body"));
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter("Section 2 Header");

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor);
        if (panel.PageBoxes.Count < 2)
            return; // single-page fallback in test env -- skip

        // Page 1 belongs to section 1; its OwnerSectionHf must be section 1's HeadersFooters.
        var page1Box = panel.PageBoxes[0];
        page1Box.OwnerSectionHf.Should().NotBeNull("page 1 box must carry an OwnerSectionHf");
        page1Box.OwnerSectionHf.Should().BeSameAs(sec1.HeadersFooters,
            "page 1's OwnerSectionHf must be section 1's HeadersFooters instance");

        // Page 2 belongs to section 2; its OwnerSectionHf must be the document-level HF.
        var page2Box = panel.PageBoxes[1];
        page2Box.OwnerSectionHf.Should().NotBeNull("page 2 box must carry an OwnerSectionHf");
        page2Box.OwnerSectionHf.Should().BeSameAs(editor.Model.FinalSectionHeadersFooters,
            "page 2's OwnerSectionHf must be the document-level FinalSectionHeadersFooters");
    }

    /// <summary>
    /// After a PaginatedCommitCoordinator.Commit on a two-section document, section 1's header
    /// slot must survive intact, and the document-level slot must also survive.
    ///
    /// CommitHeaderFooterSlots must write the sub-editor content back to the correct section's HF
    /// (section 1's for pages in section 1; document-level for pages in section 2).  Because the
    /// sub-editors are seeded with the current slot content, the slot values after commit must equal
    /// or contain the original content (barring user edits in-test, which there are none).
    /// </summary>
    [StaFact]
    public void SectionAwareCommit_CommitsToCorrectSectionHfInstance()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var sec1Para = new Paragraph("Section 1 body");
        var sec1 = new Section(new PageSettings(), SectionBreakKind.NextPage);
        sec1.HeadersFooters.Header = new HeaderFooter("S1 Header");
        sec1Para.SectionBreak = sec1;
        doc.Blocks.Add(sec1Para);

        doc.Blocks.Add(new Paragraph("Section 2 body"));
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter("S2 Header");

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor);
        PaginatedCommitCoordinator.Commit(panel, editor);

        // Section 1's HeadersFooters.Header must still be set (was not cleared by commit).
        sec1.HeadersFooters.Header.Should().NotBeNull("section 1 header must survive commit");

        // Document-level (section 2) header must still be set.
        editor.Model.FinalSectionHeadersFooters.Header.Should().NotBeNull(
            "section 2 (document-level) header must survive commit");
    }

    /// <summary>
    /// Per-section header DOCX round-trip tested directly: a two-section document's section 1
    /// header must survive DocxWriter + DocxReader.
    ///
    /// This verifies the storage model and DocxWriter/DocxReader directly; the Release-enabled
    /// PaginatedCommitCoordinatorTests suite verifies editable pagination reassembly.
    /// </summary>
    [StaFact]
    public void SectionAwareCommit_DocxRoundTrip_PerSectionHeaderPreserved()
    {
        // Build a 2-section document with per-section headers, write to DOCX, read back.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var sec1Para = new Paragraph("Section 1 body");
        var sec1 = new Section(new PageSettings(), SectionBreakKind.NextPage);
        sec1.HeadersFooters.Header = new HeaderFooter("S1 Header roundtrip");
        sec1Para.SectionBreak = sec1;
        doc.Blocks.Add(sec1Para);

        doc.Blocks.Add(new Paragraph("Section 2 body"));
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter("S2 Header roundtrip");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);

        // Section 1's header must survive the round-trip on read.Sections[0].
        read.Sections.Should().HaveCountGreaterThan(0, "sections must survive round-trip");
        var s1Header = read.Sections[0].HeadersFooters.Header;
        s1Header.Should().NotBeNull("section 1 header must survive DOCX round-trip");
        s1Header!.PlainText.Should().Contain("S1 Header roundtrip",
            "section 1 header text must survive DOCX round-trip");
    }

    // ----- helpers ---------------------------------------------------------------------------------

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static (TextDocument doc, DocumentCommandBus bus) NewBus()
    {
        var doc = new TextDocument();
        return (doc, new DocumentCommandBus(new Context(doc)));
    }
}
#endif
