using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewDrawingObjectThemeTests
{
    [Fact]
    public void ResolveDrawingShapeColors_UsesThemeReferences()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(100, 150, 200))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(10, 20, 30));
        var shape = new DrawingShapeModel
        {
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.5),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.5),
            FillColor = new CellColor(1, 1, 1),
            OutlineColor = new CellColor(2, 2, 2)
        };

        var colors = GridView.ResolveDrawingShapeColors(shape, theme);

        colors.Fill.Should().Be(new CellColor(178, 202, 227));
        colors.Outline.Should().Be(new CellColor(5, 10, 15));
    }

    [Fact]
    public void ResolveDrawingShapeColors_UsesThemeObjectDefaultsBeforeStaticDefaults()
    {
        var theme = WorkbookTheme.Office.WithSupplementalMetadata(
            alternateColorSchemes: null,
            hasObjectDefaults: true,
            objectDefaults: new WorkbookThemeObjectDefaults(
                Shape: new WorkbookThemeShapeObjectDefault(
                    FillColor: new CellColor(0xEE, 0xDD, 0xCC),
                    OutlineColor: new CellColor(0x11, 0x22, 0x33))));

        var colors = GridView.ResolveDrawingShapeColors(new DrawingShapeModel(), theme);

        colors.Fill.Should().Be(new CellColor(0xEE, 0xDD, 0xCC));
        colors.Outline.Should().Be(new CellColor(0x11, 0x22, 0x33));
    }

    [Fact]
    public void ResolveTextBoxColors_UsesThemeReferences()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(100, 150, 200))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(10, 20, 30));
        var textBox = new TextBoxModel
        {
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.5),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.5),
            FillColor = new CellColor(1, 1, 1),
            OutlineColor = new CellColor(2, 2, 2)
        };

        var colors = GridView.ResolveTextBoxColors(textBox, theme);

        colors.Fill.Should().Be(new CellColor(178, 202, 227));
        colors.Outline.Should().Be(new CellColor(5, 10, 15));
    }

    [Fact]
    public void RenderMetadataWrappers_ExposeSharedPaintAndFillPolicy()
    {
        var shapeMetadata = GridView.ResolveDrawingShapeRenderMetadata(
            new DrawingShapeModel
            {
                HasFill = false,
                FillColor = new CellColor(10, 20, 30),
                OutlineHasNoFill = true
            },
            WorkbookTheme.Office);
        var textBoxMetadata = GridView.ResolveTextBoxRenderMetadata(
            new TextBoxModel { HasFill = false },
            WorkbookTheme.Office);

        shapeMetadata.Paint.Fill.Should().Be(new CellColor(10, 20, 30));
        shapeMetadata.Paint.HasFill.Should().BeFalse();
        shapeMetadata.Paint.HasOutline.Should().BeFalse();
        textBoxMetadata.Paint.HasFill.Should().BeFalse();
        textBoxMetadata.Paint.HasOutline.Should().BeTrue();
    }

    [Fact]
    public void CreateObjectPlaceholderLabel_UsesObjectNameOrExcelLikeFallback()
    {
        GridView.CreateObjectPlaceholderLabel("Picture", "  Logo  ", 3).Should().Be("Logo");
        GridView.CreateObjectPlaceholderLabel("Picture", "", 1).Should().Be("Picture");
        GridView.CreateObjectPlaceholderLabel("Picture", null, 3).Should().Be("Picture 3");
        GridView.CreateObjectPlaceholderMetadata("Picture", "  Logo  ", 3).Label.Should().Be("Logo");
    }
}
