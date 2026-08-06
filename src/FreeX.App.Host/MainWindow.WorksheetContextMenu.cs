using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Filtering;
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

            var setAsTotal = !WaterfallChartContextMenuPlanner.IsPointTotal(chart, pointIndex);
            if (!TryExecuteCommand(
                    new SetWaterfallTotalPointCommand(_currentSheetId, chart.Id, pointIndex, setAsTotal),
                    "Set as Total"))
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
            if (item is not MenuItem menuItem || !menuItem.IsEnabled)
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

    private void ExecuteWorksheetContextMenuAction(WorksheetContextMenuAction action, CellAddress address)
    {
        switch (action)
        {
            case WorksheetContextMenuAction.Cut:
                ExecuteCopy(isCut: true);
                break;
            case WorksheetContextMenuAction.Copy:
                ExecuteCopy();
                break;
            case WorksheetContextMenuAction.Paste:
                ExecutePaste();
                break;
            case WorksheetContextMenuAction.PasteSpecial:
                PasteSpecialBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.InsertCopiedCells:
                ExecuteInsertCopiedCells();
                break;
            case WorksheetContextMenuAction.InsertCells:
                InsertCellsMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.InsertRowAbove:
                InsertRows(address.Row);
                break;
            case WorksheetContextMenuAction.InsertRowBelow:
                InsertRows(address.Row + 1);
                break;
            case WorksheetContextMenuAction.InsertColumnLeft:
                InsertColumns(address.Col);
                break;
            case WorksheetContextMenuAction.InsertColumnRight:
                InsertColumns(address.Col + 1);
                break;
            case WorksheetContextMenuAction.DeleteCells:
                DeleteCellsMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.DeleteRows:
                DeleteSelectedRows();
                break;
            case WorksheetContextMenuAction.DeleteColumns:
                DeleteSelectedColumns();
                break;
            case WorksheetContextMenuAction.SortAscending:
                SortAscButton_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.SortDescending:
                SortDescButton_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.CustomSort:
                SortCustomMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.Filter:
                FilterButton_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ClearFilter:
                ClearFilterButton_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ReapplyFilter:
                FilterReapplyMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.PickFromDropDown:
                OpenActiveDropdown();
                break;
            case WorksheetContextMenuAction.QuickAnalysis:
                ShowQuickAnalysisMenu();
                break;
            case WorksheetContextMenuAction.DefineName:
                DefineNameBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.CreateTable:
                TableBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.FormatAsTable:
                FormatTableBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.TextToColumns:
                TextToColumnsBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.RemoveDuplicates:
                RemoveDuplicatesBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.DataValidation:
                ValidationButton_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.HideRows:
                ExecuteRowsHidden(hidden: true);
                break;
            case WorksheetContextMenuAction.UnhideRows:
                ExecuteRowsHidden(hidden: false);
                break;
            case WorksheetContextMenuAction.RowHeight:
                FormatRowHeightMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.AutoFitRowHeight:
                FormatAutoRowMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.HideColumns:
                ExecuteColumnsHidden(hidden: true);
                break;
            case WorksheetContextMenuAction.UnhideColumns:
                ExecuteColumnsHidden(hidden: false);
                break;
            case WorksheetContextMenuAction.ColumnWidth:
                FormatColWidthMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.AutoFitColumnWidth:
                FormatAutoColMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.Group:
                GroupRowsBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.Ungroup:
                UngroupRowsBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.NewComment:
                ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.EditComment:
                ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ResolveComment:
                ResolveContextThreadedComment(address, resolved: true);
                break;
            case WorksheetContextMenuAction.UnresolveComment:
                ResolveContextThreadedComment(address, resolved: false);
                break;
            case WorksheetContextMenuAction.DeleteComment:
                ReviewDeleteThreadedCommentBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.NewNote:
                ReviewNewCommentBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.EditNote:
                ReviewNewCommentBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.DeleteNote:
                ReviewDeleteCommentBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ShowNotes:
                ReviewShowNotesBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ShowHideNote:
                ExecuteShowHideNote(address);
                break;
            case WorksheetContextMenuAction.ShowAllNotes:
                ExecuteShowAllNotes();
                break;
            case WorksheetContextMenuAction.OpenHyperlink:
                TryOpenHyperlink(address);
                break;
            case WorksheetContextMenuAction.Hyperlink:
                InsertLinkBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.PivotTableOptions:
                ShowPivotTableOptionsDialog(address);
                break;
            case WorksheetContextMenuAction.FormatCells:
                OpenFormatCellsDialog();
                break;
            case WorksheetContextMenuAction.ClearAll:
                ClearAllMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ClearFormats:
                ClearFormats();
                break;
            case WorksheetContextMenuAction.ClearComments:
                ClearCommentsMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ClearHyperlinks:
                ClearHyperlinksMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.RemoveHyperlinks:
                // Excel's right-click "Remove Hyperlink" removes only the link and keeps the
                // cell's visible formatting (blue/underline); only Home>Clear>Clear Hyperlinks
                // strips that formatting. Route to the dedicated format-preserving handler.
                RemoveHyperlinkMenuItem_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.ClearContents:
                ExecuteClearSelection();
                break;
            case WorksheetContextMenuAction.DeleteObject:
                // R121-model-drawing-delete-1: the picture/shape/text box/chart context menu's own
                // "Delete" entry -- the right-click already selected the object (SheetGrid.SelectedObjectId
                // /-Kind), same precondition FormatPicture/ResizeDrawingObject/etc. below rely on.
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
        OnGridContextMenuRequested(address, GetKeyboardContextMenuGridPoint(address));
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

        sheet.ThreadedComments.TryGetValue(address, out var threadedComment);
        var hasAutoFilterHeaderTarget =
            SelectionRangeService.GetCurrentRegion(sheet, address) is { } currentRegion &&
            AutoFilterDropdownMenuPlanner.TryPlan(currentRegion, address, out _);
        var hasValidationDropdown =
            sheet.DataValidations.Count > 0 &&
            DataValidationService.GetApplicable(sheet, address)
                .Any(rule => rule.Type == DvType.List && rule.ShowDropdown);
        var hasPivotTableTarget = PivotUiPlanner.FindPivotTableContainingCell(sheet, address) is not null;
        return new WorksheetContextMenuState(
            HasThreadedComment: threadedComment is not null,
            IsThreadedCommentResolved: threadedComment?.IsResolved == true,
            HasNote: sheet.Comments.ContainsKey(address),
            HasHyperlink: sheet.Hyperlinks.ContainsKey(address),
            HasAutoFilterHeaderTarget: hasAutoFilterHeaderTarget,
            HasDropdownTarget: hasAutoFilterHeaderTarget || hasValidationDropdown,
            HasPivotTableTarget: hasPivotTableTarget,
            NoteIsShown: sheet.ShownComments.Contains(address));
    }
}
