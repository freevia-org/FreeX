using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void ViewGridlinesChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;

        if (!TryExecuteGroupedSheetCommand(
                "Gridlines",
                sheetId => new SetWorksheetViewOptionsCommand(
                    sheetId,
                    chk.IsChecked == true,
                    _workbook.GetSheet(sheetId)?.ShowHeadings ?? true,
                    _workbook.GetSheet(sheetId)?.ShowRulers ?? true)))
            return;

        UpdateViewport();
    }

    private void ViewHeadersChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;

        if (!TryExecuteGroupedSheetCommand(
                "Headings",
                sheetId => new SetWorksheetViewOptionsCommand(
                    sheetId,
                    _workbook.GetSheet(sheetId)?.ShowGridlines ?? true,
                    chk.IsChecked == true,
                    _workbook.GetSheet(sheetId)?.ShowRulers ?? true)))
            return;

        UpdateViewport();
    }

    private void ViewRulerChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;

        if (!TryExecuteGroupedSheetCommand(
                "Ruler",
                sheetId => new SetWorksheetViewOptionsCommand(
                    sheetId,
                    _workbook.GetSheet(sheetId)?.ShowGridlines ?? true,
                    _workbook.GetSheet(sheetId)?.ShowHeadings ?? true,
                    chk.IsChecked == true)))
            return;

        UpdateViewport();
    }

    private void ViewFormulaBarChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressAppViewOptionSync) return;
        if (sender is not System.Windows.Controls.CheckBox chk || FormulaBarBorder is null) return;

        _options.ShowFormulaBar = chk.IsChecked == true;
        _options.Save();
        FormulaBarBorder.Visibility = _options.ShowFormulaBar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NormalViewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.Normal);

    private void PageBreakPreviewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview);

    private void PageLayoutViewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.PageLayout);

    private void SetWorksheetViewMode(WorksheetViewMode viewMode)
    {
        if (!TryExecuteGroupedSheetCommand("Workbook View",
                sheetId => new SetWorksheetViewModeCommand(sheetId, viewMode)))
            return;

        UpdateViewport();
    }

    private void CustomViewsBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomViewsDialog(_workbook, _commandBus) { Owner = this };
        dialog.ShowDialog();
        if (dialog.ViewApplied)
            UpdateViewport();
    }

    private void ArrangeAllPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void ArrangeAllContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        foreach (var item in menu.Items.OfType<MenuItem>())
            item.IsChecked = ArrangeAllMenuPlanner.IsChecked(item.Tag, _workbook.WindowArrangement);
    }

    private void ArrangeAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!ArrangeAllMenuPlanner.TryParseArrangement(
                (sender as System.Windows.Controls.MenuItem)?.Tag,
                out var arrangement))
            return;

        TryExecuteCommand(new SetWorkbookWindowArrangementCommand(arrangement), "Arrange Windows");
    }

    private void ViewWindowDeferredBtn_Click(object sender, RoutedEventArgs e)
    {
        var commandName = (sender as System.Windows.Controls.Button)?.Content?.ToString() ?? "This command";
        var message = DeferredCommandMessages.MultiWindow(commandName);
        MessageBox.Show(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void FreezePanesPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FreezeAtSelectionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        SetFreezePanes(
            (uint)Math.Max(0, (int)range.Start.Row - 1),
            (uint)Math.Max(0, (int)range.Start.Col - 1));
    }
    private void FreezeTopRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetFreezePanes(1, 0);
    }
    private void FreezeFirstColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetFreezePanes(0, 1);
    }
    private void UnfreezeAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetFreezePanes(0, 0);
    }

    private void SetFreezePanes(uint frozenRows, uint frozenCols)
    {
        var outcome = _commandBus.Execute(
            _workbook.Id,
            new SetFreezePanesCommand(_currentSheetId, frozenRows, frozenCols));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Freeze Panes");
            return;
        }

        UpdateViewport();
    }

    private void SplitViewBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        uint? splitRow = null;
        uint? splitColumn = null;
        if (sheet.SplitRow is null && sheet.SplitColumn is null &&
            SheetGrid.SelectedRange is { } range)
        {
            splitRow = range.Start.Row > 1 ? range.Start.Row : null;
            splitColumn = range.Start.Col > 1 ? range.Start.Col : null;
        }

        if (!TryExecuteGroupedSheetCommand(
                "Split",
                sheetId => new SetSplitPanesCommand(sheetId, splitRow, splitColumn)))
            return;

        UpdateViewport();
    }

    private void OnSplitDividerMoved(uint? splitRow, uint? splitColumn)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var nextRow = splitRow ?? sheet.SplitRow;
        var nextColumn = splitColumn ?? sheet.SplitColumn;
        if (nextRow == sheet.SplitRow && nextColumn == sheet.SplitColumn)
            return;

        if (!TryExecuteGroupedSheetCommand(
                "Split",
                sheetId => new SetSplitPanesCommand(sheetId, nextRow, nextColumn)))
            return;

        _splitPaneViewportOffsets.Remove(_currentSheetId);
        UpdateViewport();
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void MaxRestoreBtn_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseSysBtn_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 5);
    }
    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 5);
    }
    private void ZoomPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void ZoomPresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.MenuItem)?.Tag is not string tag ||
            !Freexcel.App.UI.ZoomLevelMapper.TryParseZoomPercent(tag, out var zoomPercent))
            return;

        ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);
    }
    private void ZoomCustomMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var current = (int)Math.Round(_zoomLevel * 100);
        var dialog = new ZoomDialog(current) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(dialog.Result.ZoomPercent);
    }
    private void Zoom100Btn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = 100;
    }
    private void ZoomSelectionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var fitPct = ZoomSelectionPlanner.CalculateFitPercent(
            SheetGrid.ActualWidth,
            SheetGrid.ActualHeight,
            range.ColCount,
            range.RowCount);
        ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(fitPct);
    }
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomSlider == null || SheetGrid == null || StatusZoomText == null) return;
        if (_snapInProgress || _suppressZoomSync) return;
        double sliderVal = e.NewValue;

        // Snap to 100% when near the midpoint
        if (Math.Abs(sliderVal - 100.0) < 3.0)
        {
            _snapInProgress = true;
            ZoomSlider.Value = 100.0;
            _snapInProgress = false;
            sliderVal = 100.0;
        }

        double zoomPct = Freexcel.App.UI.ZoomLevelMapper.SliderToZoomPercent(sliderVal);
        var roundedZoomPct = (int)Math.Round(zoomPct);
        if (!TryExecuteGroupedSheetCommand(
                "Zoom",
                sheetId => new SetWorksheetZoomCommand(sheetId, roundedZoomPct)))
            return;

        SyncZoomFromSheet(roundedZoomPct, updateSlider: false);
        UpdateViewport();
    }

    private void SyncZoomFromSheet(int zoomPercent, bool updateSlider = true)
    {
        zoomPercent = Math.Clamp(zoomPercent, SetWorksheetZoomCommand.MinZoomPercent, SetWorksheetZoomCommand.MaxZoomPercent);
        _zoomLevel = zoomPercent / 100.0;
        if (SheetGrid is not null)
        {
            SheetGrid.ZoomFactor = _zoomLevel;
            SheetGrid.RenderTransform = new System.Windows.Media.ScaleTransform(_zoomLevel, _zoomLevel, 0, 0);
        }
        if (StatusZoomText is not null)
            StatusZoomText.Text = $"{zoomPercent}%";

        if (!updateSlider || ZoomSlider is null)
            return;

        _suppressZoomSync = true;
        try
        {
            ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);
        }
        finally
        {
            _suppressZoomSync = false;
        }
    }

    private void FormulaBarExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        _formulaBarExpanded = !_formulaBarExpanded;
        _options.FormulaBarExpanded = _formulaBarExpanded;
        _options.Save();
        ApplyFormulaBarExpansion();
    }

    private void ApplyFormulaBarExpansion()
    {
        if (_formulaBarExpanded)
        {
            FormulaBar.Height       = 72;
            FormulaBar.AcceptsReturn = true;
            FormulaBarExpandBtn.Content = "▲";
        }
        else
        {
            FormulaBar.ClearValue(System.Windows.Controls.TextBox.HeightProperty);
            FormulaBar.AcceptsReturn = false;
            FormulaBarExpandBtn.Content = "▼";
        }
    }

    // ── Ribbon horizontal scroll via mouse wheel ─────────────────────────────

    private void RibbonScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta * 0.5);
        e.Handled = true;
    }
}
