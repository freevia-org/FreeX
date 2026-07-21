using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetChartWriterTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ChartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";

    // The chart writer rebuilds chart drawings into xl/drawings/drawingN.xml. A source-package drawing that
    // belongs to ANOTHER sheet (e.g. an image-only drawing1.xml on a different sheet) is preserved later at
    // its original path, so the chart writer must not claim that part name — otherwise the rebuilt charts
    // land on the wrong sheet when the source reference is restored.
    [Fact]
    public void Save_DoesNotWriteChartDrawingToAnotherSheetsSourceDrawingPath()
    {
        var (workbook, _) = CreateChartWorkbook();
        using var package = CreatePackage();
        var sourceDrawingPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // drawing1.xml belongs to a different sheet; "Charted" has no source drawing of its own.
            ["OtherSheet"] = "xl/drawings/drawing1.xml",
        };

        Save(package, workbook, sourceDrawingPaths);

        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/drawings/drawing1.xml").Should().BeNull(
            "the chart writer must not claim a drawing part name another sheet's source drawing owns");

        ResolveWorksheetDrawingTarget(archive).Should().NotContain("drawing1.xml");
    }

    // A chart sheet that already owns a source drawing must reuse that exact part, so its rebuilt charts and
    // any preserved drawing content stay together on the same sheet.
    [Fact]
    public void Save_ReusesTheChartSheetsOwnSourceDrawingPath()
    {
        var (workbook, _) = CreateChartWorkbook();
        using var package = CreatePackage();
        var sourceDrawingPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Charted"] = "xl/drawings/drawing1.xml",
        };

        Save(package, workbook, sourceDrawingPaths);

        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull(
            "a chart sheet must reuse its own source drawing part");
        ResolveWorksheetDrawingTarget(archive).Should().Contain("drawing1.xml");
    }

    private static (Workbook Workbook, Sheet Sheet) CreateChartWorkbook()
    {
        var workbook = new Workbook("ChartPlacement");
        var sheet = workbook.AddSheet("Charted");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
        });
        return (workbook, sheet);
    }

    private static void Save(MemoryStream package, Workbook workbook, IReadOnlyDictionary<string, string> sourceDrawingPaths) =>
        XlsxWorksheetChartWriter.Save(
            package,
            workbook,
            _ => true,
            (_, _, _) => new XDocument(new XElement(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/chart") + "chartSpace")),
            _ => "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
            _ => ChartRelationshipType,
            sourceDrawingPaths);

    private static string ResolveWorksheetDrawingTarget(ZipArchive archive)
    {
        var ws = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var drawingRelId = ws.Root!.Element(Ns + "drawing")!.Attribute(Rel + "id")!.Value;
        var wsRels = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        return wsRels.Root!.Elements(PkgRel + "Relationship")
            .First(r => r.Attribute("Id")!.Value == drawingRelId).Attribute("Target")!.Value;
    }

    private static MemoryStream CreatePackage() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """),
            ("xl/workbook.xml",
                """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Charted" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """),
            ("xl/worksheets/sheet1.xml",
                """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData/></worksheet>
                """));
}
