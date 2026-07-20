using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R51-io-picture-fill-shape-3-2 (partial): <see cref="DrawingShapeGradientDirection"/> can only
/// represent the four cardinal gradient angles (0/90/180/270 degrees). Previously, ANY non-cardinal
/// angle read from a shape's &lt;a:gradFill&gt;&lt;a:lin ang="..."/&gt; was unconditionally forced to
/// DiagonalDown (90 degrees), regardless of how far away the real angle actually was -- e.g. a
/// 33-degree gradient (much closer to Horizontal/0 degrees) was misread as a 90-degree gradient.
/// The fix snaps a non-cardinal angle to whichever of the four buckets is actually closest.
/// <para>
/// NOTE: this does not address the OTHER half of the R51 finding (a 3+ stop gradient collapsing to
/// 2 stops on any forced model rewrite, e.g. sheet duplication) -- <see cref="DrawingShapeModel"/>
/// has no field for a middle stop and <c>XlsxWorksheetDrawingObjectWriter.ToGradientFill</c> can only
/// emit two stops, both outside this bucket's owned files (XlsxConditionalFormatClosedXmlMapper.cs /
/// XlsxWorksheetDrawingParts.cs), so that part remains a known, separately-tracked limitation.
/// </para>
/// </summary>
public sealed class R51_ShapeGradientFillAngleSnapTests
{
    private static XDocument BuildShapeDrawingXml(string angle) => XDocument.Parse($"""
        <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                  xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <xdr:oneCellAnchor>
            <xdr:from>
              <xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff>
              <xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff>
            </xdr:from>
            <xdr:ext cx="914400" cy="914400"/>
            <xdr:sp>
              <xdr:nvSpPr>
                <xdr:cNvPr id="2" name="Rectangle 1"/>
                <xdr:cNvSpPr/>
              </xdr:nvSpPr>
              <xdr:spPr>
                <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
                <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                <a:gradFill>
                  <a:gsLst>
                    <a:gs pos="0"><a:srgbClr val="FF0000"/></a:gs>
                    <a:gs pos="100000"><a:srgbClr val="00FF00"/></a:gs>
                  </a:gsLst>
                  <a:lin ang="{angle}" scaled="1"/>
                </a:gradFill>
              </xdr:spPr>
            </xdr:sp>
            <xdr:clientData/>
          </xdr:oneCellAnchor>
        </xdr:wsDr>
        """);

    [Fact]
    public void XlsxDrawingPartReader_NonCardinalGradientAngleCloserToHorizontal_SnapsToHorizontalNotDiagonalDown()
    {
        // 33 degrees = 1,980,000 (60,000ths of a degree). Distance to Horizontal(0) = 1,980,000;
        // distance to DiagonalDown(5,400,000) = 3,420,000 -- Horizontal is the true nearest bucket.
        var shape = XlsxWorksheetDrawingPartReader.ReadShapeParts(BuildShapeDrawingXml("1980000"))
            .Shapes
            .Should().ContainSingle()
            .Subject;

        shape.GradientFillDirection.Should().Be(
            DrawingShapeGradientDirection.Horizontal,
            "33 degrees is far closer to the Horizontal (0 degree) bucket than to DiagonalDown (90 degrees)");
    }

    [Fact]
    public void XlsxDrawingPartReader_NonCardinalGradientAngleCloserToDiagonalDown_StillSnapsToDiagonalDown()
    {
        // Sibling no-regression case: an angle that genuinely IS closest to the 90-degree bucket
        // (e.g. 100 degrees = 6,000,000) must still resolve to DiagonalDown, exactly as before.
        var shape = XlsxWorksheetDrawingPartReader.ReadShapeParts(BuildShapeDrawingXml("6000000"))
            .Shapes
            .Should().ContainSingle()
            .Subject;

        shape.GradientFillDirection.Should().Be(
            DrawingShapeGradientDirection.DiagonalDown,
            "100 degrees is closest to the DiagonalDown (90 degree) bucket");
    }

    [Fact]
    public void XlsxDrawingPartReader_ExactCardinalGradientAngle_StillMapsExactly()
    {
        // Sibling no-regression case: exact cardinal angles must still map to their exact bucket.
        var shape = XlsxWorksheetDrawingPartReader.ReadShapeParts(BuildShapeDrawingXml("10800000"))
            .Shapes
            .Should().ContainSingle()
            .Subject;

        shape.GradientFillDirection.Should().Be(DrawingShapeGradientDirection.DiagonalUp);
    }
}
