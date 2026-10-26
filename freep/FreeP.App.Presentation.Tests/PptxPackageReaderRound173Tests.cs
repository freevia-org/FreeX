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
/// round173: two fixes.
///
/// F1 -- round172 resolved a relationship <c>Target</c> through
/// <c>OpcPathHelper.ResolveRelativeZipPath</c>, which collapses dot segments and strips a leading
/// '/', but a relationship Target is a full URI reference: it can also carry percent-escaped path
/// segments and a trailing URI fragment, and separately, OPC part-name comparison is
/// case-insensitive even though zip entries are case-sensitive. round172 left all three
/// unhandled, so an intact package whose Target used any of them still threw "corrupt". Fixed by
/// routing every relationship-target resolution in this reader through the new
/// <see cref="Free.Shared.Opc.OpcPathHelper.ResolveRelationshipTargetZipPath"/> (fragment-strip +
/// percent-decode + the existing dot-collapse/leading-slash-strip), and every zip-entry lookup
/// through the new <see cref="Free.Shared.Opc.OpcPathHelper.FindEntry"/> (exact match first, an
/// UNAMBIGUOUS case-insensitive match as a fallback, null -- not a guess -- when more than one
/// entry differs from the requested path only by case).
///
/// F2 -- <c>ReadGraphicFrame</c> built the <see cref="SlideShape"/> for a table/chart/chartEx
/// directly from p:xfrm/graphicData without ever reading
/// <c>p:nvGraphicFramePr/p:nvPr/p:ph</c>, unlike <c>ReadSp</c>/<c>ReadPic</c> which both read the
/// equivalent p:ph for a shape/picture. <c>PptxPackageWriter</c>'s table/chart graphicFrame
/// builders had the matching gap on the write side (a bare <c>p:nvPr</c>, never populated via
/// <c>BuildPhEl</c>). Together this meant a table/chart placed in a content placeholder always lost
/// its placeholder identity -- both on open (silently) and on save (destructively) -- which is what
/// <c>SetSlideLayoutCommand.FindMatchingPlaceholder</c> (freep/FreeP.Core.Model/PresentationCommands.cs)
/// needs to reposition it and avoid cloning a duplicate empty placeholder when the slide's layout
/// changes. Fixed by reading/writing p:ph for table/chart/chartEx graphicFrames the same way
/// ReadSp/ReadPic and BuildShapeEl already do.
/// </summary>
public sealed class PptxPackageReaderRound173Tests
{
    private static readonly XNamespace PkgRel =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string OfficeDocRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    // ── F1: URI-reference axes on the root relationship target ─────────────────────

    /// <summary>
    /// Builds a genuine, well-formed .pptx via the real writer, then rewrites ONLY the root
    /// officeDocument relationship's Target attribute to <paramref name="rewrittenTarget"/> --
    /// leaving every other byte of the archive (including the presentation.xml part itself)
    /// untouched. Mirrors PptxPackageReaderRound172Tests' adversarial one-attribute-edit repro,
    /// extended to the percent-encoding/fragment/case axes round172 did not cover.
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
    [InlineData("ppt/pre%73entation.xml", "percent-encoded literal character within a segment")]
    [InlineData("ppt/presentation.xml#somefrag", "trailing URI fragment")]
    [InlineData("PPT/PRESENTATION.XML", "differently-cased target (OPC part-name comparison is case-insensitive)")]
    public void Read_RootOfficeDocumentTargetUsesUriReferenceAxis_OpensIntactPackage(
        string rewrittenTarget, string because)
    {
        using var pptx = BuildPptxWithRewrittenOfficeDocumentTarget(rewrittenTarget);

        var presentation = default(PresentationModel);
        var act = () => presentation = PptxPackageReader.Read(pptx);

        act.Should().NotThrow(
            $"a relationship Target is a URI reference -- {because} still names the exact same " +
            "intact 'ppt/presentation.xml' part, so an intact package must open");
        presentation!.Slides.Should().NotBeEmpty(
            "the package is fully intact and must load its real slide content, not merely avoid throwing");
    }

