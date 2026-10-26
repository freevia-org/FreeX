using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r378: Format Picture's lock flags must survive a save.
///
/// <para><c>PictureModel.LockAspectRatio</c> (Size &gt; "Lock aspect ratio") and
/// <c>PictureModel.Locked</c> (Properties &gt; "Locked") were session-only, documented as deferred
/// follow-up work: nothing wrote <c>a:picLocks</c>, so unchecking either, saving and reopening
/// brought the lock straight back. Excel persists both, and Excel is the authority for this
/// product's behaviour.</para>
///
/// <para>The element is written only when a flag departs from its default. Both model properties
/// default to locked -- matching Excel's authored default -- so an ordinary picture still produces
/// exactly the XML it did before this change, and the element appears only for one the author
/// actually unlocked.</para>
/// </summary>
public sealed class R378_PictureLockFlagsSurviveSaveTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly XNamespace DrawingNs =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static MemoryStream SaveWithPicture(Action<PictureModel> configure)
    {
        var workbook = new Workbook("Locks");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = new PictureModel { Name = "Pic1", ImageBytes = Png, Width = 100, Height = 50 };
        configure(picture);
        sheet.Pictures.Add(picture);

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static PictureModel Reload(MemoryStream saved)
    {
        saved.Position = 0;
        return new XlsxFileAdapter().Load(saved).GetSheetAt(0).Pictures.Single();
    }

    private static XElement? PictureLocksElement(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries.Where(e =>
                     e.FullName.StartsWith("xl/drawings/drawing", StringComparison.Ordinal) &&
                     e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            using var stream = entry.Open();
            if (XDocument.Load(stream).Descendants(DrawingNs + "picLocks").FirstOrDefault() is { } locks)
                return locks;
        }

        return null;
    }

    [Fact]
    public void AnUnlockedPictureStaysUnlockedAcrossASave()
    {
        using var saved = SaveWithPicture(picture =>
        {
            picture.LockAspectRatio = false;
            picture.Locked = false;
        });

        var reloaded = Reload(saved);

        reloaded.LockAspectRatio.Should().BeFalse("the author unchecked Lock aspect ratio");
        reloaded.Locked.Should().BeFalse("the author unchecked Locked");
    }

    [Fact]
    public void UnlockingOnlyTheAspectRatioLeavesTheOtherLockAlone()
    {
        using var saved = SaveWithPicture(picture => picture.LockAspectRatio = false);

        var reloaded = Reload(saved);

        reloaded.LockAspectRatio.Should().BeFalse();
        reloaded.Locked.Should().BeTrue("only the aspect-ratio lock was cleared");
    }

    [Fact]
    public void ADefaultPictureRoundTripsLockedAndWritesNoElement()
    {
        // Byte-stability: the defaults are "locked", so an ordinary picture must still produce the
        // XML it produced before this change. Writing picLocks unconditionally would churn every
        // drawing part in every existing workbook.
        using var saved = SaveWithPicture(_ => { });

        PictureLocksElement(saved).Should().BeNull("an all-default picture needs no lock override");

        var reloaded = Reload(saved);
        reloaded.LockAspectRatio.Should().BeTrue();
        reloaded.Locked.Should().BeTrue();
    }

    [Fact]
    public void AnExcelStyleAspectLockIsReadAsLockedAspectAndMovablePicture()
    {
        // What Excel actually writes for a picture whose aspect is locked but which can still be
        // moved: the element is present, noChangeAspect is set, and the move/resize locks are absent,
        // which per the OOXML defaults means false.
        using var saved = SaveWithPicture(picture =>
        {
            picture.LockAspectRatio = false;
            picture.Locked = false;
        });

        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.Entries.First(e =>
                e.FullName.StartsWith("xl/drawings/drawing", StringComparison.Ordinal) &&
                e.FullName.EndsWith(".xml", StringComparison.Ordinal));

            XDocument document;
            using (var read = entry.Open())
                document = XDocument.Load(read);

            var locks = document.Descendants(DrawingNs + "picLocks").Single();
            locks.RemoveAttributes();
            locks.SetAttributeValue("noChangeAspect", "1");

            entry.Delete();
            using var write = new StreamWriter(archive.CreateEntry(entry.FullName).Open());
            write.Write(document.ToString(SaveOptions.DisableFormatting));
        }

        var reloaded = Reload(saved);

        reloaded.LockAspectRatio.Should().BeTrue("noChangeAspect=\"1\" locks the aspect ratio");
        reloaded.Locked.Should().BeFalse("noMove/noResize are absent, which the schema reads as false");
    }
}
