using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class TableWorkflowsScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesTableWorkflowTotalsPersistenceEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.TableWorkflows.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_TABLE_WORKFLOWS_TOUR");
        dispatcherSource.Should().Contain("TableWorkflowsTourOutputDirectoryName = \"table-workflows-tour\"");
        dispatcherSource.Should().Contain("TableWorkflowsTourSavedWorkbookFileName = \"freex_table_workflows_saved.xlsx\"");
        dispatcherSource.Should().Contain("CaptureTableWorkflowsTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(TableWorkflowsTourManifest))]");

        tourSource.Should().Contain("new CreateTableDialog(");
        tourSource.Should().Contain("new CreateStyledStructuredTableCommand(");
        tourSource.Should().Contain("new RenameStructuredTableCommand(");
        tourSource.Should().Contain("new ApplyStructuredTableFiltersCommand(");
        tourSource.Should().Contain("new SetStructuredTableTotalsRowCommand(");
        tourSource.Should().Contain("new ApplyStructuredTableStyleCommand(");
        tourSource.Should().Contain("TotalsRowFunction: \"sum\"");
        tourSource.Should().Contain("TotalsRowFunction: \"count\"");
        tourSource.Should().Contain("TableStyleMedium4");
        tourSource.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        tourSource.Should().Contain("await OpenFileAsync(savedWorkbookPath);");
        tourSource.Should().Contain("freex_table_workflows_create_table_submitted_result");
        tourSource.Should().Contain("freex_table_workflows_filter_totals_style_result");
        tourSource.Should().Contain("freex_table_workflows_reopened_persisted_table");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.TableWorkflowsTourManifest");
        tourSource.Should().Contain("Totals function dropdown selection and filter dropdown selection remain seeded metadata");

        catalog.Should().Contain("FREEX_TABLE_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("screenshots/table-workflows-tour/");
        catalog.Should().Contain("table_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_table_workflows_create_table_submitted_result.png");
        catalog.Should().Contain("freex_table_workflows_filter_totals_style_result.png");
        catalog.Should().Contain("freex_table_workflows_reopened_persisted_table.png");
        catalog.Should().Contain("freex_table_workflows_saved.xlsx");
        catalog.Should().Contain("TableStyleMedium4");
        catalog.Should().Contain("totals-function metadata is seeded in-process");
    }
}
