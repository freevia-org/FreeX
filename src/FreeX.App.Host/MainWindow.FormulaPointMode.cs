using System.Windows.Input;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    public string WorkbookName => _workbook.Name;

    public bool HasActiveFormulaPointMode => _formulaRangeEditingSession.IsPointModeActive(
        GetFormulaRangeEntryEditor() is not null,
        _formulaEditCell is not null);

    public bool AcceptFormulaPointModeSelection(FormulaPointModeEditSelection selection) =>
        _formulaRangeEditingSession.TryApplyPointModeSelection(
            selection,
            GetFormulaRangeEntryEditor() is not null,
            _formulaEditCell is not null,
            append => TryAppendDisjointFormulaRangeReference(
                append.Range,
                append.SheetName,
                append.ExternalWorkbookName),
            replace => TryApplyFormulaRangeSelection(
                replace.Range,
                replace.Range.Start,
                replace.Range.End,
                replace.SheetName,
                replace.ExternalWorkbookName));

    public void ShowFormulaPointModeSourceSelection(GridRange range)
    {
        SynchronizeWorkbookSessionSelection();
        var previousSheetId = _currentSheetId;
        if (!_session.SelectFormulaPointModeSourceRange(range))
            return;

        _currentSheetId = _session.ActiveSheet.Id;
        if (!previousSheetId.Equals(_currentSheetId))
        {
            SelectSingleSheetTab(_currentSheetId);
            UpdateViewport();
            RefreshSheetTabs();
        }

        _selectionAnchor = _session.ActiveCell;
        _selectionCursor = _session.SelectedRange.End;
        SetSelectedRangesIfChanged(null);
        SheetGrid.SelectedRange = _session.SelectedRange;
        CellAddressBox.Text = FormatRangeReference(
            _session.SelectedRange.Start,
            _session.SelectedRange.End);
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
        if (!FormulaPointModeWorkbookResolver.TryCreateSelection(_workbook, range, out var selection))
            return false;

        return FormulaPointModeWorkbookResolver.TryRouteSelection(
            _windowRegistry?.FormulaPointModeWindows ?? [],
            this,
            selection,
            append,
            extendSelection);
    }

    private bool TryRouteFormulaPointModeKey(Key key)
    {
        var command = _formulaRangeEditingSession.GetRoutedPointModeCommand(
            FormulaBarWpfInputAdapter.ToFormulaEditorKey(key),
            GetFormulaRangeEntryEditor() is not null,
            _formulaEditCell is not null);
        return command is { } routedCommand &&
               FormulaPointModeWorkbookResolver.TryRouteCommand(
                   _windowRegistry?.FormulaPointModeWindows ?? [],
                   this,
                   routedCommand);
    }

}
