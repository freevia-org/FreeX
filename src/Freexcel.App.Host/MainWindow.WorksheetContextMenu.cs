using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    // ── Context menu + Insert/Delete ─────────────────────────────────────────

    private void OnGridContextMenuRequested(CellAddress clickedCell, System.Windows.Point gridPos)
    {
        var actualAddr = new CellAddress(_currentSheetId, clickedCell.Row, clickedCell.Col);
        if (SheetGrid.SelectedRange is null)
            SetActiveCell(actualAddr);

        var targetKind = GetWorksheetContextMenuTargetKind(actualAddr);
        var state = GetWorksheetContextMenuState(actualAddr);
        var menu = new ContextMenu();
        foreach (var command in WorksheetContextMenuPlanner.BuildCommands(targetKind, state))
        {
            if (command.IsSeparator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem { Header = command.AccessHeader, IsEnabled = command.IsEnabled };
            item.Click += (_, _) => ExecuteWorksheetContextMenuAction(command.Action, actualAddr);
            menu.Items.Add(item);
        }

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        menu.PlacementTarget = SheetGrid;
        menu.Opened += WorksheetContextMenu_Opened;
        PositionWorksheetContextMenu(menu, gridPos);
        menu.IsOpen = true;
    }

    private static void WorksheetContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        var firstEnabledItem = menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled);
        if (firstEnabledItem is null)
            return;

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
            case WorksheetContextMenuAction.NewComment:
                ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.EditComment:
                ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs());
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
                ReviewShowCommentsBtn_Click(this, new RoutedEventArgs());
                break;
            case WorksheetContextMenuAction.Hyperlink:
                InsertLinkBtn_Click(this, new RoutedEventArgs());
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
            case WorksheetContextMenuAction.ClearContents:
                ExecuteClearSelection();
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
        if (DrawingTargetResolver.GetTargetPicture(sheet, address) is not null)
            return WorksheetContextMenuTargetKind.Picture;

        return DrawingTargetResolver.GetTargetDrawingObject(sheet, address)?.Kind switch
        {
            DrawingObjectTargetKind.Shape => WorksheetContextMenuTargetKind.Shape,
            DrawingObjectTargetKind.TextBox => WorksheetContextMenuTargetKind.TextBox,
            _ => WorksheetContextMenuTargetKind.Worksheet
        };
    }

    private WorksheetContextMenuState GetWorksheetContextMenuState(CellAddress address)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return WorksheetContextMenuState.Default;

        return new WorksheetContextMenuState(
            HasThreadedComment: sheet.ThreadedComments.ContainsKey(address),
            HasNote: sheet.Comments.ContainsKey(address),
            HasHyperlink: sheet.Hyperlinks.ContainsKey(address));
    }
}
