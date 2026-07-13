using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Covers three round-42 group-shape nested-transform bugs in
/// <see cref="XlsxWorksheetDrawingPartReader"/> (XlsxWorksheetDrawingParts.cs):
/// <list type="bullet">
/// <item>
/// R42-io-drawing-group-transform-3-1 (HIGH): a chart nested inside an <c>&lt;xdr:grpSp&gt;</c>
/// group collapsed onto the whole group's shared outer anchor instead of its own local
/// <c>&lt;xdr:graphicFrame&gt;&lt;xdr:xfrm&gt;</c> sub-position/size (ReadChartParts called only
/// the 1-arg <c>ReadNearestAnchor</c>, with no <c>ComputeGroupTransform</c> call at all — unlike
/// the picture/shape/connector paths).
/// </item>
/// <item>
/// R42-io-drawing-group-transform-3-2 (HIGH): an ancestor group's own rotation
/// (<c>grpSpPr/xfrm</c> <c>rot</c>) was silently dropped when composing the group transform, so
/// children of a rotated group were read at their pre-rotation position.
/// </item>
/// <item>
/// R42-io-drawing-group-transform-3-3 (MED): likewise for an ancestor group's own
/// <c>flipH</c>/<c>flipV</c> — a mirrored group's children kept their original (unmirrored)
/// positions.
/// </item>
/// </list>
/// All three are fixed together via <c>ComputeGroupTransform</c>/<c>TryReadGroupXfrm</c> now
/// composing a full 2D affine (translate + scale + rotate + flip, evaluated per ancestor level via
/// <c>ComputeGroupLevelAffine</c>/<c>ApplyGroupLevelPoint</c>) instead of a translate+scale-only
/// pair, and <c>ReadChartParts</c> now applying that composed transform to the chart's own local
/// <c>xdr:xfrm</c> exactly like the picture/shape/connector paths already did.
/// </summary>
public sealed class R42_DrawingGroupTransformTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string SpreadsheetDrawingNsUri = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string DrawingNsUri = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string ChartNsUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string RelNsUri = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // Standard OOXML EMU-per-pixel factor (96 DPI): 914400 EMU/inch / 96 px/inch = 9525.
    private const double EmuPerPixel = 9525;

    // ── R42-io-drawing-group-transform-3-1: grouped chart must not collapse onto the group anchor ──

    [Fact]
    public void ReadChartParts_ChartNestedInGroup_UsesItsOwnSubPositionAndSizeWithinGroup()
    {
        // Group xfrm: off=(0,0) ext=(1905000,952500) chOff=(0,0) chExt=(952500,952500)
        //   => groupTransform scale: scaleX=2, scaleY=1 (a legitimate author-resized group).
        // Chart local off=(476250,0) ext=(238125,476250)
        //   => expected worksheet position: offsetX = 476250*2 = 952500 EMU = 100px, offsetY = 0.
        //   => expected size: width = 238125*2 = 476250 EMU = 50px, height = 476250*1 = 476250 EMU = 50px.
        // The bug this guards against: the chart instead reading the group's own outer twoCellAnchor
        // verbatim (from col1,row1 offset 0,0 — i.e. FromColumnOffset 0 and no explicit Width/Height).
        var drawingXml = $"""
            <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNsUri}" xmlns:a="{DrawingNsUri}" xmlns:r="{RelNsUri}">
              <xdr:twoCellAnchor>
                <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:to><xdr:col>10</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>20</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                <xdr:grpSp>
                  <xdr:nvGrpSpPr>
                    <xdr:cNvPr id="10" name="Group 1" />
                    <xdr:cNvGrpSpPr />
                  </xdr:nvGrpSpPr>
                  <xdr:grpSpPr>
                    <a:xfrm>
                      <a:off x="0" y="0" />
                      <a:ext cx="1905000" cy="952500" />
                      <a:chOff x="0" y="0" />
                      <a:chExt cx="952500" cy="952500" />
                    </a:xfrm>
                  </xdr:grpSpPr>
                  <xdr:graphicFrame macro="">
                    <xdr:nvGraphicFramePr>
                      <xdr:cNvPr id="13" name="Chart 1" />
                      <xdr:cNvGraphicFramePr />
                    </xdr:nvGraphicFramePr>
                    <xdr:xfrm>
                      <a:off x="476250" y="0" />
                      <a:ext cx="238125" cy="476250" />
                    </xdr:xfrm>
                    <a:graphic>
                      <a:graphicData uri="{ChartNsUri}">
                        <c:chart xmlns:c="{ChartNsUri}" r:id="rIdChart1" />
                      </a:graphicData>
                    </a:graphic>
                  </xdr:graphicFrame>
                </xdr:grpSp>
                <xdr:clientData />
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """;

        var drawingRelsXml = XlsxPackageTestFixtures.RelationshipsXml(
            XlsxPackageTestFixtures.Relationship(
                "rIdChart1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart", "../charts/chart1.xml"));

        using var package = CreateDrawingPackage(
            drawingXml,
            drawingRelsXml,
            ("xl/charts/chart1.xml", """<c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" />"""));

        var parts = ReadDrawingPackageParts(package);

        var chart = parts.ChartParts.Should().ContainSingle().Subject;
        chart.Anchor.Should().NotBeNull();

        chart.Anchor!.FromColumnOffset.Should().BeApproximately(100, 0.01);
        chart.Anchor.FromRowOffset.Should().BeApproximately(0, 0.01);
        chart.Anchor.Width.Should().BeApproximately(50, 0.01);
        chart.Anchor.Height.Should().BeApproximately(50, 0.01);

        // The bug this guards against: the chart collapsing onto the group's own outer anchor
        // (FromColumnOffset 0, no explicit Width/Height) instead of its own sub-rectangle.
        chart.Anchor.FromColumnOffset.Should().NotBe(0);
    }

    [Fact]
    public void ReadChartParts_UngroupedChart_UsesItsOwnAnchorUnaffectedByGroupTransformLogic()
    {
        // Sibling no-regression case: a chart with no enclosing <xdr:grpSp> must keep behaving
        // exactly as before — anchor comes straight from its own oneCellAnchor, unaffected by any
        // group-transform composition (groupTransform is Identity).
        var drawingXml = $"""
            <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNsUri}" xmlns:a="{DrawingNsUri}" xmlns:r="{RelNsUri}">
              <xdr:oneCellAnchor>
                <xdr:from><xdr:col>2</xdr:col><xdr:colOff>9525</xdr:colOff><xdr:row>2</xdr:row><xdr:rowOff>19050</xdr:rowOff></xdr:from>
                <xdr:ext cx="952500" cy="476250" />
                <xdr:graphicFrame macro="">
                  <xdr:nvGraphicFramePr>
                    <xdr:cNvPr id="20" name="Solo Chart" />
                    <xdr:cNvGraphicFramePr />
                  </xdr:nvGraphicFramePr>
                  <xdr:xfrm>
                    <a:off x="0" y="0" />
                    <a:ext cx="952500" cy="476250" />
                  </xdr:xfrm>
                  <a:graphic>
                    <a:graphicData uri="{ChartNsUri}">
                      <c:chart xmlns:c="{ChartNsUri}" r:id="rIdChartSolo" />
                    </a:graphicData>
                  </a:graphic>
                </xdr:graphicFrame>
                <xdr:clientData />
              </xdr:oneCellAnchor>
            </xdr:wsDr>
            """;

        var drawingRelsXml = XlsxPackageTestFixtures.RelationshipsXml(
            XlsxPackageTestFixtures.Relationship(
                "rIdChartSolo", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart", "../charts/chart1.xml"));

        using var package = CreateDrawingPackage(
            drawingXml,
            drawingRelsXml,
            ("xl/charts/chart1.xml", """<c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" />"""));

        var parts = ReadDrawingPackageParts(package);

        var chart = parts.ChartParts.Should().ContainSingle().Subject;
        chart.Anchor.Should().NotBeNull();
        chart.Anchor!.Width.Should().BeApproximately(100, 0.01);
        chart.Anchor.Height.Should().BeApproximately(50, 0.01);
        chart.Anchor.FromColumnOffset.Should().BeApproximately(1, 0.01);
        chart.Anchor.FromRowOffset.Should().BeApproximately(2, 0.01);
    }

    // ── R42-io-drawing-group-transform-3-2: group rotation must be composed into child position ──

    [Fact]
    public void ReadShapeParts_ShapeInsideRotatedGroup_PositionComposesGroupRotation()
    {
        // Group xfrm: off=(0,0) ext=(1000000,1000000) chOff=(0,0) chExt=(1000000,1000000)
        //   rot=5400000 (90 degrees clockwise, rotating the group's whole rendered content -- and
        //   therefore every child's computed position -- about the group's own box center
        //   (500000,500000)).
        // Shape local off=(700000,0) ext=(300000,300000): an off-center child.
        //   Pre-fix (rot ignored): absoluteOff = local off verbatim = (700000,0) EMU
        //     => FromColumnOffset ~= 700000/9525 = 73.5px, FromRowOffset ~= 0px (wrong place).
        //   Post-fix (rot composed): rotate (700000,0) about (500000,500000) by 90 degrees CW
        //     => (1000000,700000) EMU => FromColumnOffset ~= 105.0px, FromRowOffset ~= 73.5px.
        var drawingXml = $"""
            <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNsUri}" xmlns:a="{DrawingNsUri}" xmlns:r="{RelNsUri}">
              <xdr:twoCellAnchor>
                <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:to><xdr:col>10</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>20</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                <xdr:grpSp>
                  <xdr:nvGrpSpPr>
                    <xdr:cNvPr id="10" name="Rotated Group" />
                    <xdr:cNvGrpSpPr />
                  </xdr:nvGrpSpPr>
                  <xdr:grpSpPr>
                    <a:xfrm rot="5400000">
                      <a:off x="0" y="0" />
                      <a:ext cx="1000000" cy="1000000" />
                      <a:chOff x="0" y="0" />
                      <a:chExt cx="1000000" cy="1000000" />
                    </a:xfrm>
                  </xdr:grpSpPr>
                  <xdr:sp>
                    <xdr:nvSpPr>
                      <xdr:cNvPr id="11" name="Off-center Shape" />
                      <xdr:cNvSpPr />
                    </xdr:nvSpPr>
                    <xdr:spPr>
                      <a:xfrm><a:off x="700000" y="0" /><a:ext cx="300000" cy="300000" /></a:xfrm>
                      <a:prstGeom prst="rect"><a:avLst /></a:prstGeom>
                    </xdr:spPr>
                  </xdr:sp>
                </xdr:grpSp>
                <xdr:clientData />
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """;

        var (_, shapes) = XlsxWorksheetDrawingPartReader.ReadShapeParts(XDocument.Parse(drawingXml));

        var shape = shapes.Should().ContainSingle().Subject;
        shape.Anchor.Should().NotBeNull();

        shape.Anchor!.FromColumnOffset.Should().BeApproximately(1000000 / EmuPerPixel, 0.01);
        shape.Anchor.FromRowOffset.Should().BeApproximately(700000 / EmuPerPixel, 0.01);

        // The bug this guards against: rotation ignored entirely, leaving the shape at its
        // pre-rotation local offset (73.5px, 0px) instead of the rotated position.
        shape.Anchor.FromRowOffset.Should().NotBe(0);
    }

    // ── R42-io-drawing-group-transform-3-3: group flipH/flipV must be composed into child position ──

    [Fact]
    public void ReadShapeParts_ShapeInsideFlippedGroup_PositionComposesGroupFlip()
    {
        // Group xfrm: off=(0,0) ext=(1000000,500000) chOff=(0,0) chExt=(1000000,500000) flipH="1"
        //   (mirrors the group's whole rendered content -- and every child's computed position --
        //   horizontally about the group's own box center).
        // Shape local off=(0,0) ext=(200000,200000): a left-edge child.
        //   Pre-fix (flip ignored): absoluteOff = local off verbatim = (0,0) EMU => FromColumnOffset ~= 0px.
        //   Post-fix (flip composed): mirror x=0 about center (extCx=1000000) => x'=1000000 EMU
        //     => FromColumnOffset ~= 105.0px (moved to the mirrored/right side of the group box).
        var drawingXml = $"""
            <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNsUri}" xmlns:a="{DrawingNsUri}" xmlns:r="{RelNsUri}">
              <xdr:twoCellAnchor>
                <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:to><xdr:col>10</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>20</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                <xdr:grpSp>
                  <xdr:nvGrpSpPr>
                    <xdr:cNvPr id="10" name="Flipped Group" />
                    <xdr:cNvGrpSpPr />
                  </xdr:nvGrpSpPr>
                  <xdr:grpSpPr>
                    <a:xfrm flipH="1">
                      <a:off x="0" y="0" />
                      <a:ext cx="1000000" cy="500000" />
                      <a:chOff x="0" y="0" />
                      <a:chExt cx="1000000" cy="500000" />
                    </a:xfrm>
                  </xdr:grpSpPr>
                  <xdr:sp>
                    <xdr:nvSpPr>
                      <xdr:cNvPr id="11" name="Left-edge Shape" />
                      <xdr:cNvSpPr />
                    </xdr:nvSpPr>
                    <xdr:spPr>
                      <a:xfrm><a:off x="0" y="0" /><a:ext cx="200000" cy="200000" /></a:xfrm>
                      <a:prstGeom prst="rect"><a:avLst /></a:prstGeom>
                    </xdr:spPr>
                  </xdr:sp>
                </xdr:grpSp>
                <xdr:clientData />
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """;

        var (_, shapes) = XlsxWorksheetDrawingPartReader.ReadShapeParts(XDocument.Parse(drawingXml));

        var shape = shapes.Should().ContainSingle().Subject;
        shape.Anchor.Should().NotBeNull();

        shape.Anchor!.FromColumnOffset.Should().BeApproximately(1000000 / EmuPerPixel, 0.01);
        shape.Anchor.FromRowOffset.Should().BeApproximately(0, 0.01);

        // The bug this guards against: flip ignored entirely, leaving the shape at its
        // unmirrored local offset (0px) instead of the mirrored position.
        shape.Anchor.FromColumnOffset.Should().NotBe(0);
    }

    [Fact]
    public void ReadShapeParts_ShapeInsideUnrotatedUnflippedGroup_UsesPlainScaleTranslateComposition()
    {
        // Sibling no-regression case for both 3-2 and 3-3: a group with NO rot and NO flip (the
        // overwhelming common case) must keep composing pure scale+translate exactly as before --
        // the new full-affine machinery must reduce to the old behaviour when rot=0/flipH=flipV=false.
        // Group xfrm: off=(0,0) ext=(1905000,952500) chOff=(0,0) chExt=(952500,952500)
        //   => scaleX=2, scaleY=1 (same composition as the pre-existing R36 grouped-picture test).
        // Shape local off=(476250,0) ext=(476250,476250)
        //   => expected: offsetX = 476250*2 = 952500 EMU = 100px, width = 100px, height = 50px.
        var drawingXml = $"""
            <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNsUri}" xmlns:a="{DrawingNsUri}" xmlns:r="{RelNsUri}">
              <xdr:twoCellAnchor>
                <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:to><xdr:col>5</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>3</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                <xdr:grpSp>
                  <xdr:nvGrpSpPr>
                    <xdr:cNvPr id="10" name="Plain Group" />
                    <xdr:cNvGrpSpPr />
                  </xdr:nvGrpSpPr>
                  <xdr:grpSpPr>
                    <a:xfrm>
                      <a:off x="0" y="0" />
                      <a:ext cx="1905000" cy="952500" />
                      <a:chOff x="0" y="0" />
                      <a:chExt cx="952500" cy="952500" />
                    </a:xfrm>
                  </xdr:grpSpPr>
                  <xdr:sp>
                    <xdr:nvSpPr>
                      <xdr:cNvPr id="11" name="Plain Shape" />
                      <xdr:cNvSpPr />
                    </xdr:nvSpPr>
                    <xdr:spPr>
                      <a:xfrm><a:off x="476250" y="0" /><a:ext cx="476250" cy="476250" /></a:xfrm>
                      <a:prstGeom prst="rect"><a:avLst /></a:prstGeom>
                    </xdr:spPr>
                  </xdr:sp>
                </xdr:grpSp>
                <xdr:clientData />
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """;

        var (_, shapes) = XlsxWorksheetDrawingPartReader.ReadShapeParts(XDocument.Parse(drawingXml));

        var shape = shapes.Should().ContainSingle().Subject;
        shape.Anchor.Should().NotBeNull();
        shape.Anchor!.FromColumnOffset.Should().BeApproximately(100, 0.01);
        shape.Anchor.FromRowOffset.Should().BeApproximately(0, 0.01);
        shape.XfrmWidthPixels.Should().BeApproximately(100, 0.01);
        shape.XfrmHeightPixels.Should().BeApproximately(50, 0.01);
    }

    private static MemoryStream CreateDrawingPackage(
        string drawingXml,
        string drawingRelsXml,
        params (string Path, string Content)[] extraEntries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(
                archive,
                "xl/worksheets/_rels/sheet1.xml.rels",
                XlsxPackageTestFixtures.RelationshipsXml(
                    XlsxPackageTestFixtures.Relationship(
                        "rIdDrawing1",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing",
                        "../drawings/drawing1.xml")));
            WriteTextEntry(archive, "xl/drawings/drawing1.xml", drawingXml);
            WriteTextEntry(archive, "xl/drawings/_rels/drawing1.xml.rels", drawingRelsXml);
            foreach (var (path, content) in extraEntries)
                WriteTextEntry(archive, path, content);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static XlsxWorksheetDrawingPackageParts ReadDrawingPackageParts(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XDocument.Parse(
            $"""
            <worksheet xmlns="{WorksheetNs.NamespaceName}" xmlns:r="{RelNsUri}">
              <drawing r:id="rIdDrawing1" />
            </worksheet>
            """);

        return XlsxWorksheetDrawingPartReader.ReadParts(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }
}
