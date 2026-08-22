using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void RefreshPivotChartInsertCommandState()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var canInsert = sheet is not null && SheetGrid.SelectedRange is { } selection &&
            PivotUiPlanner.FindPivotTableContainingSelection(sheet, selection) is not null;
        _ribbonState.SetEnabled(FreeXRibbonCommandIds.PivotChartInsert, canInsert);
    }

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
                ChartCommandWorkflowPlanner.BuildAddPivotChartCommand(
                    _currentSheetId,
                    pivotTable,
                    dialog.Result.ChartType,
                    $"{pivotTable.Name} Chart"),
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

        if (!TryExecuteCommand(
                ChartCommandWorkflowPlanner.BuildChangePivotChartTypeCommand(
                    _currentSheetId,
                    chart,
                    dialog.Result.ChartType),
                "Change PivotChart Type"))
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
                ChartCommandWorkflowPlanner.BuildPivotChartOptionsCommand(_currentSheetId, chart, dialog.Result),
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

        var headers = PivotSourceContext.ReadHeaders(_workbook, pivotTable, sheet);
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

    private static PivotFieldBucket? ResolvePivotChartFieldButtonZone(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        string fieldButton,
        string caption)
    {
        if (string.Equals(fieldButton, "Values", StringComparison.OrdinalIgnoreCase) ||
            PivotUiPlanner.FindDataFieldIndex(pivotTable, caption) is not null)
        {
            return PivotFieldBucket.Values;
        }

        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, caption);
        if (sourceIndex is null)
            return null;

        return PivotUiPlanner.ResolvePivotChartFieldArea(pivotTable, sourceIndex.Value) switch
        {
            PivotHeaderArea.Page => PivotFieldBucket.Filters,
            PivotHeaderArea.Column => PivotFieldBucket.Columns,
            _ => PivotFieldBucket.Rows
        };
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

    // Builds the PivotChart field-button (and pivot header-dropdown) context menu from the neutral
    // PivotChartFieldContextMenuPlanner so the menu's order, headers, enablement, and tooltips are single-sourced
    // with the Avalonia port instead of hand-authored here. The live filter/sort state is resolved from the
    // clicked field (TryResolvePivotFieldMenuContext) and threaded into the planner as PivotChartFieldContextMenuState;
    // each emitted item dispatches to the same Click handler the previous ContextMenu wired, and keytips are still
    // assigned at render time by MenuKeyTipAssigner (preserving the previous behavior verbatim).
    private ContextMenu CreatePivotFieldContextMenu()
    {
        var menu = new ContextMenu();
        foreach (var command in PivotChartFieldContextMenuPlanner.BuildCommands(BuildPivotChartFieldContextMenuState()))
            AddPivotChartFieldContextMenuItem(menu.Items, command);

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        return menu;
    }

    private PivotChartFieldContextMenuState BuildPivotChartFieldContextMenuState()
    {
        var context = TryResolvePivotFieldMenuContext();
        var filterState = context is { SourceFieldIndex: { } sourceIndex } &&
                          ToPivotHeaderArea(context.Zone) is { } area
            ? PivotFieldFilterSummary.CreateState(
                context.PivotTable,
                sourceIndex,
                area,
                PivotUiPlanner.FieldCaption(context.Headers, sourceIndex),
                PivotSourceContext.ReadItems(_workbook, context.Sheet, context.PivotTable, sourceIndex),
                WpfResourceKeyTextResolver.Instance)
            : null;
        var valueFieldIndex = context is null
            ? null
            : ResolveValueFieldSettingsIndex(context.PivotTable, context.Caption, context.Zone);

        return new PivotChartFieldContextMenuState(
            HasFilterState: filterState is not null,
            OverallSummary: filterState?.OverallSummary ?? "",
            SelectItemsHeader: filterState is null ? "Select Items..." : PivotFieldFilterSummary.FormatSelectItemsHeader(filterState),
            LabelFilterHeader: filterState is null ? "Label Filter..." : PivotFieldFilterSummary.FormatLabelFilterHeader(filterState),
            ValueFilterHeader: filterState is null ? "Value Filter..." : PivotFieldFilterSummary.FormatValueFilterHeader(filterState),
            ClearFilterHeader: filterState is null ? "Clear Filters from Field" : PivotFieldFilterSummary.FormatClearFilterHeader(filterState),
            CanValueFilter: filterState is not null && context?.PivotTable.DataFields.Count > 0,
            HasAnyFilter: filterState?.HasAnyFilter == true,
            CanValueFieldSettings: valueFieldIndex is not null);
    }

    private void AddPivotChartFieldContextMenuItem(ItemCollection target, PivotChartFieldContextMenuCommand command)
    {
        if (command.IsSeparator)
        {
            target.Add(new Separator());
            return;
        }

        var item = new MenuItem { Header = command.Header, IsEnabled = command.IsEnabled };
        if (!string.IsNullOrWhiteSpace(command.ToolTip))
            item.ToolTip = command.ToolTip;

        if (ResolvePivotChartFieldContextMenuHandler(command.Action) is { } handler)
            item.Click += handler;

        target.Add(item);
    }

    // Maps neutral planner actions to the existing pivot-field Click handlers. The disabled summary banner
    // (Summary) carries no handler, matching the previous non-interactive header MenuItem.
    private RoutedEventHandler? ResolvePivotChartFieldContextMenuHandler(PivotChartFieldContextMenuAction action) =>
        action switch
        {
            PivotChartFieldContextMenuAction.SortAscending => PivotFieldSortAscendingMenuItem_Click,
            PivotChartFieldContextMenuAction.SortDescending => PivotFieldSortDescendingMenuItem_Click,
            PivotChartFieldContextMenuAction.MoreSortOptions => PivotFieldMoreSortOptionsMenuItem_Click,
            PivotChartFieldContextMenuAction.SelectItems => PivotFieldSelectItemsMenuItem_Click,
            PivotChartFieldContextMenuAction.LabelFilter => PivotFieldLabelFilterMenuItem_Click,
            PivotChartFieldContextMenuAction.ValueFilter => PivotFieldValueFilterMenuItem_Click,
            PivotChartFieldContextMenuAction.ClearFilter => PivotFieldClearFilterMenuItem_Click,
            PivotChartFieldContextMenuAction.ValueFieldSettings => PivotFieldValueSettingsMenuItem_Click,
            _ => null
        };
}
