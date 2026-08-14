using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-REF: tests for the References tab — Insert Footnote / Endnote, Table of Contents (insert + update),
/// Insert Caption (Figure / Table), Cross-reference, and Citation / Bibliography. Covers the DocumentView
/// insert methods (model mutation + undo) and that every References command id resolves in the registry.
/// Pure-model — no headless Avalonia backend required.
/// </summary>
public sealed class ReferencesTabTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static FreeWRibbonHostExecutionPorts NoopCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { }, OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
            OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { }, OpenWordCountDialog: () => { },
            InsertPicture: () => { }, ApplyZoom: (_, _) => { });

    private static DocumentView ViewWith(params Block[] blocks)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        if (blocks.Length == 0)
            doc.Blocks.Add(new Paragraph("Body paragraph"));
        else
            doc.Blocks.AddRange(blocks);
        var view = new DocumentView();
        view.LoadDocument(doc);
        return view;
    }

    private static Paragraph Heading(string text, int level) =>
        new(text) { StyleId = "Heading" + level };

    // ── Footnote / Endnote ───────────────────────────────────────────────────────

    [Fact]
    public void InsertFootnote_adds_note_to_store_and_reference_run_and_undo_reverts()
    {
        var view = ViewWith();

        view.InsertFootnote();

        view.Document.Footnotes.Should().ContainKey(1, "a footnote must be created in the store");
        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.Any(r => r.FootnoteId == 1).Should().BeTrue("a footnote reference run must be appended");

        view.Undo();
        view.Document.Footnotes.Should().BeEmpty("undo removes the note from the store");
        ((Paragraph)view.Document.Blocks[0]).Runs.Any(r => r.FootnoteId is not null)
            .Should().BeFalse("undo removes the reference run");
    }

    [Fact]
    public void InsertEndnote_adds_endnote_to_store_and_reference_run()
    {
        var view = ViewWith();

        view.InsertEndnote();

        view.Document.Endnotes.Should().ContainKey(1);
        ((Paragraph)view.Document.Blocks[0]).Runs.Any(r => r.EndnoteId == 1).Should().BeTrue();

        view.Undo();
        view.Document.Endnotes.Should().BeEmpty();
    }

    [Fact]
    public void InsertFootnote_allocates_incrementing_ids()
    {
        var view = ViewWith();

        view.InsertFootnote();
        view.InsertFootnote();

        view.Document.Footnotes.Keys.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void InsertFootnote_places_marker_at_caret_and_redo_repeats_the_same_edit()
    {
        var view = ViewWith(new Paragraph("before after"));
        view.SetSelectionRangePublic(0, 7, 0, 7);

        view.InsertFootnote("note text");

        var paragraph = (Paragraph)view.Document.Blocks[0];
        paragraph.Runs.Select(run => run.Text).Should().Equal("before ", "1", "after");
        paragraph.Runs[1].FootnoteId.Should().Be(1);
        view.Document.Footnotes[1].PlainText.Should().Be("note text");

        view.Undo();
        paragraph.Runs.Select(run => run.Text).Should().Equal("before after");
        view.Document.Footnotes.Should().BeEmpty();

        view.Redo();
        paragraph.Runs.Select(run => run.Text).Should().Equal("before ", "1", "after");
        view.Document.Footnotes[1].PlainText.Should().Be("note text");
    }

    // ── Table of Contents ──────────────────────────────────────────────────────────

    [Fact]
    public void InsertTableOfContents_generates_entries_from_headings()
    {
        var view = ViewWith(
            Heading("Chapter One", 1),
            new Paragraph("Some body"),
            Heading("Section A", 2));
        var before = view.Document.Blocks.Count;

        view.InsertTableOfContents();

        // One "Contents" heading paragraph + one entry per heading (2 headings).
        var tocParas = view.Document.Blocks.Where(TableOfContents.IsTocParagraph).ToList();
        tocParas.Should().HaveCount(3, "a TOC heading plus one entry per document heading");
        tocParas.Select(b => ((Paragraph)b).PlainText).Should()
            .Contain(new[] { TableOfContents.HeadingText, "Chapter One\t1", "Section A\t1" });

        view.Undo();
        view.Document.Blocks.Count.Should().Be(before, "undo removes the whole generated TOC");
    }

    [Fact]
    public void UpdateTableOfContents_regenerates_in_place_after_heading_change()
    {
        var view = ViewWith(Heading("First", 1), new Paragraph("body"));
        view.InsertTableOfContents();
        var afterInsert = view.Document.Blocks.Count(TableOfContents.IsTocParagraph);
        afterInsert.Should().Be(2, "Contents heading + one entry");

        // Add a second heading at the end, then update the TOC.
        view.Document.Blocks.Add(Heading("Second", 1));
        view.UpdateTableOfContents();

        var entries = view.Document.Blocks.Where(TableOfContents.IsTocParagraph)
            .Select(b => ((Paragraph)b).PlainText).ToList();
        entries.Should().Contain("Second\t1", "update picks up the newly added heading");
        entries.Count(t => t != TableOfContents.HeadingText).Should().Be(2, "now two heading entries");
    }

    [Fact]
    public void UpdateTableOfContents_UsesLogicalPageLabelOfPlacedHeading()
    {
        var view = new DocumentView();
        view.Document.Blocks.Clear();
        view.Document.Blocks.Add(new Paragraph(TableOfContents.HeadingText) { StyleId = TableOfContents.HeadingStyleId });
        view.Document.Blocks.Add(new Paragraph("Old Heading\t9") { StyleId = TableOfContents.EntryStyleId(1) });
        view.Document.Blocks.Add(DocumentOps.CreatePageBreak());
        view.Document.Blocks.Add(Heading("Chapter Two", 1));
        view.Document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        view.Document.Page.PageNumberStartAt = 4;

        view.UpdateTableOfContents();

        view.Document.Blocks.Where(TableOfContents.IsTocParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Chapter Two\tV");
    }

    [Fact]
    public void UpdateTableOfContents_ReplacesNativeOwnedResultWithoutDeletingSourceHeading()
    {
        var field = new ComplexField(" TOC \\o \"1-3\" ");
        var view = ViewWith(
            new Paragraph("Old Heading\t9")
            {
                StyleId = "Normal",
                SpanningFieldStart = field,
                SpanningFieldOwner = field,
                EndsSpanningField = true
            },
            Heading("Source Heading", 1));

        view.UpdateTableOfContents();

        view.Document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Contain("Source Heading");
        view.Document.Blocks.Where(TableOfContents.IsTocParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Source Heading\t1").And.NotContain("Old Heading\t9");
        var generated = view.Document.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.SpanningFieldOwner is { Keyword: "TOC" });
        generated.SpanningFieldStart!.Instruction.Should().Be(TableOfContents.NativeFieldInstruction);
        generated.EndsSpanningField.Should().BeTrue();
    }

    [Fact]
    public Task InsertTableOfContents_stabilizes_page_references_after_generated_region_reflow() =>
        RunOnUiThread(() =>
        {
            var view = ReflowingTableOfContentsView(includeExistingRegion: false);

            view.InsertTableOfContents();
            AssertTableOfContentsPagesStable(view);
        });

    [Fact]
    public Task InsertTableOfContents_preserves_existing_table_of_contents_region() =>
        RunOnUiThread(() =>
        {
            var view = ViewWith(
                new Paragraph(TableOfContents.HeadingText) { StyleId = TableOfContents.HeadingStyleId },
                new Paragraph("Existing Chapter\t9") { StyleId = TableOfContents.EntryStyleId(1) },
                Heading("New Chapter", 1));
            view.MoveCaretToBlockForTest(2, 0);

            view.InsertTableOfContents();

            view.Document.Blocks.Where(TableOfContents.IsTocParagraph)
                .Cast<Paragraph>()
                .Select(paragraph => paragraph.PlainText)
                .Should().Contain("Existing Chapter\t9").And.Contain("New Chapter\t1");
            view.Document.Blocks.Count(TableOfContents.IsTocParagraph).Should().Be(4);

            view.Undo();
            view.Document.Blocks.Where(TableOfContents.IsTocParagraph)
                .Cast<Paragraph>()
                .Select(paragraph => paragraph.PlainText)
                .Should().Equal(TableOfContents.HeadingText, "Existing Chapter\t9");
        });

    [Fact]
    public Task UpdateTableOfContents_stabilizes_page_references_after_replacement_reflow() =>
        RunOnUiThread(() =>
        {
            var view = ReflowingTableOfContentsView(includeExistingRegion: true);

            view.UpdateTableOfContents();
            AssertTableOfContentsPagesStable(view);
        });

    // ── Caption ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void UpdateFields_refreshes_toc_and_bibliography_in_same_pass()
    {
        var view = ViewWith(
            new Paragraph(TableOfContents.HeadingText) { StyleId = TableOfContents.HeadingStyleId },
            new Paragraph("Old Heading") { StyleId = TableOfContents.EntryStyleId(1) },
            Heading("New Heading", 1),
            new Paragraph(Citations.HeadingText) { StyleId = Citations.HeadingStyleId },
            new Paragraph("Old. (1999). Entry.") { StyleId = Citations.EntryStyleId });
        view.Document.Sources.Add(new Source { Tag = "New2024", Author = "New Author", Title = "Fresh Entry", Year = "2024" });

        view.UpdateFields();

        var tocText = view.Document.Blocks.Where(TableOfContents.IsTocParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .ToList();
        tocText.Should().Contain("New Heading\t1");
        tocText.Should().NotContain("Old Heading");

        var bibliographyText = view.Document.Blocks.Where(Citations.IsBibliographyParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .ToList();
        bibliographyText.Should().Contain("New Author. (2024). Fresh Entry.");
        bibliographyText.Should().NotContain("Old. (1999). Entry.");
    }

    [Fact]
    public void UpdateFields_refreshes_existing_table_of_authorities_with_explicit_break_page_references()
    {
        var oldRegion = TableOfAuthorities.Build(new[] { new Citation("Old Case", CitationCategory.Cases) });
        var view = ViewWith(
            new Paragraph("Before"),
            CitationMarkParagraph("Case A", formatted: false),
            DocumentOps.CreatePageBreak(),
            CitationMarkParagraph("Case A", formatted: false),
            oldRegion[0],
            oldRegion[1],
            oldRegion[2],
            new Paragraph("After"));

        view.UpdateFields();

        view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "Case A\t1, 2");
        view.Document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().NotContain("Old Case")
            .And.EndWith("After");

        var entry = view.Document.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.Runs.Select(run => run.Text).Should().Equal("Case A", "\t", "1, 2");
    }

    [Fact]
    public Task UpdateFields_refreshes_existing_table_of_authorities_with_overflow_page_references() => RunOnUiThread(() =>
    {
        var oldRegion = TableOfAuthorities.Build(new[] { new Citation("Old Case", CitationCategory.Cases) });
        var blocks = new List<Block>
        {
            CitationMarkParagraph("Overflow Case", formatted: false)
        };
        for (var i = 0; i < 120; i++)
            blocks.Add(new Paragraph($"Overflow filler {i + 1}: The quick brown fox jumps over the lazy dog."));
        blocks.Add(CitationMarkParagraph("Overflow Case", formatted: false));
        blocks.AddRange(oldRegion);

        var view = ViewWith(blocks.ToArray());
        view.Document.Page.WidthPt = 300;
        view.Document.Page.HeightPt = 220;
        view.Document.Page.MarginTopPt = 18;
        view.Document.Page.MarginBottomPt = 18;
        view.Document.Page.MarginLeftPt = 18;
        view.Document.Page.MarginRightPt = 18;
        view.Measure(new global::Avalonia.Size(800, 4000));

        view.UpdateFields();

        var entry = view.Document.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.PlainText.Should().MatchRegex(@"^Overflow Case\t1, [2-9][0-9]*$");
        entry.Runs.Select(run => run.Text).Should().HaveCount(3);
    });

    [Fact]
    public void RefreshTableOfAuthorities_uses_direct_and_nested_paginated_table_citation_pages()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        document.Blocks.RemoveAt(0);
        var table = document.Blocks.OfType<Table>().Single();
        table.Rows[1].Cells[0].Paragraphs[0] = CitationMarkParagraph("Table Case", formatted: false);
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0] = CitationMarkParagraph("Table Case", formatted: false);
        table.Rows[8].Cells[0].NestedTables.Add(nested);
        var oldRegion = TableOfAuthorities.Build(new[] { new Citation("Old Case", CitationCategory.Cases) });
        document.Blocks.AddRange(oldRegion);
        document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        document.Page.PageNumberStartAt = 4;
        var view = new DocumentView();
        view.LoadDocument(document);

        view.RefreshTableOfAuthorities();

        var entry = document.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.PlainText.Should().Be("Table Case\tIV, V");
        document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().NotContain("Old Case");
    }

    [Fact]
    public Task RefreshTableOfAuthorities_uses_distinct_pages_for_passim_and_preserves_options() => RunOnUiThread(() =>
    {
        var blocks = new List<Block> { new Paragraph("Before") };
        for (var i = 0; i < 5; i++)
        {
            var mark = Run.CitationMark(new Citation("Roe v. Wade", CitationCategory.Cases));
            if (i == 0)
                mark.Formatting = new RunFormatting { Bold = true, Underline = true, ColorHex = "#C00000" };
            blocks.Add(new Paragraph { Runs = { mark } });
        }

        blocks.AddRange(TableOfAuthorities.Build(new[] { new Citation("Old Case", CitationCategory.Cases) }));
        blocks.Add(new Paragraph("After"));
        var view = ViewWith(blocks.ToArray());
        view.Document.Page.WidthPt = 700;
        view.Document.Page.MarginLeftPt = 80;
        view.Document.Page.MarginRightPt = 90;
        view.Measure(new global::Avalonia.Size(800, 4000));

        view.RefreshTableOfAuthorities(new ToaOptions
        {
            CategoryFilter = CitationCategory.Cases,
            KeepOriginalFormatting = true,
            UsePassim = true,
            TabLeader = ToaTabLeader.Dashes
        });

        view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "Roe v. Wade\t1");
        view.Document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().NotContain("Old Case")
            .And.EndWith("After");

        var entry = view.Document.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.Formatting.TabStops.Should().Equal(
            new TabStop(530, TabStopAlignment.Right, TabLeader.Dashes));
        entry.Runs.Select(run => run.Text).Should().Equal("Roe v. Wade", "\t", "1");
        view.Document.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.CategoryStyleId)
            .SpanningFieldStart!.Instruction.Should().Be(" TOA \\h \\c \"1\" \\p ");
        entry.Runs[0].Formatting.Should().Be(new RunFormatting
        {
            Bold = true,
            Underline = true,
            ColorHex = "#C00000"
        });
    });

    [Fact]
    public void InsertCaption_inserts_autonumbered_paragraph_after_caret()
    {
        var view = ViewWith();

        view.InsertCaption(CaptionLabel.Figure, "My diagram");

        var caption = view.Document.Blocks.OfType<Paragraph>()
            .SingleOrDefault(p => Captions.IsCaptionParagraph(p));
        caption.Should().NotBeNull();
        caption!.PlainText.Should().Be("Figure 1: My diagram");

        view.Undo();
        view.Document.Blocks.OfType<Paragraph>().Any(Captions.IsCaptionParagraph)
            .Should().BeFalse("undo removes the caption");
    }

    [Fact]
    public void InsertCaption_increments_number_per_label()
    {
        var view = ViewWith();

        view.InsertCaption(CaptionLabel.Figure, "A");
        view.InsertCaption(CaptionLabel.Figure, "B");
        view.InsertCaption(CaptionLabel.Table, "T");
        view.InsertCaption(CaptionLabel.Equation, "E");

        var texts = view.Document.Blocks.OfType<Paragraph>()
            .Where(Captions.IsCaptionParagraph).Select(p => p.PlainText).ToList();
        texts.Should().Contain(new[] { "Figure 1: A", "Figure 2: B", "Table 1: T", "Equation 1: E" });
    }

    [Fact]
    public void InsertCaption_supports_custom_label_text()
    {
        var view = ViewWith();

        view.InsertCaption("Scheme", "Flow");
        view.InsertCaption("Scheme", "State");

        view.Document.Blocks.OfType<Paragraph>()
            .Where(Captions.IsCaptionParagraph)
            .Select(p => p.PlainText)
            .Should().Contain(new[] { "Scheme 1: Flow", "Scheme 2: State" });
    }

    // ── Cross-reference ─────────────────────────────────────────────────────────────

    [Fact]
    public void InsertCrossReference_inserts_field_run_and_anchors_target()
    {
        var view = ViewWith(Heading("Intro", 1), new Paragraph("caret here"));
        // Caret is at the second (body) paragraph by default (first editable block); move it explicitly
        // by inserting at the body paragraph: use the first heading as the target.
        var targets = CrossReferences.Targets(view.Document, CrossRefType.Heading);
        targets.Should().NotBeEmpty();

        view.InsertCrossReference(CrossRefType.Heading, targets[0], CrossRefInsertAs.Text, hyperlink: true);

        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Any(r => r.CrossReference is not null)
            .Should().BeTrue("a cross-reference field run must be inserted");

        // The heading target paragraph must have gained an auto-bookmark so the REF field can resolve.
        ((Paragraph)view.Document.Blocks[0]).BookmarkName.Should().NotBeNullOrEmpty(
            "the target heading is auto-anchored with a _Ref bookmark");

        view.Undo();
        view.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .Any(r => r.CrossReference is not null)
            .Should().BeFalse("undo removes the cross-reference (and its anchor)");
    }

    [Fact]
    public void InsertCrossReference_uses_undoable_anchor_command_and_caches_field_text()
    {
        var target = new Paragraph("Intro");
        target.BookmarkNames.Add("chapter");
        target.BookmarkNames.Add("_Ref2");
        var existing = new Paragraph("Existing") { BookmarkName = "_Ref1" };
        var view = ViewWith(target, existing, new Paragraph("See "));

        view.InsertCrossReference(
            CrossRefType.Heading,
            new CrossRefTarget("Intro", Anchor: null, BlockIndex: 0),
            CrossRefInsertAs.Text,
            hyperlink: true);

        var field = view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.CrossReference is not null);
        field.Text.Should().Be("Intro");
        field.CrossReference!.Target.Should().Be("_Ref3");
        target.BookmarkNames.Should().Equal("chapter", "_Ref2", "_Ref3");

        view.Undo();

        target.BookmarkNames.Should().Equal("chapter", "_Ref2");
        view.Document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs)
            .Any(run => run.CrossReference is not null)
            .Should().BeFalse("one undo reverts both the anchor command and field insertion");
    }

    [Fact]
    public void InsertCrossReference_footnote_anchors_only_the_physical_marker()
    {
        var marker = new Paragraph();
        marker.Runs.Add(new Run("Body"));
        marker.Runs.Add(Run.FootnoteReference(1));
        var view = ViewWith(marker, new Paragraph("See "));
        view.Document.Footnotes[1] = new Footnote(1, "note");

        var target = CrossReferences.Targets(view.Document, CrossRefType.Footnote).Single();
        view.InsertCrossReference(CrossRefType.Footnote, target, CrossRefInsertAs.Text, hyperlink: true);

        marker.BookmarkNames.Should().Contain("_Ref1");
        marker.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.Start, 1, "_Ref1"));
        marker.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.End, 2));
        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.CrossReference is not null)
            .CrossReference!.Target.Should().Be("_Ref1");
    }

    [Fact]
    public void InsertCrossReference_caption_text_anchors_only_descriptive_text()
    {
        var caption = Captions.BuildCaption(CaptionLabel.Figure, 1, "Sample caption text");
        var view = ViewWith(caption, new Paragraph("See "));
        var target = CrossReferences.Targets(view.Document, CrossRefType.Figure).Single();

        view.InsertCrossReference(
            CrossRefType.Figure, target, CrossRefInsertAs.CaptionText, hyperlink: true);

        caption.Runs.Select(run => run.Text).Should().Equal("Figure ", "1", ": ", "Sample caption text");
        caption.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.Start, 3, "_Ref1"));
        caption.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.End, 4));
        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.CrossReference is not null)
            .Text.Should().Be("Sample caption text");
    }

    // ── Citation / Bibliography ─────────────────────────────────────────────────────

    [Fact]
    public void UpdateFields_refreshes_cross_reference_cached_text()
    {
        var view = ViewWith(
            new Paragraph("Chapter Two") { StyleId = "Heading1", BookmarkName = "_Ref1" },
            new Paragraph
            {
                Runs =
                {
                    new Run("See "),
                    Run.CrossReferenceFieldRun(
                        new CrossReferenceField(CrossRefFieldKind.Ref, "_Ref1", CrossRefInsertAs.Text, Hyperlink: true),
                        "Chapter One")
                }
            });

        view.UpdateFields();

        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Single(r => r.CrossReference is not null)
            .Text.Should().Be("Chapter Two");
    }

    [Fact]
    public void UpdateFields_page_reference_uses_target_physical_page()
    {
        var view = ViewWith(
            new Paragraph
            {
                Runs =
                {
                    new Run("See page "),
                    Run.CrossReferenceFieldRun(
                        new CrossReferenceField(CrossRefFieldKind.PageRef, "_Ref2", CrossRefInsertAs.PageNumber, Hyperlink: false),
                        "9"),
                    new Run(" and imported "),
                    Run.ComplexFieldRun(" PAGEREF _Ref2 ", "9")
                }
            },
            DocumentOps.CreatePageBreak(),
            new Paragraph("Target")
            {
                BookmarkName = "_Ref2",
            });
        view.Document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        view.Document.Page.PageNumberStartAt = 4;

        view.UpdateFields();

        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.CrossReference is not null)
            .Text.Should().Be("V");
        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "PAGEREF")
            .Text.Should().Be("V");
    }

    [Fact]
    public void UpdateFields_refreshes_stale_styleref_cached_text()
    {
        var view = ViewWith(
            Heading("Chapter Two", 1),
            new Paragraph
            {
                Runs =
                {
                    new Run("See "),
                    Run.ComplexFieldRun(" STYLEREF 1 ", "Chapter One")
                }
            });

        view.UpdateFields();

        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Single(r => r.ComplexField?.Keyword == "STYLEREF")
            .Text.Should().Be("Chapter Two");
    }

    [Fact]
    public void UpdateFields_styleref_uses_following_heading_when_none_precedes_field()
    {
        var view = ViewWith(
            new Paragraph
            {
                Runs = { Run.ComplexFieldRun(" STYLEREF 1 ", "stale") }
            },
            Heading("Following chapter", 1));

        view.UpdateFields();

        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Single(r => r.ComplexField?.Keyword == "STYLEREF")
            .Text.Should().Be("Following chapter");
    }

    [Fact]
    public void InsertCitation_inserts_intext_citation_at_caret()
    {
        var view = ViewWith(new Paragraph("See here "));
        var source = new Source { Tag = "Do24", Author = "Jane Q. Doe", Title = "A Work", Year = "2024" };

        view.InsertCitation(source);

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Contain("(Doe, 2024)",
            "the Avalonia editor should inherit shared in-text citation formatting");
        ((Paragraph)view.Document.Blocks[0]).Runs
            .Select(run => run.ComplexField?.Instruction)
            .Should().Contain(" CITATION Do24 ");
    }

    [Fact]
    public void InsertCitation_tagged_source_with_quoted_field_argument_renumbers_on_update_fields()
    {
        var view = ViewWith(new Paragraph("See here "));
        var source = new Source
        {
            Tag = "Do \"AI\" 24",
            Author = "Jane Q. Doe",
            Title = "A Work",
            Year = "2024"
        };
        view.Document.BibliographyStyle = CitationStyle.Vancouver;
        view.Document.Sources.Add(new Source { Tag = "Other24", Author = "Other Author", Year = "2024" });
        view.Document.Sources.Add(source);

        view.InsertCitation(source);

        var run = ((Paragraph)view.Document.Blocks[0]).Runs.Single(r => r.ComplexField is not null);
        run.Text.Should().Be("[2]");
        run.ComplexField!.Instruction.Should().Be(" CITATION \"Do \\\"AI\\\" 24\" ");

        view.Document.Sources.Clear();
        view.Document.Sources.Add(source);
        view.UpdateFields();

        ((Paragraph)view.Document.Blocks[0]).Runs.Single(r => r.ComplexField is not null)
            .Text.Should().Be("[1]");
    }

    [Fact]
    public void InsertBibliography_builds_block_from_sources_and_undo_reverts()
    {
        var view = ViewWith();
        view.Document.Sources.Add(new Source { Tag = "Sm24", Author = "Smith", Title = "A Work", Year = "2024" });
        var before = view.Document.Blocks.Count;

        view.InsertBibliography();

        view.Document.Blocks.Count.Should().BeGreaterThan(before, "bibliography paragraphs are inserted");
        var bibliography = view.Document.Blocks.OfType<Paragraph>()
            .Where(Citations.IsBibliographyParagraph)
            .ToArray();
        bibliography.Select(paragraph => paragraph.PlainText).Should().Equal(
            "References",
            "Smith. (2024). A Work.");
        bibliography[0].SpanningFieldOwner.Should().BeNull();
        bibliography[1].SpanningFieldStart!.Instruction.Should().Be(Citations.NativeFieldInstruction);
        bibliography[1].EndsSpanningField.Should().BeTrue();

        view.Undo();
        view.Document.Blocks.Count.Should().Be(before, "undo removes the whole bibliography block");
    }

    [Fact]
    public void ReplaceSources_replaces_source_list_and_undo_reverts()
    {
        var view = ViewWith();
        view.Document.Sources.Add(new Source { Tag = "Old", Author = "Old Author", Title = "Old Title", Year = "1999" });

        view.ReplaceSources(new[]
        {
            new Source { Tag = "New", Author = "New Author", Title = "New Title", Year = "2026" }
        });

        view.Document.Sources.Should().ContainSingle().Which.Tag.Should().Be("New");
        view.Undo();
        view.Document.Sources.Should().ContainSingle().Which.Tag.Should().Be("Old");
    }

    [Fact]
    public void MarkCitation_drops_hidden_citation_mark_into_body_and_undo_reverts()
    {
        var view = ViewWith(new Paragraph("Brown v. Board"));

        view.MarkCitation("Brown v. Board, 347 U.S. 483 (1954)");

        var mark = view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.Citation is not null);
        mark.Text.Should().BeEmpty("a Word TA mark is a hidden, textless field run");
        mark.Citation!.LongCitation.Should().Be("Brown v. Board, 347 U.S. 483 (1954)");
        mark.Citation.Category.Should().Be(CitationCategory.Cases);
        view.Document.Citations.Should().BeEmpty("Avalonia now persists the durable body mark instead of only the transient side-store");

        view.Undo();

        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Should().NotContain(run => run.Citation != null);
    }

    [Fact]
    public void MarkCitation_accepts_full_citation_dialog_result()
    {
        var view = ViewWith(new Paragraph("17 U.S.C. 107"));

        view.MarkCitation(new Citation("17 U.S.C. 107", CitationCategory.Statutes, "fair use"));

        var mark = view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.Citation is not null);
        mark.Citation!.Category.Should().Be(CitationCategory.Statutes);
        mark.Citation.LongCitation.Should().Be("17 U.S.C. 107");
        mark.Citation.ShortCitation.Should().Be("fair use");
    }

    [Fact]
    public Task MarkCitation_dialog_builds_full_citation_from_category_long_and_short_fields() => RunOnUiThread(() =>
    {
        var dialog = new MarkCitationDialog("Brown v. Board");

        dialog.Width.Should().Be(MarkCitationDialogPlanner.DialogWidth);
        dialog.GetLogicalDescendants().OfType<TextBlock>()
            .Select(text => text.Text)
            .Should().ContainInOrder(
                MarkCitationDialogPlanner.CategoryLabel,
                MarkCitationDialogPlanner.LongCitationLabel,
                MarkCitationDialogPlanner.ShortCitationLabel);

        dialog.SetForTests(CitationCategory.Statutes, "  17 U.S.C. 107  ", "  fair use  ");
        dialog.AcceptForTests().Should().BeTrue();

        dialog.Citation.Should().NotBeNull();
        dialog.Citation!.Category.Should().Be(CitationCategory.Statutes);
        dialog.Citation.LongCitation.Should().Be("17 U.S.C. 107");
        dialog.Citation.ShortCitation.Should().Be("fair use");
    });

    [Fact]
    public void MarkCitation_survives_plain_text_edit_rebuild()
    {
        var view = ViewWith(new Paragraph("Brown v. Board"));

        view.MarkCitation("Brown v. Board, 347 U.S. 483 (1954)");
        view.InsertText("See ");

        var paragraph = view.Document.Blocks.OfType<Paragraph>().Single();
        paragraph.PlainText.Should().Be("See Brown v. Board");
        paragraph.Runs.Should().ContainSingle(run => run.Citation != null)
            .Which.Citation!.LongCitation.Should().Be("Brown v. Board, 347 U.S. 483 (1954)");
    }

    [Fact]
    public void MarkCitation_body_mark_builds_table_and_survives_docx_roundtrip()
    {
        var view = ViewWith(new Paragraph("Brown v. Board"));

        view.MarkCitation("Brown v. Board, 347 U.S. 483 (1954)");
        view.InsertTableOfAuthorities();

        view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should()
            .ContainInOrder("Table of Authorities", "Cases", "Brown v. Board, 347 U.S. 483 (1954)");

        using var stream = new MemoryStream();
        DocxWriter.Write(view.Document, stream);
        stream.Position = 0;
        var reopened = DocxReader.Read(stream);

        reopened.Citations.Should().BeEmpty("the durable Word-facing TA field lives in the body");
        reopened.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Should().ContainSingle(run => run.Citation != null)
            .Which.Citation!.LongCitation.Should().Be("Brown v. Board, 347 U.S. 483 (1954)");

        reopened.Blocks.RemoveAll(TableOfAuthorities.IsTableOfAuthoritiesParagraph);
        reopened.Blocks.AddRange(TableOfAuthorities.Build(reopened));
        reopened.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should()
            .ContainInOrder("Table of Authorities", "Cases", "Brown v. Board, 347 U.S. 483 (1954)");
    }

    [Fact]
    public void Index_commands_mark_insert_and_refresh_generated_index()
    {
        var view = ViewWith(new Paragraph("Beta"));

        view.MarkIndexEntry("Beta");
        view.InsertIndex();
        view.MarkIndexEntry("Alpha");
        view.RefreshIndex();

        view.Document.Blocks.OfType<Paragraph>()
            .Where(DocumentIndex.IsIndexParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should()
            .Equal("A", "Alpha, 1", "B", "Beta, 1");
        view.Document.Blocks.OfType<Paragraph>()
            .Count(paragraph => paragraph.StyleId == DocumentIndex.HeadingStyleId)
            .Should()
            .Be(2);
    }

    [Fact]
    public void Index_commands_insert_default_and_selective_regions_with_matching_entries_only()
    {
        var view = ViewWith(new Paragraph
        {
            Runs =
            {
                new Run("Entries"),
                DocumentIndex.MarkRun(new IndexMark("Alpha")),
                DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")),
                DocumentIndex.MarkRun(new IndexMark("Ignored", Identifier: "Places"))
            }
        });

        view.InsertIndex();
        view.InsertIndex("People");

        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, identifier: null))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Alpha, 1");
        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Ada, 1");
        view.Document.Blocks
            .Should().NotContain(block => DocumentIndex.IsIndexParagraph(block, "Places"));
    }

    [Fact]
    public Task InsertIndex_dialog_returns_default_or_trimmed_identifier() => RunOnUiThread(() =>
    {
        var dialog = new InsertIndexDialog();

        dialog.Width.Should().Be(InsertIndexDialogPlanner.DialogWidth);
        dialog.Title.Should().Be(InsertIndexDialogPlanner.Title);
        dialog.ActionLabelForTests.Should().Be(InsertIndexDialogPlanner.InsertButtonLabel);
        dialog.BuildResultForTests("   ").Identifier.Should().BeNull();
        dialog.BuildResultForTests(" People ").Identifier.Should().Be("People");

        var updateDialog = InsertIndexDialog.CreateUpdateForTests("People");
        updateDialog.Title.Should().Be(InsertIndexDialogPlanner.UpdateTitle);
        updateDialog.ActionLabelForTests.Should().Be(InsertIndexDialogPlanner.UpdateButtonLabel);
        updateDialog.BuildResultForTests(" People ").Identifier.Should().Be("People");
    });

    [Fact]
    public void Index_insert_ribbon_command_uses_owner_dialog_callback_for_selective_index()
    {
        var view = ViewWith(new Paragraph
        {
            Runs =
            {
                new Run("Entries"),
                DocumentIndex.MarkRun(new IndexMark("Alpha")),
                DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")),
                DocumentIndex.MarkRun(new IndexMark("Ignored", Identifier: "Places"))
            }
        });
        var calls = 0;
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks() with
        {
            OpenInsertIndexDialog = () =>
            {
                calls++;
                view.InsertIndex("People");
            }
        });

        Execute(registry, "freew.index-insert");

        calls.Should().Be(1);
        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Ada, 1");
        view.Document.Blocks.Should().NotContain(block =>
            DocumentIndex.IsIndexParagraph(block, identifier: null)
            || DocumentIndex.IsIndexParagraph(block, "Places"));
    }

    [Fact]
    public void Index_insert_ribbon_command_without_callback_inserts_default_index()
    {
        var view = ViewWith(new Paragraph
        {
            Runs =
            {
                new Run("Entries"),
                DocumentIndex.MarkRun(new IndexMark("Alpha")),
                DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People"))
            }
        });
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.index-insert");

        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, identifier: null))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Alpha, 1");
        view.Document.Blocks.Should().NotContain(block =>
            DocumentIndex.IsIndexParagraph(block, "People"));
    }

    [Fact]
    public void Index_update_ribbon_command_uses_owner_dialog_callback_for_selective_index()
    {
        var view = ViewWith(new Paragraph
        {
            Runs =
            {
                new Run("Entries"),
                DocumentIndex.MarkRun(new IndexMark("Alpha")),
                DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People"))
            }
        });
        view.InsertIndex();
        view.InsertIndex("People");
        var defaultRegion = view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, identifier: null))
            .ToArray();
        view.Document.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Grace", Identifier: "People")) }
        });
        var calls = 0;
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks() with
        {
            OpenUpdateIndexDialog = () =>
            {
                calls++;
                view.RefreshIndex("People");
            }
        });

        Execute(registry, "freew.index-refresh");

        calls.Should().Be(1);
        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, identifier: null))
            .Should().Equal(defaultRegion);
        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Ada, 1", "G", "Grace, 1");
    }

    [Fact]
    public void Index_update_ribbon_command_without_callback_refreshes_default_index_only()
    {
        var view = ViewWith(new Paragraph
        {
            Runs =
            {
                new Run("Entries"),
                DocumentIndex.MarkRun(new IndexMark("Alpha")),
                DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People"))
            }
        });
        view.InsertIndex();
        view.InsertIndex("People");
        var peopleRegion = view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .ToArray();
        view.Document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                DocumentIndex.MarkRun(new IndexMark("Beta")),
                DocumentIndex.MarkRun(new IndexMark("Grace", Identifier: "People"))
            }
        });
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.index-refresh");

        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, identifier: null))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Alpha, 1", "B", "Beta, 1");
        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Should().Equal(peopleRegion);
    }

    [Fact]
    public void RefreshIndex_replaces_Word_updated_native_region_by_field_ownership()
    {
        var field = new ComplexField(" INDEX \\h \"A\" \\z \"1033\" ");
        var heading = new Paragraph("A")
        {
            StyleId = "IndexHeading",
            SpanningFieldStart = field,
            SpanningFieldOwner = field
        };
        var entry = new Paragraph("Alpha, 1")
        {
            StyleId = "Index1",
            SpanningFieldOwner = field
        };
        var trailing = new Paragraph
        {
            StyleId = "IndexEntry",
            SpanningFieldOwner = field,
            EndsSpanningField = true
        };
        var view = ViewWith(
            new Paragraph { Runs = { DocumentIndex.MarkRun("Beta") } },
            heading,
            entry,
            trailing);

        view.RefreshIndex();

        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, identifier: null))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("B", "Beta, 1");
        view.Document.Blocks.Should().NotContain(heading).And.NotContain(entry).And.NotContain(trailing);
    }

    [Fact]
    public void RefreshIndex_selective_region_updates_people_and_leaves_default_region_untouched()
    {
        var defaultHeading = new Paragraph(DocumentIndex.HeadingText)
        {
            StyleId = DocumentIndex.HeadingStyleIdFor(identifier: null)
        };
        var defaultEntry = new Paragraph("Alpha, 7")
        {
            StyleId = DocumentIndex.EntryStyleIdFor(identifier: null)
        };
        var peopleHeading = new Paragraph(DocumentIndex.HeadingText)
        {
            StyleId = DocumentIndex.HeadingStyleIdFor("People")
        };
        var peopleEntry = new Paragraph("Old Person, 9")
        {
            StyleId = DocumentIndex.EntryStyleIdFor("People")
        };
        var view = ViewWith(
            defaultHeading,
            defaultEntry,
            peopleHeading,
            peopleEntry,
            new Paragraph
            {
                Runs =
                {
                    new Run("Entries"),
                    DocumentIndex.MarkRun(new IndexMark("Beta")),
                    DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")),
                    DocumentIndex.MarkRun(new IndexMark("Grace", Identifier: "People"))
                }
            });

        view.RefreshIndex("People");

        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, identifier: null))
            .Should().Equal(defaultHeading, defaultEntry);
        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Ada, 1", "G", "Grace, 1");
        view.Document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Should().NotContain(peopleHeading)
            .And.NotContain(peopleEntry);
    }

    [Fact]
    public void Table_of_figures_commands_insert_and_refresh_caption_table()
    {
        var view = ViewWith();

        view.InsertCaption(CaptionLabel.Figure, "First");
        view.InsertTableOfFigures();
        view.Document.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 2, "Second"));
        view.RefreshTableOfFigures();

        view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should()
            .Equal("Table of Figures", "Figure 1: First\t1", "Figure 2: Second\t1");
        view.Document.Blocks.OfType<Paragraph>()
            .Count(paragraph => paragraph.StyleId == TableOfFigures.HeadingStyleId)
            .Should()
            .Be(1);
        var nativeEntries = view.Document.Blocks.OfType<Paragraph>()
            .Where(paragraph => TableOfFigures.TryGetNativeLabel(paragraph.SpanningFieldOwner, out var label)
                && label == Captions.FigureLabelText)
            .ToArray();
        nativeEntries.Should().HaveCount(2);
        nativeEntries[0].SpanningFieldStart!.Instruction.Should().Be(" TOC \\c \"Figure\" ");
        nativeEntries[1].EndsSpanningField.Should().BeTrue();
    }

    [Fact]
    public void Generated_reference_refresh_without_existing_region_uses_WPF_back_matter_placement()
    {
        var indexView = ViewWith(new Paragraph("Index body"));
        indexView.Document.IndexEntries.Add(new IndexEntry("Alpha"));

        indexView.RefreshIndex();

        indexView.Document.Blocks[0].Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Be("Index body");
        DocumentIndex.IsIndexParagraph(indexView.Document.Blocks[^1]).Should().BeTrue();

        var figuresView = ViewWith(Captions.BuildCaption(CaptionLabel.Figure, 1, "Architecture"));

        figuresView.RefreshTableOfFigures();

        figuresView.Document.Blocks[0].Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Contain("Architecture");
        TableOfFigures.IsTableOfFiguresParagraph(figuresView.Document.Blocks[^1]).Should().BeTrue();
    }

    [Fact]
    public void Index_commands_preserve_subentry_and_cross_reference_semantics()
    {
        var view = ViewWith(new Paragraph("Transport"));

        view.MarkIndexEntry(new IndexMark("Transportation", "Rail", "See Trains"));
        view.InsertIndex();

        view.Document.Blocks.OfType<Paragraph>()
            .Where(DocumentIndex.IsIndexParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("T", "Transportation", "Rail. See Trains");
        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(DocumentIndex.MarkedEntry)
            .Should().Contain(new IndexMark("Transportation", "Rail", "See Trains"));
    }

    [Fact]
    public Task MarkIndexEntry_dialog_builds_hierarchy_and_cross_reference() => RunOnUiThread(() =>
    {
        var dialog = new MarkIndexEntryDialog("Animals");

        dialog.Width.Should().Be(MarkIndexEntryDialogPlanner.DialogWidth);
        dialog.CrossReferenceEnabledForTests.Should().BeFalse();
        dialog.PageNumberFormattingEnabledForTests.Should().BeTrue();
        dialog.SetForTests(" Animals ", " Cats ", true, " See Pet care ");
        dialog.CrossReferenceEnabledForTests.Should().BeTrue();
        dialog.PageNumberFormattingEnabledForTests.Should().BeFalse();
        dialog.AcceptForTests().Should().BeTrue();
        dialog.Mark.Should().Be(new IndexMark("Animals", "Cats", "See Pet care"));
    });

    [Fact]
    public Task MarkIndexEntry_dialog_carries_bold_and_italic_page_number_format() => RunOnUiThread(() =>
    {
        var dialog = new MarkIndexEntryDialog("Alpha");
        dialog.SetForTests("Alpha", null, false, null, boldPageNumber: true, italicPageNumber: true);

        dialog.AcceptForTests().Should().BeTrue();
        dialog.Mark.Should().Be(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            ItalicPageNumber: true));
    });

    [Fact]
    public Task MarkIndexEntry_dialog_carries_trimmed_optional_identifier() => RunOnUiThread(() =>
    {
        var dialog = new MarkIndexEntryDialog("Alpha");
        dialog.SetForTests("Alpha", null, false, null, identifier: " People ");

        dialog.AcceptForTests().Should().BeTrue();
        dialog.Mark.Should().Be(new IndexMark("Alpha", Identifier: "People"));
    });

    [Fact]
    public Task MarkIndexEntry_dialog_returns_mark_all_action_for_selected_text() => RunOnUiThread(() =>
    {
        var dialog = new MarkIndexEntryDialog("Alpha");

        dialog.MarkAllEnabledForTests.Should().BeTrue();
        dialog.AcceptAllForTests().Should().BeTrue();
        dialog.MarkAll.Should().BeTrue();
        dialog.Mark.Should().Be(new IndexMark("Alpha"));
        dialog.Mark!.Identifier.Should().BeEmpty();
    });

    [Fact]
    public Task MarkIndexEntry_dialog_returns_bookmark_page_range_with_page_formatting() => RunOnUiThread(() =>
    {
        var dialog = new MarkIndexEntryDialog("Animals", ["chapter", "appendix"]);
        dialog.SetForTests(
            " Animals ",
            " Cats ",
            IndexEntryReferenceKind.PageRange,
            "appendix",
            null,
            boldPageNumber: true,
            italicPageNumber: true);

        dialog.BookmarkSelectorEnabledForTests.Should().BeTrue();
        dialog.CrossReferenceEnabledForTests.Should().BeFalse();
        dialog.PageNumberFormattingEnabledForTests.Should().BeTrue();
        dialog.MarkAllEnabledForTests.Should().BeFalse();
        dialog.AcceptForTests().Should().BeTrue();
        dialog.MarkAll.Should().BeFalse();
        dialog.Mark.Should().Be(new IndexMark(
            "Animals",
            "Cats",
            BoldPageNumber: true,
            ItalicPageNumber: true,
            BookmarkName: "appendix"));
    });

    [Fact]
    public Task MarkIndexEntry_dialog_updates_selector_and_mark_all_for_reference_kind() => RunOnUiThread(() =>
    {
        var dialog = new MarkIndexEntryDialog("Alpha", ["chapter"]);

        dialog.BookmarkSelectorEnabledForTests.Should().BeFalse();
        dialog.MarkAllEnabledForTests.Should().BeTrue();

        dialog.SetForTests("Alpha", null, IndexEntryReferenceKind.PageRange, "chapter", null);
        dialog.BookmarkSelectorEnabledForTests.Should().BeTrue();
        dialog.PageNumberFormattingEnabledForTests.Should().BeTrue();
        dialog.MarkAllEnabledForTests.Should().BeFalse();
        dialog.AcceptAllForTests().Should().BeFalse();

        dialog.SetForTests("Alpha", null, IndexEntryReferenceKind.CrossReference, null, "See Beta");
        dialog.BookmarkSelectorEnabledForTests.Should().BeFalse();
        dialog.CrossReferenceEnabledForTests.Should().BeTrue();
        dialog.PageNumberFormattingEnabledForTests.Should().BeFalse();
        dialog.MarkAllEnabledForTests.Should().BeTrue();
    });

    [Fact]
    public void MarkAllIndexEntries_marks_matching_paragraphs_as_one_undoable_operation()
    {
        var view = ViewWith(
            new Paragraph("Alpha first Alpha"),
            new Paragraph("alphabet control"),
            new Paragraph("Second ALPHA"));
        var mark = new IndexMark("Alpha", "Topic", ItalicPageNumber: true);

        view.MarkAllIndexEntries("Alpha", mark).Should().Be(3);
        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(DocumentIndex.MarkedEntry)
            .OfType<IndexMark>()
            .Should().Equal(mark, mark, mark);

        view.Undo();
        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(DocumentIndex.MarkedEntry)
            .Should().AllSatisfy(entry => entry.Should().BeNull());
        view.Redo();
        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(DocumentIndex.MarkedEntry)
            .OfType<IndexMark>()
            .Should().Equal(mark, mark, mark);
    }

    [Fact]
    public void MarkAllIndexEntries_includes_table_cells_in_the_undo_group()
    {
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph("Alpha in a nested cell"));
        var row = new TableRow();
        row.Cells.Add(cell);
        var nestedTable = new Table();
        nestedTable.Rows.Add(row);
        var outerCell = new TableCell("outer control");
        outerCell.NestedTables.Add(nestedTable);
        var outerRow = new TableRow();
        outerRow.Cells.Add(outerCell);
        var table = new Table();
        table.Rows.Add(outerRow);
        var body = new Paragraph("Alpha in the body");
        var view = ViewWith(table, body);
        var mark = new IndexMark("Alpha", "Topic", BoldPageNumber: true);

        view.MarkAllIndexEntries("Alpha", mark).Should().Be(2);
        cell.Paragraphs[0].Runs.Select(DocumentIndex.MarkedEntry).OfType<IndexMark>()
            .Should().Equal(mark);
        body.Runs.Select(DocumentIndex.MarkedEntry).OfType<IndexMark>()
            .Should().Equal(mark);

        view.Undo();
        cell.Paragraphs[0].Runs.Select(DocumentIndex.MarkedEntry).Should().AllSatisfy(entry => entry.Should().BeNull());
        body.Runs.Select(DocumentIndex.MarkedEntry).Should().AllSatisfy(entry => entry.Should().BeNull());
        view.Redo();
        cell.Paragraphs[0].Runs.Select(DocumentIndex.MarkedEntry).OfType<IndexMark>()
            .Should().Equal(mark);
        body.Runs.Select(DocumentIndex.MarkedEntry).OfType<IndexMark>()
            .Should().Equal(mark);
    }

    [Fact]
    public void Index_mark_ribbon_command_uses_owner_dialog_callback_when_available()
    {
        var view = ViewWith(new Paragraph("Transport"));
        var calls = 0;
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks() with
        {
            OpenMarkIndexEntryDialog = () =>
            {
                calls++;
                view.MarkIndexEntry(new IndexMark("Transportation", "Rail", "See Trains"));
            }
        });

        Execute(registry, "freew.index-mark");

        calls.Should().Be(1);
        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(DocumentIndex.MarkedEntry)
            .Should().Contain(new IndexMark("Transportation", "Rail", "See Trains"));
    }

    [Fact]
    public void Index_refresh_preserves_repeated_labels_from_distinct_physical_pages()
    {
        var firstSectionPage = new PageSettings
        {
            PageNumberFormat = PageNumberFormat.Decimal,
            PageNumberStartAt = 1
        };
        var view = ViewWith(
            new Paragraph(DocumentIndex.HeadingText) { StyleId = DocumentIndex.HeadingStyleId },
            new Paragraph("Old, 9") { StyleId = DocumentIndex.EntryStyleId },
            new Paragraph
            {
                Runs = { new Run("First"), DocumentIndex.MarkRun("Alpha") },
                SectionBreak = new Section(firstSectionPage, SectionBreakKind.NextPage)
            },
            new Paragraph
            {
                Runs = { new Run("Second"), DocumentIndex.MarkRun("Alpha"), DocumentIndex.MarkRun("Beta") }
            });
        view.Document.Page.PageNumberFormat = PageNumberFormat.Decimal;
        view.Document.Page.PageNumberStartAt = 1;

        view.RefreshIndex();

        view.Document.Blocks.OfType<Paragraph>()
            .Where(DocumentIndex.IsIndexParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Alpha, 1, 1", "B", "Beta, 1");
    }

    [Fact]
    public void Index_refresh_reports_broken_xe_range_bookmark()
    {
        var view = ViewWith(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "MissingRange")) }
        });

        view.RefreshIndex();

        view.Document.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId)
            .PlainText.Should().Be("Alpha, " + DocumentIndex.BrokenBookmarkText);
    }

    [Fact]
    public void Table_of_figures_refresh_uses_caption_logical_page_label()
    {
        var nativeField = new ComplexField(" TOC \\c \"Figure\" ");
        var view = ViewWith(
            new Paragraph(TableOfFigures.HeadingText(CaptionLabel.Figure))
            {
                StyleId = TableOfFigures.HeadingStyleId
            },
            new Paragraph("Old Figure\t9")
            {
                StyleId = "Normal",
                SpanningFieldStart = nativeField,
                SpanningFieldOwner = nativeField,
                EndsSpanningField = true
            },
            DocumentOps.CreatePageBreak(),
            Captions.BuildCaption(CaptionLabel.Figure, 1, "Architecture"));
        view.Document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        view.Document.Page.PageNumberStartAt = 4;

        view.RefreshTableOfFigures();

        view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Figure 1: Architecture\tV").And.NotContain("Old Figure\t9");
    }

    [Fact]
    public void Table_of_figures_refresh_uses_each_caption_row_page_in_paginated_table()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var table = document.Blocks.OfType<Table>().Single();
        table.Rows[1].Cells[0].Paragraphs[0] = Captions.BuildCaption(CaptionLabel.Figure, 1, "Early row");
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0] = Captions.BuildCaption(CaptionLabel.Figure, 2, "Later row");
        table.Rows[5].Cells[0].NestedTables.Add(nested);
        var oldRegion = TableOfFigures.Build(document, CaptionLabel.Figure, _ => "9");
        for (var index = oldRegion.Count - 1; index >= 0; index--)
            document.Blocks.Insert(0, oldRegion[index]);
        var view = new DocumentView();
        view.LoadDocument(document);

        view.RefreshTableOfFigures();

        var pageLabels = document.Blocks.OfType<Paragraph>()
            .Where(paragraph => paragraph.StyleId == TableOfFigures.EntryStyleId)
            .Select(paragraph => paragraph.PlainText.Split('\t').Last())
            .ToArray();
        pageLabels.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        pageLabels.Should().NotContain("9");
    }

    [Fact]
    public void Table_of_figures_supports_equation_and_custom_caption_labels()
    {
        var view = ViewWith();

        view.InsertCaption(CaptionLabel.Equation, "Energy");
        view.InsertCaption("Scheme", "Flow");
        view.InsertTableOfFigures(CaptionLabel.Equation);

        view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should()
            .Equal("Table of Equations", "Equation 1: Energy\t1");

        view.RefreshTableOfFigures("Scheme");

        view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should()
            .Equal("Table of Schemes", "Scheme 1: Flow\t1");
    }

    [Fact]
    public void UpdateFields_refreshes_table_of_figures_region()
    {
        var view = ViewWith();

        view.InsertCaption(CaptionLabel.Equation, "First");
        view.InsertTableOfFigures(CaptionLabel.Equation);
        view.Document.Blocks.Add(Captions.BuildCaption(CaptionLabel.Equation, 2, "Second"));

        view.UpdateFields();

        view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should()
            .Equal("Table of Equations", "Equation 1: First\t1", "Equation 2: Second\t1");
    }

    [Fact]
    public Task Table_of_authorities_commands_mark_insert_and_refresh_generated_table() =>
        RunOnUiThread(() =>
    {
        var view = ViewWith(new Paragraph("Brown v. Board"));

        view.MarkCitation("Brown v. Board");
        view.InsertTableOfAuthorities();
        view.MarkCitation("Roe v. Wade");
        view.RefreshTableOfAuthorities();

        view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should()
            .ContainInOrder("Table of Authorities", "Cases", "Brown v. Board\t1", "Roe v. Wade\t1");
        view.Document.Blocks.OfType<Paragraph>()
            .Count(paragraph => paragraph.StyleId == TableOfAuthorities.HeadingStyleId)
            .Should()
            .Be(1);
        var toa = view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .ToList();
        toa[0].SpanningFieldOwner.Should().BeNull();
        toa.Skip(1).Should().OnlyContain(paragraph =>
            paragraph.SpanningFieldOwner != null
            && paragraph.SpanningFieldOwner.Instruction == " TOA \\h \\c \"1\" \\f ");
        toa[1].SpanningFieldStart.Should().NotBeNull();
        toa[^1].EndsSpanningField.Should().BeTrue();
    });

    [Fact]
    public Task Table_of_authorities_refresh_without_existing_region_appends_at_document_end() =>
        RunOnUiThread(() =>
    {
        var view = ViewWith(new Paragraph("Intro"), new Paragraph("Brown v. Board"));

        view.MarkCitation("Brown v. Board");
        view.RefreshTableOfAuthorities();

        view.Document.Blocks.OfType<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal(
                "Intro",
                "Brown v. Board",
                "Table of Authorities",
                "Cases",
                "Brown v. Board\t1");
    });

    [Fact]
    public Task Table_of_authorities_insert_accepts_shared_options_in_avalonia_host() =>
        RunOnUiThread(() =>
    {
        var mark = Run.CitationMark(new Citation("17 U.S.C. 107", CitationCategory.Statutes));
        mark.Formatting = new RunFormatting { Italic = true };
        var view = ViewWith(new Paragraph { Runs = { mark } });

        view.InsertTableOfAuthorities(new ToaOptions
        {
            CategoryFilter = CitationCategory.Statutes,
            KeepOriginalFormatting = true,
            TabLeader = ToaTabLeader.None
        });

        var toa = view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .ToList();
        toa.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Statutes", "17 U.S.C. 107\t1");

        var entry = toa.Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.Formatting.TabStops.Should().ContainSingle()
            .Which.Leader.Should().Be(TabLeader.None);
        entry.Runs.Select(run => run.Text).Should().Equal("17 U.S.C. 107", "\t", "1");
        entry.Runs[0].Formatting.Italic.Should().BeTrue();
    });

    [Fact]
    public void Table_of_authorities_insert_uses_shared_explicit_break_page_references()
    {
        var view = ViewWith(
            CitationMarkParagraph("Brown v. Board", formatted: false),
            DocumentOps.CreatePageBreak(),
            CitationMarkParagraph("Brown v. Board", formatted: false));

        view.InsertTableOfAuthorities();

        var entry = view.Document.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.PlainText.Should().Be("Brown v. Board\t1, 2");
        entry.Runs.Select(run => run.Text).Should().Equal("Brown v. Board", "\t", "1, 2");
    }

    [Fact]
    public Task Table_of_authorities_insert_stabilizes_page_references_after_region_reflow() =>
        RunOnUiThread(() =>
        {
            var view = ReflowingTableOfAuthoritiesView(includeExistingRegion: false);

            view.InsertTableOfAuthorities();
            var firstPass = TableOfAuthoritiesEntries(view.Document);

            view.RefreshTableOfAuthorities();
            var secondPass = TableOfAuthoritiesEntries(view.Document);

            firstPass.Should().Equal(secondPass);
            firstPass.Should().Equal(ExpectedReflowEntries());
        });

    [Fact]
    public Task Table_of_authorities_refresh_stabilizes_page_references_after_replacement_reflow() =>
        RunOnUiThread(() =>
        {
            var view = ReflowingTableOfAuthoritiesView(includeExistingRegion: true);

            view.RefreshTableOfAuthorities();
            var firstPass = TableOfAuthoritiesEntries(view.Document);

            view.RefreshTableOfAuthorities();
            var secondPass = TableOfAuthoritiesEntries(view.Document);

            firstPass.Should().Equal(secondPass);
            firstPass.Should().Equal(ExpectedReflowEntries());
        });

    [Fact]
    public Task Table_of_authorities_insert_resolves_mark_position_inside_long_paragraph_after_page_transition() =>
        RunOnUiThread(() =>
        {
            var filler = string.Join(
                " ",
                Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 80));
            var paragraph = new Paragraph
            {
                Runs =
                {
                    new Run(filler + " "),
                    Run.CitationMark(new Citation("Late Case", CitationCategory.Cases))
                }
            };
            var view = ViewWith(paragraph);
            view.Document.Page.WidthPt = 300;
            view.Document.Page.HeightPt = 220;
            view.Document.Page.MarginTopPt = 18;
            view.Document.Page.MarginBottomPt = 18;
            view.Document.Page.MarginLeftPt = 18;
            view.Document.Page.MarginRightPt = 18;
            view.Measure(new global::Avalonia.Size(800, 4000));

            view.InsertTableOfAuthorities();

            var entry = view.Document.Blocks.OfType<Paragraph>()
                .Single(block => block.StyleId == TableOfAuthorities.EntryStyleId);
            entry.PlainText.Should().MatchRegex(@"^Late Case\t[1-9][0-9]*$");
            entry.Runs.Select(run => run.Text).Should().HaveCount(3);
            entry.Runs[2].Text.Should().NotBe("1");
        });

    [Fact]
    public Task Table_of_authorities_refresh_consumes_shared_render_plan_metadata() =>
        RunOnUiThread(() =>
    {
        var oldRegion = TableOfAuthorities.Build(new[] { new Citation("Old Case", CitationCategory.Cases) });
        var view = ViewWith(
            new Paragraph("Before"),
            CitationMarkParagraph("Roe v. Wade", formatted: true),
            CitationMarkParagraph("Roe v. Wade", formatted: false),
            CitationMarkParagraph("Roe v. Wade", formatted: false),
            CitationMarkParagraph("Roe v. Wade", formatted: false),
            CitationMarkParagraph("Roe v. Wade", formatted: false),
            oldRegion[0],
            oldRegion[1],
            oldRegion[2],
            new Paragraph("After"));
        view.Document.Page.WidthPt = 700;
        view.Document.Page.MarginLeftPt = 80;
        view.Document.Page.MarginRightPt = 90;

        view.RefreshTableOfAuthorities(new ToaOptions
        {
            CategoryFilter = CitationCategory.Cases,
            KeepOriginalFormatting = true,
            UsePassim = true,
            TabLeader = ToaTabLeader.Dashes
        });

        view.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "Roe v. Wade\t1");
        view.Document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().NotContain("Old Case")
            .And.EndWith("After");

        var entry = view.Document.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.Formatting.TabStops.Should().Equal(
            new TabStop(530, TabStopAlignment.Right, TabLeader.Dashes));
        var entryFormatting = entry.Runs[0].Formatting;
        entryFormatting.Bold.Should().BeTrue();
        entryFormatting.Underline.Should().BeTrue();
        entryFormatting.ColorHex.Should().Be("#C00000");
    });

    // ── Registry wiring ─────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_resolves_all_references_tab_commands()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());

        var expected = new[]
        {
            "freew.footnote", "freew.endnote",
            "freew.insert-footnote", "freew.insert-endnote",
            "freew.toc", "freew.toc-refresh",
            "freew.insert-toc", "freew.update-toc",
            "freew.caption",
            "freew.insert-caption", "freew.insert-caption.figure", "freew.insert-caption.table", "freew.insert-caption.equation",
            "freew.cross-reference",
            "freew.citation",
            "freew.insert-citation", "freew.citation-style", "freew.bibliography",
            "freew.show-notes", "freew.footnote-endnote-options",
            "freew.manage-sources",
            "freew.tof", "freew.tof.figure", "freew.tof.table", "freew.tof.equation",
            "freew.tof-refresh", "freew.tof-refresh.figure", "freew.tof-refresh.table", "freew.tof-refresh.equation",
            "freew.index-mark", "freew.index-insert", "freew.index-refresh",
            "freew.mark-citation", "freew.table-of-authorities", "freew.table-of-authorities-refresh",
        };

        foreach (var id in expected)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"References-tab command '{id}' must be registered");
    }

    [Fact]
    public void Registry_preserves_legacy_references_aliases_for_canonical_commands()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
        var aliases = new[]
        {
            ("freew.footnote", "freew.insert-footnote"),
            ("freew.endnote", "freew.insert-endnote"),
            ("freew.toc", "freew.insert-toc"),
            ("freew.toc-refresh", "freew.update-toc"),
            ("freew.caption", "freew.insert-caption"),
            ("freew.citation", "freew.insert-citation"),
        };

        foreach (var (canonicalId, aliasId) in aliases)
        {
            registry.TryGet(new RibbonCommandId(canonicalId), out var canonical).Should().BeTrue();
            registry.TryGet(new RibbonCommandId(aliasId), out var alias).Should().BeTrue();
            alias.Should().BeSameAs(canonical, $"{aliasId} remains a compatibility alias for {canonicalId}");
        }
    }

    [Fact]
    public void ShowNotes_with_pane_callbacks_exposes_live_checked_state()
    {
        var paneVisible = false;
        var callbacks = NoopCallbacks() with
        {
            ToggleNotesPane = () => paneVisible = !paneVisible,
            IsNotesPaneVisible = () => paneVisible,
        };
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), callbacks);

        registry.TryGet(new RibbonCommandId("freew.show-notes"), out var command).Should().BeTrue();
        var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        stateful.GetState().IsChecked.Should().BeFalse();

        command!.Execute(RibbonCommandContext.Empty);
        paneVisible.Should().BeTrue();
        stateful.GetState().IsChecked.Should().BeTrue();

        command.Execute(RibbonCommandContext.Empty);
        paneVisible.Should().BeFalse();
        stateful.GetState().IsChecked.Should().BeFalse();
    }

    [Fact]
    public void References_tab_definition_exposes_groups()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var references = definition.FindTab("references");
        references.Should().NotBeNull();

        references!.Groups.Select(g => g.Header).Should()
            .Contain(new[]
            {
                "Table of Contents",
                "Footnotes",
                "Citations & Bibliography",
                "Captions",
                "Index",
                "Table of Authorities"
            });
    }

    [Fact]
    public void References_tab_definition_uses_canonical_shared_command_ids()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var references = definition.FindTab("references");
        references.Should().NotBeNull();
        var commandIds = references!.Groups
            .SelectMany(group => group.Controls)
            .SelectMany(CommandIds)
            .ToHashSet(StringComparer.Ordinal);

        commandIds.Should().Contain(new[]
        {
            "freew.toc",
            "freew.toc-refresh",
            "freew.footnote",
            "freew.endnote",
            "freew.citation",
            "freew.citation-style",
            "freew.bibliography",
            "freew.caption",
            "freew.insert-caption.figure",
            "freew.insert-caption.table",
            "freew.insert-caption.equation",
            "freew.cross-reference",
            "freew.show-notes",
            "freew.footnote-endnote-options",
            "freew.manage-sources",
            "freew.tof",
            "freew.tof.figure",
            "freew.tof.table",
            "freew.tof.equation",
            "freew.tof-refresh",
            "freew.index-mark",
            "freew.index-insert",
            "freew.index-refresh",
            "freew.mark-citation",
            "freew.table-of-authorities",
            "freew.table-of-authorities-refresh",
        });

        commandIds.Should().NotContain(new[]
        {
            "freew.insert-toc",
            "freew.update-toc",
            "freew.insert-footnote",
            "freew.insert-endnote",
            "freew.insert-citation",
            "freew.insert-caption",
        });
    }

    [Fact]
    public void Citation_style_combo_exposes_all_model_styles_in_wpf_and_avalonia_profiles()
    {
        var expected = Enum.GetValues<CitationStyle>().Select(Citations.StyleName).ToArray();
        expected.Should().Equal(FreeW.Ribbon.Definitions.FreeWRibbonDefinitionData.CitationStyleNames);

        var avalonia = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var wpf = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);

        CitationStyleItems(avalonia).Should().Equal(expected);
        CitationStyleItems(wpf).Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(CitationStyleLabels))]
    public void Citation_style_command_accepts_every_profile_value_and_reports_selected_state(string styleName)
    {
        var view = ViewWith(new Paragraph("Body"));
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.citation-style", RibbonCommandContext.ForSelectedValue(styleName));

        view.Document.BibliographyStyle.Should().Be(Citations.ParseStyle(styleName));
        registry.TryGet(new RibbonCommandId("freew.citation-style"), out var command).Should().BeTrue();
        command.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().Value.Should().Be(styleName);
    }

    [Fact]
    public Task Citation_style_combo_renders_current_state_and_applies_selected_value() => RunOnUiThread(() =>
    {
        var view = ViewWith(new Paragraph("Body"));
        view.Document.BibliographyStyle = CitationStyle.Harvard;
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
        var references = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia).FindTab("references");

        var content = AvaloniaRibbonRenderer.BuildTabContent(references!, registry);
        var combo = content.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(box => Equals(box.Tag, "freew.citation-style"));

        combo.SelectedItem.Should().Be("Harvard");

        combo.SelectedItem = "Vancouver";

        view.Document.BibliographyStyle.Should().Be(CitationStyle.Vancouver);
    });

    [Fact]
    public void Canonical_references_commands_execute_via_registry()
    {
        var view = ViewWith();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.footnote");
        view.Document.Footnotes.Should().ContainKey(1, "executing the canonical command inserts a footnote");

        Execute(registry, "freew.endnote");
        view.Document.Endnotes.Should().ContainKey(1, "executing the canonical command inserts an endnote");

        var tocView = ViewWith(Heading("First", 1), new Paragraph("body"));
        var tocRegistry = FreeWAvaloniaRibbonCommands.Build(tocView, NoopCallbacks());

        Execute(tocRegistry, "freew.toc");
        tocView.Document.Blocks.Count(TableOfContents.IsTocParagraph)
            .Should().Be(2, "executing the canonical command inserts a generated TOC");

        tocView.Document.Blocks.Add(Heading("Second", 1));
        Execute(tocRegistry, "freew.toc-refresh");
        tocView.Document.Blocks.Where(TableOfContents.IsTocParagraph)
            .Select(block => ((Paragraph)block).PlainText)
            .Should().Contain("Second\t1", "executing the canonical refresh updates the TOC in place");

        var indexView = ViewWith(new Paragraph("Alpha"));
        var indexRegistry = FreeWAvaloniaRibbonCommands.Build(indexView, NoopCallbacks());
        Execute(indexRegistry, "freew.index-mark");
        Execute(indexRegistry, "freew.index-insert");
        indexView.Document.Blocks.OfType<Paragraph>()
            .Where(DocumentIndex.IsIndexParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should()
            .Contain("Alpha, 1");

        var authoritiesView = ViewWith(new Paragraph("Brown v. Board"));
        var authoritiesRegistry = FreeWAvaloniaRibbonCommands.Build(authoritiesView, NoopCallbacks() with
        {
            OpenMarkCitationDialog = () => authoritiesView.MarkCitation(
                new Citation("Brown v. Board", CitationCategory.Cases, "Brown"))
        });
        Execute(authoritiesRegistry, "freew.mark-citation");
        Execute(authoritiesRegistry, "freew.table-of-authorities");
        authoritiesView.Document.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should()
            .Contain("Brown v. Board");
    }

    [Fact]
    public void Citation_style_command_changes_subsequent_insert_citation_output()
    {
        var view = ViewWith(new Paragraph("See "));
        var source = new Source { Tag = "Sm24", Author = "Smith", Title = "A Work", Year = "2024" };
        view.Document.Sources.Add(source);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks() with
        {
            OpenCitationDialog = () => view.InsertCitation(source),
        });

        Execute(registry, "freew.citation-style", RibbonCommandContext.ForSelectedValue("MLA"));
        Execute(registry, "freew.citation");

        view.Document.BibliographyStyle.Should().Be(CitationStyle.Mla);
        view.Document.Blocks.OfType<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().ContainSingle(text => text.Contains("(Smith)", StringComparison.Ordinal)
                && !text.Contains("2024", StringComparison.Ordinal));
    }

    [Fact]
    public void Citation_style_command_changes_subsequent_bibliography_output_and_state()
    {
        var view = ViewWith(new Paragraph("Body"));
        view.Document.Sources.Add(new Source
        {
            Tag = "Sm24",
            Author = "Smith",
            Title = "A Work",
            Year = "2024",
            Publisher = "Press"
        });
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.citation-style", RibbonCommandContext.ForSelectedValue("Chicago"));
        Execute(registry, "freew.bibliography");

        view.Document.BibliographyStyle.Should().Be(CitationStyle.Chicago);
        registry.TryGet(new RibbonCommandId("freew.citation-style"), out var command).Should().BeTrue();
        ((IRibbonStatefulCommand)command!).GetState().Value.Should().Be("Chicago");
        view.Document.Blocks.OfType<Paragraph>()
            .Where(Citations.IsBibliographyParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Bibliography", "Smith. A Work. Press, 2024.");
    }

    [Fact]
    public void Citation_style_command_refreshes_existing_citation_and_bibliography_undoably()
    {
        var source = new Source
        {
            Tag = "Sm24",
            Author = "Smith",
            Title = "A Work",
            Year = "2024",
            Publisher = "Press"
        };
        var view = ViewWith(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" CITATION Sm24 ", "(Smith, 2024)") }
        });
        view.Document.Sources.Add(source);
        view.Document.Blocks.AddRange(Citations.BuildBibliography(view.Document, CitationStyle.Apa));
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.citation-style", RibbonCommandContext.ForSelectedValue("IEEE"));

        view.Document.BibliographyStyle.Should().Be(CitationStyle.Ieee);
        view.Document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "CITATION").Text.Should().Be("[1]");
        view.Document.Blocks.Where(Citations.IsBibliographyParagraph)
            .Select(block => ((Paragraph)block).PlainText)
            .Should().Equal("References", "[1] Smith, \"A Work,\" Press, 2024.");

        view.Undo();
        view.Document.BibliographyStyle.Should().Be(CitationStyle.Apa);
        view.Document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "CITATION").Text.Should().Be("(Smith, 2024)");
    }

    [Fact]
    public void References_dialog_commands_noop_without_shell_callbacks()
    {
        var view = ViewWith(
            Heading("First", 1),
            Heading("Second", 1),
            new Paragraph("See "));
        view.Document.Sources.Add(new Source { Tag = "Sm24", Author = "Smith", Title = "A Work", Year = "2024" });
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.cross-reference");
        Execute(registry, "freew.citation");
        Execute(registry, "freew.manage-sources");
        Execute(registry, "freew.mark-citation");

        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Any(run => run.CrossReference is not null)
            .Should().BeFalse("dialog-backed cross-reference must not silently choose the first heading");
        view.Document.Blocks.OfType<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().NotContain(text => text.Contains("Smith", StringComparison.Ordinal),
                "dialog-backed citation must not silently choose the first source");
        view.Document.Sources.Should().ContainSingle().Which.Tag.Should().Be("Sm24");
        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Any(run => run.Citation is not null)
            .Should().BeFalse("dialog-backed mark citation must not silently mark the current paragraph");
    }

    [Fact]
    public void References_dialog_commands_apply_shell_callback_choices()
    {
        var view = ViewWith(
            Heading("First", 1),
            Heading("Second", 1),
            new Paragraph("See "));
        view.Document.Sources.Add(new Source { Tag = "Sm24", Author = "Smith", Title = "A Work", Year = "2024" });
        view.Document.Sources.Add(new Source { Tag = "Jo25", Author = "Jones", Title = "Other Work", Year = "2025" });

        var callbacks = NoopCallbacks() with
        {
            OpenCrossReferenceDialog = () =>
            {
                var targets = CrossReferenceDialogPlanner.BuildTargetChoices(view.Document, CrossRefType.Heading);
                var choice = CrossReferenceDialogPlanner.CreateChoice(
                    CrossRefType.Heading,
                    targets[1].Target,
                    CrossRefInsertAs.Text,
                    hyperlink: true);
                view.InsertCrossReference(choice.Type, choice.Target, choice.InsertAs, choice.Hyperlink);
            },
            OpenCitationDialog = () => view.InsertCitation(view.Document.Sources[1]),
            OpenManageSourcesDialog = () =>
            {
                var state = SourceManagementDialogPlanner.BuildInitialState(view.Document.Sources, masterSources: []);
                var plan = SourceManagementDialogPlanner.AddCurrentSource(
                    state,
                    new SourceManagementSourceEntry("Ng26", "Ng", "Planner Work", "2026", string.Empty));
                var result = SourceManagementDialogPlanner.BuildResult(plan.State);
                view.ReplaceSources(result.CurrentSources);
            },
            OpenMarkCitationDialog = () => view.MarkCitation(
                new Citation("17 U.S.C. 107", CitationCategory.Statutes, "fair use"))
        };
        var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

        Execute(registry, "freew.cross-reference");
        Execute(registry, "freew.citation");
        Execute(registry, "freew.manage-sources");
        Execute(registry, "freew.mark-citation");

        var headings = view.Document.Blocks.OfType<Paragraph>().Take(2).ToList();
        headings[0].BookmarkName.Should().BeNullOrEmpty();
        headings[1].BookmarkName.Should().NotBeNullOrEmpty("the callback selected the second heading");
        view.Document.Blocks.OfType<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain(text => text.Contains("Jones", StringComparison.Ordinal))
            .And.NotContain(text => text.Contains("Smith", StringComparison.Ordinal));
        view.Document.Sources.Select(source => source.Tag).Should().Equal("Sm24", "Jo25", "Ng26");
        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.Citation is not null)
            .Citation.Should().Match<Citation>(citation =>
                citation.Category == CitationCategory.Statutes
                && citation.LongCitation == "17 U.S.C. 107"
                && citation.ShortCitation == "fair use");
    }

    [Fact]
    public Task Table_of_authorities_command_applies_shell_callback_options() =>
        RunOnUiThread(() =>
    {
        var view = ViewWith(new Paragraph("Brown v. Board"));
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks() with
        {
            OpenMarkCitationDialog = () => view.MarkCitation(
                new Citation("Brown v. Board", CitationCategory.Cases, "Brown")),
            ShowTableOfAuthoritiesDialog = () => view.InsertTableOfAuthorities(new ToaOptions
            {
                CategoryFilter = CitationCategory.Cases,
                TabLeader = ToaTabLeader.Underline
            })
        });

        Execute(registry, "freew.mark-citation");
        Execute(registry, "freew.table-of-authorities");

        var entry = view.Document.Blocks
            .OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.PlainText.Should().Be("Brown v. Board\t1");
        entry.Formatting.TabStops.Should().Equal(
            new TabStop(
                TableOfAuthorities.DefaultEntryRightTabStopPt,
                TabStopAlignment.Right,
                TabLeader.Underline));
    });

    [Fact]
    public void Legacy_caption_label_commands_remain_backed()
    {
        var view = ViewWith();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.insert-caption.figure");
        Execute(registry, "freew.insert-caption.table");

        view.Document.Blocks.OfType<Paragraph>()
            .Where(Captions.IsCaptionParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain(text => text.StartsWith("Figure 1", StringComparison.Ordinal))
            .And.Contain(text => text.StartsWith("Table 1", StringComparison.Ordinal));
    }

    private static void Execute(RibbonCommandRegistry registry, string commandId)
    {
        Execute(registry, commandId, RibbonCommandContext.Empty);
    }

    private static void Execute(RibbonCommandRegistry registry, string commandId, RibbonCommandContext context)
    {
        registry.TryGet(new RibbonCommandId(commandId), out var command).Should().BeTrue();
        command!.Execute(context);
    }

    private static IEnumerable<string> CommandIds(RibbonControl control)
    {
        yield return control.CommandId.Value;

        var menu = control switch
        {
            RibbonDropdown dropdown => dropdown.Menu,
            RibbonSplitButton splitButton => splitButton.Menu,
            _ => null,
        };

        if (menu is null)
            yield break;

        foreach (var item in menu.Items)
        {
            if (item.CommandId is { } commandId)
                yield return commandId.Value;
        }
    }

    public static IEnumerable<object[]> CitationStyleLabels() =>
        FreeW.Ribbon.Definitions.FreeWRibbonDefinitionData.CitationStyleNames
            .Select(style => new object[] { style });

    private static IReadOnlyList<string> CitationStyleItems(RibbonDefinition definition)
    {
        var references = definition.FindTab("references");
        references.Should().NotBeNull();
        return references!.Groups
            .SelectMany(group => group.Controls)
            .OfType<RibbonComboBox>()
            .Single(combo => combo.CommandId.Value == "freew.citation-style")
            .Items;
    }

    private static Paragraph CitationMarkParagraph(string longCitation, bool formatted)
    {
        var mark = Run.CitationMark(new Citation(longCitation, CitationCategory.Cases));
        if (formatted)
            mark.Formatting = new RunFormatting { Bold = true, Underline = true, ColorHex = "#C00000" };
        return new Paragraph { Runs = { mark } };
    }

    private static DocumentView ReflowingTableOfAuthoritiesView(bool includeExistingRegion)
    {
        var blocks = new List<Block>();
        if (includeExistingRegion)
        {
            blocks.AddRange(TableOfAuthorities.Build(
                new[] { new Citation("Old Case", CitationCategory.Cases) }));
        }

        for (var i = 0; i < 8; i++)
            blocks.Add(CitationMarkParagraph($"Reflow Case {i + 1}", formatted: false));

        var view = ViewWith([.. blocks]);
        view.Document.Page.WidthPt = 300;
        view.Document.Page.HeightPt = 180;
        view.Document.Page.MarginTopPt = 12;
        view.Document.Page.MarginBottomPt = 12;
        view.Document.Page.MarginLeftPt = 18;
        view.Document.Page.MarginRightPt = 18;
        view.Measure(new global::Avalonia.Size(800, 4000));
        return view;
    }

    private static string[] TableOfAuthoritiesEntries(TextDocument document) =>
        document.Blocks.OfType<Paragraph>()
            .Where(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId)
            .Select(paragraph => paragraph.PlainText)
            .ToArray();

    private static string[] ExpectedReflowEntries() =>
        Enumerable.Range(1, 8)
            .Select(index => $"Reflow Case {index}\t2")
            .ToArray();

    private static DocumentView ReflowingTableOfContentsView(bool includeExistingRegion)
    {
        var blocks = new List<Block>();
        if (includeExistingRegion)
        {
            blocks.Add(new Paragraph(TableOfContents.HeadingText)
            {
                StyleId = TableOfContents.HeadingStyleId
            });
            blocks.Add(new Paragraph("Old Heading\t1")
            {
                StyleId = TableOfContents.EntryStyleId(1)
            });
        }
        blocks.AddRange(Enumerable.Range(1, 8)
            .Select(index => (Block)Heading($"Reflow Chapter {index}", 1)));

        var view = ViewWith([.. blocks]);
        view.Document.Page.WidthPt = 300;
        view.Document.Page.HeightPt = 180;
        view.Document.Page.MarginTopPt = 12;
        view.Document.Page.MarginBottomPt = 12;
        view.Document.Page.MarginLeftPt = 18;
        view.Document.Page.MarginRightPt = 18;
        view.Measure(new global::Avalonia.Size(800, 4000));
        return view;
    }

    private static void AssertTableOfContentsPagesStable(DocumentView view)
    {
        var firstPass = TableOfContentsEntries(view.Document);
        view.UpdateTableOfContents();
        var secondPass = TableOfContentsEntries(view.Document);

        firstPass.Should().Equal(secondPass);
        firstPass.Select(ParsePageReference).Should().OnlyContain(page => page >= 2);
    }

    private static string[] TableOfContentsEntries(TextDocument document) =>
        document.Blocks.Where(TableOfContents.IsTocParagraph)
            .Cast<Paragraph>()
            .Where(paragraph => paragraph.StyleId != TableOfContents.HeadingStyleId)
            .Select(paragraph => paragraph.PlainText)
            .ToArray();

    private static int ParsePageReference(string entry) =>
        int.Parse(entry[(entry.LastIndexOf('\t') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
}
