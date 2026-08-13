using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingObjectContextualRibbonPlannerTests
{
    [Fact]
    public void Build_TextBoxSelectionShowsShapeTabAndDisablesShapeOnlyCommands()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 60
        };
        sheet.TextBoxes.Add(textBox);

        var plan = DrawingObjectContextualRibbonPlanner.Build(
            sheet,
            SelectionPaneObjectKind.TextBox,
            textBox.Id);

        plan.ShapeFormatVisible.Should().BeTrue();
        plan.PictureFormatVisible.Should().BeFalse();
        plan.ShapeGradientEnabled.Should().BeFalse();
        plan.ShapeEffectsEnabled.Should().BeFalse();
        plan.CropPictureEnabled.Should().BeFalse();
    }

    [Fact]
    public void Build_ShapeSelectionShowsShapeTabAndEnablesShapeOnlyCommands()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 60
        };
        sheet.DrawingShapes.Add(shape);

        var plan = DrawingObjectContextualRibbonPlanner.Build(
            sheet,
            SelectionPaneObjectKind.Shape,
            shape.Id);

        plan.ShapeFormatVisible.Should().BeTrue();
        plan.PictureFormatVisible.Should().BeFalse();
        plan.ShapeGradientEnabled.Should().BeTrue();
        plan.ShapeEffectsEnabled.Should().BeTrue();
        plan.CropPictureEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(PictureKind.Image, true)]
    [InlineData(PictureKind.CellRangeSnapshot, false)]
    public void Build_PictureSelectionShowsPictureTabAndEnablesCropOnlyForImages(
        PictureKind kind,
        bool expectedCropEnabled)
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var picture = new PictureModel
        {
            Kind = kind,
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 60
        };
        sheet.Pictures.Add(picture);

        var plan = DrawingObjectContextualRibbonPlanner.Build(
            sheet,
            SelectionPaneObjectKind.Picture,
            picture.Id);

        plan.ShapeFormatVisible.Should().BeFalse();
        plan.PictureFormatVisible.Should().BeTrue();
        plan.CropPictureEnabled.Should().Be(expectedCropEnabled);
        plan.ShapeGradientEnabled.Should().BeFalse();
        plan.ShapeEffectsEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Picture, DrawingObjectContextualRibbonPlanner.PictureContextKey)]
    [InlineData(SelectionPaneObjectKind.Shape, DrawingObjectContextualRibbonPlanner.ShapeContextKey)]
    [InlineData(SelectionPaneObjectKind.TextBox, DrawingObjectContextualRibbonPlanner.ShapeContextKey)]
    public void ResolveActivationKey_MapsDrawingSelectionToSharedContextKeys(
        SelectionPaneObjectKind kind,
        string expected) =>
        DrawingObjectContextualRibbonPlanner.ResolveActivationKey(kind).Should().Be(expected);

    [Fact]
    public void CreatePictureShapeCommandSpecs_OwnsContextualTabActionMetadata()
    {
        var specs = DrawingObjectContextualRibbonPlanner.CreatePictureShapeCommandSpecs();

        specs.Select(spec => spec.CommandId).Should().OnlyHaveUniqueItems();
        specs.Should().Contain(new DrawingObjectContextualCommandSpec(
            "Format Picture",
            DrawingObjectContextualCommandAction.FormatPicture));
        specs.Should().Contain(new DrawingObjectContextualCommandSpec(
            "Shape Gradient",
            DrawingObjectContextualCommandAction.ShapeGradient));
        specs.Should().Contain(new DrawingObjectContextualCommandSpec(
            "Shadow",
            DrawingObjectContextualCommandAction.ShapeEffectPreset,
            DrawingShapeEffectPreset.Shadow));
        specs.Should().Contain(new DrawingObjectContextualCommandSpec(
            "3-D Rotation",
            DrawingObjectContextualCommandAction.ShapeEffectPreset,
            DrawingShapeEffectPreset.ThreeDRotation));
    }
}
