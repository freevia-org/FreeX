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
            "freew.footnote", "freew.endnote",
            "freew.insert-footnote", "freew.insert-endnote",
            "freew.toc", "freew.toc-refresh",
            "freew.insert-toc", "freew.update-toc",
            "freew.caption",
            "freew.insert-caption", "freew.insert-caption.figure", "freew.insert-caption.table",
            "freew.cross-reference",
            "freew.citation",
            "freew.insert-citation", "freew.bibliography",
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
    public void References_tab_definition_exposes_groups()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var references = definition.FindTab("references");
        references.Should().NotBeNull();

        references!.Groups.Select(g => g.Header).Should()
            .Contain(new[] { "Table of Contents", "Footnotes", "Citations & Bibliography", "Captions" });
    }

    [Fact]
    public void References_tab_definition_uses_canonical_shared_command_ids()
    {
        var definition = FreeWRibbon.BuildDefinition();
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
            "freew.bibliography",
            "freew.caption",
            "freew.insert-caption.figure",
            "freew.insert-caption.table",
            "freew.cross-reference",
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
    public void Canonical_references_commands_execute_via_registry()
    {
        var view = ViewWith();
        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());

        Execute(registry, "freew.footnote");
        view.Document.Footnotes.Should().ContainKey(1, "executing the canonical command inserts a footnote");

        Execute(registry, "freew.endnote");
        view.Document.Endnotes.Should().ContainKey(1, "executing the canonical command inserts an endnote");

        var tocView = ViewWith(Heading("First", 1), new Paragraph("body"));
        var tocRegistry = FreeWRibbon.BuildRegistry(tocView, NoopCallbacks());

        Execute(tocRegistry, "freew.toc");
        tocView.Document.Blocks.Count(TableOfContents.IsTocParagraph)
            .Should().Be(2, "executing the canonical command inserts a generated TOC");

        tocView.Document.Blocks.Add(Heading("Second", 1));
        Execute(tocRegistry, "freew.toc-refresh");
        tocView.Document.Blocks.Where(TableOfContents.IsTocParagraph)
            .Select(block => ((Paragraph)block).PlainText)
            .Should().Contain("Second", "executing the canonical refresh updates the TOC in place");

        var citationView = ViewWith(new Paragraph("See "));
        citationView.Document.Sources.Add(new Source { Tag = "Sm24", Author = "Smith", Title = "A Work", Year = "2024" });
        var citationRegistry = FreeWRibbon.BuildRegistry(citationView, NoopCallbacks());

        Execute(citationRegistry, "freew.citation");
        citationView.Document.Blocks.OfType<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain(text => text.Contains("Smith", StringComparison.Ordinal),
                "executing the canonical command inserts an in-text citation");
    }

    [Fact]
    public void Legacy_caption_label_commands_remain_backed()
    {
        var view = ViewWith();
        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());

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
        registry.TryGet(new RibbonCommandId(commandId), out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
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
}
