using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;
using Free.Shared.Shell.Avalonia;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using FreeShellShell = Free.Shared.Shell;

namespace FreeX.App.Avalonia;

/// <summary>
/// Handlers for ribbon dropdown / split-button menu items that previously fell through to the
/// NoOpRibbonCommand seed because their canonical ids were never bound in the <c>ExtraCommands</c>
/// dictionary (see <c>MainWindow.cs</c>). Each handler reuses an existing shared command / shell
/// method; this file only adds the dispatch glue plus a few small View-tab surfaces (Ruler toggle,
/// Switch Windows, Reset Window Position) that had no shell entry point at all.
/// </summary>
public sealed partial class MainWindow
{
    // ── View ▸ Show ▸ Ruler ─────────────────────────────────────────────────────
    private void ToggleShowRulers()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        var showRulers = !_session.IsShowingRulers;
        var result = _session.SetShowRulers(showRulers);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("InsertLoc_RulerFailed"));
            return;
        }

        RefreshShell(showRulers ? UiText.Get("RibbonWire_RulerShown") : UiText.Get("RibbonWire_RulerHidden"));
    }

    // ── View ▸ Zoom split-button presets ────────────────────────────────────────
    private void ApplyZoomPercentPreset(int zoomPercent) =>
        ApplyZoomPercent(zoomPercent, UiText.Get("InsertLoc_ZoomFailed"));

    // ── View ▸ Window ▸ Switch Windows ──────────────────────────────────────────
    // Builds a chooser of every visible top-level window and activates the picked one. Self-contained
    // (uses AllTopLevelWindows from MainWindow.WindowManagement.cs) — no shared multi-window service.
    private void ShowSwitchWindowsDialog() => _ = ShowSwitchWindowsDialogAsync();

    private async Task ShowSwitchWindowsDialogAsync()
    {
        var windows = AllTopLevelWindows.Where(static w => w.IsVisible).ToList();
        if (windows.Count <= 1)
        {
            RefreshShell(UiText.Get("RibbonWire_SwitchWindowsNone"));
            return;
        }

        Window? picked = null;
        var dialog = new Window
        {
            Title = UiText.Get("RibbonWire_SwitchWindowsTitle"),
            Width = 360,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "SwitchWindowsDialog");

        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            var label = string.IsNullOrWhiteSpace(window.Title) ? UiText.Format("InsertLoc_WindowLabel", index + 1) : window.Title!;
            var button = new Button
            {
                Content = label,
                MinWidth = 320,
                Padding = new Thickness(8, 6),
                IsDefault = ReferenceEquals(window, this),
            };
            button.Click += (_, _) => { picked = window; dialog.Close(); };
            panel.Children.Add(button);
        }

        dialog.Content = new ScrollViewer { Content = panel };
        await dialog.ShowDialog(this);

        if (picked is null)
            return;

        if (picked.WindowState == WindowState.Minimized)
            picked.WindowState = WindowState.Normal;
        picked.Activate();
        RefreshShell(UiText.Get("RibbonWire_SwitchWindowsActivated"));
    }

    // ── View ▸ Window ▸ Reset Window Position ────────────────────────────────────
    // Reuses the portable Free.Shared.Shell.WindowResetPositionPlanner to compute a centered rect.
    private void ResetWindowPosition()
    {
        var workArea = GetPrimaryWorkArea();
        // The planner returns a rect relative to a (0,0) work-area origin; offset by the real origin.
        var reset = FreeShellShell.WindowResetPositionPlanner.Compute(
            workArea.Width, workArea.Height, windowIndex: 0);

        WindowState = WindowState.Normal;
        Width = reset.Width;
        Height = reset.Height;
        Position = new PixelPoint(
            workArea.X + (int)reset.X,
            workArea.Y + (int)reset.Y);
        RefreshShell(UiText.Get("RibbonWire_WindowPositionReset"));
    }

    // ── Formulas ▸ Calculation ▸ Calculate Sheet ─────────────────────────────────
    // The shared session exposes only a whole-workbook recalc (no per-sheet engine entry point), so
    // Calculate Sheet recalculates the workbook — functionally a superset of recalculating the active
    // sheet. Reported honestly via the status text.
    private void CalculateSheet()
    {
        _session.RecalculateWorkbook();
        RefreshShell(UiText.Get("RibbonWire_CalculateSheetDone"));
    }

    // ── Formulas ▸ Formula Auditing ▸ Remove Arrows submenu ──────────────────────
    private void RemoveFormulaTraceArrowsOfKind(FormulaTraceArrowKind kind)
    {
        var removed = _formulaTraceArrows.RemoveAll(arrow => arrow.Kind == kind);
        if (removed == 0)
        {
            RefreshShell(kind == FormulaTraceArrowKind.Precedent
                ? UiText.Get("RibbonWire_NoPrecedentArrows")
                : UiText.Get("RibbonWire_NoDependentArrows"));
            return;
        }

        RefreshShell(kind == FormulaTraceArrowKind.Precedent
            ? UiText.Get("RibbonWire_RemovedPrecedentArrows")
            : UiText.Get("RibbonWire_RemovedDependentArrows"));
    }

    // ── Home ▸ Cells ▸ Insert / Delete sheet rows & columns ──────────────────────

    /// <summary>
    /// R124-ribbonwires-multiarea-insertdelete-1: mirrors the WPF host's R123 fix
    /// (ResolveInsertAreas/TryExecuteRepeatableCurrentSelectionAreasInsertCommand and its Delete-side
    /// counterpart, MainWindow.CellsCommands.cs) for the Avalonia ribbon's Home ▸ Cells ▸ Insert/Delete
    /// Sheet Rows/Columns handlers below. Ctrl+click on row/column headers
    /// (AddAdditionalRowSelection/AddAdditionalColumnSelection, MainWindow.RowColumnVisibility.cs) is a
    /// first-class Excel gesture that builds a genuine multi-area selection -- every clicked whole
    /// row/column lands in _session.SelectedRanges, while _session.SelectedRange is only the
    /// last-clicked (active) one. Reading only SelectedRange (as this file did before) silently
    /// dropped every area but the active one from the insert/delete, unlike real Excel, which acts on
    /// every disjoint area of a multi-area selection. Routes through the same
    /// SelectionStyleCommandPlanner.ResolveRanges choke point MainWindow.Outline.cs's Group/Ungroup
    /// multi-area fix already uses. Areas are ordered DESCENDING by row/column so acting on one area
    /// never renumbers the still-pending index of another queued area (whether inserting -- which
    /// shifts everything below/right of the insert point down/over -- or deleting -- which shifts
    /// everything below/right of the deleted band up/left).
    /// </summary>
    private IReadOnlyList<GridRange> ResolveSheetEditAreas(bool orderByRow)
    {
        var ranges = SelectionStyleCommandPlanner.ResolveRanges(_session.SelectedRange, _session.SelectedRanges);
        if (ranges.Count == 0)
            ranges = [_session.SelectedRange];

        return orderByRow
            ? ranges.OrderByDescending(static r => r.Start.Row).ToList()
            : ranges.OrderByDescending(static r => r.Start.Col).ToList();
    }

    private void InsertSheetRows()
    {
        var range = _session.SelectedRange;
        var areas = ResolveSheetEditAreas(orderByRow: true);
        var sheetId = _session.ActiveSheet.Id;
        var commands = areas
            .Select(area => (IWorkbookCommand)new InsertRowsCommand(sheetId, area.Start.Row, area.RowCount))
            .ToList();
        var command = commands.Count == 1 ? commands[0] : new CompositeWorkbookCommand("Insert Sheet Rows", commands);
        var result = _session.ExecuteReviewCommand(command);
        if (result.Success)
        {
            // R127C-avalonia-clipboard-marquee-ribbon-multi-area-1: ExecuteReviewCommand already
            // retires the SESSION-level pending Copy/Cut for this structural edit (WorkbookSession.
            // IsStructuralCellShiftCommand), but this shell's own marching-ants overlay
            // (_clipboardMarqueeRange in MainWindow.cs) is separate UI-only state RefreshShell does
            // not touch -- clear it here too, matching MainWindow.InsertDeleteCells.cs's whole-row/
            // whole-column paths and the WPF host's ClearClipboardMarqueeAfterStructuralEdit.
            SetClipboardMarquee(null, isCut: false);
            ClearFormulaTraceArrowsAfterStructuralEdit();
            ShiftScrollOriginForRowEdit(range.Start.Row, (int)range.RowCount);
        }
        RefreshShell(result.Success
            ? UiText.Get("RibbonWire_InsertedSheetRows")
            : result.ErrorMessage ?? UiText.Get("RibbonWire_InsertSheetRowsFailed"));
    }

    private void InsertSheetColumns()
    {
        var range = _session.SelectedRange;
        var areas = ResolveSheetEditAreas(orderByRow: false);
        var sheetId = _session.ActiveSheet.Id;
        var commands = areas
            .Select(area => (IWorkbookCommand)new InsertColumnsCommand(sheetId, area.Start.Col, area.ColCount))
            .ToList();
        var command = commands.Count == 1 ? commands[0] : new CompositeWorkbookCommand("Insert Sheet Columns", commands);
        var result = _session.ExecuteReviewCommand(command);
        if (result.Success)
        {
            // R127C-avalonia-clipboard-marquee-ribbon-multi-area-1: see the matching comment in
            // InsertSheetRows() above.
            SetClipboardMarquee(null, isCut: false);
            ClearFormulaTraceArrowsAfterStructuralEdit();
            ShiftScrollOriginForColEdit(range.Start.Col, (int)range.ColCount);
        }
        RefreshShell(result.Success
            ? UiText.Get("RibbonWire_InsertedSheetColumns")
            : result.ErrorMessage ?? UiText.Get("RibbonWire_InsertSheetColumnsFailed"));
    }

    private void DeleteSheetRows()
    {
        var range = _session.SelectedRange;
        var areas = ResolveSheetEditAreas(orderByRow: true);
        var sheetId = _session.ActiveSheet.Id;
        var commands = areas
            .Select(area => (IWorkbookCommand)new DeleteRowsCommand(sheetId, area.Start.Row, area.RowCount))
            .ToList();
        var command = commands.Count == 1 ? commands[0] : new CompositeWorkbookCommand("Delete Sheet Rows", commands);
        var result = _session.ExecuteReviewCommand(command);
        if (result.Success)
        {
            // R127C-avalonia-clipboard-marquee-ribbon-multi-area-1: see the matching comment in
            // InsertSheetRows() above.
            SetClipboardMarquee(null, isCut: false);
            ClearFormulaTraceArrowsAfterStructuralEdit();
            ShiftScrollOriginForRowEdit(range.Start.Row, -(int)range.RowCount);
        }
        RefreshShell(result.Success
            ? UiText.Get("RibbonWire_DeletedSheetRows")
            : result.ErrorMessage ?? UiText.Get("RibbonWire_DeleteSheetRowsFailed"));
    }

    private void DeleteSheetColumns()
    {
        var range = _session.SelectedRange;
        var areas = ResolveSheetEditAreas(orderByRow: false);
        var sheetId = _session.ActiveSheet.Id;
        var commands = areas
            .Select(area => (IWorkbookCommand)new DeleteColumnsCommand(sheetId, area.Start.Col, area.ColCount))
            .ToList();
        var command = commands.Count == 1 ? commands[0] : new CompositeWorkbookCommand("Delete Sheet Columns", commands);
        var result = _session.ExecuteReviewCommand(command);
        if (result.Success)
        {
            // R127C-avalonia-clipboard-marquee-ribbon-multi-area-1: see the matching comment in
            // InsertSheetRows() above.
            SetClipboardMarquee(null, isCut: false);
            ClearFormulaTraceArrowsAfterStructuralEdit();
            ShiftScrollOriginForColEdit(range.Start.Col, -(int)range.ColCount);
        }
        RefreshShell(result.Success
            ? UiText.Get("RibbonWire_DeletedSheetColumns")
            : result.ErrorMessage ?? UiText.Get("RibbonWire_DeleteSheetColumnsFailed"));
    }

    // Mirrors the WPF host's ClearFormulaTraceArrowsAfterStructuralEdit invalidation:
    // row/column insert/delete rewrites formulas and shifts cells (RowColumnShiftHelpers) but never
    // touches _formulaTraceArrows itself, so a stale arrow set would silently keep pointing at
    // pre-edit grid coordinates that, after the shift, belong to different cells than the formula's
    // actual (now-moved) precedents/dependents. Excel clears trace arrows outright on a structural
    // edit rather than trying to re-derive them, since the frontier the arrows were expanded from may
    // no longer even resolve to a formula cell; this mirrors that behavior. RefreshShell (called by
    // every caller right after) repaints the overlay, so no separate redraw is needed here.
    private void ClearFormulaTraceArrowsAfterStructuralEdit()
    {
        _formulaTraceArrows.Clear();
    }

    /// <summary>
    /// R76-render-freeze-scroll-4-1: Insert/Delete Rows renumbers every row at or below the edit
    /// point, so if the edit happens AT OR ABOVE the current viewport's top-left anchor
    /// (<see cref="Sheet.ViewTopRow"/>), the same anchor now points at DIFFERENT worksheet
    /// content -- the view visibly jumps even though nothing scrolled. Excel instead keeps the
    /// same content on screen by shifting the anchor by the inserted/deleted row count. Only
    /// applies when the edit is at/above the view; an edit strictly below the view never moves
    /// it. Mirrors the WPF host's ShiftScrollOriginForRowEdit (MainWindow.Viewport.cs); must run
    /// before the RefreshShell() call every caller already makes, since that is what rebuilds the
    /// grid from ActiveSheet.ViewTopRow.
    /// </summary>
    private void ShiftScrollOriginForRowEdit(uint editRow, int rowDelta)
    {
        if (rowDelta == 0) return;

        var sheet = _session.ActiveSheet;
        var currentTopRow = sheet.ViewTopRow ?? Math.Max(1, sheet.FrozenRows + 1);
        if (editRow > currentTopRow) return;

        sheet.ViewTopRow = (uint)Math.Clamp((long)currentTopRow + rowDelta, 1, CellAddress.MaxRow);
    }

    /// <summary>
    /// Column counterpart of <see cref="ShiftScrollOriginForRowEdit"/> for Insert/Delete Columns.
    /// </summary>
    private void ShiftScrollOriginForColEdit(uint editCol, int colDelta)
    {
        if (colDelta == 0) return;

        var sheet = _session.ActiveSheet;
        var currentLeftCol = sheet.ViewLeftCol ?? Math.Max(1, sheet.FrozenCols + 1);
        if (editCol > currentLeftCol) return;

        sheet.ViewLeftCol = (uint)Math.Clamp((long)currentLeftCol + colDelta, 1, CellAddress.MaxCol);
    }

    // ── Home ▸ Cells ▸ Format ▸ Lock Cell ────────────────────────────────────────
    private void ToggleSelectedRangeLock()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var locked = _session.IsSelectedRangeStartLocked;
        var result = _session.ApplySelectedRangeCompactFormat(new StyleDiff(Locked: !locked), borderPreset: null);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("InsertLoc_LockCellFailed"));
            return;
        }

        RefreshShell(!locked ? UiText.Get("RibbonWire_CellLocked") : UiText.Get("RibbonWire_CellUnlocked"));
    }

    // ── Data ▸ Outline ▸ Show / Hide Detail ──────────────────────────────────────
    // Mirrors WPF's blanket ExpandGroupBtn_Click/CollapseGroupBtn_Click (MainWindow.OutlineCommands.cs):
    // pick row vs. column outline based on whether the current selection is a whole-column
    // selection, so grouped columns can be expanded/collapsed from the ribbon too, not just rows.
    // (The per-group +/- boundary toggle that WPF's grid raises via OnOutlineGroupToggleRequested
    // has no Avalonia counterpart yet — the Avalonia shell has no outline-gutter click surface in
    // its grid rendering to wire it to; that remains a separate, grid-rendering-level gap.)
    // MainWindow.OutlineGrid.cs now supplies the Avalonia gutter and routes +/- through the same
    // undo-aware session command path.
    private void ShowOutlineDetail()
    {
        var range = _session.SelectedRange;
        var axis = OutlineGroupingService.GetGroupingAxis(range);
        var result = axis == OutlineGroupingAxis.Columns
            ? _session.ExecuteReviewCommand(new ExpandColGroupCommand(
                _session.ActiveSheet.Id, 1, range.Start.Col, range.End.Col))
            : _session.ExecuteReviewCommand(new ExpandRowGroupCommand(_session.ActiveSheet.Id, 1, range.Start.Row, range.End.Row));
        RefreshShell(result.Success
            ? UiText.Get("RibbonWire_ShownDetail")
            : result.ErrorMessage ?? UiText.Get("RibbonWire_ShowDetailFailed"));
    }

    private void HideOutlineDetail()
    {
        var range = _session.SelectedRange;
        var axis = OutlineGroupingService.GetGroupingAxis(range);
        var result = axis == OutlineGroupingAxis.Columns
            ? _session.ExecuteReviewCommand(new CollapseColGroupCommand(
                _session.ActiveSheet.Id, 1, range.Start.Col, range.End.Col))
            : _session.ExecuteReviewCommand(new CollapseRowGroupCommand(_session.ActiveSheet.Id, 1, range.Start.Row, range.End.Row));
        RefreshShell(result.Success
            ? UiText.Get("RibbonWire_HidDetail")
            : result.ErrorMessage ?? UiText.Get("RibbonWire_HideDetailFailed"));
    }

    // ── Page Layout ▸ Page Setup group quick presets ─────────────────────────────
    private void RegisterPageLayoutRibbonActions(IDictionary<string, Action> commands)
    {
        foreach (var descriptor in PageLayoutRibbonActionPlanner.RibbonActionDescriptors)
            commands[descriptor.CommandId] = CreatePageLayoutRibbonAction(descriptor);
    }

    private Action CreatePageLayoutRibbonAction(PageLayoutRibbonActionDescriptor descriptor) =>
        descriptor.Kind switch
        {
            PageLayoutRibbonActionKind.OpenPageSetupDialog => () => _ = ShowPageSetupDialogAsync(descriptor.PageSetupOpenSource),
            PageLayoutRibbonActionKind.ShowPageBreaksMenu => ShowPageBreaksMenu,
            PageLayoutRibbonActionKind.ShowGridlinesSheetOptions => () => _ = ShowGridlinesSheetOptionsAsync(),
            PageLayoutRibbonActionKind.ShowHeadingsSheetOptions => () => _ = ShowHeadingsSheetOptionsAsync(),
            PageLayoutRibbonActionKind.ChooseBackground => ChooseSheetBackground,
            PageLayoutRibbonActionKind.DeleteBackground => DeleteSheetBackground,
            PageLayoutRibbonActionKind.SetPrintArea => SetPrintAreaFromSelection,
            PageLayoutRibbonActionKind.ClearPrintArea => ClearPrintArea,
            PageLayoutRibbonActionKind.ApplyMarginsPreset => () => ApplyPageMarginsPreset(descriptor.MarginPreset!.Value),
            PageLayoutRibbonActionKind.ApplyOrientationPreset => () => ApplyPageOrientationPreset(descriptor.OrientationPreset!.Value),
            PageLayoutRibbonActionKind.ApplyPaperSizePreset => () => ApplyPaperSizePreset(descriptor.PaperSizePreset!.Value),
            PageLayoutRibbonActionKind.ApplyPageBreakAction => () => ApplyPageBreakAction(descriptor.PageBreakAction!.Value),
            _ => throw new InvalidOperationException($"Unsupported Page Layout ribbon action: {descriptor.Kind}"),
        };

    private void ApplyPageMarginsPreset(PageLayoutMarginPreset preset)
    {
        var plan = PageLayoutRibbonActionPlanner.PlanMarginsPreset(preset);
        var result = _session.ExecuteReviewCommand(
            PageLayoutRibbonCommandPlanner.BuildMarginsCommand(_session.ActiveSheet.Id, plan.Value));
        RefreshShell(PageLayoutStatusPlanner.ResolveCommandStatus(
            PageLayoutStatusPlanner.ForPreset(plan),
            result.Success,
            result.ErrorMessage,
            UiText.Get));
    }

    private void ApplyPageOrientationPreset(PageLayoutOrientationPreset preset)
    {
        var plan = PageLayoutRibbonActionPlanner.PlanOrientationPreset(preset);
        var result = _session.ExecuteReviewCommand(
            PageLayoutRibbonCommandPlanner.BuildOrientationCommand(_session.ActiveSheet.Id, plan.Value));
        RefreshShell(PageLayoutStatusPlanner.ResolveCommandStatus(
            PageLayoutStatusPlanner.ForPreset(plan),
            result.Success,
            result.ErrorMessage,
            UiText.Get));
    }

    private void ApplyPaperSizePreset(PageLayoutPaperSizePreset preset)
    {
        var plan = PageLayoutRibbonActionPlanner.PlanPaperSizePreset(preset);
        var result = _session.ExecuteReviewCommand(
            PageLayoutRibbonCommandPlanner.BuildPaperSizeCommand(_session.ActiveSheet.Id, plan.Value));
        RefreshShell(PageLayoutStatusPlanner.ResolveCommandStatus(
            PageLayoutStatusPlanner.ForPreset(plan),
            result.Success,
            result.ErrorMessage,
            UiText.Get));
    }

    private void SetPrintAreaFromSelection()
    {
        var result = _session.ExecuteReviewCommand(
            PageLayoutRibbonCommandPlanner.BuildSetPrintAreaCommand(_session.ActiveSheet.Id, _session.SelectedRange));
        RefreshShell(PageLayoutStatusPlanner.ResolveCommandStatus(
            PageLayoutStatusPlanner.PrintAreaSet,
            result.Success,
            result.ErrorMessage,
            UiText.Get));
    }

    private void ClearPrintArea()
    {
        var result = _session.ExecuteReviewCommand(
            PageLayoutRibbonCommandPlanner.BuildClearPrintAreaCommand(_session.ActiveSheet.Id));
        RefreshShell(PageLayoutStatusPlanner.ResolveCommandStatus(
            PageLayoutStatusPlanner.PrintAreaClear,
            result.Success,
            result.ErrorMessage,
            UiText.Get));
    }

    // ── Page Layout ▸ Page Setup ▸ Background (Choose / Delete) ──────────────────
    private void ChooseSheetBackground() => _ = ChooseSheetBackgroundAsync();

    private async Task ChooseSheetBackgroundAsync()
    {
        if (!((IStorageProvider)StorageProvider).CanOpen)
        {
            ShowEditIssue(UiText.Get("RibbonWire_BackgroundUnavailable"));
            return;
        }

        if (!TryCommitPendingFormulaEdit())
            return;

        var pickerPlan = SheetBackgroundPickerPlanner.BuildOpenPickerPlan();
        var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromDescriptors(
                UiText.Get("RibbonWire_BackgroundPickerTitle"),
                pickerPlan.FileTypes));

        if (file is null)
            return;

        if (!SheetBackgroundPickerPlanner.IsSupportedImagePath(file.Name))
        {
            ShowEditIssue(UiText.Get("RibbonWire_BackgroundUnsupported"));
            return;
        }

        byte[] imageBytes;
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            imageBytes = memory.ToArray();
        }
        catch (IOException ex)
        {
            ShowEditIssue(UiText.Format("InsertLoc_CouldNotReadImage", ex.Message));
            return;
        }

        if (imageBytes.Length == 0)
        {
            ShowEditIssue(UiText.Get("RibbonWire_BackgroundUnsupported"));
            return;
        }

        if (!SheetBackgroundPickerPlanner.TryBuildBackgroundImage(imageBytes, file.Name, out var background)
            || background is null)
        {
            ShowEditIssue(UiText.Get("RibbonWire_BackgroundUnsupported"));
            return;
        }

        var result = _session.ExecuteReviewCommand(
            PageLayoutRibbonCommandPlanner.BuildSetBackgroundCommand(_session.ActiveSheet.Id, background));
        RefreshShell(result.Success
            ? UiText.Get("RibbonWire_BackgroundSet")
            : result.ErrorMessage ?? UiText.Get("RibbonWire_BackgroundSet"));
    }

    private void DeleteSheetBackground()
    {
        var result = _session.ExecuteReviewCommand(
            PageLayoutRibbonCommandPlanner.BuildClearBackgroundCommand(_session.ActiveSheet.Id));
        RefreshShell(result.Success
            ? UiText.Get("RibbonWire_BackgroundDeleted")
            : result.ErrorMessage ?? UiText.Get("RibbonWire_BackgroundDeleted"));
    }
}
