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

    private static RibbonHostCallbacks NoopCallbacks() =>
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
            "freew.page-break", "freew.picture", "freew.shape", "freew.text-box",
            "freew.symbol", "freew.header", "freew.footer",
            "freew.symbol.euro", "freew.symbol.emdash", "freew.symbol.arrow-right",
        };

        foreach (var id in expected)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Insert-tab command '{id}' must be registered");
    }

    [Fact]
    public void Insert_tab_definition_exposes_new_groups_and_controls()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var insert = definition.FindTab("insert");
        insert.Should().NotBeNull();

        var groupHeaders = insert!.Groups.Select(g => g.Header).ToList();
        groupHeaders.Should().Contain(new[] { "Pages", "Tables", "Illustrations", "Header & Footer", "Symbols" });
    }

    [Fact]
    public void Symbol_palette_subcommand_inserts_its_glyph()
    {
        var view = ViewWith("X");

        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.symbol.copyright"), out var cmd)
            .Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Contain("©");
    }
}
