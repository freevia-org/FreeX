using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R65-io-image-drawing-6-2 regression: <see cref="XlsxSourceDrawingGeometryRewriter"/> must pair each
/// source-loaded <see cref="PictureModel"/> with its own physical <c>&lt;xdr:pic&gt;</c> element by
/// IDENTITY (matching the element's <c>cNvPr@name</c>), not by a positional
/// <c>Skip(pictureElements.Count - sourcePictures.Count).Zip(...)</c> that assumes every UNMODELED
/// <c>&lt;xdr:pic&gt;</c> (e.g. one whose image relationship/media entry could not be resolved on load,
/// so the reader skipped it — the same class of gap R65-io-image-drawing-6-1 fixed for "Link to File"
/// pictures) sorts BEFORE every modeled one in document order. When the unmodeled element instead sits
/// between two modeled pictures, the old positional pairing wrote an edit to the wrong sibling's XML.
/// </summary>
public sealed class XlsxSourceDrawingGeometryRewriterPictureIdentityTests
{
    [Fact]
    public void ResizingSourcePicture_WithUnmodeledPictureBetweenSiblings_PatchesTheCorrectElement()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("PictureIdentityAlignment");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "First", 2);
        AddPicture(sheet, "Second", 6);
        AddPicture(sheet, "Third", 10);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        // "Second"'s media entry is removed from the package AFTER writing but BEFORE the next load, so
        // its <xdr:pic> element (which sits in the MIDDLE of document order, between "First" and "Third")
        // survives physically in the drawing part but is skipped by the reader (missing image entry) and
        // never becomes a PictureModel — an unmodeled element the old Skip/Zip alignment could not
        // tolerate anywhere except at the front of the list.
        DeleteMediaEntry(initialSave, pictureIndex: 2);

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;
        reloadedSheet.Pictures.Should().HaveCount(2,
            "the picture whose media entry is missing must be dropped on load -- an orthogonal, expected edge case");
        reloadedSheet.Pictures.Select(picture => picture.Name)
            .Should().BeEquivalentTo(new[] { "First", "Third" });

        // (No call to TryPrepareLoadedPackageSnapshotForEdit: the deliberately-broken relationship
        // target left behind by DeleteMediaEntry makes that edit-preparation path decline categorically
        // -- a separate, pre-existing guard unrelated to this fix. adapter.Save still falls back to the
        // full save path below on its own, which is exactly the path that exercises the rewriter.)
        var firstPicture = reloadedSheet.Pictures.Single(picture => picture.Name == "First");
        firstPicture.RotationDegrees = 45;

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        secondSave.Position = 0;
        var finalSheet = adapter.Load(secondSave).GetSheet("Sheet1")!;
        finalSheet.Pictures.Should().HaveCount(2);

        var finalFirst = finalSheet.Pictures.Single(picture => picture.Name == "First");
        var finalThird = finalSheet.Pictures.Single(picture => picture.Name == "Third");

        // Without the fix, Skip(3 physical elements - 2 models = 1) drops the FIRST physical element
        // entirely from the zip, then pairs (Second-element, First-model) and (Third-element,
        // Third-model) -- so First's rotation edit is written onto the unmodeled Second element (which
        // nobody ever reads back) instead of First's own element, and First comes back unrotated.
        finalFirst.RotationDegrees.Should().BeApproximately(45, 0.01,
            "the rotation edit must land on First's own <xdr:pic> element even though an unmodeled " +
            "picture sits between First and Third in document order");
        finalThird.RotationDegrees.Should().BeApproximately(0, 0.01,
            "Third's own geometry must be unaffected by First's edit");
    }

    [Fact]
    public void ResizingSourcePicture_WithOnlyEmbeddedSiblings_StillAlignsCorrectly()
    {
        // Sibling no-regression test: with no unmodeled element at all (the overwhelmingly common case),
        // the new identity-based match must keep behaving exactly like the old positional one.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("PictureIdentityNoRegression");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "Alpha", 2);
        AddPicture(sheet, "Beta", 8);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(reloaded, out var blockReason)
            .Should().BeTrue(blockReason);

        var alpha = reloadedSheet.Pictures.Single(picture => picture.Name == "Alpha");
        alpha.RotationDegrees = 30;

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        secondSave.Position = 0;
        var finalSheet = adapter.Load(secondSave).GetSheet("Sheet1")!;
        finalSheet.Pictures.Should().HaveCount(2);

        finalSheet.Pictures.Single(picture => picture.Name == "Alpha").RotationDegrees
            .Should().BeApproximately(30, 0.01);
        finalSheet.Pictures.Single(picture => picture.Name == "Beta").RotationDegrees
            .Should().BeApproximately(0, 0.01);
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

    private static void DeleteMediaEntry(MemoryStream packageStream, int pictureIndex)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var prefix = $"xl/media/freexPicture{pictureIndex}.";
        foreach (var entry in archive.Entries
                     .Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            entry.Delete();
        }
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
