using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Backlog item "shape-xfrm-ext-stale": a source-loaded drawing SHAPE (<c>&lt;xdr:sp&gt;</c>, and equally an
/// <c>&lt;xdr:cxnSp&gt;</c> connector) whose <c>&lt;xdr:spPr&gt;</c> carries an internal
/// <c>&lt;a:xfrm&gt;&lt;a:ext cx cy/&gt;</c> alongside its <c>twoCellAnchor</c> reverted to its ORIGINAL size
/// after a single save+reload cycle when the user resized it.
/// <para>
/// Root cause: on load, <c>XlsxDrawingAnchorApplier.ApplyToShape</c> PREFERS a positive xfrm ext over the
/// anchor's cell-span-derived size (correct for rotated shapes, where the anchor is the rotated bounding
/// box). On save, <c>XlsxSourceDrawingGeometryRewriter</c> only rewrote the anchor's <c>to</c> marker and
/// left the internal xfrm ext stale, so the next load read the stale (still-positive) ext back and it took
/// priority over the freshly-rewritten anchor -- silently discarding the resize. The fix teaches the shape
/// rewrite path to also patch <c>spPr/a:xfrm/a:ext</c> cx/cy to the model's current Width/Height, while
/// leaving an intentional zero axis of a line-like shape (a horizontal line has cy=0, a vertical line cx=0)
/// untouched (an axis is rewritten only when its source value was already positive).
/// </para>
/// </summary>
public sealed class Backlog_shape_xfrm_ext_stale_Tests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const long EmuPerPixel = 9525;

    // A plain prstGeom rectangle sized 96x96 px (914400 EMU) via its own internal xfrm ext, under a
    // twoCellAnchor. This is the exact shape shape family the bug report calls out.
    private const string RectangleAnchorXml = """
        <xdr:twoCellAnchor xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:to><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
          <xdr:sp macro="" textlink="">
            <xdr:nvSpPr>
              <xdr:cNvPr id="2" name="Rectangle 1"/>
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
        """;

    // A VERTICAL straight-line connector: its internal xfrm ext has cx=0 (intentionally flat on the
    // horizontal axis) and cy=914400 (96 px tall). ApplyToShape leaves the model's Width at its default
    // for a zero cx axis, so the model cannot faithfully reproduce the zero -- the rewriter must preserve
    // the source cx="0" rather than clobber it with a default-derived width.
    private const string VerticalLineAnchorXml = """
        <xdr:twoCellAnchor xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:to><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
          <xdr:cxnSp macro="">
            <xdr:nvCxnSpPr>
              <xdr:cNvPr id="3" name="Straight Connector 1"/>
              <xdr:cNvCxnSpPr/>
            </xdr:nvCxnSpPr>
            <xdr:spPr>
              <a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="914400"/></a:xfrm>
              <a:prstGeom prst="line"><a:avLst/></a:prstGeom>
              <a:ln><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></a:ln>
            </xdr:spPr>
          </xdr:cxnSp>
          <xdr:clientData/>
        </xdr:twoCellAnchor>
        """;

    [Fact]
    public void PrstGeomShape_Resize_PersistsAcrossFullSaveReloadCycle()
    {
        using var package = BuildPackageWithShapeAnchor(RectangleAnchorXml);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var shape = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        shape.Width.Should().BeApproximately(96, 0.5, "the source xfrm ext (914400 EMU) is 96 px");
        shape.Height.Should().BeApproximately(96, 0.5);

        var resizedWidth = shape.Width * 2;
        var resizedHeight = shape.Height * 2;
        shape.Width = resizedWidth;
        shape.Height = resizedHeight;

        // First save -> reload: before the fix, the anchor's to-marker was updated but the shape's own
        // internal xfrm ext was left stale, and ApplyToShape reads that stale (still-positive) ext back in
        // preference to the anchor -- so the reloaded shape snapped back to its ORIGINAL 96x96 size.
        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        reloaded.Width.Should().BeApproximately(resizedWidth, 0.5,
            "a resized source-loaded shape must keep its new width after a save+reload, not revert to the " +
            "stale internal xfrm ext");
        reloaded.Height.Should().BeApproximately(resizedHeight, 0.5,
            "a resized source-loaded shape must keep its new height after a save+reload");
    }

    [Fact]
    public void PrstGeomShape_Resize_UpdatesInternalXfrmExtInSavedXml()
    {
        using var package = BuildPackageWithShapeAnchor(RectangleAnchorXml);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var shape = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        shape.Width *= 2;   // 96 -> 192 px
        shape.Height *= 2;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var ext = drawingXml
            .Descendants(SpreadsheetDrawingNs + "sp")
            .Single()
            .Element(SpreadsheetDrawingNs + "spPr")!
            .Element(DrawingNs + "xfrm")!
            .Element(DrawingNs + "ext")!;

        ext.Attribute("cx")!.Value.Should().Be((192L * EmuPerPixel).ToString(),
            "the shape's internal xfrm ext cx must be rewritten to the resized width so the anchor bounding " +
            "box and the internal xfrm stay consistent");
        ext.Attribute("cy")!.Value.Should().Be((192L * EmuPerPixel).ToString(),
            "the shape's internal xfrm ext cy must be rewritten to the resized height");
    }

    [Fact]
    public void LineLikeShape_IntentionalZeroExtentAxis_IsPreservedNotClobberedOnSave()
    {
        using var package = BuildPackageWithShapeAnchor(VerticalLineAnchorXml);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var line = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        line.Kind.Should().Be(DrawingShapeKind.Line);
        line.Height.Should().BeApproximately(96, 0.5, "the source xfrm ext cy (914400 EMU) is 96 px");

        // Resize the (non-flat) height axis; the flat cx axis stays intentionally zero in the model.
        line.Height *= 2;   // 96 -> 192 px

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var ext = drawingXml
            .Descendants(SpreadsheetDrawingNs + "cxnSp")
            .Single()
            .Element(SpreadsheetDrawingNs + "spPr")!
            .Element(DrawingNs + "xfrm")!
            .Element(DrawingNs + "ext")!;

        ext.Attribute("cx")!.Value.Should().Be("0",
            "a line-like shape's intentional zero axis must NOT be clobbered -- ApplyToShape leaves the " +
            "model's Width at its default for a zero cx, so rewriting cx from the model would inject a bogus " +
            "width and destroy the vertical line's flatness");
        ext.Attribute("cy")!.Value.Should().Be((192L * EmuPerPixel).ToString(),
            "the non-flat height axis must still be rewritten to reflect the resize");
    }

    private static MemoryStream BuildPackageWithShapeAnchor(string anchorXml)
    {
        var workbook = new Workbook("ShapeXfrmExtResize");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var adapter = new XlsxFileAdapter();
        var package = new MemoryStream();
        adapter.Save(workbook, package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var drawingXml = new XDocument(
                new XElement(SpreadsheetDrawingNs + "wsDr", XElement.Parse(anchorXml)));
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
        document.Save(stream, SaveOptions.DisableFormatting);
    }
}
