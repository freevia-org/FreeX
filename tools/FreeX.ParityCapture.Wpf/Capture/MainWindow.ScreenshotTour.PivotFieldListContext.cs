using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CapturePivotFieldListContextTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeletePivotFieldListContextTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1280;
        Height = 820;
        await Task.Delay(700);

        var context = EnsurePivotFieldListContextTourContext();
        var captures = new List<PivotFieldListContextTourManifestCapture>();

        try
        {
            captures.Add(await CapturePivotFieldListContextWindowStateAsync(
                outputDir,
                "analyze-field-list-visible",
                "freex_pivot_field_list_analyze_field_list",
                "PivotTable Analyze is selected with the production PivotTable Fields pane visible beside a populated pivot result grid.",
                "PivotTable Analyze ribbon plus field list"));

            SelectRibbonTourTab(RibbonScreenshotTourPlanner.PivotContextTabs.Single(tab => tab.Header == "Design"));
            captures.Add(await CapturePivotFieldListContextWindowStateAsync(
                outputDir,
                "design-field-list-visible",
                "freex_pivot_field_list_design_field_list",
                "PivotTable Design is selected while the same pivot target keeps the field list pane visible.",
                "PivotTable Design ribbon plus field list"));

            PivotFieldListDeferLayoutCheckBox.IsChecked = true;
            PivotAvailableFieldsList.SelectedItem = _pivotFieldListAvailableItems.First(item => item.Caption == "Channel");
            MoveSelectedPivotField(PivotFieldBucket.Rows);
            PivotFieldListSearchBox.Text = "sales";
            PivotFieldListUpdateBtn.Focus();
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CapturePivotFieldListContextWindowStateAsync(
                outputDir,
                "field-list-deferred-search-buttons-checks",
                "freex_pivot_field_list_deferred_search_buttons_checks",
                "Field list shows search filtering, checked available-field state, Rows/Columns/Values/Filters/Remove buttons, Defer Layout Update, and enabled Update.",
                "PivotTable Fields pane"));

            PivotFieldListSearchBox.Text = "";
            PivotValuesList.SelectedItem = PivotValuesList.Items.OfType<string>().FirstOrDefault(item => item.Contains("Sales", StringComparison.OrdinalIgnoreCase));
            captures.Add(await CapturePivotFieldListContextMenuAsync(outputDir));

            captures.Add(await CapturePivotValueFieldSettingsDialogForTourAsync(outputDir, context));
            captures.Add(await CapturePivotFieldFilterDialogForTourAsync(outputDir, context));

            PivotFieldListDeferLayoutCheckBox.IsChecked = false;
            PivotFieldListUpdateBtn_Click(PivotFieldListUpdateBtn, new RoutedEventArgs(ButtonBase.ClickEvent));
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
            SetSelectionRange(new GridRange(context.PivotTable.TargetRange.Start, context.PivotTable.TargetRange.Start), context.PivotTable.TargetRange.Start);
            UpdateViewport();
            PivotFieldListPane.Visibility = Visibility.Collapsed;
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CapturePivotFieldListContextWindowStateAsync(
                outputDir,
                "pivot-result-grid",
                "freex_pivot_field_list_result_grid",
                "Pivot result grid remains materialized after the deferred field-list update applies the added Channel row field.",
                "Worksheet pivot result grid"));

            ValidatePivotFieldListContextTourEvidence(outputDir, captures);
            await WritePivotFieldListContextTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeletePivotFieldListContextTourEvidence(outputDir);
            throw;
        }
    }

    private PivotFieldListContextTourContext EnsurePivotFieldListContextTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Pivot field-list/context tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        var headers = new[] { "Region", "Product", "Quarter", "Sales", "Channel", "Margin" };
        object[][] rows =
        [
            ["North", "Coffee", "Q1", 1280d, "Retail", 0.31d],
            ["North", "Tea", "Q1", 760d, "Online", 0.27d],
            ["South", "Coffee", "Q2", 960d, "Retail", 0.29d],
            ["South", "Tea", "Q2", 690d, "Wholesale", 0.24d],
            ["West", "Cocoa", "Q3", 1140d, "Online", 0.34d],
            ["West", "Coffee", "Q4", 1510d, "Retail", 0.36d]
        ];

        var clearRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 18, 12));
        foreach (var address in clearRange.AllCells())
            sheet.ClearCell(address);

        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                if (rows[row][col] is double number)
                    sheet.SetCell(address, new NumberValue(number));
                else
                    sheet.SetCell(address, new TextValue(rows[row][col].ToString() ?? ""));
            }
        }

        sheet.PivotTables.RemoveAll(pivot => string.Equals(pivot.Name, ScreenshotTourPivotTableName, StringComparison.OrdinalIgnoreCase));
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 6));
        var targetRange = new GridRange(new CellAddress(sheet.Id, 2, 8), new CellAddress(sheet.Id, 12, 12));
        if (!TryExecuteCommand(
                new AddPivotTableCommand(
                    sheet.Id,
                    sourceRange,
                    targetRange,
                    ScreenshotTourPivotTableName,
                    rowFieldIndexes: [0],
                    dataFieldIndexes: [3]),
                "Insert PivotTable",
                out var addOutcome))
        {
            throw new InvalidOperationException(addOutcome.ErrorMessage ?? "Pivot field-list/context tour setup failed.");
        }

        var pivotTable = FindScreenshotTourPivotTable(sheet)
            ?? throw new InvalidOperationException("Pivot field-list/context tour could not find the seeded PivotTable.");

        var dataField = new PivotDataFieldModel(3, "Sum of Sales", "sum", NumberFormatId: 4);
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
            throw new InvalidOperationException(layoutOutcome.ErrorMessage ?? "Pivot field-list/context tour layout setup failed.");
        }

        pivotTable = FindScreenshotTourPivotTable(sheet)
            ?? throw new InvalidOperationException("Pivot field-list/context tour lost the seeded PivotTable after layout setup.");

        SetSelectionRange(new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start), pivotTable.TargetRange.Start);
        EnsureCellVisible(pivotTable.TargetRange.Start);
        RefreshPivotFieldListPane();
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.PivotContextTabs.Single(tab => tab.Header == "PivotTable Analyze"));
        UpdateViewport();
        RefreshToolbar();

        return new PivotFieldListContextTourContext(sheet, pivotTable, sourceRange, targetRange, headers);
    }

    private async Task<PivotFieldListContextTourManifestCapture> CapturePivotFieldListContextWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string evidenceSummary,
        string surface)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 780);
        return CreatePivotFieldListContextTourCapture(
            state,
            surface,
            fileName,
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 780),
            evidenceSummary,
            []);
    }

    private async Task<PivotFieldListContextTourManifestCapture> CapturePivotFieldListContextMenuAsync(string outputDir)
    {
        var menu = PivotValuesList.ContextMenu
            ?? throw new InvalidOperationException("Pivot field-list/context tour could not find the values-area context menu.");

        menu.PlacementTarget = PivotValuesList;
        menu.Placement = PlacementMode.Left;
        menu.IsOpen = true;
        await Task.Delay(350);
        menu.UpdateLayout();
        await CaptureElementAsync(menu, outputDir, "freex_pivot_field_list_context_menu_opened");

        var headers = new List<string>();
        AddMenuHeaders(menu, headers);
        menu.IsOpen = false;
        return CreatePivotFieldListContextTourCapture(
            "value-field-context-menu-opened",
            "Pivot field context menu",
            "freex_pivot_field_list_context_menu_opened",
            "RenderTargetBitmap-pivot-field-context-menu",
            menu.ActualWidth,
            menu.ActualHeight,
            "Values-area context menu exposes sort, item/filter actions, clear filter, and Value Field Settings for the selected Sum of Sales field.",
            headers);
    }

    private async Task<PivotFieldListContextTourManifestCapture> CapturePivotValueFieldSettingsDialogForTourAsync(
        string outputDir,
        PivotFieldListContextTourContext context)
    {
        var dataField = context.PivotTable.DataFields.First();
        var dialog = new PivotValueFieldSettingsDialog(dataField, context.SourceHeaders) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_pivot_value_field_settings_dialog");
            return CreatePivotFieldListContextTourCapture(
                "value-field-settings-dialog",
                "Value Field Settings dialog",
                "freex_pivot_value_field_settings_dialog",
                "RenderTargetBitmap-value-field-settings-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "Value Field Settings opens for Sum of Sales with custom-name focus, summary function, Show Values As, base-field controls, number format controls, and OK/Cancel.",
                []);
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<PivotFieldListContextTourManifestCapture> CapturePivotFieldFilterDialogForTourAsync(
        string outputDir,
        PivotFieldListContextTourContext context)
    {
        var selectedItems = new[] { "Coffee", "Tea" };
        var dialog = new PivotFieldFilterDialog(
            PivotSourceContext.ReadItems(_workbook, context.Sheet, context.PivotTable, sourceFieldIndex: 1),
            selectedItems)
        {
            Owner = this,
            Title = UiText.Format("MainWindowMessage_PivotFieldFilterTitle", "Product")
        };

        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            if (FindDescendant<TextBox>(dialog) is { } searchBox)
                searchBox.Text = "co";

            dialog.UpdateLayout();
            await Task.Delay(150);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_pivot_field_filter_dialog");
            return CreatePivotFieldListContextTourCapture(
                "field-filter-dialog",
                "Pivot field Select Items dialog",
                "freex_pivot_field_filter_dialog",
                "RenderTargetBitmap-pivot-field-filter-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "Pivot field Select Items dialog shows search-box filtering, checklist check states, Select All, Label Filter, Value Filter, OK, and Cancel.",
                []);
        }
        finally
        {
            dialog.Close();
        }
    }

    private PivotFieldListContextTourManifestCapture CreatePivotFieldListContextTourCapture(
        string state,
        string surface,
        string fileName,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary,
        IReadOnlyList<string> menuHeaders)
    {
        var availableFields = _pivotFieldListAvailableItems.Select(item => item.Caption).ToArray();
        var checkedFields = _pivotFieldListAvailableItems.Where(item => item.IsChecked).Select(item => item.Caption).ToArray();
        return new PivotFieldListContextTourManifestCapture(
            CaptureKey: $"interactive:pivot-field-list-context:{state}",
            PairKey: $"interactive:pivot-field-list-context:{state}",
            ScenarioId: "pivot:field-list-context",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            FieldListVisible: PivotFieldListPane.Visibility == Visibility.Visible,
            DeferLayoutChecked: PivotFieldListDeferLayoutCheckBox.IsChecked == true,
            UpdateButtonEnabled: PivotFieldListUpdateBtn.IsEnabled,
            SearchText: PivotFieldListSearchBox.Text,
            AvailableFields: availableFields,
            CheckedAvailableFields: checkedFields,
            RowAreaFields: PivotRowsList.Items.OfType<string>().ToArray(),
            ColumnAreaFields: PivotColumnsList.Items.OfType<string>().ToArray(),
            FilterAreaFields: PivotFiltersList.Items.OfType<string>().ToArray(),
            ValueAreaFields: PivotValuesList.Items.OfType<string>().ToArray(),
            MenuHeaders: menuHeaders,
            EvidenceSummary: evidenceSummary);
    }

    private static void DeletePivotFieldListContextTourEvidence(string outputDir)
    {
        foreach (var fileName in PivotFieldListContextTourExpectedFileNames().Append(PivotFieldListContextTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static IReadOnlyList<string> PivotFieldListContextTourExpectedFileNames() =>
    [
        "freex_pivot_field_list_analyze_field_list.png",
        "freex_pivot_field_list_design_field_list.png",
        "freex_pivot_field_list_deferred_search_buttons_checks.png",
        "freex_pivot_field_list_context_menu_opened.png",
        "freex_pivot_value_field_settings_dialog.png",
        "freex_pivot_field_filter_dialog.png",
        "freex_pivot_field_list_result_grid.png"
    ];

    private static void ValidatePivotFieldListContextTourEvidence(
        string outputDir,
        IReadOnlyList<PivotFieldListContextTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Pivot field-list/context tour did not create {capture.OutputFileName}.");

            if (new FileInfo(path).Length == 0)
                throw new InvalidOperationException($"Pivot field-list/context tour created an empty {capture.OutputFileName}.");
        }
    }

    private static async Task WritePivotFieldListContextTourManifestAsync(
        string outputDir,
        PivotFieldListContextTourContext context,
        IReadOnlyList<PivotFieldListContextTourManifestCapture> captures)
    {
        var manifest = new PivotFieldListContextTourManifest(
            Tool: "FREEX_PIVOT_FIELD_LIST_CONTEXT_TOUR",
            EvidenceFamily: "pivot-field-list-context",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "pivot:field-list-context",
            OutputDirectory: outputDir,
            OutputNaming: "freex_pivot_*.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-CONTEXT-003",
                "UI-CAT-CONTEXT-003B",
                "UI-CAT-CONTEXT-003C",
                "UI-CMD-INSERT-002",
                "UI-CMD-INSERT-003",
                "UI-CMD-CTXOBJ-001"
            ],
            SheetName: context.Sheet.Name,
            PivotTableName: context.PivotTable.Name,
            SourceRange: context.SourceRange.ToString(),
            TargetRange: context.PivotTable.TargetRange.ToString(),
            SourceHeaders: context.SourceHeaders,
            CaptureStatus: "complete",
            CaptureMode: "RenderTargetBitmap-main-window-context-menu-and-dialogs",
            PlannedCaptureCount: PivotFieldListContextTourExpectedFileNames().Count,
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
                "The tour manipulates the production WPF controls in-process and does not prove foreground mouse, keyboard, drag/drop, or keytip traversal.",
                "Drag/drop field placement is represented by the same layout mutation path used by buttons; physical pointer drag evidence remains open.",
                "PivotChart field-button dropdowns and saved/reloaded pivot field-list persistence are outside this bounded slice."
            ]);

        var path = Path.Combine(outputDir, PivotFieldListContextTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.PivotFieldListContextTourManifest);
    }

    private sealed record PivotFieldListContextTourContext(
        Sheet Sheet,
        PivotTableModel PivotTable,
        GridRange SourceRange,
        GridRange InitialTargetRange,
        IReadOnlyList<string> SourceHeaders);

    private sealed record PivotFieldListContextTourManifest(
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
        string TargetRange,
        IReadOnlyList<string> SourceHeaders,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<PivotFieldListContextTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record PivotFieldListContextTourManifestCapture(
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
        bool FieldListVisible,
        bool DeferLayoutChecked,
        bool UpdateButtonEnabled,
        string SearchText,
        IReadOnlyList<string> AvailableFields,
        IReadOnlyList<string> CheckedAvailableFields,
        IReadOnlyList<string> RowAreaFields,
        IReadOnlyList<string> ColumnAreaFields,
        IReadOnlyList<string> FilterAreaFields,
        IReadOnlyList<string> ValueAreaFields,
        IReadOnlyList<string> MenuHeaders,
        string EvidenceSummary);
}
