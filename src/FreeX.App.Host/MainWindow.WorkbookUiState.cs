using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void ApplyOptionsToView()
    {
        SheetGrid.UseR1C1ReferenceStyle = _options.UseR1C1ReferenceStyle;
        _suppressAppViewOptionSync = true;
        try
        {
            if (ViewFormulaBarChk is not null)
                ViewFormulaBarChk.IsChecked = _options.ShowFormulaBar;
            if (FormulaBarBorder is not null)
                FormulaBarBorder.Visibility = _options.ShowFormulaBar ? Visibility.Visible : Visibility.Collapsed;
            _formulaBarExpanded = _options.FormulaBarExpanded;
            ApplyFormulaBarExpansion();
        }
        finally
        {
            _suppressAppViewOptionSync = false;
        }

        if (SheetGrid.SelectedRange is { } range)
        {
            CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
            var sheet = _workbook.GetSheet(_currentSheetId);
            FormulaBar.Text = FormatFormulaBarText(sheet?.GetCell(range.Start), range.Start);
        }
    }

    private void RecalculateWorkbook()
    {
        _recalcEngine.RecalculateAllFormulas(_workbook);
        InvalidateNavigationCaches();
    }

    private void RebuildDependenciesAndCalculate()
    {
        _recalcEngine.RebuildFormulaDependencies(_workbook);
        _recalcEngine.RecalculateAllFormulas(_workbook);
        InvalidateNavigationCaches();
        UpdateViewport();
    }

    private void RecalculateIfAutomatic(IReadOnlyList<CellAddress> changedCells)
    {
        if (_workbook.CalculationMode == WorkbookCalculationMode.Automatic)
        {
            _recalcEngine.Recalculate(_workbook, changedCells);
            InvalidateNavigationCaches();
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            NormalizeRibbonSurfaceAfterResize();
        ScheduleViewportResizeRefresh();
    }

    private void ScheduleViewportResizeRefresh()
    {
        if (!_resizeViewportRefreshPending)
            SheetGrid.IsLiveResizing = true;

        _resizeViewportRefreshPending = true;
        _resizeViewportRefreshGeneration++;
        _resizeViewportRefreshTimer ??= CreateResizeViewportRefreshTimer();
        _resizeViewportRefreshTimer.Stop();
        if (_isInWindowResizeMoveLoop)
            return;

        _resizeViewportRefreshTimer.Start();
    }

    private System.Windows.Threading.DispatcherTimer CreateResizeViewportRefreshTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = System.TimeSpan.FromMilliseconds(ResizeViewportRefreshDelayMilliseconds)
        };

        timer.Tick += (_, _) => QueueViewportResizeRefreshCompletion();

        return timer;
    }

    private void QueueViewportResizeRefreshCompletion()
    {
        _resizeViewportRefreshTimer?.Stop();
        var generation = _resizeViewportRefreshGeneration;
        Dispatcher.BeginInvoke(
            new System.Action(() =>
            {
                if (!_resizeViewportRefreshPending ||
                    _isInWindowResizeMoveLoop ||
                    generation != _resizeViewportRefreshGeneration)
                {
                    return;
                }

                CompleteViewportResizeRefresh();
            }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void CompleteViewportResizeRefresh()
    {
        _resizeViewportRefreshTimer?.Stop();
        _resizeViewportRefreshPending = false;
        SheetGrid.IsLiveResizing = false;
        UpdateViewport();
    }

    private void CancelPendingViewportResizeRefresh()
    {
        if (!_resizeViewportRefreshPending)
            return;

        _resizeViewportRefreshTimer?.Stop();
        _resizeViewportRefreshPending = false;
        _resizeViewportRefreshGeneration++;
        SheetGrid.IsLiveResizing = false;
    }

    private string FormatCellReference(CellAddress address) =>
        SpreadsheetDisplayFormatter.FormatCellReference(address, _options.UseR1C1ReferenceStyle);

    private string FormatColumnReference(uint column) =>
        SpreadsheetDisplayFormatter.FormatColumnReference(column, _options.UseR1C1ReferenceStyle);

    private string FormatRangeReference(CellAddress start, CellAddress end) =>
        SpreadsheetDisplayFormatter.FormatRangeReference(start, end, _options.UseR1C1ReferenceStyle);

    private string FormatFormulaBarText(Cell? cell, CellAddress address) =>
        SpreadsheetDisplayFormatter.FormatFormulaBarText(cell, address, _options.UseR1C1ReferenceStyle);

    private void InvalidateToolbarVisualState()
    {
        _toolbarVisualStateCache.Clear();
        _lastToolbarVisualState = null;
    }

    private void RefreshToolbar()
    {
        RefreshQuickAccessToolbarCommandStates();

        if (SheetGrid.SelectedRange is not { } range)
        {
            InvalidateToolbarVisualState();
            return;
        }
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
        {
            InvalidateToolbarVisualState();
            return;
        }
        var styleId = sheet.GetCell(range.Start)?.StyleId ?? StyleId.Default;
        var state = _toolbarVisualStateCache.TryGet(_workbook.Id, styleId, out var cachedState)
            ? cachedState
            : _toolbarVisualStateCache.AddOrUpdate(
                _workbook.Id,
                styleId,
                ToolbarVisualState.From(_workbook.GetStyle(styleId)));
        if (state == _lastToolbarVisualState)
            return;

        _suppressToolbarSync = true;
        try
        {
            SetToggleCheckedIfChanged(BoldButton, state.Bold);
            SetToggleCheckedIfChanged(ItalicButton, state.Italic);
            SetToggleCheckedIfChanged(UnderlineButton, state.Underline);
            SetToggleCheckedIfChanged(StrikeButton, state.Strikethrough);
            SetToggleCheckedIfChanged(AlignTopBtn, state.VerticalAlignment == CellVAlign.Top);
            SetToggleCheckedIfChanged(AlignMiddleBtn, state.VerticalAlignment == CellVAlign.Center);
            SetToggleCheckedIfChanged(AlignBottomBtn, state.VerticalAlignment == CellVAlign.Bottom);
            SetToggleCheckedIfChanged(AlignLeftBtn, state.HorizontalAlignment == CellHAlign.Left);
            SetToggleCheckedIfChanged(AlignCenterBtn, state.HorizontalAlignment == CellHAlign.Center);
            SetToggleCheckedIfChanged(AlignRightBtn, state.HorizontalAlignment == CellHAlign.Right);
            SetToggleCheckedIfChanged(WrapTextBtn, state.WrapText);
            SetSelectedItemIfChanged(FontNameBox, state.FontName);
            SetSelectedItemIfChanged(FontSizeBox, state.FontSizeText);
            _lastToolbarVisualState = state;
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    private static void SetToggleCheckedIfChanged(ToggleButton button, bool? value)
    {
        if (button.IsChecked != value)
            button.IsChecked = value;
    }

    private static void SetSelectedItemIfChanged(ComboBox comboBox, object value)
    {
        if (Equals(comboBox.SelectedItem, value))
            return;

        if (comboBox.Items.Contains(value))
            comboBox.SelectedItem = value;
    }

    private void ApplyStyleDiff(StyleDiff diff)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteRepeatableApplyStyle(diff, "Apply Style"))
            return;

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void FindButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FindReplaceDialog(
            () => _workbook,
            _commandBus,
            NavigateToCell,
            replaceMode: false,
            () => _currentSheetId,
            () => SheetGrid.SelectedRange?.Start)
        {
            Owner = this
        };
        dlg.Show();
    }

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FindReplaceDialog(
            () => _workbook,
            _commandBus,
            NavigateToCell,
            replaceMode: true,
            () => _currentSheetId,
            () => SheetGrid.SelectedRange?.Start)
        {
            Owner = this
        };
        dlg.Show();
    }

    private void NavigateToCell(CellAddress addr)
    {
        _currentSheetId = addr.Sheet;
        SetActiveCell(addr);
        EnsureCellVisible(addr);
        UpdateViewport();
    }

    private void RefreshSheetProtectionUi()
    {
        if (ProtectSheetButton is null)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var uiText = SheetProtectionWorkflow.GetUiText(sheet);
        ProtectSheetButton.Content = uiText.ButtonContent;
        RibbonTooltip.SetTitle(ProtectSheetButton, uiText.TooltipTitle);
        RibbonTooltip.SetDescription(ProtectSheetButton, uiText.TooltipDescription);

        if (AllowEditRangesButton is not null)
            AllowEditRangesButton.IsEnabled = !sheet.IsProtected;
    }

    private void RefreshWorkbookProtectionUi()
    {
        var uiText = WorkbookProtectionWorkflow.GetUiText(_workbook);
        if (ProtectWorkbookButton is not null)
        {
            ProtectWorkbookButton.Content = uiText.ButtonContent;
            RibbonTooltip.SetTitle(ProtectWorkbookButton, uiText.TooltipTitle);
            RibbonTooltip.SetDescription(ProtectWorkbookButton, uiText.TooltipDescription);
        }

        RefreshBackstageInfoProtectionButton();
    }
}
