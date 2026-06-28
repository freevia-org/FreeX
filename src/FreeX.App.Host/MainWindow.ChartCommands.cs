using System;
using System.Windows;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void InsertChartButton_Click(object sender, RoutedEventArgs e)
        => InsertChartOfType(ChartType.Column);

    private void InsertEmbeddedChart() => InsertChartOfType(ChartType.Column);

    private void InsertChartSheet()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        AddChartSheetCommand? command = null;
        IWorkbookCommand CreateCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            var sheet = _workbook.GetSheet(_currentSheetId);
            command = ChartInsertionPlanner.BuildChartSheetCommand(
                sheet,
                _currentSheetId,
                currentRange,
                ChartType.Column,
                "Chart");
            return command;
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Insert Chart Sheet");
            return;
        }

        _repeatPostAction = null;
        if (command?.CreatedSheetId is { } createdSheetId)
        {
            _currentSheetId = createdSheetId;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            _sheetGroupAnchor = _currentSheetId;
        }

        RefreshSheetTabs();
        UpdateViewport();
    }

    private void InsertChartOfType(ChartType type)
    {
        if (!ChartAuthoringPlanner.CanAuthor(type))
        {
            ShowDeferredChartFamilyMessage();
            return;
        }

        if (SheetGrid.SelectedRange is not { } range) return;
        AddChartCommand? command = null;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Chart",
                range,
                currentRange =>
                {
                    var sheet = _workbook.GetSheet(_currentSheetId);
                    var plan = sheet is null
                        ? ChartInsertionPlanner.CreateEmbeddedChartPlan(
                            _currentSheetId,
                            currentRange,
                            type,
                            "Chart",
                            ChartInsertionPlanner.DefaultPlacement)
                        : ChartInsertionPlanner.CreateEmbeddedChartPlan(
                            sheet,
                            currentRange,
                            type,
                            new ChartInsertionViewport(
                                SheetGrid.Viewport,
                                CalculateViewportAvailableWidth(SheetGrid.ActualWidth, SheetGrid.ActualRowHeaderWidth, _zoomLevel),
                                Math.Max(0, (SheetGrid.ActualHeight - SheetGrid.EffectiveColHeaderHeight) / _zoomLevel)),
                            "Chart");
                    command = plan.Command;
                    return command;
                }))
            return;

        UpdateViewport();
        if (command is not null)
            SelectInsertedChart(command.ChartId);
    }

    private void SelectInsertedChart(Guid chartId)
    {
        SheetGrid.SelectedObjectId = chartId;
        SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Chart;
        SheetGrid.InvalidateVisual();
    }

    private void InsertChartPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InsertChartDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        InsertChartOfType(dialog.Result.ChartType);
    }

    private void ChangeChartTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveNormalChart("Change Chart Type", out var chart))
            return;

        var dialog = new ChangeChartTypeDialog(chart.Type) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(new ChangeChartTypeCommand(_currentSheetId, chart.Id, dialog.Result.ChartType), "Change Chart Type"))
            return;

        UpdateViewport();
    }

    private void SelectChartDataSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveNormalChart("Select Data Source", out var chart))
            return;

        SelectDataSourceDialog? dialog = null;
        dialog = new SelectDataSourceDialog(
            FormatRangeReference(chart.DataRange.Start, chart.DataRange.End),
            chart.FirstColIsCategories,
            request => ApplySelectDataSourceRangeSelection(dialog, request),
            sheetId: _currentSheetId,
            resolveSheetId: ResolveSheetIdByName)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
            return;

        if (!ChartInputParser.TryParseDataRange(dialog.Result.SourceRangeText, _currentSheetId, ResolveSheetIdByName, out var dataRange))
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_ChartInvalidDataRange"),
                UiText.Get("MainWindowMessage_SelectDataSourceTitle"));
            return;
        }

        if (!TryExecuteCommand(
                new ChangeChartSourceCommand(
                    _currentSheetId,
                    chart.Id,
                    dataRange,
                    firstRowIsHeader: chart.FirstRowIsHeader,
                    firstColIsCategories: dialog.Result.FirstColumnIsCategories),
                "Select Data Source"))
            return;

        UpdateViewport();
    }

    private void ApplySelectDataSourceRangeSelection(
        SelectDataSourceDialog? dialog,
        SelectDataSourceRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(FormatWorkbookRange(selectedRange)));
    }

    private void MoveChartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActiveNormalChart("Move Chart", out var chart))
            return;

        var currentSheet = _workbook.GetSheet(_currentSheetId);
        if (currentSheet is null)
            return;

        var dialog = new MoveChartDialog(currentSheet.Name) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (dialog.Result.TargetKind == MoveChartTargetKind.NewChartSheet)
        {
            if (!TryExecuteCommand(new MoveChartToNewSheetCommand(_currentSheetId, chart.Id, dialog.Result.TargetName), "Move Chart"))
                return;

            var createdSheet = _workbook.GetSheet(dialog.Result.TargetName);
            if (createdSheet is not null)
                _currentSheetId = createdSheet.Id;
        }
        else
        {
            var targetSheet = _workbook.GetSheet(dialog.Result.TargetName);
            if (targetSheet is null)
            {
                _messageService.ShowWarning(
                    UiText.Get("MainWindowMessage_ChartTargetSheetNotFound"),
                    UiText.Get("MainWindowMessage_MoveChartTitle"));
                return;
            }

            if (!TryExecuteCommand(new MoveChartCommand(_currentSheetId, chart.Id, targetSheet.Id), "Move Chart"))
                return;

            _currentSheetId = targetSheet.Id;
        }

        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
        UpdateViewport();
    }

    private void ChartStylesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog("Chart Styles", "Insert or select a chart before choosing a chart style.", out var chart))
            return;

        var dialog = new ChartStyleDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(new SetChartStyleCommand(_currentSheetId, chart.Id, dialog.Result.ChartStyleId), "Chart Styles"))
            return;

        UpdateViewport();
    }

    private void ResizeSelectedChartObject()
    {
        if (!TryGetActiveNormalChart("Chart Size", out var chart))
            return;

        var dialog = new ObjectSizeDialog(chart.Width, chart.Height, UiText.Get("MainWindowMessage_ObjectSizeTitle")) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteCommand(
                new SetChartBoundsCommand(
                    _currentSheetId,
                    chart.Id,
                    chart.Left,
                    chart.Top,
                    dialog.Result.Width,
                    dialog.Result.Height),
                "Chart Size"))
            return;

        SheetGrid.SelectedObjectId = chart.Id;
        SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Chart;
        UpdateViewport();
    }

    private void FormatChartAreaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog("Format Chart Area", "Insert or select a chart before formatting the chart area.", out var chart))
            return;

        var dialog = new ChartAreaLegendDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!ApplyChartLayoutDialogResult("Format Chart Area", chart, dialog.Result.ToOptions()))
            return;

        UpdateViewport();
    }

    private bool TryGetActiveNormalChart(string caption, out ChartModel chart)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        chart = null!;
        chart = ChartWorkflowTargetPlanner.FindSelectedOrFirstChart(sheet, GetSelectedChartIdOnCurrentSheet())!;

        if (chart is not null)
            return true;

        _messageService.ShowInfo(UiText.Get("MainWindowMessage_ChartSelectBeforeCommand"), caption);
        return false;
    }

    private void RefreshChartContextualTabs()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        SetChartContextualTabsVisible(ChartWorkflowTargetPlanner.HasSelectedChart(sheet, GetSelectedChartIdOnCurrentSheet()));
    }

    private void SetChartContextualTabsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (ChartDesignTab is not null)
            ChartDesignTab.Visibility = visibility;
        if (ChartFormatTab is not null)
            ChartFormatTab.Visibility = visibility;

        if (!visible &&
            RibbonTabs is not null &&
            (ReferenceEquals(RibbonTabs.SelectedItem, ChartDesignTab) ||
             ReferenceEquals(RibbonTabs.SelectedItem, ChartFormatTab)))
        {
            RibbonTabs.SelectedIndex = 1;
        }

        InvalidateVisibleKeyTipElementCache();
    }

    private void ChartColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Column);
    private void ChartStackedColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.StackedColumn);
    private void ChartPercentStackedColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.PercentStackedColumn);
    private void ChartLineMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType(ChartType.Line);
    private void Chart3DLineMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDLine);
    private void ChartPieMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType(ChartType.Pie);
    private void Chart3DPieMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDPie);
    private void ChartDoughnutMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Doughnut);
    private void ChartBarMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType(ChartType.Bar);
    private void ChartStackedBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.StackedBar);
    private void ChartPercentStackedBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.PercentStackedBar);
    private void ChartAreaMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType(ChartType.Area);
    private void Chart3DAreaMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDArea);
    private void ChartScatterMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Scatter);
    private void ChartBubbleMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Bubble);
    private void ChartRadarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Radar);
    private void ChartStockMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Stock);
    private void ChartSurfaceMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Surface);
    private void Chart3DSurfaceMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDSurface);
    private void Chart3DColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDColumn);
    private void Chart3DBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDBar);
    private void ChartTreemapMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Treemap);
    private void ChartSunburstMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Sunburst);
    private void ChartHistogramMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Histogram);
    private void ChartParetoMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Pareto);
    private void ChartBoxAndWhiskerMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.BoxAndWhisker);
    private void ChartWaterfallMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Waterfall);
    private void ChartFunnelMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Funnel);
    private void DeferredChartFamilyMenuItem_Click(object sender, RoutedEventArgs e) => ShowDeferredChartFamilyMessage();

    private void ShowDeferredChartFamilyMessage() =>
        _messageService.ShowInfo(
            UiText.Get("MainWindowMessage_ChartFamilyDeferred"),
            UiText.Get("MainWindowMessage_ChartFamilyDeferredTitle"));

    private void ChartFirstSliceAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "First Slice Angle",
            UiText.Get("MainWindowMessage_ChartSelectPieDoughnutForFirstSliceAngle"),
            ChartQuickCommand.FirstSliceAngle,
            UiText.Get("MainWindowMessage_ChartFirstSliceAngleUnsupported"));
    }

    private void ChartDoughnutHoleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Doughnut Hole Size",
            UiText.Get("MainWindowMessage_ChartSelectDoughnutForHoleSize"),
            ChartQuickCommand.DoughnutHoleSize,
            UiText.Get("MainWindowMessage_ChartDoughnutHoleSizeUnsupported"));
    }

    private void ChartExplodedSliceBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Explode Slice",
            UiText.Get("MainWindowMessage_ChartSelectPieDoughnutForExplode"),
            ChartQuickCommand.ExplodedSlice,
            UiText.Get("MainWindowMessage_ChartExplodedSliceUnsupported"));
    }

    private void ChartBarFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog(
                "Format Bar/Column",
                UiText.Get("MainWindowMessage_ChartSelectBarColumnForGapWidth"),
                out var chart))
            return;

        if (!ChartTypeSupport.SupportsBarGapWidth(chart.Type))
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_ChartGapWidthUnsupported"),
                UiText.Get("MainWindowMessage_FormatBarColumnTitle"));
            return;
        }

        var dialog = new ChartBarFormatDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Bar/Column", chart, dialog.Result.ToOptions());
    }

    private void ChartBubbleFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog(
                "Format Bubble Chart",
                UiText.Get("MainWindowMessage_ChartSelectBubbleForOptions"),
                out var chart))
            return;

        if (chart.Type != ChartType.Bubble)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_ChartBubbleOptionsUnsupported"),
                UiText.Get("MainWindowMessage_FormatBubbleChartTitle"));
            return;
        }

        var dialog = new ChartBubbleFormatDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Bubble Chart", chart, dialog.Result.ToOptions());
    }

    private void ChartPieFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog(
                "Format Pie/Doughnut",
                UiText.Get("MainWindowMessage_ChartSelectPieDoughnutForOptions"),
                out var chart))
            return;

        if (!ChartTypeSupport.SupportsFirstSliceAngle(chart.Type))
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_ChartPieOptionsUnsupported"),
                UiText.Get("MainWindowMessage_FormatPieDoughnutTitle"));
            return;
        }

        var dialog = new ChartPieFormatDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Pie/Doughnut", chart, dialog.Result.ToOptions());
    }

    private void ChartStockFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog(
                "Format Stock Chart",
                UiText.Get("MainWindowMessage_ChartSelectStockForOptions"),
                out var chart))
            return;

        if (chart.Type != ChartType.Stock)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_ChartStockOptionsUnsupported"),
                UiText.Get("MainWindowMessage_FormatStockChartTitle"));
            return;
        }

        var dialog = new ChartStockFormatDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Stock Chart", chart, dialog.Result.ToOptions());
    }

    private void ChartDataLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartDataLabelsDialog();
    }

    private void ShowChartDataLabelsDialog()
    {
        if (!TryGetFirstChartForDialog("Format Data Labels", UiText.Get("MainWindowMessage_ChartSelectForDataLabels"), out var chart))
            return;

        var dialog = new ChartDataLabelsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Data Labels", chart, dialog.Result.ToOptions());
    }

    private void ChartDataLabelPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartDataLabelsDialog();
    }

    private void ChartDataLabelCategoryBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Category Name",
            UiText.Get("MainWindowMessage_ChartSelectForDataLabelOptions"),
            ChartQuickCommand.DataLabelCategoryName);
    }

    private void ChartDataLabelSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Series Name",
            UiText.Get("MainWindowMessage_ChartSelectForDataLabelOptions"),
            ChartQuickCommand.DataLabelSeriesName);
    }

    private void ChartDataLabelPercentageBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Percentage",
            UiText.Get("MainWindowMessage_ChartSelectForDataLabelOptions"),
            ChartQuickCommand.DataLabelPercentage);
    }

    private void ChartDataLabelSeparatorBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Label Separator",
            UiText.Get("MainWindowMessage_ChartSelectForDataLabelOptions"),
            ChartQuickCommand.DataLabelSeparator);
    }

    private void ChartDataLabelNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Label Number Format",
            UiText.Get("MainWindowMessage_ChartSelectForDataLabelOptions"),
            ChartQuickCommand.DataLabelNumberFormat);
    }

    private void ChartDataLabelCalloutBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Data Callout",
            UiText.Get("MainWindowMessage_ChartSelectForDataLabelOptions"),
            ChartQuickCommand.DataLabelCallout);
    }

    private void ChartDataLabelFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Data Label Fill",
            UiText.Get("MainWindowMessage_ChartSelectForDataLabelOptions"),
            ChartQuickCommand.DataLabelFill);
    }

    private void ChartDataLabelTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Data Label Text",
            UiText.Get("MainWindowMessage_ChartSelectForDataLabelOptions"),
            ChartQuickCommand.DataLabelTextColor);
    }

    private void ChartDataLabelBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Data Label Border",
            UiText.Get("MainWindowMessage_ChartSelectForDataLabelOptions"),
            ChartQuickCommand.DataLabelBorder);
    }

    private void ChartDataLabelSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Data Label Size",
            UiText.Get("MainWindowMessage_ChartSelectForDataLabelOptions"),
            ChartQuickCommand.DataLabelFontSize);
    }

    private void ChartDataLabelAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Data Label Angle",
            UiText.Get("MainWindowMessage_ChartSelectForDataLabelOptions"),
            ChartQuickCommand.DataLabelAngle);
    }

    private void ChartPointDataLabelBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Format Data Point Label",
            UiText.Get("MainWindowMessage_ChartSelectForPointDataLabel"),
            ChartQuickCommand.PointDataLabel,
            UiText.Get("MainWindowMessage_ChartPointDataLabelNeedsDataPoints"));
    }

    private void ChartAreaFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Chart Area Fill",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.ChartAreaFill);
    }

    private void ChartTitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Chart Title Color",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.ChartTitleColor);
    }

    private void ChartTitlesBtn_Click(object sender, RoutedEventArgs e)
    {
        const string caption = "Chart Titles";
        if (!TryGetFirstChartForDialog(caption, UiText.Get("MainWindowMessage_ChartSelectForTitles"), out var chart))
            return;

        var dialog = new ChartTitlesDialog(chart.Title, chart.XAxisTitle, chart.YAxisTitle) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions());
    }

    private void ChartTitleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Chart Title Size",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.ChartTitleFontSize);
    }

    private void ChartAxisTitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Axis Title Color",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.AxisTitleColor);
    }

    private void ChartAxisTitleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Axis Title Size",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.AxisTitleFontSize);
    }

    private void ChartPlotAreaFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Plot Area Fill",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.PlotAreaFill);
    }

    private void ChartPlotAreaBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Plot Area Border",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.PlotAreaBorder);
    }

    private void ChartLegendTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Legend Text",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.LegendTextColor);
    }

    private void ChartLegendFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Legend Fill",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.LegendFill);
    }

    private void ChartLegendBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Legend Border",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.LegendBorder);
    }

    private void ChartLegendSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Legend Font Size",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.LegendFontSize);
    }

    private void ChartLegendOverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Legend Overlay",
            UiText.Get("MainWindowMessage_ChartSelectForChartAreaFormatting"),
            ChartQuickCommand.LegendOverlay);
    }

    private void ChartTrendlineBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartTrendlineDialog();
    }

    private void ShowChartTrendlineDialog()
    {
        if (!TryGetFirstChartForDialog("Format Trendline", UiText.Get("MainWindowMessage_ChartSelectForTrendlines"), out var chart))
            return;

        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            ShowCommandError(new CommandOutcome(false, UiText.Get("MainWindowMessage_ChartTrendlinesSupportedTypes")), "Format Trendline");
            return;
        }

        var dialog = new ChartTrendlineOptionsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Trendline", chart, dialog.Result.ToOptions());
    }

    private void ChartTrendlineTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartTrendlineDialog();
    }

    private void ChartTrendlinePeriodBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Moving Average Period",
            UiText.Get("MainWindowMessage_ChartSelectForMovingAveragePeriod"),
            ChartQuickCommand.TrendlineMovingAveragePeriod,
            UiText.Get("MainWindowMessage_ChartTrendlinesSupportedTypes"));
    }

    private void ChartTrendlineOrderBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Polynomial Order",
            UiText.Get("MainWindowMessage_ChartSelectForPolynomialOrder"),
            ChartQuickCommand.TrendlinePolynomialOrder,
            UiText.Get("MainWindowMessage_ChartTrendlinesSupportedTypes"));
    }

    private void ChartTrendlineEquationBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Trendline Equation",
            UiText.Get("MainWindowMessage_ChartSelectForTrendlineInformation"),
            ChartQuickCommand.TrendlineEquation,
            UiText.Get("MainWindowMessage_ChartTrendlineInformationSupportedTypes"));
    }

    private void ChartTrendlineRSquaredBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "R-squared",
            UiText.Get("MainWindowMessage_ChartSelectForTrendlineInformation"),
            ChartQuickCommand.TrendlineRSquared,
            UiText.Get("MainWindowMessage_ChartTrendlineInformationSupportedTypes"));
    }

    private void ChartTrendlineColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Trendline Color",
            UiText.Get("MainWindowMessage_ChartSelectForTrendlineInformation"),
            ChartQuickCommand.TrendlineColor,
            UiText.Get("MainWindowMessage_ChartTrendlineInformationSupportedTypes"));
    }

    private void ChartTrendlineDashBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Trendline Dash",
            UiText.Get("MainWindowMessage_ChartSelectForTrendlineInformation"),
            ChartQuickCommand.TrendlineDash,
            UiText.Get("MainWindowMessage_ChartTrendlineInformationSupportedTypes"));
    }

    private void ChartTrendlineWidthBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Trendline Width",
            UiText.Get("MainWindowMessage_ChartSelectForTrendlineInformation"),
            ChartQuickCommand.TrendlineThickness,
            UiText.Get("MainWindowMessage_ChartTrendlineInformationSupportedTypes"));
    }

    private void ChartErrorBarsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFirstChartForDialog("Format Error Bars", UiText.Get("MainWindowMessage_ChartSelectForErrorBars"), out var chart))
            return;

        var dialog = new ChartErrorBarsDialog(chart) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!ApplyChartLayoutDialogResult("Format Error Bars", chart, dialog.Result.ToOptions()))
            return;

        UpdateViewport();
    }

    private void ChartSecondaryAxisSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Secondary Axis Series",
            UiText.Get("MainWindowMessage_ChartSelectForSecondaryAxisSeries"),
            ChartQuickCommand.SecondaryAxisSeries,
            UiText.Get("MainWindowMessage_ChartSecondaryAxisUnsupported"));
    }

    private void ChartComboBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Combo Chart",
            UiText.Get("MainWindowMessage_ChartSelectForComboOptions"),
            ChartQuickCommand.ComboToggle,
            UiText.Get("MainWindowMessage_ChartComboUnsupported"));
    }

    private void ChartComboSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Combo Chart Series",
            UiText.Get("MainWindowMessage_ChartSelectForComboSeries"),
            ChartQuickCommand.ComboSeries,
            UiText.Get("MainWindowMessage_ChartComboUnsupported"));
    }

    private void ChartSeriesColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartSeriesFormatDialog();
    }

    private void ChartSeriesWidthBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Series Width",
            UiText.Get("MainWindowMessage_ChartSelectForSeriesFormatting"),
            ChartQuickCommand.SeriesWidth,
            UiText.Get("MainWindowMessage_ChartSeriesFormattingNeedsDataSeries"));
    }

    private void ChartSeriesDashBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Series Dash",
            UiText.Get("MainWindowMessage_ChartSelectForSeriesFormatting"),
            ChartQuickCommand.SeriesDash,
            UiText.Get("MainWindowMessage_ChartSeriesFormattingNeedsDataSeries"));
    }

    private void ChartSeriesMarkerBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowChartSeriesFormatDialog();
    }

    private void ShowChartSeriesFormatDialog()
    {
        if (!TryGetFirstChartForDialog("Format Data Series", UiText.Get("MainWindowMessage_ChartSelectForSeriesFormatting"), out var chart))
            return;

        var seriesCount = ChartSeriesFormatPlanner.GetSeriesCount(chart);
        if (seriesCount <= 0)
        {
            ShowCommandError(new CommandOutcome(false, UiText.Get("MainWindowMessage_ChartSeriesFormattingNeedsDataSeries")), "Format Data Series");
            return;
        }

        var dialog = new ChartSeriesFormatDialog(chart, seriesCount) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        ApplyChartLayoutDialogResult("Format Data Series", chart, dialog.Result.ToOptions(chart));
    }

    private void ChartSeriesMarkerSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteChartQuickCommand(
            "Marker Size",
            UiText.Get("MainWindowMessage_ChartSelectForSeriesFormatting"),
            ChartQuickCommand.SeriesMarkerSize,
            UiText.Get("MainWindowMessage_ChartSeriesMarkersSupportedTypes"));
    }

    private void ExecuteChartQuickCommand(
        string caption,
        string missingMessage,
        ChartQuickCommand command,
        string? unsupportedMessage = null)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                missingMessage,
                chart => ChartQuickCommandPlanner.CanApply(chart, command),
                unsupportedMessage,
                chart => ChartQuickCommandPlanner.Plan(chart, command)))
            return;

        UpdateViewport();
    }

    private void InsertChartOfType(string type)
    {
        InsertChartOfType(ChartOptionCycler.ParseChartType(type));
    }

}
