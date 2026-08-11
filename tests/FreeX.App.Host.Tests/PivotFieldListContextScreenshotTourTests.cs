using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PivotFieldListContextScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesPivotFieldListContextVisualEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.PivotFieldListContext.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_PIVOT_FIELD_LIST_CONTEXT_TOUR");
        dispatcherSource.Should().Contain("PivotFieldListContextTourOutputDirectoryName = \"pivot-field-list-context-tour\"");
        dispatcherSource.Should().Contain("CapturePivotFieldListContextTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(PivotFieldListContextTourManifest))]");

        tourSource.Should().Contain("EnsurePivotFieldListContextTourContext");
        tourSource.Should().Contain("new AddPivotTableCommand(");
        tourSource.Should().Contain("new ConfigurePivotTableLayoutCommand(");
        tourSource.Should().Contain("PivotFieldListDeferLayoutCheckBox.IsChecked = true;");
        tourSource.Should().Contain("MoveSelectedPivotField(PivotFieldDropZone.Rows)");
        tourSource.Should().Contain("PivotFieldListUpdateBtn_Click(PivotFieldListUpdateBtn");
        tourSource.Should().Contain("PivotValuesList.ContextMenu");
        tourSource.Should().Contain("new PivotValueFieldSettingsDialog(dataField, context.SourceHeaders)");
        tourSource.Should().Contain("PivotSourceContext.ReadItems(_workbook, context.Sheet, context.PivotTable");
        tourSource.Should().NotContain("ReadPivotFieldItems(");

        tourSource.Should().Contain("freex_pivot_field_list_analyze_field_list");
        tourSource.Should().Contain("freex_pivot_field_list_design_field_list");
        tourSource.Should().Contain("freex_pivot_field_list_deferred_search_buttons_checks");
        tourSource.Should().Contain("freex_pivot_field_list_context_menu_opened");
        tourSource.Should().Contain("freex_pivot_value_field_settings_dialog");
        tourSource.Should().Contain("freex_pivot_field_filter_dialog");
        tourSource.Should().Contain("freex_pivot_field_list_result_grid");
        tourSource.Should().Contain("UI-CAT-CONTEXT-003B");
        tourSource.Should().Contain("UI-CAT-CONTEXT-003C");
        tourSource.Should().Contain("physical pointer drag evidence remains open");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.PivotFieldListContextTourManifest");

        catalog.Should().Contain("FREEX_PIVOT_FIELD_LIST_CONTEXT_TOUR=1");
        catalog.Should().Contain("screenshots/pivot-field-list-context-tour/");
        catalog.Should().Contain("pivot_field_list_context_tour_manifest.json");
    }
}
