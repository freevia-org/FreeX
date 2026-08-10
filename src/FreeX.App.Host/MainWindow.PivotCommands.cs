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
        var createModel = PivotApplication.PrepareCreate(_currentSheetId, SheetGrid.SelectedRange);
        if (!createModel.CanShow || createModel.SourceRange is not { } sourceRange)
        {
            ShowPivotApplicationMessage(
                createModel.Message,
                UiText.Get("MainWindowMessage_InsertPivotTableTitle"));
            return;
        }

        PivotTableDialog? dialog = null;
        dialog = new PivotTableDialog(
            _workbook,
            _currentSheetId,
            sourceRange,
            request => ApplyPivotTableRangeSelection(dialog, request)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPivotApplicationPlan(
            PivotApplication.PlanCreate(
                _currentSheetId,
                new PivotCreateSubmission(
                    dialog.Result.SourceRangeText,
                    dialog.Result.DestinationKind,
                    dialog.Result.DestinationRangeText,
                    dialog.Result.OpenFieldList)),
            UiText.Get("MainWindowMessage_InsertPivotTableTitle"));
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
        var title = UiText.Get("MainWindowMessage_RefreshPivotTableTitle");
        if (!TryResolvePivotTarget(
                title,
                out var target,
                missingMessageResourceKey: "MainWindowMessage_PivotTableSelectExistingForRefresh"))
            return;

        ApplyPivotApplicationPlan(PivotApplication.PlanRefresh(target), title);
    }

    private void PivotTableNameBtn_Click(object sender, RoutedEventArgs e)
    {
        var title = UiText.Get("MainWindowMessage_PivotTableRenameTitle");
        if (!TryResolvePivotTarget(title, out var target))
            return;

        var dialog = new PivotTableNameDialog(target.PivotTable.Name) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPivotApplicationPlan(
            PivotApplication.PlanRename(target, dialog.Result.Name),
            title);
    }

    private void PivotTableOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotTableClearBtn_Click(object sender, RoutedEventArgs e)
    {
        var title = UiText.Get("MainWindowMessage_PivotTableClearTitle");
        if (!TryResolvePivotTarget(title, out var target))
            return;

        ApplyPivotApplicationPlan(PivotApplication.PlanClear(target), title);
    }

    private void PivotTableSelectBtn_Click(object sender, RoutedEventArgs e)
    {
        var title = UiText.Get("MainWindowMessage_PivotTableSelectCommandTitle");
        if (!TryResolvePivotTarget(title, out var target))
            return;

        ApplyPivotApplicationPlan(PivotApplication.PlanSelect(target), title);
    }

    private void PivotTableMoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var title = UiText.Get("MainWindowMessage_MovePivotTableTitle");
        if (!TryResolvePivotTarget(title, out var target))
            return;

        var destination = new GridRange(target.PivotTable.TargetRange.Start, target.PivotTable.TargetRange.Start);
        MovePivotTableDialog? dialog = null;
        dialog = new MovePivotTableDialog(
            FormatWorkbookRange(destination),
            request => ApplyMovePivotTableRangeSelection(dialog, request),
            sheetId: target.Sheet.Id,
            resolveSheetId: ResolveSheetIdByName)
        { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPivotApplicationPlan(
            PivotApplication.PlanMove(target, dialog.Result.DestinationRangeText),
            title);
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
        var title = UiText.Get("MainWindowMessage_ShowPivotTableDetailsTitle");
        var plan = PivotApplication.PlanShowDetails(_currentSheetId, SheetGrid.SelectedRange);
        if (!plan.CanApply)
        {
            if (showMessage)
            {
                _messageService.ShowInfo(
                    UiText.Get("MainWindowMessage_PivotTableSelectValueForDetails"),
                    title);
            }

            return false;
        }

        return ApplyPivotApplicationPlan(plan, title);
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

        var headers = PivotSourceContext.ReadHeaders(_workbook, pivotTable, sheet);
        var displayedLayout = GetDisplayedPivotLayout(pivotTable);
        var areas = displayedLayout?.Areas ?? PivotFieldLayoutPlanner.Capture(pivotTable);
        var rowFields = areas.RowFields;
        var columnFields = areas.ColumnFields;
        var pageFields = areas.PageFields;
        var dataFields = areas.DataFields;

        _pivotFieldListAvailableItems = PivotFieldListPaneBuilder.BuildAvailableFields(headers, areas);
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
        var title = UiText.Get("PivotDataSource_Title");
        if (!TryResolvePivotTarget(title, out var target))
            return;

        PivotTableDataSourceDialog? dialog = null;
        dialog = new PivotTableDataSourceDialog(
            FormatWorkbookRange(target.PivotTable.SourceRange),
            request => ApplyPivotTableDataSourceRangeSelection(dialog, request),
            sheetId: target.Sheet.Id,
            resolveSheetId: ResolveSheetIdByName,
            resolveReference: (string reference, out GridRange range) =>
                TryParseWorkbookRange(target.Sheet.Id, reference, out range))
        { Owner = this };
        if (dialog.ShowDialog() != true ||
            dialog.Result.SourceRange is null)
            return;

        ApplyPivotApplicationPlan(
            PivotApplication.PlanChangeDataSource(target, dialog.Result.SourceRangeText),
            title);
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

        var headers = PivotSourceContext.ReadHeaders(_workbook, pivotTable, sheet);
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

        var headers = PivotSourceContext.ReadHeaders(_workbook, pivotTable, sheet);
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
        var resolution = PivotApplication.ResolveTarget(_currentSheetId, SheetGrid.SelectedRange);
        if (resolution.Target is { } target)
        {
            sheet = target.Sheet;
            pivotTable = target.PivotTable;
            return true;
        }

        sheet = null!;
        pivotTable = null!;
        return false;
    }

    private bool TryGetSelectedPivotTable(string title, out Sheet sheet, out PivotTableModel pivotTable)
    {
        if (TryResolvePivotTarget(title, out var target))
        {
            sheet = target.Sheet;
            pivotTable = target.PivotTable;
            return true;
        }

        sheet = null!;
        pivotTable = null!;
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
        PivotFieldListPaneBuilder.GetItemCaption(list.SelectedItem);

    private static int GetPivotFieldDragSourceIndex(ListBox list, object originalSource, string caption)
    {
        if (originalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(list, source) is ListBoxItem item)
        {
            return list.ItemContainerGenerator.IndexFromContainer(item);
        }

        for (var index = 0; index < list.Items.Count; index++)
        {
            if (string.Equals(PivotFieldListPaneBuilder.GetItemCaption(list.Items[index]), caption, StringComparison.OrdinalIgnoreCase))
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
                PivotFieldListPaneBuilder.GetItemCaption(dataContext) is { } caption)
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
        if (sender is not CheckBox { DataContext: PivotAvailableFieldItemModel item } checkBox)
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

            var headers = PivotSourceContext.ReadHeaders(_workbook, pivotTable, sheet);
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
        var selected = GetSelectedPivotFieldListItem();
        if (string.IsNullOrWhiteSpace(selected))
            return;

        MovePivotFieldToZone(selected, PivotFieldDropZone.Available, -1);
    }

    private void PivotFieldSortAscendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyPivotFieldSort(PivotSortDirection.Ascending);

    private void PivotFieldSortDescendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyPivotFieldSort(PivotSortDirection.Descending);

    private void PivotFieldClearFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryResolvePivotFieldMenuContext() is not { SourceFieldIndex: { } sourceIndex } context ||
            ToPivotHeaderArea(context.Zone) is not { } area)
            return;

        ClearPivotFieldFilters(context.PivotTable, area, sourceIndex);
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
        var selected = GetSelectedPivotFieldListItem();
        if (string.IsNullOrWhiteSpace(selected))
            return;

        MovePivotFieldToZone(selected, zone, -1);
    }

    private void MovePivotFieldToZone(
        string caption,
        PivotFieldDropZone targetZone,
        int insertIndex,
        PivotFieldDragPayload? payload = null)
    {
        var resolution = PivotApplication.ResolveTarget(
            _currentSheetId,
            SheetGrid.SelectedRange,
            PivotTargetFallback.FirstOnSheet);
        if (resolution.Target is not { } target)
            return;

        var pivotTable = target.PivotTable;
        var headers = PivotSourceContext.ReadHeaders(_workbook, pivotTable, target.Sheet);
        var displayedLayout = GetDisplayedOrCurrentPivotLayout(pivotTable);
        PivotFieldBucket? sourceBucket = payload is null
            ? null
            : ToPivotFieldBucket(payload.SourceZone);
        var sourceIndex = PivotFieldLayoutPlanner.ResolveSourceFieldIndex(
            displayedLayout.Areas,
            headers,
            caption,
            sourceBucket,
            payload?.SourceIndex ?? -1);
        if (sourceIndex is null)
            return;

        var adjustedInsertIndex = payload is not null &&
                                  payload.SourceZone == targetZone &&
                                  insertIndex > payload.SourceIndex
            ? insertIndex - 1
            : insertIndex;
        var sourceSheet = PivotUiPlanner.ResolvePivotSourceSheet(_workbook, target.Sheet, pivotTable);
        var validator = new PivotFieldDragValidator(index =>
            PivotUiPlanner.IsNumericSourceField(sourceSheet, pivotTable, index));
        var dropPlan = PivotFieldLayoutPlanner.PlanDrop(
            displayedLayout.Areas,
            headers,
            new PivotFieldDropRequest(
                sourceIndex.Value,
                ToPivotFieldBucket(targetZone),
                adjustedInsertIndex,
                sourceBucket,
                payload?.SourceIndex ?? -1),
            validator);
        if (!dropPlan.Result.IsAllowed)
            return;
        if (dropPlan.Areas is not { } areas)
        {
            ShowPivotApplicationMessage(
                new PivotMessageModel(
                    PivotApplicationIssue.MissingValueField,
                    PivotMessageSeverity.Information),
                UiText.Get("MainWindowMessage_PivotTableFieldsTitle"));
            return;
        }

        ApplyPivotFieldListLayout(
            pivotTable,
            areas.RowFields,
            areas.ColumnFields,
            areas.PageFields,
            areas.DataFields);
    }

    private void ApplyPivotFieldSort(PivotSortDirection direction)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = PivotSourceContext.ReadHeaders(_workbook, pivotTable, sheet);
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
        if (TryResolvePivotFieldMenuContext() is not { SourceFieldIndex: { } sourceIndex } context ||
            ToPivotHeaderArea(context.Zone) is not { } area)
            return;

        var pivotTable = context.PivotTable;
        var allItems = PivotSourceContext.ReadItems(
            _workbook,
            context.Sheet,
            pivotTable,
            sourceIndex).ToList();
        var state = PivotFieldFilterSummary.CreateState(
            pivotTable,
            sourceIndex,
            area,
            PivotUiPlanner.FieldCaption(context.Headers, sourceIndex),
            allItems,
            WpfResourceKeyTextResolver.Instance);
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
                ApplyPivotFieldItemFilter(pivotTable, area, sourceIndex, dialog.SelectedItems, allItems.Count);
                break;
            case PivotFieldFilterDialogAction.ClearItemFilter:
                ApplyPivotFieldItemFilter(pivotTable, area, sourceIndex, null, allItems.Count);
                break;
            case PivotFieldFilterDialogAction.ClearFieldFilters:
                ClearPivotFieldFilters(pivotTable, area, sourceIndex);
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
            .Where(item => !PivotFilterOwnership.BelongsToSourceField(item, sourceIndex))
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
        PivotHeaderArea area,
        int sourceIndex,
        IReadOnlyList<string>? selectedItems,
        int allItemCount)
    {
        var items = selectedItems is null
            ? null
            : PivotFieldFilterPlanner.ResolveItemSelection(selectedItems, allItemCount);
        var selectionState = PivotUiPlanner
            .CreateFieldSelectionState(pivotTable, area, sourceIndex)
            .WithSelectedItems(items);
        ApplyPivotFieldFilters(
            pivotTable,
            selectionState.RowFields,
            selectionState.ColumnFields,
            selectionState.PageFields,
            pivotTable.LabelFilters.ToList(),
            pivotTable.ValueFilters.ToList(),
            pivotTable.Sorts.ToList());
    }

    private void ClearPivotFieldFilters(PivotTableModel pivotTable, PivotHeaderArea area, int sourceIndex)
    {
        var selectionState = PivotUiPlanner
            .CreateFieldSelectionState(pivotTable, area, sourceIndex)
            .WithSelectedItems(null);
        ApplyPivotFieldFilters(
            pivotTable,
            selectionState.RowFields,
            selectionState.ColumnFields,
            selectionState.PageFields,
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
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        ApplyPivotApplicationPlan(
            PivotApplication.PlanMutation(
                new PivotApplicationTarget(sheet, pivotTable),
                new ConfigurePivotTableFieldFiltersCommand(
                    sheet.Id,
                    pivotTable.Name,
                    rowFields,
                    columnFields,
                    pageFields,
                    labelFilters,
                    valueFilters,
                    sorts)),
            UiText.Get("MainWindowMessage_PivotTableFieldsTitle"));
    }

    private void ApplyPivotFieldListLayout(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotDataFieldModel> dataFields,
        bool forceApply = false)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var target = new PivotApplicationTarget(sheet, pivotTable);
        var areas = new PivotFieldAreas(rowFields, columnFields, pageFields, dataFields);
        var plan = PivotApplication.PlanLayout(target, areas);
        if (!plan.CanApply)
        {
            ShowPivotApplicationMessage(
                plan.Message,
                UiText.Get("MainWindowMessage_PivotTableFieldsTitle"));
            return;
        }

        if (!forceApply && PivotFieldListDeferLayoutCheckBox.IsChecked == true)
        {
            _pendingPivotLayout = new PivotFieldLayoutDraft(pivotTable.Name, areas);
            RefreshPivotFieldListPane();
            return;
        }

        var previousVisibleRange = PivotUiPlanner.VisiblePivotRange(pivotTable);
        var outcome = PivotApplication.Execute(plan);
        if (!outcome.Success)
        {
            if (outcome.Message?.Issue != PivotApplicationIssue.CommandFailed)
            {
                ShowPivotApplicationMessage(
                    outcome.Message,
                    UiText.Get("MainWindowMessage_PivotTableFieldsTitle"));
            }
            return;
        }

        _pendingPivotLayout = null;
        ReconcilePivotFieldListSelectionAfterPaneMutation(previousVisibleRange, pivotTable);
        ApplyPivotDisplayTransition(outcome.Action, outcome.Transition);
    }

    private void ApplyPivotFieldView(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        IReadOnlyList<PivotValueFilterModel> valueFilters,
        IReadOnlyList<PivotSortModel> sorts)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var previousVisibleRange = PivotUiPlanner.VisiblePivotRange(pivotTable);
        var plan = PivotApplication.PlanMutation(
            new PivotApplicationTarget(sheet, pivotTable),
            new ConfigurePivotTableViewCommand(
                sheet.Id,
                pivotTable.Name,
                labelFilters,
                valueFilters,
                sorts));
        var outcome = PivotApplication.Execute(plan);
        if (!outcome.Success)
            return;

        ReconcilePivotFieldListSelectionAfterPaneMutation(previousVisibleRange, pivotTable);
        ApplyPivotDisplayTransition(outcome.Action, outcome.Transition);
    }

    private PivotFieldMenuContext? TryResolvePivotFieldMenuContext()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return null;

        var headers = PivotSourceContext.ReadHeaders(_workbook, pivotTable, sheet);
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
            if (PivotFieldListPaneBuilder.GetItemCaption(list.SelectedItem) is { } value)
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

    private static int LastAxisFieldSourceIndexOrDefault(PivotTableModel pivotTable)
    {
        if (pivotTable.RowFields.Count > 0)
            return pivotTable.RowFields[pivotTable.RowFields.Count - 1].SourceFieldIndex;

        return pivotTable.ColumnFields.Count == 0
            ? 0
            : pivotTable.ColumnFields[pivotTable.ColumnFields.Count - 1].SourceFieldIndex;
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

    private static PivotFieldBucket ToPivotFieldBucket(PivotFieldDropZone zone) =>
        zone switch
        {
            PivotFieldDropZone.Rows => PivotFieldBucket.Rows,
            PivotFieldDropZone.Columns => PivotFieldBucket.Columns,
            PivotFieldDropZone.Values => PivotFieldBucket.Values,
            PivotFieldDropZone.Filters => PivotFieldBucket.Filters,
            _ => PivotFieldBucket.Available,
        };

    private static PivotHeaderArea? ToPivotHeaderArea(PivotFieldDropZone? zone) =>
        zone switch
        {
            PivotFieldDropZone.Rows => PivotHeaderArea.Row,
            PivotFieldDropZone.Columns => PivotHeaderArea.Column,
            PivotFieldDropZone.Filters => PivotHeaderArea.Page,
            PivotFieldDropZone.Values => PivotHeaderArea.Value,
            _ => null
        };

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
