using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-REF: tests for the References tab — Insert Footnote / Endnote, Table of Contents (insert + update),
/// Insert Caption (Figure / Table), Cross-reference, and Citation / Bibliography. Covers the DocumentView
/// insert methods (model mutation + undo) and that every References command id resolves in the registry.
/// Pure-model — no headless Avalonia backend required.
/// </summary>
public sealed class ReferencesTabTests
{
    private static RibbonHostCallbacks NoopCallbacks() =>
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
            .Contain(new[] { TableOfContents.HeadingText, "Chapter One", "Section A" });

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
        entries.Should().Contain("Second", "update picks up the newly added heading");
        entries.Count(t => t != TableOfContents.HeadingText).Should().Be(2, "now two heading entries");
    }

    // ── Caption ─────────────────────────────────────────────────────────────────────

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

        var texts = view.Document.Blocks.OfType<Paragraph>()
            .Where(Captions.IsCaptionParagraph).Select(p => p.PlainText).ToList();
        texts.Should().Contain(new[] { "Figure 1: A", "Figure 2: B", "Table 1: T" });
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

    // ── Citation / Bibliography ─────────────────────────────────────────────────────

    [Fact]
    public void InsertCitation_inserts_intext_citation_at_caret()
    {
        var view = ViewWith(new Paragraph("See here "));
        var source = new Source { Tag = "Sm24", Author = "Smith", Title = "A Work", Year = "2024" };

        view.InsertCitation(source);

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Contain("Smith",
            "an in-text citation referencing the source is inserted as text");
    }

    [Fact]
    public void InsertBibliography_builds_block_from_sources_and_undo_reverts()
    {
        var view = ViewWith();
        view.Document.Sources.Add(new Source { Tag = "Sm24", Author = "Smith", Title = "A Work", Year = "2024" });
        var before = view.Document.Blocks.Count;

        view.InsertBibliography();

        view.Document.Blocks.Count.Should().BeGreaterThan(before, "bibliography paragraphs are inserted");

        view.Undo();
        view.Document.Blocks.Count.Should().Be(before, "undo removes the whole bibliography block");
    }

    // ── Registry wiring ─────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_resolves_all_references_tab_commands()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());

        var expected = new[]
        {
            "freew.insert-footnote", "freew.insert-endnote",
            "freew.insert-toc", "freew.update-toc",
            "freew.insert-caption", "freew.insert-caption.figure", "freew.insert-caption.table",
            "freew.cross-reference",
            "freew.insert-citation", "freew.bibliography",
        };

        foreach (var id in expected)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"References-tab command '{id}' must be registered");
    }

    [Fact]
    public void References_tab_definition_exposes_groups()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var references = definition.FindTab("references");
        references.Should().NotBeNull();

        references!.Groups.Select(g => g.Header).Should()
            .Contain(new[] { "Table of Contents", "Footnotes", "Citations & Bibliography", "Captions" });
    }

    [Fact]
    public void InsertFootnote_command_executes_via_registry()
    {
        var view = ViewWith();
        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.insert-footnote"), out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);

        view.Document.Footnotes.Should().ContainKey(1, "executing the command inserts a footnote");
    }
}