    /// <summary>
    /// Deliberately NOT fixed, and asserted here so the gap is visible rather than silent: a
    /// percent-encoded path SEPARATOR (<c>%2F</c>/<c>%5C</c>) is left encoded by
    /// <c>OpcPathHelper.UnescapeRelationshipPathSegments</c> rather than decoded back into a real
    /// '/' that would split one path segment into two. That is an existing, deliberate contract --
    /// <c>OpcSharedHelperTests.UnescapeRelationshipPathSegments_PreservesOpcPathControlSegments</c>
    /// (tests/FreeX.Core.IO.Tests/OpcSharedHelperTests.cs) asserts
    /// <c>"../media/image%2F1.png"</c> unescapes to itself, unchanged -- guarding against exactly
    /// the class of bug where decoding an escaped separator turns one validated segment into two
    /// new ones (letting an encoded "..%2F.." smuggle a directory-traversal segment past whatever
    /// dot-segment collapsing already ran on the pre-decode split). Rule 9: this is a contract
    /// conflict, not a defect -- decoding %2F here for FreeP would either contradict that shared
    /// test or require special-casing FreeP against FreeW/FreeX's shared helper, which the
    /// round173 directive explicitly asks us to avoid. Left alone.
    /// </summary>
    [Fact]
    public void Read_RootOfficeDocumentTargetHasPercentEncodedPathSeparator_StillThrows_KnownLimitation()
    {
        using var pptx = BuildPptxWithRewrittenOfficeDocumentTarget("ppt%2Fpresentation.xml");

        Action act = () => PptxPackageReader.Read(pptx);

        act.Should().Throw<InvalidDataException>(
            "OpcPathHelper.UnescapeRelationshipPathSegments deliberately preserves an escaped path " +
            "separator instead of decoding it into a real '/' -- see " +
            "OpcSharedHelperTests.UnescapeRelationshipPathSegments_PreservesOpcPathControlSegments");
    }

    /// <summary>
    /// Sibling no-regression: a package whose root relationship Target is case-AMBIGUOUS -- it
    /// case-insensitively matches TWO different zip entries -- must still be refused rather than
    /// have the reader silently guess which one was meant. This is the deliberate limit the
    /// round173 directive calls out: the case fallback must be unambiguous or not apply at all.
    /// <para>
    /// In THIS reader's actual pipeline, such a package is rejected earlier and for a different
    /// reason: <c>WorkbookOpenSizeGuard.EnsureArchiveWithinLimits</c> (called from
    /// <c>PptxPackageReader.Read(Stream)</c> before <c>ReadArchive</c> ever runs) already rejects
    /// any package containing two zip entries whose names differ only by case, throwing
    /// <see cref="Free.Shared.Opc.WorkbookInvalidException"/> -- so <c>FindEntry</c>'s own
    /// ambiguity guard never actually gets exercised via the public <c>Read</c> entry point for
    /// this specific shape of attack. This test proves the END-TO-END outcome (refused, not
    /// guessed) via the real API; <c>FindEntry_AmbiguousCaseInsensitiveMatch_ReturnsNull</c> below
    /// proves the new helper's OWN ambiguity guard directly and unconditionally, independent of
    /// that earlier guard existing at all.
    /// </para>
    /// </summary>
    [Fact]
    public void Read_RootOfficeDocumentTargetIsCaseAmbiguous_StillThrowsRatherThanGuessing()
    {
        var presentation = PresentationModel.CreateEmpty();
        var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            // Add a second, differently-cased entry for the same conceptual part -- a
            // pathological but zip-legal package (zip entries are case-sensitive, so both can
            // coexist), and point the root relationship at a THIRD casing that exactly matches
            // neither entry, forcing the case-insensitive fallback to face two candidates.
            var originalEntry = archive.GetEntry("ppt/presentation.xml");
            originalEntry.Should().NotBeNull();
            byte[] originalBytes;
            using (var s = originalEntry!.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                originalBytes = ms.ToArray();
            }

            var duplicateEntry = archive.CreateEntry("PPT/PRESENTATION.XML");
            using (var writeStream = duplicateEntry.Open())
                writeStream.Write(originalBytes, 0, originalBytes.Length);

            var relsEntry = archive.GetEntry("_rels/.rels")!;
            XDocument rootRels;
            using (var readStream = relsEntry.Open())
                rootRels = XDocument.Load(readStream);

            var officeDocRel = rootRels.Root!.Elements(PkgRel + "Relationship")
                .First(r => r.Attribute("Type")?.Value == OfficeDocRelType);
            officeDocRel.Attribute("Target")!.Value = "Ppt/Presentation.xml";

            relsEntry.Delete();
            var newRelsEntry = archive.CreateEntry("_rels/.rels");
            using var writeRelsStream = newRelsEntry.Open();
            rootRels.Save(writeRelsStream);
        }

