using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using static FreeX.Core.IO.Tests.XlsxPackageTestFixtures;

namespace FreeX.Core.IO.Tests;

// R19-worksheet-drawing-zorder-3: the source-vs-generated drawing merge used to de-duplicate anchors
// purely by "{anchor kind}:{cNvPr name}". Excel's own default object naming ("TextBox 1", "Picture 1",
// ...) is reused independently per sheet, so a source-loaded object left untouched by the user and a
// brand-new object authored in FreeX could collide on the exact same default name even though they sit
// at completely different cell positions. When that happened, the merge silently dropped the untouched
// source object as a "duplicate". The fix folds the anchor's own from/to (+ offsets) position into the
// dedup key so two same-named anchors at different positions both survive, while a source anchor that is
// re-emitted verbatim (same name AND same position) still collapses to one.
public sealed class R19_drawing_dedup_Tests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";

    [Fact]
    public void Merge_SourceAndGeneratedAnchorsShareDefaultNameButDifferentPosition_BothSurvive()
    {
        // Source package: an untouched source-loaded text box Excel named "TextBox 1", anchored at A1.
        using var sourcePackage = CreatePackage(
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml()),
            ("xl/worksheets/sheet5.xml", WorksheetXml("<drawing r:id=\"rId6\" />")),
            ("xl/worksheets/_rels/sheet5.xml.rels", RelationshipsXml(
                Relationship("rId6", DrawingRelationshipType, "../drawings/drawing5.xml"))),
            ("xl/drawings/drawing5.xml", ShapeDrawingXml(
                anchorId: "1", name: "TextBox 1", fromCol: 0, fromRow: 0)),
            ("xl/drawings/_rels/drawing5.xml.rels", RelationshipsXml()));

        // Target package: the freshly-written package already contains a NEW text box, also auto-named
        // "TextBox 1" by FreeX's own per-sheet shapeIndex counter, anchored far away at F6.
        using var targetPackage = CreatePackage(
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml()),
            ("xl/worksheets/sheet5.xml", WorksheetXml("<drawing r:id=\"rId1\" />")),
            ("xl/worksheets/_rels/sheet5.xml.rels", RelationshipsXml(
                Relationship("rId1", DrawingRelationshipType, "../drawings/drawing1.xml"))),
            ("xl/drawings/drawing1.xml", ShapeDrawingXml(
                anchorId: "101", name: "TextBox 1", fromCol: 5, fromRow: 5)),
            ("xl/drawings/_rels/drawing1.xml.rels", RelationshipsXml()));

        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);
        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);
        context.Should().NotBeNull();

        XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths(sourceArchive, targetArchive, context);

        var mergedDrawingXml = LoadPackageXml(targetArchive, "xl/drawings/drawing1.xml");
        var names = mergedDrawingXml
            .Descendants(SpreadsheetDrawingNs + "cNvPr")
            .Select(element => element.Attribute("name")?.Value)
            .ToList();

        // Both the untouched source text box and the brand-new one must survive the merge even though
        // they share the same default name -- they are distinct objects at distinct positions.
        names.Should().HaveCount(2);
        names.Should().OnlyContain(name => name == "TextBox 1");

        var fromRows = mergedDrawingXml
            .Descendants(SpreadsheetDrawingNs + "from")
            .Select(from => from.Element(SpreadsheetDrawingNs + "row")!.Value)
            .ToList();
        fromRows.Should().BeEquivalentTo(new[] { "0", "5" });
    }

    [Fact]
    public void Merge_SourceAnchorReemittedVerbatimWithSameNameAndPosition_StaysDeduplicated()
    {
        // Same name AND same position on both sides -- this represents the writer re-emitting the exact
        // same source anchor unchanged. The merge must still collapse this to a single anchor.
        using var sourcePackage = CreatePackage(
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml()),
            ("xl/worksheets/sheet5.xml", WorksheetXml("<drawing r:id=\"rId6\" />")),
            ("xl/worksheets/_rels/sheet5.xml.rels", RelationshipsXml(
                Relationship("rId6", DrawingRelationshipType, "../drawings/drawing5.xml"))),
            ("xl/drawings/drawing5.xml", ShapeDrawingXml(
                anchorId: "1", name: "TextBox 1", fromCol: 0, fromRow: 0)),
            ("xl/drawings/_rels/drawing5.xml.rels", RelationshipsXml()));

        using var targetPackage = CreatePackage(
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml()),
            ("xl/worksheets/sheet5.xml", WorksheetXml("<drawing r:id=\"rId1\" />")),
            ("xl/worksheets/_rels/sheet5.xml.rels", RelationshipsXml(
                Relationship("rId1", DrawingRelationshipType, "../drawings/drawing1.xml"))),
            ("xl/drawings/drawing1.xml", ShapeDrawingXml(
                anchorId: "101", name: "TextBox 1", fromCol: 0, fromRow: 0)),
            ("xl/drawings/_rels/drawing1.xml.rels", RelationshipsXml()));

        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);
        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);
        context.Should().NotBeNull();

        XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths(sourceArchive, targetArchive, context);

        var mergedDrawingXml = LoadPackageXml(targetArchive, "xl/drawings/drawing1.xml");
        var names = mergedDrawingXml
            .Descendants(SpreadsheetDrawingNs + "cNvPr")
            .Select(element => element.Attribute("name")?.Value)
            .ToList();

        names.Should().ContainSingle().Which.Should().Be("TextBox 1");
    }

    private static string ShapeDrawingXml(string anchorId, string name, int fromCol, int fromRow) =>
        $$"""
        <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                  xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <xdr:oneCellAnchor>
            <xdr:from><xdr:col>{{fromCol}}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{{fromRow}}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
            <xdr:ext cx="952500" cy="381000" />
            <xdr:sp textBox="1">
              <xdr:nvSpPr>
                <xdr:cNvPr id="{{anchorId}}" name="{{name}}" />
                <xdr:cNvSpPr txBox="1" />
              </xdr:nvSpPr>
              <xdr:spPr>
                <a:prstGeom prst="rect"><a:avLst /></a:prstGeom>
              </xdr:spPr>
            </xdr:sp>
            <xdr:clientData />
          </xdr:oneCellAnchor>
        </xdr:wsDr>
        """;

    private static string WorkbookXml() =>
        """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Dashboard" sheetId="1" r:id="rId1" />
          </sheets>
        </workbook>
        """;

    private static string WorkbookRelationshipsXml() =>
        RelationshipsXml(Relationship(
            "rId1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
            "worksheets/sheet5.xml"));

    private static string WorksheetXml(string drawingElement) =>
        $$"""
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheetData>
            <row r="1">
              <c r="A1" t="str"><v>Dashboard</v></c>
            </row>
          </sheetData>
          {{drawingElement}}
        </worksheet>
        """;
}
