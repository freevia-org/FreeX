using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for R31-io-drawing-anchor-deep-3: the authored-object anchor writer
/// (<see cref="XlsxWorksheetDrawingObjectWriter"/>) used to hardcode <c>colOff</c>/<c>rowOff</c>
/// to "0" for pictures, shapes, and text boxes, so a nonzero sub-cell
/// <see cref="PictureModel.AnchorOffsetX"/>/<see cref="PictureModel.AnchorOffsetY"/> (and the
/// equivalent shape/text-box properties) silently snapped to the cell's top-left corner on save
/// (e.g. after Duplicate Sheet, which routes cloned drawing objects back through this writer).
/// </summary>
public sealed class DrawingAnchorOffsetWriterTests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    [Fact]
    public void Picture_WithNonzeroAnchorOffset_WritesOffsetInsteadOfZero()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("PictureAnchorOffsetRegression");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "OffsetPicture",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64,
            AnchorOffsetX = 12.5,
            AnchorOffsetY = 30,
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var (colOff, rowOff) = ReadFirstAnchorOffsets(stream);
        colOff.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(12.5),
            "a nonzero AnchorOffsetX must be emitted as EMU, not hardcoded to 0");
        rowOff.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(30),
            "a nonzero AnchorOffsetY must be emitted as EMU, not hardcoded to 0");

        // Round-trip: reloading must recover the same sub-cell offset (in pixels).
        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var reloadedPicture = reloaded.GetSheet("Sheet1")!.Pictures.Should().ContainSingle().Subject;
        reloadedPicture.AnchorOffsetX.Should().BeApproximately(12.5, 0.01);
        reloadedPicture.AnchorOffsetY.Should().BeApproximately(30, 0.01);
    }

    [Fact]
    public void Picture_WithZeroAnchorOffset_StillWritesZero()
    {
        // Sibling case: a plain picture anchored exactly at the cell's top-left corner (the common
        // case) must remain unaffected by the fix and still write colOff/rowOff as 0.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("PictureZeroAnchorOffset");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "ZeroOffsetPicture",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64,
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var (colOff, rowOff) = ReadFirstAnchorOffsets(stream);
        colOff.Should().Be(0);
        rowOff.Should().Be(0);
    }

    [Fact]
    public void Shape_WithNonzeroAnchorOffset_WritesOffsetInsteadOfZero()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("ShapeAnchorOffsetRegression");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "OffsetShape",
            Anchor = new CellAddress(sheet.Id, 3, 3),
            Kind = DrawingShapeKind.Rectangle,
            Width = 80,
            Height = 40,
            AnchorOffsetX = 15,
            AnchorOffsetY = 5,
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var (colOff, rowOff) = ReadFirstAnchorOffsets(stream);
        colOff.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(15));
        rowOff.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(5));
    }

    private static (long ColOff, long RowOff) ReadFirstAnchorOffsets(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var drawingEntry = archive.Entries.First(e => e.FullName.StartsWith("xl/drawings/drawing", System.StringComparison.OrdinalIgnoreCase)
                                                       && e.FullName.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase));
        using var drawingStream = drawingEntry.Open();
        var drawingXml = XDocument.Load(drawingStream);
        var anchor = drawingXml.Root!.Elements(SpreadsheetDrawingNs + "oneCellAnchor").First();
        var from = anchor.Element(SpreadsheetDrawingNs + "from")!;
        var colOff = long.Parse(from.Element(SpreadsheetDrawingNs + "colOff")!.Value);
        var rowOff = long.Parse(from.Element(SpreadsheetDrawingNs + "rowOff")!.Value);
        return (colOff, rowOff);
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
        0x42, 0x60, 0x82
    ];
}
