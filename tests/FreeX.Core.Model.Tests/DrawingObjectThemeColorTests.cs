using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class DrawingObjectThemeColorTests
{
    [Fact]
    public void DrawingShapeModel_ResolvesThemeFillAndOutlineColors()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(100, 150, 200))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(10, 20, 30));
        var shape = new DrawingShapeModel
        {
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.5),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.5)
        };

        shape.GetEffectiveFillColor(theme, new CellColor(1, 1, 1)).Should().Be(new CellColor(178, 202, 227));
        shape.GetEffectiveOutlineColor(theme, new CellColor(1, 1, 1)).Should().Be(new CellColor(5, 10, 15));
    }

    [Fact]
    public void DrawingShapeModel_ResolvesDefaultColorsFromThemeObjectDefaults()
    {
        var theme = WorkbookTheme.Office.WithSupplementalMetadata(
            alternateColorSchemes: null,
            hasObjectDefaults: true,
            objectDefaults: new WorkbookThemeObjectDefaults(
                Shape: new WorkbookThemeShapeObjectDefault(
                    FillColor: new CellColor(0xEE, 0xDD, 0xCC),
                    OutlineColor: new CellColor(0x11, 0x22, 0x33))));

        DrawingShapeModel.ResolveDefaultFillColor(theme).Should().Be(new CellColor(0xEE, 0xDD, 0xCC));
        DrawingShapeModel.ResolveDefaultOutlineColor(theme).Should().Be(new CellColor(0x11, 0x22, 0x33));
    }

    [Fact]
    public void TextBoxModel_ResolvesThemeFillAndOutlineColors()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(100, 150, 200))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(10, 20, 30));
        var textBox = new TextBoxModel
        {
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.5),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.5)
        };

        textBox.GetEffectiveFillColor(theme, new CellColor(1, 1, 1)).Should().Be(new CellColor(178, 202, 227));
        textBox.GetEffectiveOutlineColor(theme, new CellColor(1, 1, 1)).Should().Be(new CellColor(5, 10, 15));
    }
}
