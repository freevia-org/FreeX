using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FileIoImportSmokeScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesFileIoImportSmokeVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.ScreenshotTour.cs",
            "MainWindow.ScreenshotTour.FileIoImportSmoke.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_FILE_IO_IMPORT_SMOKE_TOUR");
        source.Should().Contain("file-io-import-smoke-tour");
        source.Should().Contain("file_io_import_smoke_tour_manifest.json");
        source.Should().Contain("CaptureFileIoImportSmokeTourAsync");
        source.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, xlsxSaveAdapter))");
        source.Should().Contain("await OpenFileAsync(savedWorkbookPath)");
        source.Should().Contain("new ImportSheetCommand(_currentSheetId, destination, imported.Sheets[0])");
        source.Should().Contain("WorkbookImportFailurePlanner.FromException");
        source.Should().Contain("ShowOwnedMessage(diagnostic.UserMessage, caption, MessageBoxButton.OK, MessageBoxImage.Error)");
        source.Should().Contain("new ExportOptionsDialog(hasSelection: true, initialPdfLanguage: _options.PdfExportLanguage, format)");
        source.Should().Contain("FindOwnedNativeWindow(owner, caption)");
        source.Should().Contain("CaptureNativeWindow(dialogHandle, outputDir, fileName)");
        source.Should().Contain("no global mouse, keyboard, native OpenFileDialog, native SaveFileDialog, or screen-wide CopyFromScreen automation is used");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.FileIoImportSmokeTourManifest");
        source.Should().Contain("freex_file_io_import_smoke_saved_xlsx_title_status");
        source.Should().Contain("freex_file_io_import_smoke_reopened_xlsx_grid");
        source.Should().Contain("freex_file_io_import_smoke_imported_csv_grid");
        source.Should().Contain("freex_file_io_import_smoke_imported_txt_grid");
        source.Should().Contain("freex_file_io_import_smoke_import_warning");
        source.Should().Contain("freex_file_io_import_smoke_export_pdf_options");
        source.Should().Contain("freex_file_io_import_smoke_export_xps_options");
        source.Should().Contain("UI-CAT-DATA-001A");
        source.Should().Contain("UI-CAT-FILE-002C");

        catalog.Should().Contain("FREEX_FILE_IO_IMPORT_SMOKE_TOUR=1");
        catalog.Should().Contain("screenshots/file-io-import-smoke-tour/");
        catalog.Should().Contain("file_io_import_smoke_tour_manifest.json");
        catalog.Should().Contain("freex_file_io_import_smoke_saved_xlsx_title_status.png");
        catalog.Should().Contain("freex_file_io_import_smoke_imported_csv_grid.png");
        catalog.Should().Contain("freex_file_io_import_smoke_export_pdf_options.png");
    }
}
