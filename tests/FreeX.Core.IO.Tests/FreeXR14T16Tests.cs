using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression guard for round-14 review finding (bucket T16), UPDATED by round 119
/// (R119-io-camera-linked-picture-identity):
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
///
/// R14 fixed the outright data loss by reconstructing the object as a vector
/// <c>&lt;xdr:grpSp&gt;</c> group of rectangle/text shapes -- but that group carried no metadata
/// identifying it as a picture at all, so FreeX's own reader (which walks every &lt;xdr:sp&gt;
/// regardless of grpSp nesting) flattened it into independent, disconnected
/// <see cref="DrawingShapeModel"/> objects on load, permanently destroying the picture's identity
/// and (for the Linked Picture / Camera variant) its live link. The final assertion below used to
/// assert exactly that flattened outcome as the accepted status quo -- per the round's
/// "DO NOT CERTIFY A BUG" disposition rule, that was the R14 fix's own accepted-but-incomplete
/// behavior, not correct behavior Excel agrees with (real Excel's Camera/Linked-Picture objects
/// stay single, identifiable, live-updating pictures across any number of save/reload cycles), so
/// R119 fixes the underlying writer/reader gap and this test's final assertion is UPDATED (not
/// left encoding the old bug) to require the reloaded object come back as a single
/// CellRangeSnapshot PictureModel again. See <see cref="R119_CameraLinkedPictureIdentityTests"/>
/// for the full linked-identity round-trip coverage this file's narrower scope doesn't reach.
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

        // R119-io-camera-linked-picture-identity: the object must reload as a SINGLE
        // CellRangeSnapshot PictureModel again -- not as a flattened, disconnected DrawingShapeModel
        // (the R14-era behavior this test used to certify as accepted). FreeX's writer now records
        // enough metadata in the group's extLst for the reader to rebuild the picture instead of
        // flattening its per-cell rectangles.
        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;
        reloadedSheet.DrawingShapes.Should().BeEmpty(
            "the camera picture's per-cell rectangles must no longer be flattened into independent shapes");
        reloadedSheet.Pictures
            .Should().ContainSingle(p => p.Kind == PictureKind.CellRangeSnapshot)
            .Which.Cells.Should().Contain(c => c.Text == "Snap",
                "the camera picture's cell content must round-trip as a real picture, not vanish or flatten");
    }
}
