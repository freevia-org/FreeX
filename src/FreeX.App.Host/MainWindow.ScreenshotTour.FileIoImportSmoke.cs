using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureFileIoImportSmokeTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteFileIoImportSmokeTourEvidence(outputDir);

        var savedWorkbookPath = Path.Combine(outputDir, "freex_file_io_import_smoke_saved.xlsx");
        var csvImportPath = Path.Combine(outputDir, "freex_file_io_import_smoke_import.csv");
        var textImportPath = Path.Combine(outputDir, "freex_file_io_import_smoke_import.txt");
        DeleteIfExists(savedWorkbookPath);
        DeleteIfExists(csvImportPath);
        DeleteIfExists(textImportPath);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var captures = new List<FileIoImportSmokeTourManifestCapture>();
        var savedWorkbookBytes = 0L;
        try
        {
            var context = EnsureFileIoImportSmokeTourWorkbookContext();
            captures.Add(await CaptureFileIoImportSmokeWindowAsync(
                outputDir,
                "seeded-workbook-before-save",
                "freex_file_io_import_smoke_seeded_workbook",
                "Workbook grid seeded with representative text, numeric, formula, and table-like data before exercising file IO.",
                "Worksheet grid before XLSX save"));

            if (FileDialogFilterBuilder.FindSaveAdapter(_fileAdapters, ".xlsx", out _) is not { } xlsxSaveAdapter)
                throw new InvalidOperationException("File IO/import smoke tour could not find the XLSX save adapter.");

            if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, xlsxSaveAdapter)))
                throw new InvalidOperationException("File IO/import smoke tour could not save the seeded XLSX workbook.");

            savedWorkbookBytes = new FileInfo(savedWorkbookPath).Length;
            captures.Add(await CaptureFileIoImportSmokeWindowAsync(
                outputDir,
                "saved-xlsx-title-status",
                "freex_file_io_import_smoke_saved_xlsx_title_status",
                "After XLSX save, the title bar shows the saved workbook name without the dirty marker and the workbook remains visible in the grid.",
                "Saved workbook title/status"));

            await OpenFileAsync(savedWorkbookPath);
            captures.Add(await CaptureFileIoImportSmokeWindowAsync(
                outputDir,
                "reopened-xlsx-grid",
                "freex_file_io_import_smoke_reopened_xlsx_grid",
                "The saved workbook is reopened through the production open path and the representative grid values are visible again.",
                "Reopened XLSX workbook grid"));

            WriteFileIoImportSmokeCsvSample(csvImportPath);
            var csvRange = ImportFileIoImportSmokeSheet(csvImportPath, ".csv", new CellAddress(_currentSheetId, 8, 1));
            captures.Add(await CaptureFileIoImportSmokeWindowAsync(
                outputDir,
                "imported-csv-result-grid",
                "freex_file_io_import_smoke_imported_csv_grid",
                "CSV data loaded through the CSV adapter and ImportSheetCommand is materialized in the worksheet grid.",
                "Imported CSV result grid",
                csvRange));

            WriteFileIoImportSmokeTextSample(textImportPath);
            var textRange = ImportFileIoImportSmokeSheet(textImportPath, ".txt", new CellAddress(_currentSheetId, 13, 1));
            captures.Add(await CaptureFileIoImportSmokeWindowAsync(
                outputDir,
                "imported-txt-result-grid",
                "freex_file_io_import_smoke_imported_txt_grid",
                "Tab-delimited TXT data loaded through the text adapter and ImportSheetCommand is materialized below the CSV import.",
                "Imported TXT result grid",
                textRange));

            captures.Add(await CaptureFileIoImportSmokeImportWarningAsync(outputDir));

            captures.Add(await CaptureFileIoImportSmokeExportOptionsDialogAsync(
                outputDir,
                ExportFormat.Pdf,
                "export-pdf-options-summary",
                "freex_file_io_import_smoke_export_pdf_options",
                "PDF Export Options dialog summary surface with active-sheet/selection/workbook scope, page range, document properties, PDF-only options, and open-after-publish controls."));

            captures.Add(await CaptureFileIoImportSmokeExportOptionsDialogAsync(
                outputDir,
                ExportFormat.Xps,
                "export-xps-options-summary",
                "freex_file_io_import_smoke_export_xps_options",
                "XPS Export Options dialog summary surface shows PDF-only controls disabled while shared publish controls remain available."));

            ValidateFileIoImportSmokeTourEvidence(outputDir, captures);
            await WriteFileIoImportSmokeTourManifestAsync(outputDir, context, savedWorkbookPath, savedWorkbookBytes, captures);
        }
        catch
        {
            DeleteFileIoImportSmokeTourEvidence(outputDir);
            throw;
        }
        finally
        {
            DeleteIfExists(csvImportPath);
            DeleteIfExists(textImportPath);
            DeleteIfExists(savedWorkbookPath);
        }
    }

    private FileIoImportSmokeTourContext EnsureFileIoImportSmokeTourWorkbookContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("File IO/import smoke tour requires an active worksheet.");

        HideStartScreen();
        _currentSheetId = sheet.Id;
        sheet.Name = "File IO Smoke";

        var clearRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 8));
        foreach (var address in clearRange.AllCells())
            sheet.ClearCell(address);

        string[] headers = ["SKU", "Region", "Units", "Revenue"];
        object[][] rows =
        [
            ["FIO-100", "North", 12d, 1200d],
            ["FIO-200", "South", 8d, 880d],
            ["FIO-300", "West", 16d, 1680d]
        ];

        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < rows[row].Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                if (rows[row][col] is double number)
                    sheet.SetCell(address, new NumberValue(number));
                else
                    sheet.SetCell(address, new TextValue(rows[row][col].ToString() ?? ""));
            }
        }

        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Total revenue"));
        sheet.SetFormula(new CellAddress(sheet.Id, 5, 4), "SUM(D2:D4)");
        _session.RecalculateWorkbook();

        var sampleRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4));
        SetSelectionRange(sampleRange, sampleRange.Start);
        EnsureCellVisible(sampleRange.Start);
        UpdateViewport();
        RefreshStatusBar();
        _workbook.Name = "File IO import smoke";
        _currentFilePath = null;
        MarkWorkbookDirty();
        UpdateTitleBar();

        return new FileIoImportSmokeTourContext(sheet.Name, sampleRange.ToString());
    }

    private GridRange ImportFileIoImportSmokeSheet(string path, string extension, CellAddress destination)
    {
        if (FileDialogFilterBuilder.FindOpenAdapter(_fileAdapters, extension, out var format) is not { } adapter)
            throw new InvalidOperationException($"File IO/import smoke tour could not find an import adapter for {extension}.");

        Workbook imported;
        using (var stream = File.OpenRead(path))
            imported = adapter.Load(stream);

        if (imported.Sheets.Count == 0)
            throw new InvalidOperationException($"File IO/import smoke tour import {Path.GetFileName(path)} produced no sheets.");

        if (!TryExecuteCommand(
                new ImportSheetCommand(_currentSheetId, destination, imported.Sheets[0]),
                "Get Data",
                out var outcome))
        {
            throw new InvalidOperationException(outcome.ErrorMessage ?? $"File IO/import smoke tour import failed for {Path.GetFileName(path)}.");
        }

        var importedRange = FindFileIoImportSmokeImportedRange(imported.Sheets[0], destination);
        SetSelectionRange(importedRange, importedRange.Start);
        EnsureCellVisible(importedRange.Start);
        UpdateViewport();
        RefreshStatusBar();
        RecordDiagnosticEvent(
            "import_completed",
            BuildImportDiagnosticProperties(extension, format?.FormatName ?? adapter.FormatName, null, imported.Sheets.Count));
        return importedRange;
    }

    private async Task<FileIoImportSmokeTourManifestCapture> CaptureFileIoImportSmokeWindowAsync(
        string outputDir,
        string state,
        string fileName,
        string evidenceSummary,
        string surface,
        GridRange? selectedRange = null)
    {
        if (selectedRange is not null)
        {
            var range = selectedRange.Value;
            SetSelectionRange(range, range.Start);
            EnsureCellVisible(range.Start);
        }

        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateFileIoImportSmokeCapture(
            state,
            surface,
            fileName,
            "RenderTargetBitmap-window-full",
            evidenceSummary,
            ActualWidth,
            Math.Min(ActualHeight, 760));
    }

    private async Task<FileIoImportSmokeTourManifestCapture> CaptureFileIoImportSmokeImportWarningAsync(string outputDir)
    {
        var diagnostic = WorkbookImportFailurePlanner.FromException(
            ".xml",
            new InvalidDataException("The selected XML file could not be parsed as SpreadsheetML."));
        var caption = UiText.Get("MainWindowMessage_GetDataTitle");
        var fileName = "freex_file_io_import_smoke_import_warning";
        var captureTask = CaptureFileIoImportSmokeOwnedNativeMessageAsync(
            caption,
            outputDir,
            fileName,
            "import-warning-owned-message",
            "Owned Get Data warning",
            "Get Data warning message",
            "The Get Data owned warning path renders a deterministic import failure message without driving a native file-open dialog.");

        ShowOwnedMessage(diagnostic.UserMessage, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        return await captureTask;
    }

    private async Task<FileIoImportSmokeTourManifestCapture> CaptureFileIoImportSmokeExportOptionsDialogAsync(
        string outputDir,
        ExportFormat format,
        string state,
        string fileName,
        string evidenceSummary)
    {
        var dialog = new ExportOptionsDialog(hasSelection: true, initialPdfLanguage: _options.PdfExportLanguage, format) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(350);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
            return CreateFileIoImportSmokeCapture(
                state,
                format == ExportFormat.Xps ? "XPS Export Options dialog" : "PDF Export Options dialog",
                fileName,
                $"RenderTargetBitmap-export-options-{format.ToString().ToLowerInvariant()}-dialog",
                evidenceSummary,
                dialog.ActualWidth,
                dialog.ActualHeight);
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<FileIoImportSmokeTourManifestCapture> CaptureFileIoImportSmokeOwnedNativeMessageAsync(
        string caption,
        string outputDir,
        string fileName,
        string state,
        string surface,
        string captureMethodSuffix,
        string evidenceSummary)
    {
        var owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (owner == IntPtr.Zero)
            throw new InvalidOperationException("File IO/import smoke tour could not resolve the FreeX owner window handle.");

        var size = await Task.Run(() =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            IntPtr dialogHandle;
            do
            {
                dialogHandle = FindOwnedNativeWindow(owner, caption);
                if (dialogHandle != IntPtr.Zero)
                    break;

                Task.Delay(100).GetAwaiter().GetResult();
            }
            while (DateTime.UtcNow < deadline);

            if (dialogHandle == IntPtr.Zero)
                throw new InvalidOperationException($"File IO/import smoke tour did not find the owned native message '{caption}'.");

            var size = CaptureNativeWindow(dialogHandle, outputDir, fileName);
            PostMessage(dialogHandle, 0x0010, IntPtr.Zero, IntPtr.Zero);
            return size;
        });

        return CreateFileIoImportSmokeCapture(
            state,
            surface,
            fileName,
            $"PrintWindow-{captureMethodSuffix}",
            evidenceSummary,
            size.Width,
            size.Height);
    }

    private FileIoImportSmokeTourManifestCapture CreateFileIoImportSmokeCapture(
        string state,
        string surface,
        string fileName,
        string captureMethod,
        string evidenceSummary,
        double captureLogicalWidth,
        double captureLogicalHeight)
    {
        var focusedAutomationId = Keyboard.FocusedElement is DependencyObject focusedElement
            ? AutomationProperties.GetAutomationId(focusedElement)
            : null;
        return new FileIoImportSmokeTourManifestCapture(
            CaptureKey: $"file-io-import-smoke:{state}",
            PairKey: $"interactive:file-io-import-smoke:{state}",
            ScenarioId: "file-io-import-smoke",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureLogicalWidth,
            CaptureLogicalHeight: captureLogicalHeight,
            FocusedElementAutomationId: focusedAutomationId,
            WorkbookTitle: Title,
            WorkbookName: _workbook.Name,
            CurrentFilePath: _currentFilePath,
            IsWorkbookDirty: _workbookDirty,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            EvidenceSummary: evidenceSummary);
    }

    private static GridRange FindFileIoImportSmokeImportedRange(Sheet importedSheet, CellAddress destination)
    {
        uint maxRow = 0;
        uint maxColumn = 0;
        foreach (var entry in importedSheet.GetUsedCells())
        {
            var address = entry.Key;
            maxRow = Math.Max(maxRow, address.Row);
            maxColumn = Math.Max(maxColumn, address.Col);
        }

        if (maxRow == 0 || maxColumn == 0)
            return new GridRange(destination, destination);

        return new GridRange(
            destination,
            new CellAddress(
                destination.Sheet,
                Math.Min(CellAddress.MaxRow, destination.Row + maxRow - 1),
                Math.Min(CellAddress.MaxCol, destination.Col + maxColumn - 1)));
    }

    private static void WriteFileIoImportSmokeCsvSample(string path)
    {
        File.WriteAllText(
            path,
            string.Join(
                Environment.NewLine,
                "Product,Region,Units,Amount",
                "Coffee,North,14,210",
                "Tea,South,9,135",
                "\"Cocoa, dark\",West,6,156"),
            Encoding.UTF8);
    }

    private static void WriteFileIoImportSmokeTextSample(string path)
    {
        File.WriteAllText(
            path,
            string.Join(
                Environment.NewLine,
                "Date\tChannel\tOrders\tStatus",
                "2026-06-10\tOnline\t18\tOpen",
                "2026-06-11\tRetail\t11\tReady",
                "2026-06-12\tWholesale\t7\tReview"),
            Encoding.UTF8);
    }

    private static void DeleteFileIoImportSmokeTourEvidence(string outputDir)
    {
        foreach (var fileName in FileIoImportSmokeTourExpectedFileNames().Append(FileIoImportSmokeTourManifestFileName))
            DeleteIfExists(Path.Combine(outputDir, fileName));

        DeleteIfExists(Path.Combine(outputDir, "freex_file_io_import_smoke_saved.xlsx"));
        DeleteIfExists(Path.Combine(outputDir, "freex_file_io_import_smoke_import.csv"));
        DeleteIfExists(Path.Combine(outputDir, "freex_file_io_import_smoke_import.txt"));
    }

    private static void ValidateFileIoImportSmokeTourEvidence(
        string outputDir,
        IReadOnlyList<FileIoImportSmokeTourManifestCapture> captures)
    {
        var missing = FileIoImportSmokeTourExpectedFileNames()
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"File IO/import smoke tour did not capture expected evidence: {string.Join(", ", missing)}.");

        if (captures.Count != FileIoImportSmokeTourExpectedFileNames().Count)
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "File IO/import smoke tour captured {0} states; expected {1}.",
                    captures.Count,
                    FileIoImportSmokeTourExpectedFileNames().Count));
    }

    private static IReadOnlyList<string> FileIoImportSmokeTourExpectedFileNames() =>
    [
        "freex_file_io_import_smoke_seeded_workbook.png",
        "freex_file_io_import_smoke_saved_xlsx_title_status.png",
        "freex_file_io_import_smoke_reopened_xlsx_grid.png",
        "freex_file_io_import_smoke_imported_csv_grid.png",
        "freex_file_io_import_smoke_imported_txt_grid.png",
        "freex_file_io_import_smoke_import_warning.png",
        "freex_file_io_import_smoke_export_pdf_options.png",
        "freex_file_io_import_smoke_export_xps_options.png"
    ];

    private static async Task WriteFileIoImportSmokeTourManifestAsync(
        string outputDir,
        FileIoImportSmokeTourContext context,
        string savedWorkbookPath,
        long savedWorkbookBytes,
        IReadOnlyList<FileIoImportSmokeTourManifestCapture> captures)
    {
        var manifest = new FileIoImportSmokeTourManifest(
            Tool: "FREEX_FILE_IO_IMPORT_SMOKE_TOUR=1",
            EvidenceFamily: "FreeX screenshot-tour visual evidence",
            EvidenceSubject: "File IO and interop smoke",
            EvidenceApp: "FreeX",
            ScenarioId: "file-io-import-smoke",
            OutputDirectory: "screenshots/file-io-import-smoke-tour/",
            OutputNaming: "freex_file_io_import_smoke_<state>.png",
            CatalogEvidenceTarget: "File IO and interop smoke; UI-CAT-DATA-001A; UI-CAT-FILE-002C",
            CatalogIds:
            [
                "File IO and interop smoke",
                "UI-CAT-DATA-001",
                "UI-CAT-DATA-001A",
                "UI-CAT-FILE-002",
                "UI-CAT-FILE-002C"
            ],
            EntryPaths:
            [
                "XLSX Save",
                "XLSX Open",
                "Data > Get Data CSV/TXT import",
                "Get Data owned warning",
                "File > Export > PDF/XPS Export Options"
            ],
            SheetName: context.SheetName,
            SeededRange: context.SeededRange,
            SavedWorkbookFileName: Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes: savedWorkbookBytes,
            SavedWorkbookRetained: false,
            CaptureStatus: "complete",
            CaptureMode: "deterministic RenderTargetBitmap plus owned-HWND PrintWindow message capture",
            PlannedCaptureCount: FileIoImportSmokeTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: true,
                Policy: "FreeX main window or a FreeX-owned dialog owns the capture; no global mouse, keyboard, native OpenFileDialog, native SaveFileDialog, or screen-wide CopyFromScreen automation is used."),
            Captures: captures,
            CoveredStates: captures.Select(capture => capture.State).ToArray(),
            Limitations:
            [
                "Native OpenFileDialog and SaveFileDialog foreground workflows remain out of scope for this bounded smoke slice.",
                "PDF/XPS output-file byte inspection and overwrite/cancel workflows remain covered by source/planner/exporter tests, not this visual tour.",
                "Microsoft Excel-paired evidence remains open."
            ]);

        var path = Path.Combine(outputDir, FileIoImportSmokeTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.FileIoImportSmokeTourManifest);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private sealed record FileIoImportSmokeTourContext(
        string SheetName,
        string SeededRange);

    private sealed record FileIoImportSmokeTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        IReadOnlyList<string> EntryPaths,
        string SheetName,
        string SeededRange,
        string SavedWorkbookFileName,
        long SavedWorkbookBytes,
        bool SavedWorkbookRetained,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<FileIoImportSmokeTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record FileIoImportSmokeTourManifestCapture(
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
        string? FocusedElementAutomationId,
        string WorkbookTitle,
        string WorkbookName,
        string? CurrentFilePath,
        bool IsWorkbookDirty,
        string SelectedRange,
        string EvidenceSummary);
}
