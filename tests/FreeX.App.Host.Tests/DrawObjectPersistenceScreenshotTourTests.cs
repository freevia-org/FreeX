using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DrawObjectPersistenceScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesSubmittedDrawObjectPersistenceEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.DrawObjectPersistence.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_DRAW_OBJECT_PERSISTENCE_TOUR");
        dispatcherSource.Should().Contain("DrawObjectPersistenceTourOutputDirectoryName = \"draw-object-persistence-tour\"");
        dispatcherSource.Should().Contain("DrawObjectPersistenceTourSavedWorkbookFileName = \"freex_draw_object_persistence_saved.fxl\"");
        dispatcherSource.Should().Contain("CaptureDrawObjectPersistenceTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(DrawObjectPersistenceTourManifest))]");

        tourSource.Should().Contain("EnsureDrawObjectFormattingTourContext");
        tourSource.Should().Contain("new SetDrawingShapeColorsCommand(");
        tourSource.Should().Contain("new SetDrawingShapeGradientCommand(");
        tourSource.Should().Contain("new SetDrawingShapeEffectCommand(");
        tourSource.Should().Contain("new ResizeDrawingShapeCommand(");
        tourSource.Should().Contain("new RotateDrawingShapeCommand(");
        tourSource.Should().Contain("new SetDrawingShapeAltTextCommand(");
        tourSource.Should().Contain("new ResizePictureCommand(");
        tourSource.Should().Contain("new RotatePictureCommand(");
        tourSource.Should().Contain("new SetPictureLockAspectRatioCommand(");
        tourSource.Should().Contain("new SetPictureCropCommand(context.Sheet.Id, context.Picture.Id, 0.16");
        tourSource.Should().Contain("new SetPictureCropCommand(context.Sheet.Id, context.Picture.Id, 0, 0, 0, 0)");
        tourSource.Should().Contain("new SetPictureAltTextCommand(");
        tourSource.Should().Contain("new SetTextBoxColorsCommand(");
        tourSource.Should().Contain("new ResizeTextBoxCommand(");
        tourSource.Should().Contain("new RotateTextBoxCommand(");
        tourSource.Should().Contain("new SetTextBoxAltTextCommand(");
        tourSource.Should().Contain("new MoveSelectionPaneObjectCommand(");
        tourSource.Should().Contain("new RenameSelectionPaneObjectCommand(");
        tourSource.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        tourSource.Should().Contain("OpenFileAsync(savedWorkbookPath)");
        tourSource.Should().Contain("IsNonBlankPng");

        tourSource.Should().Contain("freex_draw_object_persistence_mutated_shape_result");
        tourSource.Should().Contain("freex_draw_object_persistence_picture_crop_result");
        tourSource.Should().Contain("freex_draw_object_persistence_picture_reset_crop_result");
        tourSource.Should().Contain("freex_draw_object_persistence_mutated_text_box_result");
        tourSource.Should().Contain("freex_draw_object_persistence_selection_pane_arranged");
        tourSource.Should().Contain("freex_draw_object_persistence_saved_native_workbook");
        tourSource.Should().Contain("freex_draw_object_persistence_reopened_persisted_objects");
        tourSource.Should().Contain("DrawObjectPersistenceTourManifest");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.DrawObjectPersistenceTourManifest");
        tourSource.Should().Contain("XLSX drawing-object round-trip breadth remains a separate compatibility lane");

        catalog.Should().Contain("FREEX_DRAW_OBJECT_PERSISTENCE_TOUR=1");
        catalog.Should().Contain("screenshots/draw-object-persistence-tour/");
        catalog.Should().Contain("draw_object_persistence_tour_manifest.json");
        catalog.Should().Contain("freex_draw_object_persistence_reopened_persisted_objects.png");
        catalog.Should().Contain("freex_draw_object_persistence_saved.fxl");
    }
}
