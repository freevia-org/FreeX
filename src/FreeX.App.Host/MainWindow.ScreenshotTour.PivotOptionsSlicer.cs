using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CapturePivotOptionsSlicerTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeletePivotOptionsSlicerTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1360;
        Height = 860;
        await Task.Delay(700);

        var context = EnsurePivotOptionsSlicerTourContext();
        var captures = new List<PivotOptionsSlicerTourManifestCapture>();

        try
        {
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.PivotContextTabs.Single(tab => tab.Header == "PivotTable Analyze"));
            captures.Add(await CapturePivotOptionsSlicerWindowStateAsync(
                outputDir,
                "analyze-pivot-selection",
                "freex_pivot_options_slicer_analyze_selection",
                "PivotTable Analyze is selected against a seeded PivotTable with source fields suitable for options, slicer, timeline, and PivotChart evidence.",
                "PivotTable Analyze ribbon"));

            captures.Add(await CapturePivotTableOptionsDialogForTourAsync(outputDir, context));

            SelectRibbonTourTab(RibbonScreenshotTourPlanner.PivotContextTabs.Single(tab => tab.Header == "Design"));
            PivotBandedRowsBtn_Click(this, new RoutedEventArgs(ButtonBase.ClickEvent));
            PivotBandedColumnsBtn_Click(this, new RoutedEventArgs(ButtonBase.ClickEvent));
            UpdateViewport();
            captures.Add(await CapturePivotOptionsSlicerWindowStateAsync(
                outputDir,
                "design-style-options-surface",
                "freex_pivot_design_style_options_surface",
                "PivotTable Design shows Layout, PivotTable Style Options, PivotTable Styles, and the pivot grid after banded-row/column style options are toggled.",
                "PivotTable Design ribbon and grid"));

            captures.Add(await CapturePivotStyleGalleryDialogForTourAsync(outputDir, context));
            captures.Add(await CaptureInsertSlicerDialogForTourAsync(outputDir, context));
            captures.Add(await CaptureInsertTimelineDialogForTourAsync(outputDir, context));

            if (!TryExecuteCommand(new AddSlicerCommand("Region Slicer", context.PivotTable.Name, "Region"), "Insert Slicer", out var slicerOutcome))
                throw new InvalidOperationException(slicerOutcome.ErrorMessage ?? "Pivot options/slicer tour could not create a slicer.");
            if (!TryExecuteCommand(new AddTimelineCommand("Date Timeline", context.PivotTable.Name, "Date"), "Insert Timeline", out var timelineOutcome))
                throw new InvalidOperationException(timelineOutcome.ErrorMessage ?? "Pivot options/slicer tour could not create a timeline.");
            if (!TryExecuteCommand(new SetSlicerSelectionCommand("Region Slicer", ["North", "West"]), "Slicer", out var slicerFilterOutcome))
                throw new InvalidOperationException(slicerFilterOutcome.ErrorMessage ?? "Pivot options/slicer tour could not filter the slicer.");
            if (!TryExecuteCommand(new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-03-31"), "Timeline", out var timelineFilterOutcome))
                throw new InvalidOperationException(timelineFilterOutcome.ErrorMessage ?? "Pivot options/slicer tour could not filter the timeline.");

            RefreshSlicerTimelinePane();
            UpdateViewport();
            captures.Add(await CapturePivotOptionsSlicerWindowStateAsync(
                outputDir,
                "slicer-timeline-pane-filtered",
                "freex_pivot_slicer_timeline_pane_filtered",
                "Slicer/timeline pane shows real Region Slicer tiles and Date Timeline bounds with active filters connected to the seeded PivotTable.",
                "Slicer and timeline pane"));

            captures.Add(await CapturePivotChartTypeDialogForTourAsync(outputDir));
            if (!TryExecuteCommand(
                    new AddPivotChartCommand(context.Sheet.Id, context.PivotTable.Name, ChartType.Column, $"{context.PivotTable.Name} Chart", left: 520, top: 150, width: 430, height: 300),
                    "Insert PivotChart",
                    out var chartOutcome))
            {
                throw new InvalidOperationException(chartOutcome.ErrorMessage ?? "Pivot options/slicer tour could not create a PivotChart.");
            }

            var chart = FindPivotChartForPivotTable(context.Sheet, context.PivotTable)
                ?? throw new InvalidOperationException("Pivot options/slicer tour could not find the seeded PivotChart.");
            captures.Add(await CapturePivotChartOptionsDialogForTourAsync(outputDir, chart));

            captures.Add(await CapturePivotChartFieldButtonMenuForTourAsync(outputDir, context));

            ValidatePivotOptionsSlicerTourEvidence(outputDir, captures);
            await WritePivotOptionsSlicerTourManifestAsync(outputDir, context, chart, captures);
        }
        catch
        {
            DeletePivotOptionsSlicerTourEvidence(outputDir);
            throw;
        }
    }

    private PivotOptionsSlicerTourContext EnsurePivotOptionsSlicerTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Pivot options/slicer tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        _workbook.Slicers.RemoveAll(slicer => slicer.Name is "Region Slicer");
        _workbook.Timelines.RemoveAll(timeline => timeline.Name is "Date Timeline");
        sheet.Charts.RemoveAll(chart => chart.IsPivotChart || string.Equals(chart.Title, ScreenshotTourChartName, StringComparison.OrdinalIgnoreCase));
        sheet.PivotTables.RemoveAll(pivot => string.Equals(pivot.Name, ScreenshotTourPivotTableName, StringComparison.OrdinalIgnoreCase));

        var headers = new[] { "Region", "Product", "Quarter", "Date", "Sales", "Channel", "Margin" };
        object[][] rows =
        [
            ["North", "Coffee", "Q1", new DateTime(2026, 1, 5), 1280d, "Retail", 0.31d],
            ["North", "Tea", "Q1", new DateTime(2026, 2, 12), 760d, "Online", 0.27d],
            ["South", "Coffee", "Q2", new DateTime(2026, 4, 3), 960d, "Retail", 0.29d],
            ["South", "Tea", "Q2", new DateTime(2026, 5, 18), 690d, "Wholesale", 0.24d],
            ["West", "Cocoa", "Q3", new DateTime(2026, 7, 9), 1140d, "Online", 0.34d],
            ["West", "Coffee", "Q4", new DateTime(2026, 10, 22), 1510d, "Retail", 0.36d],
            ["East", "Tea", "Q1", new DateTime(2026, 3, 16), 880d, "Wholesale", 0.25d]
        ];

        var clearRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 22, 14));
        foreach (var address in clearRange.AllCells())
            sheet.ClearCell(address);

        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                switch (rows[row][col])
                {
                    case double number:
                        sheet.SetCell(address, new NumberValue(number));
                        break;
                    case DateTime date:
                        sheet.SetCell(address, DateTimeValue.FromDateTime(date));
                        break;
                    default:
                        sheet.SetCell(address, new TextValue(rows[row][col].ToString() ?? ""));
                        break;
                }
            }
        }

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, (uint)(rows.Length + 1), (uint)headers.Length));
        var targetRange = new GridRange(new CellAddress(sheet.Id, 2, 9), new CellAddress(sheet.Id, 13, 14));
        if (!TryExecuteCommand(
                new AddPivotTableCommand(
                    sheet.Id,
                    sourceRange,
                    targetRange,
                    ScreenshotTourPivotTableName,
                    rowFieldIndexes: [0],
                    dataFieldIndexes: [4]),
                "Insert PivotTable",
                out var addOutcome))
        {
            throw new InvalidOperationException(addOutcome.ErrorMessage ?? "Pivot options/slicer tour setup failed.");
        }

        var pivotTable = FindScreenshotTourPivotTable(sheet)
            ?? throw new InvalidOperationException("Pivot options/slicer tour could not find the seeded PivotTable.");
        var dataField = new PivotDataFieldModel(4, "Sum of Sales", "sum", NumberFormatId: 4);
        if (!TryExecuteCommand(
                new ConfigurePivotTableLayoutCommand(
                    sheet.Id,
                    pivotTable.Name,
                    rowFields: [new PivotFieldModel(0)],
                    columnFields: [new PivotFieldModel(1)],
                    pageFields: [new PivotFieldModel(2)],
                    dataFields: [dataField]),
                "PivotTable Fields",
                out var layoutOutcome))
        {
            throw new InvalidOperationException(layoutOutcome.ErrorMessage ?? "Pivot options/slicer tour layout setup failed.");
        }

        pivotTable = FindScreenshotTourPivotTable(sheet)
            ?? throw new InvalidOperationException("Pivot options/slicer tour lost the seeded PivotTable after layout setup.");
        SetSelectionRange(new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start), pivotTable.TargetRange.Start);
        EnsureCellVisible(pivotTable.TargetRange.Start);
        RefreshPivotFieldListPane();
        RefreshSlicerTimelinePane();
        UpdateViewport();
        RefreshToolbar();

        return new PivotOptionsSlicerTourContext(sheet, pivotTable, sourceRange, headers);
    }

    private async Task<PivotOptionsSlicerTourManifestCapture> CapturePivotOptionsSlicerWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string evidenceSummary,
        string surface)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 820);
        return CreatePivotOptionsSlicerTourCapture(
            state,
            surface,
            fileName,
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 820),
            evidenceSummary);
    }

    private async Task<PivotOptionsSlicerTourManifestCapture> CapturePivotTableOptionsDialogForTourAsync(
        string outputDir,
        PivotOptionsSlicerTourContext context)
    {
        var cache = _workbook.PivotCaches.FirstOrDefault(cache => cache.CacheId == context.PivotTable.CacheId);
        var dialog = new PivotTableOptionsDialog(context.PivotTable, cache) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            if (FindDescendant<TabControl>(dialog) is { } tabs)
                tabs.SelectedIndex = 2;
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_pivot_options_dialog_display_style_options");
            return CreatePivotOptionsSlicerTourCapture(
                "pivot-options-dialog-display-style-options",
                "PivotTable Options dialog",
                "freex_pivot_options_dialog_display_style_options",
                "RenderTargetBitmap-pivot-options-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "PivotTable Options dialog opens on Display with style selector, row/column headers, field captions, tooltips, classic layout, no-data items, stripes, and expand/collapse options.");
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<PivotOptionsSlicerTourManifestCapture> CapturePivotStyleGalleryDialogForTourAsync(
        string outputDir,
        PivotOptionsSlicerTourContext context)
    {
        var dialog = new PivotStyleGalleryDialog(context.PivotTable.StyleName) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_pivot_style_gallery_dialog");
            return CreatePivotOptionsSlicerTourCapture(
                "pivot-style-gallery-dialog",
                "PivotTable Styles dialog",
                "freex_pivot_style_gallery_dialog",
                "RenderTargetBitmap-pivot-style-gallery-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "PivotTable Styles dialog shows the built-in style gallery with the current PivotStyle selected.");
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<PivotOptionsSlicerTourManifestCapture> CaptureInsertSlicerDialogForTourAsync(
        string outputDir,
        PivotOptionsSlicerTourContext context)
    {
        var dialog = new InsertSlicerDialog(context.SourceHeaders, "Region") { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_pivot_insert_slicer_dialog");
            return CreatePivotOptionsSlicerTourCapture(
                "insert-slicer-dialog",
                "Insert Slicer dialog",
                "freex_pivot_insert_slicer_dialog",
                "RenderTargetBitmap-insert-slicer-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "Insert Slicer dialog shows selectable PivotTable source fields and the generated slicer caption for the Region field.");
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<PivotOptionsSlicerTourManifestCapture> CaptureInsertTimelineDialogForTourAsync(
        string outputDir,
        PivotOptionsSlicerTourContext context)
    {
        var dialog = new InsertTimelineDialog(context.SourceHeaders, "Date") { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_pivot_insert_timeline_dialog");
            return CreatePivotOptionsSlicerTourCapture(
                "insert-timeline-dialog",
                "Insert Timeline dialog",
                "freex_pivot_insert_timeline_dialog",
                "RenderTargetBitmap-insert-timeline-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "Insert Timeline dialog shows selectable date fields and the generated timeline caption for the Date field.");
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<PivotOptionsSlicerTourManifestCapture> CapturePivotChartTypeDialogForTourAsync(string outputDir)
    {
        var dialog = new PivotChartTypeDialog(ChartType.Column) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_pivotchart_type_dialog");
            return CreatePivotOptionsSlicerTourCapture(
                "pivotchart-type-dialog",
                "PivotChart type dialog",
                "freex_pivotchart_type_dialog",
                "RenderTargetBitmap-pivotchart-type-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "PivotChart type dialog shows Recommended PivotCharts and All Charts tabs using the shared chart picker gallery.");
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<PivotOptionsSlicerTourManifestCapture> CapturePivotChartOptionsDialogForTourAsync(
        string outputDir,
        ChartModel chart)
    {
        var dialog = new PivotChartOptionsDialog(chart) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_pivotchart_options_dialog");
            return CreatePivotOptionsSlicerTourCapture(
                "pivotchart-options-dialog",
                "PivotChart Options dialog",
                "freex_pivotchart_options_dialog",
                "RenderTargetBitmap-pivotchart-options-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "PivotChart Options dialog shows chart style, field-button visibility toggles, data table, rounded-corner, hidden-data, and blank-cell options.");
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<PivotOptionsSlicerTourManifestCapture> CapturePivotChartFieldButtonMenuForTourAsync(
        string outputDir,
        PivotOptionsSlicerTourContext context)
    {
        var headers = PivotSourceContext.ReadHeaders(_workbook, context.PivotTable, context.Sheet);
        _pivotFieldMenuContextCaption = PivotUiPlanner.ResolvePivotChartFieldButtonCaption(context.PivotTable, headers, "Axis Fields");
        var menu = CreatePivotFieldContextMenu();
        try
        {
            menu.PlacementTarget = SheetGrid;
            menu.Placement = PlacementMode.RelativePoint;
            menu.HorizontalOffset = 680;
            menu.VerticalOffset = 210;
            menu.IsOpen = true;
            await Task.Delay(350);
            menu.UpdateLayout();
            await CaptureElementAsync(menu, outputDir, "freex_pivotchart_field_button_menu_opened");

            var menuHeaders = new List<string>();
            AddMenuHeaders(menu, menuHeaders);
            return CreatePivotOptionsSlicerTourCapture(
                "pivotchart-field-button-menu-opened",
                "PivotChart field-button menu",
                "freex_pivotchart_field_button_menu_opened",
                "RenderTargetBitmap-pivotchart-field-button-context-menu",
                menu.ActualWidth,
                menu.ActualHeight,
                "PivotChart axis field-button menu uses the production pivot field context menu with sorting, Select Items, label/value filters, Clear Filter, and Value Field Settings.",
                menuHeaders);
        }
        finally
        {
            menu.IsOpen = false;
            _pivotFieldMenuContextCaption = null;
        }
    }

    private PivotOptionsSlicerTourManifestCapture CreatePivotOptionsSlicerTourCapture(
        string state,
        string surface,
        string fileName,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary,
        IReadOnlyList<string>? menuHeaders = null) =>
        new(
            CaptureKey: $"interactive:pivot-options-slicer:{state}",
            PairKey: $"interactive:pivot-options-slicer:{state}",
            ScenarioId: "pivot:options-slicer-timeline-pivotchart",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SelectedRibbonTab: RibbonTabs.SelectedItem is TabItem tab ? tab.Header?.ToString() ?? "" : "",
            PivotFieldListVisible: PivotFieldListPane.Visibility == Visibility.Visible,
            SlicerTimelinePaneVisible: SlicerTimelinePane.Visibility == Visibility.Visible,
            SlicerCount: _workbook.Slicers.Count,
            TimelineCount: _workbook.Timelines.Count,
            PivotChartCount: _workbook.Sheets.SelectMany(sheet => sheet.Charts).Count(chart => chart.IsPivotChart),
            MenuHeaders: menuHeaders ?? [],
            EvidenceSummary: evidenceSummary);

    private static void DeletePivotOptionsSlicerTourEvidence(string outputDir)
    {
        foreach (var fileName in PivotOptionsSlicerTourExpectedFileNames()
                     .Append("freex_pivotchart_field_buttons_surface.png")
                     .Append(PivotOptionsSlicerTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static IReadOnlyList<string> PivotOptionsSlicerTourExpectedFileNames() =>
    [
        "freex_pivot_options_slicer_analyze_selection.png",
        "freex_pivot_options_dialog_display_style_options.png",
        "freex_pivot_design_style_options_surface.png",
        "freex_pivot_style_gallery_dialog.png",
        "freex_pivot_insert_slicer_dialog.png",
        "freex_pivot_insert_timeline_dialog.png",
        "freex_pivot_slicer_timeline_pane_filtered.png",
        "freex_pivotchart_type_dialog.png",
        "freex_pivotchart_options_dialog.png",
        "freex_pivotchart_field_button_menu_opened.png"
    ];

    private static void ValidatePivotOptionsSlicerTourEvidence(
        string outputDir,
        IReadOnlyList<PivotOptionsSlicerTourManifestCapture> captures)
    {
        var missingOrEmpty = captures
            .Select(capture => Path.Combine(outputDir, capture.OutputFileName))
            .Where(path => !File.Exists(path) || new FileInfo(path).Length == 0)
            .Select(Path.GetFileName)
            .ToArray();

        if (missingOrEmpty.Length > 0)
            throw new InvalidOperationException($"Pivot options/slicer tour did not create non-empty evidence: {string.Join(", ", missingOrEmpty)}.");
    }

    private static async Task WritePivotOptionsSlicerTourManifestAsync(
        string outputDir,
        PivotOptionsSlicerTourContext context,
        ChartModel chart,
        IReadOnlyList<PivotOptionsSlicerTourManifestCapture> captures)
    {
        var manifest = new PivotOptionsSlicerTourManifest(
            Tool: "FREEX_PIVOT_OPTIONS_SLICER_TOUR",
            EvidenceFamily: "pivot-options-slicer-timeline-pivotchart",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "pivot:options-slicer-timeline-pivotchart",
            OutputDirectory: outputDir,
            OutputNaming: "freex_pivot*.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-INSERT-001B",
                "UI-CAT-INSERT-001C",
                "UI-CAT-INSERT-001E",
                "UI-CAT-INSERT-001F",
                "UI-CAT-INSERT-001G",
                "UI-CAT-INSERT-001H",
                "UI-CMD-INSERT-011",
                "UI-CMD-INSERT-013",
                "UI-CMD-INSERT-014"
            ],
            SheetName: context.Sheet.Name,
            PivotTableName: context.PivotTable.Name,
            SourceRange: context.SourceRange.ToString(),
            SourceHeaders: context.SourceHeaders,
            PivotChartTitle: chart.Title ?? "",
            CaptureStatus: "complete",
            CaptureMode: "RenderTargetBitmap-main-window-dialogs-context-menu",
            PlannedCaptureCount: PivotOptionsSlicerTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture without OS foreground ownership; no global mouse, keyboard, or screen capture input is used."
                    : "Abort and clear current PNG/manifest evidence unless the FreeX window being captured owns foreground focus immediately before render and file write."),
            Captures: captures,
            CoveredStates: captures.Select(capture => capture.State).ToArray(),
            Limitations:
            [
                "This in-app tour captures FreeX-only RenderTargetBitmap evidence; paired Microsoft Excel screenshots remain separate.",
                "The tour manipulates production WPF controls and command paths in-process and does not prove foreground mouse, keytip, dialog access-key, UIA Invoke, or physical drag/drop traversal.",
                "PivotChart field-button options and menu routing are covered by the PivotChart Options dialog and the same production context-menu surface used by field-button requests; rendered chart-field-button annotations and hit-test pointer opening remain outside this bounded background-render slice.",
                "Saved/reloaded PivotTable, slicer, timeline, and PivotChart persistence remains covered by model/package tests rather than this visual tour."
            ]);

        var path = Path.Combine(outputDir, PivotOptionsSlicerTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.PivotOptionsSlicerTourManifest);
    }

    private sealed record PivotOptionsSlicerTourContext(
        Sheet Sheet,
        PivotTableModel PivotTable,
        GridRange SourceRange,
        IReadOnlyList<string> SourceHeaders);

    private sealed record PivotOptionsSlicerTourManifest(
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
        string PivotTableName,
        string SourceRange,
        IReadOnlyList<string> SourceHeaders,
        string PivotChartTitle,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<PivotOptionsSlicerTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record PivotOptionsSlicerTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRibbonTab,
        bool PivotFieldListVisible,
        bool SlicerTimelinePaneVisible,
        int SlicerCount,
        int TimelineCount,
        int PivotChartCount,
        IReadOnlyList<string> MenuHeaders,
        string EvidenceSummary);
}
