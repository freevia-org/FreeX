using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

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

        shape.GetEffectiveFillColor(theme, new CellColor(1, 1, 1)).Should().Be(new CellColor(178, 202, 228));
        shape.GetEffectiveOutlineColor(theme, new CellColor(1, 1, 1)).Should().Be(new CellColor(5, 10, 15));
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

        textBox.GetEffectiveFillColor(theme, new CellColor(1, 1, 1)).Should().Be(new CellColor(178, 202, 228));
        textBox.GetEffectiveOutlineColor(theme, new CellColor(1, 1, 1)).Should().Be(new CellColor(5, 10, 15));
    }
}
