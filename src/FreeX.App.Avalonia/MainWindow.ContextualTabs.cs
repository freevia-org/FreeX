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
            ["help.about"] = () => RunGuarded(ShowAboutDialogAsync),
            ["help.helpOnline"] = () => RunGuarded(() => OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, "Help Online")),
            ["help.feedback"] = () => RunGuarded(() => OpenExternalHelpLinkAsync(AppHelpInfo.FeedbackUrl, "Send Feedback")),
            ["help.checkUpdates"] = () => RunGuarded(() => OpenExternalHelpLinkAsync(AppHelpInfo.LatestReleaseUrl, "Check for Updates")),

            // --- Chart Design (chart.selected) — real handlers via SetChartLayoutCommand /
            // ChangeChartTypeCommand / ChangeChartSourceCommand / SetChartStyleCommand (MainWindow.ChartTabs). ---
            ["chartDesign.titles"] = () => RunGuarded(ShowChartTitlesDialog),
            // The Data Labels button opens the full show/hide + position + which-values dialog
            // (ChartDataLabelsPlanner); Data Label Position keeps its quick cycle.
            ["chartDesign.dataLabels"] = () => RunGuarded(ShowChartDataLabelsDialog),
            ["chartDesign.dataLabelPosition"] = CycleChartDataLabelPosition,
            // The Trendline button opens the type/period/order + equation/R-squared dialog
            // (ChartTrendlinePlanner).
            ["chartDesign.trendline"] = () => RunGuarded(ShowChartTrendlineDialog),
            ["chartDesign.errorBars"] = ToggleChartErrorBars,
            ["chartDesign.secondaryAxis"] = CycleChartSecondaryAxis,
            ["chartDesign.chartStyles"] = CycleChartStyle,
            ["chartDesign.selectData"] = () => RunGuarded(ShowSelectChartDataDialog),
            ["chartDesign.changeType"] = () => RunGuarded(ShowChangeChartTypeDialog),
            // No Core support yet (combo overlays, move-chart sheet target dialog) — honest stubs.
            ["chartDesign.comboChart"] = () => ReportChartCommandNotYetAvailable("Combo Chart"),
            ["chartDesign.moveChart"] = () => ReportChartCommandNotYetAvailable("Move Chart"),

            // --- Chart Format (chart.selected) — real handlers via SetChartLayoutCommand. ---
            ["chartFormat.chartAreaFill"] = () => RunGuarded(ShowChartShapeFillDialog),
            ["chartFormat.plotAreaFill"] = () => RunGuarded(ShowChartPlotAreaFillDialog),
            ["chartFormat.plotAreaBorder"] = () => RunGuarded(ShowChartShapeOutlineDialog),
            ["chartFormat.seriesColor"] = () => RunGuarded(ShowChartSeriesColorDialog),
            // The Series Width button opens the full per-series fill/line/marker dialog
            // (ChartSeriesFormatPlanner); Series Color keeps its quick picker.
            ["chartFormat.seriesWidth"] = () => RunGuarded(ShowChartSeriesFormatDialog),
            // The Legend button opens the show/hide + position options dialog (ChartLegendPlanner).
            ["chartFormat.legendText"] = () => RunGuarded(ShowChartLegendDialog),
            // The Axis Bounds buttons open the per-axis min/max/format/gridlines dialog (ChartAxisPlanner);
            // the Gridlines buttons keep their quick cycle.
            ["chartFormat.xAxisBounds"] = () => RunGuarded(ShowChartXAxisFormatDialog),
            ["chartFormat.yAxisBounds"] = () => RunGuarded(ShowChartYAxisFormatDialog),
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
            ["tableDesign.removeDuplicates"] = () => RunGuarded(ShowRemoveDuplicatesDialogAsync),
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
            // PivotTable Options dialog — totals & layout-display options via ConfigurePivotTableOptionsCommand
            // (MainWindow.PivotOptions).
            ["pivotAnalyze.options"] = OpenPivotTableOptions,
            // Change Data Source dialog — validates/resolves a new source range via PivotDataSourcePlanner and
            // applies it through ChangePivotTableSourceCommand (MainWindow.PivotDataSource).
            ["pivotAnalyze.changeDataSource"] = OpenPivotDataSource,
            // PivotTable Styles gallery — picks a built-in style via PivotStyleGalleryPlanner and applies it
            // through ConfigurePivotTableOptionsCommand (MainWindow.PivotStyleGallery).
            ["pivotDesign.pivotStyles"] = OpenPivotStyleGallery,
            // No Core support yet (name dialog, field settings, group/ungroup, calculated field) — honest stubs.
            ["pivotAnalyze.name"] = () => ReportPivotNotYetAvailable("PivotTable Name"),
            ["pivotAnalyze.fieldSettings"] = () => ReportPivotNotYetAvailable("Field Settings"),
            ["pivotAnalyze.groupField"] = () => ReportPivotNotYetAvailable("Group Field"),
            ["pivotAnalyze.ungroup"] = () => ReportPivotNotYetAvailable("Ungroup"),
            ["pivotAnalyze.calculatedField"] = () => ReportPivotNotYetAvailable("Calculated Field"),

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

    /// <summary>
    /// Launches a fire-and-forget async UI handler so a thrown exception is surfaced on the status
    /// bar instead of escaping an async-void handler to the dispatcher (which crashes the app) or
    /// being silently swallowed as an unobserved task exception.
    /// </summary>
    private async void RunGuarded(Func<Task> handler)
    {
        try
        {
            await handler();
        }
        catch (Exception ex)
        {
            RefreshShell($"Command failed: {ex.Message}");
        }
    }
}
