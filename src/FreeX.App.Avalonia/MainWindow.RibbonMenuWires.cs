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
        var (workArea, scaling) = GetPrimaryWorkAreaMetrics();
        // The planner returns a rect relative to a (0,0) work-area origin; offset by the real origin.
        var reset = FreeShellShell.WindowResetPositionPlanner.Compute(
            AvaloniaWindowBoundsTranslator.PixelsToDips(workArea.Width, scaling),
            AvaloniaWindowBoundsTranslator.PixelsToDips(workArea.Height, scaling),
            windowIndex: 0);
        var tile = AvaloniaWindowBoundsTranslator.Translate(workArea, scaling, reset);

        WindowState = WindowState.Normal;
        Width = tile.Width;
        Height = tile.Height;
        Position = tile.Position;
        RefreshShell(UiText.Get("RibbonWire_WindowPositionReset"));
    }

    // ── Formulas ▸ Calculation ▸ Calculate Sheet ─────────────────────────────────
    // Keep the native ribbon alias on the same shared action path as Shift+F9.
    private void CalculateSheet() => CalculateActiveSheet();

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
    private void InsertSheetRows()
    {
        ApplyWorksheetStructureResult(
            _session.InsertSelectedRows(),
            UiText.Get("RibbonWire_InsertedSheetRows"),
            UiText.Get("RibbonWire_InsertSheetRowsFailed"));
    }

    private void InsertSheetColumns()
    {
        ApplyWorksheetStructureResult(
            _session.InsertSelectedColumns(),
            UiText.Get("RibbonWire_InsertedSheetColumns"),
            UiText.Get("RibbonWire_InsertSheetColumnsFailed"));
    }

    private void DeleteSheetRows()
    {
        ApplyWorksheetStructureResult(
            _session.DeleteSelectedRows(),
            UiText.Get("RibbonWire_DeletedSheetRows"),
            UiText.Get("RibbonWire_DeleteSheetRowsFailed"));
    }

    private void DeleteSheetColumns()
    {
        ApplyWorksheetStructureResult(
            _session.DeleteSelectedColumns(),
            UiText.Get("RibbonWire_DeletedSheetColumns"),
            UiText.Get("RibbonWire_DeleteSheetColumnsFailed"));
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
        var result = _session.SetSelectedOutlineGroupsCollapsed(collapse: false);
        RefreshShell(result.Success
            ? UiText.Get("RibbonWire_ShownDetail")
            : result.ErrorMessage ?? UiText.Get("RibbonWire_ShowDetailFailed"));
    }

    private void HideOutlineDetail()
    {
        var result = _session.SetSelectedOutlineGroupsCollapsed(collapse: true);
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
        => ExecutePageLayoutCommandWithShellRefresh(
            CreatePageLayoutCommandSession().PlanMarginsPreset(preset));

    private void ApplyPageOrientationPreset(PageLayoutOrientationPreset preset)
        => ExecutePageLayoutCommandWithShellRefresh(
            CreatePageLayoutCommandSession().PlanOrientationPreset(preset));

    private void ApplyPaperSizePreset(PageLayoutPaperSizePreset preset)
        => ExecutePageLayoutCommandWithShellRefresh(
            CreatePageLayoutCommandSession().PlanPaperSizePreset(preset));

    private void SetPrintAreaFromSelection()
        => ExecutePageLayoutCommandWithShellRefresh(
            CreatePageLayoutCommandSession().PlanSetPrintArea(_session.SelectedRange));

    private void ClearPrintArea()
        => ExecutePageLayoutCommandWithShellRefresh(
            CreatePageLayoutCommandSession().PlanClearPrintArea());

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

        ExecutePageLayoutCommandWithShellRefresh(
            CreatePageLayoutCommandSession().PlanSetBackground(background));
    }

    private void DeleteSheetBackground()
    {
        ExecutePageLayoutCommandWithShellRefresh(
            CreatePageLayoutCommandSession().PlanClearBackground());
    }
}
