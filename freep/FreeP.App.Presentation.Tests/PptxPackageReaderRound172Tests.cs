using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// round172: two fixes to <see cref="PptxPackageReader"/>.
///
/// F1 -- round171's new "missing part" guard (see PptxPackageReaderMissingRequiredPartTests)
/// resolved the root officeDocument relationship's Target with a bare
/// <c>OpcPathHelper.ToZipEntryPath</c> call, which only strips a leading '/'. A relationship
/// Target is a URI reference: dot segments must collapse before it names a zip entry. A
/// spec-legal target like "./ppt/presentation.xml" therefore failed the guard's exact-string
/// <c>archive.GetEntry(...)</c> lookup and made a completely intact package throw "corrupt",
/// which is exactly the over-correction the round171 finding warned about. Fixed by resolving
/// the target through <c>OpcPathHelper.ResolveRelativeZipPath</c> -- the same helper every other
/// relationship target in this reader already goes through.
///
/// F2 -- <c>ReadArchive</c> keyed each <see cref="SlideLayout"/> by the raw relationship id from
/// its OWNING MASTER's own .rels file (e.g. "rId1"). Every master's .rels is numbered
/// independently starting at rId1, so in a multi-master deck two different masters' first
/// layouts both got <c>Id == "rId1"</c>. Every downstream consumer resolves a slide's layout via
/// <c>presentation.Layouts.Find(l => l.Id == slide.LayoutId)</c>, and <c>List&lt;T&gt;.Find</c>
/// always returns the FIRST match -- so a slide legitimately attached to the second master's
/// layout resolved back to the first master's layout instead. Fixed by keying
/// <see cref="SlideLayout.Id"/> by the layout's own normalized zip <c>PartPath</c> instead, which
/// is globally unique across the whole package (matching the convention
/// <c>ResolveOrphanLayout</c> in the same file already used).
/// </summary>
public sealed class PptxPackageReaderRound172Tests
{
    private static readonly XNamespace PkgRel =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string OfficeDocRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    // ── F1: dot-relative root relationship target ──────────────────────────────────

    /// <summary>
    /// Builds a genuine, well-formed .pptx via the real writer, then rewrites ONLY the root
    /// officeDocument relationship's Target attribute to <paramref name="rewrittenTarget"/> --
    /// leaving every other byte of the archive (including the presentation.xml part itself)
    /// untouched, mirroring the adversarial reviewer's one-attribute-edit repro.
    /// </summary>
    private static MemoryStream BuildPptxWithRewrittenOfficeDocumentTarget(string rewrittenTarget)
    {
        var presentation = PresentationModel.CreateEmpty();
        var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("_rels/.rels");
            entry.Should().NotBeNull();

            XDocument rootRels;
            using (var readStream = entry!.Open())
                rootRels = XDocument.Load(readStream);

            var officeDocRel = rootRels.Root!.Elements(PkgRel + "Relationship")
                .First(r => r.Attribute("Type")?.Value == OfficeDocRelType);
            officeDocRel.Attribute("Target")!.Value = rewrittenTarget;

            entry.Delete();
            var newEntry = archive.CreateEntry("_rels/.rels");
            using var writeStream = newEntry.Open();
            rootRels.Save(writeStream);
        }

