using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Backlog item "custGeom-freeform": a drawing SHAPE (<c>&lt;xdr:sp&gt;</c>) can carry a CUSTOM geometry
/// (<c>&lt;a:custGeom&gt;</c> with <c>&lt;a:gdLst&gt;</c> guides, <c>&lt;a:ahLst&gt;</c> adjust handles,
/// <c>&lt;a:cxnLst&gt;</c> connection sites, <c>&lt;a:rect&gt;</c>, and a <c>&lt;a:pathLst&gt;</c> using
/// moveTo/lnTo/cubicBezTo/arcTo/close path commands) instead of a preset <c>&lt;a:prstGeom&gt;</c> -- e.g. a
/// freeform/"Edit Points"/scribble shape. The suspected gap: FreeX drops/degrades that geometry on an
/// .xlsx round-trip, corrupting or flattening the shape.
/// <para>
/// INVESTIGATION RESULT: already investigated and confirmed NOT a live data-loss bug -- see
/// <see cref="R60_CustGeomShapePreservationGapTests"/> (round-60 finding R60-render-drawing-shapes-6-2).
/// <c>XlsxWorksheetDrawingParts.ReadSpElement</c> does return early (never adds the shape to
/// <see cref="Sheet.DrawingShapes"/>) when no <c>&lt;a:prstGeom&gt;</c> is present, so a custGeom shape is
/// invisible to FreeX's own model, UI, and every model-driven feature (Format Shape, Selection Pane,
/// move/resize). But because the shape is never in the model, it is also never counted as
/// "already emitted" by <c>XlsxWorksheetDrawingObjectWriter</c>, so
/// <c>XlsxWorksheetDrawingPartMerger.MergeDrawingPart</c> -- which walks every anchor in the SOURCE
/// package's drawing part unconditionally and copies across any anchor not already present in the
/// generated package by identity -- copies the untouched custGeom <c>&lt;xdr:sp&gt;</c> back in verbatim on
/// every save, including saves that edit an unrelated sibling shape. Separately,
/// <c>XlsxSourceDrawingGeometryRewriter.RewriteDrawingGeometry</c> classifies candidate shape elements with
/// the same missing-prstGeom check, so a custGeom element is excluded from BOTH sides (models and XML
/// elements) of its positional Zip pairing -- it never desyncs that pairing for sibling shapes today.
/// </para>
/// <para>
/// This file adds deeper round-trip coverage than R60's (which only asserted element/descendant COUNTS)
/// by asserting the full custGeom subtree -- non-trivial gdLst/ahLst/cxnLst content plus a pathLst using
/// moveTo/lnTo/cubicBezTo/arcTo/close -- survives byte-for-byte (semantic XML equality), both on a pure
/// round-trip and when an unrelated sibling prstGeom shape is edited, with a no-regression sibling test
/// confirming the normal prstGeom shape's own edit is still applied correctly on the same drawing part.
/// No production code change is made here: the gap is the render/interaction one already documented and
/// deferred in R60 (needs XlsxSourceDrawingGeometryRewriter.cs / XlsxWorksheetDrawingObjectWriter.cs /
/// the App.UI renderer updated in lockstep -- out of this backlog item's scope). Data preservation on
/// round-trip is already-done; these tests pin that down with stronger assertions.
/// </para>
/// </summary>
public sealed class Backlog_custGeom_freeform_Tests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string CustGeomXml = """
        <a:custGeom xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <a:avLst>
            <a:gd name="adj" fmla="val 5000"/>
          </a:avLst>
          <a:gdLst>
            <a:gd name="myGuide" fmla="*/ w 1 2"/>
          </a:gdLst>
          <a:ahLst>
            <a:ahXY gdRefX="adj" minX="0" maxX="21600">
              <a:pos x="adj" y="0"/>
            </a:ahXY>
          </a:ahLst>
          <a:cxnLst>
            <a:cxn ang="0"><a:pos x="0" y="0"/></a:cxn>
            <a:cxn ang="5400000"><a:pos x="myGuide" y="914400"/></a:cxn>
          </a:cxnLst>
          <a:rect l="0" t="0" r="myGuide" b="914400"/>
          <a:pathLst>
            <a:path w="914400" h="914400">
              <a:moveTo><a:pt x="0" y="0"/></a:moveTo>
              <a:lnTo><a:pt x="914400" y="0"/></a:lnTo>
              <a:cubicBezTo>
                <a:pt x="914400" y="457200"/>
                <a:pt x="457200" y="914400"/>
                <a:pt x="0" y="914400"/>
              </a:cubicBezTo>
              <a:arcTo wR="200000" hR="200000" stAng="0" swAng="5400000"/>
              <a:close/>
            </a:path>
          </a:pathLst>
        </a:custGeom>
        """;

    [Fact]
    public void CustGeomShape_FullGeometrySubtree_SurvivesPureRoundTripByteForByte()
    {
        using var package = BuildPackageWithCustGeomAndNormalShape();
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var custGeom = drawingXml.Descendants(DrawingNs + "custGeom").Should().ContainSingle().Subject;

        var expected = XElement.Parse(CustGeomXml);
        XNode.DeepEquals(custGeom, expected).Should().BeTrue(
            "the entire custGeom subtree (gdLst/ahLst/cxnLst/rect/pathLst with moveTo/lnTo/cubicBezTo/arcTo/close) " +
            "must survive a save/reload unchanged -- the untouched source anchor is copied back verbatim by " +
            "XlsxWorksheetDrawingPartMerger regardless of whether the model tracks the shape");
    }

    [Fact]
    public void CustGeomShape_FullGeometrySubtree_SurvivesResaveWhenSiblingPrstGeomShapeIsEdited()
    {
        using var package = BuildPackageWithCustGeomAndNormalShape();
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var rect = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        rect.Width *= 2;
        rect.Height *= 2;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var custGeom = drawingXml.Descendants(DrawingNs + "custGeom").Should().ContainSingle().Subject;

        var expected = XElement.Parse(CustGeomXml);
        XNode.DeepEquals(custGeom, expected).Should().BeTrue(
            "the custGeom shape was never edited (only its sibling prstGeom rectangle was resized) and its " +
            "geometry subtree must remain byte-for-byte identical to the source -- editing a sibling shares the " +
            "same drawing part and must not perturb the untouched anchor's custGeom content");
    }

    [Fact]
    public void SiblingPrstGeomShape_StillRoundTripsAndReflectsEdit_NoRegression()
    {
        using var package = BuildPackageWithCustGeomAndNormalShape();
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var rect = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        var originalToCol = "9"; // matches the "to" marker BuildPackageWithCustGeomAndNormalShape authors below.
        rect.Width *= 2;
        rect.Height *= 2;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");

        // Identify the sibling (prstGeom) anchor by its geometry element, mirroring how the custGeom
        // tests above locate their own anchor -- position-only lookup would be ambiguous once the
        // resize has shifted the "to" marker.
        var anchors = drawingXml.Descendants(SpreadsheetDrawingNs + "twoCellAnchor").ToList();
        var prstGeomAnchor = anchors.SingleOrDefault(a => a.Descendants(DrawingNs + "prstGeom").Any());
        prstGeomAnchor.Should().NotBeNull("the sibling prstGeom shape's anchor must still be present after resave");
        prstGeomAnchor!.Descendants(DrawingNs + "prstGeom").Single().Attribute("prst")!.Value.Should().Be("rect",
            "the sibling shape's own preset geometry must still be present, unchanged, after resave");
        prstGeomAnchor.Element(SpreadsheetDrawingNs + "to")!.Element(SpreadsheetDrawingNs + "col")!.Value
            .Should().NotBe(originalToCol,
                "resizing the shape's model Width/Height must be reflected in its own anchor's bounding box " +
                "on save, independent of (and not blocked by) the untouched custGeom sibling sharing the part");
    }

    private static MemoryStream BuildPackageWithCustGeomAndNormalShape()
    {
        var workbook = new Workbook("CustGeomFreeformRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var adapter = new XlsxFileAdapter();
        var package = new MemoryStream();
        adapter.Save(workbook, package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var drawingXml = XDocument.Parse($"""
                <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNs}" xmlns:a="{DrawingNs}" xmlns:r="{RelNs}">
                  <xdr:twoCellAnchor>
                    <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                    <xdr:sp macro="" textlink="">
                      <xdr:nvSpPr>
                        <xdr:cNvPr id="2" name="Freeform 1"/>
                        <xdr:cNvSpPr/>
                      </xdr:nvSpPr>
                      <xdr:spPr>
                        <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
                        {CustGeomXml}
                        <a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                      </xdr:spPr>
                    </xdr:sp>
                    <xdr:clientData/>
                  </xdr:twoCellAnchor>
                  <xdr:twoCellAnchor>
                    <xdr:from><xdr:col>6</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>9</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                    <xdr:sp macro="" textlink="">
                      <xdr:nvSpPr>
                        <xdr:cNvPr id="3" name="Rectangle 2"/>
                        <xdr:cNvSpPr/>
                      </xdr:nvSpPr>
                      <xdr:spPr>
                        <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
                        <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                        <a:solidFill><a:srgbClr val="00FF00"/></a:solidFill>
                      </xdr:spPr>
                    </xdr:sp>
                    <xdr:clientData/>
                  </xdr:twoCellAnchor>
                </xdr:wsDr>
                """);
            WritePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);
            WritePackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels", new XDocument(new XElement(PackageRelNs + "Relationships")));

            var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            worksheetXml.Root!.Add(new XElement(WorksheetNs + "drawing", new XAttribute(RelNs + "id", "rIdDrawing1")));
            WritePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            const string worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } existingRelsEntry
                ? XlsxPackageTestFixtures.LoadPackageXml(existingRelsEntry)
                : new XDocument(new XElement(PackageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdDrawing1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            WritePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!.Add(new XElement(ContentTypeNs + "Override",
                new XAttribute("PartName", "/xl/drawings/drawing1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml")));
            WritePackageXml(archive, "[Content_Types].xml", contentTypesXml);
        }

        package.Position = 0;
        return package;
    }

    private static void WritePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, System.Xml.Linq.SaveOptions.DisableFormatting);
    }
}
