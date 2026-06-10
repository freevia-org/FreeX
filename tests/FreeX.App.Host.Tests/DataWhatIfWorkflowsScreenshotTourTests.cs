using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DataWhatIfWorkflowsScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesDataWhatIfWorkflowEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.DataWhatIfWorkflows.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_DATA_WHAT_IF_WORKFLOWS_TOUR");
        dispatcherSource.Should().Contain("DataWhatIfWorkflowsTourOutputDirectoryName = \"data-what-if-workflows-tour\"");
        dispatcherSource.Should().Contain("CaptureDataWhatIfWorkflowsTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(DataWhatIfWorkflowsTourManifest))]");

        tourSource.Should().Contain("GoalSeekService.Seek(");
        tourSource.Should().Contain("new GoalSeekCommand(");
        tourSource.Should().Contain("new GoalSeekDialog(");
        tourSource.Should().Contain("new GoalSeekStatusDialog(");
        tourSource.Should().Contain("new SaveScenarioCommand(");
        tourSource.Should().Contain("new ScenarioManagerDialog(");
        tourSource.Should().Contain("new ApplyScenarioCommand(\"Upside Plan\")");
        tourSource.Should().Contain("new ScenarioSummaryReportCommand(");
        tourSource.Should().Contain("DataTablePlanner.CreatePlan(");
        tourSource.Should().Contain("oneVariablePlan.CreateCommand()");
        tourSource.Should().Contain("twoVariablePlan.CreateCommand()");

        tourSource.Should().Contain("freex_data_what_if_workflows_seeded_formula_grid");
        tourSource.Should().Contain("freex_data_what_if_workflows_goal_seek_dialog");
        tourSource.Should().Contain("freex_data_what_if_workflows_goal_seek_status_success");
        tourSource.Should().Contain("freex_data_what_if_workflows_goal_seek_result");
        tourSource.Should().Contain("freex_data_what_if_workflows_scenario_manager_dialog");
        tourSource.Should().Contain("freex_data_what_if_workflows_scenario_show_result");
        tourSource.Should().Contain("freex_data_what_if_workflows_scenario_summary_report");
        tourSource.Should().Contain("freex_data_what_if_workflows_data_table_dialog");
        tourSource.Should().Contain("freex_data_what_if_workflows_data_table_one_variable_result");
        tourSource.Should().Contain("freex_data_what_if_workflows_data_table_two_variable_result");
        tourSource.Should().Contain("planned-but-blocked");
        tourSource.Should().Contain("RenderTargetBitmap-window-and-dialogs-with-real-what-if-commands");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.DataWhatIfWorkflowsTourManifest");
        tourSource.Should().Contain("UI-CAT-DATA-002");
        tourSource.Should().Contain("UI-CAT-DIALOG-001A");
        tourSource.Should().Contain("UI-CMD-DATA-006");

        catalog.Should().Contain("FREEX_DATA_WHAT_IF_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("screenshots/data-what-if-workflows-tour/");
        catalog.Should().Contain("data_what_if_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_data_what_if_workflows_goal_seek_result.png");
        catalog.Should().Contain("freex_data_what_if_workflows_scenario_summary_report.png");
        catalog.Should().Contain("freex_data_what_if_workflows_data_table_two_variable_result.png");
    }
}
