using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FreeX.Validation.Avalonia;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookStartupSmokeServiceTests
{
    [Fact]
    public void Run_WithoutArguments_LoadsPreviewWorkbookSession()
    {
        var result = new WorkbookStartupSmokeService().Run([]);

        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Message.Should().Contain("Packaging smoke opened");
        result.Message.Should().Contain("macOS Preview Workbook");
        result.Message.Should().Contain("Port Plan");
        result.Message.Should().Contain("drawing_object_previews=3");
        result.Message.Should().Contain("drawing_object_viewport_objects=4");
        result.Message.Should().Contain("drawing_object_render_plans=4");
        result.Message.Should().Contain("cropped_image_render_plans=1");
        result.Message.Should().Contain("cell_range_snapshot_render_plans=1");
        result.Message.Should().Contain("roundtrip_drawing_object_previews=3");
        result.Message.Should().Contain("roundtrip_drawing_object_viewport_objects=4");
        result.Message.Should().Contain("roundtrip_drawing_object_render_plans=4");
        result.Message.Should().Contain("roundtrip_cropped_image_render_plans=1");
        result.Message.Should().Contain("roundtrip_cell_range_snapshot_render_plans=1");
        result.Message.Should().Contain("applying compact Format Cells style to B2");
        result.Message.Should().Contain("format_cells_style_roundtrip=true");
        result.Message.Should().Contain("edited, saved, and reopened");
    }

    [Fact]
    public void Run_WithoutArguments_UsesObjectBackedPreviewWorkbook()
    {
        var source = PortPreviewWorkbookFactory.Create("Preview", isFallback: false);
        var sheet = source.Workbook.Sheets.Should().ContainSingle().Subject;

        sheet.DrawingShapes.Should().ContainSingle(shape =>
            shape.Id != Guid.Empty &&
            shape.Name == PortPreviewWorkbookFactory.PreviewShapeName &&
            shape.Kind == DrawingShapeKind.Rectangle);
        sheet.TextBoxes.Should().ContainSingle(textBox =>
            textBox.Id != Guid.Empty &&
            textBox.Name == PortPreviewWorkbookFactory.PreviewTextBoxName &&
            textBox.Text.Contains("Avalonia preview", StringComparison.Ordinal));
        sheet.Pictures.Should().ContainSingle(picture =>
            picture.Id != Guid.Empty &&
            picture.Name == PortPreviewWorkbookFactory.PreviewPictureName &&
            picture.Kind == PictureKind.Image &&
            picture.ImageBytes != null &&
            picture.ImageBytes.Length > 0 &&
            picture.ContentType == "image/png" &&
            picture.CropLeft > 0 &&
            picture.CropTop > 0 &&
            picture.CropRight > 0 &&
            picture.CropBottom > 0);
        sheet.Pictures.Should().ContainSingle(picture =>
            picture.Id != Guid.Empty &&
            picture.Name == PortPreviewWorkbookFactory.PreviewCellRangeSnapshotName &&
            picture.Kind == PictureKind.CellRangeSnapshot &&
            picture.SourceRowCount == 2 &&
            picture.SourceColumnCount == 3 &&
            picture.Cells.Count > 0);
        sheet.DrawingObjectZOrder.Select(entry => entry.Kind)
            .Should().Equal(
                SelectionPaneObjectKind.Shape,
                SelectionPaneObjectKind.TextBox,
                SelectionPaneObjectKind.Picture,
                SelectionPaneObjectKind.Picture);

        var session = new WorkbookSessionFactory().Create(source, 240, 320, includeObjects: true);

        session.Viewport.DrawingObjects.Select(drawingObject => drawingObject.DisplayName)
            .Should().Equal(
                PortPreviewWorkbookFactory.PreviewShapeName,
                PortPreviewWorkbookFactory.PreviewTextBoxName,
                PortPreviewWorkbookFactory.PreviewPictureName,
                PortPreviewWorkbookFactory.PreviewCellRangeSnapshotName);
        session.Viewport.DrawingObjects.Should().OnlyContain(drawingObject =>
            drawingObject.Width > 0 &&
            drawingObject.Height > 0);

        var renderPlans = DrawingObjectRenderPlanner.Plan(session.Viewport);
        var croppedPlan = renderPlans.Single(plan =>
            plan.IsReady &&
            plan.PrimitiveKind == DrawingObjectRenderPrimitiveKind.CroppedImage &&
            plan.Bounds.DisplayName == PortPreviewWorkbookFactory.PreviewPictureName &&
            plan.Crop is not null);
        croppedPlan.Crop.Should().NotBeNull();

        var snapshotPlan = renderPlans.Single(plan =>
            plan.IsReady &&
            plan.PrimitiveKind == DrawingObjectRenderPrimitiveKind.CellRangeSnapshot &&
            plan.Bounds.DisplayName == PortPreviewWorkbookFactory.PreviewCellRangeSnapshotName &&
            plan.PictureGrid is not null);
        snapshotPlan.PictureGrid!.RowCount.Should().Be(2);
        snapshotPlan.PictureGrid.ColumnCount.Should().Be(3);
        snapshotPlan.PictureGrid.Cells.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Run_WithCsvPath_LoadsWorkbookThroughPortableOpenPath()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Smoke.csv");
        await File.WriteAllTextAsync(path, "Name,Amount\r\nFreeX,42\r\n");

        var result = new WorkbookStartupSmokeService().Run([path]);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Smoke.csv");
        result.Message.Should().Contain("Smoke");
        result.Message.Should().Contain("drawing_object_previews=0");
        result.Message.Should().Contain("drawing_object_render_plans=0");
        result.Message.Should().Contain("cropped_image_render_plans=0");
        result.Message.Should().Contain("cell_range_snapshot_render_plans=0");
        result.Message.Should().Contain("roundtrip_drawing_object_previews=0");
        result.Message.Should().Contain("roundtrip_drawing_object_render_plans=0");
        result.Message.Should().Contain("roundtrip_cropped_image_render_plans=0");
        result.Message.Should().Contain("roundtrip_cell_range_snapshot_render_plans=0");
        result.Message.Should().Contain("format_cells_style_roundtrip=true");
        result.Message.Should().Contain("edited, saved, and reopened");
    }

    [Fact]
    public void Run_WithoutArguments_FailsWhenReopenedFormatCellsStyleIsMissing()
    {
        var nativeAdapter = new NativeJsonAdapter();
        var styleStrippingAdapter = new TestFileAdapter(
            load: stream =>
            {
                var workbook = nativeAdapter.Load(stream);
                var sheet = workbook.Sheets.FirstOrDefault();
                if (sheet is not null)
                {
                    sheet.GetCell(2, 2)!.StyleId = StyleId.Default;
                    sheet.ClearStyleOnly(2, 2);
                }

                return workbook;
            },
            save: nativeAdapter.Save,
            extension: ".fxl",
            formatName: "Native workbook");

        var result = new WorkbookStartupSmokeService(adapters: [styleStrippingAdapter]).Run([]);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("Format Cells style was not reopened on B2");
    }

    [Fact]
    public void Run_WithMissingPath_FailsInsteadOfPreviewFallback()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Missing.csv");

        var result = new WorkbookStartupSmokeService().Run([path]);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("file not found");
        result.Message.Should().Contain("Missing.csv");
    }

    [Fact]
    public async Task Run_WithUnsupportedPath_FailsInsteadOfPreviewFallback()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Notes.unsupported");
        await File.WriteAllTextAsync(path, "not a workbook");

        var result = new WorkbookStartupSmokeService().Run([path]);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Message.Should().Contain("requested file was not opened");
        result.Message.Should().Contain("Notes.unsupported");
    }

    [Fact]
    public void PackagingSmokeCommand_WithFlag_WritesSuccessAndExitCode()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handled = PackagingSmokeCommand.TryRun(
            [PackagingSmokeCommand.Argument],
            output,
            error,
            out var exitCode);

        handled.Should().BeTrue();
        exitCode.Should().Be(0);
        output.ToString().Should().Contain("Packaging smoke opened");
        output.ToString().Should().Contain("format_cells_style_roundtrip=true");
        output.ToString().Should().Contain("edited, saved, and reopened");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public void PackagingSmokeCommand_WithBadPath_WritesFailureToError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handled = PackagingSmokeCommand.TryRun(
            [PackagingSmokeCommand.Argument, "Missing.csv"],
            output,
            error,
            out var exitCode);

        handled.Should().BeTrue();
        exitCode.Should().Be(1);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain("file not found");
    }

    [Fact]
    public void PackagingSmokeCommand_WithoutFlag_ReturnsFalse()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handled = PackagingSmokeCommand.TryRun(["Book.csv"], output, error, out var exitCode);

        handled.Should().BeFalse();
        exitCode.Should().Be(0);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().BeEmpty();
    }
}
