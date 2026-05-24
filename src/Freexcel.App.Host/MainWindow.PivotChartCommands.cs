using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void PivotChartBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableContainingSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable, or open a workbook with a PivotTable on the active sheet.",
                "Insert PivotChart",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
}
