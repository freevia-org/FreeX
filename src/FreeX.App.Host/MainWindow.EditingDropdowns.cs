using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void RefreshValidationDropdown()
    {
        if (_inlineEditor?.IsVisible == true ||
            _textBoxInlineEditor?.IsVisible == true)
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

        // Anchor to the RIGHT edge of the cell at exactly ArrowButtonWidth px wide —
        // this makes the ComboBox appear as Excel's in-cell dropdown-arrow button.
        var zoom = _zoomLevel;
        var btnWidth = DataValidationAffordancePlanner.ArrowButtonWidth * zoom;
        var btnLeft = (rect.Left + rect.Width) * zoom - btnWidth;
        var btnTop = rect.Top * zoom;
        var btnHeight = rect.Height * zoom;

        System.Windows.Controls.Canvas.SetLeft(_validationDropdown, btnLeft);
        System.Windows.Controls.Canvas.SetTop(_validationDropdown, btnTop);
        _validationDropdown.Width = btnWidth;
        _validationDropdown.Height = Math.Max(DataValidationDropdownPlanner.MinimumHeight, btnHeight);
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
                System.Windows.Media.Color.FromRgb(120, 120, 120)),
            BorderThickness = new System.Windows.Thickness(1),
            MaxDropDownHeight = 220,
            ToolTip = UiText.CreateAutomationName(UiText.Get("DataValidation_InCellDropdown"))
        };
        AutomationProperties.SetAutomationId(_validationDropdown, "WorksheetDataValidationDropdown");
        AutomationProperties.SetName(_validationDropdown, UiText.CreateAutomationName(UiText.Get("DataValidation_InCellDropdown")));
        AutomationProperties.SetHelpText(_validationDropdown, UiText.CreateAutomationName(UiText.Get("DataValidation_InCellDropdown")));
        _validationDropdown.SelectionChanged += ValidationDropdown_SelectionChanged;
        EditOverlay.Children.Add(_validationDropdown);
    }

    private void HideValidationDropdown()
    {
        if (_validationDropdown is { Visibility: not Visibility.Collapsed })
            _validationDropdown.Visibility = Visibility.Collapsed;

        if (_inlineEditor?.IsVisible != true &&
            _textBoxInlineEditor?.IsVisible != true &&
            EditOverlay.IsHitTestVisible)
            EditOverlay.IsHitTestVisible = false;
    }

    // ── DV input-message floating tooltip ────────────────────────────────────

    /// <summary>
    /// Shows or refreshes the floating input-message tooltip for the active cell if it has a
    /// DV input message. The box appears below-right of the active cell, similar to the comment
    /// preview. Dismissed on selection change or when there is no prompt.
    /// </summary>
    private void RefreshDvInputMessage()
    {
        if (_workbook.GetSheet(_currentSheetId) is not { } sheet ||
            SheetGrid.SelectedRange is not { } range)
        {
            HideDvInputMessage();
            return;
        }

        var prompt = DataValidationAffordancePlanner.GetInputMessagePrompt(sheet, range.Start);
        if (prompt is null)
        {
            HideDvInputMessage();
            return;
        }

        if (TryGetCellOverlayRect(range.Start) is not { } cellRect)
        {
            HideDvInputMessage();
            return;
        }

        EnsureDvInputMessageBorder();
        BuildDvInputMessageContent(prompt.Value);
        PositionDvInputMessage(cellRect);
        _dvInputMessageBorder!.Visibility = Visibility.Visible;
    }

    private void HideDvInputMessage()
    {
        if (_dvInputMessageBorder is { Visibility: not Visibility.Collapsed })
            _dvInputMessageBorder.Visibility = Visibility.Collapsed;
    }

    private void EnsureDvInputMessageBorder()
    {
        if (_dvInputMessageBorder is not null)
            return;

        _dvInputMessageBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 255, 225)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(158, 151, 113)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect
            {
                BlurRadius = 6,
                Direction = 315,
                Opacity = 0.20,
                ShadowDepth = 2
            }
        };
        AutomationProperties.SetAutomationId(_dvInputMessageBorder, "WorksheetDvInputMessagePopup");
        AutomationProperties.SetName(_dvInputMessageBorder, UiText.CreateAutomationName(UiText.Get("DataValidation_InputMessage")));
        CommentOverlay.Children.Add(_dvInputMessageBorder);
    }

    private void BuildDvInputMessageContent(DataValidationService.InputPrompt prompt)
    {
        var panel = new StackPanel { MaxWidth = 200 };

        if (prompt.Title.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = prompt.Title,
                FontWeight = System.Windows.FontWeights.Bold,
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, prompt.Message.Length > 0 ? 3 : 0)
            });
        }

        if (prompt.Message.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = prompt.Message,
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Black,
                TextWrapping = TextWrapping.Wrap
            });
        }

        _dvInputMessageBorder!.Child = panel;
    }

    private void PositionDvInputMessage(Rect cellRect)
    {
        if (_dvInputMessageBorder is null)
            return;

        var zoom = _zoomLevel;
        var scaledBottom = (cellRect.Top + cellRect.Height) * zoom;
        var scaledLeft = cellRect.Left * zoom;
        const double boxWidth = 160;
        const double maxBoxHeight = 120;

        // Prefer below-left of cell; shift up if it would go off-screen bottom.
        var left = Math.Max(0, Math.Min(scaledLeft, Math.Max(0, CommentOverlay.ActualWidth - boxWidth)));
        var top = scaledBottom + 2;
        if (top + maxBoxHeight > CommentOverlay.ActualHeight)
            top = Math.Max(0, (cellRect.Top * zoom) - maxBoxHeight - 2);

        _dvInputMessageBorder.Width = boxWidth;
        _dvInputMessageBorder.MaxHeight = maxBoxHeight;
        System.Windows.Controls.Canvas.SetLeft(_dvInputMessageBorder, left);
        System.Windows.Controls.Canvas.SetTop(_dvInputMessageBorder, top);
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

        if (OpenAutoFilterDropdownForActiveCell())
            return;

        // R88-app-autocomplete-picklist-5-1: Excel's classic "Pick From Drop-down List" -- a plain
        // cell with adjacent text entries and no Data Validation rule and no AutoFilter header still
        // offers a pick list built from the contiguous column text block, independent of both
        // features above.
        OpenTextEntryPickListDropdown();
    }

    private bool OpenAutoFilterDropdownForActiveCell()
    {
        if (SheetGrid.SelectedRange?.Start is not { } activeCell ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet)
        {
            return false;
        }

        return ShowAutoFilterDropdownForHeaderCell(sheet, activeCell);
    }

    /// <summary>
    /// R88-app-autocomplete-picklist-5-1: Excel's "Pick From Drop-down List" (Alt+Down / right-click)
    /// for a plain cell with no Data Validation rule and no AutoFilter header -- lists the unique
    /// text entries already present in the active cell's contiguous column block (the same block
    /// <see cref="ApplyCellValueAutoCompleteSuggestion"/> draws AutoComplete candidates from) and
    /// commits the chosen entry into the active cell on selection via the same
    /// <see cref="ValidationDropdown_SelectionChanged"/> handler the Data Validation dropdown uses.
    /// </summary>
    private void OpenTextEntryPickListDropdown()
    {
        if (_inlineEditor?.IsVisible == true || _textBoxInlineEditor?.IsVisible == true)
            return;

        if (SheetGrid.SelectedRange?.Start is not { } activeCell ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet)
        {
            return;
        }

        if (TryGetCellOverlayRect(activeCell) is not { } rect)
            return;

        var items = BuildTextEntryPickListItems(sheet, activeCell);
        if (items.Count == 0)
            return;

        EnsureValidationDropdown();

        _suppressValidationDropdownCommit = true;
        _validationDropdown!.ItemsSource = items;
        _validationDropdown.SelectedItem = null;
        _suppressValidationDropdownCommit = false;

        // Anchored/sized exactly like RefreshValidationDropdown's in-cell dropdown-arrow button.
        var zoom = _zoomLevel;
        var btnWidth = DataValidationAffordancePlanner.ArrowButtonWidth * zoom;
        var btnLeft = (rect.Left + rect.Width) * zoom - btnWidth;
        var btnTop = rect.Top * zoom;
        var btnHeight = rect.Height * zoom;

        System.Windows.Controls.Canvas.SetLeft(_validationDropdown, btnLeft);
        System.Windows.Controls.Canvas.SetTop(_validationDropdown, btnTop);
        _validationDropdown.Width = btnWidth;
        _validationDropdown.Height = Math.Max(DataValidationDropdownPlanner.MinimumHeight, btnHeight);
        _validationDropdown.Visibility = Visibility.Visible;
        EditOverlay.IsHitTestVisible = true;

        _validationDropdown.Focus();
        _validationDropdown.IsDropDownOpen = true;
    }

    /// <summary>
    /// Builds the unique, order-preserving list of text entries the plain-cell pick list offers:
    /// the contiguous column text block, deduplicated case-insensitively (first occurrence wins) --
    /// the pick list shows each distinct existing entry once regardless of how many times it repeats
    /// in the column.
    /// </summary>
    private static IReadOnlyList<string> BuildTextEntryPickListItems(Sheet sheet, CellAddress activeCell)
        => DataValidationDropdownPlanner.GetTextEntryPickListItems(sheet, activeCell);

    private void OnAutoFilterDropdownRequested(CellAddress headerCell, System.Windows.Point position)
    {
        if (_workbook.GetSheet(_currentSheetId) is not { } sheet)
            return;

        if (!ShouldPreserveAutoFilterSelection(sheet, headerCell, SheetGrid.SelectedRange))
        {
            SheetGrid.SelectedRange = new GridRange(headerCell, headerCell);
            SheetGrid.SelectedRanges = null;
            _selectionAnchor = headerCell;
            _selectionCursor = headerCell;
            CellAddressBox.Text = headerCell.ToA1();
        }

        ShowAutoFilterDropdownForHeaderCell(sheet, headerCell, position);
    }

    private static bool ShouldPreserveAutoFilterSelection(Sheet sheet, CellAddress headerCell, GridRange? selectedRange)
    {
        if (selectedRange is not { } range ||
            !range.Contains(headerCell))
        {
            return false;
        }

        if (AutoFilterDropdownMenuPlanner.TryGetAutoFilterRange(sheet, out var autoFilterRange))
            return range == autoFilterRange;

        return SelectionRangeService.GetCurrentRegion(sheet, headerCell) is { } currentRegion &&
               range == currentRegion;
    }

    private bool ShowAutoFilterDropdownForHeaderCell(
        Sheet sheet,
        CellAddress headerCell,
        System.Windows.Point? anchorPoint = null)
    {
        if (CreateAutoFilterFlyoutDialog(sheet, headerCell, anchorPoint, out var createdPlan) is not { } dialog ||
            createdPlan is not { } plan)
        {
            return false;
        }

        dialog.ResultCommitted += (_, result) =>
        {
            if (!ApplyAutoFilterDialogResult(plan.Range, plan.FilterColumnOffset, result, "AutoFilter"))
                return;
            UpdateViewport();
            RefreshStatusBar();
        };

        // Track the flyout so a sheet switch can dismiss it (it is a separate modeless window that
        // otherwise keeps floating over the newly-activated sheet).
        _autoFilterDropdown = dialog;
        _autoFilterDropdownSheetId = _currentSheetId;
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_autoFilterDropdown, dialog))
                _autoFilterDropdown = null;
        };

        dialog.Show();
        dialog.Activate();
        return true;
    }

    /// <summary>
    /// Closes the open AutoFilter dropdown flyout when the active sheet has changed since it was
    /// opened. Called from <see cref="UpdateViewport"/>; the sheet-id guard means same-sheet
    /// viewport refreshes (scroll, resize, filter apply) leave the flyout open.
    /// </summary>
    private void CloseAutoFilterDropdownOnSheetChange()
    {
        if (_autoFilterDropdown is not null && !_autoFilterDropdownSheetId.Equals(_currentSheetId))
            CloseAutoFilterDropdown();
    }

    /// <summary>
    /// Closes the open AutoFilter dropdown flyout, if any. The flyout self-dismisses on deactivation
    /// (click anywhere outside it), but events that move the anchored cell without changing window
    /// activation — scrolling the grid, a programmatic sheet switch — must dismiss it explicitly so it
    /// never floats detached from its column header.
    /// </summary>
    private void CloseAutoFilterDropdown()
    {
        if (_autoFilterDropdown is { } dialog)
        {
            _autoFilterDropdown = null;
            dialog.Close();
        }
    }

    private AutoFilterDialog? CreateAutoFilterFlyoutDialog(
        Sheet sheet,
        CellAddress headerCell,
        System.Windows.Point? anchorPoint,
        out AutoFilterDropdownPlan? createdPlan)
    {
        var currentRegion = AutoFilterDropdownMenuPlanner.TryGetAutoFilterRange(sheet, out var autoFilterRange)
            ? autoFilterRange
            : SelectionRangeService.GetCurrentRegion(sheet, headerCell);
        createdPlan = null;
        if (currentRegion is not { } range ||
            !AutoFilterDropdownMenuPlanner.TryPlan(range, headerCell, out var plan))
        {
            return null;
        }

        var menuPlan = AutoFilterDropdownMenuPlanner.CreateMenuPlan(
            _workbook,
            sheet,
            plan,
            WpfResourceKeyTextResolver.Resources.AutoFilter,
            WpfResourceKeyTextResolver.Resources.AutoFilter.BlankDisplayText);
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
        AutoFilterPopupPlacement placement;
        if (anchorPoint is { } clickedPoint)
        {
            placement = AutoFilterPopupPlacementPlanner.FromPointer(
                new LayoutPoint(clickedPoint.X, clickedPoint.Y));
        }
        else
        {
            if (TryGetCellOverlayRect(headerCell) is not { } rect)
                return;

            placement = AutoFilterPopupPlacementPlanner.FromHeaderBounds(
                new LayoutRect(rect.Left, rect.Top, rect.Width, rect.Height));
        }

        var screenPoint = SheetGrid.PointToScreen(
            new System.Windows.Point(placement.Anchor.X, placement.Anchor.Y));
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            screenPoint = target.TransformFromDevice.Transform(screenPoint);

        dialog.Left = screenPoint.X;
        dialog.Top = screenPoint.Y;
    }

    // Resolves the row/col address span a cell-anchored overlay (DV in-cell dropdown arrow,
    // input-message tooltip, autofilter flyout) must cover: the WHOLE merged block if addr sits
    // inside one, otherwise just the single cell itself. Excel renders these overlays against the
    // full merged cell, not just its own row/column metrics (R52-commands-data-validation-
    // apply-3-4).
    private (CellAddress Start, CellAddress End) GetOverlayAddressRange(Sheet? sheet, CellAddress addr) =>
        sheet is { MergedRegions.Count: > 0 } && sheet.GetMergeRegion(addr) is { } merge
            ? (merge.Start, merge.End)
            : (addr, addr);

    private Rect? TryGetCellOverlayRect(CellAddress addr)
    {
        var vp = SheetGrid.Viewport;
        if (vp is null)
            return null;

        var (startAddr, endAddr) = GetOverlayAddressRange(_workbook.GetSheet(_currentSheetId), addr);

        RowMetric? rowMetric = null;
        RowMetric? endRowMetric = null;
        foreach (var metric in vp.RowMetrics)
        {
            if (metric.Row == startAddr.Row)
                rowMetric = metric;
            if (metric.Row == endAddr.Row)
                endRowMetric = metric;
        }

        ColMetric? colMetric = null;
        ColMetric? endColMetric = null;
        foreach (var metric in vp.ColMetrics)
        {
            if (metric.Col == startAddr.Col)
                colMetric = metric;
            if (metric.Col == endAddr.Col)
                endColMetric = metric;
        }

        if (rowMetric is null || colMetric is null)
            return null;

        var left = colMetric.LeftOffset + SheetGrid.ActualRowHeaderWidth;
        var top = rowMetric.TopOffset + FreeX.App.UI.GridView.ColHeaderHeight;
        var width = endColMetric is { } lastCol ? lastCol.LeftOffset + lastCol.Width - colMetric.LeftOffset : colMetric.Width;
        var height = endRowMetric is { } lastRow ? lastRow.TopOffset + lastRow.Height - rowMetric.TopOffset : rowMetric.Height;
        return new Rect(left, top, width, height);
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
