using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingObjectRenderMetadataPlannerTests
{
    [Fact]
    public void NormalizeZOrder_FiltersUnsupportedMissingAndDuplicateEntriesThenAppendsMissingObjects()
    {
        var shape = new DrawingShapeModel();
        var picture = new PictureModel();
        var textBox = new TextBoxModel();

        var normalized = DrawingObjectRenderMetadataPlanner.NormalizeZOrder(
            [shape],
            [picture],
            [textBox],
            [
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, Guid.NewGuid()),
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, Guid.NewGuid()),
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id),
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id),
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id)
            ]);

        normalized.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id));
    }

    [Theory]
    [InlineData(DrawingObjectLayerDisplayMode.All, DrawingObjectLayerRenderMode.Objects)]
    [InlineData(DrawingObjectLayerDisplayMode.Placeholders, DrawingObjectLayerRenderMode.Placeholders)]
    [InlineData(DrawingObjectLayerDisplayMode.Nothing, DrawingObjectLayerRenderMode.Hidden)]
    public void PlanLayerRenderMode_MapsDisplayModeToRendererBranch(
        DrawingObjectLayerDisplayMode displayMode,
        DrawingObjectLayerRenderMode expected) =>
        DrawingObjectRenderMetadataPlanner.PlanLayerRenderMode(displayMode).Should().Be(expected);

    [Fact]
    public void ResolveDrawingShapeRenderMetadata_ProjectsPaintTransformOutlineAndEffectPolicy()
    {
        var shape = new DrawingShapeModel
        {
            FillColor = new CellColor(10, 20, 30),
            OutlineColor = new CellColor(40, 50, 60),
            GradientFillEndColor = new CellColor(70, 80, 90),
            GradientFillDirection = DrawingShapeGradientDirection.Horizontal,
            RotationDegrees = 45,
            FlipHorizontal = true,
            OutlineWidthPoints = 2.25,
            OutlineDash = DrawingShapeOutlineDash.Dash,
            EffectPreset = DrawingShapeEffectPreset.Glow,
            UsesThemeEffects = true,
            ShapeText = "Hello",
            HeadArrowhead = new DrawingArrowhead(DrawingArrowheadType.Triangle)
        };

        var metadata = DrawingObjectRenderMetadataPlanner.ResolveDrawingShapeRenderMetadata(
            shape,
            WorkbookTheme.Office);

        metadata.Paint.Fill.Should().Be(new CellColor(10, 20, 30));
        metadata.Paint.Outline.Should().Be(new CellColor(40, 50, 60));
        metadata.Paint.HasFill.Should().BeTrue();
        metadata.Transform.Should().Be(new DrawingObjectTransformMetadata(45, FlipHorizontal: true, FlipVertical: false));
        metadata.FillGradient.Should().Be(new DrawingShapeFillGradientMetadata(
            new CellColor(70, 80, 90),
            DrawingShapeGradientDirection.Horizontal));
        metadata.Outline.Should().Be(new DrawingShapeOutlineRenderMetadata(
            HasOutline: true,
            ThicknessDip: 3,
            DrawingShapeOutlineDash.Dash));
        metadata.AuthoredEffect.Should().Be(DrawingShapeEffectPreset.Glow);
        metadata.UsesThemeEffects.Should().BeTrue();
        metadata.HasShapeText.Should().BeTrue();
        metadata.HasArrowheads.Should().BeTrue();
    }

    [Fact]
    public void ResolveTextBoxRenderMetadata_ProjectsFillFlagAndTransform()
    {
        var textBox = new TextBoxModel
        {
            HasFill = false,
            RotationDegrees = 15,
            FlipVertical = true,
            Text = "Note"
        };

        var metadata = DrawingObjectRenderMetadataPlanner.ResolveTextBoxRenderMetadata(
            textBox,
            WorkbookTheme.Office);

        metadata.Paint.HasFill.Should().BeFalse();
        metadata.Transform.Should().Be(new DrawingObjectTransformMetadata(15, FlipHorizontal: false, FlipVertical: true));
        metadata.HasText.Should().BeTrue();
    }

    [Fact]
    public void ResolveBoundsShapeRenderMetadata_UsesDefaultFillOnlyForNonWordArtBounds()
    {
        var normalShape = new DrawingObjectBounds(
            SelectionPaneObjectKind.Shape,
            Guid.NewGuid(),
            "Shape 1",
            AnchorRow: 1,
            AnchorCol: 1,
            Left: 0,
            Top: 0,
            Width: 40,
            Height: 20,
            ShapeKind: DrawingShapeKind.Rectangle);
        var wordArt = normalShape with { IsWordArt = true };

        var normalMetadata = DrawingObjectRenderMetadataPlanner.ResolveBoundsShapeRenderMetadata(normalShape);
        var wordArtMetadata = DrawingObjectRenderMetadataPlanner.ResolveBoundsShapeRenderMetadata(wordArt);

        normalMetadata.FillColor.Should().Be(DrawingShapeModel.DefaultFillColor);
        normalMetadata.OutlineColor.Should().Be(DrawingShapeModel.DefaultOutlineColor);
        wordArtMetadata.FillColor.Should().BeNull();
        wordArtMetadata.OutlineColor.Should().Be(DrawingShapeModel.DefaultOutlineColor);
    }

    [Fact]
    public void ResolveBoundsShapeRenderMetadata_ProjectsGradientOnlyForNonLineShapes()
    {
        var shape = new DrawingObjectBounds(
            SelectionPaneObjectKind.Shape,
            Guid.NewGuid(),
            "Gradient",
            AnchorRow: 1,
            AnchorCol: 1,
            Left: 0,
            Top: 0,
            Width: 40,
            Height: 20,
            ShapeKind: DrawingShapeKind.Rectangle,
            FillColor: new CellColor(91, 155, 213),
            GradientFillEndColor: new CellColor(255, 255, 255),
            GradientFillDirection: DrawingShapeGradientDirection.Vertical);

        var line = shape with { ShapeKind = DrawingShapeKind.Line };

        DrawingObjectRenderMetadataPlanner.ResolveBoundsShapeRenderMetadata(shape).FillGradient.Should()
            .Be(new DrawingShapeFillGradientMetadata(
                new CellColor(255, 255, 255),
                DrawingShapeGradientDirection.Vertical));
        DrawingObjectRenderMetadataPlanner.ResolveBoundsShapeRenderMetadata(line).FillGradient.Should().BeNull();
    }

    [Fact]
    public void CreatePlaceholderMetadata_UsesTrimmedNameOrIndexedFallback()
    {
        DrawingObjectRenderMetadataPlanner.CreatePlaceholderMetadata("Picture", "  Logo  ", 4)
            .Label.Should()
            .Be("Logo");
        DrawingObjectRenderMetadataPlanner.CreatePlaceholderMetadata("Picture", null, 4)
            .Label.Should()
            .Be("Picture 4");
    }
}
