using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Covers two round-36 drawing/OLE round-trip bugs:
/// <list type="bullet">
/// <item>
/// R36-io-drawing-anchor-rels-2-1 (HIGH): a picture nested inside an <c>&lt;xdr:grpSp&gt;</c>
/// group must be positioned/sized using its OWN local <c>&lt;a:xfrm&gt;</c> offset/extent composed
/// through the enclosing group's chOff/chExt transform, not the group's own shared outer anchor
/// (<see cref="XlsxWorksheetDrawingPartReader.ReadPictureParts"/> in XlsxWorksheetDrawingParts.cs).
/// </item>
/// <item>
/// R36-io-drawing-anchor-rels-2-2 (HIGH): a LINKED (non-embedded) <c>&lt;oleObject&gt;</c> has no
/// <c>r:id</c> by design (its target lives in the <c>link</c> attribute instead) and must not be
/// unconditionally deleted by <see cref="XlsxWorksheetOleControlNormalizer.NormalizeWorksheetRoot"/>.
/// </item>
/// </list>
/// R36-io-drawing-anchor-rels-2-3 (connector stCxn/endCxn glue) is NOT covered here: fixing it
/// requires a new field on <c>DrawingShapeModel</c> (FreeX.Core.Model) and writer support in
/// <c>XlsxWorksheetDrawingObjectWriter</c> (FreeX.Core.IO), both outside this bucket's owned files
/// (XlsxWorksheetDrawingParts.cs / XlsxWorksheetOleControlNormalizer.cs) — reading stCxn/endCxn into
/// a local package-part record with nowhere in the model to carry it forward would accomplish
/// nothing, so this finding is deferred rather than half-implemented.
/// </summary>
public sealed class R36_DrawingAnchorGroupOleTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string SpreadsheetDrawingNsUri = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string DrawingNsUri = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string RelNsUri = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // ── R36-io-drawing-anchor-rels-2-1: grouped picture must not collapse onto the group's anchor ──

    [Fact]
    public void ReadPictureParts_TwoPicturesInsideGroup_EachGetsItsOwnOffsetAndSizeWithinGroup()
    {
        // Group xfrm: off=(0,0) ext=(1905000,952500) chOff=(0,0) chExt=(952500,952500)
        //   => groupTransform: offsetX=0, offsetY=0, scaleX=2, scaleY=1 (a real, legitimate
        //      author-resized group whose child coordinate space differs in scale from its own
        //      rendered extent).
        // Picture A: local off=(0,0) ext=(476250,476250)      -> offsetX=0px,   100x50 px.
        // Picture B: local off=(476250,0) ext=(952500,476250) -> offsetX=100px, 200x50 px.
        var drawingXml = $"""
            <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNsUri}" xmlns:a="{DrawingNsUri}" xmlns:r="{RelNsUri}">
              <xdr:twoCellAnchor>
                <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:to><xdr:col>5</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>3</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
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
                  <xdr:pic>
                    <xdr:nvPicPr>
                      <xdr:cNvPr id="11" name="Picture A" />
                      <xdr:cNvPicPr />
                    </xdr:nvPicPr>
                    <xdr:blipFill>
                      <a:blip r:embed="rIdPicA" />
                      <a:stretch><a:fillRect /></a:stretch>
                    </xdr:blipFill>
                    <xdr:spPr>
                      <a:xfrm><a:off x="0" y="0" /><a:ext cx="476250" cy="476250" /></a:xfrm>
                      <a:prstGeom prst="rect"><a:avLst /></a:prstGeom>
                    </xdr:spPr>
                  </xdr:pic>
                  <xdr:pic>
                    <xdr:nvPicPr>
                      <xdr:cNvPr id="12" name="Picture B" />
                      <xdr:cNvPicPr />
                    </xdr:nvPicPr>
                    <xdr:blipFill>
                      <a:blip r:embed="rIdPicB" />
                      <a:stretch><a:fillRect /></a:stretch>
                    </xdr:blipFill>
                    <xdr:spPr>
                      <a:xfrm><a:off x="476250" y="0" /><a:ext cx="952500" cy="476250" /></a:xfrm>
                      <a:prstGeom prst="rect"><a:avLst /></a:prstGeom>
                    </xdr:spPr>
                  </xdr:pic>
                </xdr:grpSp>
                <xdr:clientData />
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """;

        var drawingRelsXml = XlsxPackageTestFixtures.RelationshipsXml(
            XlsxPackageTestFixtures.Relationship(
                "rIdPicA", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "../media/imageA.png"),
            XlsxPackageTestFixtures.Relationship(
                "rIdPicB", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "../media/imageB.png"));

        using var package = CreateDrawingPackage(
            drawingXml,
            drawingRelsXml,
            ("xl/media/imageA.png", new byte[] { 1, 2, 3 }),
            ("xl/media/imageB.png", new byte[] { 4, 5, 6 }));

        var parts = ReadDrawingPackageParts(package);

        parts.PictureParts.Should().HaveCount(2);
        var pictureA = parts.PictureParts.Single(p => p.Name == "Picture A");
        var pictureB = parts.PictureParts.Single(p => p.Name == "Picture B");

        pictureA.Anchor.Should().NotBeNull();
        pictureB.Anchor.Should().NotBeNull();

        pictureA.Anchor!.FromColumnOffset.Should().BeApproximately(0, 0.01);
        pictureA.Anchor.Width.Should().BeApproximately(100, 0.01);
        pictureA.Anchor.Height.Should().BeApproximately(50, 0.01);

        pictureB.Anchor!.FromColumnOffset.Should().BeApproximately(100, 0.01);
        pictureB.Anchor.Width.Should().BeApproximately(200, 0.01);
        pictureB.Anchor.Height.Should().BeApproximately(50, 0.01);

        // The bug this guards against: both pictures collapsing onto the identical, group-wide anchor.
        pictureA.Anchor.Width.Should().NotBe(pictureB.Anchor.Width);
        pictureA.Anchor.FromColumnOffset.Should().NotBe(pictureB.Anchor.FromColumnOffset);
    }

    [Fact]
    public void ReadPictureParts_UngroupedPicture_UsesItsOwnAnchorUnaffectedByGroupTransformLogic()
    {
        // Sibling no-regression case: a picture with no enclosing <xdr:grpSp> must keep behaving
        // exactly as before — anchor extent/offset come straight from its own oneCellAnchor, with
        // no group-transform adjustment applied (groupTransform is Identity).
        var drawingXml = $"""
            <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNsUri}" xmlns:a="{DrawingNsUri}" xmlns:r="{RelNsUri}">
              <xdr:oneCellAnchor>
                <xdr:from><xdr:col>2</xdr:col><xdr:colOff>9525</xdr:colOff><xdr:row>2</xdr:row><xdr:rowOff>19050</xdr:rowOff></xdr:from>
                <xdr:ext cx="952500" cy="476250" />
                <xdr:pic>
                  <xdr:nvPicPr>
                    <xdr:cNvPr id="20" name="Solo Picture" />
                    <xdr:cNvPicPr />
                  </xdr:nvPicPr>
                  <xdr:blipFill>
                    <a:blip r:embed="rIdSolo" />
                    <a:stretch><a:fillRect /></a:stretch>
                  </xdr:blipFill>
                  <xdr:spPr>
                    <a:xfrm><a:off x="0" y="0" /><a:ext cx="952500" cy="476250" /></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst /></a:prstGeom>
                  </xdr:spPr>
                </xdr:pic>
                <xdr:clientData />
              </xdr:oneCellAnchor>
            </xdr:wsDr>
            """;

        var drawingRelsXml = XlsxPackageTestFixtures.RelationshipsXml(
            XlsxPackageTestFixtures.Relationship(
                "rIdSolo", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "../media/solo.png"));

        using var package = CreateDrawingPackage(
            drawingXml,
            drawingRelsXml,
            ("xl/media/solo.png", new byte[] { 7, 8, 9 }));

        var parts = ReadDrawingPackageParts(package);

        var picture = parts.PictureParts.Should().ContainSingle().Subject;
        picture.Anchor.Should().NotBeNull();
        picture.Anchor!.Width.Should().BeApproximately(100, 0.01);
        picture.Anchor.Height.Should().BeApproximately(50, 0.01);
        picture.Anchor.FromColumnOffset.Should().BeApproximately(1, 0.01);
        picture.Anchor.FromRowOffset.Should().BeApproximately(2, 0.01);
    }

    private static MemoryStream CreateDrawingPackage(
        string drawingXml,
        string drawingRelsXml,
        params (string Path, byte[] Bytes)[] mediaEntries)
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
            foreach (var (path, bytes) in mediaEntries)
            {
                var entry = archive.CreateEntry(path);
                using var entryStream = entry.Open();
                entryStream.Write(bytes, 0, bytes.Length);
            }
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

    // ── R36-io-drawing-anchor-rels-2-2: linked (non-embedded) OLE object must survive load ────────

    [Fact]
    public void NormalizeWorksheetRoot_LinkedOleObjectWithoutEmbedRelationship_IsPreserved()
    {
        var worksheetXml = XDocument.Parse(
            $"""
            <worksheet xmlns="{WorksheetNs.NamespaceName}" xmlns:r="{RelNsUri}">
              <oleObjects>
                <oleObject progId="Excel.Sheet.12" link="C:\data\source.xlsx" shapeId="1030" />
              </oleObjects>
            </worksheet>
            """);

        XlsxWorksheetOleControlNormalizer.NormalizeWorksheetRoot(worksheetXml.Root!);

        var oleObject = worksheetXml.Root!.Element(WorksheetNs + "oleObjects")?.Element(WorksheetNs + "oleObject");
        oleObject.Should().NotBeNull("a linked OLE object legitimately has no embed relationship and must not be deleted");
        oleObject!.Attribute("link")!.Value.Should().Be(@"C:\data\source.xlsx");
        oleObject.Attribute("progId")!.Value.Should().Be("Excel.Sheet.12");
        oleObject.Attribute(RelNs + "id").Should().BeNull();
    }

    [Fact]
    public void NormalizeWorksheetRoot_OleObjectWithNeitherEmbedNorLink_IsStillRemoved()
    {
        // Sibling no-regression case: an oleObject with no r:id AND no link attribute is invalid
        // under either interpretation of CT_OleObject and must still be dropped, exactly as before.
        var worksheetXml = XDocument.Parse(
            $"""
            <worksheet xmlns="{WorksheetNs.NamespaceName}" xmlns:r="{RelNsUri}">
              <oleObjects>
                <oleObject progId="Excel.Sheet.12" shapeId="1031" />
              </oleObjects>
            </worksheet>
            """);

        XlsxWorksheetOleControlNormalizer.NormalizeWorksheetRoot(worksheetXml.Root!);

        worksheetXml.Root!.Element(WorksheetNs + "oleObjects").Should().BeNull();
    }

    [Fact]
    public void NormalizeWorksheetRoot_EmbeddedOleObjectWithRelationshipId_IsStillPreserved()
    {
        // No-regression case: the ordinary embedded object (r:id present, no link) must keep
        // round-tripping exactly as it did before this fix.
        var worksheetXml = XDocument.Parse(
            $"""
            <worksheet xmlns="{WorksheetNs.NamespaceName}" xmlns:r="{RelNsUri}">
              <oleObjects>
                <oleObject r:id="rIdOle1" progId="Excel.Sheet.12" shapeId="1032" />
              </oleObjects>
            </worksheet>
            """);

        XlsxWorksheetOleControlNormalizer.NormalizeWorksheetRoot(worksheetXml.Root!);

        var oleObject = worksheetXml.Root!.Element(WorksheetNs + "oleObjects")?.Element(WorksheetNs + "oleObject");
        oleObject.Should().NotBeNull();
        oleObject!.Attribute(RelNs + "id")!.Value.Should().Be("rIdOle1");
    }

    [Fact]
    public void NormalizeWorksheetRoot_ControlWithoutRelationshipId_IsStillRemoved()
    {
        // No-regression case: an ActiveX <control> (unlike <oleObject>) has no "link" attribute
        // and always requires its ctrlProp r:id — a missing r:id must still mean removal.
        var worksheetXml = XDocument.Parse(
            $"""
            <worksheet xmlns="{WorksheetNs.NamespaceName}" xmlns:r="{RelNsUri}">
              <controls>
                <control shapeId="2001" name="CommandButton1" />
              </controls>
            </worksheet>
            """);

        XlsxWorksheetOleControlNormalizer.NormalizeWorksheetRoot(worksheetXml.Root!);

        worksheetXml.Root!.Element(WorksheetNs + "controls").Should().BeNull();
    }
}