        buffer.Position = 0;
        return buffer;
    }

    [Theory]
    [InlineData("./ppt/presentation.xml")]
    [InlineData("ppt/foo/../presentation.xml")]
    public void Read_RootOfficeDocumentTargetUsesLegalDotRelativeSpelling_OpensIntactPackage(
        string rewrittenTarget)
    {
        using var pptx = BuildPptxWithRewrittenOfficeDocumentTarget(rewrittenTarget);

        var presentation = default(PresentationModel);
        var act = () => presentation = PptxPackageReader.Read(pptx);

        act.Should().NotThrow(
            "a relationship Target is a URI reference -- dot segments collapse before it names a " +
            "zip entry, so this spec-legal spelling names the exact same intact part as the " +
            "literal 'ppt/presentation.xml' path used elsewhere in the archive");
        presentation!.Slides.Should().NotBeEmpty(
            "the package is fully intact and must load its real slide content, not merely avoid throwing");
    }

    /// <summary>
    /// Sibling no-regression: round171's guard must still refuse a package whose officeDocument
    /// relationship points at a part that is genuinely absent -- resolving the target properly
    /// must not weaken the guard back to round171's silent-empty-open behaviour.
    /// </summary>
    [Fact]
    public void Read_RootOfficeDocumentTargetStillPointsAtGenuinelyMissingPart_StillThrows()
    {
        var presentation = PresentationModel.CreateEmpty();
        var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("ppt/presentation.xml")!.Delete();
        }

        buffer.Position = 0;

        Action act = () => PptxPackageReader.Read(buffer);

        act.Should().Throw<InvalidDataException>(
            "a dot-relative target that resolves to a part which is truly missing from the archive " +
            "must still be reported as a failed open");
    }

    // ── F2: colliding layout relationship ids across masters ───────────────────────

    /// <summary>
    /// Builds a genuine two-master, two-layout, two-slide deck entirely in-memory and round-trips
    /// it through the real <see cref="PptxPackageWriter"/>/<see cref="PptxPackageReader"/> pair.
    /// Each master's own .rels is numbered independently starting at "rId1" by the writer (theme
    /// = rId1, its one layout = rId2), so on disk BOTH masters' .rels files use relationship id
    /// "rId2" for their (different) layout -- the real-world collision shape this finding
    /// describes, produced by the actual writer rather than hand-rolled XML.
    /// </summary>
    private static PresentationModel BuildTwoMasterDeck()
    {
        var presentation = new PresentationModel();
        presentation.Masters.Clear();
        presentation.Layouts.Clear();
        presentation.Slides.Clear();

        var master1 = new SlideMaster { Id = "rId1" };
        var master2 = new SlideMaster { Id = "rId2" };
        presentation.Masters.Add(master1);
        presentation.Masters.Add(master2);

        var layout1 = new SlideLayout
        {
            Id = "rId1",
            Name = "Master1 Layout",
            LayoutType = SlideLayoutType.Title,
            MasterId = master1.Id,
        };
        var layout2 = new SlideLayout
        {
            Id = "rId2",
            Name = "Master2 Layout",
            LayoutType = SlideLayoutType.TitleContent,
            MasterId = master2.Id,
        };
        presentation.Layouts.Add(layout1);
        presentation.Layouts.Add(layout2);

        var slideOnMaster1 = new Slide { LayoutId = layout1.Id, Title = "Slide on master 1" };
        var slideOnMaster2 = new Slide { LayoutId = layout2.Id, Title = "Slide on master 2" };
        presentation.Slides.Add(slideOnMaster1);
        presentation.Slides.Add(slideOnMaster2);

        return presentation;
    }

    [Fact]
    public void Read_TwoMasterDeckWithCollidingLayoutRelationshipIds_ResolvesEachSlideToItsOwnMastersLayout()
    {
        var presentation = BuildTwoMasterDeck();
        using var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;

        // Sanity: confirm the written package really does carry the collision (both masters'
        // own .rels use the same relationship id for their layout) -- otherwise this test would
        // not actually exercise the bug.
        buffer.Position = 0;
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true))
        {
            var master1LayoutRelIds = XDocument.Load(
                    archive.GetEntry("ppt/slideMasters/_rels/slideMaster1.xml.rels")!.Open())
                .Root!.Elements(PkgRel + "Relationship")
                .Where(r => r.Attribute("Target")!.Value.Contains("slideLayout"))
                .Select(r => r.Attribute("Id")!.Value)
                .ToList();
            var master2LayoutRelIds = XDocument.Load(
                    archive.GetEntry("ppt/slideMasters/_rels/slideMaster2.xml.rels")!.Open())
                .Root!.Elements(PkgRel + "Relationship")
                .Where(r => r.Attribute("Target")!.Value.Contains("slideLayout"))
                .Select(r => r.Attribute("Id")!.Value)
                .ToList();
            master1LayoutRelIds.Should().Equal(master2LayoutRelIds,
                "the writer numbers each master's own .rels independently, so this two-master " +
                "fixture must reproduce the real-world id collision the finding describes");
        }

        buffer.Position = 0;
        var reloaded = PptxPackageReader.Read(buffer);

        reloaded.Layouts.Should().HaveCount(2);
        reloaded.Layouts.Select(l => l.Id).Should().OnlyHaveUniqueItems(
            "layout identity must be globally unique across the whole package, not scoped to " +
            "the owning master's own independently-numbered .rels file");

        var reloadedSlideOnMaster1 = reloaded.Slides.Single(s => s.Title == "Slide on master 1");
        var reloadedSlideOnMaster2 = reloaded.Slides.Single(s => s.Title == "Slide on master 2");

        var resolvedLayout1 = reloaded.Layouts.Find(l => l.Id == reloadedSlideOnMaster1.LayoutId);
        var resolvedLayout2 = reloaded.Layouts.Find(l => l.Id == reloadedSlideOnMaster2.LayoutId);

        resolvedLayout1.Should().NotBeNull();
        resolvedLayout2.Should().NotBeNull();
        resolvedLayout1!.Name.Should().Be("Master1 Layout",
            "the slide attached to master 1's layout must resolve back to master 1's layout, " +
            "not silently collide with master 2's");
        resolvedLayout2!.Name.Should().Be("Master2 Layout",
            "List<T>.Find returns the FIRST id match -- before the fix this always returned " +
            "master 1's layout for both slides");
    }

    /// <summary>
    /// Sibling no-regression: an ordinary single-master deck (the common case, and the shape
    /// <see cref="PresentationModel.CreateEmpty"/> itself produces) must still resolve its slide's
    /// layout correctly after keying <see cref="SlideLayout.Id"/> by PartPath instead of the raw
    /// relationship id.
    /// </summary>
    [Fact]
    public void Read_SingleMasterDeck_LayoutStillResolvesCorrectly()
    {
        var presentation = PresentationModel.CreateEmpty();
        using var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;

        var reloaded = PptxPackageReader.Read(buffer);

        reloaded.Layouts.Should().ContainSingle();
        var slide = reloaded.Slides.Single();
        var resolvedLayout = reloaded.Layouts.Find(l => l.Id == slide.LayoutId);

        resolvedLayout.Should().NotBeNull();
        resolvedLayout!.Name.Should().Be("Title Slide");
    }
}
