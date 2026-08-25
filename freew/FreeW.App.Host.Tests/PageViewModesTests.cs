using System.IO;
using System.Windows.Controls;
using System.Windows.Documents;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;
using ModelSection = FreeW.Core.Model.Section;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for the three new paginated view modes added to View > Zoom and View > Window:
/// <c>freew.zoom-multiple-pages</c>, <c>freew.zoom-side-to-side</c>, and <c>freew.split-window</c>.
///
/// Split is a pair of synchronized live editors; the two page arrangements use their editable native
/// surfaces. Tests run on STA because they build the real WPF editing surface.
/// </summary>
public sealed class PageViewModesTests
{
    private static DocumentView NewEditor()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Hello World"));
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void ReadMode_AuthorityTogglesChromeOptionsAndRestoresPresentation()
    {
        var window = new MainWindow(new FreeWOptions(), messageService: new NoUiMessageService());
        try
        {
            var editor = GetEditor(window);
            var originalView = editor.ViewMode;
            var originalMaxWidth = editor.MaxWidth;
            var originalMargin = editor.Margin;
            var originalAlignment = editor.HorizontalAlignment;
            var originalBackground = editor.Background;
            var originalPageColor = editor.Model.Page.BackgroundColorHex;
            window.SetReadModePaneVisibilityForTests(navigation: false, reveal: true, reviewing: false);
            var originalNavPaneVisible = window.IsNavigationPaneVisibleForTests;
            var originalRevealPaneVisible = window.IsRevealPaneVisibleForTests;
            var originalReviewingPaneVisible = window.IsReviewingPaneVisibleForTests;

            window.ApplyReadModeColumnWidthForTests("wide");
            window.ApplyReadModePageColorForTests("sepia");
            window.ToggleReadModeForTests();

            window.IsReadModeActiveForTests.Should().BeTrue();
            window.ReadModeMaxWidthForTests.Should().Be(FreeWReadModePlanner.WideColumnWidth);
            window.IsTitleBarVisibleForTests.Should().BeFalse();
            window.IsRibbonVisibleForTests.Should().BeFalse();
            window.IsNavigationPaneVisibleForTests.Should().BeFalse();
            window.IsRevealPaneVisibleForTests.Should().BeFalse();
            window.IsReviewingPaneVisibleForTests.Should().BeFalse();
            ((System.Windows.Media.SolidColorBrush)editor.Background).Color.Should()
                .Be(System.Windows.Media.Color.FromRgb(0xF0, 0xE0, 0xC0));
            editor.ViewMode.Should().Be(originalView, "Read Mode is a chrome/presentation mode, not a view-mode switch");
            editor.Model.Page.BackgroundColorHex.Should().Be(originalPageColor,
                "the selected page color is transient and must not mutate the document");

            window.ToggleReadModeForTests();

            window.IsReadModeActiveForTests.Should().BeFalse();
            window.IsTitleBarVisibleForTests.Should().BeTrue();
            window.IsRibbonVisibleForTests.Should().BeTrue();
            window.IsNavigationPaneVisibleForTests.Should().Be(originalNavPaneVisible);
            window.IsRevealPaneVisibleForTests.Should().Be(originalRevealPaneVisible);
            window.IsReviewingPaneVisibleForTests.Should().Be(originalReviewingPaneVisible);
            editor.ViewMode.Should().Be(originalView);
            editor.MaxWidth.Should().Be(originalMaxWidth);
            editor.Margin.Should().Be(originalMargin);
            editor.HorizontalAlignment.Should().Be(originalAlignment);
            editor.Background.Should().BeSameAs(originalBackground);
            editor.Model.Page.BackgroundColorHex.Should().Be(originalPageColor);
        }
        finally
        {
            window.Close();
        }
    }

    // ── PrintLayout.BuildPaginatedSource / BuildPaginatedDocument ────────────────────────────────

    [StaFact]
    public void BuildPaginatedSource_YieldsAtLeastOnePage()
    {
        var view = NewEditor();

        var source = PrintLayout.BuildPaginatedSource(view);
        var paginator = source.DocumentPaginator;
        paginator.ComputePageCount();

        paginator.PageCount.Should().BeGreaterThanOrEqualTo(1,
            "a non-empty document must produce at least one page");
    }

