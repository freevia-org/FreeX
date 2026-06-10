using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ChartObjectSelectionScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesChartObjectSelectionAndPickerEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.ChartObjectSelection.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_CHART_OBJECT_SELECTION_TOUR");
        dispatcherSource.Should().Contain("ChartObjectSelectionTourOutputDirectoryName = \"chart-object-selection-tour\"");
        dispatcherSource.Should().Contain("ChartObjectSelectionTourSavedWorkbookFileName = \"freex_chart_object_selection_saved.fxl\"");
        dispatcherSource.Should().Contain("CaptureChartObjectSelectionTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(ChartObjectSelectionTourManifest))]");

        tourSource.Should().Contain("SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Chart");
        tourSource.Should().Contain("new SelectDataSourceDialog(");
        tourSource.Should().Contain("new ChangeChartTypeDialog(");
        tourSource.Should().Contain("new ChartStyleDialog(");
        tourSource.Should().Contain("new ChartTitlesDialog(");
        tourSource.Should().Contain("new ChartAreaLegendDialog(");
        tourSource.Should().Contain("OnGridContextMenuRequested(context.Shape.Anchor");
        tourSource.Should().Contain("new SelectionPaneDialog(SelectionPanePlanner.BuildItems(context.Sheet))");
        tourSource.Should().Contain("new RenameSelectionPaneObjectCommand(");
        tourSource.Should().Contain("new SetSelectionPaneObjectVisibilityCommand(");
        tourSource.Should().Contain("new MoveSelectionPaneObjectCommand(");
        tourSource.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        tourSource.Should().Contain("OpenFileAsync(savedWorkbookPath)");
        tourSource.Should().Contain("HitTestOnlyGaps");
        tourSource.Should().Contain("Chart area, plot area, series, point, axis, title, and legend subtarget selection");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.ChartObjectSelectionTourManifest");

        tourSource.Should().Contain("freex_chart_object_selection_chart_design_handles");
        tourSource.Should().Contain("freex_chart_object_selection_chart_format_handles");
        tourSource.Should().Contain("freex_chart_object_selection_select_data_dialog");
        tourSource.Should().Contain("freex_chart_object_selection_change_chart_type_dialog");
        tourSource.Should().Contain("freex_chart_object_selection_shape_context_menu");
        tourSource.Should().Contain("freex_chart_object_selection_selection_pane_arranged");
        tourSource.Should().Contain("freex_chart_object_selection_reopened_chart_handles");

        catalog.Should().Contain("FREEX_CHART_OBJECT_SELECTION_TOUR=1");
        catalog.Should().Contain("screenshots/chart-object-selection-tour/");
        catalog.Should().Contain("chart_object_selection_tour_manifest.json");
        catalog.Should().Contain("freex_chart_object_selection_chart_design_handles.png");
        catalog.Should().Contain("freex_chart_object_selection_shape_context_menu.png");
        catalog.Should().Contain("freex_chart_object_selection_saved.fxl");
        catalog.Should().Contain("physical hit-test-only gaps");
    }
}
