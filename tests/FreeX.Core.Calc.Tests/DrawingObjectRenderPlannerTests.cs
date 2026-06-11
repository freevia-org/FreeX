using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public sealed class DrawingObjectRenderPlannerTests
{
    [Fact]
    public void Plan_ImagePictures_ClassifiesPlainAndCroppedImages()
    {
        var plain = PictureBounds(
            imageBytes: [1, 2, 3],
            cropLeft: 0,
            cropTop: 0,
            cropRight: 0,
            cropBottom: 0);
        var cropped = PictureBounds(
            imageBytes: [4, 5, 6],
            cropLeft: 0.1,
            cropTop: 0.2,
            cropRight: 0.3,
            cropBottom: 0.4);

        var plainPlan = DrawingObjectRenderPlanner.Plan(plain);
        var croppedPlan = DrawingObjectRenderPlanner.Plan(cropped);

        plainPlan.IsReady.Should().BeTrue();
        plainPlan.PrimitiveKind.Should().Be(DrawingObjectRenderPrimitiveKind.Image);
        plainPlan.Crop.Should().BeNull();

        croppedPlan.IsReady.Should().BeTrue();
        croppedPlan.PrimitiveKind.Should().Be(DrawingObjectRenderPrimitiveKind.CroppedImage);
        croppedPlan.Crop.Should().Be(new DrawingPictureCrop(0.1, 0.2, 0.3, 0.4));
    }

    [Fact]
    public void Plan_ImagePictureWithoutBytes_UsesBoundsFallback()
    {
        var plan = DrawingObjectRenderPlanner.Plan(PictureBounds(imageBytes: []));

        plan.IsReady.Should().BeFalse();
        plan.PrimitiveKind.Should().Be(DrawingObjectRenderPrimitiveKind.BoundsFallback);
        plan.FallbackReason.Should().Be("Image picture has no embedded image bytes.");
    }

    [Fact]
    public void Plan_CellRangeSnapshot_NormalizesGridAndCarriesCells()
    {
        var cells = new[]
        {
            new PictureCellSnapshot(0, 0, "A1"),
            new PictureCellSnapshot(1, 2, "C2")
        };
        var bounds = PictureBounds(
            pictureKind: PictureKind.CellRangeSnapshot,
            sourceRowCount: 2,
            sourceColumnCount: 3,
            pictureCells: cells);

        var plan = DrawingObjectRenderPlanner.Plan(bounds);

        plan.IsReady.Should().BeTrue();
        plan.PrimitiveKind.Should().Be(DrawingObjectRenderPrimitiveKind.CellRangeSnapshot);
        plan.PictureGrid.Should().Be(new DrawingPictureGrid(2, 3, cells));
    }

    [Fact]
    public void Plan_CellRangeSnapshotWithEmptySourceSize_MatchesWpfOneByOneFallbackGrid()
    {
        var plan = DrawingObjectRenderPlanner.Plan(PictureBounds(
            pictureKind: PictureKind.CellRangeSnapshot,
            sourceRowCount: 0,
            sourceColumnCount: 0));

        plan.PictureGrid.Should().NotBeNull();
        plan.PictureGrid!.RowCount.Should().Be(1);
        plan.PictureGrid.ColumnCount.Should().Be(1);
    }

    [Fact]
    public void Plan_Viewport_PreservesDrawingObjectOrder()
    {
        var shape = new DrawingObjectBounds(
            SelectionPaneObjectKind.Shape,
            Guid.NewGuid(),
            "Shape",
            1,
            1,
            0,
            0,
            80,
            40,
            ShapeKind: DrawingShapeKind.Rectangle);
        var textBox = new DrawingObjectBounds(
            SelectionPaneObjectKind.TextBox,
            Guid.NewGuid(),
            "Text",
            2,
            1,
            0,
            20,
            80,
            40);
        var viewport = new ViewportModel(
            [],
            [],
            [],
            DrawingObjects: [shape, textBox]);

        var plans = DrawingObjectRenderPlanner.Plan(viewport);

        plans.Select(plan => plan.Bounds.DisplayName).Should().Equal("Shape", "Text");
        plans.Select(plan => plan.PrimitiveKind).Should().Equal(
            DrawingObjectRenderPrimitiveKind.Shape,
            DrawingObjectRenderPrimitiveKind.TextBox);
    }

    [Fact]
    public void Plan_SupportedDrawingShapeKinds_UseShapePrimitive()
    {
        foreach (var kind in Enum.GetValues<DrawingShapeKind>().Where(DrawingShapeKindSupport.IsRenderable))
        {
            var plan = DrawingObjectRenderPlanner.Plan(new DrawingObjectBounds(
                SelectionPaneObjectKind.Shape,
                Guid.NewGuid(),
                kind.ToString(),
                1,
                1,
                0,
                0,
                80,
                40,
                ShapeKind: kind));

            plan.IsReady.Should().BeTrue(kind.ToString());
            plan.PrimitiveKind.Should().Be(DrawingObjectRenderPrimitiveKind.Shape, kind.ToString());
        }
    }

    [Fact]
    public void GetViewport_PictureBoundsExposePlannerPayloads()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var imageBytes = new byte[] { 1, 2, 3 };
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = imageBytes,
            CropLeft = 0.1,
            CropTop = 0.2,
            CropRight = 0.3,
            CropBottom = 0.4
        };
        var snapshot = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Kind = PictureKind.CellRangeSnapshot,
            SourceRowCount = 2,
            SourceColumnCount = 2
        };
        snapshot.Cells.Add(new PictureCellSnapshot(0, 1, "B1"));
        sheet.Pictures.Add(picture);
        sheet.Pictures.Add(snapshot);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 120, 120));

        var imageBounds = viewport.DrawingObjects.Single(bounds => bounds.Id == picture.Id);
        imageBounds.ImageBytes.Should().Equal(imageBytes);
        imageBounds.ImageBytes.Should().NotBeSameAs(imageBytes);
        imageBounds.CropLeft.Should().Be(0.1);
        imageBounds.CropTop.Should().Be(0.2);
        imageBounds.CropRight.Should().Be(0.3);
        imageBounds.CropBottom.Should().Be(0.4);

        var snapshotBounds = viewport.DrawingObjects.Single(bounds => bounds.Id == snapshot.Id);
        snapshotBounds.SourceRowCount.Should().Be(2);
        snapshotBounds.SourceColumnCount.Should().Be(2);
        snapshotBounds.PictureCells.Should().Equal(new PictureCellSnapshot(0, 1, "B1"));
        snapshotBounds.PictureCells.Should().NotBeSameAs(snapshot.Cells);
    }

    [Fact]
    public void GetViewport_DrawingObjectBoundsExposeFlipState()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            FlipHorizontal = true
        };
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            FlipVertical = true
        };
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            FlipHorizontal = true,
            FlipVertical = true
        };
        sheet.DrawingShapes.Add(shape);
        sheet.Pictures.Add(picture);
        sheet.TextBoxes.Add(textBox);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 120, 120));

        viewport.DrawingObjects.Single(bounds => bounds.Id == shape.Id).Should().Match<DrawingObjectBounds>(
            bounds => bounds.FlipHorizontal && !bounds.FlipVertical);
        viewport.DrawingObjects.Single(bounds => bounds.Id == picture.Id).Should().Match<DrawingObjectBounds>(
            bounds => !bounds.FlipHorizontal && bounds.FlipVertical);
        viewport.DrawingObjects.Single(bounds => bounds.Id == textBox.Id).Should().Match<DrawingObjectBounds>(
            bounds => bounds.FlipHorizontal && bounds.FlipVertical);
    }

    private static DrawingObjectBounds PictureBounds(
        PictureKind pictureKind = PictureKind.Image,
        byte[]? imageBytes = null,
        double cropLeft = 0,
        double cropTop = 0,
        double cropRight = 0,
        double cropBottom = 0,
        uint sourceRowCount = 0,
        uint sourceColumnCount = 0,
        IReadOnlyList<PictureCellSnapshot>? pictureCells = null) =>
        new(
            SelectionPaneObjectKind.Picture,
            Guid.NewGuid(),
            "Picture",
            1,
            1,
            0,
            0,
            100,
            50,
            PictureKind: pictureKind,
            ImageBytes: imageBytes,
            CropLeft: cropLeft,
            CropTop: cropTop,
            CropRight: cropRight,
            CropBottom: cropBottom,
            SourceRowCount: sourceRowCount,
            SourceColumnCount: sourceColumnCount,
            PictureCells: pictureCells ?? []);
}
