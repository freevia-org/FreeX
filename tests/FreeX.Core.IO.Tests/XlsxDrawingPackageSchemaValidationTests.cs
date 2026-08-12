using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using static FreeX.Core.IO.Tests.XlsxPackageTestFixtures;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxDrawingPackageSchemaValidationTests
{
    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    [Fact]
    public void LoadedWorkbookSave_TargetGeneratedDrawingRemovesShadowSourceDrawingRelationshipAndPart()
    {
        using var sourcePackage = CreateSourcePackage();
        using var targetPackage = CreateTargetPackage(hasGeneratedDrawing: true);
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);
        context.Should().NotBeNull();

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);
        var drawingPaths = XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths(sourceArchive, targetArchive, context);
        XlsxWorksheetDrawingReferencePreserver.Preserve(context, drawingPaths);

        var worksheetXml = LoadPackageXml(targetArchive, "xl/worksheets/sheet5.xml");
        worksheetXml.Root!
            .Element(WorksheetNs + "drawing")!
            .Attribute(RelNs + "id")!
            .Value
            .Should()
            .Be("rId1");

        var worksheetRelsXml = LoadPackageXml(targetArchive, "xl/worksheets/_rels/sheet5.xml.rels");
        var drawingRelationships = worksheetRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(IsDrawingRelationship)
            .ToList();
        drawingRelationships.Should().ContainSingle();
        drawingRelationships[0].Attribute("Id")!.Value.Should().Be("rId1");
        drawingRelationships[0].Attribute("Target")!.Value.Should().Be("../drawings/drawing1.xml");

        targetArchive.GetEntry("xl/drawings/drawing5.xml").Should().BeNull();
        targetArchive.GetEntry("xl/drawings/_rels/drawing5.xml.rels").Should().BeNull();
        targetArchive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
        targetArchive.GetEntry("xl/drawings/_rels/drawing1.xml.rels").Should().NotBeNull();
    }

    [Fact]
    public void LoadedWorkbookSave_SourceOnlyDrawingKeepsCopiedDrawingRelationshipPart()
    {
        using var sourcePackage = CreateSourcePackage();
        using var targetPackage = CreateTargetPackage(hasGeneratedDrawing: false);
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);
        context.Should().NotBeNull();

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);
        var drawingPaths = XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths(sourceArchive, targetArchive, context);
        XlsxWorksheetDrawingReferencePreserver.Preserve(context, drawingPaths);

        var worksheetXml = LoadPackageXml(targetArchive, "xl/worksheets/sheet5.xml");
        var drawingRelId = worksheetXml.Root!
            .Element(WorksheetNs + "drawing")!
            .Attribute(RelNs + "id")!
            .Value;

        var worksheetRelsXml = LoadPackageXml(targetArchive, "xl/worksheets/_rels/sheet5.xml.rels");
        var drawingRelationship = worksheetRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(IsDrawingRelationship)
            .Should()
            .ContainSingle()
            .Subject;
        drawingRelationship.Attribute("Id")!.Value.Should().Be(drawingRelId);
        drawingRelationship.Attribute("Target")!.Value.Should().Be("../drawings/drawing5.xml");

        targetArchive.GetEntry("xl/drawings/drawing5.xml").Should().NotBeNull();
        targetArchive.GetEntry("xl/drawings/_rels/drawing5.xml.rels").Should().NotBeNull();
    }

    [Fact]
    public void LoadedWorkbookSave_SourceOnlyDrawingNormalizesDuplicateObjectIds()
    {
        using var sourcePackage = CreateSourcePackageWithDuplicateDrawingObjectIds();
        using var targetPackage = CreateTargetPackage(hasGeneratedDrawing: false);
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);
        context.Should().NotBeNull();

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);
        var drawingPaths = XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths(sourceArchive, targetArchive, context);
        XlsxWorksheetDrawingReferencePreserver.Preserve(context, drawingPaths);

        XlsxDrawingSchemaNormalizer.NormalizePackage(targetArchive);

        var drawingXml = LoadPackageXml(targetArchive, "xl/drawings/drawing5.xml");
        var drawingObjectIds = drawingXml
            .Descendants(SpreadsheetDrawingNs + "cNvPr")
            .Select(element => int.Parse(element.Attribute("id")!.Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        drawingObjectIds.Should().OnlyContain(id => id > 0);
        drawingObjectIds.Should().OnlyHaveUniqueItems();
    }

    private static MemoryStream CreateSourcePackage() =>
        CreatePackage(
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml()),
            ("xl/worksheets/sheet5.xml", WorksheetXml("<drawing r:id=\"rId6\" />")),
            ("xl/worksheets/_rels/sheet5.xml.rels", RelationshipsXml(
                Relationship("rId6", DrawingRelationshipType, "../drawings/drawing5.xml"))),
            ("xl/drawings/drawing5.xml", DrawingXml("rId1", "Source Chart")),
            ("xl/drawings/_rels/drawing5.xml.rels", RelationshipsXml(
                Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart", "../charts/chart1.xml"))),
            ("xl/charts/chart1.xml", ChartXml()));

    private static MemoryStream CreateSourcePackageWithDuplicateDrawingObjectIds() =>
        CreatePackage(
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml()),
            ("xl/worksheets/sheet5.xml", WorksheetXml("<drawing r:id=\"rId6\" />")),
            ("xl/worksheets/_rels/sheet5.xml.rels", RelationshipsXml(
                Relationship("rId6", DrawingRelationshipType, "../drawings/drawing5.xml"))),
            ("xl/drawings/drawing5.xml", DrawingXmlWithDuplicateObjectIds()),
            ("xl/drawings/_rels/drawing5.xml.rels", RelationshipsXml(
                Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart", "../charts/chart1.xml"))),
            ("xl/charts/chart1.xml", ChartXml()));

    private static MemoryStream CreateTargetPackage(bool hasGeneratedDrawing)
    {
        var worksheetXml = hasGeneratedDrawing
            ? WorksheetXml("<drawing r:id=\"rId1\" />")
            : WorksheetXml("");
        var worksheetRelationships = hasGeneratedDrawing
            ? RelationshipsXml(Relationship("rId1", DrawingRelationshipType, "../drawings/drawing1.xml"))
            : RelationshipsXml();

        var entries = new List<(string Path, string Content)>
        {
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml()),
            ("xl/worksheets/sheet5.xml", worksheetXml),
            ("xl/worksheets/_rels/sheet5.xml.rels", worksheetRelationships)
        };

        if (hasGeneratedDrawing)
        {
            entries.Add(("xl/charts/chart1.xml", ChartXml()));
            entries.Add(("xl/drawings/drawing1.xml", DrawingXml("rIdFreeXChart1", "Generated Chart")));
            entries.Add(("xl/drawings/_rels/drawing1.xml.rels", RelationshipsXml(
                Relationship("rIdFreeXChart1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart", "../charts/chart1.xml"))));
        }

        return CreatePackage(entries.ToArray());
    }

    private static bool IsDrawingRelationship(XElement relationship) =>
        string.Equals(
            relationship.Attribute("Type")?.Value,
            DrawingRelationshipType,
            StringComparison.OrdinalIgnoreCase);

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

    private static string DrawingXml(string chartRelId, string objectName) =>
        $$"""
        <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                  xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                  xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <xdr:twoCellAnchor>
            <xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
            <xdr:to><xdr:col>5</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>12</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
            <xdr:graphicFrame macro="">
              <xdr:nvGraphicFramePr>
                <xdr:cNvPr id="2" name="{{objectName}}" />
                <xdr:cNvGraphicFramePr />
              </xdr:nvGraphicFramePr>
              <xdr:xfrm>
                <a:off x="0" y="0" />
                <a:ext cx="0" cy="0" />
              </xdr:xfrm>
              <a:graphic>
                <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart">
                  <c:chart r:id="{{chartRelId}}" />
                </a:graphicData>
              </a:graphic>
            </xdr:graphicFrame>
            <xdr:clientData />
          </xdr:twoCellAnchor>
        </xdr:wsDr>
        """;

    private static string ChartXml() =>
        """
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <c:chart>
            <c:plotArea />
          </c:chart>
        </c:chartSpace>
        """;

    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    private static string DrawingXmlWithDuplicateObjectIds() =>
        """
        <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                  xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                  xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <xdr:twoCellAnchor>
            <xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
            <xdr:to><xdr:col>5</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>12</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
            <xdr:graphicFrame macro="">
              <xdr:nvGraphicFramePr>
                <xdr:cNvPr id="0" name="Source Chart" />
                <xdr:cNvGraphicFramePr />
              </xdr:nvGraphicFramePr>
              <xdr:xfrm>
                <a:off x="0" y="0" />
                <a:ext cx="0" cy="0" />
              </xdr:xfrm>
              <a:graphic>
                <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart">
                  <c:chart r:id="rId1" />
                </a:graphicData>
              </a:graphic>
            </xdr:graphicFrame>
            <xdr:clientData />
          </xdr:twoCellAnchor>
          <xdr:oneCellAnchor>
            <xdr:from><xdr:col>6</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
            <xdr:ext cx="952500" cy="381000" />
            <xdr:sp>
              <xdr:nvSpPr>
                <xdr:cNvPr id="0" name="Source Shape" />
                <xdr:cNvSpPr />
              </xdr:nvSpPr>
              <xdr:spPr>
                <a:prstGeom prst="rect"><a:avLst /></a:prstGeom>
              </xdr:spPr>
            </xdr:sp>
            <xdr:clientData />
          </xdr:oneCellAnchor>
        </xdr:wsDr>
        """;

}
