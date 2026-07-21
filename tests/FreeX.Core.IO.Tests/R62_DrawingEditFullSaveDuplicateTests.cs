using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression guard for the source-package drawing-duplication bug discovered while fixing
/// R62-io-drawing-textbox-6-1. A drawing object (picture/text box/shape) loaded verbatim from an
/// .xlsx starts <c>IsSourceLoaded == true</c>, so <c>XlsxWorksheetDrawingObjectWriter</c> skips it and
/// its anchor is instead PRESERVED byte-for-byte out of the source package. Several format edits
/// (the R51 shape colour/gradient/effect commands, and the R62 text-box rotate/recolour commands)
/// deliberately CLEAR that flag so the writer reconstructs the object from the edited model. But the
/// writer emits its fresh anchor BEFORE <c>XlsxWorksheetDrawingPartMerger</c> copies the ORIGINAL
/// source anchors back in, and the merge keyed anchors on <c>{anchor-type}:{name}:{position}</c> — an
/// edit that changes the anchor type (source <c>twoCellAnchor</c> → writer <c>oneCellAnchor</c>) or
/// geometry makes the two look distinct, so BOTH ended up in the saved drawing part and the object was
/// DUPLICATED on reload.
/// <para>
/// These are full <c>Save()</c>+reload round-trips, not model-level <c>IsSourceLoaded</c> assertions:
/// the R51/R62 command tests only checked the flag flipped, which is exactly why the duplication went
/// unnoticed. The fix teaches the merge to drop a source anchor whose object the writer has already
/// re-emitted (matched by the stable <c>cNvPr</c> name the reader/writer round-trip), so exactly one
/// anchor survives.
/// </para>
/// </summary>
public sealed class R62_DrawingEditFullSaveDuplicateTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    [Fact]
    public void ShapeFillEdit_FullSaveReload_ProducesSingleShapeNotDuplicate()
    {
        using var package = BuildPackageWithDrawing(ShapeAnchor("Rectangle 1", "FF0000", fromCol: 1, toCol: 4));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var shape = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle(
            "the fixture drawing holds exactly one source-loaded shape").Subject;
        shape.IsSourceLoaded.Should().BeTrue("a shape loaded verbatim from the .xlsx starts source-loaded");

        // R51-io-picture-fill-shape-3-1: the REAL command a fill edit dispatches — it clears
        // IsSourceLoaded so the writer reconstructs the shape from the edited model.
        new SetDrawingShapeColorsCommand(loaded.GetSheetAt(0).Id, shape.Id, fillColor: new CellColor(0, 0, 0xFF), outlineColor: null)
            .Apply(new TestCommandContext(loaded))
            .Success.Should().BeTrue();
        shape.IsSourceLoaded.Should().BeFalse();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        // The saved drawing part must carry exactly one shape anchor — not the freshly written blue
        // anchor AND the stale red source anchor.
        CountShapeAnchorsInSavedDrawings(saved).Should().Be(1,
            "the writer's fresh anchor supersedes the original source anchor; both must not survive");

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedShape = reloaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle(
            "the edited shape must round-trip as a single object, not a duplicate").Subject;
        reloadedShape.FillColor.Should().Be(new CellColor(0, 0, 0xFF),
            "the surviving anchor must be the EDITED (blue) one, not the original (red) source anchor");
    }

    [Fact]
    public void ShapeFillEdit_FullSaveReload_LeavesUntouchedSourceSiblingIntact()
    {
        // Two distinct source-loaded shapes; only the first is edited. The fix must drop ONLY the
        // edited shape's stale source anchor, never a sibling whose original anchor is still the only
        // copy of it (the writer never re-emits a still-source-loaded object).
        using var package = BuildPackageWithDrawing(
            ShapeAnchor("Rectangle 1", "FF0000", fromCol: 1, toCol: 4) +
            ShapeAnchor("Rectangle 2", "00FF00", fromCol: 6, toCol: 9));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var sheet = loaded.GetSheetAt(0);
        sheet.DrawingShapes.Should().HaveCount(2);
        var edited = sheet.DrawingShapes.Single(shape => shape.Name == "Rectangle 1");

        new SetDrawingShapeColorsCommand(sheet.Id, edited.Id, fillColor: new CellColor(0, 0, 0xFF), outlineColor: null)
            .Apply(new TestCommandContext(loaded))
            .Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        CountShapeAnchorsInSavedDrawings(saved).Should().Be(2,
            "the edited shape is re-emitted once and the untouched sibling is preserved once");

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedShapes = reloaded.GetSheetAt(0).DrawingShapes;
        reloadedShapes.Should().HaveCount(2);
        reloadedShapes.Single(shape => shape.Name == "Rectangle 1").FillColor.Should().Be(new CellColor(0, 0, 0xFF),
            "the edited shape keeps its new blue fill");
        reloadedShapes.Single(shape => shape.Name == "Rectangle 2").FillColor.Should().Be(new CellColor(0, 0xFF, 0),
            "the untouched source sibling must survive with its ORIGINAL green fill, not be dropped");
    }

    [Fact]
    public void TextBoxRotationEdit_FullSaveReload_ProducesSingleTextBoxNotDuplicate()
    {
        using var package = BuildPackageWithDrawing(TextBoxAnchor("TextBox 1", "Hello", "FF0000", fromCol: 1, toCol: 4));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var textBox = loaded.GetSheetAt(0).TextBoxes.Should().ContainSingle(
            "the fixture drawing holds exactly one source-loaded text box").Subject;
        textBox.IsSourceLoaded.Should().BeTrue();

        // A rotate/recolour edit through the real commands. R62-io-drawing-textbox-6-1 (landing
        // separately) makes these commands clear IsSourceLoaded; that command change is not in this
        // worktree, so clear the flag explicitly here to exercise the same save path R62 produces —
        // the point of THIS test is the save-pipeline duplication, independent of which command flips.
        var sheetId = loaded.GetSheetAt(0).Id;
        new RotateTextBoxCommand(sheetId, textBox.Id, 30).Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        new SetTextBoxColorsCommand(sheetId, textBox.Id, fillColor: new CellColor(0, 0xFF, 0), outlineColor: null)
            .Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        textBox.IsSourceLoaded = false;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        CountShapeAnchorsInSavedDrawings(saved).Should().Be(1,
            "the writer's fresh rotated anchor supersedes the original source anchor; both must not survive");

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedTextBox = reloaded.GetSheetAt(0).TextBoxes.Should().ContainSingle(
            "the edited text box must round-trip as a single object, not a duplicate").Subject;
        reloadedTextBox.RotationDegrees.Should().BeApproximately(30, 0.5,
            "the surviving anchor must be the EDITED (rotated) one, not the original unrotated source anchor");
        reloadedTextBox.Text.Should().Be("Hello");
    }

    private static int CountShapeAnchorsInSavedDrawings(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries
            .Where(entry =>
                entry.FullName.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                !entry.FullName.Contains("/_rels/", StringComparison.OrdinalIgnoreCase))
            .Sum(entry => XlsxPackageTestFixtures.LoadPackageXml(entry)
                .Descendants(SpreadsheetDrawingNs + "sp")
                .Count());
    }

    private static string ShapeAnchor(string name, string fillHex, int fromCol, int toCol) => $"""
        <xdr:twoCellAnchor>
          <xdr:from><xdr:col>{fromCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:to><xdr:col>{toCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
          <xdr:sp macro="" textlink="">
            <xdr:nvSpPr>
              <xdr:cNvPr id="{fromCol + 10}" name="{name}"/>
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

    private static string TextBoxAnchor(string name, string text, string fillHex, int fromCol, int toCol) => $"""
        <xdr:twoCellAnchor>
          <xdr:from><xdr:col>{fromCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:to><xdr:col>{toCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
          <xdr:sp macro="" textlink="">
            <xdr:nvSpPr>
              <xdr:cNvPr id="{fromCol + 20}" name="{name}"/>
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

    private static MemoryStream BuildPackageWithDrawing(string anchorsXml)
    {
        var workbook = new Workbook("DrawingEditFullSaveDuplicate");
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
