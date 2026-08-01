using System.Windows.Input;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    public string WorkbookName => _workbook.Name;

    public bool HasActiveFormulaPointMode =>
        GetFormulaRangeEntryEditor() is not null &&
        _formulaRangeEntryMode &&
        _formulaEditCell is not null;

    public bool AcceptFormulaPointModeSelection(
        FormulaPointModeSelection selection,
        bool append,
        bool extendSelection)
    {
        if (!HasActiveFormulaPointMode)
            return false;

        var externalWorkbookName = selection.WorkbookId == _workbook.Id
            ? null
            : selection.WorkbookName;
        if (append)
        {
            return TryAppendDisjointFormulaRangeReference(
                selection.Range,
                selection.SheetName,
                externalWorkbookName);
        }

        return TryApplyFormulaRangeSelection(
            selection.Range,
            selection.Range.Start,
            selection.Range.End,
            selection.SheetName,
            externalWorkbookName);
    }

    public void ShowFormulaPointModeSourceSelection(GridRange range)
    {
        if (_workbook.GetSheet(range.Start.Sheet) is null)
            return;

        if (_currentSheetId != range.Start.Sheet)
        {
            _currentSheetId = range.Start.Sheet;
            SelectSingleSheetTab(_currentSheetId);
            UpdateViewport();
            RefreshSheetTabs();
        }

        _selectionAnchor = range.Start;
        _selectionCursor = range.End;
        SetSelectedRangesIfChanged(null);
        SheetGrid.SelectedRange = range;
        CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
        RefreshStatusBar();
        SheetGrid.Focus();
    }

    public bool CommitOwnedFormulaPointModeEdit()
    {
        if (!HasActiveFormulaPointMode)
            return false;

        if (_inlineEditor?.IsVisible == true)
            FormulaBar.Text = _inlineEditor.Text;

        if (!CommitEdit())
            return false;

        HideInlineEditor(commit: false);
        ClearFormulaRangeEntryState();
        FocusSheetGridIfNeeded();
        return true;
    }

    public bool CancelOwnedFormulaPointModeEdit()
    {
        if (_formulaEditCell is not { } address)
            return false;

        var cell = _workbook.GetSheet(address.Sheet)?.GetCell(address);
        FormulaBar.Text = FormatFormulaBarText(cell, address);
        RestoreFormulaEditCellSelection(address);
        HideInlineEditor(commit: false);
        ClearFormulaRangeEntryState();
        RefreshStatusBar();
        ClearClipboardVisualState();
        FocusSheetGridIfNeeded();
        return true;
    }

    public bool CycleOwnedFormulaPointModeReference()
    {
        if (!HasActiveFormulaPointMode)
            return false;

        var editor = GetFormulaRangeEntryEditor();
        return editor is not null && TryCycleFormulaReference(editor);
    }

    private bool TryRouteFormulaPointModeSelection(
        GridRange range,
        bool append = false,
        bool extendSelection = false)
    {
        var sheet = _workbook.GetSheet(range.Start.Sheet);
        if (sheet is null)
            return false;

        return FormulaPointModeWorkbookResolver.TryRouteSelection(
            _windowRegistry?.FormulaPointModeWindows ?? [],
            this,
            new FormulaPointModeSelection(_workbook.Id, _workbook.Name, sheet.Name, range),
            append,
            extendSelection);
    }

    private bool TryRouteFormulaPointModeKey(Key key)
    {
        if (HasActiveFormulaPointMode)
            return false;

        var windows = _windowRegistry?.FormulaPointModeWindows ?? [];
        return key == Key.F4
            ? FormulaPointModeWorkbookResolver.TryRouteReferenceCycle(windows, this)
            : key == Key.Escape
            ? FormulaPointModeWorkbookResolver.TryRouteCancel(windows, this)
            : key == Key.Enter
                ? FormulaPointModeWorkbookResolver.TryRouteCommit(windows, this)
                : false;
    }

    internal string FormulaBoxTextForTest
    {
        get => FormulaBar.Text;
        set => FormulaBar.Text = value;
    }

    internal void BeginFormulaPointModeEditForTest(CellAddress address, string formulaText)
    {
        if (!FormulaEditInteractionPlanner.IsFormulaText(formulaText))
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
}
