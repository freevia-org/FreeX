using System.IO;
using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Tests for footnote and endnote content rendering in <see cref="PageBox"/> note regions.
///
/// <list type="bullet">
///   <item>A document with footnotes produces a page box whose <see cref="PageBox.FootnoteIds"/>
///     contains the referenced footnote IDs in order of appearance.</item>
///   <item>Fitting endnotes attach to the final body page; measured overflow appends a dedicated page.</item>
///   <item>Footnote IDs in the page box match the inline reference superscripts (same numeric IDs).</item>
///   <item>Endnote IDs in the synthetic page match the inline reference superscripts.</item>
///   <item>A document with both footnotes and endnotes produces the correct regions on the correct boxes.</item>
///   <item>A plain document (no notes) produces no note IDs on any page box.</item>
/// </list>
///
/// <para>Runs on STA because tests create real WPF DocumentView / PaginatedEditorPanel instances.</para>
/// </summary>
public sealed class PagedEditNoteRegionTests
{
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 1. Footnote IDs appear on the body page box
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A document containing two footnote references must produce at least one page box whose
    /// <see cref="PageBox.FootnoteIds"/> contains both footnote IDs.
    /// </summary>
    [StaFact]
    public void FootnoteIds_ArePopulated_OnBodyPageBox()
    {
        var doc = BuildDocWithFootnotes(2);
        var (panel, _) = BuildPanel(doc);

        // Collect all footnote IDs across body page boxes (overflow pagination may place them all
        // on box 0).
        var allFootnoteIds = panel.PageBoxes
            .Where(b => !b.IsEndnoteSyntheticPage)
            .SelectMany(b => b.FootnoteIds)
            .ToList();

        allFootnoteIds.Should().Contain(1, "footnote ID 1 must appear on a body page box");
        allFootnoteIds.Should().Contain(2, "footnote ID 2 must appear on a body page box");
    }

    /// <summary>
    /// A document with a single footnote reference must expose that footnote ID on the first page box.
    /// </summary>
    [StaFact]
    public void FootnoteIds_SingleFootnote_AppearsOnFirstPageBox()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var fn = new Footnote(1, "The one footnote.");
        doc.Footnotes[1] = fn;

        var body = new Paragraph();
        body.Runs.Add(new Run("Body text."));
        body.Runs.Add(Run.FootnoteReference(1));
        doc.Blocks.Add(body);

        var (panel, _) = BuildPanel(doc);

