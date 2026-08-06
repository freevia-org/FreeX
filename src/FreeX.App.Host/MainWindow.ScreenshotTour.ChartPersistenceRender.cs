using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureChartPersistenceRenderTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteChartPersistenceRenderTourEvidence(outputDir);

        var savedWorkbookPath = Path.Combine(outputDir, ChartPersistenceRenderTourSavedWorkbookFileName);
        DeleteIfExists(savedWorkbookPath);

        WindowState = WindowState.Normal;
        Width = 1280;
        Height = 820;
        await Task.Delay(700);

        var context = EnsureChartPersistenceRenderTourContext();
        var captures = new List<ChartPersistenceRenderTourManifestCapture>();

        try
        {
            _options.ObjectsDisplay = AppOptionsObjectDisplay.All;
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.ChartContextTabs.Single(tab => tab.Header == "Chart Design"));
            UpdateViewport();
            RefreshToolbar();
            captures.Add(await CaptureChartPersistenceRenderWindowStateAsync(
                outputDir,
                context,
                "seeded-rendered-chart",
                "freex_chart_persistence_render_seeded_rendered_chart",
                "Seeded embedded Column chart is shown with Objects Display set to All so the current FreeX chart renderer is exercised before mutations are submitted.",
                "initial-render"));

            var contextMenuCapture = await TryCaptureChartPersistenceRenderSubtargetMenuAsync(outputDir, context);
            if (contextMenuCapture is not null)
                captures.Add(contextMenuCapture);

            SubmitChartPersistenceRenderMutations(context);
            context = ResolveChartPersistenceRenderCurrentContext(savedWorkbookPath, "after-mutation");
            _options.ObjectsDisplay = AppOptionsObjectDisplay.All;
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.ChartContextTabs.Single(tab => tab.Header == "Chart Design"));
            UpdateViewport();
            RefreshToolbar();
            captures.Add(await CaptureChartPersistenceRenderWindowStateAsync(
                outputDir,
                context,
                "mutated-rendered-chart",
                "freex_chart_persistence_render_mutated_rendered_chart",
                "Submitted chart mutations changed type, style, title, legend, axis titles, colors, data labels, and data source through workbook commands; Objects Display All captures the rendered result.",
                "after-mutation-render"));

            _options.ObjectsDisplay = AppOptionsObjectDisplay.Placeholders;
            UpdateViewport();
            captures.Add(await CaptureChartPersistenceRenderWindowStateAsync(
                outputDir,
                context,
                "mutated-placeholder-chart",
                "freex_chart_persistence_render_mutated_placeholder_chart",
                "The same mutated chart is captured with Objects Display set to Placeholders, proving the deterministic chart-object fallback state separately from rendered chart output.",
                "after-mutation-placeholder"));

            context = await SaveChartPersistenceRenderWorkbookAsync(outputDir, savedWorkbookPath, context);
            captures.Add(await CaptureChartPersistenceRenderWindowStateAsync(
                outputDir,
                context,
                "saved-native-json-title",
                "freex_chart_persistence_render_saved_native_json_title",
                "After saving through SaveWorkbookToTargetAsync to the native FreeX workbook adapter, the title/status state shows the persisted workbook path while the mutated chart remains visible.",
                "saved"));

            await OpenFileAsync(savedWorkbookPath);
            context = ResolveChartPersistenceRenderCurrentContext(savedWorkbookPath, "after-reopen");
            _options.ObjectsDisplay = AppOptionsObjectDisplay.All;
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.ChartContextTabs.Single(tab => tab.Header == "Chart Design"));
            UpdateViewport();
            RefreshToolbar();
            captures.Add(await CaptureChartPersistenceRenderWindowStateAsync(
                outputDir,
                context,
                "reopened-rendered-chart",
                "freex_chart_persistence_render_reopened_rendered_chart",
                "The saved native FreeX workbook is reopened through the host open path, and the mutated chart metadata/data source is visible again with Objects Display All.",
                "after-reopen-render"));

            _options.ObjectsDisplay = AppOptionsObjectDisplay.Placeholders;
            UpdateViewport();
            captures.Add(await CaptureChartPersistenceRenderWindowStateAsync(
                outputDir,
                context,
                "reopened-placeholder-chart",
                "freex_chart_persistence_render_reopened_placeholder_chart",
                "After reopen, the same persisted chart is captured in placeholder mode to record the current deterministic fallback state for chart objects.",
                "after-reopen-placeholder"));

            ValidateChartPersistenceRenderTourEvidence(outputDir, captures, savedWorkbookPath);
            await WriteChartPersistenceRenderTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteChartPersistenceRenderTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (SheetGrid?.ContextMenu is { IsOpen: true } menu)
                menu.IsOpen = false;
        }
    }

    private ChartPersistenceRenderTourContext EnsureChartPersistenceRenderTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Chart render/persistence tour requires an active worksheet.");

        HideStartScreen();
        _currentSheetId = sheet.Id;
        _currentFilePath = null;
        sheet.Name = "Chart Persistence";
        sheet.Charts.Clear();
        sheet.Sparklines.Clear();
        sheet.StructuredTables.RemoveAll(table => string.Equals(table.Name, ScreenshotTourTableName, StringComparison.OrdinalIgnoreCase));
        sheet.PivotTables.RemoveAll(pivot => string.Equals(pivot.Name, ScreenshotTourPivotTableName, StringComparison.OrdinalIgnoreCase));

        for (uint row = 1; row <= 18; row++)
        {
            for (uint col = 1; col <= 10; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        SeedChartPersistenceRenderSourceData(sheet);

        var sourceRange = Range(sheet.Id, 1, 1, 6, 3);
        var mutatedSourceRange = Range(sheet.Id, 1, 1, 7, 4);
        if (!TryExecuteCommand(
                new AddChartCommand(
                    sheet.Id,
                    sourceRange,
                    ChartType.Column,
                    ScreenshotTourChartName,
                    left: 430,
                    top: 115,
                    width: 610,
                    height: 360),
                "Insert Chart",
                out var addOutcome))
        {
            throw new InvalidOperationException(addOutcome.ErrorMessage ?? "Chart render/persistence tour could not create the seeded chart.");
        }

        var chart = FindScreenshotTourChart(sheet)
            ?? throw new InvalidOperationException("Chart render/persistence tour could not find the seeded chart.");
        chart.Name = "Chart Persistence Render";

        ExecuteChartPersistenceRenderCommand(
            new SetChartLayoutCommand(
                sheet.Id,
                chart.Id,
                new ChartLayoutOptions(
                    Title: "Regional Revenue Seed",
                    XAxisTitle: "Month",
                    YAxisTitle: "Revenue",
                    ShowLegend: true,
                    LegendPosition: ChartLegendPosition.Bottom,
                    ShowDataLabels: false,
                    ChartAreaFillColor: new CellColor(245, 247, 250),
                    PlotAreaFillColor: new CellColor(255, 255, 255),
                    PlotAreaBorderColor: new CellColor(148, 163, 184),
                    PlotAreaBorderThickness: 1)),
            "Format Seed Chart");

        var waterfallRange = Range(sheet.Id, 10, 1, 15, 2);
        ChartModel? waterfallChart = null;
        if (TryExecuteCommand(
                new AddChartCommand(
                    sheet.Id,
                    waterfallRange,
                    ChartType.Waterfall,
                    "Persistence Waterfall",
                    left: 430,
                    top: 520,
                    width: 360,
                    height: 220),
                "Insert Waterfall Chart",
                out _))
        {
            waterfallChart = sheet.Charts.FirstOrDefault(candidate =>
                string.Equals(candidate.Title, "Persistence Waterfall", StringComparison.OrdinalIgnoreCase));
        }

        SetSelectionRange(sourceRange, sourceRange.Start);
        EnsureCellVisible(sourceRange.Start);
        RefreshChartContextualTabs();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateViewport();
        UpdateLayout();
        _workbook.Name = "Chart persistence render";
        MarkWorkbookDirty();
        UpdateTitleBar();

        return CreateChartPersistenceRenderContext(
            sheet,
            chart,
            waterfallChart,
            sourceRange,
            mutatedSourceRange,
            savedWorkbookPath: string.Empty,
            savedWorkbookBytes: 0,
            persistenceStage: "seeded");
    }

    private static void SeedChartPersistenceRenderSourceData(Sheet sheet)
    {
        var cells = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Month")),
            (1, 2, new TextValue("North")),
            (1, 3, new TextValue("South")),
            (1, 4, new TextValue("East")),
            (2, 1, new TextValue("Jan")),
            (2, 2, new NumberValue(1280)),
            (2, 3, new NumberValue(940)),
            (2, 4, new NumberValue(760)),
            (3, 1, new TextValue("Feb")),
            (3, 2, new NumberValue(1460)),
            (3, 3, new NumberValue(1020)),
            (3, 4, new NumberValue(890)),
            (4, 1, new TextValue("Mar")),
            (4, 2, new NumberValue(1325)),
            (4, 3, new NumberValue(1180)),
            (4, 4, new NumberValue(940)),
            (5, 1, new TextValue("Apr")),
            (5, 2, new NumberValue(1580)),
            (5, 3, new NumberValue(1210)),
            (5, 4, new NumberValue(1035)),
            (6, 1, new TextValue("May")),
            (6, 2, new NumberValue(1710)),
            (6, 3, new NumberValue(1325)),
            (6, 4, new NumberValue(1110)),
            (7, 1, new TextValue("Jun")),
            (7, 2, new NumberValue(1840)),
            (7, 3, new NumberValue(1405)),
            (7, 4, new NumberValue(1240)),
            (10, 1, new TextValue("Step")),
            (10, 2, new TextValue("Amount")),
            (11, 1, new TextValue("Start")),
            (11, 2, new NumberValue(520)),
            (12, 1, new TextValue("Online")),
            (12, 2, new NumberValue(145)),
            (13, 1, new TextValue("Retail")),
            (13, 2, new NumberValue(-65)),
            (14, 1, new TextValue("Services")),
            (14, 2, new NumberValue(120)),
            (15, 1, new TextValue("End")),
            (15, 2, new NumberValue(720))
        };

        foreach (var (row, col, value) in cells)
            SetTourCell(sheet, row, col, value);
    }

    private void SubmitChartPersistenceRenderMutations(ChartPersistenceRenderTourContext context)
    {
        ExecuteChartPersistenceRenderCommand(
            new ChangeChartSourceCommand(context.Sheet.Id, context.Chart.Id, context.MutatedSourceRange, firstRowIsHeader: true, firstColIsCategories: true),
            "Submit Select Data Source");
        ExecuteChartPersistenceRenderCommand(
            new ChangeChartTypeCommand(context.Sheet.Id, context.Chart.Id, ChartType.Line),
            "Submit Change Chart Type");
        ExecuteChartPersistenceRenderCommand(
            new SetChartStyleCommand(context.Sheet.Id, context.Chart.Id, 18),
            "Submit Chart Style");
        ExecuteChartPersistenceRenderCommand(
            new SetChartLayoutCommand(
                context.Sheet.Id,
                context.Chart.Id,
                new ChartLayoutOptions(
                    Title: "Regional Revenue Persisted",
                    XAxisTitle: "Period",
                    YAxisTitle: "Revenue USD",
                    ShowLegend: true,
                    LegendPosition: ChartLegendPosition.Right,
                    ShowDataLabels: true,
                    DataLabelPosition: ChartDataLabelPosition.OutsideEnd,
                    ShowDataLabelValue: true,
                    DataLabelNumberFormat: ChartDataLabelNumberFormat.Number,
                    ShowLinearTrendline: true,
                    TrendlineType: ChartTrendlineType.Linear,
                    ShowTrendlineEquation: true,
                    ShowErrorBars: true,
                    ErrorBarKind: ChartErrorBarKind.Percentage,
                    ErrorBarValue: 4,
                    ChartAreaFillColor: new CellColor(239, 246, 255),
                    PlotAreaFillColor: new CellColor(255, 255, 255),
                    PlotAreaBorderColor: new CellColor(37, 99, 235),
                    PlotAreaBorderThickness: 1.5)),
            "Submit Chart Layout");
    }

    private void ExecuteChartPersistenceRenderCommand(IWorkbookCommand command, string label)
    {
        if (!TryExecuteCommand(command, label, out var outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? $"Chart render/persistence tour command '{label}' failed.");
    }

    private async Task<ChartPersistenceRenderTourContext> SaveChartPersistenceRenderWorkbookAsync(
        string outputDir,
        string savedWorkbookPath,
        ChartPersistenceRenderTourContext context)
    {
        var adapter = FileDialogFilterBuilder.FindSaveAdapter(_fileAdapters, ".fxl", out _)
            ?? throw new InvalidOperationException("Chart render/persistence tour could not find the native FreeX save adapter.");
        var saved = await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter));
        if (!saved)
            throw new InvalidOperationException("Chart render/persistence tour could not save the native FreeX workbook.");

        var savedWorkbookBytes = new FileInfo(savedWorkbookPath).Length;
        return context with
        {
            SavedWorkbookPath = savedWorkbookPath,
            SavedWorkbookBytes = savedWorkbookBytes,
            PersistenceStage = "saved"
        };
    }

    private ChartPersistenceRenderTourContext ResolveChartPersistenceRenderCurrentContext(
        string savedWorkbookPath,
        string persistenceStage)
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Chart render/persistence tour could not resolve the active worksheet.");
        var chart = sheet.Charts.FirstOrDefault(candidate =>
                string.Equals(candidate.Title, "Regional Revenue Persisted", StringComparison.OrdinalIgnoreCase))
            ?? sheet.Charts.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Chart Persistence Render", StringComparison.OrdinalIgnoreCase))
            ?? sheet.Charts.FirstOrDefault()
            ?? throw new InvalidOperationException("Chart render/persistence tour could not resolve the active chart.");
        var waterfallChart = sheet.Charts.FirstOrDefault(candidate => candidate.Type == ChartType.Waterfall);
        var sourceRange = Range(sheet.Id, 1, 1, 6, 3);
        var mutatedSourceRange = Range(sheet.Id, 1, 1, 7, 4);
        var savedWorkbookBytes = File.Exists(savedWorkbookPath) ? new FileInfo(savedWorkbookPath).Length : 0;

        return CreateChartPersistenceRenderContext(
            sheet,
            chart,
            waterfallChart,
            sourceRange,
            mutatedSourceRange,
            savedWorkbookPath,
            savedWorkbookBytes,
            persistenceStage);
    }

    private ChartPersistenceRenderTourContext CreateChartPersistenceRenderContext(
        Sheet sheet,
        ChartModel chart,
        ChartModel? waterfallChart,
        GridRange sourceRange,
        GridRange mutatedSourceRange,
        string savedWorkbookPath,
        long savedWorkbookBytes,
        string persistenceStage)
    {
        return new ChartPersistenceRenderTourContext(
            Sheet: sheet,
            Chart: chart,
            WaterfallChart: waterfallChart,
            SourceRange: sourceRange,
            MutatedSourceRange: mutatedSourceRange,
            SavedWorkbookPath: savedWorkbookPath,
            SavedWorkbookOutputFileName: string.IsNullOrWhiteSpace(savedWorkbookPath) ? string.Empty : Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes: savedWorkbookBytes,
            PersistenceStage: persistenceStage);
    }

    private async Task<ChartPersistenceRenderTourManifestCapture?> TryCaptureChartPersistenceRenderSubtargetMenuAsync(
        string outputDir,
        ChartPersistenceRenderTourContext context)
    {
        if (context.WaterfallChart is null)
            return null;

        OnWaterfallChartPointContextMenuRequested(context.WaterfallChart, pointIndex: 1, new Point(760, 640));
        await Task.Delay(350);

        if (SheetGrid.ContextMenu is not { } menu)
            return null;

        menu.UpdateLayout();
        await CaptureElementAsync(menu, outputDir, "freex_chart_persistence_render_waterfall_point_context_menu");
        var headers = menu.Items
            .OfType<MenuItem>()
            .Select(item => item.Header?.ToString() ?? string.Empty)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToArray();
        menu.IsOpen = false;

        return CreateChartPersistenceRenderCapture(
            context,
            "waterfall-point-context-menu",
            "freex_chart_persistence_render_waterfall_point_context_menu",
            "Waterfall data-point context menu",
            "RenderTargetBitmap-chart-subtarget-context-menu",
            menu.ActualWidth,
            menu.ActualHeight,
            "Supported chart subtarget proof opens the Waterfall data-point context menu without synthetic foreground clicks.",
            "subtarget-context-menu",
            headers);
    }

    private async Task<ChartPersistenceRenderTourManifestCapture> CaptureChartPersistenceRenderWindowStateAsync(
        string outputDir,
        ChartPersistenceRenderTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string persistenceStage)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 820);
        return CreateChartPersistenceRenderCapture(
            context,
            state,
            fileName,
            "Chart workbook window",
            _options.ObjectsDisplay == AppOptionsObjectDisplay.All
                ? "RenderTargetBitmap-window-full-chart-renderer"
                : "RenderTargetBitmap-window-full-chart-placeholder",
            ActualWidth,
            Math.Min(ActualHeight, 820),
            evidenceSummary,
            persistenceStage,
            []);
    }

    private ChartPersistenceRenderTourManifestCapture CreateChartPersistenceRenderCapture(
        ChartPersistenceRenderTourContext context,
        string state,
        string fileName,
        string surface,
        string captureMethod,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string evidenceSummary,
        string persistenceStage,
        IReadOnlyList<string> visibleCommands)
    {
        return new ChartPersistenceRenderTourManifestCapture(
            CaptureKey: $"chart-persistence-render:{state}",
            PairKey: $"interactive:chart-persistence-render:{state}",
            CatalogIds: ["UI-CAT-INSERT-002", "UI-CAT-INSERT-002B", "UI-CAT-INSERT-002C", "UI-CMD-INSERT-016"],
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureLogicalWidth,
            CaptureLogicalHeight: captureLogicalHeight,
            SheetName: context.Sheet.Name,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            ObjectDisplayMode: _options.ObjectsDisplay.ToString(),
            ChartCount: context.Sheet.Charts.Count,
            ActiveChartName: context.Chart.Name ?? string.Empty,
            ActiveChartTitle: context.Chart.Title ?? string.Empty,
            ActiveChartType: context.Chart.Type.ToString(),
            ActiveChartStyleId: context.Chart.ChartStyleId,
            ActiveChartDataRange: context.Chart.DataRange.ToString(),
            LegendVisible: context.Chart.ShowLegend,
            LegendPosition: context.Chart.LegendPosition.ToString(),
            XAxisTitle: context.Chart.XAxisTitle ?? string.Empty,
            YAxisTitle: context.Chart.YAxisTitle ?? string.Empty,
            RendererSupported: ChartTypeSupport.IsRenderable(context.Chart.Type),
            PersistenceStage: persistenceStage,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            VisibleCommands: visibleCommands,
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteChartPersistenceRenderTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_chart_persistence_render_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, ChartPersistenceRenderTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);

        var savedWorkbookPath = Path.Combine(outputDir, ChartPersistenceRenderTourSavedWorkbookFileName);
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);
    }

    private static void ValidateChartPersistenceRenderTourEvidence(
        string outputDir,
        IReadOnlyList<ChartPersistenceRenderTourManifestCapture> captures,
        string savedWorkbookPath)
    {
        if (captures.Count < 6)
            throw new InvalidOperationException($"Chart render/persistence tour expected at least 6 captures but created {captures.Count}.");

        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Chart render/persistence tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");

        var blank = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !IsNonBlankPng(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (blank.Length > 0)
            throw new InvalidOperationException(
                $"Chart render/persistence tour created blank capture(s): {string.Join(", ", blank)}.");

        if (!File.Exists(savedWorkbookPath) || new FileInfo(savedWorkbookPath).Length <= 0)
            throw new InvalidOperationException("Chart render/persistence tour did not retain a non-empty native FreeX workbook.");
    }

    private static async Task WriteChartPersistenceRenderTourManifestAsync(
        string outputDir,
        ChartPersistenceRenderTourContext context,
        IReadOnlyList<ChartPersistenceRenderTourManifestCapture> captures)
    {
        var manifest = new ChartPersistenceRenderTourManifest(
            Tool: "FREEX_CHART_PERSISTENCE_RENDER_TOUR",
            EvidenceFamily: "chart-render-persistence",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "chart-render-persistence:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_chart_persistence_render_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-INSERT-002", "UI-CAT-INSERT-002B", "UI-CAT-INSERT-002C", "UI-CMD-INSERT-016"],
            SheetName: context.Sheet.Name,
            InitialSourceRange: context.SourceRange.ToString(),
            MutatedSourceRange: context.MutatedSourceRange.ToString(),
            FinalChartTitle: context.Chart.Title ?? string.Empty,
            FinalChartType: context.Chart.Type.ToString(),
            FinalChartStyleId: context.Chart.ChartStyleId,
            FinalChartDataRange: context.Chart.DataRange.ToString(),
            SavedWorkbookPath: context.SavedWorkbookPath,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            PersistencePath: "SaveWorkbookToTargetAsync(.fxl native FreeX adapter) then OpenFileAsync(saved .fxl)",
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new ChartPersistenceRenderTourManifestPairing(
                "interactive:chart-persistence-render:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, keytip, UIA, range-picker, or screen capture input is used."
                    : "Window and menu captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            Captures: captures,
            SubmittedMutations:
            [
                "AddChartCommand seeds the embedded Column chart from A1:C6.",
                "ChangeChartSourceCommand submits Select Data-style source range mutation from A1:C6 to A1:D7.",
                "ChangeChartTypeCommand submits Column to Line.",
                "SetChartStyleCommand submits chart style 18.",
                "SetChartLayoutCommand submits title, axis titles, legend position, data labels, trendline, error bars, and area/plot formatting.",
                "SaveWorkbookToTargetAsync writes the native .fxl workbook and OpenFileAsync reloads it through the host open path."
            ],
            CoveredStates:
            [
                "Seeded chart rendered with Objects Display All.",
                "Supported Waterfall data-point context menu proof.",
                "Mutated chart rendered after submitted commands.",
                "Mutated chart placeholder mode.",
                "Saved native FreeX workbook title/status state.",
                "Reopened native FreeX workbook rendered chart state.",
                "Reopened native FreeX workbook placeholder state."
            ],
            Limitations:
            [
                "This tour proves the current FreeX WPF chart renderer for supported chart types and separately records placeholder mode; it is still RenderTargetBitmap evidence, not foreground CopyFromScreen proof.",
                "Persistence is proven for the native FreeX .fxl adapter through host save/open services; XLSX chart mutation persistence remains a separate compatibility lane.",
                "The submitted mutations use workbook command/service paths directly rather than physical dialog OK clicks, range-picker foreground interaction, keytips, or mouse drags.",
                "The subtarget proof remains limited to the supported Waterfall data-point context menu; chart area, plot area, series, axis, title, and legend hit-tested context menus are not yet generally available.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, ChartPersistenceRenderTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.ChartPersistenceRenderTourManifest);
    }

    private sealed record ChartPersistenceRenderTourContext(
        Sheet Sheet,
        ChartModel Chart,
        ChartModel? WaterfallChart,
        GridRange SourceRange,
        GridRange MutatedSourceRange,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistenceStage);

    private sealed record ChartPersistenceRenderTourManifest(
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
        string InitialSourceRange,
        string MutatedSourceRange,
        string FinalChartTitle,
        string FinalChartType,
        int? FinalChartStyleId,
        string FinalChartDataRange,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistencePath,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        ChartPersistenceRenderTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<ChartPersistenceRenderTourManifestCapture> Captures,
        IReadOnlyList<string> SubmittedMutations,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record ChartPersistenceRenderTourManifestPairing(
        string PairKeyTemplate,
        string CounterpartApp,
        string CounterpartTool,
        string CounterpartStatus);

    private sealed record ChartPersistenceRenderTourManifestCapture(
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
        string SheetName,
        string SelectedRange,
        string ObjectDisplayMode,
        int ChartCount,
        string ActiveChartName,
        string ActiveChartTitle,
        string ActiveChartType,
        int? ActiveChartStyleId,
        string ActiveChartDataRange,
        bool LegendVisible,
        string LegendPosition,
        string XAxisTitle,
        string YAxisTitle,
        bool RendererSupported,
        string PersistenceStage,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        IReadOnlyList<string> VisibleCommands,
        string EvidenceSummary);
}
