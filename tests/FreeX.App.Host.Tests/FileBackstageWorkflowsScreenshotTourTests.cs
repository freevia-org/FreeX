using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FileBackstageWorkflowsScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesFileBackstageWorkflowPersistenceEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.FileBackstageWorkflows.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_FILE_BACKSTAGE_WORKFLOWS_TOUR");
        dispatcherSource.Should().Contain("FileBackstageWorkflowsTourOutputDirectoryName = \"file-backstage-workflows-tour\"");
        dispatcherSource.Should().Contain("CaptureFileBackstageWorkflowsTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(FileBackstageWorkflowsTourManifest))]");

        tourSource.Should().Contain("CreateNewWorkbook()");
        tourSource.Should().Contain("BackstageRecentFileListPlanner.Build");
        tourSource.Should().Contain("MissingRecentFiltered");
        tourSource.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        tourSource.Should().Contain("await OpenFileAsync(savedWorkbookPath)");
        tourSource.Should().Contain("new PrintPreviewDialog(");
        tourSource.Should().Contain("PdfDocumentExporter.Save(");
        tourSource.Should().Contain("PdfReader.Open(request.Path, PdfDocumentOpenMode.Import)");
        tourSource.Should().Contain("native OpenFileDialog, native SaveFileDialog, native PrintDialog");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.FileBackstageWorkflowsTourManifest");
        tourSource.Should().Contain("freex_file_backstage_new_entry_focused");
        tourSource.Should().Contain("freex_file_backstage_new_workbook_result");
        tourSource.Should().Contain("freex_file_backstage_open_recent_filtered_list");
        tourSource.Should().Contain("freex_file_backstage_open_pinned_list");
        tourSource.Should().Contain("freex_file_backstage_save_as_native_dialog_guard");
        tourSource.Should().Contain("freex_file_backstage_saved_title_path_info");
        tourSource.Should().Contain("freex_file_backstage_reopened_workbook_title_path");
        tourSource.Should().Contain("freex_file_backstage_print_preview_summary");
        tourSource.Should().Contain("freex_file_backstage_export_entry_output_ready");
        tourSource.Should().Contain("freex_file_backstage_workflows_saved.xlsx");
        tourSource.Should().Contain("freex_file_backstage_workflows_export.pdf");

        catalog.Should().Contain("FREEX_FILE_BACKSTAGE_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("screenshots/file-backstage-workflows-tour/");
        catalog.Should().Contain("file_backstage_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_file_backstage_saved_title_path_info.png");
        catalog.Should().Contain("freex_file_backstage_reopened_workbook_title_path.png");
        catalog.Should().Contain("freex_file_backstage_print_preview_summary.png");
        catalog.Should().Contain("freex_file_backstage_workflows_saved.xlsx");
        catalog.Should().Contain("freex_file_backstage_workflows_export.pdf");
        catalog.Should().Contain("foreground-only");
    }
}