        panel.PageBoxes[0].FootnoteIds
            .Should().Contain(1, "footnote ID 1 must appear on page box 0");
    }

    /// <summary>
    /// The footnote IDs on the page box must match the IDs used in the body run references —
    /// confirming the number shown next to the note text matches the in-body superscript.
    /// </summary>
    [StaFact]
    public void FootnoteIds_MatchBodyRunReferences()
    {
        // Build a document with footnote IDs 3 and 7 (non-consecutive, to prove ID pass-through).
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        doc.Footnotes[3] = new Footnote(3, "Footnote three.");
        doc.Footnotes[7] = new Footnote(7, "Footnote seven.");

        var body = new Paragraph();
        body.Runs.Add(new Run("Word"));
        body.Runs.Add(Run.FootnoteReference(3));
        body.Runs.Add(new Run(" and another"));
        body.Runs.Add(Run.FootnoteReference(7));
        doc.Blocks.Add(body);

        var (panel, _) = BuildPanel(doc);

        var allFootnoteIds = panel.PageBoxes
            .Where(b => !b.IsEndnoteSyntheticPage)
            .SelectMany(b => b.FootnoteIds)
            .ToList();

        allFootnoteIds.Should().Contain(3, "footnote ID 3 must be collected from the body run");
        allFootnoteIds.Should().Contain(7, "footnote ID 7 must be collected from the body run");
    }

    [StaFact]
    public void FootnoteMarkers_ExposeIdsAlongsideTheirPaginatorPositions()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Footnotes[3] = new Footnote(3, "Footnote three.");
        doc.Footnotes[7] = new Footnote(7, "Footnote seven.");

        var body = new Paragraph();
        body.Runs.Add(new Run("First"));
        body.Runs.Add(Run.FootnoteReference(3));
        body.Runs.Add(new Run(" second"));
        body.Runs.Add(Run.FootnoteReference(7));
        doc.Blocks.Add(body);

        var view = new DocumentView();
        view.LoadModel(doc);

        var markers = DocumentView.CollectFootnoteMarkers(view.Document.Blocks);

        markers.Select(marker => marker.FootnoteId).Should().Equal(3, 7);
        Assert.All(markers, marker => Assert.NotNull(marker.Position));
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 2. Endnote physical-page ownership
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void FootnotePlacementFixture_AssignsReferencesToTheirWordLikeBodyPage()
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildFootnotePlacementDocument();
        var (panel, editor) = BuildPanel(doc);

        var assignment = PaginationEngine.ComputeBlockPageAssignment(editor);
        var blocks = editor.Document.Blocks.ToArray();
        var diagnostics = string.Join(
            Environment.NewLine,
            blocks.Select((block, index) =>
                $"block={index} page={assignment.ElementAtOrDefault(index)} text={new System.Windows.Documents.TextRange(block.ContentStart, block.ContentEnd).Text.Trim()}"));
        Assert.True(panel.PageBoxes.Count == 2, diagnostics);
        Assert.True(panel.PageBoxes[0].FootnoteIds.SequenceEqual([1, 2]), diagnostics);
        Assert.Empty(panel.PageBoxes[1].FootnoteIds);
    }

    /// <summary>
    /// Fitting endnotes remain on the final body page, matching Word's document-end placement.
    /// </summary>
    [StaFact]
    public void FittingEndnotes_AttachToFinalBodyPage()
    {
        var doc = BuildDocWithEndnotes(2);
        var (panel, _) = BuildPanel(doc);

        panel.PageBoxes.Should().HaveCount(1);
        panel.PageBoxes[0].IsEndnoteSyntheticPage.Should().BeFalse();
        panel.PageBoxes[0].EndnoteIds.Should().Equal(1, 2);
    }

    /// <summary>
    /// The body page carrying fitting endnotes remains the last page in the panel.
    /// </summary>
    [StaFact]
    public void FittingEndnoteBodyPage_IsLast_InPageBoxList()
    {
        var doc = BuildDocWithEndnotes(2);
        var (panel, _) = BuildPanel(doc);

        panel.PageBoxes.Last().EndnoteIds.Should().Equal(1, 2);
        panel.PageBoxes.Last().IsEndnoteSyntheticPage.Should().BeFalse();
    }

    /// <summary>
    /// The final body page must contain all fitting endnote IDs from the document.
    /// </summary>
    [StaFact]
    public void EndnoteIds_AreAllPresent_OnFinalBodyPage()
    {
        var doc = BuildDocWithEndnotes(2);
        var (panel, _) = BuildPanel(doc);

        var finalBox = panel.PageBoxes.Last();

        finalBox.EndnoteIds.Should().Contain(1, "endnote ID 1 must appear on the final body page");
        finalBox.EndnoteIds.Should().Contain(2, "endnote ID 2 must appear on the final body page");
    }

    /// <summary>
    /// Endnote IDs on the synthetic page must match the IDs used in the body run references.
    /// </summary>
    [StaFact]
    public void EndnoteIds_MatchBodyRunReferences()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        doc.Endnotes[5] = new Endnote(5, "Endnote five.");
        doc.Endnotes[9] = new Endnote(9, "Endnote nine.");

        var body = new Paragraph();
        body.Runs.Add(new Run("Text"));
        body.Runs.Add(Run.EndnoteReference(5));
        body.Runs.Add(new Run(" more"));
        body.Runs.Add(Run.EndnoteReference(9));
        doc.Blocks.Add(body);

        var (panel, _) = BuildPanel(doc);

        var endnoteBox = panel.PageBoxes.Single(b => b.EndnoteIds.Count > 0);
        endnoteBox.EndnoteIds.Should().Contain(5, "endnote ID 5 must match the body run reference");
        endnoteBox.EndnoteIds.Should().Contain(9, "endnote ID 9 must match the body run reference");
    }

    /// <summary>
    /// Endnote IDs on the synthetic page must be in ascending key order (matching the sequential
    /// numbering Word assigns them).
    /// </summary>
    [StaFact]
    public void EndnoteIds_AreInAscendingOrder_OnSyntheticPage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        // Add endnotes out of insertion order to verify they are sorted by ID, not insertion.
        doc.Endnotes[3] = new Endnote(3, "Third endnote.");
        doc.Endnotes[1] = new Endnote(1, "First endnote.");
        doc.Endnotes[2] = new Endnote(2, "Second endnote.");

        var body = new Paragraph();
        body.Runs.Add(Run.EndnoteReference(1));
        body.Runs.Add(Run.EndnoteReference(2));
        body.Runs.Add(Run.EndnoteReference(3));
        doc.Blocks.Add(body);

        var (panel, _) = BuildPanel(doc);

        var endnoteBox = panel.PageBoxes.Single(b => b.EndnoteIds.Count > 0);
        endnoteBox.EndnoteIds.Should().BeInAscendingOrder(
            "endnote IDs must be ordered by key so they match the sequential numbering");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 3. No-notes document
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A plain document (no footnotes, no endnotes) must produce page boxes with empty
    /// FootnoteIds / EndnoteIds, and no synthetic endnotes page.
    /// </summary>
    [StaFact]
    public void NoNotes_ProducesNoNoteIds_AndNoSyntheticPage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Just body text. No footnotes. No endnotes."));

        var (panel, _) = BuildPanel(doc);

        panel.PageBoxes.Should().NotContain(b => b.IsEndnoteSyntheticPage,
            "no synthetic endnotes page must be added when the document has no endnotes");

        panel.PageBoxes.SelectMany(b => b.FootnoteIds)
            .Should().BeEmpty("no footnote IDs must appear when the document has no footnotes");

        panel.PageBoxes.SelectMany(b => b.EndnoteIds)
            .Should().BeEmpty("no endnote IDs must appear when the document has no endnotes");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 4. Document with both footnotes and endnotes
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A short document with both note kinds keeps both regions on its final body page.
    /// </summary>
    [StaFact]
    public void MixedNotes_ProducesFootnotesAndFittingEndnotesOnBodyPage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        doc.Footnotes[1] = new Footnote(1, "The footnote.");
        doc.Endnotes[1] = new Endnote(1, "The endnote.");

        var body = new Paragraph();
        body.Runs.Add(new Run("Body"));
        body.Runs.Add(Run.FootnoteReference(1));
        body.Runs.Add(new Run(" more body"));
        body.Runs.Add(Run.EndnoteReference(1));
        doc.Blocks.Add(body);

        var (panel, _) = BuildPanel(doc);

        // Body boxes must have footnote IDs.
        var bodyBoxes = panel.PageBoxes.Where(b => !b.IsEndnoteSyntheticPage).ToList();
        bodyBoxes.SelectMany(b => b.FootnoteIds)
            .Should().Contain(1, "footnote ID 1 must appear on a body page box");

        var endnoteBox = panel.PageBoxes.Single(b => b.EndnoteIds.Count > 0);
        endnoteBox.IsEndnoteSyntheticPage.Should().BeFalse();
        endnoteBox.EndnoteIds.Should().Contain(1, "endnote ID 1 must appear on the body page");
        endnoteBox.FootnoteIds.Should().Contain(1, "the fitting body page also owns its footnote");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 5. Synthetic endnotes page has no body blocks
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Measured overflow retains a dedicated final page with no header/footer sub-editors.
    /// </summary>
    [StaFact]
    public void OverflowEndnotes_UseDedicatedPageWithoutHeaderFooterSubEditors()
    {
        var doc = DocxReader.Read(RepositoryFile(
            "freew-fidelity-corpus", "files", "review", "endnotes.docx"));
        var (panel, _) = BuildPanel(doc);

        var syntheticBox = panel.PageBoxes.First(b => b.IsEndnoteSyntheticPage);

        panel.PageBoxes.Should().HaveCount(3);
        syntheticBox.EndnoteIds.Should().Equal(1, 2);
        syntheticBox.HeaderSubEditor.Should().BeNull(
            "synthetic endnotes page must have no header sub-editor");
        syntheticBox.FooterSubEditor.Should().BeNull(
            "synthetic endnotes page must have no footer sub-editor");
    }

    [StaFact]
    public void FittingEndnoteOwnership_SurvivesRepaginateAndUndoRebuild()
    {
        var doc = BuildDocWithEndnotes(2);
        var (panel, _) = BuildPanel(doc);

        panel.Repaginate();
        panel.PageBoxes.Should().HaveCount(1);
        panel.PageBoxes[0].EndnoteIds.Should().Equal(1, 2);
        panel.PageBoxes[0].IsEndnoteSyntheticPage.Should().BeFalse();

        panel.Rebuild();
        panel.PageBoxes.Should().HaveCount(1);
        panel.PageBoxes[0].EndnoteIds.Should().Equal(1, 2);
        panel.PageBoxes[0].IsEndnoteSyntheticPage.Should().BeFalse();
    }

    [StaFact]
    public void OverflowEndnoteOwnership_SurvivesRepaginateAndUndoRebuild()
    {
        var doc = DocxReader.Read(RepositoryFile(
            "freew-fidelity-corpus", "files", "review", "endnotes.docx"));
        var (panel, _) = BuildPanel(doc);

        panel.Repaginate();
        panel.PageBoxes.Should().HaveCount(3);
        panel.PageBoxes.Last().IsEndnoteSyntheticPage.Should().BeTrue();
        panel.PageBoxes.Last().EndnoteIds.Should().Equal(1, 2);

        panel.Rebuild();
        panel.PageBoxes.Should().HaveCount(3);
        panel.PageBoxes.Last().IsEndnoteSyntheticPage.Should().BeTrue();
        panel.PageBoxes.Last().EndnoteIds.Should().Equal(1, 2);
    }

    [StaFact]
    public void DedicatedEndnotePage_UsesFinalSectionGeometryAcrossRebuilds()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Portrait section")
        {
            SectionBreak = new Section(new PageSettings
            {
                WidthPt = 612,
                HeightPt = 792,
                Landscape = false,
                MarginLeftPt = 72,
                MarginRightPt = 72,
                MarginTopPt = 72,
                MarginBottomPt = 72
            }, SectionBreakKind.NextPage)
        });
        doc.Page.WidthPt = 792;
        doc.Page.HeightPt = 612;
        doc.Page.Landscape = true;
        doc.Page.MarginLeftPt = 36;
        doc.Page.MarginRightPt = 48;
        doc.Page.MarginTopPt = 54;
        doc.Page.MarginBottomPt = 60;
        doc.Blocks.Add(new Paragraph("Final landscape section"));
        doc.Endnotes[1] = new Endnote(1,
            string.Join(' ', Enumerable.Repeat("overflowing endnote content", 1000)));

        var (panel, _) = BuildPanel(doc);

        AssertFinalSectionGeometry(panel.PageBoxes.Last(), doc.Page);
        panel.Repaginate();
        AssertFinalSectionGeometry(panel.PageBoxes.Last(), doc.Page);
        panel.Rebuild();
        AssertFinalSectionGeometry(panel.PageBoxes.Last(), doc.Page);
    }

    [StaFact]
    public void FittingEndnotes_InMultiSectionDocument_AttachToFinalBodyPageAcrossRebuilds()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Portrait section")
        {
            SectionBreak = new Section(new PageSettings
            {
                WidthPt = 612,
                HeightPt = 792,
                Landscape = false
            }, SectionBreakKind.NextPage)
        });
        doc.Page.WidthPt = 792;
        doc.Page.HeightPt = 612;
        doc.Page.Landscape = true;
        doc.Blocks.Add(new Paragraph("Final landscape section"));
        doc.Endnotes[1] = new Endnote(1, "Fitting endnote body");

        var (panel, _) = BuildPanel(doc);

        AssertFittingFinalSectionOwnership(panel, doc.Page);
        panel.Repaginate();
        AssertFittingFinalSectionOwnership(panel, doc.Page);
        panel.Rebuild();
        AssertFittingFinalSectionOwnership(panel, doc.Page);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static (PaginatedEditorPanel panel, DocumentView editor) BuildPanel(TextDocument doc)
    {
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        var panel = PaginatedEditorPanel.Build(editor);
        return (panel, editor);
    }

    /// <summary>Builds a document with <paramref name="count"/> footnotes referenced inline.</summary>
    private static TextDocument BuildDocWithFootnotes(int count)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        for (var i = 1; i <= count; i++)
            doc.Footnotes[i] = new Footnote(i, $"Footnote {i} content.");

        var para = new Paragraph();
        para.Runs.Add(new Run("Opening sentence."));
        for (var i = 1; i <= count; i++)
        {
            para.Runs.Add(new Run($" Text before footnote {i}."));
            para.Runs.Add(Run.FootnoteReference(i));
        }
        doc.Blocks.Add(para);
        return doc;
    }

    /// <summary>Builds a document with <paramref name="count"/> endnotes referenced inline.</summary>
    private static TextDocument BuildDocWithEndnotes(int count)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        for (var i = 1; i <= count; i++)
            doc.Endnotes[i] = new Endnote(i, $"Endnote {i} content.");

        var para = new Paragraph();
        para.Runs.Add(new Run("Opening sentence."));
        for (var i = 1; i <= count; i++)
        {
            para.Runs.Add(new Run($" Text before endnote {i}."));
            para.Runs.Add(Run.EndnoteReference(i));
        }
        doc.Blocks.Add(para);
        return doc;
    }

    private static string RepositoryFile(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }

    private static void AssertFinalSectionGeometry(PageBox box, PageSettings expected)
    {
        box.IsEndnoteSyntheticPage.Should().BeTrue();
        box.PageGeometry.WidthPt.Should().Be(expected.WidthPt);
        box.PageGeometry.HeightPt.Should().Be(expected.HeightPt);
        box.PageGeometry.Landscape.Should().Be(expected.Landscape);
        box.PageGeometry.MarginLeftPt.Should().Be(expected.MarginLeftPt);
        box.PageGeometry.MarginRightPt.Should().Be(expected.MarginRightPt);
        box.PageGeometry.MarginTopPt.Should().Be(expected.MarginTopPt);
        box.PageGeometry.MarginBottomPt.Should().Be(expected.MarginBottomPt);
    }

    private static void AssertFittingFinalSectionOwnership(PaginatedEditorPanel panel, PageSettings expected)
    {
        panel.PageBoxes.Count.Should().BeGreaterThanOrEqualTo(2);
        panel.PageBoxes.Should().NotContain(box => box.IsEndnoteSyntheticPage);
        var finalBox = panel.PageBoxes.Last();
        finalBox.EndnoteIds.Should().Equal(1);
        finalBox.PageGeometry.WidthPt.Should().Be(expected.WidthPt);
        finalBox.PageGeometry.HeightPt.Should().Be(expected.HeightPt);
        finalBox.PageGeometry.Landscape.Should().Be(expected.Landscape);
    }
}
