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
                "Select a cell inside an existing PivotTable, or open a workbook with a PivotTable on the active sheet.",
                "Insert PivotChart");
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
                "Select a cell inside an existing PivotTable before changing a PivotChart type.",
                "Change PivotChart Type");
            return;
        }

        var chart = FindPivotChartForPivotTable(sheet, pivotTable);
        if (chart is null)
        {
            _messageService.ShowInfo(
                "Insert or select a PivotChart connected to this PivotTable before changing its type.",
                "Change PivotChart Type");
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
                "Select a cell inside an existing PivotTable before changing PivotChart options.",
                "PivotChart Options");
            return;
        }

        var chart = FindPivotChartForPivotTable(sheet, pivotTable);
        if (chart is null)
        {
            _messageService.ShowInfo(
                "Insert or select a PivotChart connected to this PivotTable before changing its options.",
                "PivotChart Options");
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

        SetActiveCell(pivotTable.TargetRange.Start);
        RefreshPivotFieldListPane();

        var menu = CreatePivotFieldContextMenu();
        menu.Closed += (_, _) => _pivotFieldMenuContextCaption = null;
        menu.PlacementTarget = SheetGrid;
        menu.Placement = PlacementMode.RelativePoint;
        menu.HorizontalOffset = position.X;
        menu.VerticalOffset = position.Y;
        menu.IsOpen = true;
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
        menu.Items.Add(new MenuItem
        {
            Header = "More Sort Options...",
            IsEnabled = false,
            ToolTip = "Custom sort lists and manual PivotTable ordering are not yet supported."
        });
        menu.Items.Add(new Separator());
        Add("Value Field Settings...", PivotFieldValueSettingsMenuItem_Click);
        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        return menu;
    }
}
