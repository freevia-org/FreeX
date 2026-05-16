using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxDrawingColorReaderTests
{
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void TryReadThemeColorReference_ReadsSchemeColor()
    {
        var solidFill = XElement.Parse("""
            <a:solidFill xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:schemeClr val="accent2"/>
            </a:solidFill>
            """);

        XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var reference)
            .Should().BeTrue();
        reference.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2));
    }

    [Fact]
    public void TryReadThemeColorReference_ReadsPositiveTintFromLumModAndLumOff()
    {
        var solidFill = XElement.Parse("""
            <a:solidFill xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:schemeClr val="accent1">
                <a:lumMod val="60000"/>
                <a:lumOff val="40000"/>
              </a:schemeClr>
            </a:solidFill>
            """);

        XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var reference)
            .Should().BeTrue();
        reference.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.4));
    }

    [Fact]
    public void TryReadThemeColorReference_ReadsNegativeTintFromLumMod()
    {
        var solidFill = XElement.Parse("""
            <a:solidFill xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:schemeClr val="tx1">
                <a:lumMod val="75000"/>
              </a:schemeClr>
            </a:solidFill>
            """);

        XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var reference)
            .Should().BeTrue();
        reference.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1, -0.25));
    }

    [Fact]
    public void TryReadConcreteColor_ReadsSrgbColor()
    {
        var solidFill = XElement.Parse("""
            <a:solidFill xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:srgbClr val="0C2238"/>
            </a:solidFill>
            """);

        XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color)
            .Should().BeTrue();
        color.Should().Be(new CellColor(12, 34, 56));
    }
}
