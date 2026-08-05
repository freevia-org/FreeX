using Avalonia.Input;
using FreeX.App.Presentation;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    public string WorkbookName => _session.Workbook.Name;

    public bool HasActiveFormulaPointMode => FormulaPointModeWorkbookResolver.IsActive(
        GetFormulaRangeEntryEditor() is not null,
        _formulaRangeEditingSession.PointMode,
        _session.FormulaEditAddress is not null);

    public bool AcceptFormulaPointModeSelection(FormulaPointModeEditSelection selection)
    {
        if (!HasActiveFormulaPointMode)
            return false;

        if (selection.Mode == FormulaPointModeSelectionMode.Append)
        {
            return TryAppendDisjointFormulaPointRange(
                selection.Range,
                selection.SheetName,
                selection.ExternalWorkbookName);
        }

        return TryApplyFormulaRangeSelection(
            selection.Range,
            selection.Range.Start,
            selection.Range.End,
            selection.SheetName,
            selection.ExternalWorkbookName);
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

    public bool CycleOwnedFormulaPointModeReference()
    {
        if (!HasActiveFormulaPointMode)
            return false;

        var editor = GetFormulaRangeEntryEditor();
        if (editor is null)
            return false;

        var args = new KeyEventArgs { Key = Key.F4, KeyModifiers = KeyModifiers.None };
        TryHandleFormulaEditorModeOrReferenceCycle(editor, args);
        return args.Handled;
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
        if (HasActiveFormulaPointMode)
            return false;

        var command = key switch
        {
            Key.F4 => FormulaPointModeCommand.CycleReference,
            Key.Escape => FormulaPointModeCommand.Cancel,
            Key.Enter => FormulaPointModeCommand.Commit,
            _ => (FormulaPointModeCommand?)null,
        };
        return command is { } routedCommand &&
               FormulaPointModeWorkbookResolver.TryRouteCommand(
                   WindowRegistry.FormulaPointModeWindows,
                   this,
                   routedCommand);
    }

    internal bool RouteFormulaPointSelectionForTest(
        GridRange range,
        bool append = false,
        bool extendSelection = false) =>
        TryRouteFormulaPointModeSelection(range, append, extendSelection);
}
