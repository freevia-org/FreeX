using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression guard for round-14 review finding (bucket T16):
///
///   R14-camera-linked-picture-3 - a FreeX-authored "camera" / Paste Special &gt; Linked Picture
///     (or Paste Picture) object is a <see cref="PictureModel"/> with
///     <see cref="PictureModel.Kind"/> == <see cref="PictureKind.CellRangeSnapshot"/> whose only
///     content is the cached <see cref="PictureModel.Cells"/> snapshot (text + style) —
///     <see cref="PictureModel.ImageBytes"/> is never populated because
///     PasteRangeAsPictureCommand never rasterizes it. XlsxWorksheetDrawingObjectWriter's
///     IsSupportedPicture filter required ImageBytes to be present, so the object (and its
///     content) silently vanished on every .xlsx save instead of surviving as a real drawing
///     object the way Excel's own camera/linked-picture objects do.
/// </summary>
public sealed class FreeXR14T16Tests
{
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void SavingXlsx_WithUnrasterizedCellRangeSnapshotPicture_DoesNotDropIt()
    {
        var workbook = new Workbook("CameraPictureRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "CameraSnapshot",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.CellRangeSnapshot,
            SourceRowCount = 1,
            SourceColumnCount = 1,
            Width = 80,
            Height = 20,
            Cells =
            {
                new PictureCellSnapshot(0, 0, "Snap", new CellStyle { FillColor = new CellColor(255, 255, 0) })
            }
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        // The drawing part must exist and must carry the cell's text — not be silently omitted
        // just because there is no raster image to embed.
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingEntry = archive.GetEntry("xl/drawings/drawing1.xml");
            drawingEntry.Should().NotBeNull(
                "the camera/linked-picture object must still produce a drawing part instead of being silently dropped");

            using var drawingStream = drawingEntry!.Open();
            var drawingXml = XDocument.Load(drawingStream);
            drawingXml.Root!
                .Descendants(DrawingNs + "t")
                .Select(t => t.Value)
                .Should().Contain("Snap", "the pasted range's cell content must survive the .xlsx save");
        }

        // The object must also be genuinely readable back through the public API (not merely
        // present as raw XML): FreeX's own drawing-part reader flattens shapes nested inside a
        // group, so the reconstructed cell comes back as a rectangle shape carrying the cell text.
        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;
        reloadedSheet.DrawingShapes
            .Should().Contain(s => s.ShapeText == "Snap",
                "the camera picture's cell content must round-trip as visible drawing content, not vanish");
    }
}
