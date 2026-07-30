using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R94 fix: <see cref="XlsxSourceDrawingGeometryRewriter"/>'s twoCellAnchor branch of
/// <c>RewriteAnchorGeometry</c> used to recompute the <c>to</c> marker on EVERY save for EVERY
/// source-loaded picture/shape/text box, gated only on "this sheet has at least one source-loaded
/// drawing object" -- never on whether THIS object was actually moved/resized. The recompute walks the
/// from-cell's pixel position and the marker target using the sheet's CURRENT column/row pixel sizes
/// (<c>SumColumnPixels</c>/<c>SumRowPixels</c>/<c>ToMarkerIndex</c>, which skip hidden/zero-size cells),
/// while the object's Width/Height it adds to that walk is the value cached once at LOAD time. Hiding or
/// resizing a row/column anywhere inside the object's own from/to span -- an ordinary user action (Format
/// &gt; Hide Column, or the Hide Columns/Rows context-menu command) that never touches the drawing object
/// itself -- desynchronizes those two: the walk now needs to travel PAST the collapsed cell to consume the
/// same (stale) pixel budget, silently landing the persisted <c>to</c> marker on a DIFFERENT cell than the
/// source file had. Real Excel never rewrites a twoCellAnchor's <c>to</c> marker for this reason -- it is a
/// literal cell+offset reference that Excel only overwrites on an explicit user drag-resize/move.
/// <para>
/// The fix captures each source-loaded object's Width/Height baseline at LOAD time
/// (<c>PictureModel.SourceLoadedWidthPixels</c>/<c>HeightPixels</c>, and the TextBoxModel/
/// DrawingShapeModel equivalents, populated by <c>XlsxDrawingAnchorApplier</c>) and skips the twoCellAnchor
/// <c>to</c>-marker recompute entirely whenever the live Width/Height still matches that baseline -- i.e.
/// nothing about the object's OWN geometry changed since load, regardless of what happened elsewhere on
/// the sheet.
/// </para>
/// These tests drive the real product entry points (<see cref="SetColumnsHiddenCommand"/>,
/// <see cref="SetRowsHiddenCommand"/>, <see cref="ResizePictureCommand"/>) through a full
/// <see cref="XlsxFileAdapter"/> save, not a hand-built model or XML fragment.
/// </summary>
public sealed class R94_TwoCellAnchorToMarkerHiddenSpanStabilityTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    // Picture spans columns B:D (0-based from.col=1, to.col=4) and rows 2:4 (0-based from.row=1,
    // to.row=4), with every column explicitly set to 10 chars (80px) and every row explicitly set to
    // 40px, so the anchor's implied pixel size is a clean, predictable 3*80=240 x 3*40=120.
    [Fact]
    public void R94_HidingColumnInsideAnchorSpan_NeverTouchingThePicture_LeavesToMarkerByteStable()
    {
        using var package = BuildPictureTwoCellAnchorPackage();
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Width.Should().BeApproximately(240, 0.5);
        picture.Height.Should().BeApproximately(120, 0.5);

        // The real product entry point for Format > Hide Column -- column C (1-based col 3) sits
        // strictly INSIDE the picture's own B:D span, but the picture itself is never touched.
        var ctx = new TestCommandContext(loaded);
        new SetColumnsHiddenCommand(sheet.Id, 3, 3, hidden: true).Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var to = drawingXml.Descendants(SpreadsheetDrawingNs + "twoCellAnchor").Single()
            .Element(SpreadsheetDrawingNs + "to")!;

        // Original file's to/col was 4 (0-based, column E) -- hiding an unrelated column inside the
        // span must NOT silently extend it to 5 (column F) to compensate for the hidden column's lost
        // pixel contribution. Real Excel leaves this marker completely untouched.
        to.Element(SpreadsheetDrawingNs + "col")!.Value.Should().Be("4",
            "hiding a column never touches the picture itself, so its persisted to-marker must stay exactly as authored");
        to.Element(SpreadsheetDrawingNs + "row")!.Value.Should().Be("4");
    }

    [Fact]
    public void R94_HidingRowInsideAnchorSpan_NeverTouchingThePicture_LeavesToMarkerByteStable()
    {
        using var package = BuildPictureTwoCellAnchorPackage();
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Height.Should().BeApproximately(120, 0.5);

        // Row 3 (1-based) sits strictly inside the picture's own row 2:4 span.
        var ctx = new TestCommandContext(loaded);
        new SetRowsHiddenCommand(sheet.Id, 3, 3, hidden: true).Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var to = drawingXml.Descendants(SpreadsheetDrawingNs + "twoCellAnchor").Single()
            .Element(SpreadsheetDrawingNs + "to")!;

        to.Element(SpreadsheetDrawingNs + "row")!.Value.Should().Be("4",
            "hiding a row never touches the picture itself, so its persisted to-marker must stay exactly as authored");
        to.Element(SpreadsheetDrawingNs + "col")!.Value.Should().Be("4");
    }

    /// <summary>
    /// No-regression sibling: a GENUINE resize (the real <see cref="ResizePictureCommand"/> entry point)
    /// must still move the to-marker, even when an unrelated column inside the old span was also hidden
    /// in the same edit session -- the R94 skip-gate must only suppress the recompute when the object's
    /// OWN geometry is unchanged from load, never when it genuinely was.
    /// </summary>
    [Fact]
    public void R94_GenuineResizeAlongsideHiddenColumn_StillMovesToMarker()
    {
        using var package = BuildPictureTwoCellAnchorPackage();
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);
        var picture = sheet.Pictures.Single();

        var ctx = new TestCommandContext(loaded);
        new SetColumnsHiddenCommand(sheet.Id, 3, 3, hidden: true).Apply(ctx).Success.Should().BeTrue();

        // A real user resize -- e.g. dragging the picture's handle -- doubles the width.
        new ResizePictureCommand(sheet.Id, picture.Id, picture.Width * 2, picture.Height)
            .Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var to = drawingXml.Descendants(SpreadsheetDrawingNs + "twoCellAnchor").Single()
            .Element(SpreadsheetDrawingNs + "to")!;

        to.Element(SpreadsheetDrawingNs + "col")!.Value.Should().NotBe("4",
            "a genuine resize must still recompute the to-marker outward to reflect the new width");

        // Full round-trip: the resized width itself must also have persisted correctly.
        saved.Position = 0;
        var reloaded = adapter.Load(saved).GetSheetAt(0).Pictures.Single();
        reloaded.Width.Should().BeApproximately(480, 1.0);
    }

    /// <summary>Baseline sanity: with no sheet changes at all, the to-marker is (and always was) byte stable.</summary>
    [Fact]
    public void R94_LoadThenSaveWithNoSheetChanges_ToMarkerByteStable()
    {
        using var package = BuildPictureTwoCellAnchorPackage();
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var to = drawingXml.Descendants(SpreadsheetDrawingNs + "twoCellAnchor").Single()
            .Element(SpreadsheetDrawingNs + "to")!;

        to.Element(SpreadsheetDrawingNs + "col")!.Value.Should().Be("4");
        to.Element(SpreadsheetDrawingNs + "row")!.Value.Should().Be("4");
    }

    private static MemoryStream BuildPictureTwoCellAnchorPackage()
    {
        var workbook = new Workbook("R94TwoCellAnchorHiddenSpan");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        for (uint col = 1; col <= 8; col++)
            sheet.ColumnWidths[col] = 10; // 10 chars * 8 = 80px, uniform and exact.
        for (uint row = 1; row <= 8; row++)
            sheet.RowHeights[row] = 40; // already in px, uniform and exact.

        var adapter = new XlsxFileAdapter();
        var package = new MemoryStream();
        adapter.Save(workbook, package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var mediaEntry = archive.CreateEntry("xl/media/image1.png", CompressionLevel.NoCompression);
            using (var mediaStream = mediaEntry.Open())
                mediaStream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            // from = col1(0-based, column B)/row1(0-based, row2); to = col4(0-based, column E)/row4
            // (0-based, row5). Span = columns B:D (3 cols * 80px = 240px), rows 2:4 (3 rows * 40px =
            // 120px), landing exactly on the to-marker below with zero sub-cell offset -- deliberately
            // chosen so the anchor's OWN encoded geometry is self-consistent with what
            // XlsxDrawingAnchorApplier.GetAnchorSize will derive from it at load time.
            var drawingXml = XDocument.Parse($"""
                <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNs}" xmlns:a="{DrawingNs}" xmlns:r="{RelNs}">
                  <xdr:twoCellAnchor>
                    <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>4</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                    <xdr:pic>
                      <xdr:nvPicPr>
                        <xdr:cNvPr id="2" name="Picture 1"/>
                        <xdr:cNvPicPr/>
                      </xdr:nvPicPr>
                      <xdr:blipFill>
                        <a:blip r:embed="rIdImage1"/>
                        <a:stretch><a:fillRect/></a:stretch>
                      </xdr:blipFill>
                      <xdr:spPr>
                        <a:xfrm><a:off x="0" y="0"/><a:ext cx="2286000" cy="1143000"/></a:xfrm>
                        <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                      </xdr:spPr>
                    </xdr:pic>
                    <xdr:clientData/>
                  </xdr:twoCellAnchor>
                </xdr:wsDr>
                """);
            WritePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);
            WritePackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels", XDocument.Parse($"""
                <Relationships xmlns="{PackageRelNs}">
                  <Relationship Id="rIdImage1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/image1.png"/>
                </Relationships>
                """));

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
            if (!contentTypesXml.Root!.Elements(ContentTypeNs + "Default").Any(e => e.Attribute("Extension")?.Value == "png"))
            {
                contentTypesXml.Root!.Add(new XElement(ContentTypeNs + "Default",
                    new XAttribute("Extension", "png"),
                    new XAttribute("ContentType", "image/png")));
            }

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
