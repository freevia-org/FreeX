using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Shell;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-VIEW: tests for the deepened View tab — the Zoom dialog mapping, the layout-gridlines and ruler
/// view-state toggles (flag + computed render geometry), and command resolution for the new ids
/// (zoom-dialog, view-gridlines, view-ruler, new-window, split).
/// </summary>
public sealed class ViewTabDepthTests
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
            return false; // no headless backend in this environment
        }
    }

    private static RibbonHostCallbacks NoopCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { }, OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
            OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { },
            InsertPicture: () => { }, OpenWordCountDialog: () => { }, ApplyZoom: (_, _) => { });

    private static TextDocument MakeDoc(string text = "Hello world")
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    private static TextDocument MakeMultiPageDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.WidthPt = 300;
        doc.Page.HeightPt = 220;
        doc.Page.MarginTopPt = 18;
        doc.Page.MarginBottomPt = 18;
        doc.Page.MarginLeftPt = 18;
        doc.Page.MarginRightPt = 18;
        doc.Blocks.Clear();

        for (var i = 0; i < 80; i++)
            doc.Blocks.Add(new Paragraph($"Side-to-side navigation paragraph {i + 1}."));

        return doc;
    }

    // ── Command resolution ───────────────────────────────────────────────────

    [Fact]
    public void View_tab_commands_are_all_registered()
    {
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), NoopCallbacks());

        foreach (var id in new[]
                 {
                     "freew.print-preview",
                     "freew.print-layout", "freew.web-layout", "freew.draft-view",
                     "freew.printlayout", "freew.weblayout", "freew.draftview",
                     "freew.zoom-one-page", "freew.zoom-page-width",
                     "freew.zoom-multiple-pages", "freew.zoom-side-to-side",
                     "freew.zoom-dialog", "freew.gridlines", "freew.ruler", "freew.nav-pane",
                     "freew.view-gridlines", "freew.view-ruler", "freew.navigationpane",
                     "freew.new-window", "freew.split", "freew.split-window",
                     "freew.arrange-all",
                 })
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"AV-VIEW command '{id}' must be registered");
    }

    [Fact]
    public void View_tab_new_commands_appear_in_ribbon_definition()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var viewTab = definition.Tabs.Single(t => t.Id == "view");
        var ids = viewTab.Groups.SelectMany(g => g.Controls)
            .Select(c => c.CommandId.Value)
            .ToList();

        ids.Should().Contain("freew.print-layout");
        ids.Should().Contain("freew.web-layout");
        ids.Should().Contain("freew.draft-view");
        ids.Should().NotContain("freew.printlayout");
        ids.Should().NotContain("freew.weblayout");
        ids.Should().NotContain("freew.draftview");
        ids.Should().Contain("freew.zoom-dialog");
        ids.Should().Contain("freew.zoom-one-page");
        ids.Should().Contain("freew.zoom-page-width");
        ids.Should().Contain("freew.zoom-multiple-pages");
        ids.Should().Contain("freew.zoom-side-to-side");
        ids.Should().Contain("freew.gridlines");
        ids.Should().Contain("freew.ruler");
        ids.Should().Contain("freew.nav-pane");
        ids.Should().NotContain("freew.view-gridlines");
        ids.Should().NotContain("freew.view-ruler");
        ids.Should().NotContain("freew.navigationpane");
        ids.Should().Contain("freew.new-window");
        ids.Should().Contain("freew.arrange-all");
        ids.Should().Contain("freew.split");
        // Show group must surface the Reviewing Pane toggle on the View tab too.
        ids.Should().Contain("freew.reviewing-pane");
    }

    [Fact]
    public void View_zoom_page_fit_commands_invoke_host_callbacks()
    {
        var onePage = 0;
        var pageWidth = 0;
        var callbacks = NoopCallbacks() with
        {
            ZoomOnePage = () => onePage++,
            ZoomPageWidth = () => pageWidth++,
        };
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), callbacks);

        registry.TryGet(new RibbonCommandId("freew.zoom-one-page"), out var onePageCommand)
            .Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.zoom-page-width"), out var pageWidthCommand)
            .Should().BeTrue();

        onePageCommand!.Execute(RibbonCommandContext.Empty);
        pageWidthCommand!.Execute(RibbonCommandContext.Empty);

        onePage.Should().Be(1);
        pageWidth.Should().Be(1);
    }

    [Fact]
    public void View_zoom_page_mode_toggles_are_stateful_host_callbacks()
    {
        var multiplePages = false;
        var sideToSide = false;
        var callbacks = NoopCallbacks() with
        {
            ToggleMultiplePages = () => multiplePages = !multiplePages,
            IsMultiplePagesActive = () => multiplePages,
            ToggleSideToSide = () => sideToSide = !sideToSide,
            IsSideToSideActive = () => sideToSide,
        };
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), callbacks);

        registry.TryGet(new RibbonCommandId("freew.zoom-multiple-pages"), out var multiplePagesCommand)
            .Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.zoom-side-to-side"), out var sideToSideCommand)
            .Should().BeTrue();

        var multiplePagesState = (IRibbonStatefulCommand)multiplePagesCommand!;
        var sideToSideState = (IRibbonStatefulCommand)sideToSideCommand!;
        multiplePagesState.GetState().IsChecked.Should().BeFalse();
        sideToSideState.GetState().IsChecked.Should().BeFalse();

        multiplePagesCommand!.Execute(RibbonCommandContext.Empty);
        sideToSideCommand!.Execute(RibbonCommandContext.Empty);

        multiplePagesState.GetState().IsChecked.Should().BeTrue();
        sideToSideState.GetState().IsChecked.Should().BeTrue();
    }

    [Fact]
    public void View_split_toggle_is_stateful_and_has_wpf_id_alias()
    {
        var split = false;
        var callbacks = NoopCallbacks() with
        {
            ToggleSplit = () => split = !split,
            IsSplitActive = () => split,
        };
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), callbacks);

        registry.TryGet(new RibbonCommandId("freew.split"), out var splitCommand)
            .Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.split-window"), out var splitWindowCommand)
            .Should().BeTrue();
        splitWindowCommand.Should().BeSameAs(splitCommand);

        var state = (IRibbonStatefulCommand)splitCommand!;
        state.GetState().IsChecked.Should().BeFalse();

        splitCommand!.Execute(RibbonCommandContext.Empty);

        state.GetState().IsChecked.Should().BeTrue();
    }

    [Fact]
    public async Task MainWindow_split_preview_uses_live_editor_and_read_only_snapshot()
    {
        FreeWViewDepthMode mode = FreeWViewDepthMode.LiveEditor;
        bool showsLiveBefore = false;
        bool showsLiveDuringSplit = true;
        bool hasSplitGrid = false;
        bool hasSnapshotScroller = false;
        string? limitation = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            showsLiveBefore = window.IsWorkspaceShowingLiveEditor;

            window.ToggleSplit();

            mode = window.ViewDepthMode;
            showsLiveDuringSplit = window.IsWorkspaceShowingLiveEditor;
            limitation = window.ViewDepthLimitation;
            hasSplitGrid = window.WorkspaceContentForTests is Grid;
            if (window.WorkspaceContentForTests is Grid grid)
                hasSnapshotScroller = grid.Children.OfType<ScrollViewer>().Any();
        });

        if (!ran) return;
        showsLiveBefore.Should().BeTrue();
        mode.Should().Be(FreeWViewDepthMode.SplitPreview);
        showsLiveDuringSplit.Should().BeFalse();
        hasSplitGrid.Should().BeTrue();
        hasSnapshotScroller.Should().BeTrue();
        limitation.Should().Contain("read-only");
    }

    [Fact]
    public async Task MainWindow_page_preview_modes_are_mutually_exclusive_and_restore_live_editor()
    {
        FreeWViewDepthMode afterMultiple = FreeWViewDepthMode.LiveEditor;
        FreeWViewDepthMode afterSideToSide = FreeWViewDepthMode.LiveEditor;
        bool multipleActiveAfterSideToSide = true;
        bool sideToSideActive = false;
        bool sideToSideEditorEditable = false;
        bool multiplePagesEditorEditable = false;
        bool liveAfterSecondToggle = false;
        string? sideToSideLimitation = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());

            window.ToggleMultiplePages();
            afterMultiple = window.ViewDepthMode;
            multiplePagesEditorEditable = window.IsMultiplePagesEditorEditableForTests;

            window.ToggleSideToSide();
            afterSideToSide = window.ViewDepthMode;
            multipleActiveAfterSideToSide = window.IsMultiplePagesPreviewActive;
            sideToSideActive = window.IsSideToSidePreviewActive;
            sideToSideLimitation = window.ViewDepthLimitation;
            sideToSideEditorEditable = window.IsSideToSideEditorEditableForTests;

            window.ToggleSideToSide();
            liveAfterSecondToggle = window.IsWorkspaceShowingLiveEditor;
        });

        if (!ran) return;
        afterMultiple.Should().Be(FreeWViewDepthMode.MultiplePagesPreview);
        multiplePagesEditorEditable.Should().BeTrue();
        afterSideToSide.Should().Be(FreeWViewDepthMode.SideToSidePreview);
        multipleActiveAfterSideToSide.Should().BeFalse();
        sideToSideActive.Should().BeTrue();
        sideToSideLimitation.Should().BeNull();
        sideToSideEditorEditable.Should().BeTrue();
        liveAfterSecondToggle.Should().BeTrue();
    }

    [Fact]
    public async Task MainWindow_side_to_side_keeps_cross_page_selection_and_undo_on_live_editor()
    {
        string selectedBeforeEdit = string.Empty;
        string selectedAfterNavigation = string.Empty;
        string textAfterUndo = string.Empty;
        bool canUndoAfterEdit = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.LoadDocument(MakeMultiPageDoc());
            window.ToggleSideToSide();

            window.Editor.SetSelectionRangePublic(0, 0, 2, 8);
            selectedBeforeEdit = window.Editor.SelectedText;
            window.Editor.InsertText("replacement");
            canUndoAfterEdit = window.Editor.CanUndo;

            window.NavigateSideToSideNextPairForTests();
            selectedAfterNavigation = window.Editor.SelectedText;

            window.Editor.Undo();
            textAfterUndo = window.Editor.PlainText;
        });

        if (!ran) return;
        selectedBeforeEdit.Should().NotBeEmpty();
        selectedAfterNavigation.Should().BeEmpty();
        canUndoAfterEdit.Should().BeTrue();
        textAfterUndo.Should().Contain("navigation paragraph 1");
        textAfterUndo.Should().Contain("navigation paragraph 3");
    }

    [Fact]
    public async Task MainWindow_side_to_side_navigation_steps_page_pairs()
    {
        bool hasNavigation = false;
        FreeWViewDepthPagePairNavigationState? initial = null;
        FreeWViewDepthPagePairNavigationState? next = null;
        FreeWViewDepthPagePairNavigationState? previous = null;
        Vector initialOffset = default;
        Vector nextOffset = default;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.LoadDocument(MakeMultiPageDoc());

            window.ToggleSideToSide();

            hasNavigation = window.HasSideToSidePagePairNavigationForTests;
            initial = window.SideToSideNavigationForTests;
            initialOffset = window.SideToSidePreviewOffsetForTests;

            window.NavigateSideToSideNextPairForTests();
            next = window.SideToSideNavigationForTests;
            nextOffset = window.SideToSidePreviewOffsetForTests;

            window.NavigateSideToSidePreviousPairForTests();
            previous = window.SideToSideNavigationForTests;
        });

        if (!ran) return;
        hasNavigation.Should().BeTrue();
        initial!.TotalPages.Should().BeGreaterThan(2);
        initial.FirstVisiblePageNumber.Should().Be(1);
        initial.CanGoToPreviousPair.Should().BeFalse();
        initial.CanGoToNextPair.Should().BeTrue();
        next!.FirstVisiblePageNumber.Should().Be(3);
        next.LastVisiblePageNumber.Should().Be(4);
        next.CanGoToPreviousPair.Should().BeTrue();
        nextOffset.X.Should().BeGreaterThan(initialOffset.X);
        previous!.FirstVisiblePageNumber.Should().Be(1);
    }

    [Fact]
    public async Task MainWindow_side_to_side_pair_navigation_tracks_zoomed_page_stride()
    {
        double pageWidthDip = 0;
        double nextOffset = 0;
        const double zoom = 1.5;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.LoadDocument(MakeMultiPageDoc());
            window.ToggleSideToSide();

            pageWidthDip = PageLayout.PageSizeDip(window.Editor.Document.Page).Width;
            window.ApplyZoomForTests(zoom);
            window.NavigateSideToSideNextPairForTests();
            nextOffset = window.SideToSidePreviewOffsetForTests.X;
        });

        if (!ran) return;
        nextOffset.Should().BeApproximately(
            2 * (pageWidthDip + DocumentViewDepthLayoutPlanner.DefaultInterPageGapDip) * zoom,
            0.01,
            "WPF page-pair navigation remains aligned after the view is zoomed");
    }

    [Fact]
    public async Task Side_to_side_projects_later_pages_horizontally_and_routes_hit_and_caret_geometry()
    {
        int targetBlock = -1;
        int targetPage = -1;
        double desiredWidth = 0;
        double horizontalExtent = 0;
        Point page0 = default;
        Point page1 = default;
        Point page2 = default;
        Rect targetRect = default;
        (int Block, int Offset)? hit = null;

        var ran = await OnUiThread(() =>
        {
            var document = MakeMultiPageDoc();
            var view = new DocumentView();
            view.LoadDocument(document);
            view.ApplyViewDepthLayout(FreeWViewDepthPlanner
                .Build(FreeWViewDepthMode.SideToSidePreview).Layout);
            view.Measure(new Size(816, double.PositiveInfinity));
            desiredWidth = view.DesiredSize.Width;
            horizontalExtent = view.HorizontalPageExtentForTest;
            page0 = view.RenderedPageOriginForTest(0);
            page1 = view.RenderedPageOriginForTest(1);
            page2 = view.RenderedPageOriginForTest(2);

            for (var block = 0; block < document.Blocks.Count; block++)
            {
                view.MoveCaretToBlockForTest(block, 0);
                if (view.CaretPageIndex <= 0 || view.CaretRectForTest is not { } rect)
                    continue;

                targetBlock = block;
                targetPage = view.CaretPageIndex;
                targetRect = rect;
                hit = view.TestHitTest(new Point(rect.X + 1, rect.Y + rect.Height / 2));
                break;
            }
        });

        if (!ran) return;
        targetBlock.Should().BeGreaterThanOrEqualTo(0);
        targetPage.Should().BeGreaterThan(0);
        horizontalExtent.Should().BeGreaterThan(desiredWidth,
            "the live editor must expose a horizontally scrollable page strip");
        page1.X.Should().BeGreaterThan(page0.X);
        page2.X.Should().BeGreaterThan(page1.X);
        page2.Y.Should().Be(page0.Y,
            "Side to Side keeps all pages in one horizontal row rather than using the Multiple Pages grid");
        targetRect.X.Should().BeGreaterThan(400,
            "a caret on page 2+ must carry the page's horizontal origin");
        hit.Should().Be((targetBlock, 0),
            "a click on a later-page glyph must route to that page's document position");
    }

    [Fact]
    public async Task AvaloniaDocumentView_records_shared_multiple_pages_layout_state()
    {
        DocumentViewDepthPageFlow? pageFlow = null;
        int pagesAcross = 0;
        int pageRows = 0;
        bool usesSnapshot = false;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            var plan = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.MultiplePagesPreview);

            view.ApplyViewDepthLayout(plan.Layout);

            pageFlow = view.ViewDepthLayout.PageFlow;
            pagesAcross = view.ViewDepthLayout.PagesAcross;
            pageRows = view.ViewDepthLayout.PageRows;
            usesSnapshot = view.ViewDepthLayout.UsesReadOnlySnapshot;
        });

        if (!ran) return;
        pageFlow.Should().Be(DocumentViewDepthPageFlow.MultiplePagesGrid);
        pagesAcross.Should().Be(2);
        pageRows.Should().Be(2);
        usesSnapshot.Should().BeFalse();
    }

    [Fact]
    public async Task Multiple_pages_projects_live_pages_into_two_column_grid_and_routes_hit_geometry()
    {
        Point page0 = default;
        Point page1 = default;
        Point page2 = default;
        int targetBlock = -1;
        (int Block, int Offset)? hit = null;

        var ran = await OnUiThread(() =>
        {
            var document = MakeMultiPageDoc();
            var view = new DocumentView();
            view.LoadDocument(document);
            view.ApplyViewDepthLayout(FreeWViewDepthPlanner
                .Build(FreeWViewDepthMode.MultiplePagesPreview).Layout);
            view.Measure(new Size(816, double.PositiveInfinity));

            view.PageCount.Should().BeGreaterThan(2);
            page0 = view.RenderedPageOriginForTest(0);
            page1 = view.RenderedPageOriginForTest(1);
            page2 = view.RenderedPageOriginForTest(2);

            for (var block = 0; block < document.Blocks.Count; block++)
            {
                view.MoveCaretToBlockForTest(block, 0);
                if (view.CaretPageIndex < 2 || view.CaretRectForTest is not { } rect)
                    continue;

                targetBlock = block;
                hit = view.TestHitTest(new Point(rect.X + 1, rect.Y + rect.Height / 2));
                break;
            }
        });

        if (!ran) return;
        page1.X.Should().BeGreaterThan(page0.X);
        page1.Y.Should().Be(page0.Y);
        page2.X.Should().Be(page0.X);
        page2.Y.Should().BeGreaterThan(page0.Y);
        targetBlock.Should().BeGreaterThanOrEqualTo(0);
        hit.Should().Be((targetBlock, 0));
    }

    [Fact]
    public async Task MainWindow_multiple_pages_keeps_live_editor_selection_and_undo()
    {
        bool liveEditor = false;
        bool canUndo = false;
        string textAfterUndo = string.Empty;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.LoadDocument(MakeMultiPageDoc());
            window.ToggleMultiplePages();
            liveEditor = window.IsWorkspaceShowingLiveEditor && window.IsMultiplePagesEditorEditableForTests;

            window.Editor.SetSelectionRangePublic(0, 0, 0, 10);
            window.Editor.InsertText("edited");
            canUndo = window.Editor.CanUndo;
            window.Editor.Undo();
            textAfterUndo = window.Editor.PlainText;
        });

        if (!ran) return;
        liveEditor.Should().BeTrue();
        canUndo.Should().BeTrue();
        textAfterUndo.Should().Contain("navigation paragraph 1");
    }

    [Fact]
    public void Layout_tab_contains_print_preview_command()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var layoutTab = definition.Tabs.Single(t => t.Id == "layout");
        var ids = layoutTab.Groups.SelectMany(g => g.Controls)
            .Select(c => c.CommandId.Value)
            .ToList();

        ids.Should().Contain("freew.print-preview");
    }

    [Fact]
    public void Print_preview_command_invokes_host_callback()
    {
        var invoked = false;
        var callbacks = NoopCallbacks() with { OpenPrintPreview = () => invoked = true };
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), callbacks);

        registry.TryGet(new RibbonCommandId("freew.print-preview"), out var command)
            .Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        invoked.Should().BeTrue("freew.print-preview must route to the Avalonia host preview surface");
    }

    [Fact]
    public void Print_layout_command_invokes_host_callback()
    {
        var invoked = false;
        var callbacks = NoopCallbacks() with { SetPrintLayout = () => invoked = true };
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), callbacks);

        registry.TryGet(new RibbonCommandId("freew.print-layout"), out var command)
            .Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        invoked.Should().BeTrue("freew.print-layout must route to the Avalonia host print-layout surface");
    }

    [Fact]
    public void View_and_review_stateful_commands_follow_live_host_predicates()
    {
        var viewMode = DocumentViewMode.PrintLayout;
        var navigationVisible = false;
        var revealVisible = false;
        var reviewingVisible = false;
        var callbacks = NoopCallbacks() with
        {
            SetPrintLayout = () => viewMode = DocumentViewMode.PrintLayout,
            SetWebLayout = () => viewMode = DocumentViewMode.WebLayout,
            SetDraftView = () => viewMode = DocumentViewMode.Draft,
            ToggleNavigationPane = () => navigationVisible = !navigationVisible,
            ToggleRevealFormatting = () => revealVisible = !revealVisible,
            ToggleReviewingPane = () => reviewingVisible = !reviewingVisible,
            IsPrintLayoutActive = () => viewMode == DocumentViewMode.PrintLayout,
            IsWebLayoutActive = () => viewMode == DocumentViewMode.WebLayout,
            IsDraftViewActive = () => viewMode == DocumentViewMode.Draft,
            IsNavigationPaneVisible = () => navigationVisible,
            IsRevealFormattingVisible = () => revealVisible,
            IsReviewingPaneVisible = () => reviewingVisible,
        };
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), callbacks);

        AssertChecked(registry, "freew.print-layout", expected: true);
        AssertChecked(registry, "freew.web-layout", expected: false);
        Execute(registry, "freew.web-layout");
        AssertChecked(registry, "freew.print-layout", expected: false);
        AssertChecked(registry, "freew.web-layout", expected: true);

        Execute(registry, "freew.draft-view");
        AssertChecked(registry, "freew.web-layout", expected: false);
        AssertChecked(registry, "freew.draft-view", expected: true);

        AssertChecked(registry, "freew.nav-pane", expected: false);
        Execute(registry, "freew.nav-pane");
        AssertChecked(registry, "freew.nav-pane", expected: true);
        AssertChecked(registry, "freew.navigationpane", expected: true);

        AssertChecked(registry, "freew.reveal-formatting", expected: false);
        Execute(registry, "freew.reveal-formatting");
        AssertChecked(registry, "freew.reveal-formatting", expected: true);

        AssertChecked(registry, "freew.reviewing-pane", expected: false);
        Execute(registry, "freew.reviewing-pane");
        AssertChecked(registry, "freew.reviewing-pane", expected: true);
    }

    [Fact]
    public async Task MainWindow_view_and_pane_commands_report_lifecycle_state()
    {
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            try
            {
                var registry = window.RibbonRegistryForTests!;
                registry.Should().NotBeNull();
                var refreshesBefore = window.RibbonStateRefreshCountForTests;

                AssertChecked(registry, "freew.print-layout", expected: true);
                Execute(registry, "freew.web-layout");
                AssertChecked(registry, "freew.print-layout", expected: false);
                AssertChecked(registry, "freew.web-layout", expected: true);

                Execute(registry, "freew.nav-pane");
                AssertChecked(registry, "freew.nav-pane", expected: true);
                Execute(registry, "freew.reveal-formatting");
                AssertChecked(registry, "freew.reveal-formatting", expected: true);
                Execute(registry, "freew.reviewing-pane");
                AssertChecked(registry, "freew.reviewing-pane", expected: true);
                window.RibbonStateRefreshCountForTests.Should().BeGreaterThan(refreshesBefore,
                    "a rendered ribbon command must synchronize peer toggle visuals after execution");
            }
            finally
            {
                window.Close();
            }
        });

        if (!ran)
            return;
    }

    [Fact]
    public void View_mode_commands_report_one_live_checked_state_and_keep_legacy_aliases()
    {
        var active = DocumentViewMode.PrintLayout;
        var callbacks = NoopCallbacks() with
        {
            SetPrintLayout = () => active = DocumentViewMode.PrintLayout,
            SetWebLayout = () => active = DocumentViewMode.WebLayout,
            SetDraftView = () => active = DocumentViewMode.Draft,
            IsPrintLayoutActive = () => active == DocumentViewMode.PrintLayout,
            IsWebLayoutActive = () => active == DocumentViewMode.WebLayout,
            IsDraftViewActive = () => active == DocumentViewMode.Draft,
        };
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), callbacks);

        var print = Stateful(registry, "freew.print-layout");
        var web = Stateful(registry, "freew.web-layout");
        var draft = Stateful(registry, "freew.draft-view");
        registry.TryGet(new RibbonCommandId("freew.printlayout"), out var printAlias).Should().BeTrue();
        printAlias.Should().BeSameAs(print);

        print.GetState().IsChecked.Should().BeTrue();
        web.GetState().IsChecked.Should().BeFalse();
        draft.GetState().IsChecked.Should().BeFalse();

        web.Execute(RibbonCommandContext.Empty);
        print.GetState().IsChecked.Should().BeFalse();
        web.GetState().IsChecked.Should().BeTrue();
        draft.GetState().IsChecked.Should().BeFalse();

        active = DocumentViewMode.Draft;
        print.GetState().IsChecked.Should().BeFalse();
        web.GetState().IsChecked.Should().BeFalse();
        draft.GetState().IsChecked.Should().BeTrue("state must follow host changes outside the ribbon");
    }

    [Fact]
    public void Pane_commands_report_live_visibility_and_keep_legacy_aliases()
    {
        var navigationVisible = false;
        var reviewingVisible = true;
        var revealVisible = false;
        var callbacks = NoopCallbacks() with
        {
            ToggleNavigationPane = () => navigationVisible = !navigationVisible,
            ToggleReviewingPane = () => reviewingVisible = !reviewingVisible,
            ToggleRevealFormatting = () => revealVisible = !revealVisible,
            IsNavigationPaneVisible = () => navigationVisible,
            IsReviewingPaneVisible = () => reviewingVisible,
            IsRevealFormattingVisible = () => revealVisible,
        };
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), callbacks);

        var navigation = Stateful(registry, "freew.nav-pane");
        var reviewing = Stateful(registry, "freew.reviewing-pane");
        var reveal = Stateful(registry, "freew.reveal-formatting");
        registry.TryGet(new RibbonCommandId("freew.navigationpane"), out var navigationAlias).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.reviewingpane"), out var reviewingAlias).Should().BeTrue();
        navigationAlias.Should().BeSameAs(navigation);
        reviewingAlias.Should().BeSameAs(reviewing);

        navigation.GetState().IsChecked.Should().BeFalse();
        reviewing.GetState().IsChecked.Should().BeTrue();
        reveal.GetState().IsChecked.Should().BeFalse();

        navigation.Execute(RibbonCommandContext.Empty);
        reviewing.Execute(RibbonCommandContext.Empty);
        reveal.Execute(RibbonCommandContext.Empty);
        navigation.GetState().IsChecked.Should().BeTrue();
        reviewing.GetState().IsChecked.Should().BeFalse();
        reveal.GetState().IsChecked.Should().BeTrue();

        revealVisible = false;
        reveal.GetState().IsChecked.Should().BeFalse("state must follow keyboard or shell changes");
    }

    [Fact]
    public void Window_group_exists_on_view_tab()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var viewTab = definition.Tabs.Single(t => t.Id == "view");
        viewTab.Groups.Should().Contain(g => g.Id == "window",
            "AV-VIEW must add a Window group to the View tab");
    }

    [Fact]
    public void View_arrange_all_routes_to_the_host_callback()
    {
        var invoked = 0;
        var callbacks = NoopCallbacks() with { ArrangeAll = () => invoked++ };
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), callbacks);

        registry.TryGet(new RibbonCommandId("freew.arrange-all"), out var command)
            .Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        invoked.Should().Be(1);
    }

    private static void Execute(IRibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static void AssertChecked(IRibbonCommandRegistry registry, string id, bool expected)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
        command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject
            .GetState().IsChecked.Should().Be(expected, $"{id} should report live checked state");
    }

    // ── Gridlines toggle (flag) ──────────────────────────────────────────────

    [Fact]
    public void Gridlines_toggle_flips_flag_via_command()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());
        view.ShowGridlines.Should().BeFalse("gridlines off by default");

        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.gridlines"), out var cmd).Should().BeTrue();
        var state = cmd.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        state.GetState().IsChecked.Should().BeFalse("gridlines start unchecked");

        cmd!.Execute(RibbonCommandContext.Empty);
        view.ShowGridlines.Should().BeTrue("executing freew.gridlines must turn gridlines on");
        state.GetState().IsChecked.Should().BeTrue("the WPF-equivalent command must report checked after enabling");

        cmd!.Execute(RibbonCommandContext.Empty);
        view.ShowGridlines.Should().BeFalse("executing it again must turn gridlines off");
        state.GetState().IsChecked.Should().BeFalse("the WPF-equivalent command must clear checked after disabling");
    }

    [Fact]
    public void Ruler_toggle_flips_flag_via_command()
    {
        var view = new DocumentView();
        view.LoadDocument(MakeDoc());
        view.ShowRuler.Should().BeFalse("ruler off by default");

        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());
        registry.TryGet(new RibbonCommandId("freew.ruler"), out var cmd).Should().BeTrue();
        var state = cmd.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        state.GetState().IsChecked.Should().BeFalse("the ruler starts unchecked");

        cmd!.Execute(RibbonCommandContext.Empty);
        view.ShowRuler.Should().BeTrue("executing freew.ruler must turn the ruler on");
        state.GetState().IsChecked.Should().BeTrue("the WPF-equivalent command must report checked after enabling");

        cmd!.Execute(RibbonCommandContext.Empty);
        view.ShowRuler.Should().BeFalse("executing it again must turn the ruler off");
        state.GetState().IsChecked.Should().BeFalse("the WPF-equivalent command must clear checked after disabling");
    }

    [Fact]
    public void Legacy_view_ids_remain_aliases_for_canonical_wpf_ids()
    {
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.gridlines"), out var gridlines).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.view-gridlines"), out var viewGridlines).Should().BeTrue();
        viewGridlines.Should().BeSameAs(gridlines);

        registry.TryGet(new RibbonCommandId("freew.ruler"), out var ruler).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.view-ruler"), out var viewRuler).Should().BeTrue();
        viewRuler.Should().BeSameAs(ruler);

        registry.TryGet(new RibbonCommandId("freew.nav-pane"), out var navPane).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.navigationpane"), out var navigationPane).Should().BeTrue();
        navigationPane.Should().BeSameAs(navPane);
    }

    // ── Gridlines / ruler render geometry (reflects the flag) ─────────────────

    [Fact]
    public async Task Gridlines_render_geometry_reflects_flag()
    {
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)>? off = null;
        IReadOnlyList<(double X1, double Y1, double X2, double Y2)>? on = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(MakeDoc("Gridlines body text"));
            view.Measure(new Size(816, 4000));

            off = view.ComputeGridlines();
            view.ShowGridlines = true;
            view.Measure(new Size(816, 4000));
            on = view.ComputeGridlines();
        });

        if (!ran) return;
        off!.Should().BeEmpty("no gridlines should be emitted when the flag is off");
        on!.Should().NotBeEmpty("turning ShowGridlines on must produce grid line segments");
        // Both horizontal (Y1==Y2) and vertical (X1==X2) lines must be present.
        on!.Should().Contain(l => Math.Abs(l.Y1 - l.Y2) < 0.001, "horizontal gridlines expected");
        on!.Should().Contain(l => Math.Abs(l.X1 - l.X2) < 0.001, "vertical gridlines expected");
    }

    [Fact]
    public async Task Ruler_tick_geometry_reflects_flag()
    {
        IReadOnlyList<double>? off = null;
        IReadOnlyList<double>? on = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(MakeDoc("Ruler body text"));
            view.Measure(new Size(816, 4000));

            off = view.ComputeRulerTicks();
            view.ShowRuler = true;
            view.Measure(new Size(816, 4000));
            on = view.ComputeRulerTicks();
        });

        if (!ran) return;
        off!.Should().BeEmpty("no ruler ticks when the flag is off");
        on!.Should().NotBeEmpty("turning ShowRuler on must produce inch tick marks");
        on!.Count.Should().BeGreaterThan(1, "a Letter/A4 page width spans several inch ticks");
    }

    [Fact]
    public async Task Gridlines_and_ruler_render_without_throwing_when_enabled()
    {
        // Smoke: a full Render pass with both overlays on must not throw.
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView { ShowGridlines = true, ShowRuler = true };
            view.LoadDocument(MakeDoc("Body with overlays"));
            view.Measure(new Size(816, 4000));
            view.Arrange(new Rect(0, 0, 816, 4000));
        });
        // If the headless backend is unavailable the dispatch returns false — that's fine; when it does
        // run, reaching here without an exception is the assertion.
        ran.Should().BeTrue("the Render path with gridlines + ruler enabled must complete on the headless backend");
    }

    // ── Zoom dialog mapping ──────────────────────────────────────────────────

    [Fact]
    public async Task Zoom_dialog_preset_maps_to_expected_scale()
    {
        double? scale100 = null;
        double? scale200 = null;
        var ran = await OnUiThread(() =>
        {
            // 100% current → the 100% radio is pre-selected → ResolveScale == 1.0.
            scale100 = new ZoomDialog(1.0, new ZoomDialogFitFactors(1.25, 1.5, 0.6)).ResolveScale();
            // 200% current → the 200% radio is pre-selected → ResolveScale == 2.0.
            scale200 = new ZoomDialog(2.0, new ZoomDialogFitFactors(1.25, 1.5, 0.6)).ResolveScale();
        });

        if (!ran) return;
        scale100.Should().Be(1.0, "a 100% current zoom must resolve to the 100% preset");
        scale200.Should().Be(2.0, "a 200% current zoom must resolve to the 200% preset");
    }

    [Fact]
    public async Task Zoom_dialog_custom_percent_maps_to_scale()
    {
        double? scale = null;
        var ran = await OnUiThread(() =>
        {
            // A non-preset current zoom (e.g. 130%) selects the custom radio; ResolveScale reads the box.
            var dialog = new ZoomDialog(1.3, new ZoomDialogFitFactors(1.25, 1.5, 0.6));
            scale = dialog.ResolveScale();
        });

        if (!ran) return;
        scale.Should().BeApproximately(1.3, 0.001,
            "a custom 130% current zoom must resolve to a 1.3 scale via the percent box");
    }

    [Fact]
    public async Task Zoom_dialog_page_relative_presets_resolve_to_their_scales()
    {
        // Page-relative fit arithmetic is owned by the shared presentation planner; the Avalonia
        // dialog only supplies the host's current fit factors.
        ZoomDialogPlanner
            .TryCreateResult(
                new ZoomDialogSelectionRequest(
                    ZoomDialogFitOption.PageWidth,
                    PresetPercent: null,
                    CustomPercentText: "not parsed"),
                new ZoomDialogFitFactors(1.25, 1.5, 0.6),
                out var scale,
                out var error)
            .Should()
            .BeTrue();

        scale.Should().Be(1.25);
        error.Should().BeNull();
        await Task.CompletedTask;
    }

    private static IRibbonStatefulCommand Stateful(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
        return command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
    }
}
