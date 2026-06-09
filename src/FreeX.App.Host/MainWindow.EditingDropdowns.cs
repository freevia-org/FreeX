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
            _workbook.GetSheet(_currentSheetId) is not { } sheet)
        {
            return;
        }

        ShowAutoFilterDropdownForHeaderCell(sheet, activeCell);
    }

    private void OnAutoFilterDropdownRequested(CellAddress headerCell, System.Windows.Point position)
    {
        if (_workbook.GetSheet(_currentSheetId) is not { } sheet)
            return;

        SheetGrid.SelectedRange = new GridRange(headerCell, headerCell);
        SheetGrid.SelectedRanges = null;
        _selectionAnchor = headerCell;
        _selectionCursor = headerCell;
        CellAddressBox.Text = headerCell.ToA1();
        ShowAutoFilterDropdownForHeaderCell(sheet, headerCell, position);
    }

    private void ShowAutoFilterDropdownForHeaderCell(
        Sheet sheet,
        CellAddress headerCell,
        System.Windows.Point? anchorPoint = null)
    {
        if (CreateAutoFilterFlyoutDialog(sheet, headerCell, anchorPoint, out var createdPlan) is not { } dialog ||
            createdPlan is not { } plan)
        {
            return;
        }

        dialog.ResultCommitted += (_, result) =>
        {
            if (!ApplyAutoFilterDialogResult(plan.Range, plan.FilterColumnOffset, result, "AutoFilter"))
                return;
            UpdateViewport();
        };
        dialog.Show();
        dialog.Activate();
    }

    private AutoFilterDialog? CreateAutoFilterFlyoutDialog(
        Sheet sheet,
        CellAddress headerCell,
        System.Windows.Point? anchorPoint,
        out AutoFilterDropdownPlan? createdPlan)
    {
        var currentRegion = AutoFilterDropdownPlanner.TryGetAutoFilterRange(sheet, out var autoFilterRange)
            ? autoFilterRange
            : SelectionRangeService.GetCurrentRegion(sheet, headerCell);
        createdPlan = null;
        if (currentRegion is not { } range ||
            !AutoFilterDropdownPlanner.TryPlan(range, headerCell, out var plan))
        {
            return null;
        }

        var menuPlan = AutoFilterDropdownPlanner.CreateMenuPlan(_workbook, sheet, plan);
        if (menuPlan.Entries.All(entry => entry.Kind != AutoFilterMenuEntryKind.ChecklistItem))
            return null;

        var dialog = new AutoFilterDialog(menuPlan)
        {
            Owner = this
        };
        dialog.ConfigureAsModelessFlyout();
        PositionAutoFilterFlyout(dialog, headerCell, anchorPoint);
        createdPlan = plan;
        return dialog;
    }

    private void PositionAutoFilterFlyout(Window dialog, CellAddress headerCell, System.Windows.Point? anchorPoint)
    {
        var point = anchorPoint is { } clickedPoint
            ? new System.Windows.Point(clickedPoint.X, clickedPoint.Y + 18)
            : (System.Windows.Point?)null;
        if (point is null)
        {
            if (TryGetCellOverlayRect(headerCell) is not { } rect)
                return;

            point = new System.Windows.Point(rect.Left, rect.Bottom);
        }

        var screenPoint = SheetGrid.PointToScreen(point.Value);
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            screenPoint = target.TransformFromDevice.Transform(screenPoint);

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
