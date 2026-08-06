using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void SheetTabsController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var sheetTabsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");

        mainSource.Should().NotContain("private void RefreshSheetTabs()");
        mainSource.Should().NotContain("private void SheetTab_MouseLeftButtonDown(");
        mainSource.Should().NotContain("private void UpdateSheetTabNavigation()");
        mainSource.Should().NotContain("private void RenameSheetFromTab(");
        mainSource.Should().NotContain("private void MoveSheetTab(");

        sheetTabsSource.Should().Contain("private void RefreshSheetTabs()");
        sheetTabsSource.Should().Contain("private void SheetTab_MouseLeftButtonDown(");
        sheetTabsSource.Should().Contain("private void UpdateSheetTabNavigation()");
        sheetTabsSource.Should().Contain("private void RenameSheetFromTab(");
        sheetTabsSource.Should().Contain("private void MoveSheetTab(");
    }

    [Fact]
    public void SheetTabListPlanning_LivesInPresentationWithHostMappingOnly()
    {
        var hostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.SheetTabs.cs");
        var sheetTabsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        var refreshSource = ExtractMethodSource(sheetTabsSource, "private void RefreshSheetTabs()");
        var mapperSource = ExtractMethodSource(sheetTabsSource, "private static SheetTabViewModel MapSheetTabListEntry(");
        var presentationSource = File.ReadAllText(TestWorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Presentation",
            "SheetUI",
            "SheetTabListPlanner.cs"));

        File.Exists(Path.Combine(hostDirectory, "SheetTabListPlanner.cs")).Should().BeFalse();
        presentationSource.Should().Contain("public sealed record SheetTabListEntry");
        presentationSource.Should().Contain("public static class SheetTabListPlanner");
        presentationSource.Should().NotContain("SheetTabViewModel");
        refreshSource.Should().Contain("SheetTabListPlanner.Build(_workbook, _currentSheetId, _groupedSheetIds)");
        refreshSource.Should().Contain("MapSheetTabListEntry(tab)");
        refreshSource.Should().NotContain("_workbook.Sheets");
        refreshSource.Should().NotContain("workbook.Sheets");
        refreshSource.Should().NotContain("new SheetTabViewModel(");
        mapperSource.Should().Contain("new(entry.Id, entry.Name, entry.TabColor, entry.IsProtected)");
    }

    [Fact]
    public void WorksheetContextMenuController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var contextMenuSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        mainSource.Should().NotContain("private void OnGridContextMenuRequested(");
        mainSource.Should().NotContain("private async void ExecuteWorksheetContextMenuAction(");
        mainSource.Should().NotContain("private void OpenKeyboardContextMenu(");

        contextMenuSource.Should().Contain("private void OnGridContextMenuRequested(");
        contextMenuSource.Should().Contain("private async void ExecuteWorksheetContextMenuAction(");
        contextMenuSource.Should().Contain("private void OpenKeyboardContextMenu(");
        contextMenuSource.Should().Contain("WorksheetContextMenuPlanner.BuildCommands(targetKind, state)");
        contextMenuSource.Should().Contain("MenuKeyTipAssigner.AssignUniqueKeyTips");
    }

    [Fact]
    public void SelectionAndGridInteractionController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        mainSource.Should().NotContain("private void SelectRow(");
        mainSource.Should().NotContain("private void SheetGrid_MouseDown(");
        mainSource.Should().NotContain("private void MainWindow_TextInput(");
        mainSource.Should().NotContain("private void MainWindow_KeyDown(");
        mainSource.Should().NotContain("private void SetActiveCell(");
        mainSource.Should().NotContain("private void SelectCurrentRegionOrAll(");
        mainSource.Should().NotContain("private void AddOrMoveAdditionalSelection(");
        mainSource.Should().NotContain("private void SheetGrid_MouseMove(");
        mainSource.Should().NotContain("private void SheetGrid_MouseUp(");

        selectionSource.Should().Contain("private void SelectRow(");
        selectionSource.Should().Contain("private void SheetGrid_MouseDown(");
        selectionSource.Should().Contain("private void MainWindow_TextInput(");
        selectionSource.Should().Contain("private void MainWindow_KeyDown(");
        selectionSource.Should().Contain("private void SetActiveCell(");
        selectionSource.Should().Contain("private void SelectCurrentRegionOrAll(");
        selectionSource.Should().Contain("private void AddOrMoveAdditionalSelection(");
        selectionSource.Should().Contain("private void SheetGrid_MouseMove(");
        selectionSource.Should().Contain("private void SheetGrid_MouseUp(");
        selectionSource.Should().Contain("ExcelWorksheetNavigationPlanner");
    }

    [Fact]
    public void InlineEditingController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var editingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");
        var dropdownSource = DialogSourceTestSupport.ReadHostSources("MainWindow.EditingDropdowns.cs");
        var formulaReferenceSource = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaReferenceEditing.cs");

        mainSource.Should().NotContain("private void EnterEditMode(");
        mainSource.Should().NotContain("private void ShowInlineEditor(");
        mainSource.Should().NotContain("private void RefreshValidationDropdown(");
        mainSource.Should().NotContain("private void OpenActiveDropdown(");
        mainSource.Should().NotContain("private void InlineEditor_KeyDown(");
        mainSource.Should().NotContain("private void FormulaBar_KeyDown(");
        mainSource.Should().NotContain("private bool CommitEdit(");
        mainSource.Should().NotContain("private bool TryCreateCellFromEntryText(");
        mainSource.Should().NotContain("private bool CommitPreparedEdits(");

        editingSource.Should().Contain("private void EnterEditMode(");
        editingSource.Should().Contain("private void ShowInlineEditor(");
        editingSource.Should().Contain("private void InlineEditor_KeyDown(");
        editingSource.Should().Contain("private void FormulaBar_KeyDown(");
        editingSource.Should().Contain("private bool CommitEdit(");
        editingSource.Should().Contain("_session.CommitCellText(");
        editingSource.Should().Contain("_session.CommitCellTextAcrossSelection(");
        editingSource.Should().NotContain("private bool TryCreateCellFromEntryText(");
        editingSource.Should().NotContain("private bool CommitPreparedEdits(");
        editingSource.Should().Contain("ExcelEditKeyPlanner");
        editingSource.Should().Contain("FormulaRangeEntryPlanner.GetKeyboardSelectionTarget");
        editingSource.Should().NotContain("CellEntryParser");
        formulaReferenceSource.Should().Contain("private bool TryApplyFormulaRangeSelection(");
        formulaReferenceSource.Should().Contain("FormulaRangeEntryPlanner");
        formulaReferenceSource.Should().Contain("FormulaReferenceHighlightPlanner");
        dropdownSource.Should().Contain("private void RefreshValidationDropdown(");
        dropdownSource.Should().Contain("private void OpenActiveDropdown(");
        dropdownSource.Should().Contain("AutoFilterDropdownMenuPlanner");
        dropdownSource.Should().Contain("AutoFilterMenuResources");
        dropdownSource.Should().Contain("DataValidationDropdownPlanner");
    }

    [Fact]
    public void InlineEditing_StartsWithCaretAtEndInsteadOfSelectingAll()
    {
        var editingSource = ReadEditingSource();

        editingSource.Should().NotContain("_inlineEditor.SelectAll();");
        // Round 61 (0779b0fda6, "R61-render-formula-bar-6-2") taught double-click to place the caret
        // at the clicked pixel (matching real Excel/the Avalonia shell) via ResolveInlineEditorCaretIndex;
        // that helper still falls back to "caret at end" (textLength) for keyboard-driven entry (F2,
        // typing, Enter/Tab), which is the behavior this test is really pinning.
        editingSource.Should().Contain("_inlineEditor.CaretIndex = ResolveInlineEditorCaretIndex(clickX, layout.TextOverlayRect.Left - 4);");
        editingSource.Should().Contain("_inlineEditor.SelectionLength = 0;");
        var caretResolverMethod = ExtractMethodSource(editingSource, "private int ResolveInlineEditorCaretIndex(");
        caretResolverMethod.Should().Contain("if (clickX is not { } x)");
        caretResolverMethod.Should().Contain("return textLength;");
    }

    [Fact]
    public void GridStatusAndResizeController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var gridSource = DialogSourceTestSupport.ReadHostSources("MainWindow.GridStatus.cs");

        mainSource.Should().NotContain("private void RefreshStatusBar(");
        mainSource.Should().NotContain("private void OnColumnResizing(");
        mainSource.Should().NotContain("private void OnColumnResized(");
        mainSource.Should().NotContain("private void OnRowResizing(");
        mainSource.Should().NotContain("private void OnRowResized(");
        mainSource.Should().NotContain("private void OnPageMarginsChanged(");
        mainSource.Should().NotContain("private void CaptureColumnResizeSnapshot(");
        mainSource.Should().NotContain("private void CaptureRowResizeSnapshot(");

        gridSource.Should().Contain("private void RefreshStatusBar(");
        gridSource.Should().Contain("private void OnColumnResizing(");
        gridSource.Should().Contain("private void OnColumnResized(");
        gridSource.Should().Contain("private void OnRowResizing(");
        gridSource.Should().Contain("private void OnRowResized(");
        gridSource.Should().Contain("private void OnPageMarginsChanged(");
        gridSource.Should().Contain("private void CaptureColumnResizeSnapshot(");
        gridSource.Should().Contain("private void CaptureRowResizeSnapshot(");
        gridSource.Should().Contain("GridResizePreviewPlanner.CaptureColumnSnapshot(sheet, startCol, endCol)");
        gridSource.Should().Contain("GridResizePreviewPlanner.CaptureRowSnapshot(sheet, startRow, endRow)");
        gridSource.Should().Contain("GridResizePreviewPlanner.ApplyColumnResizePreview(sheet, startCol, endCol, newWidthPx)");
        gridSource.Should().Contain("GridResizePreviewPlanner.ApplyRowResizePreview(sheet, startRow, endRow, newHeightPx)");
        gridSource.Should().Contain("StatusBarRefreshPlanner");
    }

    [Fact]
    public void SheetTabs_UseContextualNavigationArrowsInsteadOfAHorizontalScrollbar()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        var xamlCodeBehind = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var navigationStart = source.IndexOf("private void UpdateSheetTabNavigation()", StringComparison.Ordinal);
        var navigationEnd = source.IndexOf("private void BringCurrentSheetTabIntoView()", navigationStart, StringComparison.Ordinal);
        navigationStart.Should().BeGreaterThanOrEqualTo(0);
        navigationEnd.Should().BeGreaterThan(navigationStart);
        var navigationSource = source[navigationStart..navigationEnd];
        var viewportStart = source.IndexOf("private void UpdateSheetTabViewportWidth()", StringComparison.Ordinal);
        var viewportEnd = source.IndexOf("private void UpdateSheetTabsScrollerClip()", viewportStart, StringComparison.Ordinal);
        viewportStart.Should().BeGreaterThanOrEqualTo(0);
        viewportEnd.Should().BeGreaterThan(viewportStart);
        var viewportSource = source[viewportStart..viewportEnd];

        xaml.Should().Contain("x:Name=\"SheetTabsChromeLayer\"");
        xaml.Should().Contain("Grid.ColumnSpan=\"6\"");
        xaml.Should().Contain("x:Name=\"SheetNavLeftBtn\" Grid.Column=\"0\"");
        xaml.Should().Contain("x:Name=\"SheetTabsScroller\" Grid.Column=\"1\"");
        xaml.Should().Contain("Grid.ColumnSpan=\"4\"");
        xaml.Should().Contain("x:Name=\"SheetTabsScrollableContent\"");
        xaml.Should().Contain("HorizontalScrollBarVisibility=\"Hidden\"");
        xaml.Should().Contain("ScrollChanged=\"SheetTabsScroller_ScrollChanged\"");
        xaml.Should().Contain("SizeChanged=\"SheetTabsScroller_SizeChanged\"");
        xaml.Should().Contain("x:Name=\"AddSheetButton\" Content=\"+\"");
        xaml.Should().Contain("Margin=\"0,0,0,1\"");
        xaml.Should().Contain("Padding=\"0\"");
        xaml.Should().Contain("Width=\"44\"");
        xaml.Should().Contain("MinWidth=\"44\"");
        xaml.Should().Contain("Height=\"27\" MinHeight=\"27\"");
        xaml.Should().Contain("Height=\"28\"");
        xaml.Should().Contain("MinHeight=\"28\"");
        xaml.Should().Contain("Opacity=\"1\"");
        xaml.Should().Contain("x:Name=\"AddSheetHitVisual\"");
        xaml.Should().Contain("Width=\"44\"");
        xaml.Should().Contain("Height=\"27\"");
        xaml.Should().NotContain("x:Name=\"GhostTabRoot\"");
        xaml.Should().NotContain("x:Name=\"GhostTabChrome\"");
        xaml.Should().NotContain("MouseEnter=\"AddSheetButton_MouseEnter\"");
        xaml.Should().NotContain("MouseLeave=\"AddSheetButton_MouseLeave\"");
        xaml.Should().NotContain("x:Name=\"AddSheetButton\" Grid.Column=\"2\" Content=\"+\" Width=\"28\" Height=\"22\"");
        xaml.Should().Contain("CornerRadius=\"0,0,4,4\"");
        xaml.Should().Contain("x:Name=\"SheetNavRightBtn\" Grid.Column=\"3\"");
        xaml.Should().Contain("BorderBrush=\"Transparent\" BorderThickness=\"0\"");
        xaml.Should().Contain("Width=\"28\" Height=\"26\"");
        xaml.Should().Contain("Data=\"M 9,1 L 5,5 L 9,9\"");
        xaml.Should().Contain("Data=\"M 5,1 L 9,5 L 5,9\"");
        xaml.Should().NotContain("x:Name=\"SheetTabsLeftEdgeMask\"");
        xaml.Should().NotContain("x:Name=\"SheetTabsRightEdgeMask\"");
        xaml.Should().NotContain("x:Name=\"SheetTabsViewportMaskLayer\"");
        xaml.Should().NotContain("x:Name=\"SheetTabsLeftViewportMask\"");
        xaml.Should().NotContain("x:Name=\"SheetTabsRightViewportMask\"");
        xaml.Should().Contain("x:Name=\"SheetTabsTrailingViewportReserve\"");
        xaml.Should().Contain("Width=\"28\"");
        xaml.Should().Contain("Height=\"28\"");
        xaml.Should().Contain("Margin=\"0\"");
        xaml.Should().Contain("Padding=\"12,4,12,0\"");
        xaml.Should().NotContain("<TranslateTransform Y=\"-2\"/>");
        xamlCodeBehind.Should().Contain("private const double SheetTabGridRuleTop = 0.5;");
        xamlCodeBehind.Should().Contain("private const double SheetTabRightNavigationReserveWidth = 28.0;");
        xamlCodeBehind.Should().Contain("private const double SheetTabChromeHeight = 28.0;");
        source.Should().Contain("elementBounds.Width + leftOverlap, SheetTabChromeHeight");
        source.Should().Contain("SheetTabSeparatorBrush");
        source.Should().Contain("CreateSheetTabSeparatorGeometry");
        source.Should().Contain("new Rect(left, -3, Math.Max(0, right - left), SheetTabChromeHeight + 6)");
        source.Should().Contain("SheetTabViewportScrollPlanner.CalculateOffsetForSelectedTab(");
        source.Should().NotContain("contextTabsBeforeActive");
        source.Should().NotContain("anchorBounds.Left");
        source.Should().Contain("GetSheetTabsVisibleViewportRight");
        source.Should().Contain("UpdateAddSheetButtonInteractivity");
        source.Should().NotContain("UpdateSheetTabsViewportEdgeMasks");
        source.Should().NotContain("MeasureLeftClippedSheetTabWidth");
        source.Should().NotContain("MeasureRightClippedSheetTabWidth");
        source.Should().Contain("TryGetSheetTabViewportBounds");
        source.Should().NotContain("Canvas.SetLeft(");
        source.Should().Contain("context.BeginFigure(new Point(x, SheetTabGridRuleTop + 7.0)");
        source.Should().Contain("SheetTabsOverlayLayer.Children.Add(CreateSheetTabPath(");
        source.Should().Contain("CreateSheetTabTopRuleGeometry");
        source.Should().Contain("CreateActiveSheetTabContourGeometry");
        source.Should().Contain("CreateInactiveSheetTabFillGeometry");
        source.Should().Contain("tabClipGeometry");
        source.Should().NotContain("CreateActiveSheetTabGridRuleGeometry");
        source.Should().NotContain("CreateActiveSheetTabTopScrubGeometry");
        source.Should().NotContain("activeTop.Left");
        source.Should().NotContain("activeTop.Right");
        source.Should().NotContain("hiddenTop");
        xaml.Should().NotContain("x:Name=\"SheetTabsTrailingScrollSpacer\"");
        source.Should().NotContain("SheetTabsTrailingScrollSpacer");
        source.Should().NotContain("SheetTabLeadingViewportInset");
        xamlCodeBehind.Should().NotContain("SheetTabLeadingViewportInset");
        source.Should().NotContain("TryTrimLeadingSheetTabOverlap");
        source.Should().NotContain("CreateInactiveSheetTabBottomRuleGeometry");
        source.Should().NotContain("SheetTabInactiveBottomRuleBrush");
        source.Should().NotContain("_addSheetButtonHoverVisualActive");
        source.Should().NotContain("_suppressAddSheetHoverUntilLeave");
        source.Should().NotContain("AddSheetButton_MouseEnter");
        source.Should().NotContain("AddSheetButton_MouseLeave");
        source.Should().NotContain("CreateSheetTabFillGeometry");
        source.Should().NotContain("CreateSheetTabOutlineGeometry");
        xaml.Should().Contain("HorizontalAlignment=\"Center\" HorizontalContentAlignment=\"Center\"");
        xaml.Should().Contain("VerticalAlignment=\"Top\" VerticalContentAlignment=\"Center\"");
        xaml.Should().Contain("FontFamily=\"Segoe UI\"");
        xaml.Should().Contain("<Setter Property=\"FontWeight\" Value=\"SemiBold\"/>");
        xaml.Should().Contain("Panel.ZIndex=\"9\"");
        xaml.Should().Contain("Panel.ZIndex=\"8\"");
        xaml.Should().Contain("Panel.ZIndex=\"6\"");
        xaml.Should().Contain("HorizontalAlignment=\"Right\" HorizontalContentAlignment=\"Center\"");
        xaml.Should().Contain("<ScrollBar x:Name=\"HorizontalScroll\" Grid.Column=\"5\"");
        xaml.Should().Contain("VerticalAlignment=\"Center\" Margin=\"0\"");
        xaml.Should().Contain("MinWidth=\"0\"");
        xaml.IndexOf("x:Name=\"AddSheetButton\"", StringComparison.Ordinal)
            .Should().BeLessThan(xaml.IndexOf("x:Name=\"SheetNavRightBtn\"", StringComparison.Ordinal));
        xaml.Should().Contain("Visibility=\"Hidden\"");
        xaml.Should().NotContain("HorizontalScrollBarVisibility=\"Auto\"\r\n                              VerticalScrollBarVisibility=\"Disabled\">\r\n                    <StackPanel Orientation=\"Horizontal\">");

        source.Should().Contain("UpdateSheetTabNavigation();");
        source.Should().Contain("private void UpdateSheetTabViewportWidth()");
        source.Should().Contain("SheetTabScrollbarLayoutPlanner.Plan(tabContentWidth, rowHeaderWidth, rowWidth)");
        source.Should().Contain("CreateVisibleSheetTabClipGeometry");
        source.Should().NotContain("CreateScrollableSheetTabClipGeometry");
        source.Should().Contain("AddSheetButton.Measure");
        source.Should().Contain("SheetTabChromeBounds(AddSheetButton, SheetTabOverlapWidth)");
        source.Should().Contain("add.Left + SheetTabOverlapWidth");
        source.Should().NotContain("available * 2 / 3");
        navigationSource.Should().Contain("SheetNavLeftBtn.Visibility");
        navigationSource.Should().Contain("SheetNavRightBtn.Visibility");
        navigationSource.Should().NotContain("SheetTabsLeftEdgeMask");
        navigationSource.Should().NotContain("SheetTabsRightEdgeMask");
        navigationSource.Should().Contain("UpdateAddSheetButtonInteractivity();");
        navigationSource.Should().Contain("SheetNavLeftBtn.Foreground");
        navigationSource.Should().Contain("SheetNavRightBtn.Foreground");
        navigationSource.Should().Contain("SheetNavLeftBtn.IsHitTestVisible");
        navigationSource.Should().Contain("SheetNavRightBtn.IsHitTestVisible");
        navigationSource.Should().Contain(": Visibility.Hidden;");
        navigationSource.Should().NotContain(": Visibility.Collapsed;");
        viewportSource.Should().NotContain("BringCurrentSheetTabIntoView();");
        source.Should().NotContain("SheetTabsScroller.HorizontalOffset - 80");
        source.Should().NotContain("SheetTabsScroller.HorizontalOffset + 80");
    }

    [Fact]
    public void SheetTabsChromeAndViewport_UseNoOpGuardsForRepeatedManyTabNavigationUpdates()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        var chromeSource = ExtractMethodSource(source, "private void UpdateSheetTabsChromeLayer()");
        var viewportSource = ExtractMethodSource(source, "private void UpdateSheetTabViewportWidth()");

        source.Should().Contain("SheetTabsChromeRenderKey");
        source.Should().Contain("SheetTabViewportMeasureKey");
        source.Should().Contain("_sheetTabViewportRefreshQueued");
        source.Should().Contain("visibleTabs.Count");
        source.Should().Contain("foreach (var tab in _sheetTabs)");

        chromeSource.Should().Contain("CreateSheetTabsChromeRenderKey");
        chromeSource.Should().Contain("if (_lastSheetTabsChromeRenderKey == renderKey)");
        chromeSource.Should().Contain("return;");
        chromeSource.IndexOf("if (_lastSheetTabsChromeRenderKey == renderKey)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(chromeSource.IndexOf("SheetTabsChromeLayer.Children.Clear();", StringComparison.Ordinal));

        viewportSource.Should().Contain("CreateSheetTabViewportMeasureKey");
        viewportSource.Should().Contain("if (_lastSheetTabViewportMeasureKey == viewportKey && _lastSheetTabViewportContentWidth > 0)");
        viewportSource.Should().Contain("ApplySheetTabViewportWidths(_lastSheetTabViewportContentWidth");
        viewportSource.IndexOf("if (_lastSheetTabViewportMeasureKey == viewportKey", StringComparison.Ordinal)
            .Should()
            .BeLessThan(viewportSource.IndexOf("SheetTabsControl.Measure", StringComparison.Ordinal));

        var applyWidthsSource = ExtractMethodSource(source, "private void ApplySheetTabViewportWidths(");
        applyWidthsSource.Should().Contain("if (_sheetTabViewportRefreshQueued)");
        applyWidthsSource.Should().Contain("_sheetTabViewportRefreshQueued = true;");
        applyWidthsSource.Should().Contain("_sheetTabViewportRefreshQueued = false;");
        CountOccurrences(applyWidthsSource, "Dispatcher.BeginInvoke").Should().Be(1);
    }

    [Fact]
    public void SheetTabMutations_RouteThroughHostCommandExecutionHelpers()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        // Round 84 (dd5f3056c3) gave InsertNewSheet an optional insertBeforeSheetId parameter so a
        // sheet-tab context-menu "Insert" could insert before a specific tab, not just append.
        var insert = ExtractMethodSource(source, "private void InsertNewSheet(SheetId? insertBeforeSheetId = null)");
        var rename = ExtractMethodSource(source, "private void RenameSheet(");
        var delete = ExtractMethodSource(source, "private void SheetCtxDelete_Click(");
        var move = ExtractMethodSource(source, "private void MoveSheetTab(");

        insert.Should().Contain("TryExecuteRepeatableCommand(");
        rename.Should().Contain("TryExecuteCommand(new RenameSheetCommand");
        // "Fix 42 verified review-5 FreeX findings" (1551700d33) taught Delete Sheet to act on every
        // grouped/selected sheet tab at once, wrapping >1 RemoveSheetCommand in a CompositeWorkbookCommand,
        // so the RemoveSheetCommand construction is no longer inline in the TryExecuteCommand call.
        delete.Should().Contain("new RemoveSheetCommand(sheetId)");
        delete.Should().Contain("TryExecuteCommand(command, \"Delete Sheet\")");
        move.Should().Contain("TryExecuteCommand(new MoveSheetCommand");

        insert.Should().NotContain("_commandBus.Execute");
        rename.Should().NotContain("_commandBus.Execute");
        delete.Should().NotContain("_commandBus.Execute");
        move.Should().NotContain("_commandBus.Execute");
    }

    [Fact]
    public void MoveOrCopyCreateCopy_UsesSingleCompositeCommandWhenCopyMustMove()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        var method = ExtractMethodSource(source, "private void SheetCtxMoveOrCopy_Click(");

        // "Fix 42 verified review-5 FreeX findings" (1551700d33) extended Move-or-Copy (like Delete)
        // to act on every grouped/selected sheet tab at once: >1 DuplicateSheetCommand are wrapped in
        // a CompositeWorkbookCommand, and the actual repositioning uses the plural MoveSheetsCommand
        // over every newly-copied sheet id rather than a single-sheet MoveSheetCommand(copyIndex, targetIndex).
        method.Should().Contain("new CompositeWorkbookCommand(");
        method.Should().Contain("\"Move or Copy Sheet\"");
        method.Should().Contain("new DuplicateSheetCommand(sheetId)");
        method.Should().Contain("TryExecuteCommand(command, \"Move or Copy Sheet\")");
        method.Should().Contain("new MoveSheetsCommand(copySheetIds, targetIndex)");
        method.Should().Contain("new MoveSheetsCommand(selectedSheetIds, dialog.Result.InsertBeforeIndex)");
        method.Should().NotContain("TryExecuteCommand(new DuplicateSheetCommand(tab.Id), \"Duplicate Sheet\")");
        method.Should().NotContain("_commandBus.Execute");
    }

    [Fact]
    public void WorksheetContextMenuPickFromDropDown_ReusesActiveDropdownPath()
    {
        var source =
            DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs") +
            ReadEditingSource();

        source.Should().Contain("WorkbookApplicationCommandIntent.PickFromDropDown");
        source.Should().Contain("OpenActiveDropdown()");
    }

    [Fact]
    public void WorksheetContextMenuQuickAnalysis_ReusesCtrlQPath()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");

        source.Should().Contain("WorkbookApplicationCommandIntent.QuickAnalysis");
        source.Should().Contain("ShowQuickAnalysisMenu()");
    }

    [Fact]
    public void WorksheetContextMenu_UsesAccessKeyHeaders()
    {
        // The cell context menu now renders through the shared RibbonMenu model: the planner's
        // access mnemonic flows command.AccessHeader -> RibbonMenuItem.Header (adapter) ->
        // WPF MenuItem.Header (renderer). Assert the access header is preserved across that path.
        var adapterSource = DialogSourceTestSupport.ReadAppServicesRibbonSource("WorksheetContextMenuRibbonAdapter.cs");
        var rendererSource = DialogSourceTestSupport.ReadHostSources("WorksheetContextMenuRenderer.cs");

        adapterSource.Should().Contain("command.AccessHeader");
        rendererSource.Should().Contain("Header = accessHeader");
    }

    [Fact]
    public void KeyboardWorksheetContextMenu_IsAnchoredToActiveCell()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        source.Should().Contain("OpenKeyboardContextMenu()");
        source.Should().Contain("TryGetCellOverlayRect(address)");
        source.Should().Contain("menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint");
        source.Should().Contain("menu.HorizontalOffset = screenPoint.X");
        source.Should().Contain("menu.VerticalOffset = screenPoint.Y");
        source.Should().NotContain("OnGridContextMenuRequested(address, default);");
    }

    [Fact]
    public void KeyboardWorksheetContextMenu_FocusesFirstEnabledMenuItem()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        source.Should().Contain("menu.Opened += WorksheetContextMenu_Opened;");
        source.Should().Contain("private static void WorksheetContextMenu_Opened(object sender, RoutedEventArgs e)");
        source.Should().Contain("private static void FocusFirstWorksheetContextMenuItem(ContextMenu menu)");
        source.Should().Contain("foreach (var item in menu.Items)");
        source.Should().Contain("if (item is not MenuItem menuItem || !menuItem.IsEnabled)");
        source.Should().Contain("firstEnabledItem = menuItem;");
        source.Should().Contain("FocusManager.SetFocusedElement(menu, firstEnabledItem);");
        source.Should().Contain("Keyboard.Focus(firstEnabledItem);");
    }

    [Fact]
    public void KeyboardContextMenu_RoutesFocusedSheetTabToSheetTabMenu()
    {
        var contextMenuSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");
        var sheetTabsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        contextMenuSource.Should().Contain("if (TryOpenFocusedSheetTabContextMenu())");
        sheetTabsSource.Should().Contain("private bool TryOpenFocusedSheetTabContextMenu()");
        sheetTabsSource.Should().Contain("Keyboard.FocusedElement is not DependencyObject focusedElement");
        sheetTabsSource.Should().Contain("contextMenu.PlacementTarget = target;");
        sheetTabsSource.Should().Contain("contextMenu.IsOpen = true;");
        keyboardFocusSource.Should().Contain("return TryFocusCurrentSheetTab() || AddSheetButton.Focus();");
        xaml.Should().Contain("Focusable=\"True\"");
    }

    [Fact]
    public void KeyboardSheetTabContextMenu_FocusesFirstEnabledMenuItem()
    {
        var sheetTabsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");

        sheetTabsSource.Should().Contain("contextMenu.Opened += SheetTabContextMenu_Opened;");
        sheetTabsSource.Should().Contain("private static void SheetTabContextMenu_Opened(object sender, RoutedEventArgs e)");
        sheetTabsSource.Should().Contain("private static MenuItem? FindFirstEnabledMenuItem(ContextMenu contextMenu)");
        sheetTabsSource.Should().Contain("foreach (var item in contextMenu.Items)");
        sheetTabsSource.Should().Contain("item is MenuItem { IsEnabled: true } menuItem");
        sheetTabsSource.Should().Contain("var firstEnabledItem = FindFirstEnabledMenuItem(contextMenu);");
        sheetTabsSource.Should().Contain("Keyboard.Focus(firstEnabledItem);");
    }

    [Fact]
    public void FocusedSheetTabs_HandleArrowNavigationBeforeWorksheetNavigation()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");
        var sheetTabsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        var focusAdjacentSource = ExtractMethodSource(sheetTabsSource, "private bool FocusAdjacentVisibleSheetTab(");
        var focusEdgeSource = ExtractMethodSource(sheetTabsSource, "private bool FocusEdgeVisibleSheetTab(");

        selectionSource.Should().Contain("if (TryHandleFocusedSheetTabKeyboardNavigation(e))");
        sheetTabsSource.Should().Contain("private bool TryHandleFocusedSheetTabKeyboardNavigation(System.Windows.Input.KeyEventArgs e)");
        sheetTabsSource.Should().Contain("Keyboard.Modifiers != ModifierKeys.None");
        sheetTabsSource.Should().Contain("if (FindSheetTabContextMenuTarget(focusedElement) is null)");
        sheetTabsSource.Should().Contain("Key.Left => FocusAdjacentVisibleSheetTab(-1)");
        sheetTabsSource.Should().Contain("Key.Right => FocusAdjacentVisibleSheetTab(1)");
        sheetTabsSource.Should().Contain("Key.Home => FocusEdgeVisibleSheetTab(first: true)");
        sheetTabsSource.Should().Contain("Key.End => FocusEdgeVisibleSheetTab(first: false)");
        sheetTabsSource.Should().Contain("FocusSheetTab(nextSheetId.Value);");
        sheetTabsSource.Should().Contain("FocusSheetTab(sheetId.Value);");
        focusAdjacentSource.Should().Contain("SheetTabFocusPlanner.AdjacentTab(_sheetTabs, _currentSheetId, direction, static tab => tab.Id)");
        focusAdjacentSource.Should().NotContain("_sheetTabs.ToList()");
        focusEdgeSource.Should().Contain("SheetTabFocusPlanner.EdgeTab(_sheetTabs, first, static tab => tab.Id)");
        focusEdgeSource.Should().NotContain("_sheetTabs.ToList()");
    }

    [Fact]
    public void F6StatusBar_DelegatesInitialFocusOrderToServicesPlanner()
    {
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");
        var plannerSource = DialogSourceTestSupport.ReadAppServicesSource("StatusBarFocusNavigationPlanner.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        keyboardFocusSource.Should().Contain("return FocusStatusBar();");
        keyboardFocusSource.Should().Contain("private bool FocusStatusBar()");
        keyboardFocusSource.Should().Contain("StatusBarFocusNavigationPlanner.BuildInitialFocusOrder(candidates)");
        keyboardFocusSource.Should().Contain("TryFocusStatusBarElement(GetStatusBarFocusElement(target))");
        keyboardFocusSource.Should().NotContain("return StatusZoomOutButton.Focus() || ZoomSlider.Focus();");
        plannerSource.IndexOf("StatusBarFocusTarget.ZoomOutButton", StringComparison.Ordinal)
            .Should()
            .BeLessThan(plannerSource.IndexOf("StatusBarFocusTarget.ZoomSlider", StringComparison.Ordinal));
        xaml.Should().Contain("x:Name=\"StatusZoomOutButton\"");
        xaml.Should().Contain("x:Name=\"StatusZoomInButton\"");
    }

    [Fact]
    public void F6ShellFocusCycle_IncludesVisiblePivotFieldListTaskPane()
    {
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        keyboardFocusSource.Should().Contain("ShellFocusTarget.TaskPane");
        keyboardFocusSource.Should().Contain("ShellFocusCyclePlanner.GetNextAvailable(current, reverse, IsShellFocusTargetAvailable)");
        keyboardFocusSource.Should().Contain("private bool IsShellFocusTargetAvailable(ShellFocusTarget target)");
        keyboardFocusSource.Should().Contain("IsDescendantOf(focusedElement, PivotFieldListPane)");
        keyboardFocusSource.Should().Contain("return FocusVisibleTaskPane();");
        keyboardFocusSource.Should().Contain("private bool FocusPivotFieldListPane()");
        keyboardFocusSource.Should().Contain("PivotFieldListPane?.Visibility != Visibility.Visible");
        keyboardFocusSource.Should().Contain("TryFocusTaskPaneElement(PivotFieldListSearchBox)");
        xaml.Should().Contain("x:Name=\"PivotFieldListPane\"");
        xaml.Should().Contain("x:Name=\"PivotFieldListSearchBox\"");
        xaml.Should().Contain("x:Name=\"PivotFieldListCloseBtn\"");
    }

    [Fact]
    public void F6ShellFocusCycle_IncludesVisibleSlicerTimelineTaskPane()
    {
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        keyboardFocusSource.Should().Contain("IsDescendantOf(focusedElement, SlicerTimelinePane)");
        keyboardFocusSource.Should().Contain("private bool FocusVisibleTaskPane()");
        keyboardFocusSource.Should().Contain("FocusPivotFieldListPane() ||");
        keyboardFocusSource.Should().Contain("FocusSlicerTimelinePane();");
        keyboardFocusSource.Should().Contain("private bool FocusSlicerTimelinePane()");
        keyboardFocusSource.Should().Contain("SlicerTimelinePane?.Visibility != Visibility.Visible");
        keyboardFocusSource.Should().Contain("TryFocusTaskPaneElement(SlicerTimelinePaneCloseBtn)");
        keyboardFocusSource.Should().Contain("TryFocusTaskPaneElement(SlicerTimelinePane)");
        keyboardFocusSource.Should().Contain("DispatcherPriority.Input");
        xaml.Should().Contain("x:Name=\"SlicerTimelinePane\"");
        xaml.Should().Contain("x:Name=\"SlicerTimelinePaneCloseBtn\"");
        xaml.Should().Contain("KeyboardNavigation.TabNavigation=\"Cycle\"");
        xaml.Should().Contain("KeyboardNavigation.ControlTabNavigation=\"Cycle\"");
    }

    [Fact]
    public void FocusedStatusBar_TabTraversalIsNotHijackedByWorksheetMovement()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");

        selectionSource.Should().Contain("if (TryHandleFocusedStatusBarKeyboardNavigation(e))");
        keyboardFocusSource.Should().Contain("private bool TryHandleFocusedStatusBarKeyboardNavigation(System.Windows.Input.KeyEventArgs e)");
        keyboardFocusSource.Should().Contain("!IsDescendantOf(focusedElement, StatusBarGrid)");
        keyboardFocusSource.Should().Contain("Keyboard.Modifiers is not ModifierKeys.None and not ModifierKeys.Shift");
        keyboardFocusSource.Should().Contain("new TraversalRequest(Keyboard.Modifiers == ModifierKeys.Shift");
        keyboardFocusSource.Should().Contain("FocusNavigationDirection.Previous");
        keyboardFocusSource.Should().Contain("FocusNavigationDirection.Next");
        keyboardFocusSource.Should().Contain("focusedElement.MoveFocus(request);");
    }

    [Fact]
    public void StatusBarSelectionStatistics_SurfaceSeparatesCountAndNumericalCount()
    {
        var gridStatusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.GridStatus.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        xaml.Should().Contain("x:Name=\"StatusStatsPanel\"");
        xaml.Should().Contain("x:Name=\"StatusCountText\"");
        xaml.Should().Contain("x:Name=\"StatusNumericalCountText\"");
        xaml.Should().Contain("x:Name=\"StatusSumText\"");
        xaml.IndexOf("x:Name=\"StatusCountText\"", StringComparison.Ordinal)
            .Should().BeLessThan(xaml.IndexOf("x:Name=\"StatusNumericalCountText\"", StringComparison.Ordinal));
        xaml.IndexOf("x:Name=\"StatusNumericalCountText\"", StringComparison.Ordinal)
            .Should().BeLessThan(xaml.IndexOf("x:Name=\"StatusSumText\"", StringComparison.Ordinal));

        gridStatusSource.Should().Contain("StatusBarRefreshPlanner.Build(");
        gridStatusSource.Should().Contain("ApplyStatusBarRefreshPlan(plan)");
        gridStatusSource.Should().Contain("_statusBarStatsCache.GetOrCalculate");
        gridStatusSource.Should().Contain("WpfResourceKeyTextResolver.StatusBarTextProvider");
        gridStatusSource.Should().Contain("IsFileOperationProgressVisible()");
        gridStatusSource.Should().Contain("SetVisibilityIfChanged(StatusReadyText, Visibility.Collapsed)");
        gridStatusSource.Should().Contain("SetVisibilityIfChanged(StatusStatsPanel, Visibility.Collapsed)");
        gridStatusSource.Should().Contain("StatusBarPresentationPlanner.BuildRendererPlan(plan)");
        gridStatusSource.Should().Contain("GetStatusBarReadoutTextBlock(readout.Kind)");
        gridStatusSource.Should().Contain("StatusBarReadoutKind.Count => StatusCountText");
        gridStatusSource.Should().Contain("StatusBarReadoutKind.NumericalCount => StatusNumericalCountText");
        gridStatusSource.Should().Contain("StatusBarReadoutKind.Sum => StatusSumText");
        WorkspaceFileLocator.ReadAllText("shared", "Free.Shared.AppServices", "StatusBarViewModelCache.cs")
            .Should()
            .Contain("_textProvider.GetReadyText()");
        // Readout formatting now lives in the platform-neutral shared builder, keyed by readout kind
        // (ResourceKeyStatusBarTextProvider maps each kind to its StatusBar_*Format resource).
        WorkspaceFileLocator.ReadAllText("shared", "Free.Shared.AppServices", "StatusBarDisplayModelBuilder.cs")
            .Should()
            .Contain("CountReadout(StatusBarReadoutKind.Count")
            .And.Contain("CountReadout(StatusBarReadoutKind.NumericalCount")
            .And.Contain("Readout(StatusBarReadoutKind.Sum")
            .And.Contain("FormatNumberWithReuse");
        WorkspaceFileLocator.ReadAllText("shared", "Free.Shared.AppServices", "StatusBarPresentationPlanner.cs")
            .Should()
            .Contain("ReadoutValue(model, StatusBarReadoutKind.Count)")
            .And.Contain("ReadoutValue(model, StatusBarReadoutKind.NumericalCount)")
            .And.Contain("ReadoutValue(model, StatusBarReadoutKind.Sum)")
            .And.Contain("ReadoutElement(StatusBarReadoutKind.Count")
            .And.Contain("ReadoutElement(StatusBarReadoutKind.NumericalCount")
            .And.Contain("ReadoutElement(StatusBarReadoutKind.Sum");
        WorkspaceFileLocator.ReadAllText("shared", "Free.Shared.AppServices", "StatusBarTextResourceKeys.cs")
            .Should()
            .Contain("StatusBar_CountFormat")
            .And.Contain("StatusBar_NumericalCountFormat")
            .And.Contain("StatusBar_SumFormat");
        gridStatusSource.Should().NotContain("if (stats.Count == 0)");
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "StatusBarRefreshPlanner.cs")
            .Should()
            .Contain("StatusBarRefreshAction.Ready")
            .And.Contain("StatusBarRefreshAction.Stats")
            .And.Contain("StatusBarRefreshAction.HideReadouts");
    }

    [Fact]
    public void StatusForegroundHarness_PreparesLiveStatAndRangeValueReadback()
    {
        var foregroundSource = WorkspaceFileLocator.ReadAllText("tools", "FreeX.ForegroundCapture", "Program.cs");

        foregroundSource.Should().Contain("\"freex-status-live-stats-accessibility\"");
        foregroundSource.Should().Contain("ResizeForStatusStatisticReadback(handle, processId, guard)");
        foregroundSource.Should().Contain("NativeMethods.SetWindowPos(handle, NativeMethods.HWND_NOTOPMOST, x, y, width, height, NativeMethods.SWP_SHOWWINDOW)");
        foregroundSource.Should().Contain("FindFirstSlider(handle, \"Zoom\")");
        foregroundSource.Should().Contain("catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or TimeoutException or COMException)");
        foregroundSource.Should().Contain("\"uia-rangevalue-set-failed\"");
        foregroundSource.Should().Contain("Last UIA candidate was");
        foregroundSource.Should().Contain("\"excel-status-footer-reference\" => RunExcelStatusFooterReferenceScenario()");
        foregroundSource.Should().Contain("TryValidateExcelStatusFooterStatisticsViaContextMenu");
        foregroundSource.Should().Contain("\"Average 5\"");
        foregroundSource.Should().Contain("\"Sum 20\"");
        foregroundSource.Should().Contain("status-footer-validation-unavailable");
    }

    [Fact]
    public void AutoFilterForegroundHarness_PairsExcelAndFreeXOpenedState()
    {
        var foregroundSource = WorkspaceFileLocator.ReadAllText("tools", "FreeX.ForegroundCapture", "Program.cs");

        foregroundSource.Should().Contain("\"excel-autofilter\" => RunExcelAutoFilterScenario()");
        foregroundSource.Should().Contain("\"freex-autofilter\" => RunFreeXMainWindowPointerScenario(\"freex-autofilter\", FreeXAutoFilterOpenedState())");
        foregroundSource.Should().Contain("Seeded A1:D6 through foreground paste");
        foregroundSource.Should().Contain("NativeMethods.VK_L");
        foregroundSource.Should().Contain("NativeMethods.VK_DOWN");
        foregroundSource.Should().Contain("GuardedSendKeys(options.Scenario, processId, handle, \"%{DOWN}\", \"sendkeys-alt-down-autofilter\")");
        foregroundSource.Should().Contain("guarded header-cell dropdown click");
        foregroundSource.Should().Contain("FindFreeXAutoFilterDialog(processId, handle.ToInt64(), options.PopupTimeout)");
        foregroundSource.Should().Contain("WindowHasUiaText(dialog.Handle, \"Sort A to Z\")");
        foregroundSource.Should().Contain("WindowHasUiaText(dialog.Handle, \"Text Filters\")");
        foregroundSource.Should().Contain("WindowHasUiaText(dialog.Handle, \"Select All\")");
        foregroundSource.Should().Contain("autofilter-dialog-not-found");
    }

    [Fact]
    public void SheetTabForegroundHarness_HasCoordinateFallbackForHiddenTabUiaNames()
    {
        var foregroundSource = WorkspaceFileLocator.ReadAllText("tools", "FreeX.ForegroundCapture", "Program.cs");

        foregroundSource.Should().Contain("\"freex-sheet-tab-context-menu\" => RunFreeXMainWindowPointerScenario(\"freex-sheet-tab-context-menu\", RightClickSheetTabContextMenu())");
        foregroundSource.Should().Contain("TryOpenExcelSheetTabContextMenu");
        foregroundSource.Should().Contain("GetSheetTabStripFallbackPoints");
        foregroundSource.Should().Contain("Captured Microsoft Excel's sheet-tab context menu through guarded tab-strip coordinate fallback");
        foregroundSource.Should().Contain("Opened the FreeX sheet-tab context menu through guarded tab-strip coordinate fallback");
        foregroundSource.Should().Contain("TryShowExcelCellCommandBar");
        foregroundSource.Should().Contain("Cell command-bar context menu fallback");
        foregroundSource.Should().Contain("TryInvokeAutomationElement(addButton)");
        foregroundSource.Should().Contain("Visible tabs:");
        foregroundSource.Should().Contain("FindForegroundWindow(");
        foregroundSource.Should().Contain("ProcessHasVisibleMenuItems(processId, \"Rename\", \"Move or Copy\", \"Select All Sheets\")");
        foregroundSource.Should().Contain("Visible menu items after final attempt:");
        foregroundSource.Should().Contain("GetSheetTabIdentity(element)");
        foregroundSource.Should().Contain("element.Current.AutomationId");
        foregroundSource.Should().Contain("OpenFreeXSheetTabContextMenuNearAddButton");
        foregroundSource.Should().Contain("immediately left of the Insert Sheet button");
        foregroundSource.Should().Contain("Opened the FreeX sheet-tab context menu by cycling focus with F6 and pressing Shift+F10");
        foregroundSource.Should().Contain("FindActivateSheetListDialogWindow");
        foregroundSource.Should().Contain("IsActivateSheetListDialogWindow");
        foregroundSource.Should().Contain("WindowContainsSheetActivationList(window)");
        foregroundSource.Should().Contain("TryOpenExcelActivateSheetListDialogFromWorkbookTabsCommandBar");
        foregroundSource.Should().Contain("TryShowExcelWorkbookTabsCommandBar");
        foregroundSource.Should().Contain("\"Workbook Tabs\"");
        foregroundSource.Should().Contain("\"More Sheets\"");
        foregroundSource.Should().Contain("The harness intentionally rejects the built-in xlDialogActivate workbook/window dialog");
    }

    [Fact]
    public void UxParityScenarioBatch_ExposesExpandedCoreSuite()
    {
        var batchSource = WorkspaceFileLocator.ReadAllText("tools", "Run-UxParityScenarioBatch.ps1");

        // "native-output" was added as its own scenario category (0b54f1cb46 "batch native output
        // foreground evidence" and follow-ups); the pinned ValidateSet literal predates that.
        batchSource.Should().Contain("[ValidateSet(\"smoke\", \"core\", \"dialogs\", \"status\", \"formula\", \"filtering\", \"grid\", \"native-output\", \"all\")]");
        batchSource.Should().Contain("\"core\" { return $pairs | Where-Object { $_[\"id\"] -in @(\"format-cells-dialog\", \"format-cells-context-dialog\", \"sheet-tab-context-menu\", \"sheet-tab-overflow-activate-dialog\") } }");
        batchSource.Should().Contain("id = \"status-footer-reference\"");
        batchSource.Should().Contain("excelScenario = \"excel-status-footer-reference\"");
        batchSource.Should().Contain("freexScenario = \"freex-status-live-stats-accessibility\"");
        batchSource.Should().Contain("\"status\" { return $pairs | Where-Object { $_[\"area\"] -eq \"Status bar\" } }");
        batchSource.Should().Contain("id = \"autofilter-opened-state\"");
        batchSource.Should().Contain("excelScenario = \"excel-autofilter\"");
        batchSource.Should().Contain("freexScenario = \"freex-autofilter\"");
        batchSource.Should().Contain("\"filtering\" { return $pairs | Where-Object { $_[\"area\"] -eq \"Sorting and filtering\" } }");
        batchSource.Should().Contain("comparisonMode = \"freex-only\"");
        batchSource.Should().Contain("freexScenario = \"freex-grid-row-column-resize\"");
        batchSource.Should().Contain("freexScenario = \"freex-grid-wheel-scroll\"");
        batchSource.Should().Contain("\"grid\" { return $pairs | Where-Object { $_[\"area\"] -eq \"Grid pointer mechanics\" } }");
        batchSource.Should().Contain("function New-NotRequiredScenarioResult");
        batchSource.Should().Contain("\"freex-capture-complete\"");
        batchSource.Should().Contain("freexCaptureComplete = $freexCaptureComplete");
        batchSource.Should().Contain("[switch]$MinimizeForeignForeground");
        batchSource.Should().Contain("Clear-ForeignForegroundWindow $Scenario");
        batchSource.Should().Contain("$title.IndexOf(\"Media Player\", [StringComparison]::OrdinalIgnoreCase) -ge 0");
        batchSource.Should().Contain("function Write-ScenarioContactSheet");
        batchSource.Should().Contain("$batchContactSheetPath = Join-Path $runDirectory \"ux-scenario-contact-sheet.png\"");
        batchSource.Should().Contain("contactSheetPath = $batchContactSheetPath");
        batchSource.Should().Contain("UX parity scenario contact sheet");
    }

    [Fact]
    public void FocusedPivotFieldListTaskPane_TabTraversalIsNotHijackedByWorksheetMovement()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        selectionSource.Should().Contain("if (TryHandleFocusedTaskPaneKeyboardNavigation(e))");
        keyboardFocusSource.Should().Contain("private bool TryHandleFocusedTaskPaneKeyboardNavigation(System.Windows.Input.KeyEventArgs e)");
        keyboardFocusSource.Should().Contain("!IsDescendantOf(focusedElement, PivotFieldListPane)");
        keyboardFocusSource.Should().Contain("Keyboard.Modifiers is not ModifierKeys.None and not ModifierKeys.Shift");
        keyboardFocusSource.Should().Contain("e.Key != Key.Tab");
        keyboardFocusSource.Should().Contain("focusedElement.MoveFocus(request);");
        keyboardFocusSource.Should().Contain("e.Handled = true;");
        xaml.Should().Contain("x:Name=\"PivotFieldListPane\"");
        xaml.Should().Contain("KeyboardNavigation.TabNavigation=\"Cycle\"");
        xaml.Should().Contain("KeyboardNavigation.ControlTabNavigation=\"Cycle\"");
    }

    [Fact]
    public void FocusedPivotFieldListTaskPane_EscapeReturnsFocusToWorksheet()
    {
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");
        var taskPaneNavigationStart = keyboardFocusSource.IndexOf(
            "private bool TryHandleFocusedTaskPaneKeyboardNavigation(System.Windows.Input.KeyEventArgs e)",
            StringComparison.Ordinal);
        var ribbonNavigationStart = keyboardFocusSource.IndexOf(
            "private bool IsInsideRibbonSurface(DependencyObject element)",
            StringComparison.Ordinal);

        taskPaneNavigationStart.Should().BeGreaterThanOrEqualTo(0);
        ribbonNavigationStart.Should().BeGreaterThan(taskPaneNavigationStart);
        var taskPaneNavigationSource = keyboardFocusSource[taskPaneNavigationStart..ribbonNavigationStart];

        taskPaneNavigationSource.Should().Contain("if (e.Key == Key.Escape)");
        taskPaneNavigationSource.Should().Contain("FocusSheetGridIfNeeded();");
        taskPaneNavigationSource.Should().Contain("e.Handled = true;");
        taskPaneNavigationSource.Should().Contain("return true;");
    }

    [Fact]
    public void FocusedRibbon_TabAndArrowKeysRequestFocusTraversal()
    {
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");

        keyboardFocusSource.Should().Contain("MoveFocusedRibbonElement(focusedElement, Keyboard.Modifiers == ModifierKeys.Shift");
        keyboardFocusSource.Should().Contain("FocusNavigationDirection.Previous");
        keyboardFocusSource.Should().Contain("FocusNavigationDirection.Next");
        keyboardFocusSource.Should().Contain("Key.Left => FocusNavigationDirection.Left");
        keyboardFocusSource.Should().Contain("Key.Right => FocusNavigationDirection.Right");
        keyboardFocusSource.Should().Contain("Key.Up => FocusNavigationDirection.Up");
        keyboardFocusSource.Should().Contain("Key.Down => FocusNavigationDirection.Down");
        keyboardFocusSource.Should().Contain("Key.Home => FocusNavigationDirection.First");
        keyboardFocusSource.Should().Contain("Key.End => FocusNavigationDirection.Last");
        keyboardFocusSource.Should().Contain("private static bool MoveFocusedRibbonElement(DependencyObject focusedElement, FocusNavigationDirection direction)");
        keyboardFocusSource.Should().Contain("focusedUiElement.MoveFocus(new TraversalRequest(direction));");
    }

    [Fact]
    public void WorksheetContextMenu_UsesObjectAwareTargetKind()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        source.Should().Contain("GetWorksheetContextMenuTargetKind(actualAddr)");
        source.Should().Contain("WorksheetContextMenuPlanner.BuildCommands(targetKind, state)");
        source.Should().Contain("GetSelectedWorksheetContextMenuTargetKind(sheet, address)");
        source.Should().Contain("DrawingTargetResolver.GetTargetPicture(sheet, address, allowFallback: false)");
        source.Should().Contain("WorksheetContextMenuTargetKind.Picture");
        source.Should().Contain("case WorksheetContextMenuAction.FormatPicture:");
        source.Should().Contain("PictureSizeBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.FormatDrawingObject:");
        source.Should().Contain("ObjectSizeBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.EditAltText:");
        source.Should().Contain("SetAltTextBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.SelectionPane:");
        source.Should().Contain("SelectionPaneBtn_Click(this, new RoutedEventArgs());");
    }

    [Fact]
    public void WorksheetContextMenu_UsesRowAndColumnSelectionTargetKinds()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        source.Should().Contain("SheetGrid.SelectedRange is { } selectedRange");
        source.Should().Contain("SelectionRangeService.IsWholeRowSelection(selectedRange)");
        source.Should().Contain("WorksheetContextMenuTargetKind.RowSelection");
        source.Should().Contain("SelectionRangeService.IsWholeColumnSelection(selectedRange)");
        source.Should().Contain("WorksheetContextMenuTargetKind.ColumnSelection");
    }

    [Fact]
    public void ThreadedCommentShortcut_UsesDistinctThreadedCommentWorkflow()
    {
        var keyboard = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        // Commit 52ebe84d9f ("Extract shared presentation review controller") moved the actual
        // threaded-comment command construction out of MainWindow.ReviewCommands.cs and into the
        // shared FreeX.App.Presentation.Comments tier (used by both the WPF and Avalonia hosts);
        // ReviewCommands.cs now only delegates to ReviewSessionController.ApplyThreadedComment.
        var mutationService = DialogSourceTestSupport.ReadPresentationSources("Comments", "PresentationCommentMutationService.cs");

        keyboard.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NewThreadedComment, ReviewNewThreadedCommentBtn_Click)");
        keyboard.Should().NotContain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NewThreadedComment, ReviewNewCommentBtn_Click)");
        source.Should().Contain("private void ReviewNewThreadedCommentBtn_Click");
        source.Should().Contain("ReviewSessionController.ApplyThreadedComment(");
        mutationService.Should().Contain("new SetThreadedCommentCommand(");
        mutationService.Should().Contain("new ApplyThreadedCommentChangesCommand(");
        mutationService.Should().Contain("result.RootText is not null");
    }

    [Fact]
    public void WorksheetContextMenuNewComment_ReusesThreadedCommentWorkflow()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");

        source.Should().Contain("WorkbookApplicationCommandIntent.NewThreadedComment");
        source.Should().Contain("ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs())");
    }

    [Fact]
    public void WorksheetContextMenuEditAndDeleteComment_UseThreadedCommentWorkflow()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");
        var reviewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        source.Should().Contain("WorkbookApplicationCommandIntent.EditThreadedComment");
        source.Should().Contain("WorkbookApplicationCommandIntent.DeleteThreadedComment");
        source.Should().Contain("ReviewDeleteThreadedCommentBtn_Click(this, new RoutedEventArgs())");
        reviewSource.Should().Contain("private void ReviewDeleteThreadedCommentBtn_Click(");
        // Commit 52ebe84d9f moved the actual DeleteThreadedCommentCommand construction into the
        // shared PresentationCommentMutationService; ReviewCommands.cs now delegates to
        // ReviewSessionController.DeleteThreadedComment().
        reviewSource.Should().Contain("ReviewSessionController.DeleteThreadedComment()");
        var mutationService = DialogSourceTestSupport.ReadPresentationSources("Comments", "PresentationCommentMutationService.cs");
        mutationService.Should().Contain("new DeleteThreadedCommentCommand(");
    }

    [Fact]
    public void WorksheetContextMenuResolveComment_UsesThreadedCommentResolveCommand()
    {
        var source =
            DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs") +
            DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        source.Should().Contain("WorkbookApplicationCommandIntent.ResolveThreadedComment");
        source.Should().Contain("WorkbookApplicationCommandIntent.UnresolveThreadedComment");
        source.Should().Contain("TryExecuteRepeatableCurrentRangeCommand(");
        source.Should().Contain("range => new ResolveThreadedCommentCommand(_currentSheetId, range.Start, resolved)");
        source.Should().Contain("sheet.ThreadedComments.TryGetValue(address, out var threadedComment)");
        source.Should().Contain("IsThreadedCommentResolved: threadedComment?.IsResolved == true");
    }

    [Fact]
    public void WorksheetContextMenuShowNotes_UsesNoteOnlyWorkflow()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");
        var plannerSource = DialogSourceTestSupport.ReadAppServicesRibbonSource("WorksheetContextMenuPlanner.cs");

        source.Should().Contain("WorkbookApplicationCommandIntent.ShowHideNote");
        source.Should().Contain("ExecuteShowHideNote(TargetAddress(invocation))");
        source.Should().Contain("WorkbookApplicationCommandIntent.ShowAllNotes");
        source.Should().Contain("ExecuteShowAllNotes()");
        source.Should().NotContain("ReviewShowCommentsBtn_Click(this, new RoutedEventArgs());");
        plannerSource.Should().Contain("\"Show Notes\", WorksheetContextMenuAction.ShowAllNotes, AccessHeader: \"_Show Notes\"");
        plannerSource.Should().Contain("WorksheetContextMenuAction.ShowHideNote, AccessHeader: state.NoteIsShown ? \"_Hide Note\" : \"S_how Note\", IsEnabled: state.HasNote");
        plannerSource.Should().NotContain("IsEnabled: state.HasNote || state.HasThreadedComment");
    }

    [Fact]
    public void ReviewCommentAndNoteNavigation_KeepsThreadedCommentsAndNotesSeparate()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        // Commit 52ebe84d9f ("Extract shared presentation review controller") moved the actual
        // prev/next address-ordering calls out of MainWindow.ReviewCommands.cs (which now delegates
        // to ReviewSessionController.NavigateThreadedComment/NavigateNote) and into the shared
        // PresentationReviewSessionController used by both the WPF and Avalonia hosts.
        var controllerSource = DialogSourceTestSupport.ReadPresentationSources("Comments", "PresentationReviewSessionController.cs");

        source.Should().Contain("CommentListWindow.CreateThreadedCommentItems(sheet.ThreadedComments)");
        source.Should().Contain("ShowOrRefreshCommentListWindow(");
        source.Should().Contain("ReviewSessionController.NavigateThreadedComment(previous)");
        controllerSource.Should().Contain("CommentNavigationPlanner.OrderedThreadedCommentAddresses(sheet.ThreadedComments)");
        source.Should().Contain("sheet.ThreadedComments.Count == 0");
        source.Should().Contain("private void ReviewPrevNoteBtn_Click(");
        source.Should().Contain("private void ReviewNextNoteBtn_Click(");
        source.Should().Contain("private void ReviewShowNotesBtn_Click(");
        source.Should().Contain("CommentListWindow.CreateNoteItems(sheet.Comments)");
        source.Should().Contain("ReviewSessionController.NavigateNote(previous)");
        controllerSource.Should().Contain("CommentNavigationPlanner.OrderedNoteAddresses(sheet.Comments)");
        // NavigateNote's own "sheet.Comments.Count == 0" empty-sheet guard was generalized (52ebe84d9f)
        // into the shared Navigate() helper's "addresses.Count == 0" check, common to both the note
        // and threaded-comment navigation paths.
        controllerSource.Should().Contain("addresses.Count == 0");
        source.Should().NotContain("CommentNavigationPlanner.FormatCommentList(sheet.Comments, sheet.ThreadedComments)");
        source.Should().NotContain("CommentNavigationPlanner.OrderedCommentAddresses(sheet.Comments, sheet.ThreadedComments)");
    }

    [Fact]
    public void Selection_UpdatesVisibleCommentPreviewForSelectionAndHover()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        source.Should().Contain("UpdateCommentPreview(addr)");
        source.Should().Contain("UpdateCommentPreview(hitAddr.Value)");
        source.Should().Contain("ClearCommentPreview()");
        source.Should().Contain("SheetGrid.HideCommentPreview();");
        source.Should().Contain("SetCommentPreview(null);");
        source.Should().Contain("private void SetCommentPreview(string? preview)");
        source.Should().Contain("if (SheetGrid.ToolTip is not null)");
        source.Should().Contain("SheetGrid.ToolTip = null;");
        source.Should().NotContain("CommentNavigationPlanner.FormatCellCommentPreview(");
        source.Should().NotContain("SetCommentPreview(preview)");
        source.Should().NotContain("SheetGrid.ToolTip = preview;");
    }

    [Fact]
    public void AutoFilterKeyboardDropdown_UsesModelessFlyoutAnchoredToHeaderCell()
    {
        var source = ReadEditingSource();
        var dialog = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDialog.cs"));

        source.Should().Contain("PositionAutoFilterFlyout(dialog, headerCell, anchorPoint);");
        source.Should().Contain("private void PositionAutoFilterFlyout(Window dialog, CellAddress headerCell, System.Windows.Point? anchorPoint)");
        source.Should().Contain("TryGetCellOverlayRect(headerCell)");
        source.Should().Contain("SheetGrid.PointToScreen");
        source.Should().Contain("dialog.ConfigureAsModelessFlyout();");
        source.Should().Contain("dialog.Show();");
        source.Should().NotContain("dialog.ShowDialog() != true");
        dialog.Should().Contain("public void ConfigureAsModelessFlyout()");
        dialog.Should().Contain("WindowStartupLocation = WindowStartupLocation.Manual;");
        dialog.Should().Contain("ShowInTaskbar = false;");
    }

    [Fact]
    public void AutoFilterKeyboardDropdown_ReusesFullFilterDialogResultRouting()
    {
        var editingSource = ReadEditingSource();
        var dataFilterSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        editingSource.Should().Contain("dialog.ResultCommitted += (_, result) =>");
        editingSource.Should().Contain("ApplyAutoFilterDialogResult(plan.Range, plan.FilterColumnOffset, result, \"AutoFilter\")");
        dataFilterSource.Should().Contain("private bool ApplyAutoFilterDialogResult(");
        dataFilterSource.Should().Contain("FilterPromptPlanner.TryPlan");
        dataFilterSource.Should().Contain("FilterInputParser.ParseAllowedValues");
    }

    [Fact]
    public void AutoFilterAndAdvancedFilter_RefreshStatusBarAfterRowsAreHiddenOrShown()
    {
        var dataFilterSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");
        var editingSource = ReadEditingSource();
        var dataCommandsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        dataFilterSource.Should().Contain("private void UpdateFilterViewportAndStatusBar()");
        dataFilterSource.Should().Contain("UpdateViewport();");
        dataFilterSource.Should().Contain("RefreshStatusBar();");

        ExtractMethodSource(dataFilterSource, "private void FilterButton_Click(")
            .Should()
            .Contain("UpdateFilterViewportAndStatusBar();");
        ExtractMethodSource(dataFilterSource, "private void ReapplyAutoFilter()")
            .Should()
            .Contain("UpdateFilterViewportAndStatusBar();");
        ExtractMethodSource(dataFilterSource, "private void ClearFilterButton_Click(")
            .Should()
            .Contain("UpdateFilterViewportAndStatusBar();");

        var committedHandlerStart = editingSource.IndexOf("dialog.ResultCommitted += (_, result) =>", StringComparison.Ordinal);
        committedHandlerStart.Should().BeGreaterThanOrEqualTo(0);
        var committedHandlerEnd = editingSource.IndexOf("};", committedHandlerStart, StringComparison.Ordinal);
        committedHandlerEnd.Should().BeGreaterThan(committedHandlerStart);
        var committedHandler = editingSource[committedHandlerStart..committedHandlerEnd];
        committedHandler.Should().Contain("ApplyAutoFilterDialogResult(plan.Range, plan.FilterColumnOffset, result, \"AutoFilter\")");
        committedHandler.Should().Contain("UpdateViewport();");
        committedHandler.Should().Contain("RefreshStatusBar();");

        // R72-commands-sort-filter-4-3 split the viewport/status-bar refresh out of
        // AdvancedFilterBtn_Click into ApplyAdvancedFilterResult, which also now remembers the
        // in-place filter for Data > Reapply -- see that method's own doc comment.
        ExtractMethodSource(dataCommandsSource, "private void AdvancedFilterBtn_Click(")
            .Should()
            .Contain("ApplyAdvancedFilterResult(dialog.Result);");
        ExtractMethodSource(dataCommandsSource, "private void ApplyAdvancedFilterResult(")
            .Should()
            .Contain("UpdateViewport();")
            .And.Contain("RefreshStatusBar();");
    }

    [Fact]
    public void GridRenderedAutoFilterButtons_AreWiredToHostFlyoutRoute()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var editingSource = ReadEditingSource();

        mainSource.Should().Contain("SheetGrid.AutoFilterDropdownRequested += OnAutoFilterDropdownRequested;");
        editingSource.Should().Contain("private void OnAutoFilterDropdownRequested(CellAddress headerCell, System.Windows.Point position)");
        editingSource.Should().Contain("ShowAutoFilterDropdownForHeaderCell(sheet, headerCell, position);");
        editingSource.Should().Contain("AutoFilterDropdownMenuPlanner.TryGetAutoFilterRange(sheet, out var autoFilterRange)");
    }

    [Fact]
    public void AutoFilterKeyboardDropdown_UsesExcelStyleMenuPlanner()
    {
        var source = ReadEditingSource();
        var dialog = DialogSourceTestSupport.ReadHostSources("AutoFilterDialog.cs");

        source.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan(");
        source.Should().Contain("AutoFilterMenuResources.TextProvider");
        source.Should().Contain("new AutoFilterDialog(menuPlan)");
        dialog.Should().Contain("AutoFilterMenuPlan menuPlan");
        dialog.Should().Contain("CriteriaSuggestions");
    }

    [Fact]
    public void AutoFilterKeyboardDropdown_ExposesCriteriaSuggestionPicker()
    {
        var dialog = DialogSourceTestSupport.ReadHostSources("AutoFilterDialog.cs");

        dialog.Should().Contain("_criteriaSuggestionBox");
        dialog.Should().Contain("GetCriteriaSuggestions(menuPlan)");
        dialog.Should().Contain("_criteriaSuggestionBox.SelectionChanged");
    }

    [Fact]
    public void GridResizeSnapshots_DelegatePolicyToPresentationPlanner()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var gridStatusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.GridStatus.cs");
        var plannerSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Presentation",
            "GridInteraction",
            "GridResizePreviewPlanner.cs"));

        mainSource.Should().NotContain("private sealed record ColumnResizeSnapshot(");
        mainSource.Should().NotContain("private sealed record RowResizeSnapshot(");

        gridStatusSource.Should().NotContain("private sealed record ColumnResizeSnapshot(");
        gridStatusSource.Should().NotContain("private sealed record RowResizeSnapshot(");
        mainSource.Should().Contain("GridResizePreviewSnapshot?");
        gridStatusSource.Should().Contain("GridResizePreviewPlanner.RestoreColumnResizePreview(sheet, _columnResizeSnapshot)");
        gridStatusSource.Should().Contain("GridResizePreviewPlanner.RestoreRowResizePreview(sheet, _rowResizeSnapshot)");
        plannerSource.Should().Contain("public sealed record GridResizePreviewSnapshot");
        plannerSource.Should().Contain("public static class GridResizePreviewPlanner");
    }
}
