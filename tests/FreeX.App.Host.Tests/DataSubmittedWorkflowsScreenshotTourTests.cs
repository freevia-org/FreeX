using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DataSubmittedWorkflowsScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesDataSubmittedWorkflowEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.DataSubmittedWorkflows.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_DATA_SUBMITTED_WORKFLOWS_TOUR");
        dispatcherSource.Should().Contain("DataSubmittedWorkflowsTourOutputDirectoryName = \"data-submitted-workflows-tour\"");
        dispatcherSource.Should().Contain("CaptureDataSubmittedWorkflowsTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(DataSubmittedWorkflowsTourManifest))]");

        tourSource.Should().Contain("EnsureDataSubmittedWorkflowsTourContext");
        tourSource.Should().Contain("new SortCommand(");
        tourSource.Should().Contain("_filterWorkflowSession.PlanAllowedValues(");
        tourSource.Should().Contain("TryExecuteAutoFilterMutation(filterPlan)");
        tourSource.Should().Contain("ReapplyAutoFilter()");
        tourSource.Should().Contain("new AdvancedFilterCommand(");
        tourSource.Should().Contain("TextToColumnsDialog.CreateResult(");
        tourSource.Should().Contain("CreateTextToColumnsCommand(");
        tourSource.Should().Contain("new SetDataValidationCommand(");
        tourSource.Should().Contain("CircleInvalidDataMenuItem_Click(this, new RoutedEventArgs())");
        tourSource.Should().Contain("new SubtotalCommand(");
        tourSource.Should().Contain("new RemoveDuplicateRowsCommand(");

        tourSource.Should().Contain("freex_data_submitted_workflows_sort_before");
        tourSource.Should().Contain("freex_data_submitted_workflows_sort_after_amount_desc");
        tourSource.Should().Contain("freex_data_submitted_workflows_autofilter_applied_open");
        tourSource.Should().Contain("freex_data_submitted_workflows_autofilter_cleared");
        tourSource.Should().Contain("freex_data_submitted_workflows_autofilter_reapplied_open");
        tourSource.Should().Contain("freex_data_submitted_workflows_advanced_filter_copy_to_result");
        tourSource.Should().Contain("freex_data_submitted_workflows_text_to_columns_result");
        tourSource.Should().Contain("freex_data_submitted_workflows_data_validation_invalid_selected");
        tourSource.Should().Contain("freex_data_submitted_workflows_subtotal_result");
        tourSource.Should().Contain("freex_data_submitted_workflows_remove_duplicates_result");
        tourSource.Should().Contain("Data Validation dropdown popup commit");
        tourSource.Should().Contain("planned-but-blocked");
        tourSource.Should().Contain("RenderTargetBitmap-window-full-with-real-workbook-commands");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.DataSubmittedWorkflowsTourManifest");
        tourSource.Should().Contain("UI-CMD-DATA-002");
        tourSource.Should().Contain("UI-CMD-DATA-003");
        tourSource.Should().Contain("UI-CMD-DATA-004");
        tourSource.Should().Contain("UI-CMD-DATA-005");
        tourSource.Should().Contain("UI-CMD-DATA-007");

        catalog.Should().Contain("FREEX_DATA_SUBMITTED_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("screenshots/data-submitted-workflows-tour/");
        catalog.Should().Contain("data_submitted_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_data_submitted_workflows_autofilter_reapplied_open.png");
        catalog.Should().Contain("freex_data_submitted_workflows_remove_duplicates_result.png");
    }
}