        buffer.Position = 0;

        Action act = () => PptxPackageReader.Read(buffer);

        act.Should().Throw<Free.Shared.Opc.WorkbookInvalidException>(
            "the package contains two zip entries differing only by case -- WorkbookOpenSizeGuard " +
            "rejects that outright (before ReadArchive/FindEntry ever run) rather than let anything " +
            "downstream guess which one the ambiguous target meant");
    }

    // ── F1: OpcPathHelper.FindEntry's own ambiguity guard, tested directly ──────────

    [Fact]
    public void FindEntry_ExactCaseMatch_ReturnsIt()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            archive.CreateEntry("ppt/presentation.xml");

        buffer.Position = 0;
        using var readArchive = new ZipArchive(buffer, ZipArchiveMode.Read);

        var found = Free.Shared.Opc.OpcPathHelper.FindEntry(readArchive, "ppt/presentation.xml");

        found.Should().NotBeNull();
        found!.FullName.Should().Be("ppt/presentation.xml");
    }

    [Fact]
    public void FindEntry_UnambiguousCaseInsensitiveMatch_FallsBackToIt()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            archive.CreateEntry("ppt/presentation.xml");

        buffer.Position = 0;
        using var readArchive = new ZipArchive(buffer, ZipArchiveMode.Read);

        // Requested path differs only by case from the one real entry -- exactly the round173
        // repro (a Target like "PPT/PRESENTATION.XML" against a real "ppt/presentation.xml" entry).
        var found = Free.Shared.Opc.OpcPathHelper.FindEntry(readArchive, "PPT/PRESENTATION.XML");

        found.Should().NotBeNull(
            "OPC part-name comparison is case-insensitive, so a single differently-cased entry " +
            "must still be found");
        found!.FullName.Should().Be("ppt/presentation.xml");
    }

    [Fact]
    public void FindEntry_AmbiguousCaseInsensitiveMatch_ReturnsNull()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Two entries differing only by case -- zip entry names ARE case-sensitive, so this is
            // a legal (if pathological) archive. Request a THIRD casing matching neither exactly.
            archive.CreateEntry("ppt/presentation.xml");
            archive.CreateEntry("PPT/PRESENTATION.XML");
        }

        buffer.Position = 0;
        using var readArchive = new ZipArchive(buffer, ZipArchiveMode.Read);

        var found = Free.Shared.Opc.OpcPathHelper.FindEntry(readArchive, "Ppt/Presentation.xml");

        found.Should().BeNull(
            "two entries differ from the requested path only by case -- FindEntry must refuse to " +
            "guess which one was meant, exactly like a genuine miss, rather than silently pick one");
    }

    [Fact]
    public void FindEntry_NoMatchAtAll_ReturnsNull()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            archive.CreateEntry("ppt/presentation.xml");

        buffer.Position = 0;
        using var readArchive = new ZipArchive(buffer, ZipArchiveMode.Read);

        var found = Free.Shared.Opc.OpcPathHelper.FindEntry(readArchive, "ppt/slides/slide1.xml");

        found.Should().BeNull("a genuinely missing part must still resolve to null, not throw or guess");
    }

    /// <summary>
    /// Sibling no-regression (also proven by PptxPackageReaderRound172Tests, re-asserted here so
    /// this file stands alone): a dot-relative target that resolves to a genuinely missing part
    /// must still be reported as a failed open. None of the round173 URI-reference handling may
    /// weaken this back to round171's silent-empty-open behaviour.
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
            "a target that resolves to a part which is truly missing from the archive must still " +
            "be reported as a failed open, not silently degrade to an empty presentation");
    }

    // ── F2: table/chart placeholder identity ────────────────────────────────────

    private static Presentation BuildPresWithTablePlaceholder(Placeholder? placeholder)
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(2743200L);
        table.ColumnWidthsEmu.Add(2743200L);
        var row = new TableRow { HeightEmu = 685800L };
        row.Cells.Add(new TableCell());
        row.Cells.Add(new TableCell());
        table.Rows.Add(row);

        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 10,
            Name = "Table 1",
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 1371600,
            Placeholder = placeholder,
            Table = table
        });
        pres.Slides.Add(slide);
        return pres;
    }

    private static Presentation BuildPresWithChartPlaceholder(Placeholder? placeholder)
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });
        var series = new ChartSeries { Name = "Sales" };
        series.Values.AddRange(new double?[] { 100, 200 });
        chart.Series.Add(series);

        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 20,
            Name = "Chart 1",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 5486400,
            ExtentCyEmu = 3657600,
            Placeholder = placeholder,
            Chart = chart
        });
        pres.Slides.Add(slide);
        return pres;
    }

    private static MemoryStream WriteToStream(Presentation pres)
    {
        var buffer = new MemoryStream();
        PptxPackageWriter.Write(pres, buffer);
        buffer.Position = 0;
        return buffer;
    }

    [Fact]
    public void Read_TableInContentPlaceholder_PreservesPlaceholderIdentityAcrossRoundTrip()
    {
        var placeholder = new Placeholder { Type = PlaceholderType.Object, Idx = 1 };
        var pres = BuildPresWithTablePlaceholder(placeholder);

        using var buffer = WriteToStream(pres);

        // Sanity: confirm the writer really did emit p:ph on the graphicFrame -- otherwise this
        // test would not exercise the write-side half of the bug at all.
        buffer.Position = 0;
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true))
        {
            var slideXml = XDocument.Load(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
            var ns = (XNamespace)"http://schemas.openxmlformats.org/presentationml/2006/main";
            var phEl = slideXml.Descendants(ns + "graphicFrame")
                .Elements(ns + "nvGraphicFramePr").Elements(ns + "nvPr").Elements(ns + "ph")
                .SingleOrDefault();
            phEl.Should().NotBeNull(
                "the writer must emit p:nvGraphicFramePr/p:nvPr/p:ph for a placeholder-bound table, " +
                "the same way it does for a placeholder-bound shape/picture");
            phEl!.Attribute("idx")!.Value.Should().Be("1");
        }

        buffer.Position = 0;
        var reloaded = PptxPackageReader.Read(buffer);

        var tableShape = reloaded.Slides.Single().Shapes.Single(s => s.Kind == SlideShapeKind.Table);
        tableShape.Placeholder.Should().NotBeNull(
            "p:nvGraphicFramePr/p:nvPr/p:ph on a table graphicFrame must be read back into " +
            "SlideShape.Placeholder, the same way ReadSp/ReadPic already do for shapes/pictures");
        tableShape.Placeholder!.Type.Should().Be(PlaceholderType.Object);
        tableShape.Placeholder.Idx.Should().Be(1);
    }

    [Fact]
    public void Read_ChartInContentPlaceholder_PreservesPlaceholderIdentityAcrossRoundTrip()
    {
        var placeholder = new Placeholder { Type = PlaceholderType.Chart, Idx = 2 };
        var pres = BuildPresWithChartPlaceholder(placeholder);

        using var buffer = WriteToStream(pres);
        var reloaded = PptxPackageReader.Read(buffer);

        var chartShape = reloaded.Slides.Single().Shapes.Single(s => s.Kind == SlideShapeKind.Chart);
        chartShape.Placeholder.Should().NotBeNull(
            "p:nvGraphicFramePr/p:nvPr/p:ph on a chart graphicFrame must be read back into " +
            "SlideShape.Placeholder");
        chartShape.Placeholder!.Type.Should().Be(PlaceholderType.Chart);
        chartShape.Placeholder.Idx.Should().Be(2);
    }

    /// <summary>
    /// Sibling no-regression: a table with NO placeholder identity (the ordinary, freestanding
    /// case -- e.g. a table dropped anywhere on the slide, not into a content placeholder) must
    /// keep round-tripping with a null Placeholder. The fix must not fabricate a placeholder that
    /// was never there.
    /// </summary>
    [Fact]
    public void Read_FreestandingTableWithNoPlaceholder_StaysWithoutPlaceholder()
    {
        var pres = BuildPresWithTablePlaceholder(placeholder: null);

        using var buffer = WriteToStream(pres);
        var reloaded = PptxPackageReader.Read(buffer);

        var tableShape = reloaded.Slides.Single().Shapes.Single(s => s.Kind == SlideShapeKind.Table);
        tableShape.Placeholder.Should().BeNull(
            "a table that was never bound to a content placeholder must not have one fabricated " +
            "for it by the fix");
    }
}
