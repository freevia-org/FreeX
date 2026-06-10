using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeStylePersistenceScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesHomeStylePersistenceEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.HomeStylePersistence.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_HOME_STYLE_PERSISTENCE_TOUR");
        dispatcherSource.Should().Contain("HomeStylePersistenceTourOutputDirectoryName = \"home-style-persistence-tour\"");
        dispatcherSource.Should().Contain("HomeStylePersistenceTourSavedWorkbookFileName = \"freex_home_style_persistence_saved.fxl\"");
        dispatcherSource.Should().Contain("CaptureHomeStylePersistenceTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(HomeStylePersistenceTourManifest))]");

        tourSource.Should().Contain("new MergeCellsCommand(context.Sheet.Id, context.TitleRange)");
        tourSource.Should().Contain("new ApplyStyleCommand(");
        tourSource.Should().Contain("BorderShortcutService.GetAllBorderDiff(BorderStyle.Thin");
        tourSource.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Heading2");
        tourSource.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Good");
        tourSource.Should().Contain("new ApplyConditionalFormatCommand(");
        tourSource.Should().Contain("CfRuleType.CellValue");
        tourSource.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        tourSource.Should().Contain("OpenFileAsync(savedWorkbookPath)");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.HomeStylePersistenceTourManifest");

        tourSource.Should().Contain("freex_home_style_persistence_seeded_grid");
        tourSource.Should().Contain("freex_home_style_persistence_applied_home_style_result");
        tourSource.Should().Contain("freex_home_style_persistence_saved_native_workbook");
        tourSource.Should().Contain("freex_home_style_persistence_reopened_grid");
        tourSource.Should().Contain("Foreground-only dropdown/keytip gaps");
        tourSource.Should().Contain("Persistence is proven for the native FreeX .fxl adapter");

        catalog.Should().Contain("FREEX_HOME_STYLE_PERSISTENCE_TOUR=1");
        catalog.Should().Contain("screenshots/home-style-persistence-tour/");
        catalog.Should().Contain("home_style_persistence_tour_manifest.json");
        catalog.Should().Contain("freex_home_style_persistence_reopened_grid.png");
        catalog.Should().Contain("freex_home_style_persistence_saved.fxl");
    }
}
