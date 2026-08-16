using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// CONFIRMED FINDING (round 137): a LINKED (non-embedded) OLE object was silently dropped on
/// save while its r:id was still written into the p:oleObj element verbatim from the stored
/// OleObjXml — producing a package with a DANGLING relationship reference (p:oleObj/@r:id points
/// at a relationship that does not exist in the slide's .rels part). This is a package-validity
/// defect, not just a fidelity one: PowerPoint prompts "repair" and strict OOXML validators flag
/// the missing relationship.
///
/// Root cause: WriteSlideOleObjects (PptxPackageWriter.cs) only ever added an entry to its
/// embRels list — the list that feeds BOTH the slide .rels file AND the shape→relId map used to
/// patch p:oleObj's r:id — when <c>ole.EmbeddedBytes.Length > 0</c>. A linked object has empty
/// EmbeddedBytes by design (its data lives outside the package), so it fell through that check
/// entirely: no relationship was ever written for it, yet BuildOleGraphicFrameEl left the
/// ORIGINAL r:id from the verbatim OleObjXml untouched (because mediaById had no entry for the
/// shape), so the emitted XML still referenced a relationship id that no longer resolves.
///
/// Fix: PptxPackageReader now captures a linked object's External relationship (IsLinked +
/// LinkTarget) instead of trying to resolve it as an internal zip part, and PptxPackageWriter's
/// WriteSlideOleObjects re-emits that External relationship on write (with a freshly allocated
/// r:id, patched into the XML exactly like the embedded case), so the r:id always resolves.
/// </summary>
public sealed class LinkedOleObjectRoundTripTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.LinkedOleTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    private const string PNs = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string RNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Builds a LINKED OLE shape exactly as PptxPackageReader would produce it: EmbeddedBytes
    /// empty, IsLinked=true, LinkTarget set to the external path, and the verbatim OleObjXml
    /// carrying an r:id attribute (the id the ORIGINAL source document used — deliberately a
    /// value that will NOT be reused by the writer's own rIdOle* allocator, so the test cannot
    /// pass by accident if the writer merely happens to reuse the same literal id).
    /// </summary>
    private static SlideShape MakeLinkedOleShape(uint id, string linkTarget, string progId)
    {
        return new SlideShape
        {
            Id = id,
            Name = "LinkedOleShape" + id,
            Kind = SlideShapeKind.Ole,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1500000,
            OleObject = new OleObjectInfo
            {
                EmbeddedBytes = [],
                ProgId = progId,
                RelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject",
                IsLinked = true,
                LinkTarget = linkTarget,
                OleObjXml =
                    $"<p:oleObj xmlns:p=\"{PNs}\" xmlns:r=\"{RNs}\" r:id=\"rIdOriginalLink99\" progId=\"{progId}\" showAsIcon=\"1\"/>",
                WasAlternateContent = false,
            }
        };
    }

    private static (XDocument slideXml, XDocument? slideRels) ReadSlide1(string pptxPath)
    {
        using var zip = ZipFile.OpenRead(pptxPath);
        var slideEntry = zip.Entries.First(e => e.FullName == "ppt/slides/slide1.xml");
        XDocument slideXml;
        using (var s = slideEntry.Open()) slideXml = XDocument.Load(s);

        var relsEntry = zip.Entries.FirstOrDefault(e => e.FullName == "ppt/slides/_rels/slide1.xml.rels");
        XDocument? slideRels = null;
        if (relsEntry is not null)
        {
            using var rs = relsEntry.Open();
            slideRels = XDocument.Load(rs);
        }
        return (slideXml, slideRels);
    }

    /// <summary>
    /// THE finding, proved directly: a linked OLE object's p:oleObj/@r:id must resolve to an
    /// actual relationship entry in the slide's .rels part — no dangling reference — and that
    /// relationship must be External, pointing at the original link target, not silently dropped.
    /// </summary>
    [Fact]
    public void LinkedOle_RIdResolvesToExternalRelationship_NoDanglingReference()
    {
        var pres = new Presentation();
        var slide = new Slide();
        const string linkTarget = "file:///C:/Data/LinkedBook.xlsx";
        slide.Shapes.Add(MakeLinkedOleShape(10, linkTarget, "Excel.Sheet.12"));
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var (slideXml, slideRels) = ReadSlide1(path);

        slideRels.Should().NotBeNull("a slide containing a linked OLE object must still emit a .rels part");

        XNamespace p = PNs;
        XNamespace r = RNs;
        var oleObjEl = slideXml.Descendants(p + "oleObj").FirstOrDefault();
        oleObjEl.Should().NotBeNull("the linked OLE object must still be emitted as a p:oleObj element");

        var emittedRid = oleObjEl!.Attribute(r + "id")?.Value;
        emittedRid.Should().NotBeNullOrWhiteSpace("the emitted p:oleObj must carry an r:id");

        XNamespace rel = RelsNs;
        var relationships = slideRels!.Root!.Elements(rel + "Relationship").ToList();
        var matching = relationships.FirstOrDefault(e => (string?)e.Attribute("Id") == emittedRid);

        // THE CORE ASSERTION: the r:id the writer emitted must resolve to a relationship that
        // actually exists in the .rels part. Before the fix, WriteSlideOleObjects never added an
        // entry for a linked (EmbeddedBytes.Length == 0) object, so mediaById had no entry for
        // this shape, BuildOleGraphicFrameEl left the ORIGINAL "rIdOriginalLink99" r:id in place
        // unpatched, and no relationship with that id (or any id) was ever written for this
        // shape — a dangling reference.
        matching.Should().NotBeNull(
            $"p:oleObj/@r:id=\"{emittedRid}\" must resolve to a Relationship element in slide1.xml.rels " +
            "— an unresolved r:id is an invalid OOXML package (PowerPoint 'repair' prompt)");

        matching!.Attribute("TargetMode")?.Value.Should().Be("External",
            "a linked OLE object's relationship must be External, not an internal package part");
        matching.Attribute("Target")?.Value.Should().Be(linkTarget,
            "the link target must be preserved verbatim, not dropped");

        // No embeddings/ zip entry should have been written for a linked (non-embedded) object —
        // there is no binary payload to persist.
        using var zip = ZipFile.OpenRead(path);
        zip.Entries.Where(e => e.FullName.StartsWith("ppt/embeddings/", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("a linked OLE object has no embedded binary to write");
    }

    /// <summary>
    /// Sibling / no-regression: the ordinary EMBEDDED OLE case (the pre-existing, already-working
    /// path) must still resolve its r:id to a relationship and still write its binary part. This
    /// guards against the linked-object fix accidentally breaking the embedded branch it sits
    /// beside in WriteSlideOleObjects.
    /// </summary>
    [Fact]
    public void EmbeddedOle_RIdStillResolvesAndBinaryStillWritten_NoRegression()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 20,
            Name = "EmbeddedOleShape20",
            Kind = SlideShapeKind.Ole,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1500000,
            OleObject = new OleObjectInfo
            {
                EmbeddedBytes = [0x01, 0x02, 0x03, 0x04],
                EmbeddedContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                EmbeddedExtension = "xlsx",
                ProgId = "Excel.Sheet.12",
                RelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
                OleObjXml =
                    $"<p:oleObj xmlns:p=\"{PNs}\" xmlns:r=\"{RNs}\" r:id=\"rIdOriginalEmbed1\" progId=\"Excel.Sheet.12\" showAsIcon=\"1\"/>",
                WasAlternateContent = false,
            }
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var (slideXml, slideRels) = ReadSlide1(path);

        slideRels.Should().NotBeNull();

        XNamespace p = PNs;
        XNamespace r = RNs;
        var oleObjEl = slideXml.Descendants(p + "oleObj").FirstOrDefault();
        oleObjEl.Should().NotBeNull();
        var emittedRid = oleObjEl!.Attribute(r + "id")?.Value;
        emittedRid.Should().NotBeNullOrWhiteSpace();

        XNamespace rel = RelsNs;
        var matching = slideRels!.Root!.Elements(rel + "Relationship")
            .FirstOrDefault(e => (string?)e.Attribute("Id") == emittedRid);
        matching.Should().NotBeNull("the embedded object's r:id must resolve to a relationship");
        matching!.Attribute("TargetMode")?.Value.Should().NotBe("External",
            "an embedded object's relationship must be internal (package part), not External");

        using var zip = ZipFile.OpenRead(path);
        var embeddingEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("ppt/embeddings/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        embeddingEntries.Should().ContainSingle("the embedded object's binary must still be written to the package");
    }
}
