using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
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

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static FreeWRibbonHostExecutionPorts NoopCallbacks() =>
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

    private static void AddLinkedHeadingStyle(TextDocument document)
    {
        document.Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            Type = StyleType.Paragraph,
            LinkedStyleId = "Heading1Char",
        };
        document.Styles["Heading1Char"] = new DocumentStyle
        {
            Id = "Heading1Char",
            Name = "Heading 1 Char",
            Type = StyleType.Character,
            LinkedStyleId = "Heading1",
            Run = RunFormatting.Default with { Bold = true, ColorHex = "#2F5496" },
        };
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
            var id = FormattingGalleryRibbonWorkflow.StyleCommandId(descriptor.Id);
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"gallery style command '{id}' must be registered");
        }
        registry.TryGet(new RibbonCommandId("freew.style-clear"), out _).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.style"), out _).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.styles-gallery"), out _).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.new-style"), out _).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.manage-styles"), out _).Should().BeTrue();
    }

    [Fact]
    public async Task Style_combo_applies_display_name_undoably_and_rejects_unknown_values()
    {
        string? appliedStyleId = null;
        string? undoneStyleId = "not-observed";
        string? afterUnknownStyleId = "not-observed";
        string? initialValue = null;
        string? appliedValue = null;
        string? undoneValue = null;
        string? loadedValue = null;
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Heading text");
            view.MoveCaretToBlock(0, 0);
            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            registry.TryGet(new RibbonCommandId("freew.style"), out var command).Should().BeTrue();
            var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
            initialValue = stateful.GetState().Value;

            command!.Execute(RibbonCommandContext.ForSelectedValue("Heading 1"));
            appliedStyleId = ((Paragraph)doc.Blocks[0]).StyleId;
            appliedValue = stateful.GetState().Value;

            view.Undo();
            undoneStyleId = ((Paragraph)doc.Blocks[0]).StyleId;
            undoneValue = stateful.GetState().Value;

            command.Execute(RibbonCommandContext.ForSelectedValue("Missing Style"));
            command.Execute(RibbonCommandContext.Empty);
            afterUnknownStyleId = ((Paragraph)doc.Blocks[0]).StyleId;

            var loaded = TextDocument.CreateEmpty();
            loaded.Blocks.Clear();
            loaded.Blocks.Add(new Paragraph("Loaded title") { StyleId = "Title" });
            view.LoadDocument(loaded);
            view.MoveCaretToBlock(0, 0);
            loadedValue = stateful.GetState().Value;
        });
        if (!ran) return;

        initialValue.Should().Be("Normal");
        appliedStyleId.Should().Be("Heading1");
        appliedValue.Should().Be("Heading 1");
        undoneStyleId.Should().BeNull();
        undoneValue.Should().Be("Normal");
        afterUnknownStyleId.Should().BeNull();
        loadedValue.Should().Be("Title");
    }

    [Fact]
    public void Styles_group_exposes_gallery_clear_new_and_manage_controls()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var stylesGroup = definition.FindTab("home")!.Groups.First(g => g.Id == "styles");

        stylesGroup.Controls.OfType<RibbonDropdown>()
            .Any(d => d.CommandId.Value == "freew.styles-gallery")
            .Should().BeTrue("Styles group must contain the gallery dropdown");
        stylesGroup.Controls.OfType<RibbonButton>()
            .Any(b => b.CommandId.Value == "freew.style-clear")
            .Should().BeTrue("Styles group must contain the Clear Style button");
        stylesGroup.Controls.OfType<RibbonButton>()
            .Any(b => b.CommandId.Value == "freew.new-style")
            .Should().BeTrue("Styles group must contain the New Style button");
        stylesGroup.Controls.OfType<RibbonButton>()
            .Any(b => b.CommandId.Value == "freew.manage-styles")
            .Should().BeTrue("Styles group must contain the Manage Styles button");
    }

    [Fact]
    public async Task CreateParagraphStyleAndApply_AddsCustomStyle_AndSetsParagraphStyleId()
    {
        string? createdId = null;
        string? styleId = null;
        bool? bold = null;
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Custom body");
            view.MoveCaretToBlock(0, 0);
            var created = view.CreateParagraphStyleAndApply(
                "Callout",
                basedOnId: "Normal",
                RunFormatting.Default with { Bold = true },
                ParagraphFormatting.Default,
                nextStyleId: "Normal");
            createdId = created?.Id;
            styleId = ((Paragraph)doc.Blocks[0]).StyleId;
            bold = view.GetCaretFormatting().Run.Bold;
        });
        if (!ran) return;

        createdId.Should().Be("Callout");
        styleId.Should().Be("Callout");
        bold.Should().BeTrue("the newly created style resolves through paragraph formatting");
    }

    [Fact]
    public async Task ModifyParagraphStyle_UpdatesResolvedFormatting_ForStyledParagraph()
    {
        bool? bold = null;
        bool? italic = null;
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Styled body");
            view.MoveCaretToBlock(0, 0);
            var created = view.CreateParagraphStyleAndApply(
                "Callout",
                basedOnId: "Normal",
                RunFormatting.Default with { Bold = true },
                ParagraphFormatting.Default,
                nextStyleId: null);
            view.ModifyParagraphStyle(
                created!.Id,
                RunFormatting.Default with { Italic = true },
                ParagraphFormatting.Default,
                basedOnId: "Normal",
                nextStyleId: null);
            var formatting = view.GetCaretFormatting().Run;
            bold = formatting.Bold;
            italic = formatting.Italic;
            ((Paragraph)doc.Blocks[0]).StyleId.Should().Be(created.Id);
        });
        if (!ran) return;

        bold.Should().BeFalse();
        italic.Should().BeTrue("modifying the catalog should redraw text linked to that style");
    }

    [Fact]
    public async Task DeleteParagraphStyle_RemovesCustomStyle_ButRefusesBuiltInStyle()
    {
        bool deletedCustom = false;
        bool deletedBuiltIn = true;
        bool containsCustom = true;
        bool containsNormal = false;
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Styled body");
            var created = view.CreateParagraphStyleAndApply(
                "Temporary",
                basedOnId: null,
                RunFormatting.Default,
                ParagraphFormatting.Default,
                nextStyleId: null);
            deletedCustom = view.DeleteParagraphStyle(created!.Id);
            deletedBuiltIn = view.DeleteParagraphStyle("Normal");
            containsCustom = doc.Styles.ContainsKey(created.Id);
            containsNormal = doc.Styles.ContainsKey("Normal");
        });
        if (!ran) return;

        deletedCustom.Should().BeTrue();
        deletedBuiltIn.Should().BeFalse();
        containsCustom.Should().BeFalse();
        containsNormal.Should().BeTrue();
    }

    [Fact]
    public void ManageStylesRows_SortBuiltInsBeforeCustomStyles_WhenRequested()
    {
        var doc = TextDocument.CreateEmpty();
        var custom = StyleManager.CreateStyle(
            doc,
            "Zed Custom",
            basedOnId: null,
            RunFormatting.Default,
            ParagraphFormatting.Default);

        var rows = ManageStylesDialog.BuildRows(doc, StyleDialogSortOrder.ByType);

        rows.Should().Contain(row => row.Id == custom.Id && !row.IsBuiltIn);
        rows.TakeWhile(row => row.IsBuiltIn).Should().NotBeEmpty();
        rows.SkipWhile(row => row.IsBuiltIn).Should().OnlyContain(row => !row.IsBuiltIn);
    }

    [Fact]
    public void New_and_manage_style_commands_execute_host_callbacks()
    {
        var calls = new List<string>();
        var callbacks = NoopCallbacks() with
        {
            OpenNewStyleDialog = () => calls.Add("new"),
            OpenManageStylesDialog = () => calls.Add("manage"),
        };
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), callbacks);

        registry.TryGet(new RibbonCommandId("freew.new-style"), out var newStyle).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.manage-styles"), out var manageStyles).Should().BeTrue();
        newStyle!.Execute(RibbonCommandContext.Empty);
        manageStyles!.Execute(RibbonCommandContext.Empty);

        calls.Should().Equal("new", "manage");
    }

    [Fact]
    public void Every_styles_gallery_menu_item_resolves_in_the_registry()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
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
    public async Task ApplyNamedStyle_LinkedParagraphStyle_UsesCharacterSideForSelectedText()
    {
        string? paragraphStyleId = "sentinel";
        bool selectedBold = false;
        bool remainderBold = true;
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Linked text");
            AddLinkedHeadingStyle(doc);
            view.SetSelectionRangePublic(0, 0, 0, 6);

            view.ApplyNamedStyle("Heading1");

            var paragraph = (Paragraph)doc.Blocks[0];
            paragraphStyleId = paragraph.StyleId;
            selectedBold = paragraph.Runs.Where(run => run.Text == "Linked").All(run => run.Formatting.Bold);
            remainderBold = paragraph.Runs.Where(run => run.Text.Contains("text", StringComparison.Ordinal))
                .Any(run => run.Formatting.Bold);
        });
        if (!ran) return;

        paragraphStyleId.Should().BeNull();
        selectedBold.Should().BeTrue();
        remainderBold.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyNamedStyle_LinkedParagraphStyle_CollapsedCaretUsesParagraphSideAndUndo()
    {
        string? afterApply = null;
        string? afterUndo = "sentinel";
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Linked text");
            AddLinkedHeadingStyle(doc);
            view.MoveCaretToBlockForTest(0, 3);

            view.ApplyNamedStyle("Heading1");
            afterApply = ((Paragraph)doc.Blocks[0]).StyleId;
            view.Undo();
            afterUndo = ((Paragraph)doc.Blocks[0]).StyleId;
        });
        if (!ran) return;

        afterApply.Should().Be("Heading1");
        afterUndo.Should().BeNull();
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

    // ── ApplyNamedStyle – character style, multi-paragraph (DC1) ───────────────────────────────

    /// <summary>
    /// DC1: A character style (Strong) applied to a selection spanning 3 paragraphs must bold ALL
    /// selected text in all 3 paragraphs, not just stage a pending format for the caret.
    /// </summary>
    [Fact]
    public async Task ApplyNamedStyle_Strong_MultiParagraph_BoldsAllSelectedText()
    {
        bool p0Bold = false, p1Bold = false, p2Bold = false;
        string? p0StyleId = "sentinel", p1StyleId = "sentinel", p2StyleId = "sentinel";
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("First"));
            doc.Blocks.Add(new Paragraph("Middle"));
            doc.Blocks.Add(new Paragraph("Third"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            // Select from the start of block 0 to the end of block 2 (3-para span).
            view.SetSelectionRangePublic(0, 0, 2, 5);
            view.ApplyNamedStyle("Strong");
            var b0 = (Paragraph)doc.Blocks[0];
            var b1 = (Paragraph)doc.Blocks[1];
            var b2 = (Paragraph)doc.Blocks[2];
            p0StyleId  = b0.StyleId;
            p1StyleId  = b1.StyleId;
            p2StyleId  = b2.StyleId;
            p0Bold = b0.Runs.Count > 0 && b0.Runs.All(rn => rn.Formatting.Bold);
            p1Bold = b1.Runs.Count > 0 && b1.Runs.All(rn => rn.Formatting.Bold);
            p2Bold = b2.Runs.Count > 0 && b2.Runs.All(rn => rn.Formatting.Bold);
        });
        if (!ran) return;
        // Character style must not change paragraph StyleId on any block.
        p0StyleId.Should().BeNull("character style must not set paragraph StyleId on block 0");
        p1StyleId.Should().BeNull("character style must not set paragraph StyleId on block 1");
        p2StyleId.Should().BeNull("character style must not set paragraph StyleId on block 2");
        // All three paragraphs must be bolded.
        p0Bold.Should().BeTrue("Strong must bold the selected runs in paragraph 0 (DC1)");
        p1Bold.Should().BeTrue("Strong must bold the selected runs in paragraph 1 (DC1)");
        p2Bold.Should().BeTrue("Strong must bold the selected runs in paragraph 2 (DC1)");
    }

    /// <summary>
    /// DC1: Single-paragraph character-style apply (existing behaviour) must remain unchanged.
    /// </summary>
    [Fact]
    public async Task ApplyNamedStyle_Strong_SingleParagraph_StillWorks()
    {
        bool allBold = false;
        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Hello");
            view.SetSelectionRangePublic(0, 0, 0, 5);
            view.ApplyNamedStyle("Strong");
            allBold = ((Paragraph)doc.Blocks[0]).Runs.Count > 0
                   && ((Paragraph)doc.Blocks[0]).Runs.All(rn => rn.Formatting.Bold);
        });
        if (!ran) return;
        allBold.Should().BeTrue("single-block Strong apply must still bold the selection");
    }

    // ---- New Style / Manage Styles backing -----------------------------------------------

    [Fact]
    public async Task CreateParagraphStyleAndApply_AddsCustomStyle_AppliesIt_AndUndoRevertsBoth()
    {
        string? createdId = null;
        string? appliedStyleId = null;
        bool presentBeforeUndo = false;
        bool presentAfterUndo = true;
        string? styleAfterUndo = "sentinel";

        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Custom heading");
            view.MoveCaretToBlock(0, 0);

            var created = view.CreateParagraphStyleAndApply(
                "Callout",
                basedOnId: "Normal",
                RunFormatting.Default with { Bold = true, FontSizePt = 16 },
                ParagraphFormatting.Default,
                nextStyleId: "Normal");

            createdId = created?.Id;
            appliedStyleId = ((Paragraph)doc.Blocks[0]).StyleId;
            presentBeforeUndo = createdId is not null && doc.Styles.ContainsKey(createdId);

            view.Undo();
            presentAfterUndo = createdId is not null && doc.Styles.ContainsKey(createdId);
            styleAfterUndo = ((Paragraph)doc.Blocks[0]).StyleId;
        });
        if (!ran) return;

        createdId.Should().Be("Callout");
        appliedStyleId.Should().Be("Callout");
        presentBeforeUndo.Should().BeTrue();
        presentAfterUndo.Should().BeFalse("undo must remove the created catalog entry");
        styleAfterUndo.Should().BeNull("undo must also revert the immediate style apply");
    }

    [Fact]
    public async Task ManageStyleHelpers_ModifyAndDeleteCustomStyles_WithUndo()
    {
        bool modifiedBold = false;
        bool boldAfterUndo = true;
        bool deleted = false;
        bool restoredAfterUndo = false;

        var ran = await OnUiThread(() =>
        {
            var (view, doc) = MakeBodyDoc("Managed");
            var custom = StyleManager.CreateStyle(
                doc,
                "Callout",
                null,
                RunFormatting.Default,
                ParagraphFormatting.Default);

            view.ModifyParagraphStyle(
                custom.Id,
                RunFormatting.Default with { Bold = true },
                ParagraphFormatting.Default with { Alignment = TextAlignment.Center },
                basedOnId: "Normal",
                nextStyleId: "Normal").Should().NotBeNull();
            modifiedBold = doc.Styles[custom.Id].Run.Bold;

            view.Undo();
            boldAfterUndo = doc.Styles[custom.Id].Run.Bold;

            view.DeleteParagraphStyle(custom.Id).Should().BeTrue();
            deleted = !doc.Styles.ContainsKey(custom.Id);

            view.Undo();
            restoredAfterUndo = doc.Styles.ContainsKey(custom.Id);
        });
        if (!ran) return;

        modifiedBold.Should().BeTrue("Modify Style must mutate the catalog through StyleManager");
        boldAfterUndo.Should().BeFalse("undo must restore the previous style definition");
        deleted.Should().BeTrue("Delete Style must remove custom styles");
        restoredAfterUndo.Should().BeTrue("undo must restore deleted custom styles");
    }

    /// <summary>
    /// DC1: A single Undo must revert the character-style application across all 3 paragraphs
    /// (the multi-paragraph apply is wrapped in a single undo group).
    /// </summary>
    [Fact]
    public async Task ApplyNamedStyle_Strong_MultiParagraph_SingleUndoRevertsAll()
    {
        int boldAfterUndo = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("First"));
            doc.Blocks.Add(new Paragraph("Middle"));
            doc.Blocks.Add(new Paragraph("Third"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            view.SetSelectionRangePublic(0, 0, 2, 5);
            view.ApplyNamedStyle("Strong");
            view.Undo();
            // After one Undo, none of the paragraphs should have bold runs.
            boldAfterUndo = doc.Blocks.OfType<Paragraph>()
                               .SelectMany(p => p.Runs)
                               .Count(rn => rn.Formatting.Bold);
        });
        if (!ran) return;
        boldAfterUndo.Should().Be(0,
            "a single Undo must revert the Strong character style from all 3 paragraphs (undo group)");
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
