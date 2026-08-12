using System.Collections.Generic;
using FreeX.App.Presentation.Charts.Editing;
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
            ["About FreeX#AboutBtn_Click"] = () => RunGuarded(ShowAboutDialogAsync),
            ["Help Online#HelpOnlineBtn_Click"] = () => RunGuarded(() => OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, UiText.Get("MainWindow_Content_HelpOnline"))),
            ["Feedback#FeedbackBtn_Click"] = () => RunGuarded(() => OpenExternalHelpLinkAsync(AppHelpInfo.FeedbackUrl, UiText.Get("MainWindow_Content_Feedback"))),
            ["Check for Updates#CheckForUpdatesBtn_Click"] = () => RunGuarded(() => OpenExternalHelpLinkAsync(AppHelpInfo.LatestReleaseUrl, UiText.Get("MainWindow_Content_CheckForUpdates"))),
            ["Copy Diagnostics#CopyDiagnosticsBtn_Click"] = () => RunGuarded(CopyDiagnosticsToClipboardAsync),
            ["Legal Notices#LegalNoticesBtn_Click"] = () => RunGuarded(ShowLegalNoticesDialogAsync),

            // --- Chart Design (chart.selected) — real handlers via SetChartLayoutCommand /
            // ChangeChartTypeCommand / ChangeChartSourceCommand / SetChartStyleCommand (MainWindow.ChartTabs). ---
            ["Chart Titles"] = () => RunGuarded(ShowChartTitlesDialog),
            // The Data Labels button opens the full show/hide + position + which-values dialog
            // (ChartDataLabelsPlanner); Data Label Position keeps its quick cycle.
            ["Data Labels"] = () => RunGuarded(ShowChartDataLabelsDialog),
            ["Data Label Position"] = CycleChartDataLabelPosition,
            // The Trendline button opens the type/period/order + equation/R-squared dialog
            // (ChartTrendlinePlanner).
            ["Trendline"] = () => RunGuarded(ShowChartTrendlineDialog),
            // The Error Bars button opens the show/kind/direction + amount/end-caps dialog
            // (ChartErrorBarsPlanner).
            ["Error Bars"] = () => RunGuarded(ShowChartErrorBarsDialog),
            ["Secondary Axis"] = CycleChartSecondaryAxis,
            ["Secondary Axis Series"] = CycleChartSecondaryAxisSeries,
            ["Chart Styles"] = CycleChartStyle,
            ["Select Data Source"] = () => RunGuarded(ShowSelectChartDataDialog),
            ["Change Chart Type#ChangeChartTypeBtn_Click"] = () => RunGuarded(ShowChangeChartTypeDialog),
            // WPF's Combo Chart button is an immediate shared ComboToggle mutation. Keep the full
            // per-series planner dialog available to parity capture, but route the ribbon command
            // through the same quick-command path as WPF so existing combo charts can be toggled off
            // even when the dialog support gate cannot reopen them.
            ["Combo Chart"] = CycleChartCombo,
            // Combo Chart Series is the quick per-click per-series toggle, matching WPF.
            ["Combo Chart Series"] = CycleChartComboSeries,
            ["Move Chart"] = () => RunGuarded(ShowMoveChartDialog),

            // --- Chart Format (chart.selected) — real handlers via SetChartLayoutCommand. ---
            ["Chart Area Fill"] = () => RunGuarded(ShowChartShapeFillDialog),
            ["Plot Area Fill"] = () => RunGuarded(ShowChartPlotAreaFillDialog),
            ["Plot Area Border"] = () => RunGuarded(ShowChartShapeOutlineDialog),
            // WPF routes both Series Color and Series Marker through the full per-series
            // fill/line/marker dialog. Keep the Avalonia route on the same shared planner-backed
            // dialog so Series Color can also edit the selected series, dash, marker, and size.
            ["Series Color"] = () => RunGuarded(ShowChartSeriesFormatDialog),
            // The Series Width button also opens the full per-series fill/line/marker dialog.
            ["Series Width"] = () => RunGuarded(ShowChartSeriesFormatDialog),
            // The Legend button opens the show/hide + position options dialog (ChartLegendPlanner).
            ["Legend Text"] = () => RunGuarded(ShowChartLegendDialog),
            // The Axis Bounds buttons open the per-axis min/max/format/gridlines dialog (ChartAxisPlanner);
            // the Gridlines buttons keep their quick cycle.
            ["X Axis Bounds"] = () => RunGuarded(ShowChartXAxisFormatDialog),
            ["Y Axis Bounds"] = () => RunGuarded(ShowChartYAxisFormatDialog),
            ["X Axis Gridlines"] = CycleChartXAxisGridlines,
            ["Y Axis Gridlines"] = CycleChartYAxisGridlines,
            ["X Axis Labels"] = ToggleChartXAxisLabels,
            ["Y Axis Labels"] = ToggleChartYAxisLabels,
            // Legacy WPF axis quick commands are surfaced by the host-specific axis group as well as
            // the shared bounds dialog. They all use the shared planner and SetChartLayoutCommand path.
            ["X Axis Ticks"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.TickMarks(true), "ChartLoc_NoAxes"),
            ["Y Axis Ticks"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.TickMarks(false), "ChartLoc_NoAxes"),
            ["X Axis Label Font"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.LabelFont(true), "ChartLoc_NoAxes"),
            ["Y Axis Label Font"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.LabelFont(false), "ChartLoc_NoAxes"),
            ["X Axis Label Angle"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.LabelAngle(true), "ChartLoc_NoAxes"),
            ["Y Axis Label Angle"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.LabelAngle(false), "ChartLoc_NoAxes"),
            ["X Axis Line"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.AxisLine(true), "ChartLoc_NoAxes"),
            ["Y Axis Line"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.AxisLine(false), "ChartLoc_NoAxes"),
            ["X Axis Number Format"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.NumberFormat(true), "ChartLoc_NoAxes"),
            ["Y Axis Number Format"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.NumberFormat(false), "ChartLoc_NoAxes"),
            ["X Gridline Style"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.GridlineStyle(true), "ChartLoc_NoAxes"),
            ["Y Gridline Style"] = () => ExecuteChartAxisQuickCommand(ChartAxisWorkflowCommandCatalog.GridlineStyle(false), "ChartLoc_NoAxes"),
            ["X Log Scale"] = () => ExecuteChartAxisPlannedCommand(ChartAxisWorkflowCommandCatalog.LogScale(true), ChartAxisPlanner.PlanLogScaleToggle),
            ["Y Log Scale"] = () => ExecuteChartAxisPlannedCommand(ChartAxisWorkflowCommandCatalog.LogScale(false), ChartAxisPlanner.PlanLogScaleToggle),
            // The Format Chart Area button opens the chart-area / plot-area fill + border dialog
            // (ChartAreaFormatPlanner -> SetChartLayoutCommand).
            ["Format Chart Area"] = () => RunGuarded(ShowFormatChartAreaDialog),
            // Current Selection ▸ Format: the type-specific format dialogs (each guarded to its chart family).
            ["Format Bar/Column"] = () => RunGuarded(ShowChartBarFormatDialog),
            ["Format Pie/Doughnut"] = () => RunGuarded(ShowChartPieFormatDialog),
            ["Format Bubble Chart"] = () => RunGuarded(ShowChartBubbleFormatDialog),
            ["Format Stock Chart"] = () => RunGuarded(ShowChartStockFormatDialog),
            // Shape Styles ▸ Series Dash / Marker Size quick cycles; Series Marker opens the full series dialog
            // (same ChartSeriesFormatPlanner dialog as Series Width).
            ["Series Dash"] = CycleChartSeriesDash,
            ["Series Marker"] = () => RunGuarded(ShowChartSeriesFormatDialog),
            ["Marker Size"] = CycleChartMarkerSize,
            // Text group quick cycles: title/axis-title color & size, legend font size, data-label text/fill/border.
            ["Chart Title Color"] = CycleChartTitleColor,
            ["Chart Title Size"] = CycleChartTitleSize,
            ["Axis Title Color"] = CycleChartAxisTitleColor,
            ["Axis Title Size"] = CycleChartAxisTitleSize,
            ["Legend Font Size"] = CycleChartLegendFontSize,
            ["Data Label Text"] = CycleChartDataLabelText,
            ["Data Label Fill"] = CycleChartDataLabelFill,
            ["Data Label Border"] = CycleChartDataLabelBorder,

            // --- Table Design (table.active) — real handlers via the structured-table Core commands
            // (MainWindow.TableDesignTab). ---
            ["Total Row"] = ToggleActiveTableTotalRow,
            ["First Column"] = ToggleActiveTableFirstColumn,
            ["Last Column"] = ToggleActiveTableLastColumn,
            ["Banded Rows#TableDesignBandedRowsBtn_Click"] = ToggleActiveTableBandedRows,
            ["Banded Columns#TableDesignBandedColumnsBtn_Click"] = ToggleActiveTableBandedColumns,
            ["Filter Button"] = ToggleActiveTableFilterButton,
            ["Convert to Range"] = ConvertActiveTableToRange,
            ["Remove Duplicates#TableDesignRemoveDuplicatesBtn_Click"] = () => RunGuarded(ShowRemoveDuplicatesDialogAsync),
            // Table Name dialog — validates/renames the active table via TableNamePlanner +
            // RenameStructuredTableCommand (MainWindow.TableName).
            ["Table Name"] = OpenTableName,
            // Resize Table dialog — validates/resolves a new data range via TableResizePlanner and applies it
            // through ResizeStructuredTableCommand (+ style reapply) (MainWindow.TableResize).
            ["Resize Table"] = OpenTableResize,
            // Table Styles gallery — picks a built-in style via TableStyleGalleryPlanner and applies it through
            // ApplyStructuredTableStyleCommand (MainWindow.TableStyleGallery).
            ["Table Styles"] = OpenTableStyleGallery,
            // Summarize with PivotTable — opens the Insert PivotTable dialog seeded from the active table's range
            // (MainWindow.TableSummarizeWithPivot), reusing the existing PivotCreatePlanner path.
            ["Summarize with PivotTable"] = SummarizeActiveTableWithPivot,

            // --- PivotTable Analyze / Design (pivot.active) — real handlers via
            // ConfigurePivotTableOptionsCommand / RefreshPivotTableCommand (MainWindow.PivotTabs). ---
            ["Refresh"] = RefreshActivePivotTable,
            ["Insert Slicer"] = InsertSlicerForActivePivot,
            ["Insert Timeline"] = InsertTimelineForActivePivot,
            ["Field List"] = TogglePivotFieldList,
            ["Field Headers"] = TogglePivotFieldHeaders,
            ["Grand Totals"] = TogglePivotGrandTotals,
            ["Subtotals"] = TogglePivotSubtotals,
            ["Report Layout"] = CyclePivotReportLayout,
            ["Blank Rows"] = TogglePivotBlankRows,
            ["Banded Rows#PivotBandedRowsBtn_Click"] = TogglePivotBandedRows,
            ["Banded Columns#PivotBandedColumnsBtn_Click"] = TogglePivotBandedColumns,
            ["Row Headers"] = TogglePivotRowHeaders,
            ["Column Headers"] = TogglePivotColumnHeaders,
            // PivotTable Options dialog — totals & layout-display options via ConfigurePivotTableOptionsCommand
            // (MainWindow.PivotOptions).
            ["PivotTable Options"] = OpenPivotTableOptions,
            // Change Data Source dialog — validates/resolves a new source range via PivotDataSourcePlanner and
            // applies it through ChangePivotTableSourceCommand (MainWindow.PivotDataSource).
            ["Change Data Source"] = OpenPivotDataSource,
            // PivotTable Styles gallery — picks a built-in style via PivotStyleGalleryPlanner and applies it
            // through ConfigurePivotTableOptionsCommand (MainWindow.PivotStyleGallery).
            ["PivotTable Styles"] = OpenPivotStyleGallery,
            // PivotTable Name dialog — renames the active pivot via PivotNamePlanner + RenamePivotTableCommand
            // (MainWindow.PivotName).
            ["PivotTable Name"] = OpenPivotName,
            // Group Field / Ungroup dialogs — date/number-range grouping via PivotGroupFieldPlanner, applied
            // through ConfigurePivotTableCalculatedItemsCommand (MainWindow.PivotGroupField).
            ["Group Field"] = OpenPivotGroupField,
            ["Ungroup#PivotUngroupFieldBtn_Click"] = UngroupPivotField,
            // Calculated Field dialog — add/modify/delete a calculated field via PivotCalculatedFieldPlanner,
            // applied through ConfigurePivotTableCalculatedItemsCommand (MainWindow.PivotCalculatedField).
            ["Calculated Field"] = OpenPivotCalculatedField,
            // Field Settings opens the value-field-settings dialog (MainWindow.PivotFieldSettings) for the
            // active pivot's first value field, reusing the same PivotValueFieldPlanner the header dropdown uses.
            ["Field Settings"] = OpenActivePivotFieldSettings,
            // Show Details drills the selected value cell into a new detail sheet via DrillDownPivotTableCommand.
            ["Show Details"] = ShowActivePivotDetails,
            // Clear empties the active pivot's layout via ClearPivotTableViewCommand.
            ["Clear#PivotTableClearBtn_Click"] = ClearActivePivotTable,
            // Select moves the selection onto the active pivot's full target range.
            ["Select"] = SelectActivePivotTable,
            // Move PivotTable opens the destination dialog (MainWindow.PivotMove) -> MovePivotTableCommand.
            ["Move PivotTable"] = OpenPivotMove,
            // Calculated Item opens the add/modify/delete dialog (MainWindow.PivotCalculatedItem) ->
            // ConfigurePivotTableCalculatedItemsCommand.
            ["Calculated Item"] = OpenPivotCalculatedItem,
            // +/- Buttons toggles PivotTableModel.ShowExpandCollapseButtons via ConfigurePivotTableOptionsCommand.
            ["+/- Buttons"] = TogglePivotExpandCollapseButtons,
            // PivotChart inserts a PivotChart over the active pivot (MainWindow.PivotChart).
            ["PivotChart"] = InsertPivotChart,
            // Change Chart Type re-types the active pivot's chart (MainWindow.PivotChartCommands).
            ["Change Chart Type#PivotChartChangeTypeBtn_Click"] = () => RunGuarded(ChangeActivePivotChartTypeAsync),
            // PivotChart Options opens the field-button / data-table options dialog (MainWindow.PivotChartOptions).
            ["PivotChart Options"] = () => RunGuarded(OpenPivotChartOptionsAsync),

            // Shape Effects is a dropdown: clicking the parent opens its menu (No Effect / Shadow, wired via
            // BuildPictureShapeTabCommands). Register the parent id too so the renderer keeps it enabled
            // rather than disabling it for an unregistered command.
            ["Shape Effects"] = () => RunGuarded(OpenShapeEffectsDialogAsync),
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
