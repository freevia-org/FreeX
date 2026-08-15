using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-INSERT: tests for the deepened Insert tab — Table (sized presets), Page Break, Picture, Shape,
/// Text Box, Symbol, and Header/Footer. Covers the DocumentView insert methods (model mutation + undo)
/// and that every new ribbon command id resolves in the registry. Pure-model — no headless backend.
/// </summary>
public sealed class InsertTabDepthTests
{
    // A 1×1 transparent PNG (valid magic bytes so DetectFormat picks PNG).
    private static byte[] SmallPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    private static FreeWRibbonHostExecutionPorts NoopCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { }, OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
            OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { }, OpenWordCountDialog: () => { },
            InsertPicture: () => { }, ApplyZoom: (_, _) => { });

    private static DocumentView ViewWith(string text = "Body paragraph")
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        var view = new DocumentView();
        view.LoadDocument(doc);
        return view;
    }

    private static void Exec(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var cmd).Should().BeTrue($"command '{id}' must be registered");
        cmd!.Execute(RibbonCommandContext.Empty);
    }

    // ── Page Break ────────────────────────────────────────────────────────────

    [Fact]
    public void InsertPageBreak_adds_pagebreak_paragraph_and_undo_reverts()
    {
        var view = ViewWith();
        var before = view.Document.Blocks.Count;

        view.InsertPageBreak();

        view.Document.Blocks.Count.Should().Be(before + 1);
        view.Document.Blocks.OfType<Paragraph>()
            .Any(p => p.Formatting.PageBreakBefore)
            .Should().BeTrue("a page-break paragraph must be inserted");

        view.Undo();
        view.Document.Blocks.Count.Should().Be(before, "undo removes the page break");
    }

    [Fact]
    public void Blank_page_command_inserts_two_page_breaks_as_one_undo_step()
    {
        var view = ViewWith();
        var before = view.Document.Blocks.Count;
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Exec(registry, "freew.blank-page");

        view.Document.Blocks.Count.Should().Be(before + 2);
        view.Document.Blocks
            .Skip(1)
            .Take(2)
            .OfType<Paragraph>()
            .All(p => p.Formatting.PageBreakBefore)
            .Should().BeTrue("a blank page is represented by the shared two-break model operation");

        view.Undo();
        view.Document.Blocks.Count.Should().Be(before, "blank-page undo removes both inserted page breaks");
    }

    [Fact]
    public void Horizontal_rule_command_inserts_bottom_border_paragraph()
    {
        var view = ViewWith();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Exec(registry, "freew.horizontal-rule");

        var rule = view.Document.Blocks.OfType<Paragraph>().Single(p => p.Formatting.Border is not null);
        rule.Formatting.Border!.BottomOnly.Should().BeTrue("horizontal rule uses the shared bottom-border paragraph");
        rule.PlainText.Should().BeEmpty();
    }

    // ── Table size presets ──────────────────────────────────────────────────────

    [Fact]
    public void InsertTable_preset_adds_table_block_with_requested_dimensions()
    {
        var view = ViewWith();
        var before = view.Document.Blocks.Count;

        view.InsertTable(2, 4);

        var table = view.Document.Blocks.OfType<Table>().Single();
        table.Rows.Count.Should().Be(2);
        table.Rows[0].Cells.Count.Should().Be(4);

        view.Undo();
        view.Document.Blocks.Count.Should().Be(before);
        view.Document.Blocks.OfType<Table>().Should().BeEmpty("undo removes the table");
    }

    // ── Picture ─────────────────────────────────────────────────────────────────

    [Fact]
    public void InsertInlineImage_appends_image_run_and_undo_reverts()
    {
        var view = ViewWith();

        view.InsertInlineImage(SmallPng(), widthPt: 72, heightPt: 54);

        var para = (Paragraph)view.Document.Blocks[0];
        var imageRun = para.Runs.SingleOrDefault(r => r.Image is not null);
        imageRun.Should().NotBeNull("an inline-image run must be appended to the caret paragraph");
        imageRun!.Image!.WidthPt.Should().Be(72);
        imageRun.Image.Format.Should().Be(ImageFormat.Png, "PNG magic bytes must be detected");

        view.Undo();
        ((Paragraph)view.Document.Blocks[0]).Runs.Any(r => r.Image is not null)
            .Should().BeFalse("undo removes the image run");
    }

    // ── Shape ───────────────────────────────────────────────────────────────────

    [Fact]
    public void InsertShape_appends_floating_shape_run_and_undo_reverts()
    {
        var view = ViewWith();

        view.InsertShape();

        var para = (Paragraph)view.Document.Blocks[0];
        var shapeRun = para.Runs.SingleOrDefault(r => r.Shape is not null);
        shapeRun.Should().NotBeNull("a shape run must be appended");
        shapeRun!.Shape!.Kind.Should().Be(ShapeKind.Rectangle);
        shapeRun.Shape.IsFloating.Should().BeTrue("Insert Shape produces a floating object");

        view.Undo();
        ((Paragraph)view.Document.Blocks[0]).Runs.Any(r => r.Shape is not null)
            .Should().BeFalse("undo removes the shape run");
    }

    [Fact]
    public void InsertTextBox_appends_floating_textbox_shape()
    {
        var view = ViewWith();

        view.InsertTextBox();

        var para = (Paragraph)view.Document.Blocks[0];
        var shapeRun = para.Runs.SingleOrDefault(r => r.Shape is not null);
        shapeRun.Should().NotBeNull();
        shapeRun!.Shape!.Kind.Should().Be(ShapeKind.TextBox, "a text box is a TextBox-kind shape");
        shapeRun.Shape.HasText.Should().BeTrue("the text box carries placeholder text");
        shapeRun.Shape.IsFloating.Should().BeTrue();
    }

    // ── Symbol ──────────────────────────────────────────────────────────────────

    [Fact]
    public void InsertSymbol_inserts_glyph_at_caret_as_text()
    {
        var view = ViewWith("AB");

        view.InsertSymbol("€");

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Contain("€",
            "the chosen symbol must be inserted as ordinary text");
    }

    // ── Header / Footer ─────────────────────────────────────────────────────────

    [Fact]
    public void EnsureHeader_creates_header_region_and_undo_reverts()
    {
        var view = ViewWith();
        (view.Document.Header is null || view.Document.Header.IsEmpty).Should().BeTrue("no header yet");

        view.EnsureHeader();

        view.Document.Header.Should().NotBeNull("Insert > Header creates the region");
        view.Document.Header!.Paragraphs.Should().NotBeEmpty();

        view.Undo();
        (view.Document.Header is null || view.Document.Header.IsEmpty)
            .Should().BeTrue("undo removes the created header region");
    }

    [Fact]
    public void EnsureFooter_creates_footer_region()
    {
        var view = ViewWith();
        view.EnsureFooter();
        view.Document.Footer.Should().NotBeNull("Insert > Footer creates the region");
    }

    // ── Registry wiring ─────────────────────────────────────────────────────────

    [Fact]
    public void Registry_resolves_all_insert_tab_commands()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());

        var expected = new[]
        {
            "freew.insert-table", "freew.table", "freew.table-2x2", "freew.table-3x3",
            "freew.table-4x4", "freew.table-5x2",
            "freew.blank-page", "freew.page-break", "freew.horizontal-rule",
            "freew.picture", "freew.shapes", "freew.shape-rectangle", "freew.shape-rounded",
            "freew.shape-ellipse", "freew.shape-textbox", "freew.textbox-simple",
            "freew.textbox-sidebar", "freew.textbox-quote", "freew.shape", "freew.text-box",
            "freew.symbol", "freew.header", "freew.footer", "freew.datetime",
            "freew.symbol.euro", "freew.symbol.emdash", "freew.symbol.arrow-right",
        };

        foreach (var id in expected)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Insert-tab command '{id}' must be registered");
    }

    [Fact]
    public void Insert_tab_definition_exposes_new_groups_and_controls()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var insert = definition.FindTab("insert");
        insert.Should().NotBeNull();

        var groupHeaders = insert!.Groups.Select(g => g.Header).ToList();
        groupHeaders.Should().Contain(new[] { "Pages", "Tables", "Illustrations", "Header & Footer", "Symbols" });
    }

    [Fact]
    public void Symbol_palette_subcommand_inserts_its_glyph()
    {
        var view = ViewWith("X");

        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.symbol.copyright"), out var cmd)
            .Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Contain("©");
    }

    [Fact]
    public void Date_time_command_inserts_date_field_run()
    {
        var view = ViewWith("");
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Exec(registry, "freew.datetime");

        var paragraph = (Paragraph)view.Document.Blocks[0];
        paragraph.Runs.Should().ContainSingle(run => run.FieldKind == RunFieldKind.Date);
        paragraph.PlainText.Should().NotBeEmpty("the cached date text should render immediately");
    }

    [Fact]
    public void Date_time_command_prefers_the_host_dialog_when_available()
    {
        var view = ViewWith("");
        var invoked = 0;
        var registry = FreeWAvaloniaRibbonCommands.Build(
            view,
            NoopCallbacks() with { OpenDateTimeDialog = () => invoked++ });

        Exec(registry, "freew.datetime");

        invoked.Should().Be(1);
        ((Paragraph)view.Document.Blocks[0]).Runs.Should().BeEmpty();
    }
}
