using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PivotAdvancedWorkflowsScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesPivotAdvancedWorkflowPersistenceEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.PivotAdvancedWorkflows.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_PIVOT_ADVANCED_WORKFLOWS_TOUR");
        dispatcherSource.Should().Contain("PivotAdvancedWorkflowsTourOutputDirectoryName = \"pivot-advanced-workflows-tour\"");
        dispatcherSource.Should().Contain("PivotAdvancedWorkflowsTourSavedWorkbookFileName = \"freex_pivot_advanced_workflows_saved.xlsx\"");
        dispatcherSource.Should().Contain("CapturePivotAdvancedWorkflowsTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(PivotAdvancedWorkflowsTourManifest))]");

        tourSource.Should().Contain("EnsurePivotAdvancedWorkflowsTourContext");
        tourSource.Should().Contain("new AddPivotTableCommand(");
        tourSource.Should().Contain("MoveSelectedPivotField(PivotFieldBucket.Rows)");
        tourSource.Should().Contain("new PivotLabelFilterDialog(0)");
        tourSource.Should().Contain("new PivotValueFilterDialog(0)");
        tourSource.Should().Contain("new ConfigurePivotTableViewCommand(");
        tourSource.Should().Contain("new PivotValueFieldSettingsDialog(context.PivotTable.DataFields.First(), headers)");
        tourSource.Should().Contain("Avg Sales % Grand Total");
        tourSource.Should().Contain("new ClearPivotTableViewCommand");
        tourSource.Should().Contain("new ChangePivotTableSourceCommand");
        tourSource.Should().Contain("new RefreshPivotTableCommand");
        tourSource.Should().Contain("new AddPivotChartCommand");
        tourSource.Should().Contain("CreatePivotFieldContextMenu()");
        tourSource.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        tourSource.Should().Contain("OpenFileAsync(savedWorkbookPath)");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.PivotAdvancedWorkflowsTourManifest");

        tourSource.Should().Contain("freex_pivot_advanced_seeded_analyze_layout");
        tourSource.Should().Contain("freex_pivot_advanced_field_layout_mutated");
        tourSource.Should().Contain("freex_pivot_advanced_label_filter_dialog");
        tourSource.Should().Contain("freex_pivot_advanced_value_filter_dialog");
        tourSource.Should().Contain("freex_pivot_advanced_label_value_filters_submitted");
        tourSource.Should().Contain("freex_pivot_advanced_value_field_settings_result");
        tourSource.Should().Contain("freex_pivot_advanced_clear_select_refresh_source_result");
        tourSource.Should().Contain("freex_pivot_advanced_pivotchart_field_button_menu");
        tourSource.Should().Contain("freex_pivot_advanced_reopened_persisted_pivot");
        tourSource.Should().Contain("physical pointer drag/drop remains a separate foreground-only gap");
        tourSource.Should().Contain("Label/value filter and value-field setting submissions are deterministic command submissions");

        catalog.Should().Contain("FREEX_PIVOT_ADVANCED_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("screenshots/pivot-advanced-workflows-tour/");
        catalog.Should().Contain("pivot_advanced_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_pivot_advanced_workflows_saved.xlsx");
        catalog.Should().Contain("freex_pivot_advanced_reopened_persisted_pivot.png");
    }
}
