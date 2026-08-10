using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Localization;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaCanvas = Avalonia.Controls.Canvas;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly ResourceKeyTextResolver PivotFieldFilterText =
        new(UiText.Get, UiText.Format);

    private void AttachPivotFieldContextMenu(
        Control chip,
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldListItemModel field)
    {
        chip.Focusable = true;
        AutomationProperties.SetAutomationId(
            chip,
            $"PivotField_{field.Bucket}_{field.SourceFieldIndex}_{field.DataFieldIndex?.ToString() ?? "source"}");
        AvaloniaManagedContextMenu.Attach(
            chip,
            () => AvaloniaPivotFieldContextMenu.BuildItems(
                includeRemove: field.Bucket != PivotFieldBucket.Available,
                UiText.Get,
                action => DispatchPivotFieldContextMenuAction(pivot, headers, field, action)));
    }

    private void DispatchPivotFieldContextMenuAction(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldListItemModel field,
        PivotFieldContextMenuAction action)
    {
        if (action == PivotFieldContextMenuAction.Remove)
        {
            ApplyPivotFieldDrop(
                pivot,
                headers,
                new PivotFieldDropRequest(field.SourceFieldIndex, PivotFieldBucket.Available));
            return;
        }

        var target = ResolvePivotHeaderTarget(pivot, headers, field) ??
            new PivotHeaderDropdownTargetModel(
                pivot.Name,
                field.Caption,
                field.SourceFieldIndex,
                PivotHeaderArea.Row,
                IsActive: false,
                field.DataFieldIndex);

        if (action == PivotFieldContextMenuAction.SelectItems)
        {
            OpenPivotItemFilter(pivot, headers, target);
            return;
        }

        var headerAction = action switch
        {
            PivotFieldContextMenuAction.SortAscending => PivotHeaderMenuAction.SortAscending,
            PivotFieldContextMenuAction.SortDescending => PivotHeaderMenuAction.SortDescending,
            PivotFieldContextMenuAction.LabelFilter => PivotHeaderMenuAction.LabelFilter,
            PivotFieldContextMenuAction.ValueFilter => PivotHeaderMenuAction.ValueFilter,
            PivotFieldContextMenuAction.ClearFilter => PivotHeaderMenuAction.ClearFilter,
            PivotFieldContextMenuAction.ValueFieldSettings => PivotHeaderMenuAction.ValueFieldSettings,
            _ => PivotHeaderMenuAction.Separator,
        };
        if (headerAction != PivotHeaderMenuAction.Separator)
            InvokePivotHeaderAction(pivot, headers, target, headerAction, BuildPivotDragValidator(pivot));
    }

    private Control? BuildPivotChartFieldButtonOverlay(ChartModel chart)
    {
        if (!chart.IsPivotChart || !chart.ShowPivotChartFieldButtons ||
            string.IsNullOrWhiteSpace(chart.PivotTableName))
        {
            return null;
        }

        var pivot = _session.ActiveSheet.PivotTables.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, chart.PivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivot is null)
            return null;

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(4),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        };

        if (chart.ShowPivotChartReportFilterButtons && pivot.PageFields.Count > 0)
            AddPivotChartFieldButton(buttons, chart, pivot, headers, "Report Filter");
        if (chart.ShowPivotChartAxisFieldButtons)
            AddPivotChartFieldButton(buttons, chart, pivot, headers, "Axis Fields");
        if (chart.ShowPivotChartValueFieldButtons)
            AddPivotChartFieldButton(buttons, chart, pivot, headers, "Values");

        return buttons.Children.Count == 0 ? null : buttons;
    }

    private void AddPivotChartFieldButton(
        Panel parent,
        ChartModel chart,
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        string fieldButton)
    {
        var target = ResolvePivotChartTarget(pivot, headers, fieldButton);
        if (target is null)
            return;

        var button = new Button
        {
            Content = fieldButton,
            FontSize = 10,
            Padding = new Thickness(5, 1),
            MinHeight = 20,
            Focusable = true,
        };
        AutomationProperties.SetAutomationId(button, $"PivotChartFieldButton_{fieldButton.Replace(" ", string.Empty)}");
        AutomationProperties.SetName(button, $"PivotChart {fieldButton}");
        button.Click += (_, _) => SelectChart(chart);
        AvaloniaManagedContextMenu.Attach(
            button,
            () => AvaloniaPivotChartFieldContextMenu.BuildItems(
                BuildPivotChartFieldContextMenuState(pivot, target),
                action => DispatchPivotChartFieldContextMenuAction(pivot, headers, target, action)));
        parent.Children.Add(button);
    }

    private void AttachPivotChartHeaderContextMenu(
        Control anchor,
        PivotHeaderDropdownTargetModel target)
    {
        var pivot = _session.ActiveSheet.PivotTables.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, target.PivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivot is null)
            return;

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        AvaloniaManagedContextMenu.Attach(
            anchor,
            () => AvaloniaPivotChartFieldContextMenu.BuildItems(
                BuildPivotChartFieldContextMenuState(pivot, target),
                action => DispatchPivotChartFieldContextMenuAction(pivot, headers, target, action)));
    }

    private static PivotHeaderDropdownTargetModel? ResolvePivotChartTarget(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        string fieldButton)
    {
        var caption = PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivot, headers, fieldButton);
        if (string.IsNullOrWhiteSpace(caption))
            return null;

        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivot, caption);
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, caption);
        if (dataFieldIndex is { } dataIndex)
        {
            var dataField = pivot.DataFields[dataIndex];
            return new PivotHeaderDropdownTargetModel(
                pivot.Name, caption, dataField.SourceFieldIndex, PivotHeaderArea.Value, false, dataIndex);
        }
        if (sourceIndex is not { } index)
            return null;

        var area = pivot.PageFields.Any(field => field.SourceFieldIndex == index)
            ? PivotHeaderArea.Page
            : pivot.ColumnFields.Any(field => field.SourceFieldIndex == index)
                ? PivotHeaderArea.Column
                : PivotHeaderArea.Row;
        return new PivotHeaderDropdownTargetModel(pivot.Name, caption, index, area, false);
    }

    private PivotChartFieldContextMenuState BuildPivotChartFieldContextMenuState(
        PivotTableModel pivot,
        PivotHeaderDropdownTargetModel target)
    {
        var sourceIndex = target.SourceFieldIndex;
        var filterState = PivotFieldFilterSummary.CreateState(
            pivot,
            sourceIndex,
            target.Area,
            target.FieldCaption,
            PivotSourceContext.ReadItems(
                _session.Workbook,
                _session.ActiveSheet,
                pivot,
                sourceIndex),
            PivotFieldFilterText);
        var hasFilter = filterState.HasStoredFilter;
        var summary = hasFilter ? $"{target.FieldCaption}: Filtered" : $"{target.FieldCaption}: (All)";

        return new PivotChartFieldContextMenuState(
            HasFilterState: target.Area != PivotHeaderArea.Value,
            OverallSummary: summary,
            SelectItemsHeader: "Select Items...",
            LabelFilterHeader: "Label Filter...",
            ValueFilterHeader: "Value Filter...",
            ClearFilterHeader: $"Clear Filters from {target.FieldCaption}",
            CanValueFilter: target.Area != PivotHeaderArea.Value && pivot.DataFields.Count > 0,
            HasAnyFilter: hasFilter,
            CanValueFieldSettings: target.Area == PivotHeaderArea.Value || pivot.DataFields.Count == 1);
    }

    private void DispatchPivotChartFieldContextMenuAction(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target,
        PivotChartFieldContextMenuAction action)
    {
        if (action == PivotChartFieldContextMenuAction.SelectItems)
        {
            OpenPivotItemFilter(pivot, headers, target);
            return;
        }

        var headerAction = action switch
        {
            PivotChartFieldContextMenuAction.SortAscending => PivotHeaderMenuAction.SortAscending,
            PivotChartFieldContextMenuAction.SortDescending => PivotHeaderMenuAction.SortDescending,
            PivotChartFieldContextMenuAction.MoreSortOptions => PivotHeaderMenuAction.MoreSortOptions,
            PivotChartFieldContextMenuAction.LabelFilter => PivotHeaderMenuAction.LabelFilter,
            PivotChartFieldContextMenuAction.ValueFilter => PivotHeaderMenuAction.ValueFilter,
            PivotChartFieldContextMenuAction.ClearFilter => PivotHeaderMenuAction.ClearFilter,
            PivotChartFieldContextMenuAction.ValueFieldSettings => PivotHeaderMenuAction.ValueFieldSettings,
            _ => PivotHeaderMenuAction.Separator,
        };
        if (headerAction != PivotHeaderMenuAction.Separator)
            InvokePivotHeaderAction(pivot, headers, target, headerAction, BuildPivotDragValidator(pivot));
    }

    private Control? BuildWaterfallPointContextOverlay(ChartModel chart, ChartLayout layout)
    {
        if (chart.Type != ChartType.Waterfall)
            return null;

        var canvas = new AvaloniaCanvas { IsHitTestVisible = true };
        foreach (var bar in layout.Series.SelectMany(series => series.Bars))
        {
            var anchor = new Border
            {
                Width = Math.Max(6, bar.Rect.Width),
                Height = Math.Max(6, bar.Rect.Height),
                Background = Brushes.Transparent,
                Focusable = true,
            };
            AutomationProperties.SetAutomationId(anchor, $"WaterfallPoint_{bar.PointIndex}");
            AutomationProperties.SetName(anchor, $"Waterfall point {bar.PointIndex + 1}");
            anchor.PointerPressed += (_, args) =>
            {
                if (args.GetCurrentPoint(anchor).Properties.IsLeftButtonPressed)
                {
                    SelectChart(chart);
                    anchor.Focus();
                }
            };
            AvaloniaManagedContextMenu.Attach(
                anchor,
                () => AvaloniaWaterfallPointContextMenu.BuildItems(
                    chart,
                    bar.PointIndex,
                    () => ToggleWaterfallTotalPoint(chart, bar.PointIndex)));
            AvaloniaCanvas.SetLeft(anchor, bar.Rect.Left);
            AvaloniaCanvas.SetTop(anchor, bar.Rect.Top);
            canvas.Children.Add(anchor);
        }

        return canvas.Children.Count == 0 ? null : canvas;
    }

    private void ToggleWaterfallTotalPoint(ChartModel chart, int pointIndex)
    {
        var command = WaterfallChartContextMenuPlanner.CreateToggleCommand(
            _session.ActiveSheet.Id,
            chart,
            pointIndex);
        if (command is null)
            return;

        var result = _session.ExecuteReviewCommand(command);
        RefreshShell(result.Success ? "Set as Total" : result.ErrorMessage ?? "Set as Total failed");
    }
}
