using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R94-io-comments-threaded-shim-note-free-1: a sheet with NO independent legacy notes
/// (<c>Sheet.Comments.Count == 0</c>) that already has a pre-existing threaded comment (loaded
/// from a source package, so its <see cref="ThreadedComment.Id"/> is set) must keep that thread's
/// legacy compatibility shim when a SECOND, brand-new threaded comment (<c>Id == null</c>) is
/// added to the SAME sheet and the workbook is saved again -- exercised entirely through the real
/// <see cref="XlsxFileAdapter"/> Load/Save entry points, mirroring
/// <see cref="R93_ThreadedCommentDualFormatExtLstTests"/>.
/// </summary>
public sealed class R94_ThreadedCommentShimNoteFreeSheetTests
{
    [Fact]
    public void NewThreadAddedToNoteFreeSheet_PreservesExistingThreadsLegacyShim()
    {
        // Arrange: a note-free sheet (Sheet.Comments.Count == 0 throughout) with ONE threaded
        // comment at A1, saved and reloaded through the real adapter so its Id is populated from
        // the source package -- the shape XlsxLegacyCommentPreserver.Preserve's
        // Sheet.Comments.Count == 0 branch is built to handle.
        var workbook = new Workbook("R94NoteFreeShim");
        var sheet = workbook.AddSheet("S1");
        var address1 = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(address1, new TextValue("first"));
        sheet.ThreadedComments[address1] = new ThreadedComment("First thread", "Quinn")
        {
            CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var adapter = new XlsxFileAdapter();
        using var basePackage = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var loaded = adapter.Load(basePackage);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddress1 = new CellAddress(loadedSheet.Id, 1, 1);

        loadedSheet.Comments.Should().BeEmpty("the sheet never had an independent legacy note");
        loadedSheet.ThreadedComments.Should().ContainKey(loadedAddress1);
        loadedSheet.ThreadedComments[loadedAddress1].Id.Should().NotBeNullOrEmpty(
            "the thread was loaded from a source package, so it must carry its stable id");

        // Act: add a BRAND-NEW threaded comment at B2 (Id == null, never saved before) to the
        // same, still note-free sheet, then save again through the real product entry point.
        var address2 = new CellAddress(loadedSheet.Id, 2, 2); // B2
        loadedSheet.SetCell(address2, new TextValue("second"));
        loadedSheet.ThreadedComments[address2] = new ThreadedComment("Second thread", "Codex")
        {
            CreatedAtUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)
        };
        loadedSheet.Comments.Should().BeEmpty("still no independent legacy note anywhere on the sheet");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        // Assert: the saved package's legacy comments part must carry a compatibility shim for
        // BOTH threads -- the pre-existing A1 thread (untouched this save) and the brand-new B2
        // thread -- not just the new one.
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var legacyXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/comments1.xml");
            var legacyEntries = legacyXml.Root!
                .Element("{http://schemas.openxmlformats.org/spreadsheetml/2006/main}commentList")!
                .Elements("{http://schemas.openxmlformats.org/spreadsheetml/2006/main}comment")
                .ToList();

            legacyEntries.Count(e => e.Attribute("ref")?.Value == "A1").Should().Be(
                1, "the pre-existing thread's legacy compatibility shim must survive the save even though its own thread never changed");
            legacyEntries.Count(e => e.Attribute("ref")?.Value == "B2").Should().Be(
                1, "the brand-new thread must get its own legacy compatibility shim");
            legacyEntries.Should().HaveCount(2, "no other legacy entry should appear on this note-free sheet");
        }

        // Sibling check: reload the resaved package and confirm both threads are still modeled.
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.ThreadedComments.Should().ContainKey(new CellAddress(reloadedSheet.Id, 1, 1));
        reloadedSheet.ThreadedComments.Should().ContainKey(new CellAddress(reloadedSheet.Id, 2, 2));
        reloadedSheet.Comments.Should().BeEmpty();
    }

    [Fact]
    public void UnchangedThreadOnNoteFreeSheet_NoNewThread_StillPreservesItsOwnShim()
    {
        // No-regression sibling: when NOTHING new is authored this save (only the pre-existing,
        // untouched thread remains on the note-free sheet), the shim must still be preserved
        // exactly as before this fix -- proves the restructured Sheet.Comments.Count == 0 branch
        // did not regress the plain round-trip case it already handled correctly.
        var workbook = new Workbook("R94NoteFreeShimUnchanged");
        var sheet = workbook.AddSheet("S1");
        var address1 = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(address1, new TextValue("only"));
        sheet.ThreadedComments[address1] = new ThreadedComment("Only thread", "Quinn")
        {
            CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var adapter = new XlsxFileAdapter();
        using var basePackage = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var loaded = adapter.Load(basePackage);
        var loadedSheet = loaded.GetSheetAt(0);

        // Act: save again, completely unchanged (no model edits at all).
        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var legacyXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/comments1.xml");
            var legacyEntries = legacyXml.Root!
                .Element("{http://schemas.openxmlformats.org/spreadsheetml/2006/main}commentList")!
                .Elements("{http://schemas.openxmlformats.org/spreadsheetml/2006/main}comment")
                .ToList();

            legacyEntries.Count(e => e.Attribute("ref")?.Value == "A1").Should().Be(
                1, "the sole, untouched thread's legacy shim must still survive an unchanged round trip");
        }

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.ThreadedComments.Should().ContainKey(new CellAddress(reloadedSheet.Id, 1, 1));
        reloadedSheet.Comments.Should().BeEmpty();
    }
}
