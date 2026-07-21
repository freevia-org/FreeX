using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 60 finding R60-render-drawing-shapes-6-2: a Freeform/"Edit Points" shape (<c>&lt;xdr:sp&gt;</c>
/// whose <c>&lt;xdr:spPr&gt;</c> carries <c>&lt;a:custGeom&gt;</c> instead of <c>&lt;a:prstGeom&gt;</c>) is
/// silently dropped by <c>XlsxWorksheetDrawingParts.ReadSpElement</c> (returns early when no
/// <c>&lt;a:prstGeom&gt;</c> is present -- see line ~457) and never added to <see cref="Sheet.DrawingShapes"/>.
/// That much is a genuine, confirmed gap: the shape is never rendered, never selectable, and invisible to
/// every FreeX feature that walks <c>Sheet.DrawingShapes</c> (Selection Pane, Format Shape, move/resize, etc.).
/// <para>
/// The finding's failure scenario additionally claimed this causes "real data loss" -- that re-saving the
/// workbook permanently deletes the shape from the file. <see cref="XlsxAdapter_CustGeomShape_RawXmlSurvivesResave"/>
/// and <see cref="XlsxAdapter_CustGeomShape_SurvivesResaveEvenWhenSiblingShapeIsEdited"/> below disprove that
/// specific claim empirically: because the shape is never added to the model, it is also never in the set
/// <c>XlsxWorksheetDrawingObjectWriter</c> considers "already emitted", so
/// <c>XlsxWorksheetDrawingPartMerger.MergeDrawingPart</c> -- which walks every anchor element in the SOURCE
/// package's drawing part unconditionally (independent of the in-memory model) and copies across any anchor
/// not already present in the generated package -- copies the untouched custGeom &lt;xdr:sp&gt; back in
/// verbatim on every save, including a save that edits/resizes an unrelated sibling shape. So the shape's raw
/// XML (and hence Excel's own rendering of it) does in fact survive a FreeX save/reload cycle today; only
/// FreeX's own in-app rendering and interaction are broken.
/// </para>
/// <para>
/// DEFERRED (bucket io-hard, file scope limited to XlsxPackageMetadataMerger.cs / XlsxWorksheetDrawingParts.cs /
/// DrawingShapeModel.cs): making custGeom shapes visible/selectable in FreeX (even just as a bounding-box
/// placeholder) requires representing them in <see cref="Sheet.DrawingShapes"/>. But the model-construction
/// glue (XlsxFileAdapter.LoadSheetXmlLayoutApplication.cs, out of scope) unconditionally sets
/// <c>DrawingShapeModel.IsSourceLoaded = true</c> for every shape it maps from an <c>XlsxShapePackagePart</c>.
/// Once a shape is <c>IsSourceLoaded</c>, it enters <c>XlsxSourceDrawingGeometryRewriter.RewriteDrawingGeometry</c>'s
/// (also out of scope) shape/anchor pairing (<c>sourceShapes = sheet.DrawingShapes.Where(s =&gt; s.IsSourceLoaded)</c>
/// zipped positionally against <c>shapeElements</c>) -- and that rewriter's own element classifier excludes
/// custGeom elements from <c>shapeElements</c> (same missing-prstGeom check as the reader bug being fixed here).
/// Adding a custGeom entry to the model without also teaching that rewriter to count it would desync the
/// positional pairing for every OTHER source-loaded shape/connector on the same sheet, corrupting their
/// geometry on the very next resize -- a strictly worse regression than today's render/interaction gap. A
/// correct fix therefore needs XlsxSourceDrawingGeometryRewriter.cs (and, to actually render a placeholder,
/// XlsxWorksheetDrawingObjectWriter.cs / the App.UI renderer) updated in lockstep, all out of this bucket's
/// file scope -- hence skipped rather than half-fixed here.
/// </para>
/// </summary>
public sealed class R60_CustGeomShapePreservationGapTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    [Fact]
    public void XlsxAdapter_CustGeomShape_IsNotAddedToModel_ConfirmedGap()
    {
        using var package = BuildPackageWithCustGeomAndNormalShape();
        var adapter = new XlsxFileAdapter();

        var loaded = adapter.Load(package);

        // Only the prstGeom rectangle is represented -- the custGeom freeform is silently dropped from the model.
        var shape = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        shape.Name.Should().Be("Rectangle 2");
    }

    [Fact]
    public void XlsxAdapter_CustGeomShape_RawXmlSurvivesResave()
    {
        using var package = BuildPackageWithCustGeomAndNormalShape();
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        drawingXml.Descendants(DrawingNs + "custGeom").Should().ContainSingle(
            "the untouched source anchor is copied back verbatim by XlsxWorksheetDrawingPartMerger regardless of whether the model tracks it");
        drawingXml.Descendants(SpreadsheetDrawingNs + "sp").Should().HaveCount(2,
            "both the custGeom freeform and the prstGeom rectangle should still be present as raw XML");
    }

    [Fact]
    public void XlsxAdapter_CustGeomShape_SurvivesResaveEvenWhenSiblingShapeIsEdited()
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

        // The custGeom anchor must survive AND stay geometrically untouched (only the sibling rect resized).
        var anchors = drawingXml.Descendants(SpreadsheetDrawingNs + "twoCellAnchor").ToList();
        var custGeomAnchor = anchors.SingleOrDefault(a => a.Descendants(DrawingNs + "custGeom").Any());
        custGeomAnchor.Should().NotBeNull("the custGeom anchor must still be present after the sibling resave");
        custGeomAnchor!.Element(SpreadsheetDrawingNs + "to")!.Element(SpreadsheetDrawingNs + "col")!.Value
            .Should().Be("4", "the custGeom shape was never edited and must not be affected by the sibling resize");
    }

    private static MemoryStream BuildPackageWithCustGeomAndNormalShape()
    {
        var workbook = new Workbook("CustGeomPreservationGap");
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
                        <a:custGeom>
                          <a:avLst/>
                          <a:gdLst/>
                          <a:ahLst/>
                          <a:cxnLst/>
                          <a:rect l="0" t="0" r="0" b="0"/>
                          <a:pathLst>
                            <a:path w="914400" h="914400">
                              <a:moveTo><a:pt x="0" y="0"/></a:moveTo>
                              <a:lnTo><a:pt x="914400" y="0"/></a:lnTo>
                              <a:lnTo><a:pt x="457200" y="914400"/></a:lnTo>
                              <a:close/>
                            </a:path>
                          </a:pathLst>
                        </a:custGeom>
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
