using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R147-io-drawing-media-orphan-1: the sibling gap left by R127-io-drawing-relationship-orphan-1.
/// Deleting a picture that was originally loaded from the source .xlsx correctly drops its anchor
/// and its now-dangling image relationship (R127), but the underlying xl/media/* binary itself was
/// never excluded from <c>XlsxPackageMetadataMerger.CopyUnknownPackageParts</c>, so it survived every
/// save as an orphaned, unreferenced part -- forever, since each save re-captures the written package
/// as the next save's source.
/// </summary>
public sealed class R147_DeletedPictureMediaOrphanTests
{
    [Fact]
    public void DeleteSourceLoadedPicture_SaveAndReload_RemovesOrphanedMediaPart()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DeletePictureMedia");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var insert = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 2, 2), CreatePngBytes(), "image/png");
        insert.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        // Confirm the picture really did land in xl/media/ on the initial save (sanity check that
        // the fixture matches the real user gesture: open a file that already has a saved picture).
        initialSave.Position = 0;
        using (var initialArchive = new ZipArchive(initialSave, ZipArchiveMode.Read, leaveOpen: true))
        {
            initialArchive.Entries.Count(entry => entry.FullName.StartsWith("xl/media/", System.StringComparison.OrdinalIgnoreCase))
                .Should().Be(1, "the initial save must have written the picture's media part");
        }

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var picture = loadedSheet.Pictures.Should().ContainSingle().Which;
        picture.IsSourceLoaded.Should().BeTrue("a plain reloaded picture starts source-loaded, matching a real File > Open");

        var deleteCommand = new DeleteDrawingObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.Picture, picture.Id);
        deleteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();

        using var deletedSave = new MemoryStream();
        adapter.Save(loaded, deletedSave);

        deletedSave.Position = 0;
        using (var archive = new ZipArchive(deletedSave, ZipArchiveMode.Read, leaveOpen: true))
        {
            var mediaEntries = archive.Entries
                .Where(entry => entry.FullName.StartsWith("xl/media/", System.StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.FullName)
                .ToList();
            mediaEntries.Should().BeEmpty(
                "the deleted picture's own image binary must not be carried forward once nothing references it");
        }

        // The orphan must not resurrect itself on a further, unrelated save either -- every save
        // re-captures the written package as the next save's source snapshot.
        deletedSave.Position = 0;
        var reloadedAfterDelete = new XlsxFileAdapter().Load(deletedSave);
        using var thirdSave = new MemoryStream();
        new XlsxFileAdapter().Save(reloadedAfterDelete, thirdSave);

        thirdSave.Position = 0;
        using var thirdArchive = new ZipArchive(thirdSave, ZipArchiveMode.Read, leaveOpen: true);
        thirdArchive.Entries
            .Count(entry => entry.FullName.StartsWith("xl/media/", System.StringComparison.OrdinalIgnoreCase))
            .Should().Be(0, "the orphan must stay gone across a further unrelated save, not just the one immediately after the delete");
    }

    [Fact]
    public void DeleteOnePictureAmongTwo_SaveAndReload_KeepsSurvivingPictureMediaPart()
    {
        // No-regression sibling: excluding a deleted picture's media part must not remove a media
        // part a SURVIVING picture still needs -- that would trade the orphan-file bug for a worse
        // one (a broken image on an object the user never touched).
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DeleteOneOfTwoPicturesMedia");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var insertA = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 1, 1), CreatePngBytes(), "image/png");
        insertA.Apply(ctx).Success.Should().BeTrue();
        var insertB = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 6, 6), CreatePngBytes(), "image/png");
        insertB.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        loadedSheet.Pictures.Should().HaveCount(2);
        var pictureToDelete = loadedSheet.Pictures[0];

        var deleteCommand = new DeleteDrawingObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.Picture, pictureToDelete.Id);
        deleteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        loadedSheet.Pictures.Should().ContainSingle();

        using var deletedSave = new MemoryStream();
        adapter.Save(loaded, deletedSave);

        deletedSave.Position = 0;
        using (var archive = new ZipArchive(deletedSave, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.Entries
                .Count(entry => entry.FullName.StartsWith("xl/media/", System.StringComparison.OrdinalIgnoreCase))
                .Should().Be(1, "the surviving picture's own media part must not be pruned away too");
        }

        deletedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(deletedSave);
        var reloadedPictures = reloaded.GetSheet("Sheet1")!.Pictures;
        reloadedPictures.Should().ContainSingle();
        reloadedPictures[0].ImageBytes.Should().NotBeNullOrEmpty("the surviving picture must still carry real image bytes after reload");
    }

    private static byte[] CreatePngBytes()
    {
        // Minimal valid 1x1 transparent PNG.
        return
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
    }
}
