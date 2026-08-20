using System.IO;
using System.IO.Compression;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Host.Tests;

/// <summary>
/// sweep96 F1: a slide's declared layout relationship can name a real part in the zip that
/// isn't reachable through the master-side relationship walk PptxPackageReader uses to build
/// <see cref="FreeP.Core.Model.Presentation.Layouts"/> (e.g. a hand-edited or third-party-
/// generated package). MatchLayoutIdByPath used to fall through to <c>layouts[0].Id</c> --
/// the first layout of the first master in the package, wholly unrelated to the slide's real
/// layout -- silently corrupting the slide's placeholder geometry/background/theme, and the
/// writer's mirrored fallback then baked that wrong association permanently into any re-save.
///
/// The fix reads the layout directly from the part the slide's own relationship names (when
/// that part is genuinely present in the zip) and ties it back to its real owning master via
/// the layout's own p:sldLayout rels, instead of reassigning the slide to an arbitrary layout.
/// </summary>
public sealed class LayoutOrphanResolutionTests
{
    private static byte[] WriteToBytes(PresentationModel pres)
    {
        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        return ms.ToArray();
    }

    private static string ReadZipEntryText(ZipArchive zip, string entryPath)
    {
        var entry = zip.GetEntry(entryPath);
        entry.Should().NotBeNull($"expected {entryPath} to exist");
        using var s = entry!.Open();
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private static void WriteZipEntryText(ZipArchive zip, string entryPath, string content)
    {
        var existing = zip.GetEntry(entryPath);
        existing?.Delete();
        var entry = zip.CreateEntry(entryPath, CompressionLevel.NoCompression);
        using var s = entry.Open();
        using var w = new StreamWriter(s);
        w.Write(content);
    }

    /// <summary>
    /// Builds a package with a second, real slideLayout part (slideLayout2.xml, distinguishable
    /// by its p:cSld/@name) that is a byte-for-byte copy of slideLayout1.xml other than that
    /// name and its own rels -- then repoints the slide's OWN relationship at it, WITHOUT
    /// touching the master's rels (ppt/slideMasters/_rels/slideMaster1.xml.rels), so the
    /// master-side walk that builds Presentation.Layouts never reaches slideLayout2.xml. This
    /// reproduces "a real part in the zip but not reachable through any master relationship
    /// FreeP walked" from the finding's USER GESTURE verbatim.
    /// </summary>
    private static byte[] BuildPackageWithOrphanSlideLayout()
    {
        var pres = PresentationModel.CreateEmpty();
        var original = WriteToBytes(pres);

        using var ms = new MemoryStream();
        ms.Write(original, 0, original.Length);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var layout1Xml = ReadZipEntryText(zip, "ppt/slideLayouts/slideLayout1.xml");
            layout1Xml.Should().Contain("name=\"Title Slide\"",
                "CreateEmpty()'s single layout must be named this for the marker swap below");
            var layout2Xml = layout1Xml.Replace("name=\"Title Slide\"", "name=\"OrphanLayoutMarker\"");
            WriteZipEntryText(zip, "ppt/slideLayouts/slideLayout2.xml", layout2Xml);

            // Real part: give it its own rels (same master reference as layout1) so the fix's
            // master-lookup path has something correct to resolve.
            var layout1Rels = ReadZipEntryText(zip, "ppt/slideLayouts/_rels/slideLayout1.xml.rels");
            WriteZipEntryText(zip, "ppt/slideLayouts/_rels/slideLayout2.xml.rels", layout1Rels);

            // Register the content type, matching how a real authoring tool would add a part.
            var contentTypes = ReadZipEntryText(zip, "[Content_Types].xml");
            contentTypes.Should().Contain("/ppt/slideLayouts/slideLayout1.xml");
            var newOverride =
                "<Override PartName=\"/ppt/slideLayouts/slideLayout2.xml\" " +
                "ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\" />";
            WriteZipEntryText(zip, "[Content_Types].xml",
                contentTypes.Replace("</Types>", newOverride + "</Types>"));

            // Deliberately do NOT touch slideMaster1.xml.rels -- slideLayout2.xml stays
            // unreachable via the master-side walk that builds Presentation.Layouts.

            // Repoint the SLIDE's own relationship at the orphan layout part.
            var slideRels = ReadZipEntryText(zip, "ppt/slides/_rels/slide1.xml.rels");
            slideRels.Should().Contain("../slideLayouts/slideLayout1.xml");
            WriteZipEntryText(zip, "ppt/slides/_rels/slide1.xml.rels",
                slideRels.Replace("../slideLayouts/slideLayout1.xml", "../slideLayouts/slideLayout2.xml"));
        }

        return ms.ToArray();
    }

