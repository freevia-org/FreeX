using Avalonia.Input;
using FreeX.App.Presentation;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    public string WorkbookName => _session.Workbook.Name;

    public bool HasActiveFormulaPointMode => _formulaRangeEditingSession.IsPointModeActive(
        GetFormulaRangeEntryEditor() is not null,
        _session.FormulaEditAddress is not null);

    public bool AcceptFormulaPointModeSelection(FormulaPointModeEditSelection selection) =>
        _formulaRangeEditingSession.TryApplyPointModeSelection(
            selection,
            GetFormulaRangeEntryEditor() is not null,
            _session.FormulaEditAddress is not null,
            append => TryAppendDisjointFormulaPointRange(
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
        if (!_session.SelectFormulaPointModeSourceRange(range))
            return;

        RefreshShell(UiText.Get("MainLoc_Ready"));
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
        RefreshShell(UiText.Get("MainLoc_Ready"));
        FocusShellRegion(ShellFocusTarget.Worksheet);
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
        if (!FormulaPointModeWorkbookResolver.TryCreateSelection(
                _session.Workbook,
                range,
                out var selection))
            return false;

        return FormulaPointModeWorkbookResolver.TryRouteSelection(
            WindowRegistry.FormulaPointModeWindows,
            this,
            selection,
            append,
            extendSelection);
    }

    private bool TryRouteFormulaPointModeKey(Key key)
    {
        // A local edit owns its Enter/Escape/F4 lifecycle even when it is in Edit mode rather
        // than Point mode. Only a window with no edit of its own may forward these keys.
        if (_session.FormulaEditAddress is not null)
            return false;

        var command = _formulaRangeEditingSession.GetRoutedPointModeCommand(
            FormulaBarAvaloniaInputAdapter.ToFormulaEditorKey(key),
            GetFormulaRangeEntryEditor() is not null,
            _session.FormulaEditAddress is not null);
        return command is { } routedCommand &&
               FormulaPointModeWorkbookResolver.TryRouteCommand(
                   WindowRegistry.FormulaPointModeWindows,
                   this,
                   routedCommand);
    }
}
