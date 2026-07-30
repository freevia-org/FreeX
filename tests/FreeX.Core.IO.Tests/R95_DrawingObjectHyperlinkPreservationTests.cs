using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R95-io-drawing-hyperlink-2-2: a hyperlink on a shape/text-box/picture's <c>xdr:cNvPr</c> (an
/// <c>a:hlinkClick</c>) survives a save ONLY while the object stays <c>IsSourceLoaded == true</c>,
/// because in that state the whole drawing part is preserved verbatim out of the source package.
/// Several ordinary, supported editing commands (fill/outline colour, gradient, effect on a shape;
/// colour/rotation on a text box -- see <c>DrawingShapeFormatCommands.cs</c>/<c>TextBoxCommands.cs</c>)
/// deliberately CLEAR that flag so the writer reconstructs the object's anchor from the (now-edited)
/// model. <see cref="DrawingShapeModel"/>/<see cref="TextBoxModel"/>/<see cref="PictureModel"/> have no
/// Hyperlink property to carry the link across that reconstruction, so
/// <c>XlsxWorksheetDrawingObjectWriter</c> silently dropped it forever, even though nothing about the
/// edit (a colour change) has anything to do with the hyperlink. The fix mirrors
/// <c>XlsxWorksheetChartWriter</c>'s existing R41 fix for the identical bug on chart graphicFrames:
/// read each hyperlink from the CURRENT (pre-rebuild) drawing bytes, keyed by the object's stable
/// <c>cNvPr@name</c>, and re-attach it (via a freshly allocated relationship) to the rebuilt anchor.
/// </summary>
public sealed class R95_DrawingObjectHyperlinkPreservationTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string HyperlinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    // --- Primary finding: a shape-colour edit through the REAL command must not drop the shape's
    // --- pre-existing object hyperlink. This is the fail-before/pass-after test.

    [Fact]
    public void ShapeColorEdit_FullSaveReload_PreservesObjectHyperlink()
    {
        using var package = BuildPackageWithDrawing(
            ShapeAnchor("Rectangle 1", "FF0000", fromCol: 1, toCol: 4, hlinkRelId: "rIdHlink1"),
            ("rIdHlink1", "https://example.com/shape-link", "External"));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var shape = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        shape.IsSourceLoaded.Should().BeTrue("a shape loaded verbatim from the .xlsx starts source-loaded");

        // The REAL command a fill edit dispatches -- it clears IsSourceLoaded so the writer
        // reconstructs the shape from the edited model instead of preserving the source XML verbatim.
        new SetDrawingShapeColorsCommand(loaded.GetSheetAt(0).Id, shape.Id, fillColor: new CellColor(0, 0, 0xFF), outlineColor: null)
            .Apply(new TestCommandContext(loaded))
            .Success.Should().BeTrue();
        shape.IsSourceLoaded.Should().BeFalse();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var (target, targetMode) = ResolveSingleShapeHyperlink(saved);
        target.Should().Be("https://example.com/shape-link",
            "the shape's object-level hyperlink must survive the colour-edit-triggered anchor rebuild");
        targetMode.Should().Be("External");
    }

    // --- No-regression sibling: the same edit path when the source shape has NO hyperlink must not
    // --- invent one, and the edited colour must still be the one that survives.

    [Fact]
    public void ShapeColorEdit_FullSaveReload_DoesNotInventHyperlinkWhenSourceHasNone()
    {
        using var package = BuildPackageWithDrawing(
            ShapeAnchor("Rectangle 1", "FF0000", fromCol: 1, toCol: 4, hlinkRelId: null));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var shape = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        new SetDrawingShapeColorsCommand(loaded.GetSheetAt(0).Id, shape.Id, fillColor: new CellColor(0, 0, 0xFF), outlineColor: null)
            .Apply(new TestCommandContext(loaded))
            .Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var cNvPr = SingleShapeCNvPr(archive);
        cNvPr.Element(DrawingNs + "hlinkClick").Should().BeNull(
            "no hyperlink existed on the source shape, so none should be invented");

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject.FillColor
            .Should().Be(new CellColor(0, 0, 0xFF), "the edit itself must still take effect");
    }

    // --- Sibling object type: a text-box colour edit must not drop the text box's hyperlink either.

    [Fact]
    public void TextBoxColorEdit_FullSaveReload_PreservesObjectHyperlink()
    {
        using var package = BuildPackageWithDrawing(
            TextBoxAnchor("TextBox 1", "Hello", "FF0000", fromCol: 1, toCol: 4, hlinkRelId: "rIdHlink1"),
            ("rIdHlink1", "https://example.com/textbox-link", "External"));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var sheetId = loaded.GetSheetAt(0).Id;
        var textBox = loaded.GetSheetAt(0).TextBoxes.Should().ContainSingle().Subject;
        textBox.IsSourceLoaded.Should().BeTrue();

        new SetTextBoxColorsCommand(sheetId, textBox.Id, fillColor: new CellColor(0, 0xFF, 0), outlineColor: null)
            .Apply(new TestCommandContext(loaded))
            .Success.Should().BeTrue();
        textBox.IsSourceLoaded.Should().BeFalse(
            "SetTextBoxColorsCommand must clear IsSourceLoaded so the writer reconstructs the text box");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var (target, targetMode) = ResolveSingleShapeHyperlink(saved);
        target.Should().Be("https://example.com/textbox-link",
            "the text box's object-level hyperlink must survive the colour-edit-triggered anchor rebuild");
        targetMode.Should().Be("External");
    }

    private static (string Target, string? TargetMode) ResolveSingleShapeHyperlink(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var cNvPr = SingleShapeCNvPr(archive);
        var hlinkClick = cNvPr.Element(DrawingNs + "hlinkClick");
        hlinkClick.Should().NotBeNull("the rebuilt anchor must carry the preserved hlinkClick");
        var relId = hlinkClick!.Attribute(RelNs + "id")!.Value;

        var drawingPath = archive.Entries
            .Select(e => e.FullName)
            .First(name => name.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) &&
                           name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                           !name.Contains("/_rels/", StringComparison.OrdinalIgnoreCase));
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, relsPath);
        var relationship = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .First(r => r.Attribute("Id")!.Value == relId);
        return (relationship.Attribute("Target")!.Value, relationship.Attribute("TargetMode")?.Value);
    }

    private static XElement SingleShapeCNvPr(ZipArchive archive)
    {
        var drawingPath = archive.Entries
            .Select(e => e.FullName)
            .First(name => name.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) &&
                           name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                           !name.Contains("/_rels/", StringComparison.OrdinalIgnoreCase));
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingPath);
        return drawingXml.Descendants(SpreadsheetDrawingNs + "sp").Single()
            .Element(SpreadsheetDrawingNs + "nvSpPr")!
            .Element(SpreadsheetDrawingNs + "cNvPr")!;
    }

    private static string ShapeAnchor(string name, string fillHex, int fromCol, int toCol, string? hlinkRelId) => $"""
        <xdr:twoCellAnchor>
          <xdr:from><xdr:col>{fromCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:to><xdr:col>{toCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
          <xdr:sp macro="" textlink="">
            <xdr:nvSpPr>
              <xdr:cNvPr id="{fromCol + 10}" name="{name}">{HlinkClickXml(hlinkRelId)}</xdr:cNvPr>
              <xdr:cNvSpPr/>
            </xdr:nvSpPr>
            <xdr:spPr>
              <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
              <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
              <a:solidFill><a:srgbClr val="{fillHex}"/></a:solidFill>
            </xdr:spPr>
          </xdr:sp>
          <xdr:clientData/>
        </xdr:twoCellAnchor>
        """;

    private static string TextBoxAnchor(string name, string text, string fillHex, int fromCol, int toCol, string? hlinkRelId) => $"""
        <xdr:twoCellAnchor>
          <xdr:from><xdr:col>{fromCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:to><xdr:col>{toCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
          <xdr:sp macro="" textlink="">
            <xdr:nvSpPr>
              <xdr:cNvPr id="{fromCol + 20}" name="{name}">{HlinkClickXml(hlinkRelId)}</xdr:cNvPr>
              <xdr:cNvSpPr txBox="1"/>
            </xdr:nvSpPr>
            <xdr:spPr>
              <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
              <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
              <a:solidFill><a:srgbClr val="{fillHex}"/></a:solidFill>
            </xdr:spPr>
            <xdr:txBody>
              <a:bodyPr/>
              <a:lstStyle/>
              <a:p><a:r><a:t>{text}</a:t></a:r></a:p>
            </xdr:txBody>
          </xdr:sp>
          <xdr:clientData/>
        </xdr:twoCellAnchor>
        """;

    private static string HlinkClickXml(string? hlinkRelId) =>
        hlinkRelId is null ? "" : $"""<a:hlinkClick r:id="{hlinkRelId}"/>""";

    private static MemoryStream BuildPackageWithDrawing(string anchorsXml, params (string Id, string Target, string TargetMode)[] hyperlinkRelationships)
    {
        var workbook = new Workbook("DrawingObjectHyperlinkPreservation");
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
                {anchorsXml}
                </xdr:wsDr>
                """);
            WritePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);

            var relsXml = new XDocument(new XElement(PackageRelNs + "Relationships",
                hyperlinkRelationships.Select(rel => new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", rel.Id),
                    new XAttribute("Type", HyperlinkRelationshipType),
                    new XAttribute("Target", rel.Target),
                    new XAttribute("TargetMode", rel.TargetMode)))));
            WritePackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels", relsXml);

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
