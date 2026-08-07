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
        CloseWorkbook,
        SelectCurrentRegion,
        ToggleExtendSelection,
        ToggleAddSelection,
        InsertCells,
        DeleteCells,
        OpenActiveDropdown,
    }

    internal readonly record struct AvaloniaHostShortcutRule(
        Key Key,
        KeyModifiers Modifiers,
        AvaloniaHostShortcut Shortcut);

    internal static IReadOnlyList<AvaloniaHostShortcutRule> AvaloniaHostShortcutRules { get; } =
    [
        R(Key.T, Ctrl, AvaloniaHostShortcut.CreateTable),
        R(Key.L, Ctrl, AvaloniaHostShortcut.CreateTable),
        R(Key.OemSemicolon, Ctrl, AvaloniaHostShortcut.InsertCurrentDate),
        R(Key.OemSemicolon, CtrlShift, AvaloniaHostShortcut.InsertCurrentTime),
        R(Key.D8, Ctrl, AvaloniaHostShortcut.ToggleOutlineSymbols),
        R(Key.F3, None, AvaloniaHostShortcut.PasteName),
        R(Key.F3, Ctrl, AvaloniaHostShortcut.NameManager),
        R(Key.F3, CtrlShift, AvaloniaHostShortcut.CreateNamesFromSelection),
        R(Key.F7, None, AvaloniaHostShortcut.SpellCheck),
        R(Key.F5, Ctrl, AvaloniaHostShortcut.RestoreWorkbookWindow),
        R(Key.F7, Ctrl, AvaloniaHostShortcut.MoveWorkbookWindow),
        R(Key.F8, Ctrl, AvaloniaHostShortcut.SizeWorkbookWindow),
        R(Key.F6, Ctrl, AvaloniaHostShortcut.SwitchToNextWorkbookWindow),
        R(Key.Tab, Ctrl, AvaloniaHostShortcut.SwitchToNextWorkbookWindow),
        R(Key.F6, CtrlShift, AvaloniaHostShortcut.SwitchToPreviousWorkbookWindow),
        R(Key.Tab, CtrlShift, AvaloniaHostShortcut.SwitchToPreviousWorkbookWindow),
        R(Key.F9, Ctrl, AvaloniaHostShortcut.MinimizeWorkbookWindow),
        R(Key.F10, Ctrl, AvaloniaHostShortcut.MaximizeOrRestoreWorkbookWindow),
        R(Key.F9, CtrlAltShift, AvaloniaHostShortcut.RebuildDependenciesAndCalculate),
        R(Key.F10, AltShift, AvaloniaHostShortcut.OpenErrorChecking),
        R(Key.U, CtrlShift, AvaloniaHostShortcut.ToggleFormulaBarExpansion),
        R(Key.L, CtrlShift, AvaloniaHostShortcut.ToggleFilter),
        R(Key.L, CtrlAlt, AvaloniaHostShortcut.ReapplyFilter),
        R(Key.Q, Ctrl, AvaloniaHostShortcut.QuickAnalysis),
        R(Key.F1, Alt, AvaloniaHostShortcut.InsertEmbeddedChart),
        R(Key.F11, None, AvaloniaHostShortcut.InsertChartSheet),
        R(Key.Right, AltShift, AvaloniaHostShortcut.GroupSelection),
        R(Key.Left, AltShift, AvaloniaHostShortcut.UngroupSelection),
        R(Key.F, CtrlShift, AvaloniaHostShortcut.OpenFormatCellsFont),
        R(Key.P, CtrlShift, AvaloniaHostShortcut.OpenFormatCellsFont),
        R(Key.F2, Shift, AvaloniaHostShortcut.NewNote),
        R(Key.F2, CtrlShift, AvaloniaHostShortcut.NewThreadedComment),
        R(Key.F2, Ctrl, AvaloniaHostShortcut.EditInFormulaBar),
        R(Key.OemPlus, CtrlAlt, AvaloniaHostShortcut.ZoomIn),
        R(Key.Add, CtrlAlt, AvaloniaHostShortcut.ZoomIn),
        R(Key.OemMinus, CtrlAlt, AvaloniaHostShortcut.ZoomOut),
        R(Key.Subtract, CtrlAlt, AvaloniaHostShortcut.ZoomOut),
        R(Key.OemQuotes, Ctrl, AvaloniaHostShortcut.CopyFormulaFromAbove),
        R(Key.OemQuotes, CtrlShift, AvaloniaHostShortcut.CopyValueFromAbove),
        R(Key.Back, Ctrl, AvaloniaHostShortcut.ScrollActiveCellIntoView),
        R(Key.OemPeriod, Ctrl, AvaloniaHostShortcut.CycleSelectionCorner),
        R(Key.Decimal, Ctrl, AvaloniaHostShortcut.CycleSelectionCorner),
        R(Key.OemOpenBrackets, Ctrl, AvaloniaHostShortcut.SelectDirectPrecedents),
        R(Key.OemCloseBrackets, Ctrl, AvaloniaHostShortcut.SelectDirectDependents),
        R(Key.OemOpenBrackets, CtrlShift, AvaloniaHostShortcut.SelectAllPrecedents),
        R(Key.OemCloseBrackets, CtrlShift, AvaloniaHostShortcut.SelectAllDependents),
        R(Key.Back, None, AvaloniaHostShortcut.ClearSelectionAndEdit),
        R(Key.Back, Shift, AvaloniaHostShortcut.ClearSelectionAndEdit),
        R(Key.F4, Ctrl, AvaloniaHostShortcut.CloseWorkbook),
        R(Key.D8, CtrlShift, AvaloniaHostShortcut.SelectCurrentRegion),
        R(Key.F8, None, AvaloniaHostShortcut.ToggleExtendSelection),
        R(Key.F8, Shift, AvaloniaHostShortcut.ToggleAddSelection),
        R(Key.OemPlus, Ctrl, AvaloniaHostShortcut.InsertCells),
        R(Key.OemPlus, CtrlShift, AvaloniaHostShortcut.InsertCells),
        R(Key.OemMinus, Ctrl, AvaloniaHostShortcut.DeleteCells),
        R(Key.Down, Alt, AvaloniaHostShortcut.OpenActiveDropdown),
    ];

    internal static bool TryResolveAvaloniaHostShortcutForTest(
        Key key,
        KeyModifiers modifiers,
        out AvaloniaHostShortcut shortcut) =>
        TryResolveAvaloniaHostShortcut(key, modifiers, out shortcut);

    internal bool FormulaBarExpandedForTest => _formulaBarExpanded;
    internal ExcelSelectionMode KeyboardSelectionModeForTest => _keyboardSelectionMode;

    private ExcelSelectionMode _keyboardSelectionMode;

    private static bool TryResolveAvaloniaHostShortcut(
        Key key,
        KeyModifiers modifiers,
        out AvaloniaHostShortcut shortcut)
    {
        foreach (var rule in AvaloniaHostShortcutRules)
        {
            if (rule.Key != key || rule.Modifiers != modifiers)
                continue;

            shortcut = rule.Shortcut;
            return true;
        }

        shortcut = default;
        return false;
    }

    private static AvaloniaHostShortcutRule R(
        Key key,
        KeyModifiers modifiers,
        AvaloniaHostShortcut shortcut) => new(key, modifiers, shortcut);

    private bool TryHandleWorkbookWindowSwitchShortcut(KeyEventArgs args)
    {
        if (args.Key is not (Key.F6 or Key.Tab) ||
            args.KeyModifiers is not (KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift)))
        {
            return false;
        }

        args.Handled = true;
        SwitchWorkbookWindow(forward: args.KeyModifiers == KeyModifiers.Control);
        return true;
    }

    private const KeyModifiers None = KeyModifiers.None;
    private const KeyModifiers Ctrl = KeyModifiers.Control;
    private const KeyModifiers Shift = KeyModifiers.Shift;
    private const KeyModifiers Alt = KeyModifiers.Alt;
    private const KeyModifiers CtrlShift = KeyModifiers.Control | KeyModifiers.Shift;
    private const KeyModifiers CtrlAlt = KeyModifiers.Control | KeyModifiers.Alt;
    private const KeyModifiers AltShift = KeyModifiers.Alt | KeyModifiers.Shift;
    private const KeyModifiers CtrlAltShift = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift;

    private async Task<bool> TryHandleAvaloniaHostShortcutAsync(KeyEventArgs args)
    {
        if (TryHandleFocusedEditorShortcut(args))
            return true;

        if (_keyboardSelectionMode != ExcelSelectionMode.Normal &&
            args.Key == Key.Escape &&
            args.KeyModifiers == KeyModifiers.None)
        {
            _keyboardSelectionMode = ExcelSelectionMode.Normal;
            args.Handled = true;
            RefreshShell(UiText.Get(ExcelSelectionModePlanner.StatusBarModeResourceKey(_keyboardSelectionMode)));
            return true;
        }

        if (TryHandleStickySelectionNavigation(args))
            return true;

        // Route the shared workbook catalog before the worksheet-navigation fallback. This is what
        // lets non-command-modifier aliases such as Shift+F12, Shift+Insert, and Alt+Backspace reach
        // the same production handlers as Ctrl+S, Ctrl+V, and Ctrl+Z.
        if (!IsTextEditingEventSource(args) && await TryHandleWorkbookShortcutRouteAsync(args))
            return true;

        // WPF keeps plain F12 as a local Save As command, separate from the shared Shift+F12
        // Save route. X11 may report Shift+F12 as the logical F24 key, so use the physical-key
        // normalization here as well as in the shared route dispatcher.
        if (!IsTextEditingEventSource(args) &&
            GetEffectiveWorkbookShortcutKey(args) == Key.F12 &&
            args.KeyModifiers == KeyModifiers.None)
        {
            args.Handled = true;
            await SaveWorkbookAsAsync();
            return true;
        }

        if (!TryResolveAvaloniaHostShortcut(args.Key, args.KeyModifiers, out var shortcut))
            return false;

        var isWorkbookWindowSwitch = shortcut is
            AvaloniaHostShortcut.SwitchToNextWorkbookWindow or
            AvaloniaHostShortcut.SwitchToPreviousWorkbookWindow;
        if (IsTextEditingEventSource(args) &&
            shortcut != AvaloniaHostShortcut.ToggleFormulaBarExpansion &&
            !isWorkbookWindowSwitch)
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
            case AvaloniaHostShortcut.CloseWorkbook:
                await CloseWorkbookAsync();
                break;
            case AvaloniaHostShortcut.SelectCurrentRegion:
                SelectCurrentRegionOrAll();
                break;
            case AvaloniaHostShortcut.ToggleExtendSelection:
                ToggleKeyboardSelectionMode(ExcelWorksheetNavigationModifiers.None);
                break;
            case AvaloniaHostShortcut.ToggleAddSelection:
                ToggleKeyboardSelectionMode(ExcelWorksheetNavigationModifiers.Shift);
                break;
            case AvaloniaHostShortcut.InsertCells:
                await ShowInsertCellsDialogAsync();
                break;
            case AvaloniaHostShortcut.DeleteCells:
                await ShowDeleteCellsDialogAsync();
                break;
            case AvaloniaHostShortcut.OpenActiveDropdown:
                OpenActiveDropdown();
                break;
        }

        return true;
    }

    internal static Key GetEffectiveWorkbookShortcutKeyForTest(
        Key key,
        PhysicalKey physicalKey) =>
        physicalKey == PhysicalKey.F12 ? Key.F12 : key;

    private static Key GetEffectiveWorkbookShortcutKey(KeyEventArgs args) =>
        GetEffectiveWorkbookShortcutKeyForTest(args.Key, args.PhysicalKey);

    private bool TryHandleFocusedEditorShortcut(KeyEventArgs args)
    {
        if (args.Key is not (Key.Escape or Key.F4 or Key.F8 or Key.Enter) ||
            args.Key == Key.Enter && args.KeyModifiers != KeyModifiers.Alt)
        {
            return false;
        }

        if (_inlineCellEditor is { } editor && _inlineCellEditAddress is { } address)
        {
            InlineCellEditor_KeyDown(address, editor, args);
            if (!args.Handled && args.Key == Key.F4 && args.KeyModifiers == KeyModifiers.None)
                args.Handled = true;
            return args.Handled;
        }

        if (!_formulaBox.IsFocused)
            return false;

        FormulaBox_KeyDown(_formulaBox, args);
        if (!args.Handled && args.Key == Key.F4 && args.KeyModifiers == KeyModifiers.None)
            args.Handled = true;
        return args.Handled;
    }

    private void ToggleKeyboardSelectionMode(ExcelWorksheetNavigationModifiers modifiers)
    {
        if (!ExcelSelectionModePlanner.TryToggle(
                ExcelSelectionKey.F8,
                modifiers,
                _keyboardSelectionMode,
                out var next))
        {
            return;
        }

        _keyboardSelectionMode = next;
        RefreshShell(UiText.Get(ExcelSelectionModePlanner.StatusBarModeResourceKey(next)));
    }

    private bool TryHandleStickySelectionNavigation(KeyEventArgs args)
    {
        if (_keyboardSelectionMode == ExcelSelectionMode.Normal ||
            args.Key is not (
                Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown) ||
            args.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return false;
        }

        var previousRanges = _keyboardSelectionMode == ExcelSelectionMode.Add
            ? _session.SelectedRanges.ToArray()
            : [];
        var navigationArgs = new KeyEventArgs
        {
            Key = args.Key,
            KeyModifiers = _keyboardSelectionMode == ExcelSelectionMode.Extend
                ? args.KeyModifiers | KeyModifiers.Shift
                : args.KeyModifiers,
        };
        NavigateActiveCell(navigationArgs);
        if (!navigationArgs.Handled)
            return false;

        if (_keyboardSelectionMode == ExcelSelectionMode.Add)
        {
            var addedRange = _session.SelectedRange;
            var ranges = previousRanges
                .Append(addedRange)
                .Distinct()
                .ToArray();
            _session.SelectRanges(addedRange, ranges);
        }

        args.Handled = true;
        RefreshShell(UiText.Get(ExcelSelectionModePlanner.StatusBarModeResourceKey(_keyboardSelectionMode)));
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

        // Keep the full rectangle selected while moving only the active cell to the requested corner --
        // matching Excel and WPF's CycleSelectionCorner. The old form passed a 1x1 GridRange(next, next)
        // as the primary range, collapsing SelectedRange to a single cell even though the full rectangle
        // survived in SelectedRanges. Every consumer that reads the primary SelectedRange rather than
        // SelectedRanges then saw just one cell: Define Name, Insert Chart and Conditional Format would
        // target the corner cell, and multi-cell command gating (SelectedRange.RowCount/ColCount/
        // CellCount, e.g. Merge/Sort enablement) would misreport the selection as 1x1.
        _session.SelectRanges(range, [range], next);
        RefreshShell("Ready");
    }

    /// <summary>
    /// R75-commands-clear-delete-4-1: Backspace clears ONLY the active cell (via
    /// WorkbookSession.ClearActiveCellContents) then enters edit -- unlike the Delete-key/ribbon
    /// "Clear Contents" path (ClearSelectedRangeContents in MainWindow.cs), which clears the whole
    /// selection. Excel's Backspace is never a bulk-clear operation, and a multi-cell selection's
    /// shape must survive it.
    ///
    /// R124-model-drawing-backspace-avalonia-1: mirrors the WPF host's R123-model-drawing-backspace-1
    /// fix (MainWindow.KeyboardCommands.cs), which was never ported to this shell. When a picture/
    /// shape/text box/chart is genuinely selected (_selectedDrawingObjectKind/_selectedDrawingObjectId
    /// -- the same state TryDeleteSelectedDrawingObject checks for the Delete key), Backspace must be
    /// a total no-op: Excel never deletes the object (Delete-only), and never touches whatever cell
    /// happened to be active before the object was clicked. Without this guard, the calls below
    /// silently clear that unrelated cell and open it for edit -- and BeginInlineCellEdit's own
    /// ClearSelectedDrawingObject() call even deselects the object out from under the user.
    /// </summary>
    private void ClearSelectionAndEdit()
    {
        if (_selectedDrawingObjectKind is not null && _selectedDrawingObjectId is not null)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var clearResult = _session.ClearActiveCellContents();
        // R127C-avalonia-clipboard-marquee-backspace-1: WorkbookSession.ClearActiveCellContents
        // already retires the SESSION-level pending Copy/Cut on success (CancelPendingCutAfterMutatingEdit),
        // but this shell's own marching-ants overlay is separate UI-only state that BeginInlineCellEdit
        // does not touch -- clear it here too, matching the ordinary-edit gap closed for
        // ClearSelectedRangeContents (MainWindow.cs) and the WPF host's TryExecuteEditCells path.
        if (clearResult.Success)
            SetClipboardMarquee(null, isCut: false);
        BeginInlineCellEdit(_session.ActiveCell, string.Empty, 0);
    }

    private void RestoreWorkbookWindow()
    {
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;
    }

    private void SwitchWorkbookWindow(bool forward)
    {
        WindowRegistry.SwitchToWindow(this, forward);
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

    private static readonly Lazy<PostMessageDelegate?> PostMessageNative = new(CreatePostMessageDelegate);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool PostMessageDelegate(nint windowHandle, uint message, nuint wParam, nint lParam);

    private static bool PostMessage(nint windowHandle, uint message, nuint wParam, nint lParam) =>
        PostMessageNative.Value?.Invoke(windowHandle, message, wParam, lParam) == true;

    private static PostMessageDelegate? CreatePostMessageDelegate()
    {
        if (!OperatingSystem.IsWindows() ||
            !NativeLibrary.TryLoad("user32.dll", out var libraryHandle) ||
            !NativeLibrary.TryGetExport(libraryHandle, "PostMessageW", out var functionPointer))
        {
            return null;
        }

        return Marshal.GetDelegateForFunctionPointer<PostMessageDelegate>(functionPointer);
    }
}
