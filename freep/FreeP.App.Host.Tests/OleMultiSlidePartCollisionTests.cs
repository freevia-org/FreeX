using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Round 131 fix: OLE embedded-object part paths ("ppt/embeddings/oleObjectN.ext") must be
/// numbered with ONE monotonically-increasing scheme shared between the actual zip writer
/// (WriteSlideOleObjects) and the [Content_Types].xml Override prediction (BuildContentTypesXml).
///
/// Before the fix:
///   - WriteSlideOleObjects reset its embCounter to 1 on every call (once per slide), so slide 2's
///     OLE object collided with slide 1's path (duplicate "ppt/embeddings/oleObject1.*" zip entry).
///   - BuildContentTypesXml predicted with a separate GLOBAL never-reset oleEmbIdx, and deduped
///     Override emission using seenOleParts.Add(shape.Id.ToString()) — keyed on shape id ONLY, with
///     no slide index — so two slides whose OLE shapes happen to share a cNvPr id (very common,
///     since PowerPoint/round-tripped ids often restart per slide) silently dropped the second
///     Override entirely.
/// </summary>
public sealed class OleMultiSlidePartCollisionTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.OleMultiSlideTests", Guid.NewGuid().ToString("N"));

    public OleMultiSlidePartCollisionTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    private static SlideShape MakeOleShape(uint id, byte[] embeddedBytes, string ext, string contentType, string progId)
    {
        return new SlideShape
        {
            Id = id,
            Name = "OleShape" + id,
            Kind = SlideShapeKind.Ole,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1500000,
            OleObject = new OleObjectInfo
            {
                EmbeddedBytes = embeddedBytes,
                EmbeddedContentType = contentType,
                EmbeddedExtension = ext,
                ProgId = progId,
                RelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
                OleObjXml = $"<p:oleObj xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" progId=\"{progId}\" showAsIcon=\"1\"/>",
                WasAlternateContent = false,
            }
        };
    }

    /// <summary>
    /// FA-style pre-scan/writer helper: reads the written [Content_Types].xml back out of the zip.
    /// </summary>
    private static XDocument ReadContentTypes(string pptxPath)
    {
        using var zip = ZipFile.OpenRead(pptxPath);
        var entry = zip.Entries.First(e => e.FullName == "[Content_Types].xml");
        using var ms = new MemoryStream();
        using (var s = entry.Open()) s.CopyTo(ms);
        ms.Position = 0;
        return XDocument.Load(ms);
    }

    /// <summary>
    /// Two slides, each with one OLE shape whose cNvPr id COLLIDES across slides (id=10 on both) —
    /// this is the exact scenario the finding calls out: "two slides whose OLE shapes share a
    /// cNvPr id". Each slide's embedded binary must land at a DISTINCT ppt/embeddings/ path (no
    /// duplicate zip entry names), and [Content_Types].xml must carry an Override for BOTH.
    /// </summary>
    [Fact]
    public void MultiSlide_Ole_SameShapeId_BothEmbeddedPartsWrittenAndOverridden()
    {
        var pres = new Presentation();

        var slide1 = new Slide();
        slide1.Shapes.Add(MakeOleShape(10, new byte[] { 0x01, 0x02, 0x03 }, "xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Excel.Sheet.12"));
        pres.Slides.Add(slide1);

        var slide2 = new Slide();
        // Same shape id (10) as slide1 — mirrors real-world documents where per-slide cNvPr ids restart.
        slide2.Shapes.Add(MakeOleShape(10, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, "xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Excel.Sheet.12"));
        pres.Slides.Add(slide2);

        var path = WriteToPptx(pres);

        using var zip = ZipFile.OpenRead(path);
        var embeddingEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("ppt/embeddings/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Two distinct embedded objects must produce two distinct zip entry names — no collision.
        embeddingEntries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase)
            .Should().HaveCount(2, "each slide's OLE binary must be written to its own unique part path");
        embeddingEntries.Should().HaveCount(2, "there must be no duplicate zip entries for the two OLE parts");

        // Each of the two written parts must have a matching Override in [Content_Types].xml —
        // the corrupted-package repair prompt happens when a part exists but has no Override.
        var ct = ReadContentTypes(path);
        XNamespace ct_ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        var overridePartNames = ct.Root!.Elements(ct_ns + "Override")
            .Select(o => (string)o.Attribute("PartName")!)
            .Where(p => p.StartsWith("/ppt/embeddings/oleObject", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entry in embeddingEntries)
        {
            var partName = "/" + entry.FullName;
            overridePartNames.Should().Contain(partName,
                $"the written part {partName} must have a matching Content_Types Override or PowerPoint will prompt to repair the file");
        }
    }

    /// <summary>
    /// Sibling/no-regression: a SINGLE slide with TWO OLE shapes (distinct ids) must still get two
    /// distinct embedded parts and two distinct Overrides — the ordinary same-slide multi-OLE case
    /// must not be broken by the cross-slide fix.
    /// </summary>
    [Fact]
    public void SingleSlide_TwoOleShapes_BothEmbeddedPartsWrittenAndOverridden()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(MakeOleShape(10, new byte[] { 0x01 }, "bin", "application/vnd.ms-excel", "Excel.Sheet.8"));
        slide.Shapes.Add(MakeOleShape(11, new byte[] { 0x02, 0x03 }, "bin", "application/vnd.ms-excel", "Excel.Sheet.8"));
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);

        using var zip = ZipFile.OpenRead(path);
        var embeddingEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("ppt/embeddings/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        embeddingEntries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase)
            .Should().HaveCount(2, "two OLE shapes on the same slide must still get two unique part paths");

        var ct = ReadContentTypes(path);
        XNamespace ct_ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        var overridePartNames = ct.Root!.Elements(ct_ns + "Override")
            .Select(o => (string)o.Attribute("PartName")!)
            .Where(p => p.StartsWith("/ppt/embeddings/oleObject", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entry in embeddingEntries)
        {
            var partName = "/" + entry.FullName;
            overridePartNames.Should().Contain(partName,
                $"the written part {partName} must have a matching Content_Types Override");
        }
    }

    /// <summary>
    /// Round 132 fix: WriteSlideOleObjects and BuildContentTypesXml MUST derive the
    /// "ppt/embeddings/oleObjectN.ext" part path from ONE shared helper
    /// (PptxPackageWriter.GetOleEmbeddedPartPath). r131 aligned the two call sites' literals but
    /// left each with its OWN empty-extension fallback: the writer fell back to "bin", while the
    /// Content_Types predictor used the raw (empty) EmbeddedExtension verbatim — producing
    /// "oleObject1.bin" in the zip but "/ppt/embeddings/oleObject1." (trailing dot, no extension)
    /// in the Override. That desync reopens the exact "part exists but has no Override / Override
    /// points at nothing" repair-prompt class r131 closed, specifically for an EMPTY/null
    /// EmbeddedExtension.
    /// </summary>
    [Fact]
    public void Ole_EmptyExtension_WrittenZipPathAndContentTypesOverrideMatch()
    {
        var pres = new Presentation();
        var slide = new Slide();
        // Explicit EMPTY extension (not null-default) — the exact case the finding calls out.
        slide.Shapes.Add(MakeOleShape(10, new byte[] { 0x01, 0x02 }, ext: "",
            contentType: "application/octet-stream", progId: "Package"));
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);

        using var zip = ZipFile.OpenRead(path);
        var embeddingEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("ppt/embeddings/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        embeddingEntries.Should().HaveCount(1);
        // The writer's own empty-extension fallback is "bin" — assert the ACTUAL written path,
        // so this test cannot be satisfied by both sides drifting to some other shared literal.
        embeddingEntries[0].FullName.Should().Be("ppt/embeddings/oleObject1.bin");

        var ct = ReadContentTypes(path);
        XNamespace ct_ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        var overridePartNames = ct.Root!.Elements(ct_ns + "Override")
            .Select(o => (string)o.Attribute("PartName")!)
            .Where(p => p.StartsWith("/ppt/embeddings/oleObject", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Exactly one embeddings Override, and it must point at the part that was ACTUALLY written.
        overridePartNames.Should().ContainSingle()
            .Which.Should().Be("/ppt/embeddings/oleObject1.bin",
                "the Content_Types Override must predict the SAME path the writer actually used for the empty-extension OLE part");
    }

    /// <summary>
    /// Round 132 durable invariant: for a deck exercising several part families in one slide
    /// (OLE with an EMPTY extension, speaker notes, and a legacy comment), EVERY part path
    /// declared via a Content_Types Override or Default corresponds to an actual zip entry, and
    /// EVERY actual zip entry is covered by either an Override or a Default — in both directions.
    /// This is the "cannot drift again" guard the finding asked for: it does not hard-code any
    /// path literal, so it keeps catching this whole class of desync even if the shared helper's
    /// fallback extension changes in the future.
    /// </summary>
    [Fact]
    public void AllPackageParts_ContentTypesCoverage_MatchesActualZipEntries_BothDirections()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(MakeOleShape(10, new byte[] { 0x01, 0x02, 0x03 }, ext: "",
            contentType: "application/octet-stream", progId: "Package"));
        slide.Notes = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Speaker note." });
        slide.Notes.Paragraphs.Add(para);
        slide.Comments.Add(new SlideComment
        {
            Author = "Reviewer",
            Initials = "RV",
            Text = "Looks good.",
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);

        using var zip = ZipFile.OpenRead(path);
        var ct = ReadContentTypes(path);
        XNamespace ct_ns = "http://schemas.openxmlformats.org/package/2006/content-types";

        var overridePartNames = ct.Root!.Elements(ct_ns + "Override")
            .Select(o => (string)o.Attribute("PartName")!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultExtensions = ct.Root!.Elements(ct_ns + "Default")
            .Select(d => (string)d.Attribute("Extension")!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        static string ExtensionOf(string entryFullName)
        {
            var fileName = entryFullName[(entryFullName.LastIndexOf('/') + 1)..];
            var dot = fileName.LastIndexOf('.');
            // OPC extension rule: text after the LAST '.' in the segment, even when that '.' is
            // the first character (e.g. "_rels/.rels" -> extension "rels").
            return dot >= 0 ? fileName[(dot + 1)..] : "";
        }

        // Direction 1: every actual part (excluding the content-types stream itself) must be
        // covered by an Override for its exact path, OR a Default for its extension.
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                continue;

            var partName = "/" + entry.FullName;
            var covered = overridePartNames.Contains(partName) ||
                          defaultExtensions.Contains(ExtensionOf(entry.FullName));

            covered.Should().BeTrue(
                $"zip part {partName} must be covered by a Content_Types Override or Default, " +
                "or PowerPoint will prompt to repair the file");
        }

        // Direction 2: every declared Override must point at a part that actually exists.
        var actualPartNames = zip.Entries
            .Select(e => "/" + e.FullName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var overridePartName in overridePartNames)
        {
            actualPartNames.Should().Contain(overridePartName,
                $"Content_Types Override {overridePartName} must correspond to an actual zip entry");
        }
    }
}
