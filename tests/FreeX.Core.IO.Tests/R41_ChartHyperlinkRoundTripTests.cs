using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R41-io-hyperlink-drawing-rels-3-1 / -3-2: XlsxWorksheetChartWriter.WriteWorksheetCharts
/// unconditionally deletes a sheet's chart drawing part (and each chart's own part) and rebuilds them
/// purely from ChartModel whenever the slow full-rebuild save path runs -- ChartModel has no Hyperlink
/// property, so a chart-object hyperlink (a:hlinkClick on the graphicFrame's cNvPr) and a hyperlink on
/// the chart's main title run were both silently dropped. The fix reads each hyperlink from the
/// package's CURRENT (pre-rebuild) drawing/chart bytes before they are deleted and re-attaches it (as a
/// native passthrough, with a freshly allocated relationship id) to the rebuilt anchor/title.
/// </summary>
public sealed class R41_ChartHyperlinkRoundTripTests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ChartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string HyperlinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    // --- Finding 3-1: chart-object hyperlink on the graphicFrame ---------------------------------

    [Fact]
    public void Save_PreservesChartObjectHyperlinkOnRebuiltGraphicFrame()
    {
        using var package = CreatePackage(
            drawingXml: """
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <xdr:absoluteAnchor>
                    <xdr:pos x="0" y="0"/>
                    <xdr:ext cx="100" cy="100"/>
                    <xdr:graphicFrame>
                      <xdr:nvGraphicFramePr>
                        <xdr:cNvPr id="1" name="Chart 1"><a:hlinkClick r:id="rIdHlink1"/></xdr:cNvPr>
                        <xdr:cNvGraphicFramePr/>
                      </xdr:nvGraphicFramePr>
                      <xdr:xfrm/>
                      <a:graphic>
                        <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart">
                          <c:chart r:id="rIdChart1"/>
                        </a:graphicData>
                      </a:graphic>
                    </xdr:graphicFrame>
                    <xdr:clientData/>
                  </xdr:absoluteAnchor>
                </xdr:wsDr>
                """,
            drawingRelsXml: XlsxPackageTestFixtures.RelationshipsXml(
                """<Relationship Id="rIdHlink1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.com/object-link" TargetMode="External"/>""",
                """<Relationship Id="rIdChart1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="../charts/chart1.xml"/>"""));

        var workbook = CreateChartWorkbook();
        Save(package, workbook, ownDrawingPath: "xl/drawings/drawing1.xml");

        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var (target, targetMode) = ResolveGraphicFrameHyperlink(archive, "xl/drawings/drawing1.xml");

        target.Should().Be("https://example.com/object-link",
            "the chart-object hyperlink on the source graphicFrame must survive the anchor rebuild");
        targetMode.Should().Be("External");
    }

    // Sibling no-regression: a chart with no source hyperlink must not gain a spurious one.
    [Fact]
    public void Save_DoesNotAddGraphicFrameHyperlinkWhenSourceHasNone()
    {
        using var package = CreatePackage(
            drawingXml: """
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <xdr:absoluteAnchor>
                    <xdr:pos x="0" y="0"/>
                    <xdr:ext cx="100" cy="100"/>
                    <xdr:graphicFrame>
                      <xdr:nvGraphicFramePr>
                        <xdr:cNvPr id="1" name="Chart 1"/>
                        <xdr:cNvGraphicFramePr/>
                      </xdr:nvGraphicFramePr>
                      <xdr:xfrm/>
                      <a:graphic>
                        <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart">
                          <c:chart r:id="rIdChart1"/>
                        </a:graphicData>
                      </a:graphic>
                    </xdr:graphicFrame>
                    <xdr:clientData/>
                  </xdr:absoluteAnchor>
                </xdr:wsDr>
                """,
            drawingRelsXml: XlsxPackageTestFixtures.RelationshipsXml(
                """<Relationship Id="rIdChart1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="../charts/chart1.xml"/>"""));

        var workbook = CreateChartWorkbook();
        Save(package, workbook, ownDrawingPath: "xl/drawings/drawing1.xml");

        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var cNvPr = drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame").Single()
            .Element(SpreadsheetDrawingNs + "nvGraphicFramePr")!
            .Element(SpreadsheetDrawingNs + "cNvPr")!;
        cNvPr.Element(DrawingNs + "hlinkClick").Should().BeNull(
            "no hyperlink existed on the source graphicFrame, so none should be invented");
    }

    // --- Finding 3-2: hyperlink on the chart's main title run -------------------------------------

    [Fact]
    public void Save_PreservesChartTitleHyperlink()
    {
        using var package = CreatePackage(
            chartXml: """
                <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <c:chart>
                    <c:title>
                      <c:tx>
                        <c:rich>
                          <a:bodyPr/>
                          <a:p>
                            <a:r>
                              <a:rPr sz="1600"><a:hlinkClick r:id="rIdTitleHlink"/></a:rPr>
                              <a:t>My Title</a:t>
                            </a:r>
                          </a:p>
                        </c:rich>
                      </c:tx>
                    </c:title>
                  </c:chart>
                </c:chartSpace>
                """,
            chartRelsXml: XlsxPackageTestFixtures.RelationshipsXml(
                """<Relationship Id="rIdTitleHlink" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.org/title-link" TargetMode="External"/>"""));

        var workbook = CreateChartWorkbook();
        Save(package, workbook, createChartXml: CreateChartXmlWithTitle);

        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var (target, targetMode) = ResolveTitleHyperlink(archive, "xl/charts/chart1.xml");

        target.Should().Be("https://example.org/title-link",
            "the chart title's hyperlink must survive the c:title rebuild");
        targetMode.Should().Be("External");
    }

    // Sibling no-regression: a title with no source hyperlink must not gain a spurious one, and the
    // title text itself must still round-trip.
    [Fact]
    public void Save_DoesNotAddTitleHyperlinkWhenSourceHasNone()
    {
        using var package = CreatePackage(
            chartXml: """
                <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <c:chart>
                    <c:title>
                      <c:tx>
                        <c:rich>
                          <a:bodyPr/>
                          <a:p>
                            <a:r>
                              <a:rPr sz="1600"/>
                              <a:t>My Title</a:t>
                            </a:r>
                          </a:p>
                        </c:rich>
                      </c:tx>
                    </c:title>
                  </c:chart>
                </c:chartSpace>
                """);

        var workbook = CreateChartWorkbook();
        Save(package, workbook, createChartXml: CreateChartXmlWithTitle);

        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var chartXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/chart1.xml");
        var rPr = chartXml.Root!
            .Element(ChartNs + "chart")!
            .Element(ChartNs + "title")!
            .Element(ChartNs + "tx")!
            .Element(ChartNs + "rich")!
            .Element(DrawingNs + "p")!
            .Element(DrawingNs + "r")!
            .Element(DrawingNs + "rPr")!;
        rPr.Element(DrawingNs + "hlinkClick").Should().BeNull(
            "no hyperlink existed on the source title run, so none should be invented");

        var titleText = chartXml.Descendants(DrawingNs + "t").Single().Value;
        titleText.Should().Be("My Title", "the title text itself must still round-trip");
    }

    private static XDocument CreateChartXmlWithTitle(ChartModel chart, Workbook workbook, Sheet sheet) =>
        new(new XElement(ChartNs + "chartSpace",
            new XAttribute(XNamespace.Xmlns + "c", ChartNs),
            new XAttribute(XNamespace.Xmlns + "a", DrawingNs),
            new XElement(ChartNs + "chart",
                new XElement(ChartNs + "title",
                    new XElement(ChartNs + "tx",
                        new XElement(ChartNs + "rich",
                            new XElement(DrawingNs + "bodyPr"),
                            new XElement(DrawingNs + "p",
                                new XElement(DrawingNs + "r",
                                    new XElement(DrawingNs + "rPr", new XAttribute("sz", 1600)),
                                    new XElement(DrawingNs + "t", "My Title")))))))));

    private static (string Target, string? TargetMode) ResolveGraphicFrameHyperlink(ZipArchive archive, string drawingPath)
    {
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingPath);
        var cNvPr = drawingXml.Descendants(SpreadsheetDrawingNs + "graphicFrame").Single()
            .Element(SpreadsheetDrawingNs + "nvGraphicFramePr")!
            .Element(SpreadsheetDrawingNs + "cNvPr")!;
        var hlinkClick = cNvPr.Element(DrawingNs + "hlinkClick");
        hlinkClick.Should().NotBeNull("the rebuilt graphicFrame must carry the preserved hlinkClick");
        var relId = hlinkClick!.Attribute(Rel + "id")!.Value;

        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingRelsPath);
        var relationship = relsXml.Root!.Elements(PkgRel + "Relationship")
            .First(r => r.Attribute("Id")!.Value == relId);
        return (relationship.Attribute("Target")!.Value, relationship.Attribute("TargetMode")?.Value);
    }

    private static (string Target, string? TargetMode) ResolveTitleHyperlink(ZipArchive archive, string chartPath)
    {
        var chartXml = XlsxPackageTestFixtures.LoadPackageXml(archive, chartPath);
        var hlinkClick = chartXml.Root!
            .Element(ChartNs + "chart")!
            .Element(ChartNs + "title")!
            .Element(ChartNs + "tx")!
            .Element(ChartNs + "rich")!
            .Element(DrawingNs + "p")!
            .Element(DrawingNs + "r")!
            .Element(DrawingNs + "rPr")!
            .Element(DrawingNs + "hlinkClick");
        hlinkClick.Should().NotBeNull("the rebuilt title run must carry the preserved hlinkClick");
        var relId = hlinkClick!.Attribute(Rel + "id")!.Value;

        var chartRelsPath = XlsxPackagePath.GetRelationshipPartPath(chartPath);
        var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, chartRelsPath);
        var relationship = relsXml.Root!.Elements(PkgRel + "Relationship")
            .First(r => r.Attribute("Id")!.Value == relId);
        return (relationship.Attribute("Target")!.Value, relationship.Attribute("TargetMode")?.Value);
    }

    private static ChartModel CreateChartWorkbookChart(Sheet sheet) => new()
    {
        Type = ChartType.Column,
        Title = "My Title",
        DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
    };

    private static Workbook CreateChartWorkbook()
    {
        var workbook = new Workbook("ChartHyperlinks");
        var sheet = workbook.AddSheet("Charted");
        sheet.Charts.Add(CreateChartWorkbookChart(sheet));
        return workbook;
    }

    private static void Save(
        MemoryStream package,
        Workbook workbook,
        string? ownDrawingPath = null,
        Func<ChartModel, Workbook, Sheet, XDocument>? createChartXml = null)
    {
        var sourceDrawingPaths = ownDrawingPath is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Charted"] = ownDrawingPath };

        XlsxWorksheetChartWriter.Save(
            package,
            workbook,
            _ => true,
            createChartXml ?? ((_, _, _) => new XDocument(new XElement(ChartNs + "chartSpace"))),
            _ => "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
            _ => ChartRelationshipType,
            sourceDrawingPaths);
    }

    private static MemoryStream CreatePackage(
        string? drawingXml = null,
        string? drawingRelsXml = null,
        string? chartXml = null,
        string? chartRelsXml = null)
    {
        var entries = new List<(string Path, string Content)>
        {
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
                """),
        };

        if (drawingXml is not null)
            entries.Add(("xl/drawings/drawing1.xml", drawingXml));
        if (drawingRelsXml is not null)
            entries.Add(("xl/drawings/_rels/drawing1.xml.rels", drawingRelsXml));
        if (chartXml is not null)
            entries.Add(("xl/charts/chart1.xml", chartXml));
        if (chartRelsXml is not null)
            entries.Add(("xl/charts/_rels/chart1.xml.rels", chartRelsXml));

        return XlsxPackageTestFixtures.CreatePackage(entries.ToArray());
    }
}
