using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeSubmittedWorkflowsScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesHomeSubmittedWorkflowVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.ScreenshotTour.cs",
            "MainWindow.ScreenshotTour.HomeSubmittedWorkflows.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_HOME_SUBMITTED_WORKFLOWS_TOUR");
        source.Should().Contain("home-submitted-workflows-tour");
        source.Should().Contain("home_submitted_workflows_tour_manifest.json");
        source.Should().Contain("CaptureHomeSubmittedWorkflowsTourAsync");
        source.Should().Contain("PasteCommandFactory.CreateInternalPasteCommand");
        source.Should().Contain("PasteSpecialContentKind.ValuesAndSourceFormatting");
        source.Should().Contain("new InsertRowsCommand(_currentSheetId, context.InsertRow, 1)");
        source.Should().Contain("new DeleteCellsCommand(_currentSheetId, context.DeleteCellsRange, DeleteCellsShiftDirection.Left)");
        source.Should().Contain("RowColumnSizingPlanner.CreateRowsHiddenCommand(_currentSheetId, context.HideRowRange, hidden: true)");
        source.Should().Contain("RowColumnSizingPlanner.CreateRowsHiddenCommand(_currentSheetId, context.HideRowRange, hidden: false)");
        source.Should().Contain("new ClearContentsCommand(_currentSheetId, context.ClearContentsRange)");
        source.Should().Contain("new ApplyStyleCommand(_currentSheetId, context.ClearFormatsRange, CellStyleDiffPlanner.ClearFormatsDiff())");
        source.Should().Contain("FindReplaceService.TryReplaceAll");
        source.Should().Contain("KeyboardCommandShortcut.RepeatLastAction/F4 -> ExecuteRepeatLast -> CommandBus.RepeatLast");
        source.Should().Contain("KeyboardCommandShortcut.Undo/Ctrl+Z -> ExecuteUndo -> CommandBus.Undo");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.HomeSubmittedWorkflowsTourManifest");
        source.Should().Contain("PlannedCaptureKeys");
        source.Should().Contain("CommandRoutesUsed");
        source.Should().Contain("freex_home_submitted_workflows_seeded_before");
        source.Should().Contain("freex_home_submitted_workflows_paste_special_values_source_formatting");
        source.Should().Contain("freex_home_submitted_workflows_insert_row_result");
        source.Should().Contain("freex_home_submitted_workflows_delete_cells_shift_left_result");
        source.Should().Contain("freex_home_submitted_workflows_hidden_row_result");
        source.Should().Contain("freex_home_submitted_workflows_unhidden_row_result");
        source.Should().Contain("freex_home_submitted_workflows_clear_formats_contents_result");
        source.Should().Contain("freex_home_submitted_workflows_find_replace_submitted_result");
        source.Should().Contain("freex_home_submitted_workflows_f4_repeat_clear_contents_result");
        source.Should().Contain("freex_home_submitted_workflows_undo_restored_repeat_target");
        source.Should().Contain("no global mouse, keyboard, keytip, OS clipboard, or screen capture input is used");
        source.Should().Contain("Excel-paired screenshots, save/reload persistence breadth, foreground mouse/keytip proof");

        catalog.Should().Contain("FREEX_HOME_SUBMITTED_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("screenshots/home-submitted-workflows-tour/");
        catalog.Should().Contain("home_submitted_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_home_submitted_workflows_paste_special_values_source_formatting.png");
        catalog.Should().Contain("freex_home_submitted_workflows_f4_repeat_clear_contents_result.png");
        catalog.Should().Contain("freex_home_submitted_workflows_undo_restored_repeat_target.png");
        catalog.Should().Contain("CommandBus.RepeatLast");
        catalog.Should().Contain("CommandBus.Undo");
    }
}