    [Fact]
    public void OrphanSlideLayout_ResolvesToItsOwnRealLayout_NotAnArbitraryOne()
    {
        var bytes = BuildPackageWithOrphanSlideLayout();

        var reloaded = PptxPackageReader.Read(new MemoryStream(bytes));

        var slide = reloaded.Slides[0];
        var resolvedLayout = reloaded.Layouts.Find(l => l.Id == slide.LayoutId);

        resolvedLayout.Should().NotBeNull(
            "the slide's declared layout part is genuinely present in the zip, so it must resolve");
        resolvedLayout!.PartPath.Should().Be("ppt/slideLayouts/slideLayout2.xml",
            "the slide must be tied to the layout its OWN relationship names");
        resolvedLayout.Name.Should().Be("OrphanLayoutMarker",
            "before the fix this fell back to layouts[0] -- the unrelated 'Title Slide' layout -- " +
            "instead of the slide's real declared layout");
    }

    [Fact]
    public void OrphanSlideLayout_TiesBackToItsRealOwningMaster()
    {
        var bytes = BuildPackageWithOrphanSlideLayout();

        var reloaded = PptxPackageReader.Read(new MemoryStream(bytes));

        var slide = reloaded.Slides[0];
        var resolvedLayout = reloaded.Layouts.Find(l => l.Id == slide.LayoutId);

        resolvedLayout.Should().NotBeNull();
        reloaded.Masters.Should().ContainSingle();
        resolvedLayout!.MasterId.Should().Be(reloaded.Masters[0].Id,
            "the orphan layout's own p:sldLayout rels names its real master; the fix must use " +
            "that instead of guessing, so theme/placeholder inheritance stays correct");
    }

    [Fact]
    public void OrphanSlideLayout_DoesNotCorruptTheMasterWalkedLayoutList()
    {
        var bytes = BuildPackageWithOrphanSlideLayout();

        var reloaded = PptxPackageReader.Read(new MemoryStream(bytes));

        // The original master-reachable layout must still be present, untouched, alongside the
        // newly resolved orphan -- the fix must be additive, not a replacement.
        reloaded.Layouts.Should().HaveCount(2);
        reloaded.Layouts.Should().Contain(l => l.Name == "Title Slide" && l.PartPath == "ppt/slideLayouts/slideLayout1.xml");
    }

    /// <summary>
    /// Sibling no-regression case: a normal, well-formed package (the master walk reaches every
    /// layout a slide could reference) must resolve entirely through the existing exact-path
    /// match and must NOT take the new orphan-resolution path -- i.e. Presentation.Layouts stays
    /// at its original count, with no fabricated entries.
    /// </summary>
    [Fact]
    public void WellFormedPackage_ResolvesViaExactPathMatch_NoOrphanEntryAdded()
    {
        var pres = PresentationModel.CreateEmpty();
        var bytes = WriteToBytes(pres);

        var reloaded = PptxPackageReader.Read(new MemoryStream(bytes));

        reloaded.Layouts.Should().ContainSingle(
            "a well-formed single-layout package must not grow a phantom layout entry");
        var slide = reloaded.Slides[0];
        var resolvedLayout = reloaded.Layouts.Find(l => l.Id == slide.LayoutId);
        resolvedLayout.Should().NotBeNull();
        resolvedLayout!.Name.Should().Be("Title Slide");
    }
}