    [StaFact]
    public void BuildPaginatedDocument_YieldsAtLeastOnePage()
    {
        var view = NewEditor();

        var flow = PrintLayout.BuildPaginatedDocument(view);
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.ComputePageCount();

        paginator.PageCount.Should().BeGreaterThanOrEqualTo(1,
            "a non-empty FlowDocument must paginate into at least one page");
    }

    [StaFact]
    public void BuildPaginatedSource_CommitsEditorContentBeforePaginating()
    {
        // The host calls CommitToModel before building; verify that uncommitted text survives
        // (tags intact) by round-tripping through the paginator pipeline.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Styled") { StyleId = "Heading1" });
        var view = new DocumentView();
        view.LoadModel(doc);

        _ = PrintLayout.BuildPaginatedSource(view);

        // Tags must be intact so a subsequent CommitToModel still recovers the style id.
        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).StyleId.Should().Be("Heading1");
    }

    // ── Multiple Pages mode ─────────────────────────────────────────────────────────────────────

    [StaFact]
    public void MultiplePages_CommandRegistered_WhenHostProvidesCallbacks()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var store = new RibbonStateStore();

        var registry = FreeWRibbonCommands.Build(
            view,
            store,
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleMultiplePages = () => { },
                IsMultiplePagesActive = () => false,
            });

        registry.TryGet("freew.zoom-multiple-pages", out _).Should().BeTrue();
    }

    [StaFact]
    public void MultiplePages_CommandAbsent_WhenHostProvidesNoCallback()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());

        registry.TryGet("freew.zoom-multiple-pages", out _).Should().BeFalse();
    }

    [StaFact]
    public void MultiplePages_IsStateful_ReflectsActiveFlag()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var store = new RibbonStateStore();
        var active = false;

        var registry = FreeWRibbonCommands.Build(
            view,
            store,
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleMultiplePages = () => active = !active,
                IsMultiplePagesActive = () => active,
            });

        registry.TryGet("freew.zoom-multiple-pages", out var command).Should().BeTrue();
        var stateful = (IRibbonStatefulCommand)command!;

        stateful.GetState().IsChecked.Should().BeFalse("initially not active");

        command!.Execute(RibbonCommandContext.Empty);
        stateful.GetState().IsChecked.Should().BeTrue("active after first toggle");

        command.Execute(RibbonCommandContext.Empty);
        stateful.GetState().IsChecked.Should().BeFalse("inactive after second toggle");
    }

    // ── Side to Side mode ───────────────────────────────────────────────────────────────────────

    [StaFact]
    public void SideToSide_CommandRegistered_WhenHostProvidesCallbacks()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var store = new RibbonStateStore();

        var registry = FreeWRibbonCommands.Build(
            view,
            store,
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleSideToSide = () => { },
                IsSideToSideActive = () => false,
            });

        registry.TryGet("freew.zoom-side-to-side", out _).Should().BeTrue();
    }

    [StaFact]
    public void SideToSide_CommandAbsent_WhenHostProvidesNoCallback()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());

        registry.TryGet("freew.zoom-side-to-side", out _).Should().BeFalse();
    }

    [StaFact]
    public void SideToSide_IsStateful_ReflectsActiveFlag()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var store = new RibbonStateStore();
        var active = false;

        var registry = FreeWRibbonCommands.Build(
            view,
            store,
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleSideToSide = () => active = !active,
                IsSideToSideActive = () => active,
            });

        registry.TryGet("freew.zoom-side-to-side", out var command).Should().BeTrue();
        var stateful = (IRibbonStatefulCommand)command!;

        stateful.GetState().IsChecked.Should().BeFalse();
        command!.Execute(RibbonCommandContext.Empty);
        stateful.GetState().IsChecked.Should().BeTrue();
        command.Execute(RibbonCommandContext.Empty);
        stateful.GetState().IsChecked.Should().BeFalse();
    }

    [StaFact]
    public void WpfDocumentView_RecordsSharedSideToSideLayoutState()
    {
        var view = NewEditor();
        var plan = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SideToSidePreview);

        view.ApplyViewDepthLayout(plan.Layout);

        view.ViewDepthLayout.PageFlow.Should().Be(DocumentViewDepthPageFlow.SideToSideHorizontal);
        view.ViewDepthLayout.PagesAcross.Should().Be(2);
        view.ViewDepthLayout.UsesHorizontalPageFlow.Should().BeTrue();
        view.ViewDepthLayout.UsesReadOnlySnapshot.Should().BeFalse();
        view.ViewDepthLayout.AllowsPrimaryEditing.Should().BeTrue();
    }

    [StaFact]
    public void WpfDocumentView_SplitLayoutCapsVerticalPrintMarginsBeforeThePaneIsMeasured()
    {
        var view = NewEditor();
        var plan = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SplitPreview);

        view.ApplyViewDepthLayout(plan.Layout);

        view.ViewDepthLayout.PageFlow.Should().Be(DocumentViewDepthPageFlow.SplitVerticalEditors);
        view.Padding.Top.Should().Be(48,
            "an unmeasured Split editor must reserve client height for editable text instead of both full page margins");
        view.Padding.Bottom.Should().Be(48);
    }

    // ── Split Window mode ────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void PaginatedEditorPanel_TwoColumnGridWrapsThirdPageToSecondRow()
    {
        var editor = new DocumentView();
        editor.LoadModel(NewMultiPageDocument());

        var panel = PaginatedEditorPanel.Build(editor, PaginatedPageLayoutMode.TwoColumnGrid);

        panel.LayoutMode.Should().Be(PaginatedPageLayoutMode.TwoColumnGrid);
        panel.PageBoxes.Should().HaveCountGreaterThan(2);
        var grid = panel.Content.Should().BeOfType<Grid>().Subject;
        grid.ColumnDefinitions.Should().HaveCount(2);
        Grid.GetRow(panel.PageBoxes[0]).Should().Be(0);
        Grid.GetColumn(panel.PageBoxes[0]).Should().Be(0);
        Grid.GetRow(panel.PageBoxes[1]).Should().Be(0);
        Grid.GetColumn(panel.PageBoxes[1]).Should().Be(1);
        Grid.GetRow(panel.PageBoxes[2]).Should().Be(1);
        Grid.GetColumn(panel.PageBoxes[2]).Should().Be(0);

        panel.Rebuild();

        Grid.GetRow(panel.PageBoxes[0]).Should().Be(0);
        Grid.GetColumn(panel.PageBoxes[1]).Should().Be(1);
        Grid.GetRow(panel.PageBoxes[2]).Should().Be(1,
            "live repagination and undo rebuilds must retain the two-column arrangement");
    }

    [StaFact]
    public void WpfHost_MultiplePagesEditsCommitWhenRestoringLiveEditor()
    {
        var window = new MainWindow(new FreeWOptions(), messageService: new NoUiMessageService());
        try
        {
            var document = NewMultiPageDocument();
            var firstParagraph = document.Blocks.OfType<Paragraph>().First();
            firstParagraph.SectionBreak = new ModelSection(new PageSettings(), SectionBreakKind.NextPage);
            GetEditor(window).LoadModel(document);

            InvokePrivate(window, "ToggleMultiplePages");

            window.HasMultiplePagesEditablePageSurfaceForTests.Should().BeTrue();
            var panel = window.EditablePaginatedPanelForTests
                ?? throw new InvalidOperationException("Multiple Pages did not create an editable WPF surface.");
            panel.LayoutMode.Should().Be(PaginatedPageLayoutMode.TwoColumnGrid);
            panel.PageBoxes[0].Body.AppendText(" Multiple Pages edit persisted.");

            InvokePrivate(window, "ToggleMultiplePages");

            window.HasMultiplePagesEditablePageSurfaceForTests.Should().BeFalse();
            var paragraphs = GetEditor(window).Model.Blocks.OfType<Paragraph>().ToList();
            paragraphs.Should().Contain(paragraph =>
                paragraph.PlainText.Contains("Multiple Pages edit persisted.", StringComparison.Ordinal));
            paragraphs[0].SectionBreak.Should().NotBeNull();
            paragraphs[0].SectionBreak!.BreakKind.Should().Be(SectionBreakKind.NextPage);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfHost_SplitEditorsSynchronizeLowerPaneEditsToTheSavedDocument()
    {
        var window = new MainWindow(new FreeWOptions(), messageService: new NoUiMessageService());
        try
        {
            GetEditor(window).LoadModel(NewEditor().Model);
            InvokePrivate(window, "ToggleSplitWindow");

            var lower = window.SplitEditorForTests;
            lower.Should().NotBeNull();
            lower!.IsReadOnly.Should().BeFalse();
            lower.InsertText(" lower pane edit");

            GetEditor(window).Model.PlainText.Should().Contain("lower pane edit");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfHost_SideToSideNavigationControlsStepPagePairs()
    {
        var window = new MainWindow(new FreeWOptions(), messageService: new NoUiMessageService());
        try
        {
            GetEditor(window).LoadModel(NewMultiPageDocument());

            InvokePrivate(window, "ToggleSideToSide");

            window.HasSideToSidePagePairNavigationForTests.Should().BeTrue();
            window.HasSideToSideEditablePageSurfaceForTests.Should().BeTrue();
            var initial = window.SideToSideNavigationForTests;
            initial.TotalPages.Should().BeGreaterThan(2);
            initial.FirstVisiblePageNumber.Should().Be(1);
            initial.CanGoToPreviousPair.Should().BeFalse();
            initial.CanGoToNextPair.Should().BeTrue();

            window.NavigateSideToSideNextPairForTests();
            var next = window.SideToSideNavigationForTests;
            next.FirstVisiblePageNumber.Should().Be(3);
            next.LastVisiblePageNumber.Should().Be(4);
            next.CanGoToPreviousPair.Should().BeTrue();

            window.NavigateSideToSidePreviousPairForTests();
            window.SideToSideNavigationForTests.FirstVisiblePageNumber.Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SplitWindow_CommandRegistered_WhenHostProvidesCallbacks()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var store = new RibbonStateStore();

        var registry = FreeWRibbonCommands.Build(
            view,
            store,
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleSplit = () => { },
                IsSplitActive = () => false,
            });

        registry.TryGet("freew.split-window", out _).Should().BeTrue();
    }

    [StaFact]
    public void SplitWindow_CommandAbsent_WhenHostProvidesNoCallback()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());

        registry.TryGet("freew.split-window", out _).Should().BeFalse();
    }

    [StaFact]
    public void SplitWindow_IsStateful_ReflectsActiveFlag()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var store = new RibbonStateStore();
        var active = false;

        var registry = FreeWRibbonCommands.Build(
            view,
            store,
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleSplit = () => active = !active,
                IsSplitActive = () => active,
            });

        registry.TryGet("freew.split-window", out var command).Should().BeTrue();
        var stateful = (IRibbonStatefulCommand)command!;

        stateful.GetState().IsChecked.Should().BeFalse();
        command!.Execute(RibbonCommandContext.Empty);
        stateful.GetState().IsChecked.Should().BeTrue();
        command.Execute(RibbonCommandContext.Empty);
        stateful.GetState().IsChecked.Should().BeFalse();
    }

    // ── Ribbon placement parity ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ViewZoom_ExposesMultiplePagesAndSideToSide()
    {
        var zoom = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf).FindTab("view")!.FindGroup("zoom");

        zoom.Should().NotBeNull();
        zoom!.Controls.Select(c => c.CommandId.Value)
            .Should()
            .Contain("freew.zoom-multiple-pages", "Multiple Pages is placed in View > Zoom");
        zoom.Controls.Select(c => c.CommandId.Value)
            .Should()
            .Contain("freew.zoom-side-to-side", "Side to Side is placed in View > Zoom");
    }

    [Fact]
    public void ViewWindow_ExposesSplitWindow()
    {
        var view = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf).FindTab("view");
        var window = view!.FindGroup("window");

        window.Should().NotBeNull("View > Window group must exist");
        window!.Controls.Select(c => c.CommandId.Value)
            .Should()
            .Contain("freew.split-window", "Split is placed in View > Window");
    }

    [Fact]
    public void ViewZoomLabels_MatchWordLabels()
    {
        var zoom = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf).FindTab("view")!.FindGroup("zoom");

        zoom.Should().NotBeNull();
        zoom!.Controls.Select(c => c.Label)
            .Should()
            .Contain("Multiple Pages")
            .And
            .Contain("Side to Side");
    }

    [Fact]
    public void ViewWindowLabels_ContainSplit()
    {
        var window = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf).FindTab("view")!.FindGroup("window");

        window.Should().NotBeNull();
        window!.Controls.Select(c => c.Label)
            .Should()
            .Contain("Split");
    }

    // ── Split snapshot building ──────────────────────────────────────────────────────────────────

    [Fact]
    public void WpfHost_RoutesViewDepthStateThroughPortableSession()
    {
        var source = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Host",
            "MainWindow.cs"));

        source.Should().Contain("FreeWViewSession _viewSession");
        source.Should().Contain("_viewSession.Execute(FreeWViewDepthCommand.ToggleMultiplePages)");
        source.Should().Contain("_viewSession.Execute(FreeWViewDepthCommand.ToggleSideToSide)");
        source.Should().Contain("_viewSession.Execute(FreeWViewDepthCommand.ToggleSplit)");
        source.Should().Contain("ApplyViewDepthTransition(");
        source.Should().Contain("_editor.ApplyViewDepthLayout(plan.Layout);");
        source.Should().Contain("var pagesAcross = plan.Layout.PagesAcross > 1 ? plan.Layout.PagesAcross : 0;");
        source.Should().Contain("DocumentViewDepthLayoutPlanner.BuildDocumentViewerZoomPercent(");
        source.Should().Contain("SyncViewDepthRibbonState()");
        source.Should().Contain("IsMultiplePagesActive = () => _viewSession.CurrentDepth.IsMultiplePagesActive");
        source.Should().Contain("IsSideToSideActive = () => _viewSession.CurrentDepth.IsSideToSideActive");
        source.Should().Contain("IsSplitActive = () => _viewSession.CurrentDepth.IsSplitActive");
        source.Should().NotContain("CurrentViewDepthState");
        source.Should().NotContain("private bool _multiplePagesMode;");
        source.Should().NotContain("private bool _sideToSideMode;");
        source.Should().NotContain("private bool _splitWindowMode;");
    }

    [StaFact]
    public void SplitSnapshot_BuildPaginatedDocument_DoesNotThrowAndPreservesContent()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Split test paragraph"));
        var view = new DocumentView();
        view.LoadModel(doc);

        FlowDocument? snapshot = null;
        var ex = Record.Exception(() => snapshot = PrintLayout.BuildPaginatedDocument(view));

        ex.Should().BeNull("BuildPaginatedDocument must not throw");
        snapshot.Should().NotBeNull();
        snapshot!.Blocks.Should().NotBeEmpty("the cloned document must carry blocks");
    }

    [StaFact]
    public void SplitSnapshot_DoesNotMutateEditorModel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Unchanged"));
        var view = new DocumentView();
        view.LoadModel(doc);
        var blocksBefore = view.Model.Blocks.Count;

        _ = PrintLayout.BuildPaginatedDocument(view);

        view.CommitToModel();
        view.Model.Blocks.Count.Should().Be(blocksBefore,
            "building the split snapshot must not mutate the editor model");
    }

    private static string FindRepoFile(params string[] parts) =>
        Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine(parts));

    private static TextDocument NewMultiPageDocument()
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

    private static DocumentView GetEditor(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_editor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (DocumentView)field!.GetValue(window)!;
    }

    private static void InvokePrivate(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method!.Invoke(window, null);
    }

    private sealed class NoUiMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) =>
            UserMessageResult.No;
    }

}
