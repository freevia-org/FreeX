using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureChartDataLayoutTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteChartDataLayoutTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1280;
        Height = 820;
        await Task.Delay(700);

        var context = EnsureChartDataLayoutTourContext();
        var captures = new List<ChartDataLayoutTourManifestCapture>();
        Window? openDialog = null;

        try
        {
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.ChartContextTabs.Single(tab => tab.Header == "Chart Design"));
            UpdateViewport();
            RefreshToolbar();
            captures.Add(await CaptureChartDataLayoutWindowStateAsync(
                outputDir,
                "UI-CAT-INSERT-002B,UI-CAT-INSERT-002C,UI-CMD-INSERT-016",
                "selected-chart-design-context",
                "Selected embedded chart with Chart Design contextual tab",
                "freex_chart_data_layout_selected_chart_design_context",
                "Seeded embedded chart is visible with Chart Design selected, exposing Chart Layouts, Chart Styles, Data, Type, and Location command groups."));

            SelectRibbonTourTab(RibbonScreenshotTourPlanner.ChartContextTabs.Single(tab => tab.Header == "Format"));
            captures.Add(await CaptureChartDataLayoutWindowStateAsync(
                outputDir,
                "UI-CAT-INSERT-002B,UI-CAT-INSERT-002C,UI-CMD-INSERT-016",
                "selected-chart-format-context",
                "Selected embedded chart with Format contextual tab",
                "freex_chart_data_layout_selected_chart_format_context",
                "The same chart target keeps the Format contextual tab visible with chart area, title, axis, series, trendline, and error-bar command surfaces."));

            openDialog = new SelectDataSourceDialog(
                FormatRangeReference(context.Chart.DataRange.Start, context.Chart.DataRange.End),
                context.Chart.FirstColIsCategories,
                request => { },
                context.Sheet.Id,
                ResolveSheetIdByName)
            {
                Owner = this
            };
            await ShowChartDataLayoutTourDialogAsync(openDialog);
            captures.Add(await CaptureChartDataLayoutDialogAsync(
                openDialog,
                outputDir,
                "UI-CAT-INSERT-002B,UI-CMD-INSERT-016",
                "select-data-dialog",
                "Select Data Source dialog",
                "freex_chart_data_layout_select_data_dialog",
                "Select Data Source shows the current chart range, first-column category choice, switch-row/column option, previewed series, axis labels, Add/Edit/Remove controls, and Hidden and Empty Cells entry point."));
            CloseChartDataLayoutTourDialog(openDialog);
            openDialog = null;

            openDialog = new MoveChartDialog(context.Sheet.Name) { Owner = this };
            await ShowChartDataLayoutTourDialogAsync(openDialog);
            captures.Add(await CaptureChartDataLayoutDialogAsync(
                openDialog,
                outputDir,
                "UI-CAT-INSERT-002B,UI-CMD-INSERT-016",
                "move-chart-dialog",
                "Move Chart dialog",
                "freex_chart_data_layout_move_chart_dialog",
                "Move Chart opens with Object in sheet selected, New chart sheet available, target name editor, and OK/Cancel command row."));
            CloseChartDataLayoutTourDialog(openDialog);
            openDialog = null;

            openDialog = new ChangeChartTypeDialog(context.Chart.Type) { Owner = this };
            await ShowChartDataLayoutTourDialogAsync(openDialog);
            captures.Add(await CaptureChartDataLayoutDialogAsync(
                openDialog,
                outputDir,
                "UI-CAT-INSERT-002B,UI-CMD-INSERT-016",
                "change-chart-type-dialog",
                "Change Chart Type dialog",
                "freex_chart_data_layout_change_chart_type_dialog",
                "Change Chart Type opens to the subtype gallery for the current Column chart, with supported chart families and OK/Cancel controls visible."));
            CloseChartDataLayoutTourDialog(openDialog);
            openDialog = null;

            openDialog = new ChartStyleDialog(context.Chart) { Owner = this };
            await ShowChartDataLayoutTourDialogAsync(openDialog);
            captures.Add(await CaptureChartDataLayoutDialogAsync(
                openDialog,
                outputDir,
                "UI-CAT-INSERT-002B,UI-CMD-INSERT-016",
                "chart-styles-dialog",
                "Chart Styles dialog",
                "freex_chart_data_layout_chart_styles_dialog",
                "Chart Styles dialog displays the style gallery for the active chart, including selected style state and OK/Cancel controls."));
            CloseChartDataLayoutTourDialog(openDialog);
            openDialog = null;

            openDialog = new ChartTitlesDialog(context.Chart.Title, context.Chart.XAxisTitle, context.Chart.YAxisTitle) { Owner = this };
            await ShowChartDataLayoutTourDialogAsync(openDialog);
            captures.Add(await CaptureChartDataLayoutDialogAsync(
                openDialog,
                outputDir,
                "UI-CAT-INSERT-002B,UI-CMD-INSERT-016",
                "chart-titles-dialog",
                "Chart Titles dialog",
                "freex_chart_data_layout_chart_titles_dialog",
                "Chart Titles dialog shows chart title, horizontal-axis title, and vertical-axis title fields populated from the seeded chart metadata."));
            CloseChartDataLayoutTourDialog(openDialog);
            openDialog = null;

            openDialog = new ChartAreaLegendDialog(context.Chart) { Owner = this };
            await ShowChartDataLayoutTourDialogAsync(openDialog);
            captures.Add(await CaptureChartDataLayoutDialogAsync(
                openDialog,
                outputDir,
                "UI-CAT-INSERT-002B,UI-CMD-INSERT-016",
                "format-chart-area-dialog",
                "Format Chart Area dialog",
                "freex_chart_data_layout_format_chart_area_dialog",
                "Format Chart Area shows chart area, plot area, and legend formatting sections, including fill/border fields, legend position, overlay, and font-size controls."));
            CloseChartDataLayoutTourDialog(openDialog);
            openDialog = null;

            var contextMenuCapture = await TryCaptureChartDataLayoutSubtargetContextMenuAsync(outputDir, context);
            if (contextMenuCapture is not null)
                captures.Add(contextMenuCapture);

            ValidateChartDataLayoutTourEvidence(outputDir, captures);
            await WriteChartDataLayoutTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteChartDataLayoutTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (openDialog is { IsVisible: true })
                CloseChartDataLayoutTourDialog(openDialog);

            if (SheetGrid?.ContextMenu is { IsOpen: true } menu)
                menu.IsOpen = false;
        }
    }

    private ChartDataLayoutTourContext EnsureChartDataLayoutTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Chart data/layout/context tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        sheet.Charts.Clear();
        sheet.Sparklines.Clear();
        sheet.StructuredTables.RemoveAll(table => string.Equals(table.Name, ScreenshotTourTableName, StringComparison.OrdinalIgnoreCase));
        sheet.PivotTables.RemoveAll(pivot => string.Equals(pivot.Name, ScreenshotTourPivotTableName, StringComparison.OrdinalIgnoreCase));

        for (uint row = 1; row <= 18; row++)
        {
            for (uint col = 1; col <= 10; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        SeedChartDataLayoutSourceData(sheet);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3));
        if (!TryExecuteCommand(
                new AddChartCommand(
                    sheet.Id,
                    sourceRange,
                    ChartType.Column,
                    ScreenshotTourChartName,
                    left: 420,
                    top: 115,
                    width: 560,
                    height: 340),
                "Insert Chart",
                out var addOutcome))
        {
            throw new InvalidOperationException(addOutcome.ErrorMessage ?? "Chart data/layout/context tour could not create the seeded chart.");
        }

        var chart = FindScreenshotTourChart(sheet)
            ?? throw new InvalidOperationException("Chart data/layout/context tour could not find the seeded chart.");
        chart.Name = "Regional Revenue Chart";

        if (!TryExecuteCommand(
                new SetChartLayoutCommand(
                    sheet.Id,
                    chart.Id,
                    new ChartLayoutOptions(
                        Title: "Regional Revenue",
                        XAxisTitle: "Month",
                        YAxisTitle: "Revenue",
                        ShowLegend: true,
                        LegendPosition: ChartLegendPosition.Bottom,
                        ShowDataLabels: true,
                        DataLabelPosition: ChartDataLabelPosition.OutsideEnd,
                        ShowDataLabelValue: true,
                        DataLabelNumberFormat: ChartDataLabelNumberFormat.Number,
                        ShowLinearTrendline: true,
                        TrendlineType: ChartTrendlineType.Linear,
                        ShowTrendlineEquation: true,
                        ShowErrorBars: true,
                        ErrorBarKind: ChartErrorBarKind.Percentage,
                        ErrorBarValue: 5,
                        ChartAreaFillColor: new CellColor(248, 250, 252),
                        PlotAreaFillColor: new CellColor(255, 255, 255),
                        PlotAreaBorderColor: new CellColor(148, 163, 184),
                        PlotAreaBorderThickness: 1.25)),
                "Format Chart Layout",
                out var layoutOutcome))
        {
            throw new InvalidOperationException(layoutOutcome.ErrorMessage ?? "Chart data/layout/context tour could not format the seeded chart.");
        }

        var waterfallRange = new GridRange(new CellAddress(sheet.Id, 9, 1), new CellAddress(sheet.Id, 14, 2));
        ChartModel? waterfallChart = null;
        if (TryExecuteCommand(
                new AddChartCommand(
                    sheet.Id,
                    waterfallRange,
                    ChartType.Waterfall,
                    "Waterfall Context",
                    left: 420,
                    top: 475,
                    width: 360,
                    height: 240),
                "Insert Waterfall Chart",
                out _))
        {
            waterfallChart = sheet.Charts.FirstOrDefault(candidate =>
                string.Equals(candidate.Title, "Waterfall Context", StringComparison.OrdinalIgnoreCase));
        }

        _options.ObjectsDisplay = AppOptionsObjectDisplay.Placeholders;
        SetSelectionRange(new GridRange(sourceRange.Start, sourceRange.Start), sourceRange.Start);
        EnsureCellVisible(sourceRange.Start);
        RefreshChartContextualTabs();
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.ChartContextTabs.Single(tab => tab.Header == "Chart Design"));
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new ChartDataLayoutTourContext(sheet, chart, waterfallChart, sourceRange, waterfallRange);
    }

    private static void SeedChartDataLayoutSourceData(Sheet sheet)
    {
        var cells = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Month")),
            (1, 2, new TextValue("North")),
            (1, 3, new TextValue("South")),
            (2, 1, new TextValue("Jan")),
            (2, 2, new NumberValue(1280)),
            (2, 3, new NumberValue(940)),
            (3, 1, new TextValue("Feb")),
            (3, 2, new NumberValue(1460)),
            (3, 3, new NumberValue(1020)),
            (4, 1, new TextValue("Mar")),
            (4, 2, new NumberValue(1325)),
            (4, 3, new NumberValue(1180)),
            (5, 1, new TextValue("Apr")),
            (5, 2, new NumberValue(1580)),
            (5, 3, new NumberValue(1210)),
            (6, 1, new TextValue("May")),
            (6, 2, new NumberValue(1710)),
            (6, 3, new NumberValue(1325)),
            (9, 1, new TextValue("Step")),
            (9, 2, new TextValue("Amount")),
            (10, 1, new TextValue("Start")),
            (10, 2, new NumberValue(420)),
            (11, 1, new TextValue("Online")),
            (11, 2, new NumberValue(130)),
            (12, 1, new TextValue("Retail")),
            (12, 2, new NumberValue(-80)),
            (13, 1, new TextValue("Services")),
            (13, 2, new NumberValue(95)),
            (14, 1, new TextValue("End")),
            (14, 2, new NumberValue(565))
        };

        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
    }

    private static async Task ShowChartDataLayoutTourDialogAsync(Window dialog)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Show();
        dialog.Activate();
        dialog.UpdateLayout();
        await Task.Delay(450);
        await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static void CloseChartDataLayoutTourDialog(Window dialog)
    {
        if (dialog.IsVisible)
            dialog.Close();
    }

    private async Task<ChartDataLayoutTourManifestCapture> CaptureChartDataLayoutWindowStateAsync(
        string outputDir,
        string catalogIds,
        string state,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 800);
        return CreateChartDataLayoutCapture(
            catalogIds,
            state,
            surface,
            fileName,
            "RenderTargetBitmap-chart-window",
            ActualWidth,
            Math.Min(ActualHeight, 800),
            evidenceSummary);
    }

    private async Task<ChartDataLayoutTourManifestCapture> CaptureChartDataLayoutDialogAsync(
        Window dialog,
        string outputDir,
        string catalogIds,
        string state,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        dialog.UpdateLayout();
        await Task.Delay(250);
        await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
        return CreateChartDataLayoutCapture(
            catalogIds,
            state,
            surface,
            fileName,
            "RenderTargetBitmap-chart-dialog-window",
            dialog.ActualWidth,
            dialog.ActualHeight,
            evidenceSummary);
    }

    private async Task<ChartDataLayoutTourManifestCapture?> TryCaptureChartDataLayoutSubtargetContextMenuAsync(
        string outputDir,
        ChartDataLayoutTourContext context)
    {
        if (context.WaterfallChart is null)
            return null;

        OnWaterfallChartPointContextMenuRequested(context.WaterfallChart, pointIndex: 1, new Point(760, 610));
        await Task.Delay(350);

        if (SheetGrid.ContextMenu is not { } menu)
            return null;

        menu.UpdateLayout();
        await CaptureElementAsync(menu, outputDir, "freex_chart_data_layout_waterfall_point_context_menu");
        var headers = menu.Items
            .OfType<MenuItem>()
            .Select(item => item.Header?.ToString() ?? string.Empty)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToArray();
        menu.IsOpen = false;

        return CreateChartDataLayoutCapture(
            "UI-CAT-INSERT-002C",
            "waterfall-point-context-menu",
            "Waterfall data-point context menu",
            "freex_chart_data_layout_waterfall_point_context_menu",
            "RenderTargetBitmap-chart-subtarget-context-menu",
            menu.ActualWidth,
            menu.ActualHeight,
            $"Waterfall data-point context menu opens for a seeded point with {string.Join(", ", headers)} command text.",
            headers);
    }

    private ChartDataLayoutTourManifestCapture CreateChartDataLayoutCapture(
        string catalogIds,
        string state,
        string surface,
        string fileName,
        string captureMethod,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string evidenceSummary,
        IReadOnlyList<string>? visibleCommands = null)
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet();
        var chart = sheet?.Charts.FirstOrDefault(candidate => string.Equals(candidate.Title, "Regional Revenue", StringComparison.OrdinalIgnoreCase))
            ?? sheet?.Charts.FirstOrDefault(candidate => string.Equals(candidate.Title, ScreenshotTourChartName, StringComparison.OrdinalIgnoreCase));

        return new ChartDataLayoutTourManifestCapture(
            CaptureKey: $"chart-data-layout:{state}",
            PairKey: $"interactive:chart-data-layout:{state}",
            CatalogIds: catalogIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureLogicalWidth,
            CaptureLogicalHeight: captureLogicalHeight,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            ChartCount: sheet?.Charts.Count ?? 0,
            ActiveChartTitle: chart?.Title ?? string.Empty,
            ActiveChartType: chart?.Type.ToString() ?? string.Empty,
            ActiveChartDataRange: chart?.DataRange.ToString() ?? string.Empty,
            VisibleCommands: visibleCommands ?? [],
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteChartDataLayoutTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_chart_data_layout_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, ChartDataLayoutTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateChartDataLayoutTourEvidence(
        string outputDir,
        IReadOnlyList<ChartDataLayoutTourManifestCapture> captures)
    {
        if (captures.Count < 8)
            throw new InvalidOperationException($"Chart data/layout/context tour expected at least 8 captures but created {captures.Count}.");

        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Chart data/layout/context tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private static async Task WriteChartDataLayoutTourManifestAsync(
        string outputDir,
        ChartDataLayoutTourContext context,
        IReadOnlyList<ChartDataLayoutTourManifestCapture> captures)
    {
        var manifest = new ChartDataLayoutTourManifest(
            Tool: "FREEX_CHART_DATA_LAYOUT_TOUR",
            EvidenceFamily: "chart-data-layout-context",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "chart-data-layout-context:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_chart_data_layout_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-INSERT-002B", "UI-CAT-INSERT-002C", "UI-CMD-INSERT-016"],
            SheetName: context.Sheet.Name,
            SourceRange: context.SourceRange.ToString(),
            ChartTitle: context.Chart.Title ?? string.Empty,
            ChartType: context.Chart.Type.ToString(),
            ChartDataRange: context.Chart.DataRange.ToString(),
            WaterfallContextRange: context.WaterfallChart is null ? string.Empty : context.WaterfallRange.ToString(),
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new ChartDataLayoutTourManifestPairing(
                "interactive:chart-data-layout:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, keytip, UIA, range-picker, or screen capture input is used."
                    : "Window, dialog, and menu captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            Captures: captures,
            CoveredStates:
            [
                "Seeded embedded chart visible with Chart Design contextual tab selected.",
                "Seeded embedded chart visible with Format contextual tab selected.",
                "Select Data Source dialog with chart range, series preview, axis-label preview, Switch Row/Column, and Hidden and Empty Cells controls.",
                "Move Chart dialog with object/new-sheet target choices.",
                "Change Chart Type dialog subtype gallery.",
                "Chart Styles dialog gallery.",
                "Chart Titles dialog for chart and axis-title metadata.",
                "Format Chart Area dialog for chart area, plot area, and legend layout/format controls.",
                "Waterfall point context menu when the supported chart-point context surface is available."
            ],
            Limitations:
            [
                "This bounded tour captures real FreeX WPF chart windows, dialogs, and menus with RenderTargetBitmap; it is not foreground CopyFromScreen proof.",
                "Dialog captures show production visual/default-focus states, but this slice does not submit dialog mutations, save/reload native JSON/XLSX metadata, or prove undo/repeat.",
                "The chart is seeded and formatted through in-process workbook commands; physical mouse, keytip, access-key, range-picker collapse/restore, and UIA invocation remain open.",
                "The worksheet window captures use FreeX's deterministic chart-object placeholder display so the selected chart target is visible without depending on asynchronous full chart rendering.",
                "FreeX currently exposes chart contextual tabs for visible normal charts; physical chart selection handles and chart-area/plot-area/series/axis/title/legend hit-tested subtarget selection remain open.",
                "The subtarget context capture is limited to the supported Waterfall data-point Set as Total context menu.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, ChartDataLayoutTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.ChartDataLayoutTourManifest);
    }

    private sealed record ChartDataLayoutTourContext(
        Sheet Sheet,
        ChartModel Chart,
        ChartModel? WaterfallChart,
        GridRange SourceRange,
        GridRange WaterfallRange);

    private sealed record ChartDataLayoutTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string SheetName,
        string SourceRange,
        string ChartTitle,
        string ChartType,
        string ChartDataRange,
        string WaterfallContextRange,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        ChartDataLayoutTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<ChartDataLayoutTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record ChartDataLayoutTourManifestPairing(
        string PairKeyTemplate,
        string CounterpartApp,
        string CounterpartTool,
        string CounterpartStatus);

    private sealed record ChartDataLayoutTourManifestCapture(
        string CaptureKey,
        string PairKey,
        IReadOnlyList<string> CatalogIds,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRange,
        int ChartCount,
        string ActiveChartTitle,
        string ActiveChartType,
        string ActiveChartDataRange,
        IReadOnlyList<string> VisibleCommands,
        string EvidenceSummary);
}
