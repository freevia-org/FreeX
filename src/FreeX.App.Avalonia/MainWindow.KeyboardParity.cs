using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Input;

using FreeX.App.Presentation;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Editing;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    public enum AvaloniaHostShortcut
    {
        CreateTable,
        InsertCurrentDate,
        InsertCurrentTime,
        ToggleOutlineSymbols,
        PasteName,
        NameManager,
        CreateNamesFromSelection,
        SpellCheck,
        RestoreWorkbookWindow,
        MoveWorkbookWindow,
        SizeWorkbookWindow,
        SwitchToNextWorkbookWindow,
        SwitchToPreviousWorkbookWindow,
        MinimizeWorkbookWindow,
        MaximizeOrRestoreWorkbookWindow,
        RebuildDependenciesAndCalculate,
        OpenErrorChecking,
        ToggleFormulaBarExpansion,
        ToggleFilter,
        ReapplyFilter,
        QuickAnalysis,
        InsertEmbeddedChart,
        InsertChartSheet,
        GroupSelection,
        UngroupSelection,
        OpenFormatCellsFont,
        NewNote,
        NewThreadedComment,
        EditInFormulaBar,
        ZoomIn,
        ZoomOut,
        CopyFormulaFromAbove,
        CopyValueFromAbove,
        ScrollActiveCellIntoView,
        CycleSelectionCorner,
        SelectDirectPrecedents,
        SelectDirectDependents,
        SelectAllPrecedents,
        SelectAllDependents,
        ClearSelectionAndEdit,
    }

    internal static bool TryResolveAvaloniaHostShortcutForTest(
        Key key,
        KeyModifiers modifiers,
        out AvaloniaHostShortcut shortcut) =>
        TryResolveAvaloniaHostShortcut(key, modifiers, out shortcut);

    internal bool FormulaBarExpandedForTest => _formulaBarExpanded;

    private static bool TryResolveAvaloniaHostShortcut(
        Key key,
        KeyModifiers modifiers,
        out AvaloniaHostShortcut shortcut)
    {
        var resolved = (key, modifiers) switch
        {
            (Key.T or Key.L, KeyModifiers.Control) => AvaloniaHostShortcut.CreateTable,
            (Key.OemSemicolon, KeyModifiers.Control) => AvaloniaHostShortcut.InsertCurrentDate,
            (Key.OemSemicolon, KeyModifiers.Control | KeyModifiers.Shift) => AvaloniaHostShortcut.InsertCurrentTime,
            (Key.D8, KeyModifiers.Control) => AvaloniaHostShortcut.ToggleOutlineSymbols,
            (Key.F3, KeyModifiers.None) => AvaloniaHostShortcut.PasteName,
            (Key.F3, KeyModifiers.Control) => AvaloniaHostShortcut.NameManager,
            (Key.F3, KeyModifiers.Control | KeyModifiers.Shift) => AvaloniaHostShortcut.CreateNamesFromSelection,
            (Key.F7, KeyModifiers.None) => AvaloniaHostShortcut.SpellCheck,
            (Key.F5, KeyModifiers.Control) => AvaloniaHostShortcut.RestoreWorkbookWindow,
            (Key.F7, KeyModifiers.Control) => AvaloniaHostShortcut.MoveWorkbookWindow,
            (Key.F8, KeyModifiers.Control) => AvaloniaHostShortcut.SizeWorkbookWindow,
            (Key.F6 or Key.Tab, KeyModifiers.Control) => AvaloniaHostShortcut.SwitchToNextWorkbookWindow,
            (Key.F6 or Key.Tab, KeyModifiers.Control | KeyModifiers.Shift) => AvaloniaHostShortcut.SwitchToPreviousWorkbookWindow,
            (Key.F9, KeyModifiers.Control) => AvaloniaHostShortcut.MinimizeWorkbookWindow,
            (Key.F10, KeyModifiers.Control) => AvaloniaHostShortcut.MaximizeOrRestoreWorkbookWindow,
            (Key.F9, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift) => AvaloniaHostShortcut.RebuildDependenciesAndCalculate,
            (Key.F10, KeyModifiers.Alt | KeyModifiers.Shift) => AvaloniaHostShortcut.OpenErrorChecking,
            (Key.U, KeyModifiers.Control | KeyModifiers.Shift) => AvaloniaHostShortcut.ToggleFormulaBarExpansion,
            (Key.L, KeyModifiers.Control | KeyModifiers.Shift) => AvaloniaHostShortcut.ToggleFilter,
            (Key.L, KeyModifiers.Control | KeyModifiers.Alt) => AvaloniaHostShortcut.ReapplyFilter,
            (Key.Q, KeyModifiers.Control) => AvaloniaHostShortcut.QuickAnalysis,
            (Key.F1, KeyModifiers.Alt) => AvaloniaHostShortcut.InsertEmbeddedChart,
            (Key.F11, KeyModifiers.None) => AvaloniaHostShortcut.InsertChartSheet,
            (Key.Right, KeyModifiers.Alt | KeyModifiers.Shift) => AvaloniaHostShortcut.GroupSelection,
            (Key.Left, KeyModifiers.Alt | KeyModifiers.Shift) => AvaloniaHostShortcut.UngroupSelection,
            (Key.F or Key.P, KeyModifiers.Control | KeyModifiers.Shift) => AvaloniaHostShortcut.OpenFormatCellsFont,
            (Key.F2, KeyModifiers.Shift) => AvaloniaHostShortcut.NewNote,
            (Key.F2, KeyModifiers.Control | KeyModifiers.Shift) => AvaloniaHostShortcut.NewThreadedComment,
            (Key.F2, KeyModifiers.Control) => AvaloniaHostShortcut.EditInFormulaBar,
            (Key.OemPlus or Key.Add, KeyModifiers.Control | KeyModifiers.Alt) => AvaloniaHostShortcut.ZoomIn,
            (Key.OemMinus or Key.Subtract, KeyModifiers.Control | KeyModifiers.Alt) => AvaloniaHostShortcut.ZoomOut,
            (Key.OemQuotes, KeyModifiers.Control) => AvaloniaHostShortcut.CopyFormulaFromAbove,
            (Key.OemQuotes, KeyModifiers.Control | KeyModifiers.Shift) => AvaloniaHostShortcut.CopyValueFromAbove,
            (Key.Back, KeyModifiers.Control) => AvaloniaHostShortcut.ScrollActiveCellIntoView,
            (Key.OemPeriod or Key.Decimal, KeyModifiers.Control) => AvaloniaHostShortcut.CycleSelectionCorner,
            (Key.OemOpenBrackets, KeyModifiers.Control) => AvaloniaHostShortcut.SelectDirectPrecedents,
            (Key.OemCloseBrackets, KeyModifiers.Control) => AvaloniaHostShortcut.SelectDirectDependents,
            (Key.OemOpenBrackets, KeyModifiers.Control | KeyModifiers.Shift) => AvaloniaHostShortcut.SelectAllPrecedents,
            (Key.OemCloseBrackets, KeyModifiers.Control | KeyModifiers.Shift) => AvaloniaHostShortcut.SelectAllDependents,
            (Key.Back, KeyModifiers.None or KeyModifiers.Shift) => AvaloniaHostShortcut.ClearSelectionAndEdit,
            _ => (AvaloniaHostShortcut?)null,
        };

        shortcut = resolved.GetValueOrDefault();
        return resolved.HasValue;
    }

    private async Task<bool> TryHandleAvaloniaHostShortcutAsync(KeyEventArgs args)
    {
        if (!TryResolveAvaloniaHostShortcut(args.Key, args.KeyModifiers, out var shortcut))
            return false;

        if (IsTextEditingEventSource(args) &&
            shortcut != AvaloniaHostShortcut.ToggleFormulaBarExpansion)
        {
            return false;
        }

        args.Handled = true;
        switch (shortcut)
        {
            case AvaloniaHostShortcut.CreateTable:
                await InsertTableFromSelectionAsync();
                break;
            case AvaloniaHostShortcut.InsertCurrentDate:
                InsertCurrentDateOrTime(insertTime: false);
                break;
            case AvaloniaHostShortcut.InsertCurrentTime:
                InsertCurrentDateOrTime(insertTime: true);
                break;
            case AvaloniaHostShortcut.ToggleOutlineSymbols:
                ToggleOutlineSymbolsShortcut();
                break;
            case AvaloniaHostShortcut.PasteName:
                await ShowPasteNamesDialogAsync();
                break;
            case AvaloniaHostShortcut.NameManager:
                await ShowNameManagerDialogAsync();
                break;
            case AvaloniaHostShortcut.CreateNamesFromSelection:
                await ShowCreateNamesFromSelectionDialogAsync();
                break;
            case AvaloniaHostShortcut.SpellCheck:
                await ShowSpellingDialogAsync();
                break;
            case AvaloniaHostShortcut.RestoreWorkbookWindow:
                RestoreWorkbookWindow();
                break;
            case AvaloniaHostShortcut.MoveWorkbookWindow:
                BeginNativeWindowCommand(NativeSystemMove);
                break;
            case AvaloniaHostShortcut.SizeWorkbookWindow:
                BeginNativeWindowCommand(NativeSystemSize);
                break;
            case AvaloniaHostShortcut.SwitchToNextWorkbookWindow:
                SwitchWorkbookWindow(forward: true);
                break;
            case AvaloniaHostShortcut.SwitchToPreviousWorkbookWindow:
                SwitchWorkbookWindow(forward: false);
                break;
            case AvaloniaHostShortcut.MinimizeWorkbookWindow:
                WindowState = WindowState.Minimized;
                break;
            case AvaloniaHostShortcut.MaximizeOrRestoreWorkbookWindow:
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            case AvaloniaHostShortcut.RebuildDependenciesAndCalculate:
                CalculateNow();
                break;
            case AvaloniaHostShortcut.OpenErrorChecking:
                await CheckFormulaErrorsAsync();
                break;
            case AvaloniaHostShortcut.ToggleFormulaBarExpansion:
                _formulaBarExpanded = !_formulaBarExpanded;
                ApplyFormulaBarExpansion();
                break;
            case AvaloniaHostShortcut.ToggleFilter:
                ToggleAutoFilter();
                break;
            case AvaloniaHostShortcut.ReapplyFilter:
                ReapplyCurrentFilterSort();
                break;
            case AvaloniaHostShortcut.QuickAnalysis:
                await ShowQuickAnalysisDialogAsync();
                break;
            case AvaloniaHostShortcut.InsertEmbeddedChart:
                InsertChartFromSelection(ChartType.Column);
                break;
            case AvaloniaHostShortcut.InsertChartSheet:
                InsertChartSheetFromSelection();
                break;
            case AvaloniaHostShortcut.GroupSelection:
                GroupSelectedRows();
                break;
            case AvaloniaHostShortcut.UngroupSelection:
                UngroupSelection();
                break;
            case AvaloniaHostShortcut.OpenFormatCellsFont:
                await ShowFormatCellsDialogAsync(initialTabIndex: 2);
                break;
            case AvaloniaHostShortcut.NewNote:
                await ShowNewNoteDialogAsync();
                break;
            case AvaloniaHostShortcut.NewThreadedComment:
                await ShowNewThreadedCommentDialogAsync();
                break;
            case AvaloniaHostShortcut.EditInFormulaBar:
                BeginFormulaEdit(_session.ActiveCell);
                break;
            case AvaloniaHostShortcut.ZoomIn:
                ApplyZoomPercent(_session.ZoomPercent + StatusBarZoomSliderPlanner.ZoomStepPercent, UiText.Get("InsertLoc_ZoomFailed"));
                break;
            case AvaloniaHostShortcut.ZoomOut:
                ApplyZoomPercent(_session.ZoomPercent - StatusBarZoomSliderPlanner.ZoomStepPercent, UiText.Get("InsertLoc_ZoomFailed"));
                break;
            case AvaloniaHostShortcut.CopyFormulaFromAbove:
                CopyFromAbove(CopyFromAboveMode.FormulaOrContent);
                break;
            case AvaloniaHostShortcut.CopyValueFromAbove:
                CopyFromAbove(CopyFromAboveMode.Value);
                break;
            case AvaloniaHostShortcut.ScrollActiveCellIntoView:
                ScrollActiveCellIntoView();
                break;
            case AvaloniaHostShortcut.CycleSelectionCorner:
                CycleSelectionCorner();
                break;
            case AvaloniaHostShortcut.SelectDirectPrecedents:
                SelectFormulaAuditCells(selectDependents: false, includeTransitive: false);
                break;
            case AvaloniaHostShortcut.SelectDirectDependents:
                SelectFormulaAuditCells(selectDependents: true, includeTransitive: false);
                break;
            case AvaloniaHostShortcut.SelectAllPrecedents:
                SelectFormulaAuditCells(selectDependents: false, includeTransitive: true);
                break;
            case AvaloniaHostShortcut.SelectAllDependents:
                SelectFormulaAuditCells(selectDependents: true, includeTransitive: true);
                break;
            case AvaloniaHostShortcut.ClearSelectionAndEdit:
                ClearSelectionAndEdit();
                break;
        }

        return true;
    }

    private void InsertCurrentDateOrTime(bool insertTime)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var target = _session.ActiveCell;
        var value = insertTime
            ? DateTimeEntryService.CurrentTime(DateTime.Now)
            : DateTimeEntryService.CurrentDate(DateTime.Now);
        var result = _session.ExecuteReviewCommand(
            EditCellsCommand.ForValue(_session.ActiveSheet.Id, target, value),
            target);
        RefreshShell(result.Success
            ? insertTime ? "Inserted current time." : "Inserted current date."
            : result.ErrorMessage ?? "Could not insert the current date or time.");
    }

    private void ToggleOutlineSymbolsShortcut()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        var next = !(_session.ActiveSheet.ShowOutlineSymbols ?? true);
        var result = _session.ExecuteReviewCommand(
            new SetWorksheetOutlineSymbolsCommand(_session.ActiveSheet.Id, next));
        if (result.Success)
            _session.SelectRange(range);
        RefreshShell(result.Success
            ? next ? "Showing outline symbols." : "Hiding outline symbols."
            : result.ErrorMessage ?? "Could not change outline symbols.");
    }

    private void CopyFromAbove(CopyFromAboveMode mode)
    {
        var target = _session.ActiveCell;
        if (CopyFromAbovePlanner.CreateEdit(_session.ActiveSheet, target, mode) is not { } edit)
            return;

        var result = _session.ExecuteReviewCommand(
            new EditCellsCommand(_session.ActiveSheet.Id, [edit]),
            target);
        RefreshShell(result.Success
            ? mode == CopyFromAboveMode.Value ? "Copied value from above." : "Copied formula from above."
            : result.ErrorMessage ?? "Could not copy from above.");
    }

    private void InsertChartSheetFromSelection()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var sheet = _session.ActiveSheet;
        var command = ChartInsertionPlanner.BuildChartSheetCommand(
            sheet,
            sheet.Id,
            _session.SelectedRange,
            ChartType.Column,
            "Chart");
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("ChartLoc_InsertChartFailed"));
            return;
        }

        if (command.CreatedSheetId is { } createdSheetId)
            _session.SelectSheet(createdSheetId);
        RefreshShell("Inserted chart sheet.");
    }

    private void SelectFormulaAuditCells(bool selectDependents, bool includeTransitive)
    {
        var activeCell = _session.ActiveCell;
        IReadOnlyList<CellAddress> matches;
        if (includeTransitive)
        {
            var arrows = selectDependents
                ? FormulaAuditingService.GetDependentTraceArrows(_session.Workbook, activeCell)
                : FormulaAuditingService.GetPrecedentTraceArrows(_session.Workbook, activeCell);
            matches = arrows
                .Select(arrow => selectDependents ? arrow.To : arrow.From)
                .ToList();
        }
        else
        {
            matches = selectDependents
                ? FormulaAuditingService.GetDirectDependents(_session.Workbook, activeCell)
                : FormulaAuditingService.GetDirectPrecedents(_session.Workbook, activeCell);
        }

        var plan = FormulaAuditSelectionPlanner.Plan(_session.ActiveSheet.Id, matches);
        if (plan is null)
        {
            var depth = includeTransitive ? "traceable" : "direct";
            RefreshShell(selectDependents ? $"No {depth} dependents" : $"No {depth} precedents");
            return;
        }

        if (plan.TargetSheetId != _session.ActiveSheet.Id)
            _session.SelectSheet(plan.TargetSheetId);
        var ranges = SelectionRangeService.CompressAddresses(plan.Matches);
        _session.SelectRanges(new GridRange(plan.Matches[0], plan.Matches[0]), ranges);
        RefreshShell(selectDependents ? "Selected formula dependents." : "Selected formula precedents.");
    }

    private void ScrollActiveCellIntoView()
    {
        var active = _session.ActiveCell;
        var rowVisible = _session.Viewport.RowMetrics.Any(metric => metric.Row == active.Row);
        var columnVisible = _session.Viewport.ColMetrics.Any(metric => metric.Col == active.Col);
        if (rowVisible && columnVisible)
            return;

        var topRow = rowVisible ? _session.ActiveSheet.ViewTopRow ?? 1 : active.Row;
        var leftColumn = columnVisible ? _session.ActiveSheet.ViewLeftCol ?? 1 : active.Col;
        if (_session.SetViewportOrigin(topRow, leftColumn))
            RefreshShell("Ready");
    }

    private void CycleSelectionCorner()
    {
        var range = _session.SelectedRanges.Count == 1 ? _session.SelectedRanges[0] : _session.SelectedRange;
        var corners = new[]
        {
            range.Start,
            new CellAddress(range.Start.Sheet, range.Start.Row, range.End.Col),
            range.End,
            new CellAddress(range.Start.Sheet, range.End.Row, range.Start.Col),
        }.Distinct().ToList();
        var index = corners.IndexOf(_session.ActiveCell);
        var next = index < 0 ? range.Start : corners[(index + 1) % corners.Count];

        // WorkbookSession currently models the primary selection start as the active cell. Keep the
        // full rectangle in SelectedRanges while making the requested corner the primary active cell.
        _session.SelectRanges(new GridRange(next, next), [range]);
        RefreshShell("Ready");
    }

    private void ClearSelectionAndEdit()
    {
        ClearSelectedRangeContents();
        BeginInlineCellEdit(_session.ActiveCell, string.Empty, 0);
    }

    private void RestoreWorkbookWindow()
    {
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;
    }

    private void SwitchWorkbookWindow(bool forward)
    {
        var windows = AllTopLevelWindows.Where(static window => window.IsVisible).ToList();
        if (windows.Count <= 1)
            return;

        var currentIndex = windows.IndexOf(this);
        if (currentIndex < 0)
            currentIndex = 0;
        var nextIndex = (currentIndex + (forward ? 1 : -1) + windows.Count) % windows.Count;
        var next = windows[nextIndex];
        if (next.WindowState == WindowState.Minimized)
            next.WindowState = WindowState.Normal;
        next.Activate();
    }

    private const uint WindowSystemCommandMessage = 0x0112;
    private const nuint NativeSystemMove = 0xF010;
    private const nuint NativeSystemSize = 0xF000;

    private void BeginNativeWindowCommand(nuint command)
    {
        if (!OperatingSystem.IsWindows() || WindowState != WindowState.Normal)
            return;

        var handle = TryGetPlatformHandle()?.Handle ?? 0;
        if (handle != 0)
            PostMessage(handle, WindowSystemCommandMessage, command, 0);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint windowHandle, uint message, nuint wParam, nint lParam);
}
