using System.IO;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Headless tests for the Page Setup dialog apply logic and the Layout ribbon commands (AV-PAGE).
///
/// The dialog itself is a modal Avalonia window and cannot be headlessly clicked; its static
/// <see cref="PageSetupDialog.ApplyResult"/> method is fully testable without a window.
///
/// Quick-command wiring (orientation toggle, margin presets, paper sizes) is verified by
/// invoking the DocumentView-level logic directly.
/// </summary>
public sealed class PageSetupDialogTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TextDocument MakeDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Test"));
        return doc;
    }

    private static FreeWRibbonHostExecutionPorts NoopCallbacks() =>
        new(
            Open: () => { },
            Save: () => { },
            Cut: () => { },
            Copy: () => { },
            Paste: () => { },
            Backstage: () => { },
            NewDocument: () => { },
            ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { },
            ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { },
            SetWebLayout: () => { },
            SetDraftView: () => { },
            OpenFontDialog: () => { },
            OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { },
            ApplyMarginPreset: _ => { },
            ApplyPaperSize: _ => { },
            InsertPicture: () => { },
            OpenWordCountDialog: () => { },
            ApplyZoom: (_, _) => { });

    private static PageSetupDialogResult Result(
        double MarginTopPt,
        double MarginBottomPt,
        double MarginLeftPt,
        double MarginRightPt,
        bool Landscape,
        double WidthPt,
        double HeightPt,
        bool GutterAtTop = false) =>
        new(
            MarginTopPt,
            MarginBottomPt,
            MarginLeftPt,
            MarginRightPt,
            GutterPt: 0,
            Landscape,
            MirrorMargins: false,
            WidthPt,
            HeightPt,
            SectionStart: SectionBreakKind.NextPage,
            DifferentFirstPage: false,
            DifferentOddEvenPages: false,
            HeaderDistancePt: 36,
            FooterDistancePt: 36,
            VerticalAlignment: PageVerticalAlignment.Top,
            GutterAtTop: GutterAtTop);

    // ── Command registry ──────────────────────────────────────────────────────

    [Fact]
    public void Page_setup_dialog_command_is_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.page-setup"), out _)
            .Should().BeTrue("freew.page-setup must be the shared registered command");
        registry.TryGet(new RibbonCommandId("freew.page-setup-dialog"), out _)
            .Should().BeTrue("freew.page-setup-dialog must remain a compatibility alias");
        registry.TryGet(new RibbonCommandId("freew.custom-margins"), out _)
            .Should().BeTrue("freew.custom-margins must route to the shared page setup dialog");
        registry.TryGet(new RibbonCommandId("freew.more-paper-sizes"), out _)
            .Should().BeTrue("freew.more-paper-sizes must route to the shared page setup dialog");
    }

    [Fact]
    public void Page_orientation_command_is_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.orientation"), out _)
            .Should().BeTrue("freew.orientation must be the shared registered command");
        registry.TryGet(new RibbonCommandId("freew.page-orientation"), out _)
            .Should().BeTrue("freew.page-orientation must remain a compatibility alias");
    }

    [Fact]
    public void Page_margin_normal_command_is_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.page-margins-normal"), out _)
            .Should().BeTrue("freew.page-margins-normal must be registered");
    }

    [Fact]
    public void Page_margin_narrow_command_is_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.page-margins-narrow"), out _)
            .Should().BeTrue("freew.page-margins-narrow must be registered");
    }

    [Fact]
    public void Page_margin_wide_command_is_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.page-margins-wide"), out _)
            .Should().BeTrue("freew.page-margins-wide must be registered");
    }

    [Fact]
    public void Page_size_letter_command_is_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.page-size-letter"), out _)
            .Should().BeTrue("freew.page-size-letter must be registered");
    }

    [Fact]
    public void Page_size_a4_command_is_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.page-size-a4"), out _)
            .Should().BeTrue("freew.page-size-a4 must be registered");
    }

    [Fact]
    public void Shared_layout_page_setup_registry_commands_are_registered()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks());

        foreach (var commandId in new[]
        {
            "freew.margins",
            "freew.orientation",
            "freew.size",
            "freew.columns",
            "freew.columns-one",
            "freew.columns-two",
            "freew.columns-three",
            "freew.columns-left",
            "freew.columns-right",
            "freew.breaks",
            "freew.column-break",
            "freew.section-break-next-page",
            "freew.section-break-continuous",
            "freew.section-break-even-page",
            "freew.section-break-odd-page",
            "freew.page-setup",
            "freew.custom-margins",
            "freew.more-paper-sizes",
        })
        {
            registry.TryGet(new RibbonCommandId(commandId), out _)
                .Should().BeTrue($"{commandId} must be registered for the Avalonia Layout/Page Setup surface");
        }
    }

    [Fact]
    public void Layout_commands_publish_live_values_checked_modes_and_protection_enablement()
    {
        var document = MakeDoc();
        ((Paragraph)document.Blocks[0]).Formatting = ParagraphFormatting.Default with
        {
            IndentLeftPt = 18,
            IndentRightPt = 24,
            SpaceBeforePt = 6,
            SpaceAfterPt = 12,
        };
        document.Page.ColumnCount = 3;
        document.Page.LineNumberMode = LineNumberMode.RestartEachPage;
        document.Page.AutoHyphenation = true;

        var editor = new DocumentView();
        editor.LoadDocument(document);
        var registry = FreeWAvaloniaRibbonCommands.Build(editor, NoopCallbacks());

        Stateful(registry, "freew.indent-left").GetState().Value.Should().Be("18");
        Stateful(registry, "freew.indent-right").GetState().Value.Should().Be("24");
        Stateful(registry, "freew.space-before").GetState().Value.Should().Be("6");
        Stateful(registry, "freew.space-after").GetState().Value.Should().Be("12");
        Stateful(registry, "freew.columns-three").GetState().IsChecked.Should().BeTrue();
        Stateful(registry, "freew.line-numbers-restart-page").GetState().IsChecked.Should().BeTrue();
        Stateful(registry, "freew.hyphenation-auto").GetState().IsChecked.Should().BeTrue();

        editor.SetProtection(ProtectionMode.ReadOnly);
        Stateful(registry, "freew.orientation").GetState().IsEnabled.Should().BeFalse();
        Stateful(registry, "freew.page-margins-normal").GetState().IsEnabled.Should().BeFalse();
        Stateful(registry, "freew.page-size-a4").GetState().IsEnabled.Should().BeFalse();
        Stateful(registry, "freew.columns-three").GetState().IsEnabled.Should().BeFalse();
        Stateful(registry, "freew.line-numbers-restart-page").GetState().IsEnabled.Should().BeFalse();
        Stateful(registry, "freew.hyphenation-auto").GetState().IsEnabled.Should().BeFalse();
    }

    private static IRibbonStatefulCommand Stateful(RibbonCommandRegistry registry, string commandId)
    {
        registry.TryGet(new RibbonCommandId(commandId), out var command).Should().BeTrue();
        return command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
    }

    // ── Ribbon definition ─────────────────────────────────────────────────────

    [Fact]
    public void Shared_and_alias_page_setup_commands_invoke_callbacks()
    {
        var opened = 0;
        var orientationToggles = 0;
        var marginPresets = new List<string>();
        var paperSizes = new List<string>();
        var callbacks = NoopCallbacks() with
        {
            OpenPageSetupDialog = () => opened++,
            ToggleOrientation = () => orientationToggles++,
            ApplyMarginPreset = preset => marginPresets.Add(preset),
            ApplyPaperSize = size => paperSizes.Add(size),
        };
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

        Execute(registry, "freew.page-setup");
        Execute(registry, "freew.page-setup-dialog");
        Execute(registry, "freew.custom-margins");
        Execute(registry, "freew.more-paper-sizes");
        Execute(registry, "freew.orientation");
        Execute(registry, "freew.page-orientation");
        Execute(registry, "freew.margins");
        Execute(registry, "freew.page-margins-wide");
        Execute(registry, "freew.size");
        Execute(registry, "freew.page-size-a4");

        opened.Should().Be(4, "shared page setup ids and the old alias must open the dialog route");
        orientationToggles.Should().Be(2, "shared orientation id and alias must use the same callback");
        marginPresets.Should().Equal("narrow", "wide");
        paperSizes.Should().Equal("a4", "a4");
    }

    [Fact]
    public void Column_and_break_commands_mutate_existing_document_model()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        Execute(registry, "freew.columns-three");
        view.Document.Page.ColumnCount.Should().Be(3);
        view.CanUndo.Should().BeTrue("column presets must use the undoable page-settings path");

        Execute(registry, "freew.columns-left");
        view.Document.Page.ColumnCount.Should().Be(2);
        view.Document.Page.ColumnWidthsPt.Should().NotBeNull("Left columns should use unequal widths");

        var blockCount = view.Document.Blocks.Count;
        Execute(registry, "freew.column-break");
        view.Document.Blocks.Should().HaveCount(blockCount + 1);

        Execute(registry, "freew.section-break-continuous");
        view.Document.Blocks
            .OfType<Paragraph>()
            .Any(paragraph => paragraph.SectionBreak is { BreakKind: SectionBreakKind.Continuous })
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Ribbon_definition_contains_layout_tab()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        definition.Tabs.Should().Contain(t => t.Id == "layout",
            "the ribbon must declare a Layout tab");
    }

    [Fact]
    public void Layout_tab_contains_page_setup_group()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var layoutTab = definition.Tabs.FirstOrDefault(t => t.Id == "layout");
        layoutTab.Should().NotBeNull();
        layoutTab!.Groups.Should().Contain(g => g.Id == "page-setup",
            "the Layout tab must have a 'page-setup' group");
    }

    [Fact]
    public void Layout_tab_page_setup_group_uses_shared_command_ids()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var layoutTab = definition.Tabs.First(t => t.Id == "layout");
        var group = layoutTab.Groups.First(g => g.Id == "page-setup");
        var ids = CommandIds(group).ToList();

        ids.Should().Contain(new[]
        {
            "freew.margins",
            "freew.orientation",
            "freew.size",
            "freew.columns",
            "freew.columns-one",
            "freew.columns-two",
            "freew.columns-three",
            "freew.columns-left",
            "freew.columns-right",
            "freew.breaks",
            "freew.page-break",
            "freew.column-break",
            "freew.section-break-next-page",
            "freew.section-break-continuous",
            "freew.section-break-even-page",
            "freew.section-break-odd-page",
            "freew.page-setup",
            "freew.custom-margins",
            "freew.more-paper-sizes",
        });

        ids.Should().NotContain("freew.page-setup-dialog");
        ids.Should().NotContain("freew.page-orientation");
    }

    // ── PageSetupDialog.ApplyResult ───────────────────────────────────────────

    [Fact]
    public void ApplyResult_sets_margins()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var result = Result(
            MarginTopPt:    36,
            MarginBottomPt: 48,
            MarginLeftPt:   54,
            MarginRightPt:  60,
            Landscape:      false,
            WidthPt:        612,
            HeightPt:       792);

        PageSetupDialog.ApplyResult(view, result);

        view.Document.Page.MarginTopPt.Should().BeApproximately(36, 0.01, "top margin should be 36pt");
        view.Document.Page.MarginBottomPt.Should().BeApproximately(48, 0.01, "bottom margin should be 48pt");
        view.Document.Page.MarginLeftPt.Should().BeApproximately(54, 0.01, "left margin should be 54pt");
        view.Document.Page.MarginRightPt.Should().BeApproximately(60, 0.01, "right margin should be 60pt");
    }

    [Fact]
    public void ApplyResult_sets_top_gutter_position()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);
        var result = Result(72, 72, 72, 72, false, 612, 792, GutterAtTop: true);

        PageSetupDialog.ApplyResult(view, result);

        view.Document.Page.GutterAtTop.Should().BeTrue();
    }

    [Fact]
    public void ApplyResult_sets_page_size()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var result = Result(
            MarginTopPt: 72, MarginBottomPt: 72, MarginLeftPt: 72, MarginRightPt: 72,
            Landscape: false,
            WidthPt: 595.3, HeightPt: 841.9);

        PageSetupDialog.ApplyResult(view, result);

        view.Document.Page.WidthPt.Should().BeApproximately(595.3, 0.5, "width should be A4 595.3pt");
        view.Document.Page.HeightPt.Should().BeApproximately(841.9, 0.5, "height should be A4 841.9pt");
    }

    [Fact]
    public void ApplyResult_sets_landscape_flag()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Document.Page.Landscape.Should().BeFalse("default is portrait");

        var result = Result(
            MarginTopPt: 72, MarginBottomPt: 72, MarginLeftPt: 72, MarginRightPt: 72,
            Landscape: true,
            WidthPt: 792, HeightPt: 612);

        PageSetupDialog.ApplyResult(view, result);

        view.Document.Page.Landscape.Should().BeTrue("Landscape flag must be applied");
        view.Document.Page.WidthPt.Should().BeGreaterThan(view.Document.Page.HeightPt,
            "landscape page must have width > height");
    }

    [Fact]
    public void ApplyResult_is_undoable_in_one_step()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var originalTop = view.Document.Page.MarginTopPt;

        var result = Result(
            MarginTopPt: 36, MarginBottomPt: 36, MarginLeftPt: 36, MarginRightPt: 36,
            Landscape: false,
            WidthPt: 612, HeightPt: 792);
        PageSetupDialog.ApplyResult(view, result);

        view.Document.Page.MarginTopPt.Should().BeApproximately(36, 0.01, "margins should be 36pt after apply");

        view.CanUndo.Should().BeTrue("there should be a command to undo after ApplyResult");
        view.Undo();

        view.Document.Page.MarginTopPt.Should().BeApproximately(originalTop, 0.01,
            "after Undo, top margin should be restored to the original value");
        view.CanUndo.Should().BeFalse("the single command should be the only undo entry");
    }

    // ── Orientation toggle ────────────────────────────────────────────────────

    [Fact]
    public void Orientation_toggle_swaps_width_and_height()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        // Default: portrait (width < height).
        var initialW = view.Document.Page.WidthPt;
        var initialH = view.Document.Page.HeightPt;

        // Toggle to landscape: swap so width > height.
        var settings = view.Document.Page.Clone();
        settings.Landscape = true;
        (settings.WidthPt, settings.HeightPt) = (initialH, initialW);
        view.SetPageSettings(settings);

        view.Document.Page.WidthPt.Should().BeApproximately(initialH, 0.01,
            "after toggle to landscape, width should equal the previous height");
        view.Document.Page.HeightPt.Should().BeApproximately(initialW, 0.01,
            "after toggle to landscape, height should equal the previous width");
        view.Document.Page.Landscape.Should().BeTrue("Landscape flag should be set");
    }

    [Fact]
    public void Orientation_toggle_to_landscape_then_back_restores_original_size()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var originalW = view.Document.Page.WidthPt;
        var originalH = view.Document.Page.HeightPt;

        // Toggle to landscape.
        var s1 = view.Document.Page.Clone();
        s1.Landscape = true;
        (s1.WidthPt, s1.HeightPt) = (originalH, originalW);
        view.SetPageSettings(s1);

        // Toggle back to portrait.
        var s2 = view.Document.Page.Clone();
        s2.Landscape = false;
        (s2.WidthPt, s2.HeightPt) = (view.Document.Page.HeightPt, view.Document.Page.WidthPt);
        view.SetPageSettings(s2);

        view.Document.Page.WidthPt.Should().BeApproximately(originalW, 0.01,
            "toggling landscape then back should restore original width");
        view.Document.Page.HeightPt.Should().BeApproximately(originalH, 0.01,
            "toggling landscape then back should restore original height");
        view.Document.Page.Landscape.Should().BeFalse("Landscape flag should be cleared");
    }

    // ── Margin presets ────────────────────────────────────────────────────────

    [Fact]
    public void Margin_preset_normal_sets_72pt_all_sides()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var settings = view.Document.Page.Clone();
        settings.MarginTopPt = settings.MarginBottomPt =
        settings.MarginLeftPt = settings.MarginRightPt = 72;
        view.SetPageSettings(settings);

        view.Document.Page.MarginTopPt.Should().BeApproximately(72, 0.01, "Normal top margin = 72pt (1 in)");
        view.Document.Page.MarginLeftPt.Should().BeApproximately(72, 0.01, "Normal left margin = 72pt");
    }

    [Fact]
    public void Margin_preset_narrow_sets_36pt_all_sides()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var settings = view.Document.Page.Clone();
        settings.MarginTopPt = settings.MarginBottomPt =
        settings.MarginLeftPt = settings.MarginRightPt = 36;
        view.SetPageSettings(settings);

        view.Document.Page.MarginTopPt.Should().BeApproximately(36, 0.01, "Narrow top margin = 36pt (0.5 in)");
        view.Document.Page.MarginLeftPt.Should().BeApproximately(36, 0.01, "Narrow left margin = 36pt");
    }

    [Fact]
    public void Margin_preset_wide_sets_108pt_left_right_72pt_top_bottom()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var settings = view.Document.Page.Clone();
        settings.MarginTopPt    = 72;
        settings.MarginBottomPt = 72;
        settings.MarginLeftPt   = 108;
        settings.MarginRightPt  = 108;
        view.SetPageSettings(settings);

        view.Document.Page.MarginTopPt.Should().BeApproximately(72, 0.01, "Wide top margin = 72pt (1 in)");
        view.Document.Page.MarginLeftPt.Should().BeApproximately(108, 0.01, "Wide left margin = 108pt (1.5 in)");
    }

    // ── Paper size quick commands ─────────────────────────────────────────────

    [Fact]
    public void Paper_size_letter_sets_612x792_in_portrait()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var settings = view.Document.Page.Clone();
        settings.WidthPt  = 612;
        settings.HeightPt = 792;
        view.SetPageSettings(settings);

        view.Document.Page.WidthPt.Should().BeApproximately(612, 0.5, "Letter width = 612pt");
        view.Document.Page.HeightPt.Should().BeApproximately(792, 0.5, "Letter height = 792pt");
    }

    [Fact]
    public void Paper_size_a4_sets_595x842_approximately()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var settings = view.Document.Page.Clone();
        settings.WidthPt  = 595.3;
        settings.HeightPt = 841.9;
        view.SetPageSettings(settings);

        view.Document.Page.WidthPt.Should().BeApproximately(595.3, 0.5, "A4 width ≈ 595pt");
        view.Document.Page.HeightPt.Should().BeApproximately(841.9, 0.5, "A4 height ≈ 842pt");
    }

    // ── SetPageSettings undo ──────────────────────────────────────────────────

    [Fact]
    public void SetPageSettings_is_undoable()
    {
        var doc = MakeDoc();
        var view = new DocumentView();
        view.LoadDocument(doc);

        var originalW = view.Document.Page.WidthPt;
        var originalH = view.Document.Page.HeightPt;

        var settings = view.Document.Page.Clone();
        settings.WidthPt  = 595.3;
        settings.HeightPt = 841.9;
        view.SetPageSettings(settings);

        view.Document.Page.WidthPt.Should().NotBeApproximately(originalW, 1.0, "size should have changed");
        view.CanUndo.Should().BeTrue("SetPageSettings must push an undoable command");

        view.Undo();

        view.Document.Page.WidthPt.Should().BeApproximately(originalW, 0.01, "width restored after Undo");
        view.Document.Page.HeightPt.Should().BeApproximately(originalH, 0.01, "height restored after Undo");
    }

    [Fact]
    public void SetPageSettings_preserves_other_properties()
    {
        // Applying a size change must NOT wipe out watermark, column count, etc.
        var doc = MakeDoc();
        doc.Page.Watermark = "DRAFT";
        doc.Page.ColumnCount = 2;

        var view = new DocumentView();
        view.LoadDocument(doc);

        var settings = view.Document.Page.Clone();
        settings.WidthPt  = 595.3;
        settings.HeightPt = 841.9;
        view.SetPageSettings(settings);

        view.Document.Page.Watermark.Should().Be("DRAFT",
            "SetPageSettings must not disturb the Watermark property");
        view.Document.Page.ColumnCount.Should().Be(2,
            "SetPageSettings must not disturb the ColumnCount property");
    }

    [Fact]
    public void DialogPolicy_IsDelegatedToPresentationPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
            "freew",
            "FreeW.App.Avalonia",
            "PageSetupDialog.cs"));

        source.Should().Contain("PageSetupDialogPlanner.CreateSession(");
        source.Should().Contain("_session.PlanPaperSelection(");
        source.Should().Contain("_session.PlanDimensionEdit(");
        source.Should().Contain("_session.PlanAcceptance(this, followUp)");
        source.Should().Contain("ApplyFocus(_session.InitialFocusPlan)");
        source.Should().Contain("ApplyFocus(acceptance.FocusPlan!)");
        source.Should().Contain("PageSetupDialogPlanner.Surface.LayoutLaunchers");
        source.Should().Contain("Accept(launcher.FollowUp)");
        source.Should().Contain("ControlFor(row.Kind)");
        source.Should().Contain("ApplyEnabledState(_session.EnabledState)");
        source.Should().NotContain("PaperSizes =");
        source.Should().NotContain("TryParseNonNeg(");
        source.Should().NotContain("TryParsePos(");
        source.Should().NotContain("new PageSetupDialogInput(");
        source.Should().NotContain("PageSetupDialogPlanner.BuildInitialState(");
        source.Should().NotContain("PageSetupDialogPlanner.ApplyPaperPreset(");
        source.Should().NotContain("PageSetupDialogPlanner.PaperIndexFor(");
        source.Should().NotContain("PageSetupDialogPlanner.TryBuildResult(");
        source.Should().NotContain("double.TryParse(");
        source.Should().NotContain("new PageSetupDialogResult(");
        source.Should().NotContain("_lineNumbersRequested");
        source.Should().NotContain("_bordersRequested");
        source.Should().NotContain("FocusAndSelect(_top)");
        source.Should().Contain("IPageSetupDialogControlSource");
        source.Should().Contain("PageSetupDialogTabKind initialTab");
        source.Should().NotContain("enum PageSetupDialogTab");
    }


    private static void Execute(RibbonCommandRegistry registry, string commandId)
    {
        registry.TryGet(new RibbonCommandId(commandId), out var command)
            .Should().BeTrue($"{commandId} must be registered");
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static IEnumerable<string> CommandIds(RibbonGroup group)
    {
        foreach (var control in group.Controls)
        {
            if (!string.IsNullOrEmpty(control.CommandId.Value))
                yield return control.CommandId.Value;

            foreach (var menuItem in MenuItems(control))
            {
                foreach (var commandId in CommandIds(menuItem))
                    yield return commandId;
            }
        }
    }

    private static IEnumerable<RibbonMenuItem> MenuItems(RibbonControl control) =>
        control switch
        {
            RibbonDropdown dropdown => dropdown.Menu.Items,
            RibbonSplitButton splitButton => splitButton.Menu.Items,
            _ => Array.Empty<RibbonMenuItem>(),
        };

    private static IEnumerable<string> CommandIds(RibbonMenuItem item)
    {
        if (item.CommandId is { } commandId)
            yield return commandId.Value;

        foreach (var child in item.Children)
        {
            foreach (var childCommandId in CommandIds(child))
                yield return childCommandId;
        }
    }
}
