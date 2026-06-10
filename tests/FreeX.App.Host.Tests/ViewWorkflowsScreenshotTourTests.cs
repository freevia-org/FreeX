using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ViewWorkflowsScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesViewWorkflowSubmittedAndPersistenceEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.ViewWorkflows.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_VIEW_WORKFLOWS_TOUR");
        dispatcherSource.Should().Contain("ViewWorkflowsTourOutputDirectoryName = \"view-workflows-tour\"");
        dispatcherSource.Should().Contain("ViewWorkflowsTourSavedWorkbookFileName = \"freex_view_workflows_saved.fxl\"");
        dispatcherSource.Should().Contain("CaptureViewWorkflowsTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(ViewWorkflowsTourManifest))]");

        tourSource.Should().Contain("new SaveCustomViewCommand(ViewWorkflowsTourCustomViewName");
        tourSource.Should().Contain("new ApplyCustomViewCommand(ViewWorkflowsTourCustomViewName)");
        tourSource.Should().Contain("new DeleteCustomViewCommand(ViewWorkflowsTourCustomViewName)");
        tourSource.Should().Contain("new SetWorksheetViewModeCommand(sheet.Id, WorksheetViewMode.PageBreakPreview)");
        tourSource.Should().Contain("new SetWorksheetViewOptionsCommand(sheet.Id, showGridlines: false");
        tourSource.Should().Contain("new SetFreezePanesCommand(sheet.Id, 3, 2)");
        tourSource.Should().Contain("new SetSplitPanesCommand(sheet.Id, 8, 4)");
        tourSource.Should().Contain("new SetWorkbookWindowArrangementCommand(WorkbookWindowArrangement.Horizontal)");
        tourSource.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        tourSource.Should().Contain("OpenFileAsync(savedWorkbookPath)");

        tourSource.Should().Contain("freex_view_workflows_custom_view_save_result");
        tourSource.Should().Contain("freex_view_workflows_split_arrange_result");
        tourSource.Should().Contain("freex_view_workflows_custom_view_show_applied_result");
        tourSource.Should().Contain("freex_view_workflows_view_toggle_save_ready");
        tourSource.Should().Contain("freex_view_workflows_saved_native_workbook");
        tourSource.Should().Contain("freex_view_workflows_reopened_view_toggle_persistence");
        tourSource.Should().Contain("freex_view_workflows_reopened_custom_view_show_result");
        tourSource.Should().Contain("freex_view_workflows_custom_view_delete_result_dialog");
        tourSource.Should().Contain("physical-split-divider-drag");
        tourSource.Should().Contain("new-window-side-by-side-os-layout");
        tourSource.Should().Contain("synchronous-scrolling-foreground-proof");
        tourSource.Should().Contain("planned-but-blocked");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.ViewWorkflowsTourManifest");
        tourSource.Should().Contain("UI-CAT-VIEW-001");
        tourSource.Should().Contain("UI-CAT-VIEW-002");
        tourSource.Should().Contain("UI-CAT-STATUS-003A-E");

        catalog.Should().Contain("FREEX_VIEW_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("screenshots/view-workflows-tour/");
        catalog.Should().Contain("view_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_view_workflows_reopened_view_toggle_persistence.png");
        catalog.Should().Contain("freex_view_workflows_saved.fxl");
    }
}
