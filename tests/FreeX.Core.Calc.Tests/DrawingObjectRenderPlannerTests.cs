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

    // ── Wave 3: outline width/dash/no-fill + shape text projection ──────────────────────────────

    [Fact]
    public void GetViewport_ShapeBoundsExposeOutlineWidthAndDash()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            OutlineWidthPoints = 3.0,
            OutlineDash = DrawingShapeOutlineDash.Dash,
        };
        sheet.DrawingShapes.Add(shape);

        var viewport = new ViewportService().GetViewport(
            workbook, sheet.Id, new ViewportRequest(1, 1, 120, 120));

        var bounds = viewport.DrawingObjects.Single(b => b.Id == shape.Id);
        bounds.OutlineWidthPoints.Should().Be(3.0);
        bounds.OutlineDash.Should().Be(DrawingShapeOutlineDash.Dash);
        bounds.OutlineHasNoFill.Should().BeFalse();
    }

    [Fact]
    public void GetViewport_ShapeBoundsExposeAuthoredGradientFill()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            FillColor = new CellColor(91, 155, 213),
            GradientFillEndColor = new CellColor(255, 255, 255),
            GradientFillDirection = DrawingShapeGradientDirection.Vertical,
        };
        sheet.DrawingShapes.Add(shape);

        var viewport = new ViewportService().GetViewport(
            workbook, sheet.Id, new ViewportRequest(1, 1, 120, 120));

        var bounds = viewport.DrawingObjects.Single(b => b.Id == shape.Id);
        bounds.GradientFillEndColor.Should().Be(new CellColor(255, 255, 255));
        bounds.GradientFillDirection.Should().Be(DrawingShapeGradientDirection.Vertical);
    }

    [Fact]
    public void GetViewport_ShapeBoundsExposeOutlineHasNoFill()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            OutlineHasNoFill = true,
        };
        sheet.DrawingShapes.Add(shape);

        var viewport = new ViewportService().GetViewport(
            workbook, sheet.Id, new ViewportRequest(1, 1, 120, 120));

        var bounds = viewport.DrawingObjects.Single(b => b.Id == shape.Id);
        bounds.OutlineHasNoFill.Should().BeTrue();
    }

    [Fact]
    public void GetViewport_ShapeBoundsExposeShapeText()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            ShapeText = "Hello",
            ShapeTextFontSizePoints = 14.0,
            ShapeTextBold = true,
            ShapeTextItalic = false,
            ShapeTextUnderline = true,
            ShapeTextHAlign = DrawingShapeTextHAlign.Center,
            ShapeTextVAnchor = DrawingShapeTextVAnchor.Bottom,
            ShapeTextWrap = false,
        };
        sheet.DrawingShapes.Add(shape);

        var viewport = new ViewportService().GetViewport(
            workbook, sheet.Id, new ViewportRequest(1, 1, 120, 120));

        var bounds = viewport.DrawingObjects.Single(b => b.Id == shape.Id);
        bounds.ShapeText.Should().Be("Hello");
        bounds.ShapeTextFontSizePoints.Should().Be(14.0);
        bounds.ShapeTextBold.Should().BeTrue();
        bounds.ShapeTextItalic.Should().BeFalse();
        bounds.ShapeTextUnderline.Should().BeTrue();
        bounds.ShapeTextHAlign.Should().Be(DrawingShapeTextHAlign.Center);
        bounds.ShapeTextVAnchor.Should().Be(DrawingShapeTextVAnchor.Bottom);
        bounds.ShapeTextWrap.Should().BeFalse();
    }

    [Fact]
    public void DrawingObjectBounds_OutlineDefaults_MatchLegacyBehavior()
    {
        // Fields default to 0/Solid/false so existing callers that don't set them
        // continue to get the legacy 1.5 px solid stroke (handled by the renderer).
        var bounds = new DrawingObjectBounds(
            SelectionPaneObjectKind.Shape,
            Guid.NewGuid(), "S", 1, 1, 0, 0, 80, 40,
            ShapeKind: DrawingShapeKind.Rectangle);

        bounds.OutlineWidthPoints.Should().Be(0);
        bounds.OutlineDash.Should().Be(DrawingShapeOutlineDash.Solid);
        bounds.OutlineHasNoFill.Should().BeFalse();
        bounds.ShapeText.Should().BeNull();
    }

    // ── WordArt projection (ViewportService → DrawingObjectBounds) ─────────

    [Fact]
    public void GetViewport_WordArtShape_IsWordArtAndNoBodyFillAppliedInBounds()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            HasFill = false,   // WordArt typically has no body fill
            IsWordArt = true,
            WarpPreset = "textWave1",
            ShapeText = "FreeX",
            ShapeTextFontSizePoints = 36,
            ShapeTextBold = true,
            ShapeTextColor = new CellColor(0xFF, 0x45, 0x00),
            ShapeTextGradientEndColor = new CellColor(0x00, 0x00, 0xFF),
            ShapeTextOutlineColor = new CellColor(0x8B, 0x00, 0x00),
            ShapeTextOutlineWidthPoints = 1.5,
        };
        sheet.DrawingShapes.Add(shape);

        var viewport = new ViewportService().GetViewport(
            workbook, sheet.Id, new ViewportRequest(1, 1, 120, 120));

        var bounds = viewport.DrawingObjects.Single(b => b.Id == shape.Id);
        bounds.IsWordArt.Should().BeTrue();
        bounds.ShapeText.Should().Be("FreeX");
        bounds.ShapeTextColor.Should().Be(new CellColor(0xFF, 0x45, 0x00));
        bounds.ShapeTextGradientEndColor.Should().Be(new CellColor(0x00, 0x00, 0xFF));
        bounds.ShapeTextOutlineColor.Should().Be(new CellColor(0x8B, 0x00, 0x00));
        bounds.ShapeTextOutlineWidthPoints.Should().Be(1.5);
        // FillColor should be null (no body fill)
        bounds.FillColor.Should().BeNull();
    }

    [Fact]
    public void GetViewport_WordArtShape_ThemeColorResolvedToConcreteColor()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var theme = WorkbookTheme.Office;
        workbook.Theme = theme;

        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            IsWordArt = true,
            ShapeText = "Theme",
            // Use a theme color reference — it should resolve to a concrete color in bounds.
            ShapeTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0),
        };
        sheet.DrawingShapes.Add(shape);

        var viewport = new ViewportService().GetViewport(
            workbook, sheet.Id, new ViewportRequest(1, 1, 120, 120));

        var bounds = viewport.DrawingObjects.Single(b => b.Id == shape.Id);
        bounds.IsWordArt.Should().BeTrue();
        // ShapeTextColor should be the resolved theme color, not null.
        bounds.ShapeTextColor.Should().NotBeNull("theme color must be resolved to concrete color");
    }

    [Fact]
    public void GetViewport_NonWordArtShape_IsWordArtFalse_GradAndOutlineNull()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            IsWordArt = false,
            ShapeText = "Normal",
            ShapeTextColor = new CellColor(0xFF, 0xFF, 0xFF),
        };
        sheet.DrawingShapes.Add(shape);

        var viewport = new ViewportService().GetViewport(
            workbook, sheet.Id, new ViewportRequest(1, 1, 120, 120));

        var bounds = viewport.DrawingObjects.Single(b => b.Id == shape.Id);
        bounds.IsWordArt.Should().BeFalse();
        bounds.ShapeTextGradientEndColor.Should().BeNull();
        bounds.ShapeTextOutlineColor.Should().BeNull();
        bounds.ShapeTextOutlineWidthPoints.Should().Be(0);
    }

    [Fact]
    public void DrawingObjectBounds_WordArtDefaults_AreFalseAndNull()
    {
        // Ensure legacy DrawingObjectBounds construction without WordArt fields
        // defaults safely (no breaking change for existing callers).
        var bounds = new DrawingObjectBounds(
            SelectionPaneObjectKind.Shape,
            Guid.NewGuid(), "S", 1, 1, 0, 0, 80, 40,
            ShapeKind: DrawingShapeKind.Rectangle);

        bounds.IsWordArt.Should().BeFalse();
        bounds.ShapeTextGradientEndColor.Should().BeNull();
        bounds.ShapeTextOutlineColor.Should().BeNull();
        bounds.ShapeTextOutlineWidthPoints.Should().Be(0);
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
