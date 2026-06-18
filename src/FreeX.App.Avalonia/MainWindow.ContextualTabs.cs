using System.Collections.Generic;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

/// <summary>
/// Phase-1 wiring for the Help tab and the contextual ribbon tabs (Chart/Picture/Shape/Table/Pivot).
/// The contextual tabs render as shells on selection (driven by <see cref="AvaloniaRibbonContextSource"/>);
/// most of their commands are honest "not yet available" status reports, and the few tractable ones reuse
/// existing shell handlers. The command ids here mirror those declared in
/// <see cref="Ribbon.AvaloniaRibbonHost"/>'s definition.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Central map of Help/contextual-tab command id → handler, merged into the ribbon's ExtraCommands so
    /// every Phase-1 button does something honest. Real handlers reuse existing shell behavior; the rest
    /// report a clearly-labeled "not yet available" status (no silent no-ops, no invented behavior).
    /// </summary>
    private IReadOnlyDictionary<string, Action> BuildContextualTabCommands()
    {
        var dict = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            // --- Help tab (always visible): About is real; the rest report honestly. ---
            ["help.about"] = () => _ = ShowAboutDialogAsync(),
            ["help.helpOnline"] = () => _ = OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, "Help Online"),
            ["help.feedback"] = () => _ = OpenExternalHelpLinkAsync(AppHelpInfo.FeedbackUrl, "Send Feedback"),
            ["help.checkUpdates"] = () => _ = OpenExternalHelpLinkAsync(AppHelpInfo.LatestReleaseUrl, "Check for Updates"),

            // --- Chart Design (chart.selected) — real handlers via SetChartLayoutCommand /
            // ChangeChartTypeCommand / ChangeChartSourceCommand / SetChartStyleCommand (MainWindow.ChartTabs). ---
            ["chartDesign.titles"] = ShowChartTitlesDialog,
            ["chartDesign.dataLabels"] = ToggleChartDataLabels,
            ["chartDesign.dataLabelPosition"] = CycleChartDataLabelPosition,
            ["chartDesign.trendline"] = ToggleChartTrendline,
            ["chartDesign.errorBars"] = ToggleChartErrorBars,
            ["chartDesign.secondaryAxis"] = CycleChartSecondaryAxis,
            ["chartDesign.chartStyles"] = CycleChartStyle,
            ["chartDesign.selectData"] = ShowSelectChartDataDialog,
            ["chartDesign.changeType"] = ShowChangeChartTypeDialog,
            // No Core support yet (combo overlays, move-chart sheet target dialog) — honest stubs.
            ["chartDesign.comboChart"] = () => ReportChartCommandNotYetAvailable("Combo Chart"),
            ["chartDesign.moveChart"] = () => ReportChartCommandNotYetAvailable("Move Chart"),

            // --- Chart Format (chart.selected) — real handlers via SetChartLayoutCommand. ---
            ["chartFormat.chartAreaFill"] = ShowChartShapeFillDialog,
            ["chartFormat.plotAreaFill"] = ShowChartPlotAreaFillDialog,
            ["chartFormat.plotAreaBorder"] = ShowChartShapeOutlineDialog,
            ["chartFormat.seriesColor"] = ShowChartSeriesColorDialog,
            ["chartFormat.legendText"] = CycleChartLegendTextColor,
            ["chartFormat.xGridlines"] = CycleChartXAxisGridlines,
            ["chartFormat.yGridlines"] = CycleChartYAxisGridlines,
            ["chartFormat.xLabels"] = ToggleChartXAxisLabels,
            ["chartFormat.yLabels"] = ToggleChartYAxisLabels,
            // Type-specific format dialogs have no Core support yet — honest stub.
            ["chartFormat.formatChartArea"] = () => ReportChartCommandNotYetAvailable("Format Chart Area"),

            // --- Table Design (table.active) — real handlers via the structured-table Core commands
            // (MainWindow.TableDesignTab). ---
            ["tableDesign.totalRow"] = ToggleActiveTableTotalRow,
            ["tableDesign.firstColumn"] = ToggleActiveTableFirstColumn,
            ["tableDesign.lastColumn"] = ToggleActiveTableLastColumn,
            ["tableDesign.bandedRows"] = ToggleActiveTableBandedRows,
            ["tableDesign.bandedColumns"] = ToggleActiveTableBandedColumns,
            ["tableDesign.filterButton"] = ToggleActiveTableFilterButton,
            ["tableDesign.convertToRange"] = ConvertActiveTableToRange,
            ["tableDesign.removeDuplicates"] = () => _ = ShowRemoveDuplicatesDialogAsync(),
            // No Core support yet (table name / resize / styles gallery) — honest stubs.
            ["tableDesign.tableName"] = () => ReportContextualNotYetAvailable("Table Name"),
            ["tableDesign.resize"] = () => ReportContextualNotYetAvailable("Resize Table"),
            ["tableDesign.tableStyles"] = () => ReportContextualNotYetAvailable("Table Styles"),

            // --- PivotTable Analyze / Design (pivot.active) — real handlers via
            // ConfigurePivotTableOptionsCommand / RefreshPivotTableCommand (MainWindow.PivotTabs). ---
            ["pivotAnalyze.refresh"] = RefreshActivePivotTable,
            ["pivotAnalyze.insertSlicer"] = InsertSlicerForActivePivot,
            ["pivotAnalyze.insertTimeline"] = InsertTimelineForActivePivot,
            ["pivotAnalyze.fieldList"] = TogglePivotFieldList,
            ["pivotAnalyze.fieldHeaders"] = TogglePivotFieldHeaders,
            ["pivotDesign.grandTotals"] = TogglePivotGrandTotals,
            ["pivotDesign.subtotals"] = TogglePivotSubtotals,
            ["pivotDesign.reportLayout"] = CyclePivotReportLayout,
            ["pivotDesign.blankRows"] = TogglePivotBlankRows,
            ["pivotDesign.bandedRows"] = TogglePivotBandedRows,
            ["pivotDesign.bandedColumns"] = TogglePivotBandedColumns,
            ["pivotDesign.rowHeaders"] = TogglePivotRowHeaders,
            ["pivotDesign.columnHeaders"] = TogglePivotColumnHeaders,
            // No Core support yet (name/options dialog, field settings, group/ungroup, change data source,
            // calculated field, pivot styles gallery) — honest stubs.
            ["pivotAnalyze.name"] = () => ReportPivotNotYetAvailable("PivotTable Name"),
            ["pivotAnalyze.options"] = () => ReportPivotNotYetAvailable("PivotTable Options"),
            ["pivotAnalyze.fieldSettings"] = () => ReportPivotNotYetAvailable("Field Settings"),
            ["pivotAnalyze.groupField"] = () => ReportPivotNotYetAvailable("Group Field"),
            ["pivotAnalyze.ungroup"] = () => ReportPivotNotYetAvailable("Ungroup"),
            ["pivotAnalyze.changeDataSource"] = () => ReportPivotNotYetAvailable("Change Data Source"),
            ["pivotAnalyze.calculatedField"] = () => ReportPivotNotYetAvailable("Calculated Field"),
            ["pivotDesign.pivotStyles"] = () => ReportPivotNotYetAvailable("PivotTable Styles"),

            // Shape Effects is a dropdown: clicking the parent opens its menu (No Effect / Shadow, wired via
            // BuildPictureShapeTabCommands). Register the parent id too so the renderer keeps it enabled
            // rather than disabling it for an unregistered command.
            ["shapeFormat.shapeEffects"] = () => RefreshShell("Choose a shape effect from the menu."),
        };

        // Merge the Picture/Shape Format handlers (Arrange / Shape Styles / Accessibility), which also
        // provide the picture & shape z-order/fill/outline/gradient/effect/rotate/size/alt-text commands.
        foreach (var (id, action) in BuildPictureShapeTabCommands())
            dict[id] = action;

        return dict;
    }

    /// <summary>Reports that a contextual-tab command is a Phase-1 shell, on the status bar.</summary>
    private void ReportContextualNotYetAvailable(string commandLabel)
        => RefreshShell($"{commandLabel} is not yet available.");
}
