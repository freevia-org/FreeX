using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void RefreshValidationDropdown()
    {
        if (_inlineEditor?.IsVisible == true)
            return;

        if (SheetGrid.SelectedRange is not { } range ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet ||
            TryGetCellOverlayRect(range.Start) is not { } rect)
        {
            HideValidationDropdown();
            return;
        }

        var rule = DataValidationService.GetApplicable(sheet, range.Start)
            .FirstOrDefault(dv => dv.Type == DvType.List && dv.ShowDropdown);
        if (rule is null)
        {
            HideValidationDropdown();
            return;
        }

        var items = DataValidationService.GetListItems(rule, sheet, _workbook);
        if (items.Count == 0)
        {
            HideValidationDropdown();
            return;
        }

        EnsureValidationDropdown();

        _suppressValidationDropdownCommit = true;
        _validationDropdown!.ItemsSource = items;
        var currentText = SpreadsheetDisplayFormatter.FormatCellValue(sheet.GetCell(range.Start)?.Value);
        _validationDropdown.SelectedItem = items.FirstOrDefault(item =>
            string.Equals(item, currentText, StringComparison.OrdinalIgnoreCase));
        _suppressValidationDropdownCommit = false;

        var width = Math.Max(18, Math.Min(rect.Width, 160));
        System.Windows.Controls.Canvas.SetLeft(_validationDropdown, rect.Right - width);
        System.Windows.Controls.Canvas.SetTop(_validationDropdown, rect.Top);
        _validationDropdown.Width = width;
        _validationDropdown.Height = Math.Max(18, rect.Height);
        _validationDropdown.Visibility = Visibility.Visible;
        EditOverlay.IsHitTestVisible = true;
    }

    private void EnsureValidationDropdown()
    {
        if (_validationDropdown is not null)
            return;

        _validationDropdown = new System.Windows.Controls.ComboBox
        {
            FontSize = 12,
            Padding = new System.Windows.Thickness(0),
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(33, 115, 70)),
            BorderThickness = new System.Windows.Thickness(1),
            MaxDropDownHeight = 220,
            ToolTip = "Pick from list"
        };
        _validationDropdown.SelectionChanged += ValidationDropdown_SelectionChanged;
        EditOverlay.Children.Add(_validationDropdown);
    }

    private void HideValidationDropdown()
    {
        if (_validationDropdown is not null)
            _validationDropdown.Visibility = Visibility.Collapsed;

        if (_inlineEditor?.IsVisible != true)
            EditOverlay.IsHitTestVisible = false;
    }

    private void OpenActiveDropdown()
    {
        RefreshValidationDropdown();
        if (_validationDropdown?.Visibility == Visibility.Visible)
        {
            _validationDropdown.Focus();
            _validationDropdown.IsDropDownOpen = true;
            return;
        }

        OpenAutoFilterDropdownForActiveCell();
    }

    private void OpenAutoFilterDropdownForActiveCell()
    {
        if (SheetGrid.SelectedRange?.Start is not { } activeCell ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet ||
            SelectionRangeService.GetCurrentRegion(sheet, activeCell) is not { } currentRegion ||
            !AutoFilterDropdownPlanner.TryPlan(currentRegion, activeCell, out var plan))
        {
            return;
        }

        var menuPlan = AutoFilterDropdownPlanner.CreateMenuPlan(_workbook, sheet, plan);
        if (menuPlan.Entries.All(entry => entry.Kind != AutoFilterMenuEntryKind.ChecklistItem))
            return;

        var dialog = new AutoFilterDialog(menuPlan)
        {
            Owner = this
        };
        PositionAutoFilterDialogAtActiveCell(dialog, activeCell);

        if (dialog.ShowDialog() != true)
            return;

        if (!ApplyAutoFilterDialogResult(plan.Range, plan.FilterColumnOffset, dialog.Result, "AutoFilter"))
            return;
        UpdateViewport();
    }

    private void PositionAutoFilterDialogAtActiveCell(Window dialog, CellAddress activeCell)
    {
        if (TryGetCellOverlayRect(activeCell) is not { } rect)
            return;

        var screenPoint = SheetGrid.PointToScreen(new System.Windows.Point(rect.Left, rect.Bottom));
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            screenPoint = target.TransformFromDevice.Transform(screenPoint);

        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = screenPoint.X;
        dialog.Top = screenPoint.Y;
    }

    private Rect? TryGetCellOverlayRect(CellAddress addr)
    {
        var vp = SheetGrid.Viewport;
        if (vp is null)
            return null;

        var rowMetric = vp.RowMetrics.FirstOrDefault(r => r.Row == addr.Row);
        var colMetric = vp.ColMetrics.FirstOrDefault(c => c.Col == addr.Col);
        if (rowMetric is null || colMetric is null)
            return null;

        var left = colMetric.LeftOffset + SheetGrid.ActualRowHeaderWidth;
        var top = rowMetric.TopOffset + Freexcel.App.UI.GridView.ColHeaderHeight;
        return new Rect(left, top, colMetric.Width, rowMetric.Height);
    }

    private void ValidationDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressValidationDropdownCommit ||
            _validationDropdown?.SelectedItem is not string selected ||
            SheetGrid.SelectedRange is not { } range)
        {
            return;
        }

        FormulaBar.Text = selected;
        CommitEdit();
        SetActiveCell(range.Start);
    }
}
