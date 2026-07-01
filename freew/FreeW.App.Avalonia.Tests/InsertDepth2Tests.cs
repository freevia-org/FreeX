using System.Collections.Generic;
using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-INSERT2: Tests for the second tier of Insert-tab commands — Insert Hyperlink, Insert Bookmark,
/// Cover Page, Drop Cap, Quick Parts (document-property fields + snippet), and Equation. Verifies that the
/// model-backed <see cref="DocumentView"/> methods mutate the model, that the new ribbon command ids
/// resolve and route to those methods (the dialog-driven ones through the optional host callbacks), and
/// that each insert is undoable.
/// </summary>
public sealed class InsertDepth2Tests
{
    private static TextDocument MakeDoc(string text = "Hello world")
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    private static DocumentView MakeView(string text = "Hello world")
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc(text));
        return view;
    }

    // Minimal callbacks for the required (non-optional) RibbonHostCallbacks fields; optional AV-INSERT2
    // launchers can be supplied per test.
    private static RibbonHostCallbacks Callbacks(
        Action? hyperlink = null, Action? bookmark = null, Action? quickPart = null, Action? textFromFile = null) =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { }, OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
            OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { },
            InsertPicture: () => { }, OpenWordCountDialog: () => { }, ApplyZoom: (_, _) => { },
            OpenHyperlinkDialog: hyperlink,
            OpenBookmarkDialog: bookmark,
            OpenQuickPartDialog: quickPart,
            InsertTextFromFile: textFromFile);

    private static void Exec(RibbonCommandRegistry r, string id)
    {
        r.TryGet(new RibbonCommandId(id), out var cmd).Should().BeTrue($"command '{id}' must be registered");
        cmd!.Execute(RibbonCommandContext.Empty);
    }

    // ── Registry completeness ─────────────────────────────────────────────────

    [Fact]
    public void Registry_resolves_all_avinsert2_command_ids()
    {
        var view = MakeView();
        var registry = FreeWRibbon.BuildRegistry(view, Callbacks());

        var ids = new[]
        {
            "freew.hyperlink", "freew.bookmark", "freew.bookmark-manager",
            "freew.insert-hyperlink", "freew.insert-bookmark",
            "freew.cover-page", "freew.cover-page.default", "freew.cover-page.banded", "freew.cover-page.motion",
            "freew.drop-cap", "freew.drop-cap.dropped", "freew.drop-cap.in-margin", "freew.drop-cap.none",
            "freew.quick-parts", "freew.quick-parts.title", "freew.quick-parts.author",
            "freew.quick-parts.subject", "freew.quick-parts.date", "freew.quick-parts.snippet",
            "freew.equation", "freew.equation.default", "freew.equation.fraction", "freew.equation.script",
            "freew.equation.radical", "freew.equation.integral", "freew.equation.summation",
            "freew.text-from-file",
        };

        foreach (var id in ids)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"AV-INSERT2 command '{id}' must be registered");
    }

    [Fact]
    public void Whole_ribbon_definition_resolves_in_registry()
    {
        // Ensures the new dropdowns/buttons added to the Insert tab are all wired (registry-completeness).
        var definition = FreeWRibbon.BuildDefinition();
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), Callbacks());

        var ids = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .Select(c => c.CommandId)
            .Where(id => !string.IsNullOrEmpty(id.Value))
            .ToList();

        foreach (var id in ids)
            registry.TryGet(id, out _).Should().BeTrue($"'{id.Value}' declared in ribbon but not registered");
    }

    [Fact]
    public void New_avinsert2_callbacks_are_optional()
    {
        // The four AV-INSERT2 host callbacks must default to null so existing call sites still compile and
        // the registry no-ops when the shell did not supply them.
        var callbacks = new RibbonHostCallbacks(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { }, OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
            OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { },
            InsertPicture: () => { }, OpenWordCountDialog: () => { }, ApplyZoom: (_, _) => { });

        callbacks.OpenHyperlinkDialog.Should().BeNull();
        callbacks.OpenBookmarkDialog.Should().BeNull();
        callbacks.OpenQuickPartDialog.Should().BeNull();
        callbacks.InsertTextFromFile.Should().BeNull();

        // Executing the dialog-driven commands with null callbacks must not throw.
        var registry = FreeWRibbon.BuildRegistry(MakeView(), callbacks);
        Exec(registry, "freew.insert-hyperlink");
        Exec(registry, "freew.insert-bookmark");
        Exec(registry, "freew.quick-parts.snippet");
        Exec(registry, "freew.text-from-file");
    }

    // ── Hyperlink ──────────────────────────────────────────────────────────────

    [Fact]
    public void Insert_hyperlink_command_applies_to_model_through_callback()
    {
        var view = MakeView("");
        // The dialog launcher is simulated by a callback that inserts a known link.
        var registry = FreeWRibbon.BuildRegistry(view,
            Callbacks(hyperlink: () => view.InsertHyperlink("Anthropic", "https://anthropic.com")));

        Exec(registry, "freew.insert-hyperlink");

        var para = (Paragraph)view.Document.Blocks[0];
        para.PlainText.Should().Be("Anthropic");
        para.Runs.Any(run => run.HyperlinkUrl == "https://anthropic.com")
            .Should().BeTrue("the inserted run must carry the external link URL");
    }

    [Fact]
    public void Wpf_hyperlink_command_id_routes_to_existing_hyperlink_callback()
    {
        var view = MakeView("");
        var registry = FreeWRibbon.BuildRegistry(view,
            Callbacks(hyperlink: () => view.InsertHyperlink("WPF Link", "https://wpf.example")));

        Exec(registry, "freew.hyperlink");

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.Should().Contain(run => run.HyperlinkUrl == "https://wpf.example");
    }

    [Fact]
    public void Insert_hyperlink_is_undoable()
    {
        var view = MakeView("");
        view.InsertHyperlink("Link", "https://example.com");
        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("Link");

        view.Undo();
        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().BeEmpty("Undo should remove the hyperlink text");
    }

    // ── Bookmark ───────────────────────────────────────────────────────────────

    [Fact]
    public void Insert_bookmark_command_marks_caret_paragraph_through_callback()
    {
        var view = MakeView("Target");
        var registry = FreeWRibbon.BuildRegistry(view,
            Callbacks(bookmark: () => view.InsertBookmark("Mark1")));

        Exec(registry, "freew.insert-bookmark");

        Bookmarks.List(view.Document).Any(b => b.Name == "Mark1")
            .Should().BeTrue("the caret paragraph must carry the bookmark name");
    }

    [Theory]
    [InlineData("freew.bookmark")]
    [InlineData("freew.bookmark-manager")]
    public void Wpf_bookmark_command_ids_route_to_existing_bookmark_callback(string commandId)
    {
        var view = MakeView("Target");
        var registry = FreeWRibbon.BuildRegistry(view,
            Callbacks(bookmark: () => view.InsertBookmark(commandId.Replace('.', '-'))));

        Exec(registry, commandId);

        Bookmarks.List(view.Document).Should().Contain(b => b.Name == commandId.Replace('.', '-'));
    }

    [Fact]
    public void Insert_bookmark_is_undoable()
    {
        var view = MakeView("Target");
        view.InsertBookmark("Mark1");
        Bookmarks.List(view.Document).Should().ContainSingle(b => b.Name == "Mark1");

        view.Undo();
        Bookmarks.List(view.Document).Any(b => b.Name == "Mark1")
            .Should().BeFalse("Undo should remove the bookmark");
    }

    // ── Cover Page ─────────────────────────────────────────────────────────────

    [Fact]
    public void Cover_page_command_prepends_blocks()
    {
        var doc = MakeDoc("Body");
        doc.Properties.Title = "My Title";
        doc.Properties.Author = "Jane";
        var view = new DocumentView();
        view.LoadDocument(doc);

        var before = view.Document.Blocks.Count;
        var registry = FreeWRibbon.BuildRegistry(view, Callbacks());
        Exec(registry, "freew.cover-page.default");

        view.Document.Blocks.Count.Should().BeGreaterThan(before, "cover-page inserts blocks at the start");
        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("My Title",
            "the first cover-page block is the document title");
    }

    [Fact]
    public void Cover_page_is_undoable_as_one_step()
    {
        var doc = MakeDoc("Body");
        doc.Properties.Title = "T";
        var view = new DocumentView();
        view.LoadDocument(doc);
        var before = view.Document.Blocks.Count;

        view.InsertCoverPage(CoverPagePreset.Default);
        view.Document.Blocks.Count.Should().BeGreaterThan(before);

        view.Undo();
        view.Document.Blocks.Count.Should().Be(before, "one Undo should remove the whole cover page");
    }

    // ── Drop Cap ───────────────────────────────────────────────────────────────

    [Fact]
    public void Drop_cap_command_enlarges_leading_letter()
    {
        var view = MakeView("Hello");
        var registry = FreeWRibbon.BuildRegistry(view, Callbacks());

        Exec(registry, "freew.drop-cap.dropped");

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.Count.Should().BeGreaterThan(1, "the leading letter is split into its own enlarged run");
        para.Runs[0].Text.Should().Be("H");
        para.Runs[0].Formatting.FontSizePt.Should().Be(DropCap.DefaultSizePt);
        para.Runs[0].Formatting.Bold.Should().BeTrue();
        para.PlainText.Should().Be("Hello", "the visible text is unchanged");
    }

    [Fact]
    public void Drop_cap_is_undoable()
    {
        var view = MakeView("Hello");
        view.ApplyDropCap();
        ((Paragraph)view.Document.Blocks[0]).Runs.Count.Should().BeGreaterThan(1);

        view.Undo();
        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.Count.Should().Be(1, "Undo should restore the single run");
        para.Runs[0].Formatting.FontSizePt.Should().NotBe(DropCap.DefaultSizePt);
    }

    [Fact]
    public void Drop_cap_none_clears_run_formatting()
    {
        var view = MakeView("Hello");
        view.ApplyDropCap();
        var registry = FreeWRibbon.BuildRegistry(view, Callbacks());

        Exec(registry, "freew.drop-cap.none");

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(run => !run.Formatting.Bold && run.Formatting.FontSizePt == RunFormatting.Default.FontSizePt)
            .Should().BeTrue("None resets every run's formatting to the default");
    }

    // ── Quick Parts (document-property fields + snippet) ───────────────────────

    [Fact]
    public void Quick_part_title_field_inserts_field_run()
    {
        var doc = MakeDoc("");
        doc.Properties.Title = "Doc Title";
        var view = new DocumentView();
        view.LoadDocument(doc);
        var registry = FreeWRibbon.BuildRegistry(view, Callbacks());

        Exec(registry, "freew.quick-parts.title");

        var hasTitleField = view.Document.Blocks
            .OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Any(run => run.FieldKind == RunFieldKind.Title);
        hasTitleField.Should().BeTrue("a Title document-property field run must be inserted");
    }

    [Fact]
    public void Quick_part_date_field_is_undoable()
    {
        var view = MakeView("X");
        view.InsertField(RunFieldKind.Date);
        view.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .Any(run => run.FieldKind == RunFieldKind.Date).Should().BeTrue();

        view.Undo();
        view.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .Any(run => run.FieldKind == RunFieldKind.Date).Should().BeFalse("Undo removes the field run");
    }

    [Fact]
    public void Quick_part_snippet_inserts_text_through_callback()
    {
        var view = MakeView("");
        var registry = FreeWRibbon.BuildRegistry(view,
            Callbacks(quickPart: () => view.InsertQuickPartText("Snippet body")));

        Exec(registry, "freew.quick-parts.snippet");

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("Snippet body");
    }

    [Fact]
    public void Quick_part_multiline_snippet_creates_paragraphs()
    {
        var view = MakeView("");
        view.InsertQuickPartText("Line 1\nLine 2\nLine 3");

        var paras = view.Document.Blocks.OfType<Paragraph>().ToList();
        paras.Count.Should().BeGreaterThanOrEqualTo(3, "each snippet line becomes a paragraph");
        paras[0].PlainText.Should().Be("Line 1");
        paras.Any(p => p.PlainText == "Line 3").Should().BeTrue();
    }

    // ── Equation ───────────────────────────────────────────────────────────────

    [Fact]
    public void Equation_default_inserts_equation_run()
    {
        var view = MakeView("");
        var registry = FreeWRibbon.BuildRegistry(view, Callbacks());

        Exec(registry, "freew.equation.default");

        var eqRun = view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .FirstOrDefault(run => run.Equation is not null);
        eqRun.Should().NotBeNull("an inline equation run must be inserted");
        eqRun!.Equation!.LinearText.Should().Contain("E = m", "the default sample is E = mc²");
    }

    [Fact]
    public void Equation_fraction_preset_inserts_fraction()
    {
        var view = MakeView("");
        var registry = FreeWRibbon.BuildRegistry(view, Callbacks());

        Exec(registry, "freew.equation.fraction");

        var eqRun = view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .FirstOrDefault(run => run.Equation is not null);
        eqRun.Should().NotBeNull();
        eqRun!.Equation!.Runs[0].Kind.Should().Be(MathRunKind.Fraction);
    }

    [Fact]
    public void Equation_is_undoable()
    {
        var view = MakeView("");
        view.InsertEquation();
        view.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .Any(run => run.Equation is not null).Should().BeTrue();

        view.Undo();
        view.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .Any(run => run.Equation is not null).Should().BeFalse("Undo removes the equation run");
    }
}
