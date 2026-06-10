using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WorksheetContextSubmittedScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesWorksheetContextSubmittedWorkflowEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.ScreenshotTour.cs",
            "MainWindow.ScreenshotTour.WorksheetContextSubmitted.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_WORKSHEET_CONTEXT_SUBMITTED_TOUR");
        source.Should().Contain("WorksheetContextSubmittedTourOutputDirectoryName = \"worksheet-context-submitted-tour\"");
        source.Should().Contain("WorksheetContextSubmittedTourSavedWorkbookFileName = \"freex_worksheet_context_submitted_saved.fxl\"");
        source.Should().Contain("worksheet_context_submitted_tour_manifest.json");
        source.Should().Contain("CaptureWorksheetContextSubmittedTourAsync");
        source.Should().Contain("ExecuteWorksheetContextSubmittedAction");
        source.Should().Contain("WorksheetContextMenuAction.DeleteNote");
        source.Should().Contain("WorksheetContextMenuAction.ResolveComment");
        source.Should().Contain("WorksheetContextMenuAction.RemoveHyperlinks");
        source.Should().Contain("WorksheetContextMenuAction.ClearContents");
        source.Should().Contain("WorksheetContextMenuAction.InsertRowAbove");
        source.Should().Contain("WorksheetContextMenuAction.DeleteColumns");
        source.Should().Contain("CommandBus.ExecuteRepeatable(ClearContentsCommand on protected locked cell)");
        source.Should().Contain("ExecuteUndo -> ExecuteRedo");
        source.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        source.Should().Contain("OpenFileAsync(savedWorkbookPath)");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.WorksheetContextSubmittedTourManifest");
        source.Should().Contain("freex_worksheet_context_submitted_delete_note_result");
        source.Should().Contain("freex_worksheet_context_submitted_resolve_comment_result");
        source.Should().Contain("freex_worksheet_context_submitted_remove_hyperlink_result");
        source.Should().Contain("freex_worksheet_context_submitted_clear_contents_result");
        source.Should().Contain("freex_worksheet_context_submitted_insert_row_above_result");
        source.Should().Contain("freex_worksheet_context_submitted_delete_column_result");
        source.Should().Contain("freex_worksheet_context_submitted_protected_clear_blocked");
        source.Should().Contain("freex_worksheet_context_submitted_undo_restored_delete_column");
        source.Should().Contain("freex_worksheet_context_submitted_redo_reapplied_delete_column");
        source.Should().Contain("freex_worksheet_context_submitted_reopened_persistence_result");
        source.Should().Contain("planner still does not disable protected locked-cell menu items");

        catalog.Should().Contain("FREEX_WORKSHEET_CONTEXT_SUBMITTED_TOUR=1");
        catalog.Should().Contain("screenshots/worksheet-context-submitted-tour/");
        catalog.Should().Contain("worksheet_context_submitted_tour_manifest.json");
        catalog.Should().Contain("freex_worksheet_context_submitted_clear_contents_result.png");
        catalog.Should().Contain("freex_worksheet_context_submitted_insert_row_above_result.png");
        catalog.Should().Contain("freex_worksheet_context_submitted_delete_column_result.png");
        catalog.Should().Contain("freex_worksheet_context_submitted_protected_clear_blocked.png");
        catalog.Should().Contain("freex_worksheet_context_submitted_reopened_persistence_result.png");
        catalog.Should().Contain("freex_worksheet_context_submitted_saved.fxl");
    }
}
