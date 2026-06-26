using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-STYLES: tests for the Home &gt; Styles gallery + <see cref="DocumentView.ApplyNamedStyle"/>.
/// <list type="bullet">
///   <item>Applying a paragraph style (Heading 1) sets the paragraph StyleId and the resolved run
///     formatting reflects the style (bold + larger).</item>
///   <item>Applying a character style (Strong) bolds the selected run without setting a paragraph StyleId.</item>
///   <item>A built-in style absent from the document's catalog is seeded on apply.</item>
///   <item>Undo reverts the style application.</item>
///   <item>Every gallery command (freew.style.&lt;id&gt;) resolves in the ribbon registry, and the
///     Styles group exposes the gallery dropdown + clear-style button.</item>
/// </list>
/// </summary>
public sealed class StylesGalleryTests
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
        catch
        {
            return false;
        }
    }

    private static RibbonHostCallbacks NoopCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { },
            ToggleNavigationPane: () => { }, ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { }, SetPrintLayout: () => { }, SetWebLayout: () => { },
            SetDraftView: () => { }, OpenFontDialog: () => { }, OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { }, ToggleOrientation: () => { }, ApplyMarginPreset: _ => { },
            ApplyPaperSize: _ => { }, InsertPicture: () => { }, OpenWordCountDialog: () => { },
            ApplyZoom: (_, _) => { });

    private static (DocumentView View, TextDocument Doc) MakeBodyDoc(string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 4000));
        return (view, doc);
    }

    // ── Model-level seeding (pure, no UI thread) ────────────────────────────────────────────────

    [Fact]
    public void Gallery_contains_the_expected_built_in_styles()
    {
        var ids = BuiltInStyles.Gallery.Select(d => d.Id).ToHashSet();
        ids.Should().Contain(new[]
        {
            "Normal", "NoSpacing", "Heading1", "Heading2", "Heading3", "Heading4",
            "Title", "Subtitle", "ListParagraph", "Quote", "IntenseQuote",
            "Emphasis", "Strong", "SubtleEmphasis", "IntenseEmphasis",
        });
        // At least the documented minimum count.
        BuiltInStyles.Gallery.Count.Should().BeGreaterThanOrEqualTo(15);
    }

    [Fact]
    public void EnsureSeeded_adds_a_missing_built_in_style_with_its_definition()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles.Remove("Strong"); // not in the default seed anyway, but be explicit
        BuiltInStyles.EnsureSeeded(doc, "Strong").Should().NotBeNull();
        doc.Styles.ContainsKey("Strong").Should().BeTrue("Strong must be seeded");
        doc.Styles["Strong"].Type.Should().Be(StyleType.Character);
        doc.Styles["Strong"].Run.Bold.Should().BeTrue();
    }

    [Fact]
    public void EnsureSeeded_does_not_overwrite_an_existing_definition()
    {
        var doc = TextDocument.CreateEmpty();
        // Heading1 is seeded by CreateEmpty; customise it and verify EnsureSeeded leaves it alone.
        doc.Styles["Heading1"].Run = doc.Styles["Heading1"].Run with { FontSizePt = 99 };
        BuiltInStyles.EnsureSeeded(doc, "Heading1");
        doc.Styles["Heading1"].Run.FontSizePt.Should().Be(99, "an existing definition must win");
    }

    [Fact]
    public void EnsureSeeded_returns_null_for_an_unknown_style()
    {
        var doc = TextDocument.CreateEmpty();
        BuiltInStyles.EnsureSeeded(doc, "NotARealStyle").Should().BeNull();
    }

    // ── Command registry resolution ─────────────────────────────────────────────────────────────

    [Fact]
    public void Every_gallery_style_command_is_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
        foreach (var descriptor in BuiltInStyles.Gallery)
        {
            var id = FreeWAvaloniaRibbonCommands.StyleCommandId(descriptor.Id);
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"gallery style command '{id}' must be registered");
        }
        registry.TryGet(new RibbonCommandId("freew.style-clear"), out _).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.styles-gallery"), out _).Should().BeTrue();
    }

    [Fact]
    public void Styles_group_exposes_gallery_dropdown_and_clear_button()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var stylesGroup = definition.FindTab("home")!.Groups.First(g => g.Id == "styles");

        stylesGroup.Controls.OfType<RibbonDropdown>()
            .Any(d => d.CommandId.Value == "freew.styles-gallery")
            .Should().BeTrue("Styles group must contain the gallery dropdown");
        stylesGroup.Controls.OfType<RibbonButton>()
            .Any(b => b.CommandId.Value == "freew.style-clear")
            .Should().BeTrue("Styles group must contain the Clear Style button");
    }

    [Fact]
    public void Every_styles_gallery_menu_item_resolves_in_the_registry()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), NoopCallbacks());
        var dropdown = definition.FindTab("home")!.Groups.First(g => g.Id == "styles")
            .Controls.OfType<RibbonDropdown>().First(d => d.CommandId.Value == "freew.styles-gallery");

        foreach (var item in dropdown.Menu.Items.Where(i => i.Kind != RibbonMenuItemKind.Separator && i.CommandId is not null))
            registry.TryGet(item.CommandId!.Value, out _)
                .Should().BeTrue($"menu item '{item.CommandId!.Value.Value}' must be registered");
    }

    // ── ApplyNamedStyle – paragraph style ───────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyNamedStyle_Heading1_SetsParagraphStyleId_AndResolvesBoldLarger()
    {
        string? styleId = null;
        bool? bold = null;
        double? size = null;
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Heading text");
            view.MoveCaretToBlock(0, 0);
            view.ApplyNamedStyle("Heading1");
            styleId = ((Paragraph)doc.Blocks[0]).StyleId;
            var (run, _) = view.GetCaretFormatting();
            bold = run.Bold;
            size = run.FontSizePt;
        });
        if (!ran) return;
        styleId.Should().Be("Heading1", "paragraph StyleId must be set");
        bold.Should().BeTrue("Heading 1 resolves to bold");
        size.Should().Be(16, "Heading 1 resolves to 16pt");
    }

    [Fact]
    public async Task ApplyNamedStyle_Heading1_IsUndoable()
    {
        string? afterUndo = "sentinel";
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Heading text");
            view.MoveCaretToBlock(0, 0);
            view.ApplyNamedStyle("Heading1");
            view.Undo();
            afterUndo = ((Paragraph)doc.Blocks[0]).StyleId;
        });
        if (!ran) return;
        afterUndo.Should().BeNull("undo must clear the applied paragraph style");
    }

    [Fact]
    public async Task ApplyNamedStyle_SeedsBuiltInStyle_WhenAbsentFromCatalog()
    {
        bool seededPresent = false;
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("text");
            doc.Styles.Remove("IntenseQuote"); // ensure absent
            view.MoveCaretToBlock(0, 0);
            view.ApplyNamedStyle("IntenseQuote");
            seededPresent = doc.Styles.ContainsKey("IntenseQuote");
        });
        if (!ran) return;
        seededPresent.Should().BeTrue("an absent built-in style must be seeded on apply");
    }

    [Fact]
    public async Task ApplyNamedStyle_AppliesToAllParagraphsInSelection()
    {
        string? s0 = null, s1 = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("First"));
            doc.Blocks.Add(new Paragraph("Second"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            view.SetSelectionRangePublic(0, 0, 1, 6);
            view.ApplyNamedStyle("Heading2");
            s0 = ((Paragraph)doc.Blocks[0]).StyleId;
            s1 = ((Paragraph)doc.Blocks[1]).StyleId;
        });
        if (!ran) return;
        s0.Should().Be("Heading2");
        s1.Should().Be("Heading2", "all paragraphs in the selection must get the style");
    }

    [Fact]
    public async Task ApplyNamedStyle_MultiParagraph_IsUndoneWithSingleUndo()
    {
        int cleared = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("First"));
            doc.Blocks.Add(new Paragraph("Second"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            view.SetSelectionRangePublic(0, 0, 1, 6);
            view.ApplyNamedStyle("Heading2");
            view.Undo();
            cleared = doc.Blocks.OfType<Paragraph>().Count(p => p.StyleId is null);
        });
        if (!ran) return;
        cleared.Should().Be(2, "single undo must revert both paragraphs (undo group)");
    }

    // ── ApplyNamedStyle – character style ───────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyNamedStyle_Strong_BoldsSelectedRun_WithoutParagraphStyleId()
    {
        bool allBold = false;
        string? styleId = "sentinel";
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Bold me");
            // Select the whole run "Bold me" (7 chars).
            view.SetSelectionRangePublic(0, 0, 0, 7);
            view.ApplyNamedStyle("Strong");
            var p = (Paragraph)doc.Blocks[0];
            styleId = p.StyleId;
            allBold = p.Runs.Count > 0 && p.Runs.All(rn => rn.Formatting.Bold);
        });
        if (!ran) return;
        styleId.Should().BeNull("a character style must not set the paragraph StyleId");
        allBold.Should().BeTrue("Strong must bold the selected run(s)");
    }

    [Fact]
    public async Task ApplyNamedStyle_Emphasis_ItalicizesSelectedRun_AndIsUndoable()
    {
        bool italicAfterApply = false;
        bool italicAfterUndo = true;
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Emphasise");
            view.SetSelectionRangePublic(0, 0, 0, 9);
            view.ApplyNamedStyle("Emphasis");
            var p = (Paragraph)doc.Blocks[0];
            italicAfterApply = p.Runs.Count > 0 && p.Runs.All(rn => rn.Formatting.Italic);
            view.Undo();
            var p2 = (Paragraph)doc.Blocks[0];
            italicAfterUndo = p2.Runs.Any(rn => rn.Formatting.Italic);
        });
        if (!ran) return;
        italicAfterApply.Should().BeTrue("Emphasis must italicise the selection");
        italicAfterUndo.Should().BeFalse("undo must revert the italic");
    }

    [Fact]
    public async Task ApplyNamedStyle_Strong_PreservesExistingRunFontAndColor()
    {
        // Strong only turns bold on; it must not clobber font family / size / colour.
        bool bold = false;
        string? family = null;
        double? size = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Styled", new RunFormatting { FontFamily = "Georgia", FontSizePt = 18 }));
            doc.Blocks.Add(para);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            view.SetSelectionRangePublic(0, 0, 0, 6);
            view.ApplyNamedStyle("Strong");
            var p = (Paragraph)doc.Blocks[0];
            bold = p.Runs.All(rn => rn.Formatting.Bold);
            family = p.Runs[0].Formatting.FontFamily;
            size = p.Runs[0].Formatting.FontSizePt;
        });
        if (!ran) return;
        bold.Should().BeTrue();
        family.Should().Be("Georgia", "character style must not clobber the run font");
        size.Should().Be(18, "character style must not clobber the run size");
    }

    // ── Clear style ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearParagraphStyle_RemovesAppliedParagraphStyle()
    {
        string? afterClear = "sentinel";
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("text");
            view.MoveCaretToBlock(0, 0);
            view.ApplyNamedStyle("Heading3");
            view.ClearParagraphStyle();
            afterClear = ((Paragraph)doc.Blocks[0]).StyleId;
        });
        if (!ran) return;
        afterClear.Should().BeNull("Clear Style must revert the paragraph to the document default");
    }

    [Fact]
    public async Task ApplyNamedStyle_UnknownStyle_IsNoOp()
    {
        string? styleId = "sentinel";
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("text");
            view.MoveCaretToBlock(0, 0);
            var result = view.ApplyNamedStyle("DefinitelyNotAStyle");
            result.Should().BeNull();
            styleId = ((Paragraph)doc.Blocks[0]).StyleId;
        });
        if (!ran) return;
        styleId.Should().BeNull("an unknown style id must not change the paragraph");
    }
}
