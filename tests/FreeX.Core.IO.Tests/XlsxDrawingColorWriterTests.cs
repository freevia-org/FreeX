using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Drawing;
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

    [Theory]
    [InlineData(WorkbookThemeColorSlot.Accent3, 0.25)]
    [InlineData(WorkbookThemeColorSlot.Light1, -0.4)]
    public void ToSolidFill_TintedThemeColorRoundTripsThroughDrawingColorReader(
        WorkbookThemeColorSlot slot,
        double tint)
    {
        var fill = XlsxDrawingColorWriter.ToSolidFill(
            new WorkbookThemeColorReference(slot, tint),
            null,
            DrawingNs);

        XlsxDrawingColorReader.TryReadThemeColorReference(
                fill.Should().NotBeNull().And.Subject!,
                DrawingNs,
                out var reference)
            .Should()
            .BeTrue();
        reference.Should().Be(new WorkbookThemeColorReference(slot, tint));
    }

    [Theory]
    [InlineData(WorkbookThemeColorSlot.Dark1, DrawingMlThemeColorSlot.Dark1, "dk1")]
    [InlineData(WorkbookThemeColorSlot.Light1, DrawingMlThemeColorSlot.Light1, "lt1")]
    [InlineData(WorkbookThemeColorSlot.Accent6, DrawingMlThemeColorSlot.Accent6, "accent6")]
    [InlineData(WorkbookThemeColorSlot.Hyperlink, DrawingMlThemeColorSlot.Hyperlink, "hlink")]
    [InlineData(WorkbookThemeColorSlot.FollowedHyperlink, DrawingMlThemeColorSlot.FollowedHyperlink, "folHlink")]
    public void ToSolidFill_AdaptsSharedDrawingMlSchemeColorValues(
        WorkbookThemeColorSlot workbookSlot,
        DrawingMlThemeColorSlot sharedSlot,
        string expectedSchemeValue)
    {
        var fill = XlsxDrawingColorWriter.ToSolidFill(
            new WorkbookThemeColorReference(workbookSlot),
            null,
            DrawingNs);

        DrawingMlThemeColorSlotMapper.ToSchemeColorValue(sharedSlot)
            .Should().Be(expectedSchemeValue);

        var color = fill.Should().NotBeNull().And.Subject!.Element(DrawingNs + "schemeClr");
        color.Should().NotBeNull();
        color!.Attribute("val")!.Value.Should().Be(expectedSchemeValue);
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

    [Theory]
    [InlineData(0.25, "75000", "25000")]
    [InlineData(-0.4, "60000", null)]
    [InlineData(2.0, "0", "100000")]
    [InlineData(-2.0, "0", null)]
    public void DrawingColorTint_AppliesSameClampedDrawingMlPercentages(
        double tint,
        string expectedLumMod,
        string? expectedLumOff)
    {
        var color = new XElement(DrawingNs + "schemeClr", new XAttribute("val", "accent1"));

        XlsxDrawingColorTint.ApplyTo(color, tint, DrawingNs);

        color.Element(DrawingNs + "lumMod")!.Attribute("val")!.Value.Should().Be(expectedLumMod);
        color.Element(DrawingNs + "lumOff")?.Attribute("val")!.Value.Should().Be(expectedLumOff);
    }

    [Theory]
    [InlineData("<a:lumMod val=\"60000\"/><a:lumOff val=\"40000\"/>", 0.4)]
    [InlineData("<a:lumMod val=\"75000\"/>", -0.25)]
    [InlineData("<a:tint val=\"65000\"/>", 0.35)]
    [InlineData("<a:shade val=\"65000\"/>", -0.35)]
    public void DrawingColorTint_ReadsDrawingMlTintVariants(string children, double expectedTint)
    {
        var color = XElement.Parse($"""
            <a:schemeClr xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" val="accent1">
              {children}
            </a:schemeClr>
            """);

        XlsxDrawingColorTint.ReadFrom(color, DrawingNs)
            .Should()
            .BeApproximately(expectedTint, 0.000001);
    }
}
