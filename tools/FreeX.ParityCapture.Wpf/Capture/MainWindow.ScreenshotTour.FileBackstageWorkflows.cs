using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;
using PdfSharp.Pdf.IO;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const string FileBackstageWorkflowsTourSavedWorkbookFileName = "freex_file_backstage_workflows_saved.xlsx";
    private const string FileBackstageWorkflowsTourPdfFileName = "freex_file_backstage_workflows_export.pdf";

    private async Task CaptureFileBackstageWorkflowsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteFileBackstageWorkflowsTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureFileBackstageWorkflowsTourContext(outputDir);
        var captures = new List<FileBackstageWorkflowsTourManifestCapture>();
        PrintPreviewDialog? printPreviewDialog = null;

        try
        {
            ShowStartScreen();
            _backstageFrame?.FocusEntry("BackstageNewButton");
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFileBackstageWorkflowWindowAsync(
                outputDir,
                "new-entry-focused",
                "File > New",
                "Backstage Home/New",
                "freex_file_backstage_new_entry_focused",
                "Backstage New is focused with the Blank Workbook tile visible; this records the deterministic entry point without synthesizing mouse or keytip input."));

            CreateNewWorkbook();
            _workbook.Name = "File Backstage New Result";
            UpdateTitleBar();
            HideStartScreen();
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFileBackstageWorkflowWindowAsync(
                outputDir,
                "new-workbook-result",
                "File > New > Blank workbook",
                "Worksheet grid",
                "freex_file_backstage_new_workbook_result",
                "A new clean workbook is created through the production workbook factory with no current file path and workbook focus returned to the grid."));

            RestoreFileBackstageWorkflowsWorkbookContext(context);
            ShowStartScreen();
            _backstageFrame?.FocusEntry("BackstageOpenButton");
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFileBackstageWorkflowWindowAsync(
                outputDir,
                "open-recent-filtered-list",
                "File > Open",
                "Backstage Open/Recent",
                "freex_file_backstage_open_recent_filtered_list",
                "Backstage Open shows deterministic existing recent files while the seeded missing recent path is filtered out by the Recent/Pinned planner."));

            SwitchToPinnedTab();
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFileBackstageWorkflowWindowAsync(
                outputDir,
                "open-pinned-list",
                "File > Open > Pinned",
                "Backstage Open/Pinned",
                "freex_file_backstage_open_pinned_list",
                "The Pinned tab shows seeded pinned workbooks with pin/unpin row affordances exposed through the backstage model."));

            ShowStartScreen();
            _backstageFrame?.FocusEntry("BackstageSaveAsButton");
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFileBackstageWorkflowWindowAsync(
                outputDir,
                "save-as-native-dialog-guard",
                "File > Save As",
                "Backstage Save As",
                "freex_file_backstage_save_as_native_dialog_guard",
                "Save As command focus is captured, but the native SaveFileDialog is intentionally not opened without foreground OS ownership."));

            var savedWorkbookPath = Path.Combine(outputDir, FileBackstageWorkflowsTourSavedWorkbookFileName);
            var savedWorkbookBytes = await SaveFileBackstageWorkflowWorkbookAsync(savedWorkbookPath);
            ShowStartScreen();
            ShowInfoView();
            _backstageFrame?.FocusEntry("BackstageSaveButton");
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFileBackstageWorkflowWindowAsync(
                outputDir,
                "saved-title-path-info",
                "File > Save",
                "Backstage Info saved state",
                "freex_file_backstage_saved_title_path_info",
                "Save writes the workbook through SaveWorkbookToTargetAsync; Info shows the saved workbook name/path and clean saved state."));

            await OpenFileAsync(savedWorkbookPath);
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFileBackstageWorkflowWindowAsync(
                outputDir,
                "reopened-workbook-title-path",
                "File > Open",
                "Worksheet grid reopened workbook",
                "freex_file_backstage_reopened_workbook_title_path",
                "The saved workbook is reopened through OpenFileAsync and the persisted title, current path, and seeded grid values are visible."));

            ShowStartScreen();
            ShowPrintView();
            _backstageFrame?.FocusEntry("BackstagePrintButton");
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFileBackstageWorkflowWindowAsync(
                outputDir,
                "print-entry-settings",
                "File > Print",
                "Backstage Print preview",
                "freex_file_backstage_print_entry_settings",
                "Backstage Print shows the print preview directly with page and print options on the left; the native Windows Print dialog is not launched."));

            var sheet = GetCurrentOrFirstScreenshotTourSheet()
                ?? throw new InvalidOperationException("File/backstage workflows tour requires an active worksheet for Print Preview.");
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
            await CaptureWindowElementForScreenshotTourAsync(printPreviewDialog, outputDir, "freex_file_backstage_print_preview_summary");
            captures.Add(CreateFileBackstageWorkflowCapture(
                "print-preview-summary",
                "File > Print > Print Preview",
                "Print Preview",
                "freex_file_backstage_print_preview_summary",
                "RenderTargetBitmap-print-preview-dialog",
                printPreviewDialog.ActualWidth,
                printPreviewDialog.ActualHeight,
                "Print Preview renders the saved workbook with page count/settings summary and close/print toolbar controls without opening native printer UI."));
            printPreviewDialog.Close();
            printPreviewDialog = null;

            ShowStartScreen();
            _backstageFrame?.FocusEntry("BackstageExportButton");
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFileBackstageWorkflowWindowAsync(
                outputDir,
                "export-entry-output-ready",
                "File > Export",
                "Backstage Export",
                "freex_file_backstage_export_entry_output_ready",
                "Export PDF/XPS command focus is captured before producing deterministic in-process PDF output; the native export Save dialog is not opened."));

            var proof = CreateFileBackstageWorkflowsOutputProof(outputDir, savedWorkbookPath, savedWorkbookBytes, printDocument);
            ValidateFileBackstageWorkflowsTourEvidence(outputDir, captures, proof);
            await WriteFileBackstageWorkflowsTourManifestAsync(outputDir, context, captures, proof);
        }
        catch
        {
            DeleteFileBackstageWorkflowsTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (printPreviewDialog is { IsVisible: true })
                printPreviewDialog.Close();
        }
    }

    private FileBackstageWorkflowsTourContext EnsureFileBackstageWorkflowsTourContext(string outputDir)
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("File/backstage workflows tour requires an active worksheet.");

        HideStartScreen();
        _currentSheetId = sheet.Id;
        _currentFilePath = null;
        _currentXlsxFeatureReport = null;
        _workbook.Name = "File Backstage Workflow Evidence";
        sheet.Name = "File Backstage";

        var clearRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 18, 6));
        foreach (var address in clearRange.AllCells())
            sheet.ClearCell(address);

        string[] headers = ["Workflow", "State", "Value"];
        object[][] rows =
        [
            ["New", "Blank workbook", 1d],
            ["Open", "Reopened workbook", 2d],
            ["Save", "Persisted path", 3d],
            ["Print", "Preview pages", 4d],
            ["Export", "PDF output", 5d]
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
                    sheet.SetCell(address, new TextValue(rows[row][col].ToString() ?? string.Empty));
            }
        }

        sheet.SetCell(new CellAddress(sheet.Id, 8, 1), new TextValue("Evidence total"));
        sheet.SetFormula(new CellAddress(sheet.Id, 8, 3), "SUM(C2:C6)");
        sheet.ColumnWidths[1] = 18;
        sheet.ColumnWidths[2] = 24;
        sheet.ColumnWidths[3] = 14;
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 3));
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        _session.RecalculateWorkbook();

        var selectedRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 8, 3));
        SetSelectionRange(selectedRange, selectedRange.Start);
        EnsureCellVisible(selectedRange.Start);
        UpdateViewport();
        RefreshStatusBar();
        MarkWorkbookDirty();
        UpdateTitleBar();

        var recentDir = Path.Combine(outputDir, "recent-source-files");
        Directory.CreateDirectory(recentDir);
        var existingRecentPath = Path.Combine(recentDir, "Workflow Recent Existing.xlsx");
        var secondRecentPath = Path.Combine(recentDir, "Workflow Budget Existing.xlsx");
        var pinnedPath = Path.Combine(recentDir, "Workflow Pinned Existing.xlsx");
        var missingPath = Path.Combine(recentDir, "Workflow Missing Recent.xlsx");
        foreach (var path in new[] { existingRecentPath, secondRecentPath, pinnedPath })
            File.WriteAllText(path, "FreeX file/backstage workflows recent-file placeholder");

        var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        _recentFiles.Entries.Clear();
        _recentFiles.Entries.Add(new RecentFileEntry { Path = existingRecentPath, LastOpened = now.AddMinutes(-1), IsPinned = false });
        _recentFiles.Entries.Add(new RecentFileEntry { Path = secondRecentPath, LastOpened = now.AddMinutes(-2), IsPinned = false });
        _recentFiles.Entries.Add(new RecentFileEntry { Path = pinnedPath, LastOpened = now.AddMinutes(-3), IsPinned = true });
        _recentFiles.Entries.Add(new RecentFileEntry { Path = missingPath, LastOpened = now.AddMinutes(-4), IsPinned = false });
        UpdateSsRecentList();

        var listPlan = BackstageRecentFileListPlanner.Build(_recentFiles.Entries, string.Empty, File.Exists);
        return new FileBackstageWorkflowsTourContext(
            SheetName: sheet.Name,
            SeededRange: selectedRange.ToString(),
            ExistingRecentFileNames: [Path.GetFileName(existingRecentPath), Path.GetFileName(secondRecentPath)],
            PinnedFileNames: [Path.GetFileName(pinnedPath)],
            MissingRecentFileName: Path.GetFileName(missingPath),
            MissingRecentFiltered: listPlan.AllItems.All(item => !StringComparer.OrdinalIgnoreCase.Equals(item.Path, missingPath)),
            RecentPlannerItemCount: listPlan.RecentItems.Count,
            PinnedPlannerItemCount: listPlan.PinnedItems.Count);
    }

    private void RestoreFileBackstageWorkflowsWorkbookContext(FileBackstageWorkflowsTourContext context)
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("File/backstage workflows tour could not restore its worksheet context.");

        _currentSheetId = sheet.Id;
        _currentFilePath = null;
        _currentXlsxFeatureReport = null;
        _workbook.Name = "File Backstage Workflow Evidence";
        sheet.Name = context.SheetName;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Workflow"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("State"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Reopened workbook"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(2d));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Save"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Persisted path"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(3d));
        sheet.SetFormula(new CellAddress(sheet.Id, 8, 3), "SUM(C2:C3)");
        _session.RecalculateWorkbook();
        var selectedRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 8, 3));
        SetSelectionRange(selectedRange, selectedRange.Start);
        EnsureCellVisible(selectedRange.Start);
        MarkWorkbookDirty();
        UpdateTitleBar();
        UpdateViewport();
        RefreshStatusBar();
    }

    private async Task<long> SaveFileBackstageWorkflowWorkbookAsync(string savedWorkbookPath)
    {
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);

        var adapter = FileFormatResolver.FindSaveAdapter(_fileAdapters, ".xlsx", out _)
            ?? throw new InvalidOperationException("File/backstage workflows tour could not find the XLSX save adapter.");
        if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter)))
            throw new InvalidOperationException("File/backstage workflows tour could not save the seeded workbook.");

        return new FileInfo(savedWorkbookPath).Length;
    }

    private FileBackstageWorkflowsTourOutputProof CreateFileBackstageWorkflowsOutputProof(
        string outputDir,
        string savedWorkbookPath,
        long savedWorkbookBytes,
        System.Windows.Documents.FixedDocument printDocument)
    {
        var pdfPath = Path.Combine(outputDir, FileBackstageWorkflowsTourPdfFileName);
        if (File.Exists(pdfPath))
            File.Delete(pdfPath);

        var options = ExportPlanner.CreateEffectiveOptionsForFormat(
            new ExportOptions(
                ExportContentScope.ActiveSheet,
                IncludeDocumentProperties: true,
                OpenAfterPublish: false,
                IgnorePrintAreas: false,
                PageRange: null,
                Quality: ExportQuality.Standard,
                CreateBookmarks: true,
                BookmarkMode: PdfBookmarkMode.SheetNames,
                InitialView: PdfInitialView.SinglePage,
                OpenMode: PdfOpenMode.Outlines,
                BitmapTextWhenFontsMayNotBeEmbedded: false,
                PdfLanguage: _options.PdfExportLanguage),
            ExportFormat.Pdf);
        var request = ExportPlanner.PlanExport(pdfPath, ExportFormat.Pdf, options);
        PdfDocumentExporter.Save(
            printDocument,
            request.Path,
            PdfDocumentExporter.CreateProperties(_workbook, options),
            options.PageRange,
            options.Quality,
            CreatePdfBookmarks(options),
            options.InitialView,
            options.OpenMode,
            includeSelectableText: true,
            options.PdfLanguage);

        using var pdf = PdfReader.Open(request.Path, PdfDocumentOpenMode.Import);
        return new FileBackstageWorkflowsTourOutputProof(
            SavedWorkbookFileName: Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes: savedWorkbookBytes,
            SavedWorkbookRetained: File.Exists(savedWorkbookPath),
            ReopenedWorkbookName: _workbook.Name,
            ReopenedWorkbookPath: _currentFilePath,
            PrintPreviewPageCount: printDocument.Pages.Count,
            ExportedPdfFileName: Path.GetFileName(request.Path),
            ExportedPdfBytes: new FileInfo(request.Path).Length,
            ExportedPdfPageCount: pdf.PageCount,
            ExportedPdfPageLayout: pdf.Internals.Catalog.Elements.GetName("/PageLayout"),
            ExportedPdfPageMode: pdf.Internals.Catalog.Elements.GetName("/PageMode"),
            ExportRequestSummary: WpfExportDescriptionPlanner.DescribeRequest(request));
    }

    private async Task<FileBackstageWorkflowsTourManifestCapture> CaptureFileBackstageWorkflowWindowAsync(
        string outputDir,
        string state,
        string entryPath,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateFileBackstageWorkflowCapture(
            state,
            entryPath,
            surface,
            fileName,
            "RenderTargetBitmap-main-window",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidenceSummary);
    }

    private FileBackstageWorkflowsTourManifestCapture CreateFileBackstageWorkflowCapture(
        string state,
        string entryPath,
        string surface,
        string fileName,
        string captureMethod,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string evidenceSummary)
    {
        var focusedAutomationId = Keyboard.FocusedElement is DependencyObject focusedElement
            ? AutomationProperties.GetAutomationId(focusedElement)
            : null;
        return new FileBackstageWorkflowsTourManifestCapture(
            CaptureKey: $"file-backstage-workflows:{state}",
            PairKey: $"interactive:file-backstage-workflows:{state}",
            ScenarioId: "file-backstage-workflows",
            State: state,
            EntryPath: entryPath,
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

    private static void DeleteFileBackstageWorkflowsTourEvidence(string outputDir)
    {
        foreach (var fileName in FileBackstageWorkflowsTourExpectedFileNames().Append(FileBackstageWorkflowsTourManifestFileName))
            DeleteIfExists(Path.Combine(outputDir, fileName));

        DeleteIfExists(Path.Combine(outputDir, FileBackstageWorkflowsTourSavedWorkbookFileName));
        DeleteIfExists(Path.Combine(outputDir, FileBackstageWorkflowsTourPdfFileName));

        var recentDir = Path.Combine(outputDir, "recent-source-files");
        if (Directory.Exists(recentDir))
            Directory.Delete(recentDir, recursive: true);
    }

    private static void ValidateFileBackstageWorkflowsTourEvidence(
        string outputDir,
        IReadOnlyList<FileBackstageWorkflowsTourManifestCapture> captures,
        FileBackstageWorkflowsTourOutputProof proof)
    {
        var missing = FileBackstageWorkflowsTourExpectedFileNames()
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"File/backstage workflows tour did not capture expected evidence: {string.Join(", ", missing)}.");

        var blankLike = FileBackstageWorkflowsTourExpectedFileNames()
            .Where(fileName => new FileInfo(Path.Combine(outputDir, fileName)).Length < 1024)
            .ToArray();
        if (blankLike.Length > 0)
            throw new InvalidOperationException($"File/backstage workflows tour produced blank-like captures: {string.Join(", ", blankLike)}.");

        if (captures.Count != FileBackstageWorkflowsTourExpectedFileNames().Count)
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "File/backstage workflows tour captured {0} states; expected {1}.",
                    captures.Count,
                    FileBackstageWorkflowsTourExpectedFileNames().Count));

        if (!proof.SavedWorkbookRetained || proof.SavedWorkbookBytes <= 0 || proof.ExportedPdfBytes <= 0)
            throw new InvalidOperationException("File/backstage workflows tour did not create non-empty retained output proof artifacts.");

        if (proof.ExportedPdfPageCount != proof.PrintPreviewPageCount)
            throw new InvalidOperationException("File/backstage workflows tour PDF page count did not match the Print Preview document.");
    }

    private static IReadOnlyList<string> FileBackstageWorkflowsTourExpectedFileNames() =>
    [
        "freex_file_backstage_new_entry_focused.png",
        "freex_file_backstage_new_workbook_result.png",
        "freex_file_backstage_open_recent_filtered_list.png",
        "freex_file_backstage_open_pinned_list.png",
        "freex_file_backstage_save_as_native_dialog_guard.png",
        "freex_file_backstage_saved_title_path_info.png",
        "freex_file_backstage_reopened_workbook_title_path.png",
        "freex_file_backstage_print_entry_settings.png",
        "freex_file_backstage_print_preview_summary.png",
        "freex_file_backstage_export_entry_output_ready.png"
    ];

    private static async Task WriteFileBackstageWorkflowsTourManifestAsync(
        string outputDir,
        FileBackstageWorkflowsTourContext context,
        IReadOnlyList<FileBackstageWorkflowsTourManifestCapture> captures,
        FileBackstageWorkflowsTourOutputProof proof)
    {
        var manifest = new FileBackstageWorkflowsTourManifest(
            Tool: "FREEX_FILE_BACKSTAGE_WORKFLOWS_TOUR=1",
            EvidenceFamily: "FreeX screenshot-tour visual and output evidence",
            EvidenceSubject: "File/backstage workflow persistence",
            EvidenceApp: "FreeX",
            ScenarioId: "file-backstage-workflows",
            OutputDirectory: "screenshots/file-backstage-workflows-tour/",
            OutputNaming: "freex_file_backstage_<state>.png",
            CatalogEvidenceTarget: "UI-CAT-FILE-001, UI-CAT-FILE-001A, UI-CAT-FILE-001B, UI-CAT-FILE-002, UI-CAT-FILE-002A, UI-CAT-FILE-002C",
            CatalogIds:
            [
                "UI-CAT-FILE-001",
                "UI-CAT-FILE-001A",
                "UI-CAT-FILE-001B",
                "UI-CAT-FILE-002",
                "UI-CAT-FILE-002A",
                "UI-CAT-FILE-002C",
                "UI-CMD-FILE-001",
                "UI-CMD-FILE-003",
                "UI-CMD-FILE-006",
                "UI-CMD-FILE-007"
            ],
            EntryPaths:
            [
                "File > New",
                "File > Open / Recent",
                "File > Open / Pinned",
                "File > Save",
                "File > Save As",
                "File > Print",
                "File > Print Preview",
                "File > Export"
            ],
            SheetName: context.SheetName,
            SeededRange: context.SeededRange,
            ExistingRecentFileNames: context.ExistingRecentFileNames,
            PinnedFileNames: context.PinnedFileNames,
            MissingRecentFileName: context.MissingRecentFileName,
            MissingRecentFiltered: context.MissingRecentFiltered,
            RecentPlannerItemCount: context.RecentPlannerItemCount,
            PinnedPlannerItemCount: context.PinnedPlannerItemCount,
            CaptureStatus: "complete-with-native-dialog-foreground-limitations",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: FileBackstageWorkflowsTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process WPF RenderTargetBitmap captures and output-file inspection; no global mouse, keyboard, native OpenFileDialog, native SaveFileDialog, native PrintDialog, or screen-wide CopyFromScreen automation is used."
                    : "Abort before file write unless the expected FreeX window owns foreground focus; native OS dialogs are not opened by this tour."),
            Captures: captures,
            OutputProof: proof,
            CoveredStates: captures.Select(capture => capture.State).ToArray(),
            Limitations:
            [
                "Native OpenFileDialog and SaveFileDialog workflows are foreground-only OS UI and are intentionally not opened by this deterministic tour.",
                "The Save As and Export entry captures stop at the Backstage command surface; output proof is produced through FreeX in-process save/export services.",
                "The native Windows Print dialog is intentionally not opened; Print Preview is captured as the deterministic output surface.",
                "Recent remove/context-menu access is recorded through planner/model state and row affordances, not global Shift+F10 or mouse context-menu input.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, FileBackstageWorkflowsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.FileBackstageWorkflowsTourManifest);
    }

    private sealed record FileBackstageWorkflowsTourContext(
        string SheetName,
        string SeededRange,
        IReadOnlyList<string> ExistingRecentFileNames,
        IReadOnlyList<string> PinnedFileNames,
        string MissingRecentFileName,
        bool MissingRecentFiltered,
        int RecentPlannerItemCount,
        int PinnedPlannerItemCount);

    private sealed record FileBackstageWorkflowsTourManifest(
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
        IReadOnlyList<string> ExistingRecentFileNames,
        IReadOnlyList<string> PinnedFileNames,
        string MissingRecentFileName,
        bool MissingRecentFiltered,
        int RecentPlannerItemCount,
        int PinnedPlannerItemCount,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<FileBackstageWorkflowsTourManifestCapture> Captures,
        FileBackstageWorkflowsTourOutputProof OutputProof,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record FileBackstageWorkflowsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string EntryPath,
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

    private sealed record FileBackstageWorkflowsTourOutputProof(
        string SavedWorkbookFileName,
        long SavedWorkbookBytes,
        bool SavedWorkbookRetained,
        string ReopenedWorkbookName,
        string? ReopenedWorkbookPath,
        int PrintPreviewPageCount,
        string ExportedPdfFileName,
        long ExportedPdfBytes,
        int ExportedPdfPageCount,
        string? ExportedPdfPageLayout,
        string? ExportedPdfPageMode,
        string ExportRequestSummary);
}
