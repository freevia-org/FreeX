using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SheetTabWorkflowsScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesSheetTabWorkflowAndPersistenceEvidence()
    {
        var startupSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.Startup.cs");
        var parityCaptureOwnershipSource = WorkspaceFileLocator.ReadAllText(
            "tools", "FreeX.ParityCapture.Wpf", "Capture", "MainWindow.ParityCaptureOwnership.cs");
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.SheetTabWorkflows.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        startupSource.Should().Contain("StartExternalLoadedWorkflows();");
        startupSource.Should().NotContain("TryStartSheetTabWorkflowsTour();");
        parityCaptureOwnershipSource.Should().Contain("TryStartSheetTabWorkflowsTour();");
        dispatcherSource.Should().Contain("SheetTabWorkflowsTourOutputDirectoryName = \"sheet-tab-workflows-tour\"");
        dispatcherSource.Should().Contain("SheetTabWorkflowsTourManifestFileName = \"sheet_tab_workflows_tour_manifest.json\"");
        dispatcherSource.Should().Contain("SheetTabWorkflowsTourSavedWorkbookFileName = \"freex_sheet_tab_workflows_persisted.xlsx\"");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(SheetTabWorkflowsTourManifest))]");

        tourSource.Should().Contain("FREEX_SHEET_TAB_WORKFLOWS_TOUR");
        tourSource.Should().Contain("CaptureSheetTabWorkflowsWindowAsync");
        tourSource.Should().Contain("InsertNewSheet() -> TryExecuteRepeatableCommand(AddSheetCommand)");
        tourSource.Should().Contain("new RenameSheetCommand(insertedSheet.Id, \"Submitted Plan\")");
        tourSource.Should().Contain("new DuplicateSheetCommand(moveCopySource.Id)");
        tourSource.Should().Contain("new CompositeWorkbookCommand(");
        tourSource.Should().Contain("new MoveSheetCommand(copyIndex, targetIndex)");
        tourSource.Should().Contain("Single CompositeWorkbookCommand for Move or Copy create-copy");
        tourSource.Should().NotContain("composite DuplicateSheetCommand and MoveSheetCommand route");
        tourSource.Should().NotContain("CompositeWorkbookCommand for DuplicateSheetCommand plus MoveSheetCommand");
        tourSource.Should().Contain("new SetSheetTabColorCommand(insertedSheet.Id, new CellColor(255, 192, 0))");
        tourSource.Should().Contain("new SetSheetHiddenCommand(context.ArchiveSheet.Id, hidden: true)");
        tourSource.Should().Contain("new SetSheetHiddenCommand(context.ArchiveSheet.Id, hidden: false)");
        tourSource.Should().Contain("SheetCtxSelectAllSheets_Click(this, new RoutedEventArgs())");
        tourSource.Should().Contain("SheetCtxUngroupSheets_Click(this, new RoutedEventArgs())");
        tourSource.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        tourSource.Should().Contain("OpenFileAsync(savedWorkbookPath)");
        tourSource.Should().Contain("SavedWorkbookPath");
        tourSource.Should().Contain("PlannedCaptureKeys");
        tourSource.Should().Contain("CommandRoutesUsed");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.SheetTabWorkflowsTourManifest");
        tourSource.Should().Contain("freex_sheet_tab_workflows_insert_sheet_result");
        tourSource.Should().Contain("freex_sheet_tab_workflows_rename_submitted_result");
        tourSource.Should().Contain("freex_sheet_tab_workflows_move_or_copy_result");
        tourSource.Should().Contain("freex_sheet_tab_workflows_tab_color_applied");
        tourSource.Should().Contain("freex_sheet_tab_workflows_hide_sheet_result");
        tourSource.Should().Contain("freex_sheet_tab_workflows_unhide_sheet_result");
        tourSource.Should().Contain("freex_sheet_tab_workflows_select_all_sheets_result");
        tourSource.Should().Contain("freex_sheet_tab_workflows_ungroup_sheets_result");
        tourSource.Should().Contain("freex_sheet_tab_workflows_reopened_persistence_result");
        tourSource.Should().Contain("no global mouse, double-click, drag, right-click, keytip, access-key, or UI Automation input is synthesized");

        catalog.Should().Contain("FREEX_SHEET_TAB_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("screenshots/sheet-tab-workflows-tour/");
        catalog.Should().Contain("sheet_tab_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_sheet_tab_workflows_persisted.xlsx");
        catalog.Should().Contain("freex_sheet_tab_workflows_reopened_persistence_result.png");
        catalog.Should().Contain("SaveWorkbookToTargetAsync");
        catalog.Should().Contain("OpenFileAsync");
    }
}
