using System.Collections.Generic;
using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host;

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
    }

    private void RebuildDependenciesAndCalculate()
    {
        _recalcEngine.RebuildFormulaDependencies(_workbook);
        _recalcEngine.RecalculateAllFormulas(_workbook);
        UpdateViewport();
    }

    private void RecalculateIfAutomatic(IReadOnlyList<CellAddress> changedCells)
    {
        if (_workbook.CalculationMode == WorkbookCalculationMode.Automatic)
            _recalcEngine.Recalculate(_workbook, changedCells);
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateRibbonCompactMode();
        UpdateViewport();
    }

    private string FormatCellReference(CellAddress address) =>
        SpreadsheetDisplayFormatter.FormatCellReference(address, _options.UseR1C1ReferenceStyle);

    private string FormatColumnReference(uint column) =>
        SpreadsheetDisplayFormatter.FormatColumnReference(column, _options.UseR1C1ReferenceStyle);

    private string FormatRangeReference(CellAddress start, CellAddress end) =>
        SpreadsheetDisplayFormatter.FormatRangeReference(start, end, _options.UseR1C1ReferenceStyle);

    private string FormatFormulaBarText(Cell? cell, CellAddress address) =>
        SpreadsheetDisplayFormatter.FormatFormulaBarText(cell, address, _options.UseR1C1ReferenceStyle);

    private void RefreshToolbar()
    {
        var canUndo = _commandBus.CanUndo(_workbook.Id);
        var canRedo = _commandBus.CanRedo(_workbook.Id);

        if (SheetGrid.SelectedRange is not { } range)
        {
            _lastToolbarVisualState = null;
            UndoQatBtn.IsEnabled = canUndo;
            RedoQatBtn.IsEnabled = canRedo;
            return;
        }
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
        {
            _lastToolbarVisualState = null;
            UndoQatBtn.IsEnabled = canUndo;
            RedoQatBtn.IsEnabled = canRedo;
            return;
        }
        var style = _workbook.GetStyle(sheet.GetCell(range.Start)?.StyleId ?? StyleId.Default);
        var state = ToolbarVisualState.From(style, canUndo, canRedo);
        if (state == _lastToolbarVisualState)
            return;

        _suppressToolbarSync = true;
        try
        {
            UndoQatBtn.IsEnabled = state.CanUndo;
            RedoQatBtn.IsEnabled = state.CanRedo;
            BoldButton.IsChecked = state.Bold;
            ItalicButton.IsChecked = state.Italic;
            UnderlineButton.IsChecked = state.Underline;
            StrikeButton.IsChecked = state.Strikethrough;
            AlignTopBtn.IsChecked = state.VerticalAlignment == CellVAlign.Top;
            AlignMiddleBtn.IsChecked = state.VerticalAlignment == CellVAlign.Center;
            AlignBottomBtn.IsChecked = state.VerticalAlignment == CellVAlign.Bottom;
            AlignLeftBtn.IsChecked = state.HorizontalAlignment == CellHAlign.Left;
            AlignCenterBtn.IsChecked = state.HorizontalAlignment == CellHAlign.Center;
            AlignRightBtn.IsChecked = state.HorizontalAlignment == CellHAlign.Right;
            WrapTextBtn.IsChecked = state.WrapText;
            if (FontNameBox.Items.Contains(state.FontName))
                FontNameBox.SelectedItem = state.FontName;
            if (FontSizeBox.Items.Contains(state.FontSizeText))
                FontSizeBox.SelectedItem = state.FontSizeText;
            _lastToolbarVisualState = state;
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    private void ApplyStyleDiff(StyleDiff diff)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteRepeatableApplyStyle(diff, "Apply Style"))
            return;

        UpdateViewport();
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
    }

    private void RefreshWorkbookProtectionUi()
    {
        if (ProtectWorkbookButton is null)
            return;

        var uiText = WorkbookProtectionWorkflow.GetUiText(_workbook);
        ProtectWorkbookButton.Content = uiText.ButtonContent;
        RibbonTooltip.SetTitle(ProtectWorkbookButton, uiText.TooltipTitle);
        RibbonTooltip.SetDescription(ProtectWorkbookButton, uiText.TooltipDescription);
    }
}
