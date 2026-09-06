using System.Xml.Linq;
using FluentAssertions;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r484: the mirror of r483, in the sibling file.
///
/// <para>r483 fixed ZoomFrameBorderXml APPENDING <c>a:ln</c>, which landed it after an existing
/// <c>a:effectLst</c>. ZoomFrameGeometryXml had the same class of defect from the opposite end: it
/// called <c>AddFirst</c>, which placed <c>a:prstGeom</c> BEFORE an existing <c>a:xfrm</c>. In
/// CT_ShapeProperties the transform comes first and the geometry second, so geometry can be neither
/// appended nor put unconditionally first.</para>
///
/// <para>Reachable by any zoom frame that has been moved or resized, since that is what puts an
/// <c>a:xfrm</c> in its shape properties. As in r483 the result is well-formed XML that is
/// schema-invalid, which PowerPoint reports as a presentation needing repair.</para>
///
/// <para>Checked while here: these two are the only classes in FreeP.Core.Model that mutate an
/// spPr, so the family is closed rather than merely sampled.</para>
/// </summary>
public sealed class R484_ZoomFrameGeometryKeepsSchemaOrderTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";

    private const string Transform = "<a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"100\" cy=\"100\"/></a:xfrm>";
    private const string Fill = "<a:solidFill><a:srgbClr val=\"FF0000\"/></a:solidFill>";

    private static (Presentation Presentation, PreservedObjectInfo Info) ZoomWith(string shapePropertiesInner)
    {
        var info = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            RawXml =
                "<p14:zoomFrame xmlns:p14=\"" + P14 + "\" xmlns:a=\"" + A + "\">" +
                "<p14:zmPr><a:spPr>" + shapePropertiesInner + "</a:spPr></p14:zmPr></p14:zoomFrame>",
            ZoomProperties = new ZoomObjectProperties(),
        };

        var slide = new Slide();
        slide.Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.Zoom, PreservedObject = info });

        var presentation = new Presentation();
        presentation.Slides.Add(slide);
        return (presentation, info);
    }

    private static string ShapePropertyOrder(PreservedObjectInfo info) =>
        string.Join(",", XDocument.Parse(info.RawXml).Descendants(A + "spPr").Single()
            .Elements().Select(element => element.Name.LocalName));

    [Theory]
    // Control: with nothing to order against, both the old and new code are correct -- the same
    // blind spot that hid r483, kept here deliberately so its role is explicit.
    [InlineData("", "prstGeom")]
    [InlineData(Transform, "xfrm,prstGeom")]
    [InlineData(Fill, "prstGeom,solidFill")]
    [InlineData(Transform + Fill, "xfrm,prstGeom,solidFill")]
    public void TheGeometryIsWrittenInSchemaOrder(string existingShapeProperties, string expectedOrder)
    {
        var (presentation, info) = ZoomWith(existingShapeProperties);

        new SetZoomObjectPropertiesCommand(0, 7, new ZoomObjectProperties(FrameGeometry: "roundRect"))
            .Apply(presentation);

        ShapePropertyOrder(info).Should().Be(
            expectedOrder,
            "CT_ShapeProperties puts xfrm before the geometry and the geometry before everything " +
            "else; getting either side wrong makes the deck schema-invalid");
    }

    [Fact]
    public void TheGeometryIsStillActuallyWritten()
    {
        // Narrowness: ordering is worthless if the geometry stopped being applied.
        var (presentation, info) = ZoomWith(Transform);

        new SetZoomObjectPropertiesCommand(0, 7, new ZoomObjectProperties(FrameGeometry: "roundRect"))
            .Apply(presentation);

        var geometry = XDocument.Parse(info.RawXml).Descendants(A + "prstGeom").Single();
        geometry.Attribute("prst")!.Value.Should().Be("roundRect");
        geometry.Element(A + "avLst").Should().NotBeNull("prstGeom requires an avLst child");
    }
}
