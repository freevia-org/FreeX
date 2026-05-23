using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void PivotTableBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || SheetGrid.SelectedRange is not { } sourceRange)
        {
            MessageBox.Show(
                "Select a source range with a header row before creating a PivotTable.",
                "Insert PivotTable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (sourceRange.RowCount < 2 || sourceRange.ColCount < 2)
        {
            MessageBox.Show(
                "PivotTable source data must include at least two columns and a header row.",
                "Insert PivotTable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new PivotTableDialog(_workbook, _currentSheetId, sourceRange) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryParseWorkbookRange(_currentSheetId, dialog.Result.SourceRangeText, out var dialogSourceRange))
        {
            MessageBox.Show("Enter a valid PivotTable source range.", "Insert PivotTable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sourceSheet = _workbook.GetSheet(dialogSourceRange.Start.Sheet) ?? sheet;
        var dataFieldIndex = PivotUiPlanner.ChooseDefaultDataField(sourceSheet, dialogSourceRange);
        var rowFieldIndex = dataFieldIndex == 0 ? 1 : 0;
        if (dialog.Result.DestinationKind == PivotTableDestinationKind.NewWorksheet)
        {
            var command = new AddPivotTableToNewWorksheetCommand(
                dialogSourceRange,
                PivotUiPlanner.GenerateUniquePivotTableName(sheet),
                rowFieldIndexes: [rowFieldIndex],
                dataFieldIndexes: [dataFieldIndex]);

            if (!TryExecuteCommand(command, "Insert PivotTable"))
                return;

            if (command.CreatedSheetId is { } createdSheetId)
            {
                _currentSheetId = createdSheetId;
                _groupedSheetIds.Clear();
                _groupedSheetIds.Add(_currentSheetId);
                SetActiveCell(new CellAddress(
                    _currentSheetId,
                    AddPivotTableToNewWorksheetCommand.InitialTargetRow,
                    AddPivotTableToNewWorksheetCommand.InitialTargetColumn));
            }

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
            MessageBox.Show("Enter a destination cell on the active worksheet.", "Insert PivotTable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = PivotUiPlanner.GenerateUniquePivotTableName(sheet);

        if (!TryExecuteCommand(
                new AddPivotTableCommand(
                    _currentSheetId,
                    dialogSourceRange,
                    targetRange,
                    name,
                    rowFieldIndexes: [rowFieldIndex],
                    dataFieldIndexes: [dataFieldIndex]),
                "Insert PivotTable"))
            return;

        UpdateViewport();
        if (dialog.Result.OpenFieldList)
            RefreshPivotFieldListPane();
    }

    private void RefreshPivotTableBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable, or open a workbook with a PivotTable on the active sheet.",
                "Refresh PivotTable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(new RefreshPivotTableCommand(_currentSheetId, pivotTable.Name), "Refresh PivotTable"))
            return;

        UpdateViewport();
    }

    private void PivotTableShowDetailsBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = TryShowPivotTableDetails(showMessage: true);
    }

    private bool TryShowPivotTableDetails(bool showMessage)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var selected = SheetGrid.SelectedRange?.Start;
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null || selected is null)
        {
            if (showMessage)
            {
                MessageBox.Show(
                    "Select a value cell inside an existing PivotTable before showing detail rows.",
                    "Show PivotTable Details",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return false;
        }

        if (!TryExecuteCommand(
                new DrillDownPivotTableCommand(_currentSheetId, pivotTable.Name, selected.Value),
                "Show PivotTable Details"))
            return false;

        var detailSheet = _workbook.Sheets.LastOrDefault();
        if (detailSheet is not null)
            _currentSheetId = detailSheet.Id;
        RefreshSheetTabs();
        UpdateViewport();
        return true;
    }

    private void PivotChartBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable, or open a workbook with a PivotTable on the active sheet.",
                "Insert PivotChart",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new AddPivotChartCommand(_currentSheetId, pivotTable.Name, ChartType.Column, $"{pivotTable.Name} Chart"),
                "Insert PivotChart"))
            return;

        UpdateViewport();
    }

    private void PivotChartChangeTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable before changing a PivotChart type.",
                "Change PivotChart Type",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var chart = sheet.Charts.FirstOrDefault(item =>
            item.IsPivotChart &&
            string.Equals(item.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase));
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a PivotChart connected to this PivotTable before changing its type.",
                "Change PivotChart Type",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new PivotChartTypeDialog(chart.Type) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(new ChangePivotChartTypeCommand(_currentSheetId, chart.Id, dialog.Result.ChartType), "Change PivotChart Type"))
            return;

        UpdateViewport();
    }

    private void PivotChartOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable before changing PivotChart options.",
                "PivotChart Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var chart = FindPivotChartForPivotTable(sheet, pivotTable);
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a PivotChart connected to this PivotTable before changing its options.",
                "PivotChart Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new PivotChartOptionsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(
                new ConfigurePivotChartOptionsCommand(
                    _currentSheetId,
                    chart.Id,
                    dialog.Result.ChartStyleId,
                    dialog.Result.ShowFieldButtons,
                    dialog.Result.ShowReportFilterButtons,
                    dialog.Result.ShowAxisFieldButtons,
                    dialog.Result.ShowValueFieldButtons,
                    dialog.Result.ShowDataTable,
                    dialog.Result.ShowDataTableLegendKeys,
                    dialog.Result.RoundedCorners,
                    dialog.Result.ShowHiddenData,
                    dialog.Result.BlankDisplayMode),
                "PivotChart Options"))
            return;

        UpdateViewport();
    }

    private static ChartModel? FindPivotChartForPivotTable(Sheet sheet, PivotTableModel pivotTable) =>
        sheet.Charts.FirstOrDefault(item =>
            item.IsPivotChart &&
            string.Equals(item.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase));

    private void OnPivotChartFieldButtonRequested(ChartModel chart, string fieldButton, System.Windows.Point position)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || !chart.IsPivotChart || string.IsNullOrWhiteSpace(chart.PivotTableName))
            return;

        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, chart.PivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        _pivotChartContextFieldCaption = PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivotTable, headers, fieldButton);
        if (string.IsNullOrWhiteSpace(_pivotChartContextFieldCaption))
            return;

        SetActiveCell(pivotTable.TargetRange.Start);
        RefreshPivotFieldListPane();

        var menu = CreatePivotFieldContextMenu();
        menu.Closed += (_, _) => _pivotChartContextFieldCaption = null;
        menu.PlacementTarget = SheetGrid;
        menu.Placement = PlacementMode.RelativePoint;
        menu.HorizontalOffset = position.X;
        menu.VerticalOffset = position.Y;
        menu.IsOpen = true;
    }

    private ContextMenu CreatePivotFieldContextMenu()
    {
        var menu = new ContextMenu();
        void Add(string header, RoutedEventHandler handler)
        {
            var item = new MenuItem { Header = header };
            item.Click += handler;
            menu.Items.Add(item);
        }

        Add("Sort A to Z", PivotFieldSortAscendingMenuItem_Click);
        Add("Sort Z to A", PivotFieldSortDescendingMenuItem_Click);
        Add("Select Items...", PivotFieldSelectItemsMenuItem_Click);
        Add("Label Filter...", PivotFieldLabelFilterMenuItem_Click);
        Add("Value Filter...", PivotFieldValueFilterMenuItem_Click);
        Add("Clear Filter", PivotFieldClearFilterMenuItem_Click);
        menu.Items.Add(new Separator());
        Add("Value Field Settings...", PivotFieldValueSettingsMenuItem_Click);
        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        return menu;
    }

    private void RefreshPivotFieldListPane()
    {
        if (PivotFieldListPane is null)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
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

        _pivotFieldListAvailableItems = headers
            .Select((caption, index) => new PivotFieldListItem(
                caption,
                rowFields.Any(field => field.SourceFieldIndex == index) ||
                columnFields.Any(field => field.SourceFieldIndex == index) ||
                pageFields.Any(field => field.SourceFieldIndex == index) ||
                dataFields.Any(field => field.SourceFieldIndex == index)))
            .ToList();
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

    private void RefreshSlicerTimelinePane()
    {
        if (SlicerTimelinePane is null)
            return;

        var slicers = _workbook.Slicers
            .Where(slicer => !string.IsNullOrWhiteSpace(slicer.Name))
            .Select(slicer => new SlicerPaneItem(
                slicer.Name,
                slicer.SourceFieldName ?? slicer.CacheName,
                BuildSlicerTiles(slicer)))
            .ToList();
        var timelines = _workbook.Timelines
            .Where(timeline => !string.IsNullOrWhiteSpace(timeline.Name))
            .Select(SlicerTimelinePlanner.BuildTimelineItem)
            .ToList();

        SlicerItemsControl.ItemsSource = slicers;
        TimelineItemsControl.ItemsSource = timelines;
        if (slicers.Count == 0 && timelines.Count == 0)
        {
            SlicerTimelinePane.Visibility = Visibility.Collapsed;
            _slicerTimelinePaneDismissed = false;
        }
        else if (!_slicerTimelinePaneDismissed)
            SlicerTimelinePane.Visibility = Visibility.Visible;
    }

    private IReadOnlyList<SlicerTileItem> BuildSlicerTiles(SlicerModel slicer)
    {
        return SlicerTimelinePlanner.BuildSlicerTiles(slicer, ReadSlicerSourceItems(slicer));
    }

    private IReadOnlyList<string> ReadSlicerSourceItems(SlicerModel slicer)
    {
        if (string.IsNullOrWhiteSpace(slicer.SourcePivotTableName) ||
            string.IsNullOrWhiteSpace(slicer.SourceFieldName))
        {
            return [];
        }

        foreach (var sheet in _workbook.Sheets)
        {
            var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
                string.Equals(pivot.Name, slicer.SourcePivotTableName, StringComparison.OrdinalIgnoreCase));
            if (pivotTable is null)
                continue;

            var headers = ReadPivotSourceHeaders(sheet, pivotTable);
            var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, slicer.SourceFieldName);
            return sourceIndex is null ? [] : ReadPivotFieldItems(sheet, pivotTable, sourceIndex.Value);
        }

        return [];
    }

    private void SlicerTimelinePaneCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        _slicerTimelinePaneDismissed = true;
        SlicerTimelinePane.Visibility = Visibility.Collapsed;
    }

    private void SlicerTileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SlicerTileItem tile })
            return;

        var slicer = _workbook.Slicers.FirstOrDefault(item =>
            string.Equals(item.Name, tile.SlicerName, StringComparison.OrdinalIgnoreCase));
        if (slicer is null)
            return;

        var allItems = ReadSlicerSourceItems(slicer).ToList();
        var selected = SlicerTimelinePlanner.ToggleSlicerSelection(allItems, slicer.SelectedItems, tile.Caption);

        if (!TryExecuteCommand(new SetSlicerSelectionCommand(slicer.Name, selected.ToList()), "Slicer"))
            return;

        UpdateViewport();
    }

    private void SlicerClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slicerName })
            return;

        if (!TryExecuteCommand(new SetSlicerSelectionCommand(slicerName, []), "Slicer"))
            return;

        UpdateViewport();
    }

    private void TimelineApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TimelinePaneItem item })
            return;

        if (!TryExecuteCommand(
                new SetTimelineRangeCommand(
                    item.Name,
                    SlicerTimelinePlanner.NormalizeTimelineDateInput(item.SelectedStartDate),
                    SlicerTimelinePlanner.NormalizeTimelineDateInput(item.SelectedEndDate)),
                "Timeline"))
            return;

        UpdateViewport();
    }

    private void TimelineClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TimelinePaneItem item })
            return;

        if (!TryExecuteCommand(new SetTimelineRangeCommand(item.Name, null, null), "Timeline"))
            return;

        UpdateViewport();
    }

    private void SetPivotContextualTabsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (PivotTableAnalyzeTab is not null)
            PivotTableAnalyzeTab.Visibility = visibility;
        if (PivotTableDesignTab is not null)
            PivotTableDesignTab.Visibility = visibility;
    }

    private void PivotFieldListBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable before showing the field list.",
                "PivotTable Fields",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var dialog = new PivotTableDataSourceDialog(FormatWorkbookRange(pivotTable.SourceRange)) { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.SourceRangeText) ||
            !TryParseWorkbookRange(sheet.Id, dialog.Result.SourceRangeText, out var sourceRange))
            return;

        if (!TryExecuteCommand(
                new ChangePivotTableSourceCommand(_currentSheetId, pivotTable.Name, sourceRange),
                "Change PivotTable Data Source"))
            return;

        UpdateViewport();
    }

    private void PivotInsertSlicerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var fieldName = GetSelectedPivotFieldListItem();
        if (PivotUiPlanner.FindSourceFieldIndex(headers, fieldName) is null)
            fieldName = headers.FirstOrDefault();

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
        var fieldName = GetSelectedPivotFieldListItem();
        if (PivotUiPlanner.FindSourceFieldIndex(headers, fieldName) is null)
            fieldName = headers.FirstOrDefault();

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

    private void PivotGrandTotalsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotSubtotalsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotReportLayoutBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotBlankRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                !pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotStyleGalleryBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowPivotTableOptionsDialog();
    }

    private void PivotRowHeadersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                !pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotColumnHeadersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                !pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotBandedRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                !pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotBandedColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                !pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void ApplyPivotOptions(
        PivotTableModel pivotTable,
        bool showRowGrandTotals,
        bool showColumnGrandTotals,
        bool showSubtotals,
        PivotSubtotalPlacement subtotalPlacement,
        bool repeatItemLabels,
        bool blankLineAfterItems,
        string styleName,
        bool showRowHeaders,
        bool showColumnHeaders,
        bool showRowStripes,
        bool showColumnStripes,
        PivotReportLayout reportLayout,
        string? emptyValueText = null,
        bool updateEmptyValueText = false,
        bool? refreshOnOpen = null,
        bool? saveSourceData = null,
        bool? printTitles = null,
        bool? printExpandCollapseButtons = null,
        string? altTextTitle = null,
        string? altTextDescription = null,
        int? compactRowLabelIndent = null,
        bool updateAltText = false)
    {
        if (!TryExecuteCommand(
                new ConfigurePivotTableOptionsCommand(
                    _currentSheetId,
                    pivotTable.Name,
                    showRowGrandTotals,
                    showColumnGrandTotals,
                    showSubtotals,
                    subtotalPlacement,
                    repeatItemLabels,
                    blankLineAfterItems,
                    styleName,
                    showRowHeaders,
                    showColumnHeaders,
                    showRowStripes,
                    showColumnStripes,
                    reportLayout,
                    emptyValueText,
                    updateEmptyValueText,
                    refreshOnOpen,
                    saveSourceData,
                    printTitles,
                    printExpandCollapseButtons,
                    altTextTitle,
                    altTextDescription,
                    compactRowLabelIndent,
                    updateAltText),
                "PivotTable Options"))
            return;

        UpdateViewport();
    }

    private void ShowPivotTableOptionsDialog()
    {
        if (!TryGetActivePivotTable(out _, out var pivotTable))
            return;

        var cache = _workbook.PivotCaches.FirstOrDefault(item => item.CacheId == pivotTable.CacheId);
        var dialog = new PivotTableOptionsDialog(pivotTable, cache) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPivotOptions(pivotTable, dialog.Result);
    }

    private void PivotGroupFieldBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = ResolveSelectedPivotSourceField(headers, pivotTable);
        if (sourceIndex is null)
            return;

        var currentField = PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value);
        var dialog = new PivotFieldGroupingDialog(headers, currentField) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyPivotGroupingResult(pivotTable, dialog.Result);
    }

    private void PivotUngroupFieldBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = ResolveSelectedPivotSourceField(headers, pivotTable);
        if (sourceIndex is null)
            return;

        ApplyPivotGroupingResult(
            pivotTable,
            PivotFieldGroupingDialog.CreateResult(
                PivotUiPlanner.FieldCaption(headers, sourceIndex.Value),
                sourceIndex.Value,
                PivotFieldGrouping.None,
                groupStart: null,
                groupEnd: null,
                groupInterval: null,
                ungroup: true));
    }

    private void PivotCalculatedFieldBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out _, out var pivotTable))
            return;

        var dialog = new PivotCalculatedFieldDialog { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.Name) ||
            string.IsNullOrWhiteSpace(dialog.Result.Formula))
        {
            return;
        }

        var calculatedFields = pivotTable.CalculatedFields
            .Where(field => !string.Equals(field.Name, dialog.Result.Name, StringComparison.CurrentCultureIgnoreCase))
            .Append(dialog.Result.ToModel())
            .ToList();

        ApplyPivotAdvancedConfiguration(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            calculatedFields,
            pivotTable.CalculatedItems.ToList());
    }

    private void PivotCalculatedItemBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = ResolveSelectedPivotSourceField(headers, pivotTable) ?? 0;
        var dialog = new PivotCalculatedItemDialog(headers, sourceIndex) { Owner = this };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.Result.Name) ||
            string.IsNullOrWhiteSpace(dialog.Result.Formula))
        {
            return;
        }

        var calculatedItems = pivotTable.CalculatedItems
            .Where(item =>
                item.SourceFieldIndex != dialog.Result.SourceFieldIndex ||
                !string.Equals(item.Name, dialog.Result.Name, StringComparison.CurrentCultureIgnoreCase))
            .Append(dialog.Result.ToModel())
            .ToList();

        ApplyPivotAdvancedConfiguration(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            pivotTable.CalculatedFields.ToList(),
            calculatedItems);
    }

    private void ApplyPivotGroupingResult(PivotTableModel pivotTable, PivotFieldGroupingDialogResult result)
    {
        var groupedField = new PivotFieldModel(
            result.SourceFieldIndex,
            Grouping: result.Ungroup ? PivotFieldGrouping.None : result.Grouping,
            GroupStart: result.Ungroup ? null : result.GroupStart,
            GroupEnd: result.Ungroup ? null : result.GroupEnd,
            GroupInterval: result.Ungroup ? null : result.GroupInterval);
        var fieldAlreadyInLayout =
            pivotTable.RowFields.Concat(pivotTable.ColumnFields).Concat(pivotTable.PageFields)
                .Any(field => field.SourceFieldIndex == groupedField.SourceFieldIndex);
        var rowFields = fieldAlreadyInLayout
            ? ReplacePivotField(pivotTable.RowFields, groupedField)
            : pivotTable.RowFields.Append(groupedField).ToList();
        var columnFields = ReplacePivotField(pivotTable.ColumnFields, groupedField);
        var pageFields = ReplacePivotField(pivotTable.PageFields, groupedField);

        ApplyPivotAdvancedConfiguration(
            pivotTable,
            rowFields,
            columnFields,
            pageFields,
            pivotTable.CalculatedFields.ToList(),
            pivotTable.CalculatedItems.ToList());
    }

    private void ApplyPivotAdvancedConfiguration(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> calculatedItems)
    {
        if (!TryExecuteCommand(
                new ConfigurePivotTableCalculatedItemsCommand(
                    _currentSheetId,
                    pivotTable.Name,
                    rowFields,
                    columnFields,
                    pageFields,
                    calculatedFields,
                    calculatedItems),
                "PivotTable Calculations"))
            return;

        _pendingPivotLayout = null;
        RefreshPivotFieldListPane();
        UpdateViewport();
    }

    private int? ResolveSelectedPivotSourceField(IReadOnlyList<string> headers, PivotTableModel pivotTable)
    {
        var selected = GetSelectedPivotFieldListItem();
        return PivotUiPlanner.FindFieldSourceIndex(headers, pivotTable, selected ?? "")
               ?? pivotTable.RowFields.Concat(pivotTable.ColumnFields).Concat(pivotTable.PageFields)
                   .FirstOrDefault()
                   ?.SourceFieldIndex;
    }

    private static List<PivotFieldModel> ReplacePivotField(
        IReadOnlyList<PivotFieldModel> fields,
        PivotFieldModel replacement) =>
        fields
            .Select(field => field.SourceFieldIndex == replacement.SourceFieldIndex
                ? replacement
                : field)
            .ToList();

    private void ApplyPivotOptions(PivotTableModel pivotTable, PivotTableOptionsDialogResult result) =>
        ApplyPivotOptions(
            pivotTable,
            result.ShowRowGrandTotals,
            result.ShowColumnGrandTotals,
            result.ShowSubtotals,
            result.SubtotalPlacement,
            result.RepeatItemLabels,
            result.BlankLineAfterItems,
            result.StyleName,
            result.ShowRowHeaders,
            result.ShowColumnHeaders,
            result.ShowRowStripes,
            result.ShowColumnStripes,
            result.ReportLayout,
            result.EmptyValueText,
            updateEmptyValueText: true,
            result.RefreshOnOpen,
            result.SaveSourceData,
            result.PrintTitles,
            result.PrintExpandCollapseButtons,
            result.AltTextTitle,
            result.AltTextDescription,
            result.CompactRowLabelIndent,
            updateAltText: true);

    private bool TryGetActivePivotTable(out Sheet sheet, out PivotTableModel pivotTable)
    {
        sheet = _workbook.GetSheet(_currentSheetId)!;
        pivotTable = sheet is null ? null! : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange)!;
        return sheet is not null && pivotTable is not null;
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
            PivotUiPlanner.GetFieldListCaption(list.SelectedItem) is not { } caption)
        {
            return;
        }

        DragDrop.DoDragDrop(list, caption, DragDropEffects.Move);
    }

    private void PivotFieldList_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox targetList ||
            e.Data.GetData(DataFormats.StringFormat) is not string caption ||
            GetPivotFieldDropZone(targetList) is not { } targetZone)
        {
            return;
        }

        MovePivotFieldToZone(caption, targetZone, targetList.SelectedIndex);
        e.Handled = true;
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
        var rowFields = sourceIndex is null
            ? pivotTable.RowFields.ToList()
            : pivotTable.RowFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var columnFields = sourceIndex is null
            ? pivotTable.ColumnFields.ToList()
            : pivotTable.ColumnFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var pageFields = sourceIndex is null
            ? pivotTable.PageFields.ToList()
            : pivotTable.PageFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var dataFields = pivotTable.DataFields
            .Where(field => !string.Equals(field.Name, selected, StringComparison.CurrentCultureIgnoreCase) &&
                            (sourceIndex is null || field.SourceFieldIndex != sourceIndex.Value))
            .ToList();

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
    }

    private void PivotFieldSortAscendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyPivotFieldSort(PivotSortDirection.Ascending);

    private void PivotFieldSortDescendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyPivotFieldSort(PivotSortDirection.Descending);

    private void PivotFieldClearFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivotTable, selected);

        var labelFilters = sourceIndex is null
            ? pivotTable.LabelFilters.ToList()
            : pivotTable.LabelFilters.Where(filter => filter.SourceFieldIndex != sourceIndex.Value).ToList();
        var valueFilters = sourceIndex is null
            ? pivotTable.ValueFilters.ToList()
            : pivotTable.ValueFilters.Where(filter => filter.SourceFieldIndex != sourceIndex.Value).ToList();
        var sorts = pivotTable.Sorts
            .Where(sort =>
                (sourceIndex is null || sort.FieldIndex != sourceIndex.Value) &&
                (dataFieldIndex is null || sort.DataFieldIndex != dataFieldIndex.Value))
            .ToList();

        ApplyPivotFieldView(pivotTable, labelFilters, valueFilters, sorts);
    }

    private void PivotFieldSelectItemsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, GetSelectedPivotFieldListItem());
        if (sourceIndex is null)
            return;

        var existingItems = pivotTable.RowFields
            .Concat(pivotTable.ColumnFields)
            .Concat(pivotTable.PageFields)
            .FirstOrDefault(field => field.SourceFieldIndex == sourceIndex.Value)
            ?.SelectedItems;
        var dialog = new PivotFieldFilterDialog(
            ReadPivotFieldItems(sheet, pivotTable, sourceIndex.Value),
            existingItems,
            pivotTable.DataFields.Count > 0)
        {
            Owner = this,
            Title = $"{PivotUiPlanner.FieldCaption(headers, sourceIndex.Value)} Filter"
        };
        if (dialog.ShowDialog() != true)
            return;

        if (dialog.RequestedAction == PivotFieldFilterDialogAction.LabelFilter)
        {
            PivotFieldLabelFilterMenuItem_Click(sender, e);
            return;
        }

        if (dialog.RequestedAction == PivotFieldFilterDialogAction.ValueFilter)
        {
            PivotFieldValueFilterMenuItem_Click(sender, e);
            return;
        }

        var allItems = ReadPivotFieldItems(sheet, pivotTable, sourceIndex.Value).ToList();
        var selectedItems = dialog.SelectedItems;
        var items = selectedItems.Count == 0 || selectedItems.Count == allItems.Count ? null : selectedItems;
        var rowFields = PivotUiPlanner.SetFieldSelectedItems(pivotTable.RowFields, sourceIndex.Value, items);
        var columnFields = PivotUiPlanner.SetFieldSelectedItems(pivotTable.ColumnFields, sourceIndex.Value, items);
        var pageFields = PivotUiPlanner.SetFieldSelectedItems(pivotTable.PageFields, sourceIndex.Value, items);

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, pivotTable.DataFields.ToList());
    }

    private void PivotFieldLabelFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, GetSelectedPivotFieldListItem());
        if (sourceIndex is null)
            return;

        var dialog = new PivotLabelFilterDialog(sourceIndex.Value) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultFilter is not { } filter)
            return;

        var labelFilters = pivotTable.LabelFilters
            .Where(item => item.SourceFieldIndex != sourceIndex.Value)
            .Append(filter)
            .ToList();
        ApplyPivotFieldView(pivotTable, labelFilters, pivotTable.ValueFilters.ToList(), pivotTable.Sorts.ToList());
    }

    private void PivotFieldValueFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null || pivotTable.DataFields.Count == 0)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, GetSelectedPivotFieldListItem());
        if (sourceIndex is null)
            return;

        var dialog = new PivotValueFilterDialog(sourceIndex.Value) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultFilter is not { } filter)
            return;

        var valueFilters = pivotTable.ValueFilters
            .Where(item => item.SourceFieldIndex != sourceIndex.Value)
            .Append(filter)
            .ToList();
        ApplyPivotFieldView(pivotTable, pivotTable.LabelFilters.ToList(), valueFilters, pivotTable.Sorts.ToList());
    }

    private void PivotFieldValueSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivotTable, selected);
        if (dataFieldIndex is null)
        {
            var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
            if (sourceIndex is null)
                return;
            dataFieldIndex = pivotTable.DataFields.FindIndex(field => field.SourceFieldIndex == sourceIndex.Value);
            if (dataFieldIndex < 0)
                return;
        }

        var current = pivotTable.DataFields[dataFieldIndex.Value];
        var dialog = new PivotValueFieldSettingsDialog(current, headers) { Owner = this };
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

        var rowFields = pivotTable.RowFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var columnFields = pivotTable.ColumnFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var pageFields = pivotTable.PageFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var dataFields = pivotTable.DataFields.ToList();
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
                if (dataFields.All(dataField => dataField.SourceFieldIndex != sourceIndex.Value))
                {
                    dataFields.Add(PivotUiPlanner.CreateDefaultDataField(sheet, pivotTable, headers, sourceIndex.Value));
                }
                break;
        }

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
    }

    private void MovePivotFieldToZone(string caption, PivotFieldDropZone targetZone, int insertIndex)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindFieldSourceIndex(headers, pivotTable, caption);
        var draggedDataField = pivotTable.DataFields.FirstOrDefault(field =>
            string.Equals(field.Name, caption, StringComparison.CurrentCultureIgnoreCase));
        if (sourceIndex is null && draggedDataField is null)
            return;

        var rowFields = pivotTable.RowFields.Where(field => field.SourceFieldIndex != sourceIndex).ToList();
        var columnFields = pivotTable.ColumnFields.Where(field => field.SourceFieldIndex != sourceIndex).ToList();
        var pageFields = pivotTable.PageFields.Where(field => field.SourceFieldIndex != sourceIndex).ToList();
        var dataFields = pivotTable.DataFields
            .Where(field => !string.Equals(field.Name, caption, StringComparison.CurrentCultureIgnoreCase) &&
                            field.SourceFieldIndex != sourceIndex)
            .ToList();

        if (targetZone == PivotFieldDropZone.Available)
        {
            ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
            return;
        }

        if (sourceIndex is null)
            return;

        switch (targetZone)
        {
            case PivotFieldDropZone.Rows:
                PivotUiPlanner.InsertOrAppend(rowFields, PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value), insertIndex);
                break;
            case PivotFieldDropZone.Columns:
                PivotUiPlanner.InsertOrAppend(columnFields, PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value), insertIndex);
                break;
            case PivotFieldDropZone.Filters:
                PivotUiPlanner.InsertOrAppend(pageFields, PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value), insertIndex);
                break;
            case PivotFieldDropZone.Values:
                var valueField = draggedDataField ?? PivotUiPlanner.CreateDefaultDataField(sheet, pivotTable, headers, sourceIndex.Value);
                PivotUiPlanner.InsertOrAppend(dataFields, valueField, insertIndex);
                break;
        }

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
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
                FieldIndex: pivotTable.RowFields.LastOrDefault()?.SourceFieldIndex ??
                            pivotTable.ColumnFields.LastOrDefault()?.SourceFieldIndex ??
                            0));
        }
        else
        {
            sorts.Add(new PivotSortModel(PivotSortTarget.Label, direction, FieldIndex: sourceIndex.GetValueOrDefault()));
        }

        ApplyPivotFieldView(pivotTable, pivotTable.LabelFilters.ToList(), pivotTable.ValueFilters.ToList(), sorts);
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
            MessageBox.Show(
                "A PivotTable requires at least one value field.",
                "PivotTable Fields",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

        if (!TryExecuteCommand(
                new ConfigurePivotTableLayoutCommand(_currentSheetId, pivotTable.Name, rowFields, columnFields, pageFields, dataFields),
                "PivotTable Fields"))
            return;

        _pendingPivotLayout = null;
        UpdateViewport();
    }

    private void ApplyPivotFieldView(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        IReadOnlyList<PivotValueFilterModel> valueFilters,
        IReadOnlyList<PivotSortModel> sorts)
    {
        if (!TryExecuteCommand(
                new ConfigurePivotTableViewCommand(_currentSheetId, pivotTable.Name, labelFilters, valueFilters, sorts),
                "PivotTable Field"))
            return;

        UpdateViewport();
    }

    private string? GetSelectedPivotFieldListItem()
    {
        if (!string.IsNullOrWhiteSpace(_pivotChartContextFieldCaption))
            return _pivotChartContextFieldCaption;

        foreach (var list in new[] { PivotAvailableFieldsList, PivotRowsList, PivotColumnsList, PivotValuesList, PivotFiltersList })
        {
            if (PivotUiPlanner.GetFieldListCaption(list.SelectedItem) is { } value)
                return value;
        }

        return null;
    }

    private Sheet GetPivotSourceSheet(Sheet fallbackSheet, PivotTableModel pivotTable) =>
        _workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) ?? fallbackSheet;

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

        return headers;
    }

    private IReadOnlyList<string> ReadPivotFieldItems(Sheet sheet, PivotTableModel pivotTable, int sourceFieldIndex)
    {
        var sourceSheet = GetPivotSourceSheet(sheet, pivotTable);
        var sourceColumn = pivotTable.SourceRange.Start.Col + (uint)sourceFieldIndex;
        var values = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        for (var row = pivotTable.SourceRange.Start.Row + 1; row <= pivotTable.SourceRange.End.Row; row++)
        {
            var text = SpreadsheetDisplayFormatter.FormatCellValue(sourceSheet.GetValue(row, sourceColumn)).Trim();
            values.Add(string.IsNullOrWhiteSpace(text) ? "(blank)" : text);
        }

        return values.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private bool TryParseWorkbookRange(SheetId defaultSheetId, string input, out GridRange range)
        => WorkbookRangeTextCodec.TryParse(
            defaultSheetId,
            input,
            sheetName => _workbook.Sheets.FirstOrDefault(item =>
                string.Equals(item.Name, sheetName, StringComparison.CurrentCultureIgnoreCase))?.Id,
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

}
