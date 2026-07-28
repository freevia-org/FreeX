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
            // --- Help tab (always visible). ---
            ["help.about"] = () => RunGuarded(ShowAboutDialogAsync),
            ["help.helpOnline"] = () => RunGuarded(() => OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, "Help Online")),
            ["help.feedback"] = () => RunGuarded(() => OpenExternalHelpLinkAsync(AppHelpInfo.FeedbackUrl, "Send Feedback")),
            ["help.checkUpdates"] = () => RunGuarded(() => OpenExternalHelpLinkAsync(AppHelpInfo.LatestReleaseUrl, "Check for Updates")),
            ["help.copyDiagnostics"] = () => RunGuarded(CopyDiagnosticsToClipboardAsync),
            ["help.legalNotices"] = () => RunGuarded(ShowLegalNoticesDialogAsync),

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
            // The Error Bars button opens the show/kind/direction + amount/end-caps dialog
            // (ChartErrorBarsPlanner).
            ["chartDesign.errorBars"] = () => RunGuarded(ShowChartErrorBarsDialog),
            ["chartDesign.secondaryAxis"] = CycleChartSecondaryAxis,
            ["chartDesign.secondaryAxisSeries"] = CycleChartSecondaryAxisSeries,
            ["chartDesign.chartStyles"] = CycleChartStyle,
            ["chartDesign.selectData"] = () => RunGuarded(ShowSelectChartDataDialog),
            ["chartDesign.changeType"] = () => RunGuarded(ShowChangeChartTypeDialog),
            // WPF's Combo Chart button is an immediate shared ComboToggle mutation. Keep the full
            // per-series planner dialog available to parity capture, but route the ribbon command
            // through the same quick-command path as WPF so existing combo charts can be toggled off
            // even when the dialog support gate cannot reopen them.
            ["chartDesign.comboChart"] = CycleChartCombo,
            // Combo Chart Series is the quick per-click per-series toggle, matching WPF.
            ["chartDesign.comboChartSeries"] = CycleChartComboSeries,
            ["chartDesign.moveChart"] = () => RunGuarded(ShowMoveChartDialog),

            // --- Chart Format (chart.selected) — real handlers via SetChartLayoutCommand. ---
            ["chartFormat.chartAreaFill"] = () => RunGuarded(ShowChartShapeFillDialog),
            ["chartFormat.plotAreaFill"] = () => RunGuarded(ShowChartPlotAreaFillDialog),
            ["chartFormat.plotAreaBorder"] = () => RunGuarded(ShowChartShapeOutlineDialog),
            // WPF routes both Series Color and Series Marker through the full per-series
            // fill/line/marker dialog. Keep the Avalonia route on the same shared planner-backed
            // dialog so Series Color can also edit the selected series, dash, marker, and size.
            ["chartFormat.seriesColor"] = () => RunGuarded(ShowChartSeriesFormatDialog),
            // The Series Width button also opens the full per-series fill/line/marker dialog.
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
            // The Format Chart Area button opens the chart-area / plot-area fill + border dialog
            // (ChartAreaFormatPlanner -> SetChartLayoutCommand).
            ["chartFormat.formatChartArea"] = () => RunGuarded(ShowFormatChartAreaDialog),
            // Current Selection ▸ Format: the type-specific format dialogs (each guarded to its chart family).
            ["chartFormat.formatBarColumn"] = () => RunGuarded(ShowChartBarFormatDialog),
            ["chartFormat.formatPieDoughnut"] = () => RunGuarded(ShowChartPieFormatDialog),
            ["chartFormat.formatBubble"] = () => RunGuarded(ShowChartBubbleFormatDialog),
            ["chartFormat.formatStock"] = () => RunGuarded(ShowChartStockFormatDialog),
            // Shape Styles ▸ Series Dash / Marker Size quick cycles; Series Marker opens the full series dialog
            // (same ChartSeriesFormatPlanner dialog as Series Width).
            ["chartFormat.seriesDash"] = CycleChartSeriesDash,
            ["chartFormat.seriesMarker"] = () => RunGuarded(ShowChartSeriesFormatDialog),
            ["chartFormat.markerSize"] = CycleChartMarkerSize,
            // Text group quick cycles: title/axis-title color & size, legend font size, data-label text/fill/border.
            ["chartFormat.chartTitleColor"] = CycleChartTitleColor,
            ["chartFormat.chartTitleSize"] = CycleChartTitleSize,
            ["chartFormat.axisTitleColor"] = CycleChartAxisTitleColor,
            ["chartFormat.axisTitleSize"] = CycleChartAxisTitleSize,
            ["chartFormat.legendFontSize"] = CycleChartLegendFontSize,
            ["chartFormat.dataLabelText"] = CycleChartDataLabelText,
            ["chartFormat.dataLabelFill"] = CycleChartDataLabelFill,
            ["chartFormat.dataLabelBorder"] = CycleChartDataLabelBorder,

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
            // Table Name dialog — validates/renames the active table via TableNamePlanner +
            // RenameStructuredTableCommand (MainWindow.TableName).
            ["tableDesign.tableName"] = OpenTableName,
            // Resize Table dialog — validates/resolves a new data range via TableResizePlanner and applies it
            // through ResizeStructuredTableCommand (+ style reapply) (MainWindow.TableResize).
            ["tableDesign.resize"] = OpenTableResize,
            // Table Styles gallery — picks a built-in style via TableStyleGalleryPlanner and applies it through
            // ApplyStructuredTableStyleCommand (MainWindow.TableStyleGallery).
            ["tableDesign.tableStyles"] = OpenTableStyleGallery,
            // Summarize with PivotTable — opens the Insert PivotTable dialog seeded from the active table's range
            // (MainWindow.TableSummarizeWithPivot), reusing the existing PivotCreatePlanner path.
            ["tableDesign.summarizeWithPivot"] = SummarizeActiveTableWithPivot,

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
            // PivotTable Name dialog — renames the active pivot via PivotNamePlanner + RenamePivotTableCommand
            // (MainWindow.PivotName).
            ["pivotAnalyze.name"] = OpenPivotName,
            // Group Field / Ungroup dialogs — date/number-range grouping via PivotGroupFieldPlanner, applied
            // through ConfigurePivotTableCalculatedItemsCommand (MainWindow.PivotGroupField).
            ["pivotAnalyze.groupField"] = OpenPivotGroupField,
            ["pivotAnalyze.ungroup"] = UngroupPivotField,
            // Calculated Field dialog — add/modify/delete a calculated field via PivotCalculatedFieldPlanner,
            // applied through ConfigurePivotTableCalculatedItemsCommand (MainWindow.PivotCalculatedField).
            ["pivotAnalyze.calculatedField"] = OpenPivotCalculatedField,
            // Field Settings opens the value-field-settings dialog (MainWindow.PivotFieldSettings) for the
            // active pivot's first value field, reusing the same PivotValueFieldPlanner the header dropdown uses.
            ["pivotAnalyze.fieldSettings"] = OpenActivePivotFieldSettings,
            // Show Details drills the selected value cell into a new detail sheet via DrillDownPivotTableCommand.
            ["pivotAnalyze.showDetails"] = ShowActivePivotDetails,
            // Clear empties the active pivot's layout via ClearPivotTableViewCommand.
            ["pivotAnalyze.clear"] = ClearActivePivotTable,
            // Select moves the selection onto the active pivot's full target range.
            ["pivotAnalyze.select"] = SelectActivePivotTable,
            // Move PivotTable opens the destination dialog (MainWindow.PivotMove) -> MovePivotTableCommand.
            ["pivotAnalyze.move"] = OpenPivotMove,
            // Calculated Item opens the add/modify/delete dialog (MainWindow.PivotCalculatedItem) ->
            // ConfigurePivotTableCalculatedItemsCommand.
            ["pivotAnalyze.calculatedItem"] = OpenPivotCalculatedItem,
            // +/- Buttons toggles PivotTableModel.ShowExpandCollapseButtons via ConfigurePivotTableOptionsCommand.
            ["pivotAnalyze.plusMinusButtons"] = TogglePivotExpandCollapseButtons,
            // PivotChart inserts a PivotChart over the active pivot (MainWindow.PivotChart).
            ["pivotAnalyze.pivotChart"] = InsertPivotChart,
            // Change Chart Type re-types the active pivot's chart (MainWindow.PivotChartCommands).
            ["pivotAnalyze.changeChartType"] = () => RunGuarded(ChangeActivePivotChartTypeAsync),
            // PivotChart Options opens the field-button / data-table options dialog (MainWindow.PivotChartOptions).
            ["pivotAnalyze.pivotChartOptions"] = () => RunGuarded(OpenPivotChartOptionsAsync),

            // Shape Effects is a dropdown: clicking the parent opens its menu (No Effect / Shadow, wired via
            // BuildPictureShapeTabCommands). Register the parent id too so the renderer keeps it enabled
            // rather than disabling it for an unregistered command.
            ["shapeFormat.shapeEffects"] = () => RunGuarded(OpenShapeEffectsDialogAsync),
        };

        // Merge the Picture/Shape Format handlers (Arrange / Shape Styles / Accessibility), which also
        // provide the picture & shape z-order/fill/outline/gradient/effect/rotate/size/alt-text commands.
        foreach (var (id, action) in BuildPictureShapeTabCommands())
            dict[id] = action;

        return dict;
    }

    /// <summary>Reports that a contextual-tab command is a Phase-1 shell, on the status bar.</summary>
    private void ReportContextualNotYetAvailable(string commandLabel)
        => RefreshShell(UiText.Format("InsertLoc_NotYetAvailable", commandLabel));

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
            RefreshShell(UiText.Format("InsertLoc_CommandFailed", ex.Message));
        }
    }
}
