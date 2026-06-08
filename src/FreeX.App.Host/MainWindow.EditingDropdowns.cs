using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void RefreshValidationDropdown()
    {
        if (_inlineEditor?.IsVisible == true)
            return;

        if (_validationDropdown is null &&
            _workbook.GetSheet(_currentSheetId)?.DataValidations.Count == 0)
        {
            return;
        }

        if (SheetGrid.SelectedRange is not { } range ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet)
        {
            HideValidationDropdown();
            return;
        }

        if (TryGetCellOverlayRect(range.Start) is not { } rect)
        {
            HideValidationDropdown();
            return;
        }

        if (!DataValidationDropdownPlanner.TryPlan(
                _workbook,
                sheet,
                range.Start,
                new DataValidationDropdownCellBounds(rect.Left, rect.Top, rect.Width, rect.Height),
                out var plan))
        {
            HideValidationDropdown();
            return;
        }

        EnsureValidationDropdown();

        _suppressValidationDropdownCommit = true;
        _validationDropdown!.ItemsSource = plan.Items;
        _validationDropdown.SelectedItem = plan.SelectedItem;
        _suppressValidationDropdownCommit = false;

        System.Windows.Controls.Canvas.SetLeft(_validationDropdown, plan.Bounds.Left);
        System.Windows.Controls.Canvas.SetTop(_validationDropdown, plan.Bounds.Top);
        _validationDropdown.Width = plan.Bounds.Width;
        _validationDropdown.Height = plan.Bounds.Height;
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
                System.Windows.Media.Color.FromRgb(15, 109, 140)),
            BorderThickness = new System.Windows.Thickness(1),
            MaxDropDownHeight = 220,
            ToolTip = "Pick from list"
        };
        _validationDropdown.SelectionChanged += ValidationDropdown_SelectionChanged;
        EditOverlay.Children.Add(_validationDropdown);
    }

    private void HideValidationDropdown()
    {
        if (_validationDropdown is { Visibility: not Visibility.Collapsed })
            _validationDropdown.Visibility = Visibility.Collapsed;

        if (_inlineEditor?.IsVisible != true && EditOverlay.IsHitTestVisible)
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

        RowMetric? rowMetric = null;
        foreach (var metric in vp.RowMetrics)
        {
            if (metric.Row == addr.Row)
            {
                rowMetric = metric;
                break;
            }
        }

        ColMetric? colMetric = null;
        foreach (var metric in vp.ColMetrics)
        {
            if (metric.Col == addr.Col)
            {
                colMetric = metric;
                break;
            }
        }

        if (rowMetric is null || colMetric is null)
            return null;

        var left = colMetric.LeftOffset + SheetGrid.ActualRowHeaderWidth;
        var top = rowMetric.TopOffset + FreeX.App.UI.GridView.ColHeaderHeight;
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
