using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R90-shape-5-3: a connector (<c>&lt;xdr:cxnSp&gt;</c>) authored in Excel carries
/// <c>&lt;a:stCxn id="..." idx="..."/&gt;</c>/<c>&lt;a:endCxn .../&gt;</c> under its
/// <c>&lt;xdr:cNvCxnSpPr&gt;</c>, recording which shapes its two endpoints are glued to.
/// FreeX never read these at all -- <see cref="DrawingShapeModel"/> had no field to hold them --
/// so a connector attached to two shapes silently became a bare, unattached line on load. These
/// tests drive the real product entry point (<see cref="XlsxFileAdapter"/> Load/Save, the same
/// path the app uses to open and save a workbook) rather than constructing the internal model
/// directly.
/// </summary>
public partial class FileAdapterSmokeTests
{
    [Fact]
    public void XlsxAdapter_Load_ReadsConnectorShapeConnectionSites()
    {
        var workbook = new Workbook("ConnectorConnectionSitesLoad");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Connector"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddNativeConnectorWithConnectionSites(source, startId: 5, startIdx: 2, endId: 8, endIdx: 1);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        var connector = loadedSheet.DrawingShapes.Should().ContainSingle().Subject;
        connector.StartConnectedShapeId.Should().Be(5);
        connector.StartConnectedShapeConnectionIndex.Should().Be(2);
        connector.EndConnectedShapeId.Should().Be(8);
        connector.EndConnectedShapeConnectionIndex.Should().Be(1);
    }

    [Fact]
    public void XlsxAdapter_Load_ConnectorWithoutConnectionSites_LeavesConnectionFieldsNull()
    {
        var workbook = new Workbook("ConnectorNoConnectionSitesLoad");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Connector"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddNativeConnectorWithConnectionSites(source, startId: null, startIdx: null, endId: null, endIdx: null);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        var connector = loadedSheet.DrawingShapes.Should().ContainSingle().Subject;
        connector.Kind.Should().Be(DrawingShapeKind.Line);
        connector.StartConnectedShapeId.Should().BeNull();
        connector.StartConnectedShapeConnectionIndex.Should().BeNull();
        connector.EndConnectedShapeId.Should().BeNull();
        connector.EndConnectedShapeConnectionIndex.Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_RoundTripsConnectorShapeConnectionSites()
    {
        var workbook = new Workbook("ConnectorConnectionSitesRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Connector"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddNativeConnectorWithConnectionSites(source, startId: 5, startIdx: 2, endId: 8, endIdx: 1);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.DrawingShapes.Should().ContainSingle();

        // R81-io-drawing-shape-cxnsp-order: adding a second untouched shape forces the reader's
        // combined sp/cxnSp document-order pass to distinguish the two element kinds correctly;
        // not central to this fix, so keep the fixture to the single native connector.
        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved);
        var reloadedConnector = reloaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        reloadedConnector.StartConnectedShapeId.Should().Be(5);
        reloadedConnector.StartConnectedShapeConnectionIndex.Should().Be(2);
        reloadedConnector.EndConnectedShapeId.Should().Be(8);
        reloadedConnector.EndConnectedShapeConnectionIndex.Should().Be(1);
    }

    /// <summary>
    /// Injects a native (non-FreeX-authored) drawing part containing a single <c>&lt;xdr:cxnSp&gt;</c>
    /// connector, optionally carrying <c>&lt;a:stCxn&gt;</c>/<c>&lt;a:endCxn&gt;</c> connection-site
    /// elements, mirroring how <c>AddUnsupportedDrawingPackage</c> injects a native connector but with
    /// the connection-site attachment metadata a real Excel-authored connector carries.
    /// </summary>
    private static void AddNativeConnectorWithConnectionSites(
        MemoryStream packageStream, int? startId, int? startIdx, int? endId, int? endIdx)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Elements(worksheetNs + "drawing").Remove();
            worksheetXml.Root!.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", "rIdNativeConnectorDrawing")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdNativeConnectorDrawing"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            XElement? stCxn = startId is null
                ? null
                : new XElement(drawingNs + "stCxn", new XAttribute("id", startId.Value), new XAttribute("idx", startIdx ?? 0));
            XElement? endCxn = endId is null
                ? null
                : new XElement(drawingNs + "endCxn", new XAttribute("id", endId.Value), new XAttribute("idx", endIdx ?? 0));

            var drawingXml = new XDocument(
                new XElement(spreadsheetDrawingNs + "wsDr",
                    new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                    new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                    new XElement(spreadsheetDrawingNs + "twoCellAnchor",
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
                        new XElement(spreadsheetDrawingNs + "cxnSp",
                            new XElement(spreadsheetDrawingNs + "nvCxnSpPr",
                                new XElement(spreadsheetDrawingNs + "cNvPr",
                                    new XAttribute("id", "2"),
                                    new XAttribute("name", "Native connector")),
                                new XElement(spreadsheetDrawingNs + "cNvCxnSpPr", stCxn, endCxn)),
                            new XElement(spreadsheetDrawingNs + "spPr",
                                new XElement(drawingNs + "prstGeom", new XAttribute("prst", "line"), new XElement(drawingNs + "avLst")))),
                        new XElement(spreadsheetDrawingNs + "clientData"))));
            ReplacePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);
        }

        packageStream.Position = 0;
    }
}
