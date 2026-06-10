using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PivotOptionsSlicerScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesPivotOptionsSlicerTimelinePivotChartEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.PivotOptionsSlicer.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_PIVOT_OPTIONS_SLICER_TOUR");
        dispatcherSource.Should().Contain("PivotOptionsSlicerTourOutputDirectoryName = \"pivot-options-slicer-tour\"");
        dispatcherSource.Should().Contain("CapturePivotOptionsSlicerTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(PivotOptionsSlicerTourManifest))]");

        tourSource.Should().Contain("EnsurePivotOptionsSlicerTourContext");
        tourSource.Should().Contain("new AddPivotTableCommand(");
        tourSource.Should().Contain("new ConfigurePivotTableLayoutCommand(");
        tourSource.Should().Contain("DateTimeValue.FromDateTime");
        tourSource.Should().Contain("new PivotTableOptionsDialog(context.PivotTable, cache)");
        tourSource.Should().Contain("new PivotStyleGalleryDialog(context.PivotTable.StyleName)");
        tourSource.Should().Contain("new InsertSlicerDialog(context.SourceHeaders, \"Region\")");
        tourSource.Should().Contain("new InsertTimelineDialog(context.SourceHeaders, \"Date\")");
        tourSource.Should().Contain("new AddSlicerCommand(\"Region Slicer\"");
        tourSource.Should().Contain("new AddTimelineCommand(\"Date Timeline\"");
        tourSource.Should().Contain("new SetSlicerSelectionCommand(\"Region Slicer\", [\"North\", \"West\"])");
        tourSource.Should().Contain("new SetTimelineRangeCommand(\"Date Timeline\", \"2026-01-01\", \"2026-03-31\")");
        tourSource.Should().Contain("new AddPivotChartCommand(");
        tourSource.Should().Contain("new PivotChartTypeDialog(ChartType.Column)");
        tourSource.Should().Contain("new PivotChartOptionsDialog(chart)");
        tourSource.Should().Contain("PivotUiPlanner.ResolvePivotChartFieldButtonCaption");
        tourSource.Should().Contain("CreatePivotFieldContextMenu()");

        tourSource.Should().Contain("freex_pivot_options_slicer_analyze_selection");
        tourSource.Should().Contain("freex_pivot_options_dialog_display_style_options");
        tourSource.Should().Contain("freex_pivot_design_style_options_surface");
        tourSource.Should().Contain("freex_pivot_style_gallery_dialog");
        tourSource.Should().Contain("freex_pivot_insert_slicer_dialog");
        tourSource.Should().Contain("freex_pivot_insert_timeline_dialog");
        tourSource.Should().Contain("freex_pivot_slicer_timeline_pane_filtered");
        tourSource.Should().Contain("freex_pivotchart_type_dialog");
        tourSource.Should().Contain("freex_pivotchart_options_dialog");
        tourSource.Should().Contain("freex_pivotchart_field_button_menu_opened");
        tourSource.Should().Contain("UI-CAT-INSERT-001B");
        tourSource.Should().Contain("UI-CAT-INSERT-001C");
        tourSource.Should().Contain("UI-CAT-INSERT-001E");
        tourSource.Should().Contain("UI-CMD-INSERT-011");
        tourSource.Should().Contain("UI-CMD-INSERT-013");
        tourSource.Should().Contain("UI-CMD-INSERT-014");
        tourSource.Should().Contain("rendered chart-field-button annotations and hit-test pointer opening remain outside");
        tourSource.Should().Contain("foreground mouse, keytip, dialog access-key, UIA Invoke");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.PivotOptionsSlicerTourManifest");

        catalog.Should().Contain("FREEX_PIVOT_OPTIONS_SLICER_TOUR=1");
        catalog.Should().Contain("screenshots/pivot-options-slicer-tour/");
        catalog.Should().Contain("pivot_options_slicer_tour_manifest.json");
        catalog.Should().Contain("freex_pivotchart_field_button_menu_opened.png");
    }
}
