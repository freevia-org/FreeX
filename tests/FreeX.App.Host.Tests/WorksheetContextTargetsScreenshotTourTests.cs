using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WorksheetContextTargetsScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesWorksheetContextTargetBreadthEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.WorksheetContextTargets.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_WORKSHEET_CONTEXT_TARGETS_TOUR");
        dispatcherSource.Should().Contain("WorksheetContextTargetsTourOutputDirectoryName = \"worksheet-context-targets-tour\"");
        dispatcherSource.Should().Contain("CaptureWorksheetContextTargetsTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(WorksheetContextTargetsTourManifest))]");

        tourSource.Should().Contain("EnsureWorksheetContextTargetsTourContext");
        tourSource.Should().Contain("new WorksheetAutoFilterModel(filterRange.ToString(), null)");
        tourSource.Should().Contain("new CreateStructuredTableCommand(");
        tourSource.Should().Contain("sheet.Comments[noteCell]");
        tourSource.Should().Contain("sheet.Hyperlinks[hyperlinkCell]");
        tourSource.Should().Contain("sheet.IsProtected = target.State == \"protected-locked-cell\";");
        tourSource.Should().Contain("OnGridHeaderContextMenuRequested");
        tourSource.Should().Contain("OnGridContextMenuRequested");
        tourSource.Should().Contain("ReadWorksheetContextTargetsMenuItems");
        tourSource.Should().Contain("ProtectedTargetStateSupported: false");

        tourSource.Should().Contain("freex_worksheet_context_target_normal_cell");
        tourSource.Should().Contain("freex_worksheet_context_target_normal_range");
        tourSource.Should().Contain("freex_worksheet_context_target_whole_row");
        tourSource.Should().Contain("freex_worksheet_context_target_whole_column");
        tourSource.Should().Contain("freex_worksheet_context_target_table_cell");
        tourSource.Should().Contain("freex_worksheet_context_target_autofilter_header");
        tourSource.Should().Contain("freex_worksheet_context_target_note_cell");
        tourSource.Should().Contain("freex_worksheet_context_target_hyperlink_cell");
        tourSource.Should().Contain("freex_worksheet_context_target_protected_locked_cell");
        tourSource.Should().Contain("captured-with-limitation");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.WorksheetContextTargetsTourManifest");

        catalog.Should().Contain("FREEX_WORKSHEET_CONTEXT_TARGETS_TOUR=1");
        catalog.Should().Contain("screenshots/worksheet-context-targets-tour/");
        catalog.Should().Contain("worksheet_context_targets_tour_manifest.json");
        catalog.Should().Contain("freex_worksheet_context_target_autofilter_header.png");
        catalog.Should().Contain("freex_worksheet_context_target_protected_locked_cell.png");
    }
}
