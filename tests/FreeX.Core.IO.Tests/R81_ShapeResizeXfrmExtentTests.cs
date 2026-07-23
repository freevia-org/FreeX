using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Backlog item "shape-resize-xfrm-ext" (r73 chip): resizing a source-loaded DrawingML shape/connector
/// that carries an explicit <c>a:ext</c> (either its own <c>spPr/a:xfrm/a:ext</c>, or the anchor's own
/// <c>xdr:ext</c> on a oneCellAnchor/absoluteAnchor) must persist across save+reload instead of reverting
/// to the original extent or getting silently corrupted.
/// <para>
/// Investigation found TWO concrete, independent gaps in <see cref="XlsxSourceDrawingGeometryRewriter"/>
/// (the "xfrm-extent sibling" of the r78 grpSp/geometry work):
/// </para>
/// <list type="number">
/// <item><b>Document-order mismatch (fixed by this round):</b> <c>RewriteDrawingGeometry</c> built its
/// <c>shapeElements</c> candidate list via TWO SEPARATE <c>Descendants()</c> passes -- every
/// <c>&lt;xdr:sp&gt;</c> first, THEN every <c>&lt;xdr:cxnSp&gt;</c> -- while the reader
/// (<c>XlsxWorksheetDrawingParts.ReadShapeAndTextBoxParts</c>, R78-io-shape-geometry-5-2) walks both in a
/// SINGLE combined document-order pass. Whenever a drawing part mixed a connector before a later shape (or
/// any order other than "every sp before every cxnSp"), the two lists desynchronized and the positional
/// <c>Zip</c> below silently paired the WRONG model with the WRONG XML element -- a resize applied to one
/// shape's model got written onto a completely different shape/connector's anchor and internal xfrm ext.
/// </item>
/// <item><b>Grouped line-like zero-axis clobber (fixed by this round):</b> <c>RewriteGroupChildGeometry</c>
/// (R78-io-drawing-grpsp-move) rewrote a grouped child's local <c>a:ext</c> cx/cy unconditionally, unlike
/// its non-grouped sibling <c>RewriteShapeXfrmExtent</c> (backlog "shape-xfrm-ext-stale"), which only
/// touches an axis whose SOURCE value was already positive. A grouped horizontal/vertical line-like
/// connector has an intentional cx=0 or cy=0 that the model can never faithfully reproduce (it keeps its
/// default, non-zero Width/Height there) -- so the ungated group-child rewrite clobbered that intentional
/// zero with the model's bogus default size on every save, turning a flat grouped line diagonal.
/// </item>
/// </list>
/// </summary>
public sealed class R81_ShapeResizeXfrmExtentTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const long EmuPerPixel = 9525;

    // Two INDEPENDENT (non-grouped) twoCellAnchors, in the "wrong" order relative to the old buggy
    // rewriter's assumption that every <xdr:sp> precedes every <xdr:cxnSp>: a connector authored FIRST,
    // then a shape authored SECOND.
    private const string ConnectorThenShapeXml = """
        <xdr:twoCellAnchor xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:to><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
          <xdr:cxnSp macro="">
            <xdr:nvCxnSpPr><xdr:cNvPr id="2" name="Connector A"/><xdr:cNvCxnSpPr/></xdr:nvCxnSpPr>
            <xdr:spPr>
              <a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="914400"/></a:xfrm>
              <a:prstGeom prst="line"><a:avLst/></a:prstGeom>
              <a:ln><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></a:ln>
            </xdr:spPr>
          </xdr:cxnSp>
          <xdr:clientData/>
        </xdr:twoCellAnchor>
        """;

    private const string ShapeSecondAnchorXml = """
        <xdr:twoCellAnchor xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <xdr:from><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:to><xdr:col>8</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
          <xdr:sp macro="" textlink="">
            <xdr:nvSpPr><xdr:cNvPr id="3" name="Shape B"/><xdr:cNvSpPr/></xdr:nvSpPr>
            <xdr:spPr>
              <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
              <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
              <a:solidFill><a:srgbClr val="00FF00"/></a:solidFill>
            </xdr:spPr>
          </xdr:sp>
          <xdr:clientData/>
        </xdr:twoCellAnchor>
        """;

    // A oneCellAnchor shape whose own internal spPr/a:xfrm/a:ext matches the anchor's own xdr:ext.
    private const string OneCellShapeXml = """
        <xdr:oneCellAnchor xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:ext cx="914400" cy="914400"/>
          <xdr:sp macro="" textlink="">
            <xdr:nvSpPr><xdr:cNvPr id="2" name="Rectangle 1"/><xdr:cNvSpPr/></xdr:nvSpPr>
            <xdr:spPr>
              <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
              <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
              <a:solidFill><a:srgbClr val="00FF00"/></a:solidFill>
            </xdr:spPr>
          </xdr:sp>
          <xdr:clientData/>
        </xdr:oneCellAnchor>
        """;

    // An absoluteAnchor shape whose own internal spPr/a:xfrm/a:ext matches the anchor's own xdr:ext.
    private const string AbsoluteShapeXml = """
        <xdr:absoluteAnchor xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <xdr:pos x="200000" y="100000"/>
          <xdr:ext cx="914400" cy="914400"/>
          <xdr:sp macro="" textlink="">
            <xdr:nvSpPr><xdr:cNvPr id="2" name="Rectangle 1"/><xdr:cNvSpPr/></xdr:nvSpPr>
            <xdr:spPr>
              <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
              <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
              <a:solidFill><a:srgbClr val="00FF00"/></a:solidFill>
            </xdr:spPr>
          </xdr:sp>
          <xdr:clientData/>
        </xdr:absoluteAnchor>
        """;

    // A grouped VERTICAL line connector (intentional cx=0) sitting alongside a grouped rectangle, under a
    // shared twoCellAnchor. Group ext == chExt (scale 1) so the local/absolute math is a pure identity.
    private const string GroupedVerticalLineAndRectangleXml = """
        <xdr:twoCellAnchor xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:to><xdr:col>20</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>20</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
          <xdr:grpSp>
            <xdr:nvGrpSpPr><xdr:cNvPr id="100" name="Group 1"/><xdr:cNvGrpSpPr/></xdr:nvGrpSpPr>
            <xdr:grpSpPr>
              <a:xfrm>
                <a:off x="0" y="0"/><a:ext cx="1828800" cy="1828800"/>
                <a:chOff x="0" y="0"/><a:chExt cx="1828800" cy="1828800"/>
              </a:xfrm>
            </xdr:grpSpPr>
            <xdr:cxnSp macro="">
              <xdr:nvCxnSpPr><xdr:cNvPr id="2" name="Vertical Connector 1"/><xdr:cNvCxnSpPr/></xdr:nvCxnSpPr>
              <xdr:spPr>
                <a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="914400"/></a:xfrm>
                <a:prstGeom prst="line"><a:avLst/></a:prstGeom>
                <a:ln><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></a:ln>
              </xdr:spPr>
            </xdr:cxnSp>
            <xdr:sp macro="" textlink="">
              <xdr:nvSpPr><xdr:cNvPr id="3" name="Rectangle Child"/><xdr:cNvSpPr/></xdr:nvSpPr>
              <xdr:spPr>
                <a:xfrm><a:off x="914400" y="914400"/><a:ext cx="457200" cy="457200"/></a:xfrm>
                <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                <a:solidFill><a:srgbClr val="00FF00"/></a:solidFill>
              </xdr:spPr>
            </xdr:sp>
          </xdr:grpSp>
          <xdr:clientData/>
        </xdr:twoCellAnchor>
        """;

    [Fact]
    public void MixedConnectorThenShapeOrder_ResizingTheShape_UpdatesTheShapesOwnTwoCellAnchor_ConnectorUnaffected()
    {
        var adapter = new XlsxFileAdapter();
        using var package = BuildPackage(ConnectorThenShapeXml, ShapeSecondAnchorXml);
        var loaded = adapter.Load(package);

        var sheet = loaded.GetSheetAt(0);
        sheet.DrawingShapes.Should().HaveCount(2);
        var shapeB = sheet.DrawingShapes.Single(s => s.Name == "Shape B");
        shapeB.Width.Should().BeApproximately(96, 0.5);
        shapeB.Height.Should().BeApproximately(96, 0.5);
        shapeB.Width *= 2;   // 96 -> 192 px
        shapeB.Height *= 2;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");

        // The resized shape's OWN internal xfrm ext must reflect the new size -- before the
        // document-order fix, the positional Zip between shapeElements (built via two separate
        // Descendants() passes: all <sp> then all <cxnSp>) and sourceShapes (built by the reader's
        // single combined document-order pass) desynchronized here, silently writing Shape B's resize
        // onto Connector A's XML element instead.
        var shapeBElement = drawingXml.Descendants(SpreadsheetDrawingNs + "sp")
            .Single(e => e.Descendants(SpreadsheetDrawingNs + "cNvPr").Any(c => c.Attribute("name")?.Value == "Shape B"));
        var shapeBExt = shapeBElement.Element(SpreadsheetDrawingNs + "spPr")!.Element(DrawingNs + "xfrm")!.Element(DrawingNs + "ext")!;
        shapeBExt.Attribute("cx")!.Value.Should().Be((192L * EmuPerPixel).ToString(),
            "the resized shape's own internal xfrm ext must reflect the new width, not a value swapped in from a different element");
        shapeBExt.Attribute("cy")!.Value.Should().Be((192L * EmuPerPixel).ToString());

        // Shape B's own twoCellAnchor 'to' marker must also have moved out to the new size.
        var shapeBAnchor = drawingXml.Descendants(SpreadsheetDrawingNs + "twoCellAnchor")
            .Single(a => a.Descendants(SpreadsheetDrawingNs + "cNvPr").Any(c => c.Attribute("name")?.Value == "Shape B"));
        var shapeBTo = shapeBAnchor.Element(SpreadsheetDrawingNs + "to")!;
        shapeBTo.Element(SpreadsheetDrawingNs + "col")!.Value.Should().NotBe("8",
            "the to-marker must move out to reflect the doubled width instead of staying at the original span");

        // No-regression: Connector A's OWN internal ext -- driven purely by Connector A's own (untouched)
        // model Width/Height via RewriteShapeXfrmExtent -- must reflect ONLY its own geometry, never a
        // value swapped in from Shape B. (Connector A's outer twoCellAnchor 'to' marker is intentionally
        // NOT asserted here: RewriteAnchorGeometry always recomputes a twoCellAnchor's 'to' marker from
        // the model's current size on every save -- see its own doc comment -- which is unrelated,
        // pre-existing behavior independent of this fix.)
        var connectorElement = drawingXml.Descendants(SpreadsheetDrawingNs + "cxnSp").Single();
        var connectorExt = connectorElement.Element(SpreadsheetDrawingNs + "spPr")!.Element(DrawingNs + "xfrm")!.Element(DrawingNs + "ext")!;
        connectorExt.Attribute("cx")!.Value.Should().Be("0",
            "Connector A's own ext must stay driven by its own (untouched, intentionally-flat) model width, not Shape B's");
        connectorExt.Attribute("cy")!.Value.Should().Be((96L * EmuPerPixel).ToString(),
            "Connector A's own ext height must reflect its own (untouched) model height, not Shape B's edited height");

        // Full round-trip: reload and confirm the model geometry itself persisted correctly too.
        saved.Position = 0;
        var reloadedSheet = adapter.Load(saved).GetSheetAt(0);
        reloadedSheet.DrawingShapes.Single(s => s.Name == "Shape B").Width.Should().BeApproximately(192, 0.5);
        reloadedSheet.DrawingShapes.Single(s => s.Name == "Shape B").Height.Should().BeApproximately(192, 0.5);
        reloadedSheet.DrawingShapes.Single(s => s.Name == "Connector A").Height.Should().BeApproximately(96, 0.5);
    }

    [Fact]
    public void GroupedVerticalLineConnector_ResizeNonFlatAxis_FlatAxisStaysZero_NotClobberedByGroupChildRewrite()
    {
        using var package = BuildPackage(GroupedVerticalLineAndRectangleXml);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var line = loaded.GetSheetAt(0).DrawingShapes.Single(s => s.Kind == DrawingShapeKind.Line);
        line.Height.Should().BeApproximately(96, 0.5, "the source xfrm ext cy (914400 EMU) is 96 px");
        line.Height *= 2;   // resize the non-flat axis only; the flat cx axis is never captured in the model

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var connectorExt = drawingXml.Descendants(SpreadsheetDrawingNs + "cxnSp").Single()
            .Element(SpreadsheetDrawingNs + "spPr")!.Element(DrawingNs + "xfrm")!.Element(DrawingNs + "ext")!;

        connectorExt.Attribute("cx")!.Value.Should().Be("0",
            "a grouped line-like connector's intentional zero axis must not be clobbered by " +
            "RewriteGroupChildGeometry rewriting both axes unconditionally from the model's " +
            "(bogus, default-derived) width");
        connectorExt.Attribute("cy")!.Value.Should().Be((192L * EmuPerPixel).ToString(),
            "the non-flat height axis must still reflect the resize");
    }

    [Fact]
    public void GroupedVerticalLineConnector_LoadThenSaveWithNoEdits_ExtIsCompletelyByteStable()
    {
        using var package = BuildPackage(GroupedVerticalLineAndRectangleXml);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var connectorExt = drawingXml.Descendants(SpreadsheetDrawingNs + "cxnSp").Single()
            .Element(SpreadsheetDrawingNs + "spPr")!.Element(DrawingNs + "xfrm")!.Element(DrawingNs + "ext")!;
        var rectExt = drawingXml.Descendants(SpreadsheetDrawingNs + "sp").Single()
            .Element(SpreadsheetDrawingNs + "spPr")!.Element(DrawingNs + "xfrm")!.Element(DrawingNs + "ext")!;

        connectorExt.Attribute("cx")!.Value.Should().Be("0",
            "no edits were made -- the connector's flat axis must not be clobbered on a plain save");
        connectorExt.Attribute("cy")!.Value.Should().Be("914400");
        rectExt.Attribute("cx")!.Value.Should().Be("457200",
            "no edits were made -- the sibling rectangle's own ext must stay byte-stable too");
        rectExt.Attribute("cy")!.Value.Should().Be("457200");
    }

    [Fact]
    public void OneCellAnchorShape_Resize_InternalXfrmExtAndAnchorExtBothUpdated()
    {
        using var package = BuildPackage(OneCellShapeXml);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var shape = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        shape.Width.Should().BeApproximately(96, 0.5);
        shape.Width *= 2;
        shape.Height *= 2;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var internalExt = drawingXml.Descendants(SpreadsheetDrawingNs + "sp").Single()
            .Element(SpreadsheetDrawingNs + "spPr")!.Element(DrawingNs + "xfrm")!.Element(DrawingNs + "ext")!;
        var anchorExt = drawingXml.Descendants(SpreadsheetDrawingNs + "oneCellAnchor").Single()
            .Element(SpreadsheetDrawingNs + "ext")!;

        internalExt.Attribute("cx")!.Value.Should().Be((192L * EmuPerPixel).ToString(),
            "the shape's own internal xfrm ext must reflect the resize on a oneCellAnchor shape too");
        internalExt.Attribute("cy")!.Value.Should().Be((192L * EmuPerPixel).ToString());
        anchorExt.Attribute("cx")!.Value.Should().Be((192L * EmuPerPixel).ToString(),
            "the oneCellAnchor's own xdr:ext must also reflect the resize");
        anchorExt.Attribute("cy")!.Value.Should().Be((192L * EmuPerPixel).ToString());
    }

    [Fact]
    public void AbsoluteAnchorShape_Resize_InternalXfrmExtAndAnchorExtBothUpdated()
    {
        using var package = BuildPackage(AbsoluteShapeXml);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var shape = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        shape.Width.Should().BeApproximately(96, 0.5);
        shape.Width *= 2;
        shape.Height *= 2;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var internalExt = drawingXml.Descendants(SpreadsheetDrawingNs + "sp").Single()
            .Element(SpreadsheetDrawingNs + "spPr")!.Element(DrawingNs + "xfrm")!.Element(DrawingNs + "ext")!;
        var anchorExt = drawingXml.Descendants(SpreadsheetDrawingNs + "absoluteAnchor").Single()
            .Element(SpreadsheetDrawingNs + "ext")!;

        internalExt.Attribute("cx")!.Value.Should().Be((192L * EmuPerPixel).ToString(),
            "the shape's own internal xfrm ext must reflect the resize on an absoluteAnchor shape too");
        internalExt.Attribute("cy")!.Value.Should().Be((192L * EmuPerPixel).ToString());
        anchorExt.Attribute("cx")!.Value.Should().Be((192L * EmuPerPixel).ToString(),
            "the absoluteAnchor's own xdr:ext must also reflect the resize");
        anchorExt.Attribute("cy")!.Value.Should().Be((192L * EmuPerPixel).ToString());
    }

    [Fact]
    public void SingleSourceLoadedShape_SavedWithoutResize_ExtStaysByteStable()
    {
        using var package = BuildPackage(OneCellShapeXml);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        // No edit at all -- just load, then immediately save.
        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var internalExt = drawingXml.Descendants(SpreadsheetDrawingNs + "sp").Single()
            .Element(SpreadsheetDrawingNs + "spPr")!.Element(DrawingNs + "xfrm")!.Element(DrawingNs + "ext")!;
        var anchorExt = drawingXml.Descendants(SpreadsheetDrawingNs + "oneCellAnchor").Single()
            .Element(SpreadsheetDrawingNs + "ext")!;

        internalExt.Attribute("cx")!.Value.Should().Be("914400", "a non-resized shape's internal ext must stay byte-stable");
        internalExt.Attribute("cy")!.Value.Should().Be("914400");
        anchorExt.Attribute("cx")!.Value.Should().Be("914400", "a non-resized shape's anchor ext must stay byte-stable");
        anchorExt.Attribute("cy")!.Value.Should().Be("914400");
    }

    private static MemoryStream BuildPackage(params string[] anchorXmlFragments)
    {
        var workbook = new Workbook("R81ShapeResizeXfrmExtent");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var adapter = new XlsxFileAdapter();
        var package = new MemoryStream();
        adapter.Save(workbook, package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var drawingXml = new XDocument(
                new XElement(SpreadsheetDrawingNs + "wsDr", anchorXmlFragments.Select(XElement.Parse)));
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
