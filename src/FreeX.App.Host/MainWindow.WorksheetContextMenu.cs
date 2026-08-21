using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.Shell;
using FreeX.App.UI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // ── Context menu + Insert/Delete ─────────────────────────────────────────

    private void OnGridContextMenuRequested(CellAddress clickedCell, System.Windows.Point gridPos)
    {
        var actualAddr = new CellAddress(_currentSheetId, clickedCell.Row, clickedCell.Col);
        if (SheetGrid.SelectedRange is not { } selectedRange || !selectedRange.Contains(actualAddr))
            SetActiveCell(actualAddr);

        HideValidationDropdown();
        ClearCommentPreview();

        var targetKind = GetWorksheetContextMenuTargetKind(actualAddr);
        var state = GetWorksheetContextMenuState(actualAddr);
        var menu = new ContextMenu();
        // Planner commands → shared declarative RibbonMenu (same model as ribbon dropdowns) → WPF items.
        var commands = WorksheetContextMenuPlanner.BuildCommands(targetKind, state);
        var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);
        WorksheetContextMenuRenderer.AddItems(
            menu.Items,
            ribbonMenu.Items,
            action => ExecuteWorksheetContextMenuAction(action, actualAddr));
        WorksheetContextMenuRenderer.AddSearchBox(menu);

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>()
            .Where(item => !WorksheetContextMenuRenderer.IsSearchMenuItem(item)));
        menu.PlacementTarget = SheetGrid;
        menu.Opened += WorksheetContextMenu_Opened;
        menu.Closed += (_, _) =>
        {
            CloseWorksheetContextMiniToolbar();
            if (ReferenceEquals(SheetGrid.ContextMenu, menu))
                SheetGrid.ContextMenu = null;
        };
        SheetGrid.ContextMenu = menu;
        PositionWorksheetContextMenu(menu, gridPos);
        if (!_suppressWorksheetContextMiniToolbar)
            ShowWorksheetContextMiniToolbar(targetKind, gridPos);
        menu.IsOpen = true;
    }

    private void OnWaterfallChartPointContextMenuRequested(ChartModel chart, int pointIndex, System.Windows.Point gridPos)
    {
        HideValidationDropdown();
        ClearCommentPreview();

        var menu = new ContextMenu();
        // Planner commands → shared declarative RibbonMenu (same model as the cell menu) → WPF items.
        // The single "Set as Total" item is checkable; its toggle command id dispatches to the undoable toggle.
        var commands = WaterfallChartContextMenuPlanner.BuildCommands(chart, pointIndex);
        var ribbonMenu = WaterfallChartContextMenuRibbonAdapter.ToRibbonMenu(commands);
        WorksheetContextMenuRenderer.AddItemsByCommandId(
            menu.Items,
            ribbonMenu.Items,
            _ => ToggleWaterfallTotalPoint(chart.Id, pointIndex));

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        menu.PlacementTarget = SheetGrid;
        menu.Opened += WorksheetContextMenu_Opened;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(SheetGrid.ContextMenu, menu))
                SheetGrid.ContextMenu = null;
        };
        SheetGrid.ContextMenu = menu;
        PositionWorksheetContextMenu(menu, gridPos);
        menu.IsOpen = true;
    }

    private void ToggleWaterfallTotalPoint(Guid chartId, int pointIndex)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        foreach (var chart in sheet.Charts)
        {
            if (chart.Id != chartId)
                continue;

            var command = WaterfallChartContextMenuPlanner.CreateToggleCommand(
                _currentSheetId,
                chart,
                pointIndex);
            if (command is null || !TryExecuteCommand(command, "Set as Total"))
                return;

            UpdateViewport();
            return;
        }
    }

    private void OnGridHeaderContextMenuRequested(GridHeaderContextMenuTarget target, uint index, System.Windows.Point gridPos)
    {
        var address = target == GridHeaderContextMenuTarget.Row
            ? new CellAddress(_currentSheetId, index, 1)
            : new CellAddress(_currentSheetId, 1, index);

        if (target == GridHeaderContextMenuTarget.Row && !ShouldPreserveHeaderContextSelection(target, index))
            SelectRow(index);
        else if (target == GridHeaderContextMenuTarget.Column && !ShouldPreserveHeaderContextSelection(target, index))
            SelectColumn(index);

        OnGridContextMenuRequested(address, gridPos);
    }

    private bool ShouldPreserveHeaderContextSelection(GridHeaderContextMenuTarget target, uint index)
    {
        if (SheetGrid.SelectedRange is not { } selectedRange)
            return false;

        return target switch
        {
            GridHeaderContextMenuTarget.Row =>
                SelectionRangeService.IsWholeRowSelection(selectedRange) &&
                index >= selectedRange.Start.Row &&
                index <= selectedRange.End.Row,
            GridHeaderContextMenuTarget.Column =>
                SelectionRangeService.IsWholeColumnSelection(selectedRange) &&
                index >= selectedRange.Start.Col &&
                index <= selectedRange.End.Col,
            _ => false
        };
    }

    private static void WorksheetContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        FocusFirstWorksheetContextMenuItem(menu);
        menu.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => FocusFirstWorksheetContextMenuItem(menu)));
    }

    private static void FocusFirstWorksheetContextMenuItem(ContextMenu menu)
    {
        MenuItem? firstEnabledItem = null;
        foreach (var item in menu.Items)
        {
            if (item is not MenuItem menuItem ||
                !menuItem.IsEnabled ||
                WorksheetContextMenuRenderer.IsSearchMenuItem(menuItem))
                continue;

            firstEnabledItem = menuItem;
            break;
        }

        if (firstEnabledItem is null)
            return;

        FocusManager.SetFocusedElement(menu, firstEnabledItem);
        firstEnabledItem.Focus();
        Keyboard.Focus(firstEnabledItem);
    }

    private async void ExecuteWorksheetContextMenuAction(WorksheetContextMenuAction action, CellAddress address)
    {
        if (WorkbookApplicationCommandRouter.TryRouteWorksheetContextMenu(action.ToString(), out var route))
        {
            await WorkbookApplicationCommands.TryExecuteAsync(route, targetAddress: address);
            return;
        }

        switch (action)
        {
            case WorksheetContextMenuAction.DeleteObject:
                TryDeleteSelectedDrawingObject();
                break;
            case WorksheetContextMenuAction.FormatPicture:
                PictureSizeBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.CropPicture:
                PictureCropBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ResetPictureCrop:
                PictureResetCropMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.FormatDrawingObject:
            case WorksheetContextMenuAction.ResizeDrawingObject:
                ObjectSizeBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.RotateDrawingObject:
                ObjectRotateBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ShapeFill:
                ObjectFillBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ShapeOutline:
                ObjectOutlineBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.FormatChartArea:
                FormatChartAreaBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.SelectChartData:
                SelectChartDataSourceBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ChangeChartType:
                ChangeChartTypeBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ChartStyles:
                ChartStylesBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ChartTitles:
                ChartTitlesBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ChartSizeAndProperties:
                ResizeSelectedChartObject();
                break;
            case WorksheetContextMenuAction.MoveChart:
                MoveChartBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.BringForward:
                BringForwardBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.SendBackward:
                SendBackwardBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.EditAltText:
                SetAltTextBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.SelectionPane:
                SelectionPaneBtn_Click(this, new RoutedEventArgs());
                break;
        }
    }

    private void OpenKeyboardContextMenu()
    {
        if (TryOpenFocusedSheetTabContextMenu())
            return;

        var address = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
        _suppressWorksheetContextMiniToolbar = true;
        try
        {
            OnGridContextMenuRequested(address, GetKeyboardContextMenuGridPoint(address));
        }
        finally
        {
            _suppressWorksheetContextMiniToolbar = false;
        }
    }

    private void ResolveContextThreadedComment(CellAddress address, bool resolved)
    {
        var fallbackRange = new GridRange(address, address);
        if (!TryExecuteRepeatableCurrentRangeCommand(
                resolved ? "Resolve Comment" : "Unresolve Comment",
                fallbackRange,
                range => new ResolveThreadedCommentCommand(_currentSheetId, range.Start, resolved)))
            return;

        UpdateViewport();
    }

    private System.Windows.Point GetKeyboardContextMenuGridPoint(CellAddress address)
    {
        return TryGetCellOverlayRect(address) is { } rect
            ? new System.Windows.Point(rect.Left, rect.Bottom)
            : new System.Windows.Point();
    }

    private void PositionWorksheetContextMenu(ContextMenu menu, System.Windows.Point gridPos)
    {
        var screenPoint = SheetGrid.PointToScreen(gridPos);
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            screenPoint = target.TransformFromDevice.Transform(screenPoint);

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = screenPoint.X;
        menu.VerticalOffset = screenPoint.Y;
    }

    private WorksheetContextMenuTargetKind GetWorksheetContextMenuTargetKind(CellAddress address)
    {
        if (SheetGrid.SelectedRange is { } selectedRange)
        {
            if (SelectionRangeService.IsWholeRowSelection(selectedRange))
                return WorksheetContextMenuTargetKind.RowSelection;
            if (SelectionRangeService.IsWholeColumnSelection(selectedRange))
                return WorksheetContextMenuTargetKind.ColumnSelection;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (GetSelectedWorksheetContextMenuTargetKind(sheet, address) is { } selectedObjectKind)
            return selectedObjectKind;

        if (DrawingTargetResolver.GetTargetPicture(sheet, address, allowFallback: false) is not null)
            return WorksheetContextMenuTargetKind.Picture;

        return DrawingTargetResolver.GetTargetDrawingObject(
            sheet,
            address,
            allowFallback: false)?.Kind switch
        {
            DrawingObjectTargetKind.Shape => WorksheetContextMenuTargetKind.Shape,
            DrawingObjectTargetKind.TextBox => WorksheetContextMenuTargetKind.TextBox,
            _ => WorksheetContextMenuTargetKind.Worksheet
        };
    }

    private WorksheetContextMenuTargetKind? GetSelectedWorksheetContextMenuTargetKind(Sheet? sheet, CellAddress address)
    {
        if (SheetGrid.SelectedObjectId == Guid.Empty ||
            SheetGrid.SelectedObjectKind == FreeX.App.UI.ObjectKind.None)
        {
            return null;
        }

        if (SheetGrid.SelectedObjectKind == FreeX.App.UI.ObjectKind.Chart)
            return ChartWorkflowTargetPlanner.HasSelectedChart(sheet, GetSelectedChartIdOnCurrentSheet())
                ? WorksheetContextMenuTargetKind.Chart
                : null;

        if (GetSelectedDrawingObjectTargetKind() is not { } selectedKind)
            return null;

        var target = DrawingTargetResolver.GetTargetDrawingObject(
            sheet,
            address,
            selectedKind,
            SheetGrid.SelectedObjectId,
            includePictures: true,
            allowFallback: false);
        if (target is null ||
            target.Anchor.Row != address.Row ||
            target.Anchor.Col != address.Col)
        {
            return null;
        }

        return target.Kind switch
        {
            DrawingObjectTargetKind.Picture => WorksheetContextMenuTargetKind.Picture,
            DrawingObjectTargetKind.Shape => WorksheetContextMenuTargetKind.Shape,
            DrawingObjectTargetKind.TextBox => WorksheetContextMenuTargetKind.TextBox,
            _ => null
        };
    }

    private WorksheetContextMenuState GetWorksheetContextMenuState(CellAddress address)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return WorksheetContextMenuState.Default;

        return WorksheetContextMenuPlanner.ResolveWorksheetState(sheet, address);
    }
}
