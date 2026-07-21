using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R63-io-drawing-chart-zorder: completes a 3-round chart-z-order saga. The OOXML loader never
/// recorded a chart's drawing-order index, so a chart never entered
/// <see cref="Sheet.DrawingObjectZOrder"/> with its true position relative to shapes/pictures/text
/// boxes -- <c>XlsxChartPackagePart</c> (XlsxWorksheetDrawingParts.cs) carried no
/// <c>DrawingOrderIndex</c> field the way <c>XlsxPicturePackagePart</c>/<c>XlsxTextBoxPackagePart</c>/
/// <c>XlsxShapePackagePart</c> already did, and <c>XlsxFileAdapter.ApplySheetXmlLayout</c>'s
/// <c>foreach (var chartPart in layout.ChartParts)</c> loop (LoadSheetXmlLayoutApplication.cs) never
/// called <c>AddLoadedDrawingObjectOrder</c> for a chart the way it already did for the other three
/// kinds.
/// <para>
/// Round 62 already made the MODEL+COMMAND layers chart-aware
/// (<c>DrawingObjectZOrder.IsSupportedKind</c>/<c>ContainsObject</c> recognize
/// <see cref="SelectionPaneObjectKind.Chart"/>, <c>DrawingObjectZOrder.AddMissingCharts</c> fallback,
/// <c>SelectionPaneCommands</c> routes Chart through the z-order) -- so without this loader fix, a
/// chart's fallback slot (<c>AddMissingCharts</c>) ALWAYS appended it to the back of the normalized
/// z-order stack, regardless of its true anchor position in the drawing part, because
/// <c>ApplyLoadedDrawingObjectZOrder</c> never received a chart entry to sort alongside the other
/// kinds.
/// </para>
/// </summary>
public sealed class R63_ChartDrawingOrderLoadTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string SpreadsheetDrawingNsUri = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string DrawingNsUri = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string ChartNsUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string RelNsUri = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNsUri = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypesNsUri = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string MinimalColumnChartXml = """
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <c:chart>
            <c:plotArea>
              <c:barChart>
                <c:barDir val="col"/>
                <c:ser>
                  <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                  <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$3</c:f></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!$B$2:$B$3</c:f></c:numRef></c:val>
                </c:ser>
              </c:barChart>
            </c:plotArea>
          </c:chart>
        </c:chartSpace>
        """;

    [Fact]
    public void Load_ChartAnchorBeforeShapeAnchorInDrawingXml_ChartLoadsBelowShapeInZOrder()
    {
        // drawing1.xml document order: chart anchor FIRST, shape anchor SECOND -- in OOXML, earlier
        // anchors render behind (below) later ones, so the shape must end up ABOVE the chart, matching
        // real Excel. Pre-fix, the chart carried no DrawingOrderIndex at all, so it was never added to
        // loadedDrawingObjectOrder and always fell back to being appended LAST (i.e. on TOP) by
        // DrawingObjectZOrder.AddMissingCharts -- the exact opposite of what this file encodes.
        using var package = BuildChartAndShapePackage();

        var loaded = new XlsxFileAdapter().Load(package);
        var sheet = loaded.GetSheetAt(0);

        var chart = sheet.Charts.Should().ContainSingle().Subject;
        var shape = sheet.DrawingShapes.Should().ContainSingle().Subject;

        DrawingObjectZOrder.GetNormalizedOrder(sheet).Should().Equal(
            new[]
            {
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, chart.Id),
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id),
            },
            "the chart anchor comes first in drawing1.xml (renders behind) and the shape anchor comes " +
            "second (renders in front) -- the loaded z-order must preserve that true relative order " +
            "instead of always pushing the chart to the back");
    }

    [Fact]
    public void Load_OnlyChartAnchorInDrawingXml_ChartStillLoadsIntoZOrder()
    {
        // Sibling/no-regression: a worksheet with ONLY a chart (no shapes/pictures/text boxes) must
        // still load the chart into the normalized z-order -- exercises the new loader hookup in
        // isolation, with nothing else to interleave with.
        using var package = BuildChartOnlyPackage();

        var loaded = new XlsxFileAdapter().Load(package);
        var sheet = loaded.GetSheetAt(0);

        var chart = sheet.Charts.Should().ContainSingle().Subject;

        DrawingObjectZOrder.GetNormalizedOrder(sheet).Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, chart.Id));
    }

    private static MemoryStream BuildChartAndShapePackage() =>
        BuildPackage(includeShape: true);

    private static MemoryStream BuildChartOnlyPackage() =>
        BuildPackage(includeShape: false);

    private static MemoryStream BuildPackage(bool includeShape)
    {
        var workbook = new Workbook("R63ChartDrawingOrderTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = WorksheetNs;
            XNamespace relNs = RelNsUri;
            XNamespace packageRelNs = PackageRelNsUri;
            XNamespace contentTypeNs = ContentTypesNsUri;
            XNamespace drawingNs = DrawingNsUri;
            XNamespace spreadsheetDrawingNs = SpreadsheetDrawingNsUri;
            XNamespace chartNs = ChartNsUri;

            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml");
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/charts/chart1.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            worksheetXml.Root!.Elements(worksheetNs + "drawing").Remove();
            worksheetXml.Root!.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", "rIdR63Drawing")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            const string worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? XlsxPackageTestFixtures.LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdR63Drawing"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            // Document order: chart anchor FIRST (order index 0), shape anchor SECOND (order index 1)
            // when includeShape is true.
            var anchors = new List<XElement>
            {
                new(spreadsheetDrawingNs + "twoCellAnchor",
                    new XElement(spreadsheetDrawingNs + "from",
                        new XElement(spreadsheetDrawingNs + "col", "3"),
                        new XElement(spreadsheetDrawingNs + "colOff", "0"),
                        new XElement(spreadsheetDrawingNs + "row", "1"),
                        new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                    new XElement(spreadsheetDrawingNs + "to",
                        new XElement(spreadsheetDrawingNs + "col", "8"),
                        new XElement(spreadsheetDrawingNs + "colOff", "0"),
                        new XElement(spreadsheetDrawingNs + "row", "16"),
                        new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                    CreateChartGraphicFrame(spreadsheetDrawingNs, drawingNs, chartNs, relNs),
                    new XElement(spreadsheetDrawingNs + "clientData")),
            };
            if (includeShape)
            {
                anchors.Add(new XElement(spreadsheetDrawingNs + "twoCellAnchor",
                    new XElement(spreadsheetDrawingNs + "from",
                        new XElement(spreadsheetDrawingNs + "col", "1"),
                        new XElement(spreadsheetDrawingNs + "colOff", "0"),
                        new XElement(spreadsheetDrawingNs + "row", "1"),
                        new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                    new XElement(spreadsheetDrawingNs + "to",
                        new XElement(spreadsheetDrawingNs + "col", "3"),
                        new XElement(spreadsheetDrawingNs + "colOff", "0"),
                        new XElement(spreadsheetDrawingNs + "row", "3"),
                        new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                    CreateShapeElement(spreadsheetDrawingNs, drawingNs),
                    new XElement(spreadsheetDrawingNs + "clientData")));
            }

            var drawingXml = new XDocument(
                new XElement(spreadsheetDrawingNs + "wsDr",
                    new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                    new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                    new XAttribute(XNamespace.Xmlns + "c", chartNs),
                    new XAttribute(XNamespace.Xmlns + "r", relNs),
                    anchors));
            ReplacePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);

            var drawingRelsXml = new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdR63Chart"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"),
                        new XAttribute("Target", "../charts/chart1.xml"))));
            ReplacePackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels", drawingRelsXml);
            ReplacePackageXml(archive, "xl/charts/chart1.xml", XDocument.Parse(MinimalColumnChartXml));
        }

        package.Position = 0;
        return package;
    }

    private static XElement CreateChartGraphicFrame(
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace relNs) =>
        new(spreadsheetDrawingNs + "graphicFrame",
            new XElement(spreadsheetDrawingNs + "nvGraphicFramePr",
                new XElement(spreadsheetDrawingNs + "cNvPr",
                    new XAttribute("id", "2"),
                    new XAttribute("name", "R63 Chart")),
                new XElement(spreadsheetDrawingNs + "cNvGraphicFramePr")),
            new XElement(spreadsheetDrawingNs + "xfrm"),
            new XElement(drawingNs + "graphic",
                new XElement(drawingNs + "graphicData",
                    new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/chart"),
                    new XElement(chartNs + "chart", new XAttribute(relNs + "id", "rIdR63Chart")))));

    private static XElement CreateShapeElement(XNamespace spreadsheetDrawingNs, XNamespace drawingNs) =>
        new(spreadsheetDrawingNs + "sp",
            new XElement(spreadsheetDrawingNs + "nvSpPr",
                new XElement(spreadsheetDrawingNs + "cNvPr",
                    new XAttribute("id", "3"),
                    new XAttribute("name", "R63 Shape")),
                new XElement(spreadsheetDrawingNs + "cNvSpPr")),
            new XElement(spreadsheetDrawingNs + "spPr",
                new XElement(drawingNs + "xfrm"),
                new XElement(drawingNs + "prstGeom", new XAttribute("prst", "ellipse"), new XElement(drawingNs + "avLst"))));

    private static void AddContentTypeOverride(XDocument contentTypesXml, XNamespace contentTypeNs, string partName, string contentType) =>
        contentTypesXml.Root!.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument xml)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        xml.Save(writer);
    }
}
