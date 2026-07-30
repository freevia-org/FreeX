using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R94-hidden-span fix: when a source-loaded picture/text-box/shape's <c>twoCellAnchor</c> span falls
/// ENTIRELY within rows/columns that are already hidden in the SOURCE FILE (not hidden later by a command
/// -- hidden from the very first load), <c>XlsxDrawingAnchorApplier.GetAnchorSize</c>'s
/// <c>SumColumnPixels</c>/<c>SumRowPixels</c> walk skips every spanned column/row and returns 0 for that
/// axis. <c>ApplyToPicture</c>/<c>ApplyToTextBox</c>/<c>ApplyToShape</c> guard the model assignment with
/// <c>if (width &gt; 0)</c>, so the model correctly keeps its class-default size on that axis (240x140 for
/// a picture) rather than being set to a bogus zero -- that part is fine. The bug was that the SAME guard
/// also gated whether <c>SourceLoadedWidthPixels</c>/<c>HeightPixels</c> got captured at all, leaving them
/// <see langword="null"/> for such an object. <see cref="XlsxSourceDrawingGeometryRewriter"/>'s
/// twoCellAnchor <c>to</c>-marker skip-gate (see <c>R94_TwoCellAnchorToMarkerHiddenSpanStabilityTests</c>)
/// only skips the recompute when the baseline is non-null AND matches the live Width/Height -- a null
/// baseline always fails that check, so on SAVE the rewriter fell through to recomputing the `to` marker
/// from the class-default 240x140 size, walking past the still-hidden span and relocating the marker to an
/// unrelated, distant cell that has no relation to where the object was actually anchored in the source
/// file.
/// <para>
/// The fix (in <c>XlsxDrawingAnchorApplier</c>) captures the baseline as whatever Width/Height the model
/// ends up with AFTER the <c>&gt; 0</c> guard -- including the retained class-default for a hidden-span
/// object -- instead of only when the computed value was itself positive. This makes the baseline
/// trivially equal to the live value for a never-resized object regardless of hidden state, so the
/// save-time skip-gate correctly preserves the original `to` marker verbatim.
/// </para>
/// These tests drive the real product entry points (<see cref="XlsxFileAdapter"/> load/save,
/// <see cref="ResizePictureCommand"/>), not a hand-built model or XML fragment.
/// </summary>
public sealed class R94_HiddenSpanAnchorSizeBaselineTests
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

    /// <summary>
    /// The failing case: a picture's own B:D/row-2:4 anchor span is hidden from the very first load (the
    /// SOURCE file already has those columns/rows marked hidden) -- the picture itself is never touched by
    /// any command. A sheet edit that is unrelated to the picture (hiding a DIFFERENT column, well outside
    /// the picture's own span -- the real <see cref="SetColumnsHiddenCommand"/> entry point) still forces
    /// <c>XlsxSourceDrawingGeometryRewriter</c> to walk every source-loaded drawing anchor on the sheet on
    /// save. That walk must leave THIS picture's persisted `to` marker exactly as authored, matching real
    /// Excel (which never rewrites a twoCellAnchor's `to` marker absent an explicit user move/resize of
    /// that specific object).
    /// </summary>
    [Fact]
    public void R94_PictureAnchorEntirelyWithinHiddenColumnsAndRowsFromLoad_UnrelatedSheetEdit_LeavesToMarkerByteStable()
    {
        using var package = BuildPictureTwoCellAnchorPackage(hideAnchorSpanFromLoad: true);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);

        var picture = sheet.Pictures.Should().ContainSingle().Subject;

        // Sanity: the anchor's own span really is entirely hidden, so GetAnchorSize computed 0 on both
        // axes and the model correctly retained its class-default size (this part was never broken).
        picture.Width.Should().Be(240, "the anchor's own span is entirely hidden, so no real pixel size could be derived and the class default is retained");
        picture.Height.Should().Be(140);

        // Real product entry point for Format > Hide Column -- column 7 is well OUTSIDE the picture's own
        // hidden 2:4 span, and the picture itself is never touched. This forces a real drawing-geometry
        // rewrite pass on save (an untouched sheet short-circuits before ever reaching per-picture logic).
        var ctx = new TestCommandContext(loaded);
        new SetColumnsHiddenCommand(sheet.Id, 7, 7, hidden: true).Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var to = drawingXml.Descendants(SpreadsheetDrawingNs + "twoCellAnchor").Single()
            .Element(SpreadsheetDrawingNs + "to")!;

        // Original file's to/col=4 (0-based, column E), to/row=4 -- a picture never touched by any command
        // must keep that exact marker, not get relocated to wherever a bogus 240x140 walk over the
        // (still-hidden) span happens to land.
        to.Element(SpreadsheetDrawingNs + "col")!.Value.Should().Be("4",
            "the picture was never resized, so its persisted to-marker must stay exactly as authored even though its own span is entirely hidden");
        to.Element(SpreadsheetDrawingNs + "row")!.Value.Should().Be("4");
    }

    /// <summary>
    /// No-regression sibling: a GENUINE resize (the real <see cref="ResizePictureCommand"/> entry point)
    /// of a picture whose own anchor span is entirely hidden must still move the to-marker -- the fix's
    /// "always capture the baseline" change must not accidentally make hidden-span pictures permanently
    /// immune to real resizes.
    /// </summary>
    [Fact]
    public void R94_GenuineResizeOfPictureAnchoredEntirelyWithinHiddenSpan_StillMovesToMarker()
    {
        using var package = BuildPictureTwoCellAnchorPackage(hideAnchorSpanFromLoad: true);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);
        var picture = sheet.Pictures.Single();

        var ctx = new TestCommandContext(loaded);
        // A real user resize -- e.g. dragging the picture's handle -- doubles the (default) width.
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
            "a genuine resize must still recompute the to-marker outward to reflect the new width, even for a hidden-span picture");
    }

    private static MemoryStream BuildPictureTwoCellAnchorPackage(bool hideAnchorSpanFromLoad)
    {
        var workbook = new Workbook("R94HiddenSpanAnchorBaseline");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        for (uint col = 1; col <= 8; col++)
            sheet.ColumnWidths[col] = 10; // 10 chars * 8 = 80px, uniform and exact.
        for (uint row = 1; row <= 8; row++)
            sheet.RowHeights[row] = 40; // already in px, uniform and exact.

        if (hideAnchorSpanFromLoad)
        {
            // Matches the picture's own from/to span below (columns 2/3/4 and rows 2/3/4, 1-based) --
            // hidden in the SOURCE FILE from the very first load, not by a later command.
            foreach (var col in new uint[] { 2, 3, 4 })
                sheet.HiddenCols.Add(col);
            foreach (var row in new uint[] { 2, 3, 4 })
                sheet.HiddenRows.Add(row);
        }

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
            // (0-based, row5). Span = columns B:D (3 cols), rows 2:4 (3 rows) -- exactly the columns/rows
            // marked hidden above when hideAnchorSpanFromLoad is true.
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
