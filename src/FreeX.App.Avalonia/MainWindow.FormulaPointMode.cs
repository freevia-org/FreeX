using Avalonia.Input;
using FreeX.App.Presentation;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    public string WorkbookName => _session.Workbook.Name;

    public bool HasActiveFormulaPointMode =>
        GetFormulaRangeEntryEditor() is not null &&
        _formulaRangeEntryMode &&
        _session.FormulaEditAddress is not null;

    public bool AcceptFormulaPointModeSelection(
        FormulaPointModeSelection selection,
        bool append,
        bool extendSelection)
    {
        if (!HasActiveFormulaPointMode)
            return false;

        var externalWorkbookName = selection.WorkbookId == _session.Workbook.Id
            ? null
            : selection.WorkbookName;
        if (append)
        {
            return TryAppendDisjointFormulaPointRange(
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
        if (_session.Workbook.GetSheet(range.Start.Sheet) is null)
            return;

        if (_session.ActiveSheet.Id != range.Start.Sheet)
            _session.SelectSheet(range.Start.Sheet);
        _session.SelectRange(range);
        RefreshShell("Ready");
    }

    public bool CommitOwnedFormulaPointModeEdit()
    {
        if (!HasActiveFormulaPointMode)
            return false;

        if (_inlineCellEditor is not null)
            _formulaBox.Text = _inlineCellEditor.Text;

        var committed = CommitFormulaBox();
        if (committed)
            FocusShellRegion(ShellFocusTarget.Worksheet);
        return committed;
    }

    public bool CancelOwnedFormulaPointModeEdit()
    {
        if (_session.FormulaEditAddress is not { } address)
            return false;

        _session.CancelFormulaEdit();
        _session.SelectCell(address);
        _formulaBoxEditOriginalText = null;
        ClearFormulaRangeEntryState();
        ClearInlineCellEditorState();
        RefreshShell("Ready");
        FocusShellRegion(ShellFocusTarget.Worksheet);
        return true;
    }

    private bool TryRouteFormulaPointModeSelection(
        GridRange range,
        bool append = false,
        bool extendSelection = false)
    {
        var sheet = _session.Workbook.GetSheet(range.Start.Sheet);
        if (sheet is null)
            return false;

        return FormulaPointModeWorkbookResolver.TryRouteSelection(
            WindowRegistry.FormulaPointModeWindows,
            this,
            new FormulaPointModeSelection(_session.Workbook.Id, _session.Workbook.Name, sheet.Name, range),
            append,
            extendSelection);
    }

    private bool TryRouteFormulaPointModeKey(Key key)
    {
        if (HasActiveFormulaPointMode)
            return false;

        return key == Key.Escape
            ? FormulaPointModeWorkbookResolver.TryRouteCancel(WindowRegistry.FormulaPointModeWindows, this)
            : key == Key.Enter
                ? FormulaPointModeWorkbookResolver.TryRouteCommit(WindowRegistry.FormulaPointModeWindows, this)
                : false;
    }

    internal bool RouteFormulaPointSelectionForTest(
        GridRange range,
        bool append = false,
        bool extendSelection = false) =>
        TryRouteFormulaPointModeSelection(range, append, extendSelection);
}
