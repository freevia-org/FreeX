using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const string PivotFieldDragFormat = "FreeX.PivotFieldDragPayload";

    private void PivotTableBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var sourcePlan = PivotCreatePlanner.CreateSourceRangePlan(sheet, SheetGrid.SelectedRange);
        if (!sourcePlan.IsValid || sourcePlan.SourceRange is not { } sourceRange)
        {
            ShowPivotTableSourceRangeError(sourcePlan.Error);
            return;
        }
        var activeSheet = sheet!;

        PivotTableDialog? dialog = null;
        dialog = new PivotTableDialog(
            _workbook,
            _currentSheetId,
            sourceRange,
            request => ApplyPivotTableRangeSelection(dialog, request)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryParseWorkbookRange(_currentSheetId, dialog.Result.SourceRangeText, out var dialogSourceRange))
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_PivotTableInvalidSourceRange"),
                UiText.Get("MainWindowMessage_InsertPivotTableTitle"));
            return;
        }

        var sourceSheet = _workbook.GetSheet(dialogSourceRange.Start.Sheet) ?? activeSheet;
        var layout = PivotCreatePlanner.CreateDefaultLayout(sourceSheet, dialogSourceRange);
        var name = PivotCreatePlanner.SuggestName(_workbook);
        if (dialog.Result.DestinationKind == PivotTableDestinationKind.NewWorksheet)
        {
            var command = PivotCreatePlanner.BuildNewWorksheetCommand(
                dialogSourceRange,
                name,
                layout.RowFieldIndexes,
                layout.DataFieldIndexes);

            if (!TryExecuteCommand(command, "Insert PivotTable"))
                return;

            if (command.CreatedSheetId is { } createdSheetId)
                ActivateNewWorksheetAtA1(createdSheetId);

            RefreshSheetTabs();
            UpdateViewport();
            RefreshStatusBar();
            if (dialog.Result.OpenFieldList)
                RefreshPivotFieldListPane();
            return;
        }

        if (!TryParseWorkbookRange(_currentSheetId, dialog.Result.DestinationRangeText, out var targetRange) ||
            targetRange.Start.Sheet != _currentSheetId)
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_PivotTableInvalidDestinationCell"),
                UiText.Get("MainWindowMessage_InsertPivotTableTitle"));
            return;
        }

        if (!TryExecuteCommand(
                PivotCreatePlanner.BuildInPlaceCommand(
                    _currentSheetId,
                    dialogSourceRange,
                    targetRange,
                    name,
                    layout.RowFieldIndexes,
                    layout.DataFieldIndexes),
                "Insert PivotTable"))
            return;

        UpdateViewport();
        if (dialog.Result.OpenFieldList)
            RefreshPivotFieldListPane();
    }

    private void ShowPivotTableSourceRangeError(PivotCreateSourceRangeError error)
    {
        switch (error)
        {
            case PivotCreateSourceRangeError.MissingSource:
                _messageService.ShowInfo(
                    UiText.Get("MainWindowMessage_PivotTableSelectSourceRange"),
                    UiText.Get("MainWindowMessage_InsertPivotTableTitle"));
                break;
            case PivotCreateSourceRangeError.MinimumShape:
                _messageService.ShowInfo(
                    UiText.Get("MainWindowMessage_PivotTableSourceMinimumShape"),
                    UiText.Get("MainWindowMessage_InsertPivotTableTitle"));
                break;
            case PivotCreateSourceRangeError.MissingHeaders:
                _messageService.ShowWarning(
                    UiText.Get("MainWindowMessage_PivotTableInvalidSourceRange"),
                    UiText.Get("MainWindowMessage_InsertPivotTableTitle"));
                break;
        }
    }

    private void ApplyPivotTableRangeSelection(
        PivotTableDialog? dialog,
        PivotTableRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(request.Target, FormatWorkbookRange(selectedRange)));
    }

    private void RefreshPivotTableBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableContainingSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_PivotTableSelectExistingForRefresh"),
                UiText.Get("MainWindowMessage_RefreshPivotTableTitle"));
            return;
        }

        if (!TryExecuteCommand(new RefreshPivotTableCommand(_currentSheetId, pivotTable.Name), "Refresh PivotTable"))
            return;

        UpdateViewport();
    }

    private void PivotTableNameBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPivotTable(
                UiText.Get("MainWindowMessage_PivotTableRenameTitle"),
                out var sheet,
                out var pivotTable))
            return;

        var dialog = new PivotTableNameDialog(pivotTable.Name) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!PivotUiPlanner.IsPivotTableNameAvailable(_workbook, pivotTable, dialog.Result.Name))
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_PivotTableNameAlreadyExists"),
                UiText.Get("MainWindowMessage_PivotTableRenameTitle"));
            return;
        }

        if (!TryExecuteCommand(
                new RenamePivotTableCommand(sheet.Id, pivotTable.Name, dialog.Result.Name),
                "Rename PivotTable"))
            return;

        RefreshPivotFieldListPane();
        RefreshSlicerTimelinePane();
    }

    private void PivotTableOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotTableClearBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPivotTable(
                UiText.Get("MainWindowMessage_PivotTableClearTitle"),
                out var sheet,
                out var pivotTable))
            return;

        if (!TryExecuteCommand(
                new ClearPivotTableViewCommand(sheet.Id, pivotTable.Name),
                "Clear PivotTable"))
            return;

        UpdateViewport();
        RefreshPivotFieldListPane();
    }

    private void PivotTableSelectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPivotTable(
                UiText.Get("MainWindowMessage_PivotTableSelectCommandTitle"),
                out _,
                out var pivotTable))
            return;

        var range = PivotUiPlanner.ResolvePivotTableSelectionRange(pivotTable);
        SetSelectionRange(range, range.Start);
        EnsureCellVisible(range.Start);
        RefreshPivotFieldListPane();
    }

    private void PivotTableMoveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPivotTable(
                UiText.Get("MainWindowMessage_MovePivotTableTitle"),
                out var sheet,
                out var pivotTable))
            return;

        var destination = new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start);
        MovePivotTableDialog? dialog = null;
        dialog = new MovePivotTableDialog(
            FormatWorkbookRange(destination),
            request => ApplyMovePivotTableRangeSelection(dialog, request),
            sheetId: sheet.Id,
            resolveSheetId: ResolveSheetIdByName)
        { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.DestinationRangeText) ||
            !TryParseWorkbookRange(sheet.Id, dialog.Result.DestinationRangeText, out var targetRange))
            return;

        if (targetRange.Start.Sheet != _currentSheetId)
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_PivotTableMoveCurrentSheetOnly"),
                UiText.Get("MainWindowMessage_MovePivotTableTitle"));
            return;
        }

        if (!PivotUiPlanner.TryCreateMovedTargetRange(pivotTable, targetRange.Start, out var movedRange))
        {
            _messageService.ShowWarning(
                UiText.Get("MovePivotTable_EnterValidDestination"),
                UiText.Get("MainWindowMessage_MovePivotTableTitle"));
            return;
        }

        if (!TryExecuteCommand(
                new MovePivotTableCommand(sheet.Id, pivotTable.Name, targetRange.Start),
                "Move PivotTable"))
            return;

        SetSelectionRange(movedRange, movedRange.Start);
        EnsureCellVisible(movedRange.Start);
        UpdateViewport();
        RefreshPivotFieldListPane();
    }

    private void ApplyMovePivotTableRangeSelection(
        MovePivotTableDialog? dialog,
        MovePivotTableRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange =>
            {
                var destination = new GridRange(selectedRange.Start, selectedRange.Start);
                dialog.ApplyRangeSelection(FormatWorkbookRange(destination));
            });
    }

    private void PivotTableShowDetailsBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = TryShowPivotTableDetails(showMessage: true);
    }

    private bool TryShowPivotTableDetails(bool showMessage)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var target = PivotUiPlanner.ResolveShowDetailsTarget(sheet, SheetGrid.SelectedRange);
        if (target is null)
        {
            if (showMessage)
            {
                _messageService.ShowInfo(
                    UiText.Get("MainWindowMessage_PivotTableSelectValueForDetails"),
                    UiText.Get("MainWindowMessage_ShowPivotTableDetailsTitle"));
            }

            return false;
        }

        if (!TryExecuteCommand(
                new DrillDownPivotTableCommand(_currentSheetId, target.PivotTableName, target.PivotCell),
                "Show PivotTable Details",
                out var outcome))
            return false;

        if (FindAffectedCellAnchor(outcome) is { } detailAnchor)
            _currentSheetId = detailAnchor.Sheet;
        RefreshSheetTabs();
        UpdateViewport();
        return true;
    }

    private void RefreshPivotFieldListPane()
    {
        if (PivotFieldListPane is null)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        var plan = PivotUiPlanner.CreateFieldListPanePlan(sheet, SheetGrid.SelectedRange);
        var pivotTable = plan.PivotTable;
        if (sheet is null ||
            SheetGrid.SelectedObjectKind != FreeX.App.UI.ObjectKind.None ||
            !plan.ShouldShow ||
            pivotTable is null)
        {
            PivotFieldListPane.Visibility = Visibility.Collapsed;
            SetPivotContextualTabsVisible(false);
            PivotAvailableFieldsList.ItemsSource = null;
            PivotRowsList.ItemsSource = null;
            PivotColumnsList.ItemsSource = null;
            PivotFiltersList.ItemsSource = null;
            PivotValuesList.ItemsSource = null;
            return;
        }

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var displayedLayout = GetDisplayedPivotLayout(pivotTable);
        var rowFields = displayedLayout?.RowFields ?? pivotTable.RowFields;
        var columnFields = displayedLayout?.ColumnFields ?? pivotTable.ColumnFields;
        var pageFields = displayedLayout?.PageFields ?? pivotTable.PageFields;
        var dataFields = displayedLayout?.DataFields ?? pivotTable.DataFields;
        var usedSourceFields = new bool[headers.Count];
        MarkUsedPivotSourceFields(usedSourceFields, rowFields, columnFields, pageFields, dataFields);

        var availableItems = new List<PivotFieldListItem>(headers.Count);
        for (var index = 0; index < headers.Count; index++)
        {
            availableItems.Add(new PivotFieldListItem(headers[index], usedSourceFields[index]));
        }

        _pivotFieldListAvailableItems = availableItems;
        ApplyPivotAvailableFieldFilter();
        PivotRowsList.ItemsSource = rowFields
            .Select(field => PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex))
            .ToList();
        PivotColumnsList.ItemsSource = columnFields
            .Select(field => PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex))
            .ToList();
        PivotFiltersList.ItemsSource = pageFields
            .Select(field => PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex))
            .ToList();
        PivotValuesList.ItemsSource = dataFields
            .Select(field => field.Name)
            .ToList();
        PivotFieldListUpdateBtn.IsEnabled = _pendingPivotLayout is not null;
        PivotFieldListPane.Visibility = Visibility.Visible;
        SetPivotContextualTabsVisible(true);
    }

    private void RefreshPivotFieldListPaneAfterSelectionChange()
    {
        if (PivotFieldListPane is null)
            return;

        RefreshViewportPivotFieldListPane(_workbook.GetSheet(_currentSheetId));
    }

    private void SetPivotContextualTabsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (PivotTableAnalyzeTab is not null)
            PivotTableAnalyzeTab.Visibility = visibility;
        if (PivotTableDesignTab is not null)
            PivotTableDesignTab.Visibility = visibility;

        if (!visible &&
            RibbonTabs is not null &&
            (ReferenceEquals(RibbonTabs.SelectedItem, PivotTableAnalyzeTab) ||
             ReferenceEquals(RibbonTabs.SelectedItem, PivotTableDesignTab)))
        {
            RibbonTabs.SelectedIndex = 1;
        }
    }

    private void PivotFieldListBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableContainingSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_PivotTableSelectExistingForFieldList"),
                UiText.Get("MainWindowMessage_PivotTableFieldsTitle"));
            return;
        }

        PivotFieldListPane.Visibility = PivotFieldListPane.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (PivotFieldListPane.Visibility == Visibility.Visible)
            RefreshPivotFieldListPane();
    }

    private void PivotChangeDataSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableContainingSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        PivotTableDataSourceDialog? dialog = null;
        dialog = new PivotTableDataSourceDialog(
            FormatWorkbookRange(pivotTable.SourceRange),
            request => ApplyPivotTableDataSourceRangeSelection(dialog, request),
            sheetId: sheet.Id,
            resolveSheetId: ResolveSheetIdByName,
            resolveReference: (string reference, out GridRange range) => TryParseWorkbookRange(sheet.Id, reference, out range))
        { Owner = this };
        if (dialog.ShowDialog() != true ||
            dialog.Result.SourceRange is not { } sourceRange)
            return;

        if (!TryExecuteCommand(
                new ChangePivotTableSourceCommand(_currentSheetId, pivotTable.Name, sourceRange),
                "Change PivotTable Data Source"))
            return;

        UpdateViewport();
    }

    private void ApplyPivotTableDataSourceRangeSelection(
        PivotTableDataSourceDialog? dialog,
        PivotTableDataSourceRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(FormatWorkbookRange(selectedRange)));
    }

    private void PivotInsertSlicerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var fieldName = GetSelectedOrFirstPivotHeader(headers);

        if (string.IsNullOrWhiteSpace(fieldName))
            return;

        var dialog = new InsertSlicerDialog(headers, fieldName) { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.FieldName) ||
            string.IsNullOrWhiteSpace(dialog.Result.SlicerName))
            return;

        if (!TryExecuteCommand(new AddSlicerCommand(dialog.Result.SlicerName, pivotTable.Name, dialog.Result.FieldName), "Insert Slicer"))
            return;

        _slicerTimelinePaneDismissed = false;
        RefreshSlicerTimelinePane();
        UpdateViewport();
    }

    private void PivotInsertTimelineBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var fieldName = GetSelectedOrFirstPivotHeader(headers);

        if (string.IsNullOrWhiteSpace(fieldName))
            return;

        var dialog = new InsertTimelineDialog(headers, fieldName) { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.DateFieldName) ||
            string.IsNullOrWhiteSpace(dialog.Result.TimelineName))
            return;

        if (!TryExecuteCommand(new AddTimelineCommand(dialog.Result.TimelineName, pivotTable.Name, dialog.Result.DateFieldName), "Insert Timeline"))
            return;

        _slicerTimelinePaneDismissed = false;
        RefreshSlicerTimelinePane();
        UpdateViewport();
    }

    private bool TryGetActivePivotTable(out Sheet sheet, out PivotTableModel pivotTable)
    {
        sheet = _workbook.GetSheet(_currentSheetId)!;
        pivotTable = sheet is null ? null! : PivotUiPlanner.FindPivotTableContainingSelection(sheet, SheetGrid.SelectedRange)!;
        return sheet is not null && pivotTable is not null;
    }

    private bool TryGetSelectedPivotTable(string title, out Sheet sheet, out PivotTableModel pivotTable)
    {
        sheet = _workbook.GetSheet(_currentSheetId)!;
        pivotTable = sheet is null ? null! : PivotUiPlanner.FindPivotTableContainingSelection(sheet, SheetGrid.SelectedRange)!;
        if (sheet is not null && pivotTable is not null)
            return true;

        _messageService.ShowInfo(
            UiText.Get("MainWindowMessage_PivotTableSelectExistingForAnalyzeAction"),
            title);
        return false;
    }

    private void PivotFieldListCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        PivotFieldListPane.Visibility = Visibility.Collapsed;
    }

    private void PivotFieldToRowsBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Rows);

    private void PivotFieldToColumnsBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Columns);

    private void PivotFieldToValuesBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Values);

    private void PivotFieldToFiltersBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Filters);

    private void PivotFieldList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not ListBox list ||
            GetPivotFieldDragCaption(list, e.OriginalSource) is not { } caption ||
            GetPivotFieldDropZone(list) is not { } sourceZone)
        {
            return;
        }

        var sourceIndex = GetPivotFieldDragSourceIndex(list, e.OriginalSource, caption);
        var data = new DataObject();
        data.SetData(PivotFieldDragFormat, new PivotFieldDragPayload(caption, sourceZone, sourceIndex));
        data.SetData(DataFormats.StringFormat, caption);
        _pivotFieldDragSourceZone = sourceZone;
        _pivotFieldDragRemoveCueActive = false;
        try
        {
            DragDrop.DoDragDrop(list, data, DragDropEffects.Move);
        }
        finally
        {
            _pivotFieldDragSourceZone = null;
            _pivotFieldDragRemoveCueActive = false;
        }
    }

    private void PivotFieldList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list &&
            e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(list, source) is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void PivotFieldList_DragOver(object sender, DragEventArgs e)
    {
        var payload = GetPivotFieldDragPayload(e);
        _pivotFieldDragRemoveCueActive =
            sender is ListBox targetList &&
            GetPivotFieldDropZone(targetList) == PivotFieldDropZone.Available &&
            payload?.SourceZone is not null and not PivotFieldDropZone.Available;
        e.Effects = HasPivotFieldDragData(e)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void PivotFieldList_Drop(object sender, DragEventArgs e)
    {
        var payload = GetPivotFieldDragPayload(e);
        if (sender is not ListBox targetList ||
            GetPivotFieldDragCaption(e, payload) is not { } caption ||
            GetPivotFieldDropZone(targetList) is not { } targetZone)
        {
            return;
        }

        MovePivotFieldToZone(caption, targetZone, GetPivotFieldDropInsertIndex(targetList, e.GetPosition(targetList)), payload);
        e.Handled = true;
    }

    private void PivotFieldRemoveDropZone_DragOver(object sender, DragEventArgs e)
    {
        _pivotFieldDragRemoveCueActive = IsBucketFieldDrag(e);
        e.Effects = _pivotFieldDragRemoveCueActive ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void PivotFieldRemoveDropZone_Drop(object sender, DragEventArgs e)
    {
        DropPivotFieldToRemoveZone(e);
    }

    private void PivotFieldListRemoveZone_DragOver(object sender, DragEventArgs e)
    {
        _pivotFieldDragRemoveCueActive = IsBucketFieldDrag(e);
        e.Effects = _pivotFieldDragRemoveCueActive ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void PivotFieldListRemoveZone_Drop(object sender, DragEventArgs e)
    {
        DropPivotFieldToRemoveZone(e);
    }

    private void PivotFieldList_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (_pivotFieldDragSourceZone is not null and not PivotFieldDropZone.Available &&
            (_pivotFieldDragRemoveCueActive || e.Effects == DragDropEffects.None))
        {
            Mouse.SetCursor(Cursors.No);
            e.UseDefaultCursors = false;
            e.Handled = true;
            return;
        }

        e.UseDefaultCursors = true;
    }

    private static bool HasPivotFieldDragData(DragEventArgs e) =>
        e.Data.GetDataPresent(PivotFieldDragFormat) ||
        e.Data.GetDataPresent(DataFormats.StringFormat);

    private static PivotFieldDragPayload? GetPivotFieldDragPayload(DragEventArgs e) =>
        e.Data.GetDataPresent(PivotFieldDragFormat) &&
        e.Data.GetData(PivotFieldDragFormat) is PivotFieldDragPayload payload
            ? payload
            : null;

    private static string? GetPivotFieldDragCaption(DragEventArgs e, PivotFieldDragPayload? payload) =>
        !string.IsNullOrWhiteSpace(payload?.Caption)
            ? payload.Caption
            : e.Data.GetData(DataFormats.StringFormat) as string;

    private static string? GetPivotFieldDragCaption(ListBox list, object originalSource) =>
        FindPivotFieldDragCaption(originalSource) ??
        PivotUiHostHelpers.GetFieldListCaption(list.SelectedItem);

    private static int GetPivotFieldDragSourceIndex(ListBox list, object originalSource, string caption)
    {
        if (originalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(list, source) is ListBoxItem item)
        {
            return list.ItemContainerGenerator.IndexFromContainer(item);
        }

        for (var index = 0; index < list.Items.Count; index++)
        {
            if (string.Equals(PivotUiHostHelpers.GetFieldListCaption(list.Items[index]), caption, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return list.SelectedIndex;
    }

    private static string? FindPivotFieldDragCaption(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: { } dataContext } &&
                PivotUiHostHelpers.GetFieldListCaption(dataContext) is { } caption)
            {
                return caption;
            }

            current = GetPivotFieldDragParent(current);
        }

        return null;
    }

    private static DependencyObject? GetPivotFieldDragParent(DependencyObject element)
    {
        if (element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
            return System.Windows.Media.VisualTreeHelper.GetParent(element) ??
                   LogicalTreeHelper.GetParent(element);

        return LogicalTreeHelper.GetParent(element);
    }

    private static bool IsBucketFieldDrag(DragEventArgs e) =>
        GetPivotFieldDragPayload(e)?.SourceZone is PivotFieldDropZone.Rows
            or PivotFieldDropZone.Columns
            or PivotFieldDropZone.Values
            or PivotFieldDropZone.Filters;

    private void DropPivotFieldToRemoveZone(DragEventArgs e)
    {
        var payload = GetPivotFieldDragPayload(e);
        if (!IsBucketFieldDrag(e) ||
            GetPivotFieldDragCaption(e, payload) is not { } caption)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        MovePivotFieldToZone(caption, PivotFieldDropZone.Available, -1, payload);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static int GetPivotFieldDropInsertIndex(ListBox targetList, Point position)
    {
        for (var index = 0; index < targetList.Items.Count; index++)
        {
            if (targetList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem item)
                continue;

            var itemPosition = item.TranslatePoint(new Point(0, 0), targetList);
            if (position.Y < itemPosition.Y + item.ActualHeight / 2)
                return index;
        }

        return -1;
    }

    private void PivotAvailableFieldCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: PivotFieldListItem item } checkBox)
            return;

        TogglePivotAvailableField(item.Caption, checkBox.IsChecked == true);
    }

    private void TogglePivotAvailableField(string caption, bool isChecked)
    {
        if (isChecked)
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
            if (sheet is null || pivotTable is null)
                return;

            var headers = ReadPivotSourceHeaders(sheet, pivotTable);
            var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, caption);
            if (sourceIndex is null)
                return;

            var zone = PivotUiPlanner.IsNumericSourceField(sheet, pivotTable, sourceIndex.Value)
                ? PivotFieldDropZone.Values
                : PivotFieldDropZone.Rows;
            MovePivotFieldToZone(caption, zone, -1);
            return;
        }

        MovePivotFieldToZone(caption, PivotFieldDropZone.Available, -1);
    }

    private void PivotFieldRemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        if (string.IsNullOrWhiteSpace(selected))
            return;

        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        var displayedLayout = GetDisplayedOrCurrentPivotLayout(pivotTable);
        var rowFields = sourceIndex is null
            ? displayedLayout.RowFields.ToList()
            : displayedLayout.RowFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var columnFields = sourceIndex is null
            ? displayedLayout.ColumnFields.ToList()
            : displayedLayout.ColumnFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var pageFields = sourceIndex is null
            ? displayedLayout.PageFields.ToList()
            : displayedLayout.PageFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var dataFields = ExcludeDataFieldsByCaptionOrSourceIndex(displayedLayout.DataFields, selected, sourceIndex);

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
    }

    private void PivotFieldSortAscendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyPivotFieldSort(PivotSortDirection.Ascending);

    private void PivotFieldSortDescendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyPivotFieldSort(PivotSortDirection.Descending);

    private void PivotFieldClearFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryResolvePivotFieldMenuContext() is not { SourceFieldIndex: { } sourceIndex } context)
            return;

        ClearPivotFieldFilters(context.PivotTable, sourceIndex);
    }

    private void PivotFieldSelectItemsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotFieldFilterDialog(PivotFieldFilterDialogTab.SelectItems);
    }

    private void PivotFieldLabelFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotFieldFilterDialog(PivotFieldFilterDialogTab.LabelFilters);
    }

    private void PivotFieldValueFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotFieldFilterDialog(PivotFieldFilterDialogTab.ValueFilters);
    }

    private void PivotFieldValueSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryResolvePivotFieldMenuContext() is not { } context)
            return;

        var pivotTable = context.PivotTable;
        var dataFieldIndex = ResolveValueFieldSettingsIndex(pivotTable, context.Caption, context.Zone);
        if (dataFieldIndex is null)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_PivotValueFieldSettingsSelectField"),
                UiText.Get("MainWindowMessage_PivotTableFieldsTitle"));
            return;
        }

        var current = pivotTable.DataFields[dataFieldIndex.Value];
        var dialog = new PivotValueFieldSettingsDialog(current, context.Headers) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var dataFields = pivotTable.DataFields.ToList();
        dataFields[dataFieldIndex.Value] = dialog.ResultDataField;

        ApplyPivotFieldListLayout(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            dataFields);
    }

    private void MoveSelectedPivotField(PivotFieldDropZone zone)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        if (sourceIndex is null)
            return;

        var displayedLayout = GetDisplayedOrCurrentPivotLayout(pivotTable);
        var rowFields = displayedLayout.RowFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var columnFields = displayedLayout.ColumnFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var pageFields = displayedLayout.PageFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var dataFields = displayedLayout.DataFields.ToList();
        var field = new PivotFieldModel(sourceIndex.Value);

        switch (zone)
        {
            case PivotFieldDropZone.Rows:
                rowFields.Add(field);
                break;
            case PivotFieldDropZone.Columns:
                columnFields.Add(field);
                break;
            case PivotFieldDropZone.Filters:
                pageFields.Add(field);
                break;
            case PivotFieldDropZone.Values:
                if (FindDataFieldIndexBySourceIndex(dataFields, sourceIndex.Value) is null)
                {
                    dataFields.Add(PivotUiPlanner.CreateDefaultDataField(
                        GetPivotSourceSheet(sheet, pivotTable),
                        pivotTable,
                        headers,
                        sourceIndex.Value));
                }
                break;
        }

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
    }

    private void MovePivotFieldToZone(
        string caption,
        PivotFieldDropZone targetZone,
        int insertIndex,
        PivotFieldDragPayload? payload = null)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var displayedLayout = GetDisplayedOrCurrentPivotLayout(pivotTable);
        var sourceIndex = ResolveDraggedSourceFieldIndex(displayedLayout, headers, caption, payload);
        var draggedDataField = ResolveDraggedDataField(displayedLayout, caption, payload);
        if (draggedDataField is null && sourceIndex is { } valueSourceIndex)
            draggedDataField = FindDataFieldBySourceIndex(displayedLayout.DataFields, valueSourceIndex);
        if (sourceIndex is null && draggedDataField is null)
            return;

        var rowFields = displayedLayout.RowFields.ToList();
        var columnFields = displayedLayout.ColumnFields.ToList();
        var pageFields = displayedLayout.PageFields.ToList();
        var dataFields = displayedLayout.DataFields.ToList();
        var removedSourceIndex = RemovePivotFieldFromLayout(
            rowFields,
            columnFields,
            pageFields,
            dataFields,
            caption,
            sourceIndex,
            payload);
        var adjustedInsertIndex = AdjustPivotFieldInsertIndex(insertIndex, targetZone, payload, removedSourceIndex);

        if (targetZone == PivotFieldDropZone.Available)
        {
            ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
            return;
        }

        if (sourceIndex is null)
            return;

        RemoveExistingPivotFieldFromTarget(rowFields, columnFields, pageFields, dataFields, targetZone, sourceIndex.Value, payload);
        switch (targetZone)
        {
            case PivotFieldDropZone.Rows:
                PivotUiHostHelpers.InsertOrAppend(rowFields, FindExistingPivotField(displayedLayout, sourceIndex.Value), adjustedInsertIndex);
                break;
            case PivotFieldDropZone.Columns:
                PivotUiHostHelpers.InsertOrAppend(columnFields, FindExistingPivotField(displayedLayout, sourceIndex.Value), adjustedInsertIndex);
                break;
            case PivotFieldDropZone.Filters:
                PivotUiHostHelpers.InsertOrAppend(pageFields, FindExistingPivotField(displayedLayout, sourceIndex.Value), adjustedInsertIndex);
                break;
            case PivotFieldDropZone.Values:
                if (payload?.SourceZone != PivotFieldDropZone.Values &&
                    draggedDataField is null &&
                    FindDataFieldIndexBySourceIndex(dataFields, sourceIndex.Value) is not null)
                {
                    break;
                }

                var valueField = draggedDataField ?? PivotUiPlanner.CreateDefaultDataField(
                    GetPivotSourceSheet(sheet, pivotTable),
                    pivotTable,
                    headers,
                    sourceIndex.Value);
                PivotUiHostHelpers.InsertOrAppend(dataFields, valueField, adjustedInsertIndex);
                break;
        }

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
    }

    private static void RemoveExistingPivotFieldFromTarget(
        List<PivotFieldModel> rowFields,
        List<PivotFieldModel> columnFields,
        List<PivotFieldModel> pageFields,
        List<PivotDataFieldModel> dataFields,
        PivotFieldDropZone targetZone,
        int sourceIndex,
        PivotFieldDragPayload? payload)
    {
        switch (targetZone)
        {
            case PivotFieldDropZone.Rows:
                rowFields.RemoveAll(field => field.SourceFieldIndex == sourceIndex);
                break;
            case PivotFieldDropZone.Columns:
                columnFields.RemoveAll(field => field.SourceFieldIndex == sourceIndex);
                break;
            case PivotFieldDropZone.Filters:
                pageFields.RemoveAll(field => field.SourceFieldIndex == sourceIndex);
                break;
            case PivotFieldDropZone.Values when payload?.SourceZone != PivotFieldDropZone.Values:
                dataFields.RemoveAll(field => field.SourceFieldIndex == sourceIndex);
                break;
        }
    }

    private static int? ResolveDraggedSourceFieldIndex(
        PendingPivotLayout layout,
        IReadOnlyList<string> headers,
        string caption,
        PivotFieldDragPayload? payload)
    {
        if (payload is not null)
        {
            var sourceIndex = payload.SourceZone switch
            {
                PivotFieldDropZone.Rows => GetPivotFieldSourceIndex(layout.RowFields, payload.SourceIndex),
                PivotFieldDropZone.Columns => GetPivotFieldSourceIndex(layout.ColumnFields, payload.SourceIndex),
                PivotFieldDropZone.Filters => GetPivotFieldSourceIndex(layout.PageFields, payload.SourceIndex),
                PivotFieldDropZone.Values => GetPivotDataFieldSourceIndex(layout.DataFields, payload.SourceIndex),
                PivotFieldDropZone.Available => PivotUiPlanner.FindSourceFieldIndex(headers, caption),
                _ => null
            };
            if (sourceIndex is not null)
                return sourceIndex;
        }

        return PivotUiPlanner.FindSourceFieldIndex(headers, caption) ??
               FindDataFieldByCaption(layout.DataFields, caption)?.SourceFieldIndex;
    }

    private static PivotDataFieldModel? ResolveDraggedDataField(
        PendingPivotLayout layout,
        string caption,
        PivotFieldDragPayload? payload)
    {
        if (payload is { SourceZone: PivotFieldDropZone.Values } &&
            (uint)payload.SourceIndex < (uint)layout.DataFields.Count)
        {
            return layout.DataFields[payload.SourceIndex];
        }

        return FindDataFieldByCaption(layout.DataFields, caption);
    }

    private static int? RemovePivotFieldFromLayout(
        List<PivotFieldModel> rowFields,
        List<PivotFieldModel> columnFields,
        List<PivotFieldModel> pageFields,
        List<PivotDataFieldModel> dataFields,
        string caption,
        int? sourceIndex,
        PivotFieldDragPayload? payload)
    {
        if (payload is not null)
        {
            var removed = payload.SourceZone switch
            {
                PivotFieldDropZone.Rows => RemovePivotFieldAt(rowFields, payload.SourceIndex, sourceIndex),
                PivotFieldDropZone.Columns => RemovePivotFieldAt(columnFields, payload.SourceIndex, sourceIndex),
                PivotFieldDropZone.Filters => RemovePivotFieldAt(pageFields, payload.SourceIndex, sourceIndex),
                PivotFieldDropZone.Values => RemovePivotDataFieldAt(dataFields, payload.SourceIndex, caption, sourceIndex),
                _ => false
            };
            if (removed)
                return payload.SourceIndex;
        }

        if (sourceIndex is not null)
        {
            rowFields.RemoveAll(field => field.SourceFieldIndex == sourceIndex.Value);
            columnFields.RemoveAll(field => field.SourceFieldIndex == sourceIndex.Value);
            pageFields.RemoveAll(field => field.SourceFieldIndex == sourceIndex.Value);
        }

        dataFields.RemoveAll(field => DataFieldMatchesCaptionOrSourceIndex(field, caption, sourceIndex));
        return null;
    }

    private static int AdjustPivotFieldInsertIndex(
        int insertIndex,
        PivotFieldDropZone targetZone,
        PivotFieldDragPayload? payload,
        int? removedSourceIndex) =>
        payload is not null &&
        removedSourceIndex is { } removedIndex &&
        payload.SourceZone == targetZone &&
        insertIndex > removedIndex
            ? insertIndex - 1
            : insertIndex;

    private static int? GetPivotFieldSourceIndex(IReadOnlyList<PivotFieldModel> fields, int index) =>
        (uint)index < (uint)fields.Count ? fields[index].SourceFieldIndex : null;

    private static int? GetPivotDataFieldSourceIndex(IReadOnlyList<PivotDataFieldModel> fields, int index) =>
        (uint)index < (uint)fields.Count ? fields[index].SourceFieldIndex : null;

    private static bool RemovePivotFieldAt(List<PivotFieldModel> fields, int index, int? sourceIndex)
    {
        if ((uint)index >= (uint)fields.Count ||
            (sourceIndex is not null && fields[index].SourceFieldIndex != sourceIndex.Value))
        {
            return false;
        }

        fields.RemoveAt(index);
        return true;
    }

    private static bool RemovePivotDataFieldAt(
        List<PivotDataFieldModel> fields,
        int index,
        string caption,
        int? sourceIndex)
    {
        if ((uint)index >= (uint)fields.Count ||
            !DataFieldMatchesCaptionOrSourceIndex(fields[index], caption, sourceIndex))
        {
            return false;
        }

        fields.RemoveAt(index);
        return true;
    }

    private void ApplyPivotFieldSort(PivotSortDirection direction)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivotTable, selected);
        if (sourceIndex is null && dataFieldIndex is null)
            return;

        var sorts = pivotTable.Sorts
            .Where(sort =>
                (sourceIndex is null || sort.FieldIndex != sourceIndex.Value) &&
                (dataFieldIndex is null || sort.DataFieldIndex != dataFieldIndex.Value))
            .ToList();

        if (dataFieldIndex is not null)
        {
            sorts.Add(new PivotSortModel(
                PivotSortTarget.Value,
                direction,
                DataFieldIndex: dataFieldIndex.Value,
                FieldIndex: LastAxisFieldSourceIndexOrDefault(pivotTable)));
        }
        else
        {
            sorts.Add(new PivotSortModel(PivotSortTarget.Label, direction, FieldIndex: sourceIndex.GetValueOrDefault()));
        }

        ApplyPivotFieldView(pivotTable, pivotTable.LabelFilters.ToList(), pivotTable.ValueFilters.ToList(), sorts);
    }

    private void PivotFieldMoreSortOptionsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryResolvePivotFieldMenuContext() is not { SourceFieldIndex: { } sourceIndex } context)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_PivotMoreSortOptionsSelectField"),
                UiText.Get("MainWindowMessage_PivotTableFieldsTitle"));
            return;
        }

        var pivotTable = context.PivotTable;
        var currentSort = pivotTable.Sorts.LastOrDefault(sort => sort.FieldIndex == sourceIndex);
        var dialog = new PivotSortOptionsDialog(
            PivotUiPlanner.FieldCaption(context.Headers, sourceIndex),
            sourceIndex,
            pivotTable.DataFields,
            currentSort)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.ResultSort is not { } sort)
            return;

        var sorts = pivotTable.Sorts
            .Where(item => item.FieldIndex != sourceIndex)
            .Append(sort)
            .ToList();
        ApplyPivotFieldView(pivotTable, pivotTable.LabelFilters.ToList(), pivotTable.ValueFilters.ToList(), sorts);
    }

    private void ShowPivotFieldFilterDialog(PivotFieldFilterDialogTab initialTab)
    {
        if (TryResolvePivotFieldMenuContext() is not { SourceFieldIndex: { } sourceIndex } context)
            return;

        var pivotTable = context.PivotTable;
        var allItems = ReadPivotFieldItems(context.Sheet, pivotTable, sourceIndex).ToList();
        var state = PivotFieldFilterSummary.CreateState(
            pivotTable,
            sourceIndex,
            PivotUiPlanner.FieldCaption(context.Headers, sourceIndex),
            allItems);
        var dialog = new PivotFieldFilterDialog(
            allItems,
            state.SelectedItems,
            pivotTable.DataFields.Count > 0,
            state,
            initialTab)
        {
            Owner = this,
            Title = UiText.Format("MainWindowMessage_PivotFieldFilterTitle", state.FieldCaption)
        };
        if (dialog.ShowDialog() != true)
            return;

        switch (dialog.RequestedAction)
        {
            case PivotFieldFilterDialogAction.SelectItems:
                ApplyPivotFieldItemFilter(pivotTable, sourceIndex, dialog.SelectedItems, allItems.Count);
                break;
            case PivotFieldFilterDialogAction.ClearItemFilter:
                ApplyPivotFieldItemFilter(pivotTable, sourceIndex, null, allItems.Count);
                break;
            case PivotFieldFilterDialogAction.ClearFieldFilters:
                ClearPivotFieldFilters(pivotTable, sourceIndex);
                break;
            case PivotFieldFilterDialogAction.LabelFilter:
                ShowPivotLabelFilterDialog(pivotTable, sourceIndex, state.LabelFilter);
                break;
            case PivotFieldFilterDialogAction.ValueFilter:
                ShowPivotValueFilterDialog(pivotTable, sourceIndex, state.ValueFilter);
                break;
            case PivotFieldFilterDialogAction.RemoveLabelFilter:
                RemovePivotLabelFilter(pivotTable, sourceIndex);
                break;
            case PivotFieldFilterDialogAction.RemoveValueFilter:
                RemovePivotValueFilter(pivotTable, sourceIndex);
                break;
        }
    }

    private void ShowPivotLabelFilterDialog(
        PivotTableModel pivotTable,
        int sourceIndex,
        PivotLabelFilterModel? existingFilter)
    {
        var dialog = new PivotLabelFilterDialog(sourceIndex, existingFilter) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultFilter is not { } filter)
            return;

        var labelFilters = pivotTable.LabelFilters
            .Where(item => item.SourceFieldIndex != sourceIndex)
            .Append(filter)
            .ToList();
        ApplyPivotFieldFilters(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            labelFilters,
            pivotTable.ValueFilters.ToList(),
            pivotTable.Sorts.ToList());
    }

    private void ShowPivotValueFilterDialog(
        PivotTableModel pivotTable,
        int sourceIndex,
        PivotValueFilterModel? existingFilter)
    {
        if (pivotTable.DataFields.Count == 0)
            return;

        var dialog = new PivotValueFilterDialog(sourceIndex, existingFilter) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultFilter is not { } filter)
            return;

        var valueFilters = pivotTable.ValueFilters
            .Where(item => !PivotFieldFilterSummary.BelongsToSourceField(item, sourceIndex))
            .Append(filter)
            .ToList();
        ApplyPivotFieldFilters(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            pivotTable.LabelFilters.ToList(),
            valueFilters,
            pivotTable.Sorts.ToList());
    }

    private void ApplyPivotFieldItemFilter(
        PivotTableModel pivotTable,
        int sourceIndex,
        IReadOnlyList<string>? selectedItems,
        int allItemCount)
    {
        var items = selectedItems is null ||
                    selectedItems.Count == 0 ||
                    selectedItems.Count == allItemCount
            ? null
            : selectedItems;
        ApplyPivotFieldFilters(
            pivotTable,
            PivotUiPlanner.SetFieldSelectedItems(pivotTable.RowFields, sourceIndex, items),
            PivotUiPlanner.SetFieldSelectedItems(pivotTable.ColumnFields, sourceIndex, items),
            PivotUiPlanner.SetFieldSelectedItems(pivotTable.PageFields, sourceIndex, items),
            pivotTable.LabelFilters.ToList(),
            pivotTable.ValueFilters.ToList(),
            pivotTable.Sorts.ToList());
    }

    private void ClearPivotFieldFilters(PivotTableModel pivotTable, int sourceIndex)
    {
        ApplyPivotFieldFilters(
            pivotTable,
            PivotUiPlanner.SetFieldSelectedItems(pivotTable.RowFields, sourceIndex, null),
            PivotUiPlanner.SetFieldSelectedItems(pivotTable.ColumnFields, sourceIndex, null),
            PivotUiPlanner.SetFieldSelectedItems(pivotTable.PageFields, sourceIndex, null),
            pivotTable.LabelFilters.Where(filter => filter.SourceFieldIndex != sourceIndex).ToList(),
            pivotTable.ValueFilters.Where(filter => !PivotFilterOwnership.BelongsToSourceField(filter, sourceIndex)).ToList(),
            pivotTable.Sorts.ToList());
    }

    private void RemovePivotLabelFilter(PivotTableModel pivotTable, int sourceIndex)
    {
        ApplyPivotFieldFilters(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            pivotTable.LabelFilters.Where(filter => filter.SourceFieldIndex != sourceIndex).ToList(),
            pivotTable.ValueFilters.ToList(),
            pivotTable.Sorts.ToList());
    }

    private void RemovePivotValueFilter(PivotTableModel pivotTable, int sourceIndex)
    {
        ApplyPivotFieldFilters(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            pivotTable.LabelFilters.ToList(),
            pivotTable.ValueFilters.Where(filter => !PivotFilterOwnership.BelongsToSourceField(filter, sourceIndex)).ToList(),
            pivotTable.Sorts.ToList());
    }

    private void ApplyPivotFieldFilters(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        IReadOnlyList<PivotValueFilterModel> valueFilters,
        IReadOnlyList<PivotSortModel> sorts)
    {
        if (!TryExecuteCommand(
                new ConfigurePivotTableFieldFiltersCommand(
                    _currentSheetId,
                    pivotTable.Name,
                    rowFields,
                    columnFields,
                    pageFields,
                    labelFilters,
                    valueFilters,
                    sorts),
                "PivotTable Field Filters"))
            return;

        UpdateViewport();
        RefreshPivotFieldListPane();
    }

    private void ApplyPivotFieldListLayout(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotDataFieldModel> dataFields,
        bool forceApply = false)
    {
        if (dataFields.Count == 0)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_PivotTableRequiresValueField"),
                UiText.Get("MainWindowMessage_PivotTableFieldsTitle"));
            return;
        }

        if (!forceApply && PivotFieldListDeferLayoutCheckBox.IsChecked == true)
        {
            _pendingPivotLayout = new PendingPivotLayout(
                pivotTable.Name,
                rowFields.ToList(),
                columnFields.ToList(),
                pageFields.ToList(),
                dataFields.ToList());
            RefreshPivotFieldListPane();
            return;
        }

        var previousVisibleRange = PivotUiPlanner.VisiblePivotRange(pivotTable);
        if (!TryExecuteCommand(
                new ConfigurePivotTableLayoutCommand(_currentSheetId, pivotTable.Name, rowFields, columnFields, pageFields, dataFields),
                "PivotTable Fields"))
            return;

        _pendingPivotLayout = null;
        ReconcilePivotFieldListSelectionAfterPaneMutation(previousVisibleRange, pivotTable);
        UpdateViewport();
        RefreshPivotFieldListPane();
    }

    private void ApplyPivotFieldView(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        IReadOnlyList<PivotValueFilterModel> valueFilters,
        IReadOnlyList<PivotSortModel> sorts)
    {
        var previousVisibleRange = PivotUiPlanner.VisiblePivotRange(pivotTable);
        if (!TryExecuteCommand(
                new ConfigurePivotTableViewCommand(_currentSheetId, pivotTable.Name, labelFilters, valueFilters, sorts),
                "PivotTable Field"))
            return;

        ReconcilePivotFieldListSelectionAfterPaneMutation(previousVisibleRange, pivotTable);
        UpdateViewport();
        RefreshPivotFieldListPane();
    }

    private PivotFieldMenuContext? TryResolvePivotFieldMenuContext()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return null;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var caption = GetSelectedPivotFieldListItem();
        return new PivotFieldMenuContext(
            sheet,
            pivotTable,
            headers,
            caption,
            PivotUiPlanner.FindSourceFieldIndex(headers, caption),
            PivotUiPlanner.FindDataFieldIndex(pivotTable, caption),
            _pivotFieldMenuContextZone ?? GetSelectedPivotFieldDropZone());
    }

    private void ClearPivotFieldMenuContext()
    {
        _pivotFieldMenuContextCaption = null;
        _pivotFieldMenuContextZone = null;
    }

    private PivotFieldDropZone? GetSelectedPivotFieldDropZone()
    {
        foreach (var list in PivotFieldLists())
        {
            if (list.SelectedItem is not null &&
                GetPivotFieldDropZone(list) is { } zone)
            {
                return zone;
            }
        }

        return null;
    }

    private static int? ResolveValueFieldSettingsIndex(
        PivotTableModel pivotTable,
        string? caption,
        PivotFieldDropZone? zone)
    {
        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivotTable, caption);
        if (dataFieldIndex is not null)
            return dataFieldIndex;

        if (pivotTable.DataFields.Count == 1)
            return 0;

        return null;
    }

    private void ReconcilePivotFieldListSelectionAfterPaneMutation(
        GridRange previousVisibleRange,
        PivotTableModel pivotTable)
    {
        if (PivotUiPlanner.ReconcileSelectionAfterPivotResize(
                previousVisibleRange,
                PivotUiPlanner.VisiblePivotRange(pivotTable),
                SheetGrid.SelectedRange) is { } target)
        {
            SetActiveCell(target);
        }
    }

    private string? GetSelectedPivotFieldListItem()
    {
        if (!string.IsNullOrWhiteSpace(_pivotFieldMenuContextCaption))
            return _pivotFieldMenuContextCaption;

        foreach (var list in PivotFieldLists())
        {
            if (PivotUiHostHelpers.GetFieldListCaption(list.SelectedItem) is { } value)
                return value;
        }

        return null;
    }

    private IEnumerable<ListBox> PivotFieldLists()
    {
        yield return PivotAvailableFieldsList;
        yield return PivotRowsList;
        yield return PivotColumnsList;
        yield return PivotValuesList;
        yield return PivotFiltersList;
    }

    private string? GetSelectedOrFirstPivotHeader(IReadOnlyList<string> headers)
    {
        var selected = GetSelectedPivotFieldListItem();
        return PivotUiPlanner.FindSourceFieldIndex(headers, selected) is null
            ? headers.Count == 0 ? null : headers[0]
            : selected;
    }

    private static CellAddress? FindAffectedCellAnchor(CommandOutcome outcome) =>
        outcome.AffectedCells is { } affectedCells
            ? affectedCells.Count == 0 ? default : affectedCells[0]
            : null;

    private static int? FindDataFieldIndexByCaptionOrSourceIndex(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        string? caption)
    {
        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivotTable, caption);
        if (dataFieldIndex is not null)
            return dataFieldIndex;

        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, caption);
        return sourceIndex is null
            ? null
            : FindDataFieldIndexBySourceIndex(pivotTable.DataFields, sourceIndex.Value);
    }

    private static int? FindDataFieldIndexBySourceIndex(
        IReadOnlyList<PivotDataFieldModel> dataFields,
        int sourceFieldIndex)
    {
        for (var index = 0; index < dataFields.Count; index++)
        {
            if (dataFields[index].SourceFieldIndex == sourceFieldIndex)
                return index;
        }

        return null;
    }

    private static PivotDataFieldModel? FindDataFieldBySourceIndex(
        IReadOnlyList<PivotDataFieldModel> dataFields,
        int sourceFieldIndex)
    {
        var index = FindDataFieldIndexBySourceIndex(dataFields, sourceFieldIndex);
        return index is null ? null : dataFields[index.Value];
    }

    private static PivotDataFieldModel? FindDataFieldByCaption(
        IEnumerable<PivotDataFieldModel> dataFields,
        string caption)
    {
        foreach (var field in dataFields)
        {
            if (DataFieldCaptionEquals(field, caption))
                return field;
        }

        return null;
    }

    private static List<PivotDataFieldModel> ExcludeDataFieldsByCaptionOrSourceIndex(
        IEnumerable<PivotDataFieldModel> dataFields,
        string caption,
        int? sourceIndex)
    {
        var filtered = new List<PivotDataFieldModel>();
        foreach (var field in dataFields)
        {
            if (!DataFieldMatchesCaptionOrSourceIndex(field, caption, sourceIndex))
                filtered.Add(field);
        }

        return filtered;
    }

    private static bool DataFieldMatchesCaptionOrSourceIndex(
        PivotDataFieldModel field,
        string caption,
        int? sourceIndex) =>
        DataFieldCaptionEquals(field, caption) ||
        (sourceIndex is not null && field.SourceFieldIndex == sourceIndex.Value);

    private static bool DataFieldCaptionEquals(PivotDataFieldModel field, string caption) =>
        string.Equals(field.Name, caption, StringComparison.CurrentCultureIgnoreCase);

    private static PivotFieldModel? FindPivotLayoutFieldBySourceIndex(PivotTableModel pivotTable, int sourceFieldIndex) =>
        FindPivotLayoutFieldBySourceIndex(pivotTable.RowFields, sourceFieldIndex) ??
        FindPivotLayoutFieldBySourceIndex(pivotTable.ColumnFields, sourceFieldIndex) ??
        FindPivotLayoutFieldBySourceIndex(pivotTable.PageFields, sourceFieldIndex);

    private static PivotFieldModel FindExistingPivotField(PendingPivotLayout layout, int sourceFieldIndex) =>
        FindPivotLayoutFieldBySourceIndex(layout.RowFields, sourceFieldIndex) ??
        FindPivotLayoutFieldBySourceIndex(layout.ColumnFields, sourceFieldIndex) ??
        FindPivotLayoutFieldBySourceIndex(layout.PageFields, sourceFieldIndex) ??
        new PivotFieldModel(sourceFieldIndex);

    private static PivotFieldModel? FindPivotLayoutFieldBySourceIndex(
        IReadOnlyList<PivotFieldModel> fields,
        int sourceFieldIndex)
    {
        foreach (var field in fields)
        {
            if (field.SourceFieldIndex == sourceFieldIndex)
                return field;
        }

        return null;
    }

    private static int LastAxisFieldSourceIndexOrDefault(PivotTableModel pivotTable)
    {
        if (pivotTable.RowFields.Count > 0)
            return pivotTable.RowFields[pivotTable.RowFields.Count - 1].SourceFieldIndex;

        return pivotTable.ColumnFields.Count == 0
            ? 0
            : pivotTable.ColumnFields[pivotTable.ColumnFields.Count - 1].SourceFieldIndex;
    }

    private static void MarkUsedPivotSourceFields(
        bool[] used,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotDataFieldModel> dataFields)
    {
        foreach (var field in rowFields)
        {
            MarkUsedSourceField(used, field.SourceFieldIndex);
        }

        foreach (var field in columnFields)
        {
            MarkUsedSourceField(used, field.SourceFieldIndex);
        }

        foreach (var field in pageFields)
        {
            MarkUsedSourceField(used, field.SourceFieldIndex);
        }

        foreach (var field in dataFields)
        {
            MarkUsedSourceField(used, field.SourceFieldIndex);
        }
    }

    private static void MarkUsedSourceField(bool[] used, int sourceFieldIndex)
    {
        if ((uint)sourceFieldIndex < (uint)used.Length)
            used[sourceFieldIndex] = true;
    }

    private Sheet GetPivotSourceSheet(Sheet fallbackSheet, PivotTableModel pivotTable) =>
        PivotUiPlanner.ResolvePivotSourceSheet(_workbook, fallbackSheet, pivotTable);

    private List<string> ReadPivotSourceHeaders(Sheet sheet, PivotTableModel pivotTable)
    {
        var sourceSheet = GetPivotSourceSheet(sheet, pivotTable);
        var headers = new List<string>();
        var start = pivotTable.SourceRange.Start;
        for (var col = start.Col; col <= pivotTable.SourceRange.End.Col; col++)
        {
            var caption = SpreadsheetDisplayFormatter.FormatCellValue(sourceSheet.GetValue(start.Row, col)).Trim();
            headers.Add(string.IsNullOrWhiteSpace(caption) ? $"Column {headers.Count + 1}" : caption);
        }

        // Cache-based pivots loaded from xlsx have no SourceRange; fall back to the cache field names
        // so captions/dropdowns show real names instead of "Column N" (Issue 123).
        return PivotSourceHeaderResolver.Resolve(_workbook, pivotTable, headers);
    }

    private IReadOnlyList<string> ReadPivotFieldItems(Sheet sheet, PivotTableModel pivotTable, int sourceFieldIndex)
    {
        var sourceSheet = GetPivotSourceSheet(sheet, pivotTable);
        return PivotFieldItemsReader.ReadItems(
            sourceSheet,
            pivotTable,
            sourceFieldIndex,
            value => SpreadsheetDisplayFormatter.FormatCellValue(value).Trim());
    }

    private bool TryParseWorkbookRange(SheetId defaultSheetId, string input, out GridRange range)
        => WorkbookRangeTextCodec.TryParse(
            defaultSheetId,
            input,
            ResolveSheetIdByName,
            out range);

    private string FormatWorkbookRange(GridRange range)
        => WorkbookRangeTextCodec.Format(
            range,
            _currentSheetId,
            sheetId => _workbook.GetSheet(sheetId)?.Name);

    private PivotFieldDropZone? GetPivotFieldDropZone(ListBox list)
    {
        if (ReferenceEquals(list, PivotRowsList))
            return PivotFieldDropZone.Rows;
        if (ReferenceEquals(list, PivotColumnsList))
            return PivotFieldDropZone.Columns;
        if (ReferenceEquals(list, PivotFiltersList))
            return PivotFieldDropZone.Filters;
        if (ReferenceEquals(list, PivotValuesList))
            return PivotFieldDropZone.Values;
        if (ReferenceEquals(list, PivotAvailableFieldsList))
            return PivotFieldDropZone.Available;
        return null;
    }

    private enum PivotFieldDropZone
    {
        Available,
        Rows,
        Columns,
        Values,
        Filters
    }

    private sealed record PivotFieldMenuContext(
        Sheet Sheet,
        PivotTableModel PivotTable,
        IReadOnlyList<string> Headers,
        string? Caption,
        int? SourceFieldIndex,
        int? DataFieldIndex,
        PivotFieldDropZone? Zone);

    [Serializable]
    private sealed record PivotFieldDragPayload(
        string Caption,
        PivotFieldDropZone SourceZone,
        int SourceIndex);

}
