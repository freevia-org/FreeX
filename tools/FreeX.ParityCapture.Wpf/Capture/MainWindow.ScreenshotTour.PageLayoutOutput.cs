using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using PdfSharp.Pdf.IO;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const string PageLayoutOutputTourSavedWorkbookFileName = "freex_page_layout_output_saved.xlsx";
    private const string PageLayoutOutputTourPdfFileName = "freex_page_layout_output_print_titles.pdf";

    private async Task CapturePageLayoutOutputTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeletePageLayoutOutputTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1240;
        Height = 780;
        await Task.Delay(700);

        var sheet = EnsurePageLayoutOutputTourContext();
        var captures = new List<PageLayoutOutputTourManifestCapture>();
        PageSetupDialog? pageSetupDialog = null;
        PrintPreviewDialog? printPreviewDialog = null;

        try
        {
            captures.Add(await CapturePageLayoutOutputMenuAsync(
                outputDir,
                "background-native-picker-guard",
                "freex_page_layout_output_background_native_picker_guard",
                "Background",
                "Background command evidence captures the owned menu surface only; the native image picker is intentionally not opened without foreground ownership.",
                ["UI-CAT-PAGE-001", "UI-CMD-PAGE-002"]));

            pageSetupDialog = new PageSetupDialog(
                sheet,
                SheetGrid.SelectedRange,
                request => ApplyPageSetupRangeSelection(pageSetupDialog, request),
                PageSetupInitialFocusTarget.RepeatRows)
            {
                Owner = this
            };
            pageSetupDialog.Show();
            await Task.Delay(350);
            pageSetupDialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(pageSetupDialog, outputDir, "freex_page_layout_output_print_titles_defaults");
            captures.Add(CreatePageLayoutOutputCapture(
                "print-titles-defaults",
                "freex_page_layout_output_print_titles_defaults",
                "Page Setup opens from Print Titles on the Sheet tab with print area, rows/columns to repeat, range-picker buttons, print options, and page order visible.",
                "Page Setup > Sheet",
                "RenderTargetBitmap-page-setup-dialog-window",
                ["UI-CAT-PAGE-001", "UI-CMD-PAGE-002", "UI-CMD-PAGE-003"],
                []));

            SelectViewPanesZoomTourRange(sheet, Range(sheet.Id, 1, 1, 2, 6));
            pageSetupDialog.RowsRepeatPickerButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(350);
            pageSetupDialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(pageSetupDialog, outputDir, "freex_page_layout_output_print_titles_range_picker_result");
            captures.Add(CreatePageLayoutOutputCapture(
                "print-titles-range-picker-result",
                "freex_page_layout_output_print_titles_range_picker_result",
                "Rows to repeat is populated by the Page Setup range-picker callback from the current worksheet selection without native or unsafe input.",
                "Page Setup > Sheet range picker",
                "RenderTargetBitmap-page-setup-dialog-window",
                ["UI-CMD-PAGE-002"],
                []));
            pageSetupDialog.Close();
            pageSetupDialog = null;

            SelectViewPanesZoomTourRange(sheet, sheet.PrintArea ?? Range(sheet.Id, 1, 1, 24, 6));
            captures.Add(await CapturePageLayoutOutputMenuAsync(
                outputDir,
                "print-area-menu-status",
                "freex_page_layout_output_print_area_menu_status",
                "Print Area",
                "Print Area menu status is captured against the selected persisted print-area range, including Set and Clear.",
                ["UI-CAT-PAGE-001", "UI-CMD-PAGE-001"]));

            SelectViewPanesZoomTourRange(sheet, Range(sheet.Id, 12, 5, 12, 5));
            captures.Add(await CapturePageLayoutOutputMenuAsync(
                outputDir,
                "breaks-menu-status",
                "freex_page_layout_output_breaks_menu_status",
                "Breaks",
                "Breaks menu status is captured with seeded row and column breaks plus a selected split point for Insert/Remove/Reset evidence.",
                ["UI-CAT-PAGE-001", "UI-CMD-PAGE-001"]));

            ApplyPageLayoutScaleToFit(new WorksheetScaleToFit(null, 1, 2));
            if (FindRenderedRibbonControl("Scale Width") is ComboBox outScaleWidthBox) outScaleWidthBox.Text = "1 page";
            if (FindRenderedRibbonControl("Scale Height") is ComboBox outScaleHeightBox) outScaleHeightBox.Text = "2 pages";
            if (FindRenderedRibbonControl("Scale Percent") is ComboBox outScalePercentBox) outScalePercentBox.Text = "100%";
            SyncPageLayoutSetupTourControls(sheet);
            captures.Add(await CapturePageLayoutOutputWindowStateAsync(
                outputDir,
                "scale-to-fit-result-status",
                "freex_page_layout_output_scale_to_fit_result_status",
                "Scale-to-Fit result status shows the production fit-to-pages model and ribbon fields after applying one-page-wide by two-pages-tall output settings.",
                ["UI-CAT-PAGE-001", "UI-CMD-PAGE-002"]));

            var printDocument = PrintRenderer.RenderWorksheet(_workbook, sheet.Id, _viewportService);
            var printSettings = PrintSettingsPlanner.Build(sheet, textResolver: WpfPrintSettingsTextResolver.Instance);
            printPreviewDialog = new PrintPreviewDialog(
                _workbook.Name,
                printDocument,
                printSettings,
                showMargins: () => PageMarginsBtn_Click(this, new RoutedEventArgs()),
                showPageSetup: () => PageSetupDialogBtn_Click(this, new RoutedEventArgs()),
                refreshPreviewWithSettings: BuildActiveSheetPrintPreview,
                sheetId: sheet.Id,
                sheet: sheet,
                executeCommand: command => TryExecuteCommand(command, "Print Settings"))
            {
                Owner = this
            };
            printPreviewDialog.Show();
            await Task.Delay(450);
            printPreviewDialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(printPreviewDialog, outputDir, "freex_page_layout_output_print_preview_summary");
            captures.Add(CreatePageLayoutOutputCapture(
                "print-preview-output-summary",
                "freex_page_layout_output_print_preview_summary",
                "Print Preview links the seeded print area, print titles, page breaks, gridlines/headings, and scale-to-fit state to a rendered output summary.",
                "Print Preview",
                "RenderTargetBitmap-print-preview-dialog-window",
                ["UI-CAT-PAGE-001", "UI-CMD-PAGE-001", "UI-CMD-PAGE-002", "UI-CMD-PAGE-003"],
                []));
            printPreviewDialog.Close();
            printPreviewDialog = null;

            var proof = await CreatePageLayoutOutputProofAsync(outputDir, sheet, printDocument);
            ValidatePageLayoutOutputTourEvidence(outputDir, captures, proof);
            await WritePageLayoutOutputTourManifestAsync(outputDir, captures, proof);
        }
        catch
        {
            DeletePageLayoutOutputTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (pageSetupDialog is { IsVisible: true })
                pageSetupDialog.Close();

            if (printPreviewDialog is { IsVisible: true })
                printPreviewDialog.Close();
        }
    }

    private Sheet EnsurePageLayoutOutputTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Page Layout output tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        _workbook.Name = "FreeX Page Layout Output Tour";
        _currentFilePath = null;
        sheet.BackgroundImage = new WorksheetBackgroundImage(CreatePageLayoutOutputTourBackgroundPng(), "image/png", "page-layout-output-background.png");

        for (uint row = 1; row <= 42; row++)
        {
            for (uint col = 1; col <= 8; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                if (row <= 2)
                    SetTourCell(sheet, row, col, new TextValue(row == 1 ? $"Output Field {col}" : $"Repeat Header {col}"));
                else if (col == 1)
                    SetTourCell(sheet, row, col, new TextValue($"Print Row {row - 2}"));
                else
                    SetTourCell(sheet, row, col, new NumberValue(row * 100 + col));
            }
        }

        sheet.ColumnWidths[1] = 18;
        sheet.ColumnWidths[2] = 14;
        sheet.ColumnWidths[3] = 14;
        sheet.ColumnWidths[4] = 14;
        sheet.ColumnWidths[5] = 16;
        sheet.ColumnWidths[6] = 16;
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.PrintArea = Range(sheet.Id, 1, 1, 24, 6);
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 2);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.RowPageBreaks.Clear();
        sheet.RowPageBreaks.Add(12);
        sheet.RowPageBreaks.Add(24);
        sheet.ColumnPageBreaks.Clear();
        sheet.ColumnPageBreaks.Add(5);
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 2);
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.ShowGridlines = true;
        sheet.ShowHeadings = true;
        sheet.CenterHorizontallyOnPage = true;
        sheet.CenterVerticallyOnPage = false;
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.PageHeader = new WorksheetHeaderFooter("", "Output Evidence", "");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &[Page] of &[Pages]", "");

        SelectViewPanesZoomTourRange(sheet, sheet.PrintArea.Value);
        SetWorksheetViewMode(WorksheetViewMode.PageLayout);
        SelectPageLayoutRibbonTabForTour();
        SyncPageLayoutSetupTourControls(sheet);
        UpdateViewport();
        RefreshStatusBar();
        UpdateLayout();
        return sheet;
    }

    private async Task<PageLayoutOutputTourManifestCapture> CapturePageLayoutOutputWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string evidencePurpose,
        IReadOnlyList<string> coveredBacklogIds)
    {
        SelectPageLayoutRibbonTabForTour();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 780);
        return CreatePageLayoutOutputCapture(
            state,
            fileName,
            evidencePurpose,
            "Page Layout ribbon",
            "RenderTargetBitmap-window-full",
            coveredBacklogIds,
            []);
    }

    private async Task<PageLayoutOutputTourManifestCapture> CapturePageLayoutOutputMenuAsync(
        string outputDir,
        string state,
        string fileName,
        string commandName,
        string evidencePurpose,
        IReadOnlyList<string> coveredBacklogIds)
    {
        SelectPageLayoutRibbonTabForTour();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        var button = FindDescendantByRibbonCommandName<Button>(RibbonTabs, commandName)
            ?? throw new InvalidOperationException($"Page Layout output tour could not find '{commandName}' ribbon button.");
        var menu = button.ContextMenu
            ?? throw new InvalidOperationException($"Page Layout output tour could not find '{commandName}' context menu.");

        OpenRibbonContextMenu(button, menu);
        await Task.Delay(350);
        menu.UpdateLayout();
        await CaptureElementAsync(menu, outputDir, fileName);
        var capturedPath = Path.Combine(outputDir, $"{fileName}.png");
        if (new FileInfo(capturedPath).Length < 1024)
        {
            menu.IsOpen = false;
            await Task.Delay(250);
            OpenRibbonContextMenu(button, menu);
            await Task.Delay(500);
            menu.UpdateLayout();
            await CaptureElementAsync(menu, outputDir, fileName);
        }

        var headers = new List<string>();
        AddMenuHeaders(menu, headers);
        menu.IsOpen = false;
        return CreatePageLayoutOutputCapture(
            state,
            fileName,
            evidencePurpose,
            commandName,
            "RenderTargetBitmap-page-layout-context-menu",
            coveredBacklogIds,
            headers);
    }

    private PageLayoutOutputTourManifestCapture CreatePageLayoutOutputCapture(
        string state,
        string fileName,
        string evidencePurpose,
        string surface,
        string captureMethod,
        IReadOnlyList<string> coveredBacklogIds,
        IReadOnlyList<string> menuHeaders)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return new PageLayoutOutputTourManifestCapture(
            CaptureKey: $"interactive:page-layout-output:{state}",
            PairKey: $"interactive:page-layout-output:{state}",
            ScenarioId: "page-layout-output:visual-and-output-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureMethod.Contains("window", StringComparison.Ordinal) ? ActualWidth : 0,
            CaptureLogicalHeight: captureMethod.Contains("window", StringComparison.Ordinal) ? Math.Min(ActualHeight, 780) : 0,
            CoveredBacklogIds: coveredBacklogIds,
            SheetName: sheet?.Name ?? string.Empty,
            ActiveRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            ViewMode: (sheet?.ViewMode ?? WorksheetViewMode.Normal).ToString(),
            BackgroundImageFileName: sheet?.BackgroundImage?.FileName,
            PrintArea: sheet?.PrintArea?.ToString() ?? string.Empty,
            PrintTitleRows: sheet?.PrintTitleRows?.ToString() ?? string.Empty,
            PrintTitleColumns: sheet?.PrintTitleColumns?.ToString() ?? string.Empty,
            RowPageBreaks: sheet?.RowPageBreaks.ToArray() ?? [],
            ColumnPageBreaks: sheet?.ColumnPageBreaks.ToArray() ?? [],
            ScaleToFit: sheet?.ScaleToFit.ToString() ?? WorksheetScaleToFit.Default.ToString(),
            PrintGridlines: sheet?.PrintGridlines ?? false,
            PrintHeadings: sheet?.PrintHeadings ?? false,
            MenuHeaders: menuHeaders,
            EvidencePurpose: evidencePurpose);
    }

    private async Task<PageLayoutOutputTourProof> CreatePageLayoutOutputProofAsync(
        string outputDir,
        Sheet sheet,
        System.Windows.Documents.FixedDocument printDocument)
    {
        var savedWorkbookPath = Path.Combine(outputDir, PageLayoutOutputTourSavedWorkbookFileName);
        var pdfPath = Path.Combine(outputDir, PageLayoutOutputTourPdfFileName);
        var xlsxSaveAdapter = FileFormatResolver.FindSaveAdapter(_fileAdapters, ".xlsx", out _)
            ?? throw new InvalidOperationException("Page Layout output tour could not find an XLSX save adapter.");

        if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, xlsxSaveAdapter)))
            throw new InvalidOperationException("Page Layout output tour could not save the XLSX persistence proof.");

        var loadedWorkbook = await LoadPageLayoutOutputWorkbookAsync(savedWorkbookPath);
        var loadedSheet = loadedWorkbook.GetSheetAt(0);
        var exportOptions = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: true,
            OpenAfterPublish: false,
            IgnorePrintAreas: false,
            Quality: ExportQuality.MinimumSize,
            BookmarkMode: PdfBookmarkMode.PrintTitles,
            InitialView: PdfInitialView.OneColumn,
            OpenMode: PdfOpenMode.Outlines,
            PdfLanguage: "en-US");
        PdfDocumentExporter.Save(
            printDocument,
            pdfPath,
            PdfDocumentExporter.CreateProperties(_workbook, exportOptions),
            pageRange: null,
            exportOptions.Quality,
            CreatePdfBookmarks(exportOptions),
            exportOptions.InitialView,
            exportOptions.OpenMode,
            includeSelectableText: true,
            exportOptions.PdfLanguage);

        using var pdf = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        return new PageLayoutOutputTourProof(
            SavedWorkbookFileName: PageLayoutOutputTourSavedWorkbookFileName,
            SavedWorkbookBytes: new FileInfo(savedWorkbookPath).Length,
            SavedWorkbookReloaded: true,
            ReloadedPrintArea: loadedSheet.PrintArea?.ToString() ?? string.Empty,
            ReloadedPrintTitleRows: loadedSheet.PrintTitleRows?.ToString() ?? string.Empty,
            ReloadedPrintTitleColumns: loadedSheet.PrintTitleColumns?.ToString() ?? string.Empty,
            ReloadedScaleToFit: loadedSheet.ScaleToFit.ToString(),
            ReloadedBackgroundImageFileName: loadedSheet.BackgroundImage?.FileName,
            ReloadedBackgroundImageBytes: loadedSheet.BackgroundImage?.ImageBytes.Length ?? 0,
            PrintPreviewPageCount: printDocument.Pages.Count,
            PrintSettingsSummary: PrintSettingsPlanner.Build(sheet, textResolver: WpfPrintSettingsTextResolver.Instance).Summary,
            ExportedPdfFileName: PageLayoutOutputTourPdfFileName,
            ExportedPdfBytes: new FileInfo(pdfPath).Length,
            ExportedPdfPageCount: pdf.PageCount,
            ExportedPdfPageLayout: pdf.Internals.Catalog.Elements.GetName("/PageLayout"),
            ExportedPdfPageMode: pdf.Internals.Catalog.Elements.GetName("/PageMode"),
            ExportedPdfOutlineCount: pdf.Outlines.Count);
    }

    private async Task<Workbook> LoadPageLayoutOutputWorkbookAsync(string path)
    {
        var adapter = FileFormatResolver.FindOpenAdapter(_fileAdapters, Path.GetExtension(path), out _)
            ?? throw new InvalidOperationException("Page Layout output tour could not find an XLSX open adapter.");
        await using var stream = File.OpenRead(path);
        return adapter.Load(stream);
    }

    private static void DeletePageLayoutOutputTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_page_layout_output_*.png"))
            File.Delete(file);

        foreach (var fileName in new[]
        {
            PageLayoutOutputTourManifestFileName,
            PageLayoutOutputTourSavedWorkbookFileName,
            PageLayoutOutputTourPdfFileName
        })
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidatePageLayoutOutputTourEvidence(
        string outputDir,
        IReadOnlyList<PageLayoutOutputTourManifestCapture> captures,
        PageLayoutOutputTourProof proof)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Page Layout output tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");

        var blankLike = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => new FileInfo(Path.Combine(outputDir, fileName)).Length < 1024)
            .ToArray();
        if (blankLike.Length > 0)
            throw new InvalidOperationException(
                $"Page Layout output tour produced {blankLike.Length} blank-like capture(s): {string.Join(", ", blankLike)}.");

        if (proof.SavedWorkbookBytes <= 0 || proof.ExportedPdfBytes <= 0)
            throw new InvalidOperationException("Page Layout output tour did not create non-empty output proof artifacts.");

        if (proof.ExportedPdfPageCount != proof.PrintPreviewPageCount)
            throw new InvalidOperationException("Page Layout output tour PDF page count did not match the rendered Print Preview document.");
    }

    private static async Task WritePageLayoutOutputTourManifestAsync(
        string outputDir,
        IReadOnlyList<PageLayoutOutputTourManifestCapture> captures,
        PageLayoutOutputTourProof proof)
    {
        var manifest = new PageLayoutOutputTourManifest(
            Tool: "FREEX_PAGE_LAYOUT_OUTPUT_TOUR",
            EvidenceFamily: "page-layout-output",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "page-layout-output:visual-and-output-evidence",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            OutputDirectory: "screenshots/page-layout-output-tour",
            CaptureStatus: "complete-with-native-picker-limitation",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-allowed"
                : "foreground-guarded-render",
            PlannedCaptureCount: 7,
            ActualCaptureCount: captures.Count,
            PlannedOutputProofCount: 2,
            ActualOutputProofCount: 2,
            Pairing: new PageLayoutOutputTourManifestPairing(
                "interactive:page-layout-output:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? "FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1 bypassed foreground-window enforcement for deterministic local rendering."
                    : "Foreground ownership is required before live input or native dialog capture."),
            Captures: captures,
            OutputProof: proof,
            CoveredStates:
            [
                "background-native-picker-guard",
                "print-titles-defaults",
                "print-titles-range-picker-result",
                "print-area-menu-status",
                "breaks-menu-status",
                "scale-to-fit-result-status",
                "print-preview-output-summary",
                "xlsx-persistence-proof",
                "pdf-output-inspection-proof"
            ],
            Limitations:
            [
                "Background Choose uses the shared WPF file dialog realizer; the tour captures the owned command/menu guard and seeded model status but does not open or drive the native image picker without foreground ownership.",
                "Print Preview and PDF proof are produced in-process from FreeX rendering services; no native PrintDialog, printer driver, or SaveFileDialog is opened.",
                "Microsoft Excel counterpart screenshots are not produced by this tool."
            ]);

        var path = Path.Combine(outputDir, PageLayoutOutputTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.PageLayoutOutputTourManifest);
    }

    private static byte[] CreatePageLayoutOutputTourBackgroundPng() =>
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFgwJ/lOh9VwAAAABJRU5ErkJggg==");

    private sealed record PageLayoutOutputTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        DateTimeOffset GeneratedAtUtc,
        string OutputDirectory,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        int PlannedOutputProofCount,
        int ActualOutputProofCount,
        PageLayoutOutputTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<PageLayoutOutputTourManifestCapture> Captures,
        PageLayoutOutputTourProof OutputProof,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record PageLayoutOutputTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record PageLayoutOutputTourManifestCapture(
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
        IReadOnlyList<string> CoveredBacklogIds,
        string SheetName,
        string ActiveRange,
        string ViewMode,
        string? BackgroundImageFileName,
        string PrintArea,
        string PrintTitleRows,
        string PrintTitleColumns,
        IReadOnlyList<uint> RowPageBreaks,
        IReadOnlyList<uint> ColumnPageBreaks,
        string ScaleToFit,
        bool PrintGridlines,
        bool PrintHeadings,
        IReadOnlyList<string> MenuHeaders,
        string EvidencePurpose);

    private sealed record PageLayoutOutputTourProof(
        string SavedWorkbookFileName,
        long SavedWorkbookBytes,
        bool SavedWorkbookReloaded,
        string ReloadedPrintArea,
        string ReloadedPrintTitleRows,
        string ReloadedPrintTitleColumns,
        string ReloadedScaleToFit,
        string? ReloadedBackgroundImageFileName,
        int ReloadedBackgroundImageBytes,
        int PrintPreviewPageCount,
        string PrintSettingsSummary,
        string ExportedPdfFileName,
        long ExportedPdfBytes,
        int ExportedPdfPageCount,
        string? ExportedPdfPageLayout,
        string? ExportedPdfPageMode,
        int ExportedPdfOutlineCount);
}
