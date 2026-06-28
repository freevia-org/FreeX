using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxDrawingColorWriterTests
{
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void ToSolidFill_WritesPositiveTintAsLumModAndLumOff()
    {
        var fill = XlsxDrawingColorWriter.ToSolidFill(
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.25),
            null,
            DrawingNs);

        var color = fill.Should().NotBeNull().And.Subject!.Element(DrawingNs + "schemeClr");
        color.Should().NotBeNull();
        color!.Attribute("val")!.Value.Should().Be("accent3");
        color.Element(DrawingNs + "lumMod")!.Attribute("val")!.Value.Should().Be("75000");
        color.Element(DrawingNs + "lumOff")!.Attribute("val")!.Value.Should().Be("25000");
    }

    [Fact]
    public void ToSolidFill_WritesNegativeTintAsLumModOnly()
    {
        var fill = XlsxDrawingColorWriter.ToSolidFill(
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Light1, -0.4),
            null,
            DrawingNs);

        var color = fill.Should().NotBeNull().And.Subject!.Element(DrawingNs + "schemeClr");
        color.Should().NotBeNull();
        color!.Attribute("val")!.Value.Should().Be("lt1");
        color.Element(DrawingNs + "lumMod")!.Attribute("val")!.Value.Should().Be("60000");
        color.Element(DrawingNs + "lumOff").Should().BeNull();
    }

    [Fact]
    public void ToSolidFill_WritesConcreteRgbColor()
    {
        var fill = XlsxDrawingColorWriter.ToSolidFill(
            null,
            new CellColor(0x0A, 0x14, 0x1E),
            DrawingNs);

        var color = fill.Should().NotBeNull().And.Subject!.Element(DrawingNs + "srgbClr");
        color.Should().NotBeNull();
        color!.Attribute("val")!.Value.Should().Be("0A141E");
    }
}
