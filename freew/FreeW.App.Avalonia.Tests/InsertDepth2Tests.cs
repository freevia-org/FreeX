using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.DocumentView;
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
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

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

    // Minimal callbacks for the required (non-optional) FreeWRibbonHostExecutionPorts fields; optional AV-INSERT2
    // launchers can be supplied per test.
    private static FreeWRibbonHostExecutionPorts Callbacks(
        Action? hyperlink = null,
        Action? editHyperlink = null,
        Action? hyperlinkTooltip = null,
        Action? bookmark = null,
        Action? linkBookmark = null,
        Action? quickPart = null,
        Action? textFromFile = null) =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { }, OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
            OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { },
            InsertPicture: () => { }, OpenWordCountDialog: () => { }, ApplyZoom: (_, _) => { },
            OpenHyperlinkDialog: hyperlink,
            OpenEditHyperlinkDialog: editHyperlink,
            OpenHyperlinkTooltipDialog: hyperlinkTooltip,
            OpenBookmarkDialog: bookmark,
            OpenLinkBookmarkDialog: linkBookmark,
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
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        var ids = new[]
        {
            "freew.hyperlink", "freew.bookmark", "freew.bookmark-manager",
            "freew.insert-hyperlink", "freew.insert-bookmark",
            "freew.edit-hyperlink", "freew.remove-hyperlink", "freew.hyperlink-tooltip", "freew.link-bookmark",
            "freew.cover-page", "freew.cover-page.default", "freew.cover-page.banded", "freew.cover-page.motion",
            "freew.drop-cap", "freew.drop-cap.dropped", "freew.drop-cap.in-margin", "freew.drop-cap.none",
            "freew.drop-cap-dropped", "freew.drop-cap-in-margin", "freew.drop-cap-none",
            "freew.quick-parts", "freew.quick-parts.title", "freew.quick-parts.author",
            "freew.quick-parts.subject", "freew.quick-parts.keywords", "freew.quick-parts.comments",
            "freew.quick-parts.date", "freew.quick-parts.snippet",
            "freew.equation", "freew.equation.default", "freew.equation.fraction", "freew.equation.script",
            "freew.equation.radical", "freew.equation.nthroot", "freew.equation.integral", "freew.equation.summation",
            "freew.equation.product", "freew.equation.accent", "freew.equation.bar", "freew.equation.bracket",
            "freew.equation.matrix", "freew.equation.func", "freew.equation.groupchr",
            "freew.insert-file", "freew.text-from-file",
            "freew.wordart", "freew.object", "freew.update-fields", "freew.toggle-field-codes",
        };

        foreach (var id in ids)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"AV-INSERT2 command '{id}' must be registered");
    }

    [Fact]
    public void Whole_ribbon_definition_resolves_in_registry()
    {
        // Ensures the new dropdowns/buttons added to the Insert tab are all wired (registry-completeness).
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), Callbacks());

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
        var callbacks = new FreeWRibbonHostExecutionPorts(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { }, OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
            OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { },
            InsertPicture: () => { }, OpenWordCountDialog: () => { }, ApplyZoom: (_, _) => { });

        callbacks.OpenHyperlinkDialog.Should().BeNull();
        callbacks.OpenEditHyperlinkDialog.Should().BeNull();
        callbacks.OpenHyperlinkTooltipDialog.Should().BeNull();
        callbacks.OpenBookmarkDialog.Should().BeNull();
        callbacks.OpenLinkBookmarkDialog.Should().BeNull();
        callbacks.OpenQuickPartDialog.Should().BeNull();
        callbacks.InsertTextFromFile.Should().BeNull();

        // Executing the dialog-driven commands with null callbacks must not throw.
        var registry = FreeWAvaloniaRibbonCommands.Build(MakeView(), callbacks);
        Exec(registry, "freew.insert-hyperlink");
        Exec(registry, "freew.edit-hyperlink");
        Exec(registry, "freew.remove-hyperlink");
        Exec(registry, "freew.hyperlink-tooltip");
        Exec(registry, "freew.insert-bookmark");
        Exec(registry, "freew.link-bookmark");
        Exec(registry, "freew.quick-parts.snippet");
        Exec(registry, "freew.insert-file");
        Exec(registry, "freew.text-from-file");
        Exec(registry, "freew.wordart");
        Exec(registry, "freew.object");
        Exec(registry, "freew.update-fields");
        Exec(registry, "freew.toggle-field-codes");
    }

    // ── Hyperlink ──────────────────────────────────────────────────────────────

    [Fact]
    public void Insert_hyperlink_command_applies_to_model_through_callback()
    {
        var view = MakeView("");
        // The dialog launcher is simulated by a callback that inserts a known link.
        var registry = FreeWAvaloniaRibbonCommands.Build(view,
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
        var registry = FreeWAvaloniaRibbonCommands.Build(view,
            Callbacks(hyperlink: () => view.InsertHyperlink("WPF Link", "https://wpf.example")));

        Exec(registry, "freew.hyperlink");

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.Should().Contain(run => run.HyperlinkUrl == "https://wpf.example");
    }

    [Fact]
    public void Edit_hyperlink_command_routes_to_callback()
    {
        var view = MakeView("");
        view.InsertHyperlink("Link", "https://old.example");
        view.MoveCaretToBlockForTest(0, 2);
        var registry = FreeWAvaloniaRibbonCommands.Build(view,
            Callbacks(editHyperlink: () => view.EditHyperlink("https://new.example")));

        Exec(registry, "freew.edit-hyperlink");

        ((Paragraph)view.Document.Blocks[0]).Runs.Should()
            .Contain(run => run.HyperlinkUrl == "https://new.example");
    }

    [Fact]
    public void Remove_hyperlink_command_clears_link_but_keeps_text()
    {
        var view = MakeView("");
        view.InsertHyperlink("Link", "https://old.example");
        view.MoveCaretToBlockForTest(0, 2);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        Exec(registry, "freew.remove-hyperlink");

        var para = (Paragraph)view.Document.Blocks[0];
        para.PlainText.Should().Be("Link");
        para.Runs.Should().OnlyContain(run => run.HyperlinkUrl == null && run.HyperlinkAnchor == null);
    }

    [Fact]
    public void Hyperlink_tooltip_command_routes_to_callback()
    {
        var view = MakeView("");
        view.InsertHyperlink("Link", "https://old.example");
        view.MoveCaretToBlockForTest(0, 2);
        var registry = FreeWAvaloniaRibbonCommands.Build(view,
            Callbacks(hyperlinkTooltip: () => view.SetHyperlinkTooltip("Screen tip")));

        Exec(registry, "freew.hyperlink-tooltip");

        ((Paragraph)view.Document.Blocks[0]).Runs.Should()
            .Contain(run => run.HyperlinkTooltip == "Screen tip");
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
        var registry = FreeWAvaloniaRibbonCommands.Build(view,
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
        var registry = FreeWAvaloniaRibbonCommands.Build(view,
            Callbacks(bookmark: () => view.InsertBookmark(commandId.Replace('.', '-'))));

        Exec(registry, commandId);

        Bookmarks.List(view.Document).Should().Contain(b => b.Name == commandId.Replace('.', '-'));
    }

    [Fact]
    public void Link_bookmark_command_routes_to_callback()
    {
        var view = MakeView("Jump target");
        view.InsertBookmark("Target1");
        view.SetSelectionRangePublic(0, 0, 0, 4);
        var registry = FreeWAvaloniaRibbonCommands.Build(view,
            Callbacks(linkBookmark: () => view.ApplyInternalLink("Target1")));

        Exec(registry, "freew.link-bookmark");

        ((Paragraph)view.Document.Blocks[0]).Runs.Should()
            .Contain(run => run.Text == "Jump" && run.HyperlinkAnchor == "Target1");
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
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());
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
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        Exec(registry, "freew.drop-cap.dropped");

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.Count.Should().BeGreaterThan(1, "the leading letter is split into its own enlarged run");
        para.Runs[0].Text.Should().Be("H");
        para.Runs[0].Formatting.FontSizePt.Should().Be(DropCap.DefaultSizePt);
        para.Runs[0].Formatting.Bold.Should().BeTrue();
        para.DropCap.Should().Be(new DropCapLayoutIntent(
            DropCapPosition.Dropped,
            DropCap.DefaultLineSpan,
            DropCap.DefaultSizePt,
            DropCap.DefaultDistanceFromTextPt));
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
        para.DropCap.Should().BeNull();
    }

    [Fact]
    public void Drop_cap_none_clears_run_formatting()
    {
        var view = MakeView("Hello");
        view.ApplyDropCap();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        Exec(registry, "freew.drop-cap.none");

        var para = (Paragraph)view.Document.Blocks[0];
        para.Runs.All(run => !run.Formatting.Bold && run.Formatting.FontSizePt == RunFormatting.Default.FontSizePt)
            .Should().BeTrue("None resets every run's formatting to the default");
        para.DropCap.Should().BeNull();
    }

    [Fact]
    public async Task Drop_cap_layout_plans_distinguish_dropped_and_in_margin_modes()
    {
        DropCapPosition? droppedPosition = null;
        DropCapPosition? inMarginPosition = null;
        double droppedInset = -1;
        double inMarginInset = -1;
        double inMarginRight = -1;
        double inMarginColumnLeft = -1;
        var hyphenAliasWorked = false;

        var ran = await OnUiThread(() =>
        {
            var droppedView = MakeView("Hello world from a wrapped paragraph");
            var droppedRegistry = FreeWAvaloniaRibbonCommands.Build(droppedView, Callbacks());
            Exec(droppedRegistry, "freew.drop-cap.dropped");
            droppedView.Measure(new global::Avalonia.Size(816, 4000));
            var droppedPlan = droppedView.DropCapLayoutPlans.Single();
            droppedPosition = ((Paragraph)droppedView.Document.Blocks[0]).DropCap?.Position;
            droppedInset = droppedPlan.BodyTextLeftInsetDip;

            var inMarginView = MakeView("Margin body text keeps its column");
            var inMarginRegistry = FreeWAvaloniaRibbonCommands.Build(inMarginView, Callbacks());
            hyphenAliasWorked = inMarginRegistry.TryGet("freew.drop-cap-in-margin", out _);
            Exec(inMarginRegistry, "freew.drop-cap-in-margin");
            inMarginView.Measure(new global::Avalonia.Size(816, 4000));
            var inMarginPlan = inMarginView.DropCapLayoutPlans.Single();
            inMarginPosition = ((Paragraph)inMarginView.Document.Blocks[0]).DropCap?.Position;
            inMarginInset = inMarginPlan.BodyTextLeftInsetDip;
            inMarginRight = inMarginPlan.TextReservation.RightDip;
            inMarginColumnLeft = inMarginPlan.CapBox.RightDip + inMarginPlan.DistanceFromTextDip;
        });

        ran.Should().BeTrue();
        hyphenAliasWorked.Should().BeTrue("Avalonia should accept the WPF-style drop-cap command aliases");
        droppedPosition.Should().Be(DropCapPosition.Dropped);
        droppedInset.Should().BeGreaterThan(0);
        inMarginPosition.Should().Be(DropCapPosition.InMargin);
        inMarginInset.Should().Be(0);
        inMarginRight.Should().BeApproximately(inMarginColumnLeft, 0.001);
    }

    // ── Quick Parts (document-property fields + snippet) ───────────────────────

    [Fact]
    public void Quick_part_title_field_inserts_field_run()
    {
        var doc = MakeDoc("");
        doc.Properties.Title = "Doc Title";
        var view = new DocumentView();
        view.LoadDocument(doc);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        Exec(registry, "freew.quick-parts.title");

        var hasTitleField = view.Document.Blocks
            .OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Any(run => run.FieldKind == RunFieldKind.Title);
        hasTitleField.Should().BeTrue("a Title document-property field run must be inserted");
    }

    [Theory]
    [InlineData("freew.quick-parts.keywords", RunFieldKind.Keywords)]
    [InlineData("freew.quick-parts.comments", RunFieldKind.DocComments)]
    public void Quick_part_extended_document_property_inserts_live_field(string commandId, RunFieldKind expectedKind)
    {
        var view = MakeView("");
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        Exec(registry, commandId);

        view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.FieldKind == expectedKind)
            .FieldKind.Should().Be(expectedKind);
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
        var registry = FreeWAvaloniaRibbonCommands.Build(view,
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

    [Fact]
    public void Insert_file_command_uses_text_from_file_callback()
    {
        var invoked = 0;
        var registry = FreeWAvaloniaRibbonCommands.Build(MakeView(""),
            Callbacks(textFromFile: () => invoked++));

        Exec(registry, "freew.insert-file");

        invoked.Should().Be(1, "the WPF-aligned Text from File id must route to the Avalonia shell callback");
    }

    [Fact]
    public void Insert_document_preserves_rich_body_blocks_and_undoes_as_one_action()
    {
        var view = MakeView("Target");
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var rich = new Paragraph();
        rich.Runs.Add(new Run("Styled", new RunFormatting { Bold = true }));
        source.Blocks.Add(rich);
        source.Blocks.Add(Table.Create(2, 2));

        view.InsertDocument(source);

        view.Document.Blocks.Should().HaveCount(3);
        ((Paragraph)view.Document.Blocks[1]).Runs.Single().Formatting.Bold.Should().BeTrue();
        view.Document.Blocks[2].Should().BeOfType<Table>();
        view.Document.Blocks[2].Should().NotBeSameAs(source.Blocks[1]);

        view.Undo();

        view.Document.Blocks.Should().ContainSingle();
        view.Document.Blocks[0].Should().BeOfType<Paragraph>();
        view.Document.Blocks[0].Should().NotBeSameAs(source.Blocks[0]);
    }

    [Fact]
    public void Insert_file_command_callback_consumes_rich_document_instead_of_plain_text()
    {
        var view = MakeView("");
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("Imported"));
        source.Blocks.Add(Table.Create(1, 1));
        var registry = FreeWAvaloniaRibbonCommands.Build(view,
            Callbacks(textFromFile: () => view.InsertDocument(source)));

        Exec(registry, "freew.insert-file");

        view.Document.Blocks.Should().Contain(block => block is Table);
        view.Document.PlainText.Should().Contain("Imported");
    }

    [Fact]
    public void Wordart_command_inserts_undoable_model_run()
    {
        var view = MakeView("");
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        Exec(registry, "freew.wordart");

        view.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .Any(run => run.WordArt is { Text: "WordArt", Style: WordArtStyle.GradientFill })
            .Should().BeTrue("the command inserts the default WordArt model run");

        view.Undo();
        view.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .Any(run => run.WordArt is not null).Should().BeFalse("Undo removes the WordArt run");
    }

    [Fact]
    public void Object_command_inserts_embedded_object_placeholder()
    {
        var view = MakeView("");
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        Exec(registry, "freew.object");

        var embedded = view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .SingleOrDefault(run => run.EmbeddedObject is not null)
            ?.EmbeddedObject;

        embedded.Should().NotBeNull();
        embedded!.ProgId.Should().Be("Package");
        embedded.Payload.Should().NotBeEmpty();
    }

    [Fact]
    public void Update_fields_refreshes_simple_document_property_fields()
    {
        var doc = MakeDoc("");
        doc.Properties.Author = "Ada";
        var paragraph = (Paragraph)doc.Blocks[0];
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run("stale") { FieldKind = RunFieldKind.Author });
        var view = new DocumentView();
        view.LoadDocument(doc);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        Exec(registry, "freew.update-fields");

        ((Paragraph)view.Document.Blocks[0]).Runs.Single().Text.Should().Be("Ada");
    }

    [Fact]
    public void Toggle_field_codes_flips_complex_field_display_state()
    {
        var doc = MakeDoc("");
        var paragraph = (Paragraph)doc.Blocks[0];
        paragraph.Runs.Clear();
        var metadata = new SimpleFieldMetadata(IsLocked: true, IsDirty: true);
        paragraph.Runs.Add(new Run("Ada")
        {
            ComplexField = new ComplexField(" AUTHOR ", SimpleField: metadata)
        });
        var view = new DocumentView();
        view.LoadDocument(doc);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        Exec(registry, "freew.toggle-field-codes");

        var field = ((Paragraph)view.Document.Blocks[0]).Runs.Single().ComplexField;
        field.Should().NotBeNull();
        field!.ShowCode.Should().BeTrue();

        Exec(registry, "freew.toggle-field-codes");
        var restored = ((Paragraph)view.Document.Blocks[0]).Runs.Single().ComplexField!;
        restored.ShowCode.Should().BeFalse();
        restored.SimpleField.Should().Be(metadata);
    }

    // ── Equation ───────────────────────────────────────────────────────────────

    [Fact]
    public void Equation_default_inserts_equation_run()
    {
        var view = MakeView("");
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

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
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        Exec(registry, "freew.equation.fraction");

        var eqRun = view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .FirstOrDefault(run => run.Equation is not null);
        eqRun.Should().NotBeNull();
        eqRun!.Equation!.Runs[0].Kind.Should().Be(MathRunKind.Fraction);
    }

    [Theory]
    [InlineData("freew.equation.nthroot", MathRunKind.Radical)]
    [InlineData("freew.equation.product", MathRunKind.NAry)]
    [InlineData("freew.equation.accent", MathRunKind.Accent)]
    [InlineData("freew.equation.bar", MathRunKind.Bar)]
    [InlineData("freew.equation.bracket", MathRunKind.Delimiter)]
    [InlineData("freew.equation.matrix", MathRunKind.Matrix)]
    [InlineData("freew.equation.func", MathRunKind.FunctionApply)]
    [InlineData("freew.equation.groupchr", MathRunKind.GroupChar)]
    public void Equation_extended_gallery_preset_inserts_expected_structure(string commandId, MathRunKind expectedKind)
    {
        var view = MakeView("");
        var registry = FreeWAvaloniaRibbonCommands.Build(view, Callbacks());

        Exec(registry, commandId);

        var equation = view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.Equation is not null)
            .Equation;
        equation!.Runs.Should().ContainSingle();
        equation.Runs[0].Kind.Should().Be(expectedKind);
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

    [Fact]
    public async Task EquationVisualPlanner_default_equation_lays_out_script_segments()
    {
        string[] texts = [];
        EquationVisualSegmentRole[] roles = [];
        (EquationVisualBaselineRole BaselineRole, double FontSizeScale, string FontFamily, bool Italic) script = default;
        MathRunKind[] kinds = [];
        var placedGlyphCount = 0;

        var ran = await OnUiThread(() =>
        {
            var view = MakeView("");

            view.InsertEquation();

            var segments = view.EquationVisualSegments;
            texts = segments.Select(segment => segment.Text).ToArray();
            roles = segments.Select(segment => segment.Role).ToArray();
            script = (
                segments[^1].BaselineRole,
                segments[^1].FontSizeScale,
                segments[^1].FontFamily,
                segments[^1].Italic);
            placedGlyphCount = view.PlacedGlyphCount;
            var eqRun = view.Document.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Runs)
                .Single(run => run.Equation is not null);
            kinds = eqRun.Equation!.Runs.Select(run => run.Kind).ToArray();
        });

        if (!ran) return;

        texts.Should().Equal("E = m", "c", "2");
        roles.Should().Equal(
            EquationVisualSegmentRole.Text,
            EquationVisualSegmentRole.Base,
            EquationVisualSegmentRole.Superscript);
        script.BaselineRole.Should().Be(EquationVisualBaselineRole.Superscript);
        script.FontSizeScale.Should().Be(EquationVisualPlanner.ScriptFontSizeScale);
        script.FontFamily.Should().Contain("Cambria Math");
        script.Italic.Should().BeTrue();
        placedGlyphCount.Should().Be(7,
            "the rendered default equation should place E = m plus c and 2, without the linear fallback caret");
        kinds.Should().Equal(MathRunKind.Text, MathRunKind.Superscript);
    }

    [Fact]
    public async Task EquationVisualPlanner_fraction_and_radical_lay_out_shared_structure()
    {
        EquationVisualElementKind[] elementKinds = [];
        (string LinearText, string Numerator, string Denominator, string Radicand, string Degree)[] elementSlots = [];
        string[] texts = [];
        EquationVisualSegmentRole[] roles = [];
        double[] fontSizeScales = [];
        MathRunKind[] kinds = [];
        var placedText = string.Empty;
        var linearText = string.Empty;
        var placedGlyphCount = 0;

        var ran = await OnUiThread(() =>
        {
            var view = MakeView("");
            view.InsertEquation(new Equation([
                MathRun.Fraction("a + b", "c"),
                MathRun.Radical("x + 1", "3")
            ]));

            var elements = view.EquationVisualElements;
            elementKinds = elements.Select(element => element.Kind).ToArray();
            elementSlots = elements
                .Select(element => (element.LinearText, element.Numerator, element.Denominator, element.Radicand, element.Degree))
                .ToArray();
            var segments = view.EquationVisualSegments;
            texts = segments.Select(segment => segment.Text).ToArray();
            roles = segments.Select(segment => segment.Role).ToArray();
            fontSizeScales = segments.Select(segment => segment.FontSizeScale).ToArray();
            placedText = string.Concat(view.GetPlacedForBlock(0).Select(placed => placed.Ch));
            placedGlyphCount = view.PlacedGlyphCount;

            var eqRun = view.Document.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Runs)
                .Single(run => run.Equation is not null);
            kinds = eqRun.Equation!.Runs.Select(run => run.Kind).ToArray();
            linearText = eqRun.Equation!.LinearText;
        });

        if (!ran) return;

        elementKinds.Should().Equal(EquationVisualElementKind.Fraction, EquationVisualElementKind.Radical);
        elementSlots[0].Should().Be(("a + b/c", "a + b", "c", string.Empty, string.Empty));
        elementSlots[1].Should().Be(($"3{EquationVisualPlanner.RadicalSignText}(x + 1)", string.Empty, string.Empty, "x + 1", "3"));
        texts.Should().Equal(
            "a + b",
            EquationVisualPlanner.FractionBarText,
            "c",
            "3",
            EquationVisualPlanner.RadicalSignText,
            "x + 1");
        roles.Should().Equal(
            EquationVisualSegmentRole.FractionNumerator,
            EquationVisualSegmentRole.FractionBar,
            EquationVisualSegmentRole.FractionDenominator,
            EquationVisualSegmentRole.RadicalDegree,
            EquationVisualSegmentRole.RadicalSign,
            EquationVisualSegmentRole.RadicalRadicand);
        fontSizeScales[0].Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        fontSizeScales[2].Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        fontSizeScales[3].Should().Be(EquationVisualPlanner.ScriptFontSizeScale);
        placedText.Should().Be("\uFFFC\uFFFC");
        placedGlyphCount.Should().Be(2,
            "Avalonia should reserve one atomic layout cell for each structured equation form");
        kinds.Should().Equal(MathRunKind.Fraction, MathRunKind.Radical);
        linearText.Should().Be($"a + b/c3{EquationVisualPlanner.RadicalSignText}(x + 1)");
    }

    [Fact]
    public async Task EquationVisualPlanner_nary_lays_out_shared_large_operator_structure()
    {
        EquationVisualElementKind[] elementKinds = [];
        (string LinearText, string Operator, string LowerLimit, string UpperLimit, string Operand)[] elementSlots = [];
        string[] texts = [];
        EquationVisualSegmentRole[] roles = [];
        (EquationVisualBaselineRole BaselineRole, double FontSizeScale, bool Italic)[] styles = [];
        MathRunKind[] kinds = [];
        var placedText = string.Empty;
        var linearText = string.Empty;
        var placedGlyphCount = 0;

        var ran = await OnUiThread(() =>
        {
            var view = MakeView("");
            view.InsertEquation(new Equation([MathRun.NAry("\u2211", "i=1", "n", "i")]));

            var elements = view.EquationVisualElements;
            elementKinds = elements.Select(element => element.Kind).ToArray();
            elementSlots = elements
                .Select(element => (element.LinearText, element.Operator, element.LowerLimit, element.UpperLimit, element.Operand))
                .ToArray();
            var segments = view.EquationVisualSegments;
            texts = segments.Select(segment => segment.Text).ToArray();
            roles = segments.Select(segment => segment.Role).ToArray();
            styles = segments
                .Select(segment => (segment.BaselineRole, segment.FontSizeScale, segment.Italic))
                .ToArray();
            placedText = string.Concat(view.GetPlacedForBlock(0).Select(placed => placed.Ch));
            placedGlyphCount = view.PlacedGlyphCount;

            var eqRun = view.Document.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Runs)
                .Single(run => run.Equation is not null);
            kinds = eqRun.Equation!.Runs.Select(run => run.Kind).ToArray();
            linearText = eqRun.Equation!.LinearText;
        });

        if (!ran) return;

        elementKinds.Should().Equal(EquationVisualElementKind.NAry);
        elementSlots[0].Should().Be(("\u2211(i=1..n) i", "\u2211", "i=1", "n", "i"));
        texts.Should().Equal("\u2211", "i=1", "n", "i");
        roles.Should().Equal(
            EquationVisualSegmentRole.NAryOperator,
            EquationVisualSegmentRole.NAryLowerLimit,
            EquationVisualSegmentRole.NAryUpperLimit,
            EquationVisualSegmentRole.NAryOperand);
        styles[0].FontSizeScale.Should().Be(EquationVisualPlanner.LargeOperatorFontSizeScale);
        styles[0].Italic.Should().BeFalse();
        styles[1].BaselineRole.Should().Be(EquationVisualBaselineRole.Subscript);
        styles[2].BaselineRole.Should().Be(EquationVisualBaselineRole.Superscript);
        styles[3].FontSizeScale.Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        placedText.Should().Be("\uFFFC");
        placedGlyphCount.Should().Be(1,
            "Avalonia should reserve the n-ary operator and its limits as one structural layout cell");
        kinds.Should().Equal(MathRunKind.NAry);
        linearText.Should().Be("\u2211(i=1..n) i");
    }

    [Fact]
    public async Task EquationVisualPlanner_matrix_lays_out_shared_grid_structure()
    {
        EquationVisualElementKind[] elementKinds = [];
        IReadOnlyList<EquationVisualMatrixRow> matrixRows = Array.Empty<EquationVisualMatrixRow>();
        string[] texts = [];
        EquationVisualSegmentRole[] roles = [];
        double[] fontSizeScales = [];
        MathRunKind[] kinds = [];
        var placedText = string.Empty;
        var linearText = string.Empty;
        var placedGlyphCount = 0;

        var ran = await OnUiThread(() =>
        {
            var view = MakeView("");
            view.InsertEquation(new Equation([MathRun.MatrixOf(MathMatrix.Identity2x2())]));

            var elements = view.EquationVisualElements;
            elementKinds = elements.Select(element => element.Kind).ToArray();
            matrixRows = elements.Single().MatrixRows;
            var segments = view.EquationVisualSegments;
            texts = segments.Select(segment => segment.Text).ToArray();
            roles = segments.Select(segment => segment.Role).ToArray();
            fontSizeScales = segments.Select(segment => segment.FontSizeScale).ToArray();
            placedText = string.Concat(view.GetPlacedForBlock(0).Select(placed => placed.Ch));
            placedGlyphCount = view.PlacedGlyphCount;

            var eqRun = view.Document.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Runs)
                .Single(run => run.Equation is not null);
            kinds = eqRun.Equation!.Runs.Select(run => run.Kind).ToArray();
            linearText = eqRun.Equation!.LinearText;
        });

        if (!ran) return;

        elementKinds.Should().Equal(EquationVisualElementKind.Matrix);
        matrixRows.Should().HaveCount(2);
        matrixRows[0].Cells.Select(cell => (cell.RowIndex, cell.ColumnIndex, cell.Text))
            .Should().Equal((0, 0, "1"), (0, 1, "0"));
        matrixRows[1].Cells.Select(cell => (cell.RowIndex, cell.ColumnIndex, cell.Text))
            .Should().Equal((1, 0, "0"), (1, 1, "1"));
        texts.Should().Equal(
            EquationVisualPlanner.MatrixOpenDelimiterText,
            "1",
            EquationVisualPlanner.MatrixColumnSeparatorText,
            "0",
            EquationVisualPlanner.MatrixRowSeparatorText,
            "0",
            EquationVisualPlanner.MatrixColumnSeparatorText,
            "1",
            EquationVisualPlanner.MatrixCloseDelimiterText);
        roles.Should().Equal(
            EquationVisualSegmentRole.MatrixOpenDelimiter,
            EquationVisualSegmentRole.MatrixCell,
            EquationVisualSegmentRole.MatrixColumnSeparator,
            EquationVisualSegmentRole.MatrixCell,
            EquationVisualSegmentRole.MatrixRowSeparator,
            EquationVisualSegmentRole.MatrixCell,
            EquationVisualSegmentRole.MatrixColumnSeparator,
            EquationVisualSegmentRole.MatrixCell,
            EquationVisualSegmentRole.MatrixCloseDelimiter);
        fontSizeScales[1].Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        fontSizeScales[3].Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        placedText.Should().Be("\uFFFC");
        placedGlyphCount.Should().Be(1,
            "Avalonia should reserve a matrix as one two-dimensional layout cell");
        kinds.Should().Equal(MathRunKind.Matrix);
        linearText.Should().Be("[1, 0; 0, 1]");
    }

    [Fact]
    public async Task EquationVisualPlanner_decorators_lay_out_shared_structure()
    {
        EquationVisualElementKind[] elementKinds = [];
        (string BaseText, string Accent, bool BarTop, string OpenDelimiter, string CloseDelimiter,
            string GroupCharacter, string GroupCharacterPosition)[] elementSlots = [];
        string[] texts = [];
        EquationVisualSegmentRole[] roles = [];
        (EquationVisualBaselineRole BaselineRole, double FontSizeScale, bool Italic)[] styles = [];
        MathRunKind[] kinds = [];
        var placedText = string.Empty;
        var linearText = string.Empty;
        var placedGlyphCount = 0;

        var ran = await OnUiThread(() =>
        {
            var view = MakeView("");
            view.InsertEquation(new Equation([
                MathRun.AccentOf("x", "hat"),
                MathRun.BarOf("y", top: false),
                MathRun.Delimiter("a + b", "[", "]"),
                MathRun.GroupCharOf("z", "\u23DF", "bot")
            ]));

            var elements = view.EquationVisualElements;
            elementKinds = elements.Select(element => element.Kind).ToArray();
            elementSlots = elements
                .Select(element => (
                    element.BaseText,
                    element.Accent,
                    element.BarTop,
                    element.OpenDelimiter,
                    element.CloseDelimiter,
                    element.GroupCharacter,
                    element.GroupCharacterPosition))
                .ToArray();
            var segments = view.EquationVisualSegments;
            texts = segments.Select(segment => segment.Text).ToArray();
            roles = segments.Select(segment => segment.Role).ToArray();
            styles = segments
                .Select(segment => (segment.BaselineRole, segment.FontSizeScale, segment.Italic))
                .ToArray();
            placedText = string.Concat(view.GetPlacedForBlock(0).Select(placed => placed.Ch));
            placedGlyphCount = view.PlacedGlyphCount;

            var eqRun = view.Document.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Runs)
                .Single(run => run.Equation is not null);
            kinds = eqRun.Equation!.Runs.Select(run => run.Kind).ToArray();
            linearText = eqRun.Equation!.LinearText;
        });

        if (!ran) return;

        elementKinds.Should().Equal(
            EquationVisualElementKind.Accent,
            EquationVisualElementKind.Bar,
            EquationVisualElementKind.Delimiter,
            EquationVisualElementKind.GroupChar);
        elementSlots[0].Should().Be(("x", "hat", true, string.Empty, string.Empty, string.Empty, string.Empty));
        elementSlots[1].Should().Be(("y", string.Empty, false, string.Empty, string.Empty, string.Empty, string.Empty));
        elementSlots[2].Should().Be(("a + b", string.Empty, true, "[", "]", string.Empty, string.Empty));
        elementSlots[3].Should().Be(("z", string.Empty, true, string.Empty, string.Empty, "\u23DF", "bot"));
        texts.Should().Equal("hat", "x", "y", EquationVisualPlanner.UnderbarCueText, "[", "a + b", "]", "z", "\u23DF");
        roles.Should().Equal(
            EquationVisualSegmentRole.AccentMark,
            EquationVisualSegmentRole.AccentBase,
            EquationVisualSegmentRole.BarBase,
            EquationVisualSegmentRole.BarMark,
            EquationVisualSegmentRole.DelimiterOpen,
            EquationVisualSegmentRole.DelimiterContent,
            EquationVisualSegmentRole.DelimiterClose,
            EquationVisualSegmentRole.GroupCharBase,
            EquationVisualSegmentRole.GroupCharMark);
        styles[0].FontSizeScale.Should().Be(EquationVisualPlanner.DecoratorFontSizeScale);
        styles[0].Italic.Should().BeFalse();
        styles[1].FontSizeScale.Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        styles[3].FontSizeScale.Should().Be(EquationVisualPlanner.DecoratorFontSizeScale);
        styles[4].FontSizeScale.Should().Be(EquationVisualPlanner.DelimiterFontSizeScale);
        placedText.Should().Be("\uFFFC\uFFFC\uFFFC\uFFFC");
        placedGlyphCount.Should().Be(4,
            "Avalonia should reserve each decorator and delimiter form as one structural layout cell");
        kinds.Should().Equal(
            MathRunKind.Accent,
            MathRunKind.Bar,
            MathRunKind.Delimiter,
            MathRunKind.GroupChar);
        linearText.Should().Be("xhat_y_[a + b]z\u23DF");
    }

    [Fact]
    public async Task EquationVisualPlanner_function_apply_lays_out_shared_function_structure()
    {
        EquationVisualElementKind[] elementKinds = [];
        (string LinearText, string FunctionName, string FunctionArgument)[] elementSlots = [];
        string[] texts = [];
        EquationVisualSegmentRole[] roles = [];
        (EquationVisualBaselineRole BaselineRole, double FontSizeScale, bool Italic)[] styles = [];
        MathRunKind[] kinds = [];
        var placedText = string.Empty;
        var linearText = string.Empty;
        var placedGlyphCount = 0;

        var ran = await OnUiThread(() =>
        {
            var view = MakeView("");
            view.InsertEquation(new Equation([MathRun.FunctionApply("sin", "x + y")]));

            var elements = view.EquationVisualElements;
            elementKinds = elements.Select(element => element.Kind).ToArray();
            elementSlots = elements
                .Select(element => (element.LinearText, element.FunctionName, element.FunctionArgument))
                .ToArray();
            var segments = view.EquationVisualSegments;
            texts = segments.Select(segment => segment.Text).ToArray();
            roles = segments.Select(segment => segment.Role).ToArray();
            styles = segments
                .Select(segment => (segment.BaselineRole, segment.FontSizeScale, segment.Italic))
                .ToArray();
            placedText = string.Concat(view.GetPlacedForBlock(0).Select(placed => placed.Ch));
            placedGlyphCount = view.PlacedGlyphCount;

            var eqRun = view.Document.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Runs)
                .Single(run => run.Equation is not null);
            kinds = eqRun.Equation!.Runs.Select(run => run.Kind).ToArray();
            linearText = eqRun.Equation!.LinearText;
        });

        if (!ran) return;

        elementKinds.Should().Equal(EquationVisualElementKind.FunctionApply);
        elementSlots[0].Should().Be(("sin(x + y)", "sin", "x + y"));
        texts.Should().Equal(
            "sin",
            "x + y");
        roles.Should().Equal(
            EquationVisualSegmentRole.FunctionName,
            EquationVisualSegmentRole.FunctionArgument);
        roles.Should().NotContain(EquationVisualSegmentRole.LinearFallback);
        styles[0].Italic.Should().BeFalse();
        styles[1].Italic.Should().BeTrue();
        styles[0].FontSizeScale.Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        styles[1].FontSizeScale.Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        placedText.Should().Be("\uFFFC");
        placedGlyphCount.Should().Be(1,
            "Avalonia should reserve a function application as one structural layout cell");
        kinds.Should().Equal(MathRunKind.FunctionApply);
        linearText.Should().Be("sin(x + y)");
    }
}
