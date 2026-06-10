using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class InsertObjectPersistenceScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesInsertObjectPersistenceAndHandleEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.InsertObjectPersistence.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_INSERT_OBJECT_PERSISTENCE_TOUR");
        dispatcherSource.Should().Contain("InsertObjectPersistenceTourOutputDirectoryName = \"insert-object-persistence-tour\"");
        dispatcherSource.Should().Contain("InsertObjectPersistenceTourSavedWorkbookFileName = \"freex_insert_object_persistence_saved.fxl\"");
        dispatcherSource.Should().Contain("CaptureInsertObjectPersistenceTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(InsertObjectPersistenceTourManifest))]");

        tourSource.Should().Contain("new SetHyperlinkCommand(");
        tourSource.Should().Contain("new SetThreadedCommentCommand(");
        tourSource.Should().Contain("new SetCommentCommand(");
        tourSource.Should().Contain("new AddDrawingShapeCommand(");
        tourSource.Should().Contain("new AddTextBoxCommand(");
        tourSource.Should().Contain("InsertObjectPlacementPlanner.CreateInsertPictureCommand(");
        tourSource.Should().Contain("SheetGrid.SelectedObjectId = objectId;");
        tourSource.Should().Contain("SheetGrid.SelectedObjectKind = kind;");
        tourSource.Should().Contain("freex_insert_object_persistence_selected_shape_handles");
        tourSource.Should().Contain("freex_insert_object_persistence_selected_text_box_handles");
        tourSource.Should().Contain("freex_insert_object_persistence_selected_picture_handles");
        tourSource.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        tourSource.Should().Contain("await OpenFileAsync(savedWorkbookPath);");
        tourSource.Should().Contain("freex_insert_object_persistence_reopened_context_state");
        tourSource.Should().Contain("freex_insert_object_persistence_reopened_picture_handles");
        tourSource.Should().Contain("blocked-foreground-guarded-not-opened");
        tourSource.Should().Contain("PlannedCaptureCount: plannedCaptures.Count");
        tourSource.Should().Contain("ActualCaptureCount: captures.Count");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.InsertObjectPersistenceTourManifest");

        catalog.Should().Contain("FREEX_INSERT_OBJECT_PERSISTENCE_TOUR=1");
        catalog.Should().Contain("screenshots/insert-object-persistence-tour/");
        catalog.Should().Contain("insert_object_persistence_tour_manifest.json");
        catalog.Should().Contain("freex_insert_object_persistence_selected_shape_handles.png");
        catalog.Should().Contain("freex_insert_object_persistence_selected_text_box_handles.png");
        catalog.Should().Contain("freex_insert_object_persistence_selected_picture_handles.png");
        catalog.Should().Contain("freex_insert_object_persistence_reopened_picture_handles.png");
        catalog.Should().Contain("freex_insert_object_persistence_saved.fxl");
        catalog.Should().Contain("guarded/blocked");
    }
}
