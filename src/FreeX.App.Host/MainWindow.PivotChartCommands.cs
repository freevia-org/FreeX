using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void PivotChartBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableContainingSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_PivotChartInsertSelectPivot"),
                UiText.Get("MainWindowMessage_PivotChartInsertTitle"));
            return;
        }

        var dialog = new PivotChartTypeDialog(ChartType.Column) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(
                new AddPivotChartCommand(_currentSheetId, pivotTable.Name, dialog.Result.ChartType, $"{pivotTable.Name} Chart"),
                "Insert PivotChart"))
            return;

        UpdateViewport();
    }

    private void PivotChartChangeTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_PivotChartChangeTypeSelectPivot"),
                UiText.Get("MainWindowMessage_PivotChartChangeTypeTitle"));
            return;
        }

        var chart = FindPivotChartForPivotTable(sheet, pivotTable);
        if (chart is null)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_PivotChartChangeTypeInsertFirst"),
                UiText.Get("MainWindowMessage_PivotChartChangeTypeTitle"));
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
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_PivotChartOptionsSelectPivot"),
                UiText.Get("MainWindowMessage_PivotChartOptionsTitle"));
            return;
        }

        var chart = FindPivotChartForPivotTable(sheet, pivotTable);
        if (chart is null)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_PivotChartOptionsInsertFirst"),
                UiText.Get("MainWindowMessage_PivotChartOptionsTitle"));
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

    private static ChartModel? FindPivotChartForPivotTable(Sheet sheet, PivotTableModel pivotTable)
    {
        foreach (var item in sheet.Charts)
        {
            if (item.IsPivotChart &&
                string.Equals(item.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private void OnPivotChartFieldButtonRequested(ChartModel chart, string fieldButton, System.Windows.Point position)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || !chart.IsPivotChart || string.IsNullOrWhiteSpace(chart.PivotTableName))
            return;

        var pivotTable = FindPivotTableByName(sheet, chart.PivotTableName);
        if (pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        _pivotFieldMenuContextCaption = PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivotTable, headers, fieldButton);
        if (string.IsNullOrWhiteSpace(_pivotFieldMenuContextCaption))
            return;
        _pivotFieldMenuContextZone = ResolvePivotChartFieldButtonZone(pivotTable, headers, fieldButton, _pivotFieldMenuContextCaption);

        SetActiveCell(pivotTable.TargetRange.Start);
        RefreshPivotFieldListPane();

        var menu = CreatePivotFieldContextMenu();
        menu.Closed += (_, _) => ClearPivotFieldMenuContext();
        menu.PlacementTarget = SheetGrid;
        menu.Placement = PlacementMode.RelativePoint;
        menu.HorizontalOffset = position.X;
        menu.VerticalOffset = position.Y;
        menu.IsOpen = true;
    }

    private static PivotFieldDropZone? ResolvePivotChartFieldButtonZone(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        string fieldButton,
        string caption)
    {
        if (string.Equals(fieldButton, "Values", StringComparison.OrdinalIgnoreCase) ||
            PivotUiPlanner.FindDataFieldIndex(pivotTable, caption) is not null)
        {
            return PivotFieldDropZone.Values;
        }

        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, caption);
        if (sourceIndex is null)
            return null;

        if (pivotTable.PageFields.Any(field => field.SourceFieldIndex == sourceIndex.Value))
            return PivotFieldDropZone.Filters;
        if (pivotTable.ColumnFields.Any(field => field.SourceFieldIndex == sourceIndex.Value))
            return PivotFieldDropZone.Columns;

        return PivotFieldDropZone.Rows;
    }

    private static PivotTableModel? FindPivotTableByName(Sheet sheet, string name)
    {
        foreach (var pivot in sheet.PivotTables)
        {
            if (string.Equals(pivot.Name, name, StringComparison.OrdinalIgnoreCase))
                return pivot;
        }

        return null;
    }

    private ContextMenu CreatePivotFieldContextMenu()
    {
        var menu = new ContextMenu();
        var context = TryResolvePivotFieldMenuContext();
        var filterState = context is { SourceFieldIndex: { } sourceIndex }
            ? PivotFieldFilterSummary.CreateState(
                context.PivotTable,
                sourceIndex,
                PivotUiPlanner.FieldCaption(context.Headers, sourceIndex),
                ReadPivotFieldItems(context.Sheet, context.PivotTable, sourceIndex))
            : null;
        var valueFieldIndex = context is null
            ? null
            : ResolveValueFieldSettingsIndex(context.PivotTable, context.Caption, context.Zone);

        void Add(string header, RoutedEventHandler handler, bool isEnabled = true, string? toolTip = null)
        {
            var item = new MenuItem { Header = header, IsEnabled = isEnabled };
            if (!string.IsNullOrWhiteSpace(toolTip))
                item.ToolTip = toolTip;
            item.Click += handler;
            menu.Items.Add(item);
        }

        if (filterState is not null)
        {
            menu.Items.Add(new MenuItem
            {
                Header = filterState.OverallSummary,
                IsEnabled = false,
                ToolTip = "Current filter state for this PivotTable field."
            });
            menu.Items.Add(new Separator());
        }

        Add("Sort A to Z", PivotFieldSortAscendingMenuItem_Click);
        Add("Sort Z to A", PivotFieldSortDescendingMenuItem_Click);
        Add("More Sort Options...", PivotFieldMoreSortOptionsMenuItem_Click, filterState is not null, "Open PivotTable sort options for this field.");
        menu.Items.Add(new Separator());
        Add(filterState is null ? "Select Items..." : PivotFieldFilterSummary.FormatSelectItemsHeader(filterState), PivotFieldSelectItemsMenuItem_Click, filterState is not null);
        Add(filterState is null ? "Label Filter..." : PivotFieldFilterSummary.FormatLabelFilterHeader(filterState), PivotFieldLabelFilterMenuItem_Click, filterState is not null);
        Add(filterState is null ? "Value Filter..." : PivotFieldFilterSummary.FormatValueFilterHeader(filterState), PivotFieldValueFilterMenuItem_Click, filterState is not null && context?.PivotTable.DataFields.Count > 0);
        Add(
            filterState is null ? "Clear Filters from Field" : PivotFieldFilterSummary.FormatClearFilterHeader(filterState),
            PivotFieldClearFilterMenuItem_Click,
            filterState?.HasAnyFilter == true,
            filterState?.HasAnyFilter == true ? null : "No item, label, or value filters are active for this field.");
        menu.Items.Add(new Separator());
        Add(
            "Value Field Settings...",
            PivotFieldValueSettingsMenuItem_Click,
            valueFieldIndex is not null,
            valueFieldIndex is null
                ? "Select a value field, the PivotChart Values button, or a PivotTable with one value field."
                : "Open settings for the relevant PivotTable value field.");
        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        return menu;
    }
}
