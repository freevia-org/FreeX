using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// media-lifecycle F1: <see cref="XlsxWorksheetDrawingObjectWriter"/> gates every picture behind
/// <c>!IsSourceLoaded</c> (see <c>IsSupportedPicture</c>), and nothing ever re-marks a picture
/// <c>IsSourceLoaded</c> after a save -- only a fresh Load does. So on a workbook that has a real
/// source package (i.e. it was loaded from an .xlsx, or has already been saved once -- see
/// <c>XlsxFileAdapter.SourcePackages</c>), a picture inserted this session used to get rewritten to a
/// BRAND NEW <c>xl/media/freexPictureN</c> file on every single full-rebuild save, even when nothing
/// about the picture itself changed between saves (e.g. the user just kept editing unrelated cells
/// and pressing Ctrl+S). The prior save's file was never deleted, and
/// <c>XlsxPackageMetadataMerger.CopyUnknownPackageParts</c> carried it forward from the now-stale
/// source-package snapshot as an "unknown" part on every subsequent save, since each save re-captures
/// the freshly written package as the next save's source. Media therefore accumulated without bound
/// purely from resaving, independent of how many times the picture was actually edited.
/// <para>
/// Reproducing this requires a genuinely LOADED (or previously-saved) workbook, matching the finding's
/// own repro ("after loading a workbook, inserting one picture, and calling Save() 6 times") -- a
/// brand-new, never-loaded <see cref="Workbook"/> takes an entirely different, source-package-free
/// save path (<c>XlsxFileAdapter.ApplyPackagePostProcessing</c>'s <c>!hasSourcePackage</c> early
/// return) that never populates <c>SourcePackages</c> at all, so it never exhibits this leak either
/// way and would be a false negative for both the fail-before and the fix-after proof.
/// </para>
/// </summary>
public sealed class R147_MediaLifecycleRepeatedSaveTests
{
    [Fact]
    public void UnchangedPicture_RepeatedlyResaved_DoesNotAccumulateDuplicateMediaParts()
    {
        var adapter = new XlsxFileAdapter();
        var loaded = LoadEmptyWorkbook(adapter, "MediaLifecycleRepeatedSave");
        var sheet = loaded.GetSheetAt(0);
        var imageBytes = CreatePngBytes();
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Photo",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = imageBytes,
            ContentType = "image/png",
            Width = 96,
            Height = 64
        });

        using var save1 = new MemoryStream();
        adapter.Save(loaded, save1);
        MediaEntryNames(save1).Should().HaveCount(1, "the initial save must write exactly one media part for the one picture");

        // Six more saves in a row, each preceded by an unrelated cell edit -- exactly like normal
        // interactive use (typing, formatting) with no further touch to the picture at all. This is
        // the finding's own repro shape.
        MemoryStream? lastSave = null;
        for (var i = 0; i < 6; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 10, (uint)(1 + i)), new NumberValue(i));
            var save = new MemoryStream();
            adapter.Save(loaded, save);
            lastSave?.Dispose();
            lastSave = save;
        }

        try
        {
            var finalMediaEntries = MediaEntryNames(lastSave!);

            // Before the fix this grew to 7 entries (freexPicture1..freexPicture7) -- one brand new
            // duplicate of the SAME image per save, every earlier one left behind unreferenced.
            finalMediaEntries.Should().HaveCount(1,
                "an unedited picture must not accumulate a new duplicate media part on every resave -- " +
                "only its single current media part should ever be present");

            // The picture must still load correctly with its original bytes -- the fix must not have
            // traded the leak for a broken/missing image.
            lastSave!.Position = 0;
            var reloaded = adapter.Load(lastSave);
            var reloadedPicture = reloaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
            reloadedPicture.ImageBytes.Should().Equal(imageBytes);
        }
        finally
        {
            lastSave?.Dispose();
        }
    }

    [Fact]
    public void PictureImageGenuinelyReplaced_BetweenSaves_StillSavesCorrectNewBytesAndStopsGrowingAfterward()
    {
        // No-regression sibling. Two things must both stay true once the reuse optimization exists:
        //   1. It must never keep serving STALE bytes once the picture's image is genuinely replaced
        //      (e.g. Format Picture > Change Picture) -- that would trade the duplicate-file leak for
        //      silent data loss (old picture content persisting after an explicit replace).
        //   2. A genuine edit is still allowed to leave its OWN prior media part behind as a single,
        //      bounded orphan (this fix targets growth from resaving an UNCHANGED picture, not from
        //      editing one -- see the writer's own comment on why only the embedded-raster branch is
        //      touched). What must NOT happen is that single edit's orphan compounding into unbounded
        //      growth on every subsequent save the way the original bug did -- i.e. once the picture
        //      settles again (no further edits), repeated resaves must stop adding new parts, exactly
        //      like the primary test above.
        var adapter = new XlsxFileAdapter();
        var loaded = LoadEmptyWorkbook(adapter, "MediaLifecycleGenuineReplace");
        var sheet = loaded.GetSheetAt(0);
        var originalBytes = CreatePngBytes();
        var picture = new PictureModel
        {
            Name = "Photo",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = originalBytes,
            ContentType = "image/png",
            Width = 96,
            Height = 64
        };
        sheet.Pictures.Add(picture);

        using var save1 = new MemoryStream();
        adapter.Save(loaded, save1);
        MediaEntryNames(save1).Should().HaveCount(1);

        // An unrelated cell edit between saves, exactly like the leak repro -- proves the reuse
        // optimization's "unchanged" check is keyed off the picture's own bytes, not merely "did
        // anything change on the sheet".
        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new TextValue("unrelated"));

        // Genuinely replace the image content (a new, differently-sized array -- never the same
        // reference as the original).
        var replacementBytes = CreateWiderPngBytes();
        picture.ImageBytes = replacementBytes;

        using var save2 = new MemoryStream();
        adapter.Save(loaded, save2);
        var save2Entries = MediaEntryNames(save2);

        // (1) The reload must carry the NEW bytes, not stale ones -- checked first and unconditionally,
        // regardless of how many media parts ended up in the package.
        save2.Position = 0;
        var reloadedAfterReplace = adapter.Load(save2);
        var pictureAfterReplace = reloadedAfterReplace.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        pictureAfterReplace.ImageBytes.Should().Equal(replacementBytes, "the reloaded picture must carry the REPLACED bytes, not the original ones");
        pictureAfterReplace.ImageBytes.Should().NotEqual(originalBytes);

        // (2) A single genuine edit may leave its own prior part behind (bounded, expected), but must
        // not have somehow multiplied beyond that.
        save2Entries.Should().HaveCountLessThanOrEqualTo(2,
            "one genuine image replacement may leave at most its own single prior media part behind as a bounded orphan");

        // Now the picture is unchanged again -- two more plain resaves (unrelated cell edits only, no
        // further picture touch) must NOT add any further media parts, proving growth stops again after
        // the edit settles instead of compounding indefinitely the way the original bug did.
        MemoryStream? lastSave = null;
        for (var i = 0; i < 2; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 12, (uint)(1 + i)), new NumberValue(i));
            var save = new MemoryStream();
            adapter.Save(loaded, save);
            lastSave?.Dispose();
            lastSave = save;
        }

        try
        {
            var finalEntries = MediaEntryNames(lastSave!);
            finalEntries.Should().HaveCount(save2Entries.Count,
                "once the replaced picture settles, further unrelated resaves must not add any more media parts");

            lastSave!.Position = 0;
            var finalReload = adapter.Load(lastSave);
            var finalPicture = finalReload.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
            finalPicture.ImageBytes.Should().Equal(replacementBytes);
        }
        finally
        {
            lastSave?.Dispose();
        }
    }

    // Builds a genuine source-package-backed Workbook (as opposed to a brand-new in-memory one) by
    // saving an empty workbook and loading it straight back -- matching the finding's own repro
    // ("after loading a workbook, ..."). This is what actually populates
    // XlsxFileAdapter's internal SourcePackages tracking for the returned Workbook, without which
    // every save takes the source-package-free fast path and the leak (and the fix) are both
    // unobservable.
    private static Workbook LoadEmptyWorkbook(XlsxFileAdapter adapter, string name)
    {
        var seed = new Workbook(name);
        seed.AddSheet("Data");
        using var seedStream = new MemoryStream();
        adapter.Save(seed, seedStream);
        seedStream.Position = 0;
        return adapter.Load(seedStream);
    }

    private static System.Collections.Generic.List<string> MediaEntryNames(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/media/", System.StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.FullName)
            .ToList();
    }

    private static byte[] CreatePngBytes() =>
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

    // A distinct, differently-shaped minimal PNG (2x1 instead of 1x1) so it is trivially
    // distinguishable by byte content from CreatePngBytes() -- used to prove a genuine image
    // replacement round-trips the NEW bytes, not the original ones.
    private static byte[] CreateWiderPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x7A, 0xE1, 0xF3,
        0x84, 0x00, 0x00, 0x00, 0x0E, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x64, 0x60, 0x60, 0x60,
        0x00, 0x00, 0x00, 0x0A, 0x00, 0x01, 0x18, 0x27,
        0xF6, 0xBE, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
        0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];
}
