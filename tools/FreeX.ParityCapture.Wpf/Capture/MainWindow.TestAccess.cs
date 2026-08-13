using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.Ribbon;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private Func<string, string?>? _reservationPasswordPromptOverrideForTest = null;
    private static int? _wheelScrollLinesTestOverride = null;

    partial void TryResolveExternalReservationPasswordPrompt(
        string workbookName,
        ref bool handled,
        ref string? password)
    {
        if (_reservationPasswordPromptOverrideForTest is null)
            return;

        password = _reservationPasswordPromptOverrideForTest(workbookName);
        handled = true;
    }

    static partial void TryGetExternalWheelScrollLines(ref int? lines) =>
        lines = _wheelScrollLinesTestOverride;

    internal bool RaiseFormulaReferenceGripDragForTest(int highlightIndex, CellAddress target)
    {
        var editor = GetFormulaReferenceHighlightEditor();
        var highlights = editor is null
            ? []
            : GetFormulaReferenceHighlights(editor.Text);
        if (editor is null || highlightIndex < 0 || highlightIndex >= highlights.Count ||
            highlights[highlightIndex].Range is not { } originalRange ||
            originalRange.Start.Sheet != target.Sheet)
        {
            return false;
        }

        var newRange = _formulaRangeEditingSession.PlanReferenceDrag(highlights[highlightIndex], target);
        if (newRange is null)
            return false;

        ApplyFormulaReferenceResize(editor, highlights[highlightIndex], newRange.Value);
        RefreshFormulaReferenceHighlights();
        return true;
    }

    internal string FormulaBoxTextForTest
    {
        get => FormulaBar.Text;
        set => FormulaBar.Text = value;
    }

    internal void BeginFormulaPointModeEditForTest(CellAddress address, string formulaText)
    {
        if (!_formulaRangeEditingSession.IsFormulaText(formulaText))
            throw new ArgumentException("Formula point-mode text must start with '='.", nameof(formulaText));

        SheetGrid.SelectedRange = new GridRange(address, address);
        BeginFormulaBarFormulaEdit(formulaText);
    }

    internal void RaiseFormulaBoxKeyDownForTest(KeyEventArgs e) => FormulaBar_KeyDown(FormulaBar, e);

    internal bool RouteFormulaPointSelectionForTest(
        GridRange range,
        bool append = false,
        bool extendSelection = false) =>
        TryRouteFormulaPointModeSelection(range, append, extendSelection);

    internal Control? FindRenderedRibbonCommandControlForTest(string commandName) =>
        FindRenderedRibbonControl(commandName);

    internal void PopulateTableDesignStyleGalleryMenuForTest() =>
        PopulateTableDesignStyleGalleryMenu();

    internal SheetId CurrentSheetIdForTest => _currentSheetId;

    internal CellAddress? FormulaEditCellForTest => _formulaEditCell;

    internal void SetFormulaEditCellForTest(CellAddress address) => _formulaEditCell = address;

    internal void SetCurrentSheetForFormulaPointForTest(SheetId sheetId) => _currentSheetId = sheetId;

    internal FormulaRangeEditingSession FormulaRangeEditingSessionForTest => _formulaRangeEditingSession;

    internal CellAddress? SelectionAnchorForTest
    {
        get => _selectionAnchor;
        set => _selectionAnchor = value;
    }

    internal CellAddress? SelectionCursorForTest
    {
        get => _selectionCursor;
        set => _selectionCursor = value;
    }

    internal TextBox? InlineEditorForTest => _inlineEditor;

    internal bool CommitEditForTest() => CommitEdit();

    internal bool CommitEditAcrossSelectionForTest(bool fillFormulaEditCellOnly) =>
        CommitEditAcrossSelection(fillFormulaEditCellOnly);

    internal void InsertNewSheetForTest() => InsertNewSheet();

    internal void SelectSingleSheetTabForTest(SheetId sheetId) => SelectSingleSheetTab(sheetId);

    internal void UpdateViewportForTest() => UpdateViewport();

    internal void RefreshSheetTabsForTest() => RefreshSheetTabs();

    internal void SetActiveCellForTest(CellAddress address) => SetActiveCell(address);

    internal void ShowInlineEditorForTest(CellAddress address, double? clickX = null) =>
        ShowInlineEditor(address, clickX);

    internal void ExecuteClearSelectionForTest() => ExecuteClearSelection();

    internal void RaiseFormulaBarKeyDownForTest(KeyEventArgs e) => FormulaBar_KeyDown(FormulaBar, e);

    internal void RaiseInlineEditorKeyDownForTest(KeyEventArgs e)
    {
        if (_inlineEditor is null)
            throw new InvalidOperationException("Inline editor is not active.");
        InlineEditor_KeyDown(_inlineEditor, e);
    }

    internal void RaiseCellAddressBoxKeyDownForTest(KeyEventArgs e) =>
        CellAddressBox_KeyDown(CellAddressBox, e);

    internal void InsertRawFormulaFunctionForTest(string functionName) =>
        InsertRawFormulaFunction(functionName);

    internal void InsertDefinedNameIntoFormulaForTest(string name) =>
        InsertDefinedNameIntoFormula(name);

    internal void ToggleFormulaBarExpansionForTest() =>
        FormulaBarExpandBtn_Click(FormulaBarExpandBtn, new RoutedEventArgs());

    internal void EditActiveCellInFormulaBarForTest() => EditActiveCellInFormulaBar();

    internal bool TryApplyFormulaRangeSelectionForTest(CellAddress target, bool extendSelection) =>
        TryApplyFormulaRangeSelection(target, extendSelection);

    internal bool TryHandleFormulaSheetTabClickForTest(SheetId sheetId, ModifierKeys modifiers) =>
        TryHandleFormulaSheetTabClick(sheetId, modifiers);

    internal bool TryToggleFormulaRangeEntrySelectionModeForTest(Key key, ModifierKeys modifiers) =>
        TryToggleFormulaRangeEntrySelectionMode(key, modifiers);

    internal void SelectRowForTest(uint row) => SelectRow(row);

    internal void SelectColumnForTest(uint column) => SelectColumn(column);

    internal void AddAdditionalRowSelectionForTest(uint row) => AddAdditionalRowSelection(row);

    internal void SelectAllForTest() => SelectAll();

    internal void PreviewColumnResizeForTest(uint column, double width) => OnColumnResizing(column, width);

    internal void CommitColumnResizeForTest(uint column, double width) => OnColumnResized(column, width);

    internal void AutoFitColumnForTest(uint column) => OnColumnAutoFitRequested(column);

    internal void PreviewRowResizeForTest(uint row, double height) => OnRowResizing(row, height);

    internal void CommitRowResizeForTest(uint row, double height) => OnRowResized(row, height);

    internal void AutoFitRowForTest(uint row) => OnRowAutoFitRequested(row);

    internal void CancelResizePreviewForTest() => OnResizeCanceled();

    internal bool ExecuteUndoForTest() => ExecuteUndo();

    internal void CreateNewWorkbookForTest() => CreateNewWorkbook();

    internal void EnterRibbonKeyTipModeForTest(FreeXRibbonKeyTipInputScope scope) =>
        EnterRibbonKeyTipMode(scope);

    internal void HandleActiveRibbonKeyTipForTest(Key key) => HandleActiveRibbonKeyTip(key);

    internal bool TryHandleDirectRibbonKeyTipForTest(Key key) => TryHandleDirectRibbonKeyTip(key);

    internal bool TryHandleFocusedRibbonKeyboardNavigationForTest(KeyEventArgs e) =>
        TryHandleFocusedRibbonKeyboardNavigation(e);

    internal bool IsInsideRibbonSurfaceForTest(DependencyObject element) => IsInsideRibbonSurface(element);

    internal IReadOnlyList<FrameworkElement> VisibleKeyTipElementsForTest(FreeXRibbonKeyTipInputScope scope) =>
        GetVisibleKeyTipElements(scope).ToList();

    internal FreeXRibbonKeyTipInputSession RibbonKeyTipSessionForTest => _ribbonKeyTipSession;

    internal ContextMenu? ActiveRibbonKeyTipMenuForTest
    {
        get => _activeRibbonKeyTipMenu;
        set => _activeRibbonKeyTipMenu = value;
    }

    internal RecentFilesStore RecentFilesForTest => _recentFiles;

    internal AppOptions OptionsForTest => _options;

    internal void UpdateRecentFilesForTest(string filter = "") => UpdateSsRecentList(filter);

    internal void RefreshSheetProtectionUiForTest() => RefreshSheetProtectionUi();

    internal void HideStartScreenForTest() => HideStartScreen();

    internal void RebuildQuickAccessToolbarForTest() => RebuildQuickAccessToolbar();

    internal void OpenCustomZoomDialogForTest() =>
        ZoomCustomMenuItem_Click(this, new RoutedEventArgs());

    internal void RefreshReviewCommentNoteCommandStatesForTest() =>
        RefreshReviewCommentNoteCommandStates();

    internal void ApplyPivotFieldListLayoutForTest(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotDataFieldModel> dataFields,
        bool forceApply) =>
        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields, forceApply);

    internal void MovePivotFieldToZoneForTest(string caption, PivotFieldBucket targetZone) =>
        MovePivotFieldToZone(caption, targetZone, -1);

    internal void RaiseSheetTabRightClickForTest(object target, MouseButtonEventArgs e) =>
        SheetTab_MouseRightButtonDown(target, e);

    internal void UpdateSheetTabNavigationForTest() => UpdateSheetTabNavigation();

    internal bool TryFocusCurrentSheetTabForTest() => TryFocusCurrentSheetTab();

    internal bool TryOpenFocusedSheetTabContextMenuForTest() =>
        TryOpenFocusedSheetTabContextMenu();

    internal bool TryHandleFocusedSheetTabKeyboardNavigationForTest(KeyEventArgs e) =>
        TryHandleFocusedSheetTabKeyboardNavigation(e);

    internal void RaiseSheetTabContextMenuOpenedForTest(ContextMenu menu) =>
        SheetTabContextMenu_Opened(menu, new RoutedEventArgs(ContextMenu.OpenedEvent, menu));

    internal void SelectAllSheetsFromContextMenuForTest() =>
        SheetCtxSelectAllSheets_Click(this, new RoutedEventArgs());

}
