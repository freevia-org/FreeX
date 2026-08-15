using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Input;

using FreeX.App.Presentation;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal enum AvaloniaLocalShortcut
    {
        SelectCurrentRegion,
        ToggleExtendSelection,
        ToggleAddSelection,
        InsertCells,
        DeleteCells,
    }

    internal readonly record struct AvaloniaLocalShortcutRule(
        Key Key,
        KeyModifiers Modifiers,
        AvaloniaLocalShortcut Shortcut);

    internal static IReadOnlyList<AvaloniaLocalShortcutRule> AvaloniaLocalShortcutRules { get; } =
    [
        R(Key.D8, CtrlShift, AvaloniaLocalShortcut.SelectCurrentRegion),
        R(Key.F8, None, AvaloniaLocalShortcut.ToggleExtendSelection),
        R(Key.F8, Shift, AvaloniaLocalShortcut.ToggleAddSelection),
        R(Key.OemPlus, Ctrl, AvaloniaLocalShortcut.InsertCells),
        R(Key.OemPlus, CtrlShift, AvaloniaLocalShortcut.InsertCells),
        R(Key.OemMinus, Ctrl, AvaloniaLocalShortcut.DeleteCells),
    ];

    private ExcelSelectionMode _keyboardSelectionMode;

    private static bool TryResolveApplicationShortcut(
        Key key,
        KeyModifiers modifiers,
        out KeyboardCommandShortcut shortcut)
    {
        shortcut = default;
        return TryGetWorkbookShortcutKey(key, out var shortcutKey) &&
            WorkbookKeyboardShortcutCatalog.TryGetApplicationCommand(
            shortcutKey,
            ToWorkbookShortcutModifiers(modifiers),
            out shortcut);
    }

    private static AvaloniaLocalShortcutRule R(
        Key key,
        KeyModifiers modifiers,
        AvaloniaLocalShortcut shortcut) => new(key, modifiers, shortcut);

    private static bool TryResolveAvaloniaLocalShortcut(
        Key key,
        KeyModifiers modifiers,
        out AvaloniaLocalShortcut shortcut)
    {
        foreach (var rule in AvaloniaLocalShortcutRules)
        {
            if (rule.Key != key || rule.Modifiers != modifiers)
                continue;

            shortcut = rule.Shortcut;
            return true;
        }

        shortcut = default;
        return false;
    }

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

    private async Task<bool> TryHandleApplicationOrLocalShortcutAsync(KeyEventArgs args)
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

        if (!TryResolveApplicationShortcut(args.Key, args.KeyModifiers, out var shortcut))
        {
            if (!TryResolveAvaloniaLocalShortcut(args.Key, args.KeyModifiers, out var localShortcut) ||
                IsTextEditingEventSource(args))
            {
                return false;
            }

            args.Handled = true;
            switch (localShortcut)
            {
                case AvaloniaLocalShortcut.SelectCurrentRegion:
                    SelectCurrentRegionOrAll();
                    break;
                case AvaloniaLocalShortcut.ToggleExtendSelection:
                    ToggleKeyboardSelectionMode(ExcelWorksheetNavigationModifiers.None);
                    break;
                case AvaloniaLocalShortcut.ToggleAddSelection:
                    ToggleKeyboardSelectionMode(ExcelWorksheetNavigationModifiers.Shift);
                    break;
                case AvaloniaLocalShortcut.InsertCells:
                    await ShowInsertCellsDialogAsync();
                    break;
                case AvaloniaLocalShortcut.DeleteCells:
                    await ShowDeleteCellsDialogAsync();
                    break;
            }

            return true;
        }

        var isWorkbookWindowSwitch = shortcut is
            KeyboardCommandShortcut.SwitchToNextWorkbookWindow or
            KeyboardCommandShortcut.SwitchToPreviousWorkbookWindow;
        if (IsTextEditingEventSource(args) &&
            shortcut != KeyboardCommandShortcut.ToggleFormulaBarExpansion &&
            !isWorkbookWindowSwitch)
        {
            return false;
        }

        args.Handled = true;
        switch (shortcut)
        {
            case KeyboardCommandShortcut.CreateTable:
                await InsertTableFromSelectionAsync();
                break;
            case KeyboardCommandShortcut.InsertCurrentDate:
                InsertCurrentDateOrTime(insertTime: false);
                break;
            case KeyboardCommandShortcut.InsertCurrentTime:
                InsertCurrentDateOrTime(insertTime: true);
                break;
            case KeyboardCommandShortcut.ToggleOutlineSymbols:
                ToggleOutlineSymbolsShortcut();
                break;
            case KeyboardCommandShortcut.PasteName:
                await ShowPasteNamesDialogAsync();
                break;
            case KeyboardCommandShortcut.NameManager:
                await ShowNameManagerDialogAsync();
                break;
            case KeyboardCommandShortcut.CreateNamesFromSelection:
                await ShowCreateNamesFromSelectionDialogAsync();
                break;
            case KeyboardCommandShortcut.SpellCheck:
                await ShowSpellingDialogAsync();
                break;
            case KeyboardCommandShortcut.RestoreWorkbookWindow:
                RestoreWorkbookWindow();
                break;
            case KeyboardCommandShortcut.MoveWorkbookWindow:
                BeginNativeWindowCommand(NativeSystemMove);
                break;
            case KeyboardCommandShortcut.SizeWorkbookWindow:
                BeginNativeWindowCommand(NativeSystemSize);
                break;
            case KeyboardCommandShortcut.SwitchToNextWorkbookWindow:
                SwitchWorkbookWindow(forward: true);
                break;
            case KeyboardCommandShortcut.SwitchToPreviousWorkbookWindow:
                SwitchWorkbookWindow(forward: false);
                break;
            case KeyboardCommandShortcut.MinimizeWorkbookWindow:
                WindowState = WindowState.Minimized;
                break;
            case KeyboardCommandShortcut.MaximizeOrRestoreWorkbookWindow:
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            case KeyboardCommandShortcut.RebuildDependenciesAndCalculate:
                CalculateFull();
                break;
            case KeyboardCommandShortcut.OpenErrorChecking:
                await CheckFormulaErrorsAsync();
                break;
            case KeyboardCommandShortcut.ToggleFormulaBarExpansion:
                _formulaBarExpanded = !_formulaBarExpanded;
                ApplyFormulaBarExpansion();
                break;
            case KeyboardCommandShortcut.ToggleFilter:
                ToggleAutoFilter();
                break;
            case KeyboardCommandShortcut.ReapplyFilter:
                ReapplyCurrentFilterSort();
                break;
            case KeyboardCommandShortcut.QuickAnalysis:
                await ShowQuickAnalysisDialogAsync();
                break;
            case KeyboardCommandShortcut.InsertEmbeddedChart:
                InsertChartFromSelection(ChartType.Column);
                break;
            case KeyboardCommandShortcut.InsertChartSheet:
                InsertChartSheetFromSelection();
                break;
            case KeyboardCommandShortcut.GroupSelection:
                GroupSelectedRows();
                break;
            case KeyboardCommandShortcut.UngroupSelection:
                UngroupSelection();
                break;
            case KeyboardCommandShortcut.OpenFormatCellsFont:
                await ShowFormatCellsDialogAsync(initialTabIndex: 2);
                break;
            case KeyboardCommandShortcut.NewNote:
                await ShowNewNoteDialogAsync();
                break;
            case KeyboardCommandShortcut.NewThreadedComment:
                await ShowNewThreadedCommentDialogAsync();
                break;
            case KeyboardCommandShortcut.EditInFormulaBar:
                BeginFormulaEdit(_session.ActiveCell);
                break;
            case KeyboardCommandShortcut.ZoomIn:
                ApplyZoomPercent(_session.ZoomPercent + StatusBarZoomSliderPlanner.ZoomStepPercent, UiText.Get("InsertLoc_ZoomFailed"));
                break;
            case KeyboardCommandShortcut.ZoomOut:
                ApplyZoomPercent(_session.ZoomPercent - StatusBarZoomSliderPlanner.ZoomStepPercent, UiText.Get("InsertLoc_ZoomFailed"));
                break;
            case KeyboardCommandShortcut.CopyFormulaFromAbove:
                CopyFromAbove(CopyFromAboveMode.FormulaOrContent);
                break;
            case KeyboardCommandShortcut.CopyValueFromAbove:
                CopyFromAbove(CopyFromAboveMode.Value);
                break;
            case KeyboardCommandShortcut.ScrollActiveCellIntoView:
                ScrollActiveCellIntoView();
                break;
            case KeyboardCommandShortcut.CycleSelectionCorner:
                CycleSelectionCorner();
                break;
            case KeyboardCommandShortcut.SelectDirectPrecedents:
                SelectFormulaAuditCells(selectDependents: false, includeTransitive: false);
                break;
            case KeyboardCommandShortcut.SelectDirectDependents:
                SelectFormulaAuditCells(selectDependents: true, includeTransitive: false);
                break;
            case KeyboardCommandShortcut.SelectAllPrecedents:
                SelectFormulaAuditCells(selectDependents: false, includeTransitive: true);
                break;
            case KeyboardCommandShortcut.SelectAllDependents:
                SelectFormulaAuditCells(selectDependents: true, includeTransitive: true);
                break;
            case KeyboardCommandShortcut.ClearSelectionAndEdit:
                ClearSelectionAndEdit();
                break;
            case KeyboardCommandShortcut.CloseWorkbook:
                await CloseWorkbookAsync();
                break;
            case KeyboardCommandShortcut.OpenActiveDropdown:
                OpenActiveDropdown();
                break;
        }

        return true;
    }

    private static Key GetEffectiveWorkbookShortcutKey(KeyEventArgs args) =>
        NormalizeWorkbookShortcutKey(args.Key, args.PhysicalKey);

    private static Key NormalizeWorkbookShortcutKey(Key key, PhysicalKey physicalKey) =>
        physicalKey == PhysicalKey.F12 ? Key.F12 : key;

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
            ? UiText.Get(insertTime ? "KeyboardLoc_InsertedCurrentTime" : "KeyboardLoc_InsertedCurrentDate")
            : result.ErrorMessage ?? UiText.Get("KeyboardLoc_InsertCurrentDateOrTimeFailed"));
    }

    private void ToggleOutlineSymbolsShortcut()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var next = !(_session.ActiveSheet.ShowOutlineSymbols ?? true);
        var result = _session.SetShowOutlineSymbols(next);
        RefreshShell(result.Success
            ? UiText.Get(next ? "KeyboardLoc_ShowingOutlineSymbols" : "KeyboardLoc_HidingOutlineSymbols")
            : result.ErrorMessage ?? UiText.Get("KeyboardLoc_ChangeOutlineSymbolsFailed"));
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
            ? UiText.Get(mode == CopyFromAboveMode.Value
                ? "KeyboardLoc_CopiedValueFromAbove"
                : "KeyboardLoc_CopiedFormulaFromAbove")
            : result.ErrorMessage ?? UiText.Get("KeyboardLoc_CopyFromAboveFailed"));
    }

    private void InsertChartSheetFromSelection()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var sheet = _session.ActiveSheet;
        var command = ChartCommandWorkflowPlanner.BuildChartSheetCommand(
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
        RefreshShell(UiText.Get("KeyboardLoc_InsertedChartSheet"));
    }

    private void SelectFormulaAuditCells(bool selectDependents, bool includeTransitive)
    {
        var activeCell = _session.ActiveCell;
        var plan = FormulaAuditSelectionPlanner.Plan(
            _session.Workbook,
            activeCell,
            selectDependents,
            includeTransitive);
        if (plan is null)
        {
            var statusKey = (selectDependents, includeTransitive) switch
            {
                (true, true) => "KeyboardLoc_NoTraceableDependents",
                (true, false) => "KeyboardLoc_NoDirectDependents",
                (false, true) => "KeyboardLoc_NoTraceablePrecedents",
                _ => "KeyboardLoc_NoDirectPrecedents",
            };
            RefreshShell(UiText.Get(statusKey));
            return;
        }

        if (plan.TargetSheetId != _session.ActiveSheet.Id)
            _session.SelectSheet(plan.TargetSheetId);
        var ranges = SelectionRangeService.CompressAddresses(plan.Matches);
        _session.SelectRanges(new GridRange(plan.Matches[0], plan.Matches[0]), ranges);
        RefreshShell(UiText.Get(selectDependents
            ? "KeyboardLoc_SelectedFormulaDependents"
            : "KeyboardLoc_SelectedFormulaPrecedents"));
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
            RefreshShell(UiText.Get("MainLoc_Ready"));
    }

    private void CycleSelectionCorner()
    {
        var range = _session.SelectedRanges.Count == 1 ? _session.SelectedRanges[0] : _session.SelectedRange;
        var next = SelectionCornerNavigator.GetNextCorner(range, _session.ActiveCell);

        // Keep the full rectangle selected while moving only the active cell to the requested corner --
        // matching Excel and WPF's CycleSelectionCorner. The old form passed a 1x1 GridRange(next, next)
        // as the primary range, collapsing SelectedRange to a single cell even though the full rectangle
        // survived in SelectedRanges. Every consumer that reads the primary SelectedRange rather than
        // SelectedRanges then saw just one cell: Define Name, Insert Chart and Conditional Format would
        // target the corner cell, and multi-cell command gating (SelectedRange.RowCount/ColCount/
        // CellCount, e.g. Merge/Sort enablement) would misreport the selection as 1x1.
        _session.SelectRanges(range, [range], next);
        RefreshShell(UiText.Get("MainLoc_Ready"));
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
