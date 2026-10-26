using System.Xml.Linq;
using FluentAssertions;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r483: setting a zoom frame's border must not reorder its shape properties into invalid DrawingML.
///
/// <para>CT_ShapeProperties is a SEQUENCE: xfrm?, geometry?, fill?, ln?, effects?, scene3d?, sp3d?,
/// extLst?. ZoomFrameBorderXml appended <c>a:ln</c>, so whenever the frame already carried an
/// <c>a:effectLst</c> the line landed AFTER it. The file stayed well-formed and became
/// schema-invalid, which PowerPoint reports as a presentation needing repair and "repairs" by
/// discarding content.</para>
///
/// <para>Reachable two ways, and neither is exotic: this same class writes shadow, glow, soft-edge
/// and reflection into that effectLst, and any imported deck whose zoom is already styled arrives
/// with one. Found by a coverage scan - ZoomFrameBorderXml is 373 lines that no test named, and its
/// only callers had no tests either.</para>
/// </summary>
public sealed class R483_ZoomFrameBorderKeepsSchemaOrderTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";

    private static (Presentation Presentation, PreservedObjectInfo Info) ZoomWith(string shapePropertiesInner)
    {
        var info = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            RawXml =
                "<p14:zoomFrame xmlns:p14=\"" + P14 + "\" xmlns:a=\"" + A + "\">" +
                "<p14:zmPr><a:spPr>" + shapePropertiesInner + "</a:spPr></p14:zmPr>" +
                "</p14:zoomFrame>",
            ZoomProperties = new ZoomObjectProperties(),
        };

        var slide = new Slide();
        slide.Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.Zoom, PreservedObject = info });

        var presentation = new Presentation();
        presentation.Slides.Add(slide);
        return (presentation, info);
    }

    private static string ShapePropertyOrder(PreservedObjectInfo info)
    {
        var shapeProperties = XDocument.Parse(info.RawXml).Descendants(A + "spPr").Single();
        return string.Join(",", shapeProperties.Elements().Select(element => element.Name.LocalName));
    }

    private static void SetBorder(Presentation presentation) =>
        new SetZoomObjectPropertiesCommand(
                0, 7, new ZoomObjectProperties(FrameBorderColor: "0000FF", FrameBorderWidthEmu: 12700))
            .Apply(presentation);

    [Theory]
    // The control: nothing to order against, so an append was always fine here -- which is exactly
    // why the defect survived. A test written on an empty frame passes either way.
    [InlineData("", "ln")]
    [InlineData("<a:effectLst><a:outerShdw/></a:effectLst>", "ln,effectLst")]
    [InlineData("<a:solidFill><a:srgbClr val=\"FF0000\"/></a:solidFill>", "solidFill,ln")]
    [InlineData("<a:solidFill><a:srgbClr val=\"FF0000\"/></a:solidFill><a:effectLst><a:outerShdw/></a:effectLst>",
                "solidFill,ln,effectLst")]
    [InlineData("<a:extLst/>", "ln,extLst")]
    public void TheLineIsWrittenInSchemaOrder(string existingShapeProperties, string expectedOrder)
    {
        var (presentation, info) = ZoomWith(existingShapeProperties);

        SetBorder(presentation);

        ShapePropertyOrder(info).Should().Be(
            expectedOrder,
            "CT_ShapeProperties is a sequence, and a line after the effects makes the deck " +
            "schema-invalid -- PowerPoint offers to repair it and drops content doing so");
    }

    [Fact]
    public void TheBorderIsStillActuallyWritten()
    {
        // Narrowness: ordering correctly is worthless if the border stopped being applied.
        var (presentation, info) = ZoomWith("<a:effectLst><a:outerShdw/></a:effectLst>");

        SetBorder(presentation);

        var line = XDocument.Parse(info.RawXml).Descendants(A + "ln").Single();
        line.Attribute("w")!.Value.Should().Be("12700");
        line.Descendants(A + "srgbClr").Single().Attribute("val")!.Value.Should().Be("0000FF");
    }
}
