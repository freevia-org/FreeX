using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R94: <see cref="XlsxDifferentialStyleReader"/> must distinguish a dxf that explicitly sets font
/// color to black (<c>&lt;font&gt;&lt;color rgb="FF000000"/&gt;&lt;/font&gt;</c>) from a dxf that never
/// mentions font color at all - both previously produced the identical <c>CellStyle.FontColor ==
/// CellColor.Black</c> (the property's own default), so a downstream conditional-format merge/stack
/// could not tell whether black was explicitly authored or simply unset. The reader now also populates
/// <see cref="CellStyle.DxfFontColor"/> (mirroring the existing DxfBold/DxfItalic/DxfUnderline/
/// DxfStrikethrough tri-state pattern) whenever a <c>&lt;color&gt;</c> element is present.
/// </summary>
public sealed class R94_DxfExplicitBlackFontColorTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void DifferentialStyleReader_ExplicitBlackColor_SetsDxfFontColor()
    {
        var dxf = XElement.Parse(
            $"""<dxf xmlns="{WorkbookNs}"><font><color rgb="FF000000"/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs);

        style.FontColor.Should().Be(CellColor.Black);
        style.DxfFontColor.Should().Be(CellColor.Black,
            "an explicit <color rgb=\"FF000000\"/> means the dxf deliberately chose black, not \"unset\"");
    }

    [Fact]
    public void DifferentialStyleReader_ExplicitRedColor_SetsDxfFontColor_NoRegression()
    {
        var dxf = XElement.Parse(
            $"""<dxf xmlns="{WorkbookNs}"><font><color rgb="FFFF0000"/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs);

        style.FontColor.Should().Be(new CellColor(255, 0, 0));
        style.DxfFontColor.Should().Be(new CellColor(255, 0, 0));
    }

    [Fact]
    public void DifferentialStyleReader_NoColorElement_SiblingRegression_LeavesDxfFontColorNull()
    {
        var dxf = XElement.Parse($"""<dxf xmlns="{WorkbookNs}"><font><b/></font></dxf>""");

        var style = XlsxDifferentialStyleReader.ReadDifferentialStyle(dxf, WorkbookNs);

        style.FontColor.Should().Be(CellColor.Black, "default value when no color is specified");
        style.DxfFontColor.Should().BeNull("a dxf that never mentions <color> must not read as an explicit black");
    }
}
