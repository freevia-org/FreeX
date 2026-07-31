using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R110-io-drawing-anchor-write-exhaustion (sibling of <see cref="R110_DrawingAnchorWriteExhaustionClampTests"/>
/// for the chart writer): <see cref="XlsxSourceDrawingGeometryRewriter"/> duplicates the identical
/// ToMarkerIndex pixel-to-cell walk to recompute a resized source-loaded picture/shape/text box's
/// twoCellAnchor 'to' marker (RewriteAnchorGeometry, only reached on a GENUINE Width/Height change vs. the
/// picture's load-time baseline -- see the R94 skip-gate). If every column/row the walk could land on is
/// hidden, the walk exhausts to `maxIndex` (16384/1,048,576) and -- before the fix -- wrote that one-past-
/// the-end value verbatim into &lt;xdr:col&gt;/&lt;xdr:row&gt;. Real product entry point:
/// <see cref="XlsxFileAdapter.Load"/> + <see cref="XlsxFileAdapter.Save"/> round-trip, resizing the picture
/// model exactly as the UI's resize-handle drag does.
/// </summary>
public sealed class R110_SourceDrawingGeometryRewriterExhaustionClampTests
{
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const uint MaxColumnIndexZeroBased = 16383;

    [Fact]
    public void ResizingATwoCellAnchoredPicture_WithAllColumnsHidden_ClampsToMarker_InsteadOfWritingOneOffTheEnd()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("HiddenColumnsPictureResize");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "Pic", 2);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        // Convert the picture's ordinary oneCellAnchor into a small, well-formed twoCellAnchor (from col 1
        // row 2 to col 3 row 4) -- the shape a real Excel-authored "move and size with cells" picture uses.
        RewriteDrawingXml(initialSave, drawingXml =>
        {
            var root = drawingXml.Root!;
            var oneCellAnchor = root.Elements(Xdr + "oneCellAnchor")
                .Single(anchor => anchor.Descendants(Xdr + "cNvPr").Any(c => c.Attribute("name")?.Value == "Pic"));
            var pic = oneCellAnchor.Element(Xdr + "pic")!;
            pic.Remove();
            oneCellAnchor.Remove();

            root.Add(new XElement(Xdr + "twoCellAnchor",
                new XElement(Xdr + "from",
                    new XElement(Xdr + "col", 1), new XElement(Xdr + "colOff", 0),
                    new XElement(Xdr + "row", 2), new XElement(Xdr + "rowOff", 0)),
                new XElement(Xdr + "to",
                    new XElement(Xdr + "col", 3), new XElement(Xdr + "colOff", 0),
                    new XElement(Xdr + "row", 4), new XElement(Xdr + "rowOff", 0)),
                pic,
                new XElement(Xdr + "clientData")));
        });

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;
        var pic2 = reloadedSheet.Pictures.Single(picture => picture.Name == "Pic");
        pic2.IsSourceLoaded.Should().BeTrue();

        // Hide every column on the sheet AFTER load (an ordinary "hide unused columns" action unrelated to
        // the picture), then genuinely resize the picture so RewriteAnchorGeometry's R94 baseline-vs-current
        // gate lets the to-marker recompute run. With nothing visible, that walk can never find a column
        // whose width absorbs the remaining distance and exhausts to the end.
        for (var col = 1u; col <= 16384u; col++)
            reloadedSheet.HiddenCols.Add(col);
        pic2.Width = 900;
        pic2.Height = 500;

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        var savedAnchor = ReadDrawingXml(secondSave).Descendants(Xdr + "twoCellAnchor").Single();
        var toCol = (uint)savedAnchor.Element(Xdr + "to")!.Element(Xdr + "col")!;

        toCol.Should().BeLessThanOrEqualTo(MaxColumnIndexZeroBased,
            "an exhausted column walk on a resized source-loaded picture must clamp to Excel's real zero-based " +
            "ceiling (16383 = XFD), never write 16384 into <xdr:col> where Excel would reject/repair the drawing");
    }

    // Sibling no-regression: the ordinary case from XlsxSourceDrawingGeometryRewriterGroupAndAbsoluteAnchorTests
    // -- a genuine resize with nothing hidden -- must still compute its real, small to-marker unaffected by
    // the exhaustion clamp.
    [Fact]
    public void ResizingATwoCellAnchoredPicture_WithNothingHidden_StillComputesOrdinarySmallToMarker()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("OrdinaryPictureResize");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "Pic", 2);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        RewriteDrawingXml(initialSave, drawingXml =>
        {
            var root = drawingXml.Root!;
            var oneCellAnchor = root.Elements(Xdr + "oneCellAnchor")
                .Single(anchor => anchor.Descendants(Xdr + "cNvPr").Any(c => c.Attribute("name")?.Value == "Pic"));
            var pic = oneCellAnchor.Element(Xdr + "pic")!;
            pic.Remove();
            oneCellAnchor.Remove();

            root.Add(new XElement(Xdr + "twoCellAnchor",
                new XElement(Xdr + "from",
                    new XElement(Xdr + "col", 1), new XElement(Xdr + "colOff", 0),
                    new XElement(Xdr + "row", 2), new XElement(Xdr + "rowOff", 0)),
                new XElement(Xdr + "to",
                    new XElement(Xdr + "col", 3), new XElement(Xdr + "colOff", 0),
                    new XElement(Xdr + "row", 4), new XElement(Xdr + "rowOff", 0)),
                pic,
                new XElement(Xdr + "clientData")));
        });

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;
        var pic2 = reloadedSheet.Pictures.Single(picture => picture.Name == "Pic");

        pic2.Width = 500;
        pic2.Height = 300;

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        var savedAnchor = ReadDrawingXml(secondSave).Descendants(Xdr + "twoCellAnchor").Single();
        var toCol = (uint)savedAnchor.Element(Xdr + "to")!.Element(Xdr + "col")!;

        // Default column width ~67px: a 500px-wide picture starting at column 1 spans well under 20
        // columns -- nowhere near the 16383 ceiling the exhaustion case clamps to.
        toCol.Should().BeLessThan(20,
            "an ordinary unhidden sheet's resize must land within the first handful of columns, not clamp at the ceiling");
    }

    private static void AddPicture(Sheet sheet, string name, uint row) =>
        sheet.Pictures.Add(new PictureModel
        {
            Name = name,
            Anchor = new CellAddress(sheet.Id, row, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64
        });

    private static void RewriteDrawingXml(MemoryStream packageStream, Action<XDocument> mutate)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry("xl/drawings/drawing1.xml")!;

        XDocument drawingXml;
        using (var reader = new StreamReader(entry.Open()))
            drawingXml = XDocument.Parse(reader.ReadToEnd());

        mutate(drawingXml);

        entry.Delete();
        var newEntry = archive.CreateEntry("xl/drawings/drawing1.xml");
        using var writer = new StreamWriter(newEntry.Open());
        writer.Write(drawingXml.ToString(SaveOptions.DisableFormatting));
    }

    private static XDocument ReadDrawingXml(MemoryStream packageStream)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/drawings/drawing1.xml")!;
        using var reader = new StreamReader(entry.Open());
        return XDocument.Parse(reader.ReadToEnd());
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];
}
