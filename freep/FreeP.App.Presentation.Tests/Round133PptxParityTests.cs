using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;
using FluentAssertions;
using Xunit;

using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 133 findings: link-only media relationships, ppt/tableStyles.xml preservation,
/// a:lnSpc, a:spcPct (spcBef/spcAft percentage variant), and group a:chOff/a:chExt.
/// </summary>
public sealed class Round133PptxParityTests
{
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";

    // ── helpers ──────────────────────────────────────────────────────────────

    private static XDocument ReadZipEntryXml(byte[] pptxBytes, string entryPath)
    {
        using var ms = new MemoryStream(pptxBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry(entryPath);
        entry.Should().NotBeNull($"the pptx package must contain {entryPath}");
        using var s = entry!.Open();
        return XDocument.Load(s);
    }

    private static byte[] WriteToBytes(PresentationModel pres)
    {
        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        return ms.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (a) HIGH: link-only (external) audio/video media
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Writer_LinkOnlyVideo_EmitsExternalRelationshipThatVideoFileActuallyReferences()
    {
        var pres = new PresentationModel();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Linked video",
            Kind = SlideShapeKind.Media,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = Array.Empty<byte>(),
                LinkUrl = "http://example.com/clip.mp4",
            },
        });
        pres.Slides.Add(slide);

        var bytes = WriteToBytes(pres);
        var slideXml = ReadZipEntryXml(bytes, "ppt/slides/slide1.xml");
        var relsXml = ReadZipEntryXml(bytes, "ppt/slides/_rels/slide1.xml.rels");

        var videoFile = slideXml.Descendants(A + "videoFile").SingleOrDefault();
        videoFile.Should().NotBeNull("BuildMediaPicEl must emit a:videoFile for a link-only video shape");
        var linkRelId = videoFile!.Attribute(R + "link")?.Value;
        linkRelId.Should().NotBeNullOrEmpty();

        var relationship = relsXml.Root!.Elements(Rel + "Relationship")
            .SingleOrDefault(e => e.Attribute("Id")?.Value == linkRelId);

        // Before the fix: WriteSlideMediaFiles skipped this shape entirely (Bytes.Length == 0),
        // so no relationship with this Id was ever written -- videoFile's r:link dangles
        // (or, with an embedded sibling present, silently aliases someone else's rId).
        relationship.Should().NotBeNull(
            "the relationship id referenced by a:videoFile/@r:link must actually exist in the .rels part");
        relationship!.Attribute("TargetMode")?.Value.Should().Be("External");
        relationship.Attribute("Target")?.Value.Should().Be("http://example.com/clip.mp4",
            "the authored MediaInfo.LinkUrl must survive into the relationship target, not be dropped");
        relationship.Attribute("Type")?.Value.Should().EndWith("/video");
    }

    [Fact]
    public void Writer_LinkOnlyAudioAlongsideEmbeddedVideo_DoesNotAliasEmbeddedShapesRelId()
    {
        // Sibling/no-regression: an embedded video (owns a real "rIdVid1") coexisting with a
        // link-only audio shape must not cause the link-only shape's videoFile/audioFile to fall
        // back to -- and thus silently point at -- the embedded shape's file.
        var pres = new PresentationModel();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Embedded video",
            Kind = SlideShapeKind.Media,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 1000000,
            ExtentCyEmu = 1000000,
            Media = new MediaInfo { IsVideo = true, Bytes = new byte[] { 1, 2, 3, 4 }, ContentType = "video/mp4" },
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Linked audio",
            Kind = SlideShapeKind.Media,
            OffsetXEmu = 2000000,
            OffsetYEmu = 0,
            ExtentCxEmu = 1000000,
            ExtentCyEmu = 1000000,
            Media = new MediaInfo
            {
                IsVideo = false,
                Bytes = Array.Empty<byte>(),
                LinkUrl = "http://example.com/track.mp3",
            },
        });
        pres.Slides.Add(slide);

        var bytes = WriteToBytes(pres);
        var slideXml = ReadZipEntryXml(bytes, "ppt/slides/slide1.xml");
        var relsXml = ReadZipEntryXml(bytes, "ppt/slides/_rels/slide1.xml.rels");

        var videoFile = slideXml.Descendants(A + "videoFile").SingleOrDefault();
        var audioFile = slideXml.Descendants(A + "audioFile").SingleOrDefault();
        videoFile.Should().NotBeNull();
        audioFile.Should().NotBeNull();

        var videoRelId = videoFile!.Attribute(R + "link")?.Value;
        var audioRelId = audioFile!.Attribute(R + "link")?.Value;
        audioRelId.Should().NotBe(videoRelId, "the linked audio must get its own relationship id, not alias the embedded video's");

        var audioRel = relsXml.Root!.Elements(Rel + "Relationship").Single(e => e.Attribute("Id")?.Value == audioRelId);
        audioRel.Attribute("Target")?.Value.Should().Be("http://example.com/track.mp3");
        audioRel.Attribute("TargetMode")?.Value.Should().Be("External");

        var videoRel = relsXml.Root!.Elements(Rel + "Relationship").Single(e => e.Attribute("Id")?.Value == videoRelId);
        (videoRel.Attribute("TargetMode")?.Value).Should().NotBe("External", "the embedded video must remain a package-internal relationship");
    }

    [Fact]
    public void RoundTrip_LinkOnlyVideo_LinkUrlSurvivesAndBytesStayEmpty()
    {
        var pres = new PresentationModel();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Linked video",
            Kind = SlideShapeKind.Media,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 1000000,
            ExtentCyEmu = 1000000,
            Media = new MediaInfo { IsVideo = true, Bytes = Array.Empty<byte>(), LinkUrl = "http://example.com/clip.mp4" },
        });
        pres.Slides.Add(slide);

        var bytes = WriteToBytes(pres);
        using var ms = new MemoryStream(bytes);
        var reloaded = PptxPackageReader.Read(ms);

        var media = reloaded.Slides[0].Shapes[0].Media;
        media.Should().NotBeNull();
        media!.Bytes.Should().BeEmpty();
        media.LinkUrl.Should().Be("http://example.com/clip.mp4");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (b) MED: ppt/tableStyles.xml preservation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Writer_PreservesCustomTableStylesXmlOnSave()
    {
        // Arrange: write a baseline deck, then splice in a hand-authored custom table style
        // (as if it had been authored/round-tripped through PowerPoint) before reading it back.
        var pres = new PresentationModel();
        pres.Slides.Add(new Slide());
        var baseline = WriteToBytes(pres);

        // Deliberately brace-free: FluentAssertions' failure-message formatter treats literal
        // "{...}" in an actual/expected value as a format placeholder and garbles the message.
        const string customGuid = "CustomStyle-11111111-2222-3333-4444-555555555555";
        byte[] patched;
        using (var msIn = new MemoryStream(baseline))
        using (var msOut = new MemoryStream())
        {
            using (var srcZip = new ZipArchive(msIn, ZipArchiveMode.Read))
            using (var dstZip = new ZipArchive(msOut, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in srcZip.Entries)
                {
                    var dstEntry = dstZip.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                    using var src = entry.Open();
                    using var dst = dstEntry.Open();
                    if (entry.FullName == "ppt/tableStyles.xml")
                    {
                        var customXml = new XDocument(
                            new XDeclaration("1.0", "UTF-8", "yes"),
                            new XElement(A + "tblStyleLst",
                                new XAttribute("def", customGuid),
                                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                                new XElement(A + "tblStyle",
                                    new XAttribute("styleId", customGuid),
                                    new XAttribute("styleName", "My Custom Style"))));
                        customXml.Save(dst, SaveOptions.DisableFormatting);
                    }
                    else
                    {
                        src.CopyTo(dst);
                    }
                }
            }
            patched = msOut.ToArray();
        }

        // Act: read the patched package (captures the custom tableStyles.xml into PackageSnapshot)
        // then write it straight back out.
        using var readStream = new MemoryStream(patched);
        var reloaded = PptxPackageReader.Read(readStream);
        var resaved = WriteToBytes(reloaded);

        // Assert: the custom style definition must survive the round trip, not be discarded in
        // favor of the hard-coded empty stub.
        var tableStylesXml = ReadZipEntryXml(resaved, "ppt/tableStyles.xml");
        tableStylesXml.Root!.Attribute("def")?.Value.Should().Be(customGuid,
            "a custom table style definition present in the source package must be preserved on save");
        tableStylesXml.Root.Elements(A + "tblStyle").Should().ContainSingle(
            e => e.Attribute("styleName") != null && e.Attribute("styleName")!.Value == "My Custom Style",
            "the custom tblStyle child must round-trip, not be discarded by the always-regenerate stub");
    }

    [Fact]
    public void Writer_FreshlyAuthoredPresentation_StillEmitsDefaultTableStylesStub()
    {
        // Sibling/no-regression: a presentation with no PackageSnapshot (freshly created in
        // memory, never read from a package) must still get the default stub -- the preservation
        // path must not somehow prevent normal decks from getting a valid tableStyles.xml.
        var pres = new PresentationModel();
        pres.Slides.Add(new Slide());
        pres.PackageSnapshot.Should().BeNull("a freshly-authored presentation has no source package to preserve from");

        var bytes = WriteToBytes(pres);
        var tableStylesXml = ReadZipEntryXml(bytes, "ppt/tableStyles.xml");

        tableStylesXml.Root!.Name.Should().Be(A + "tblStyleLst");
        tableStylesXml.Root.Attribute("def")?.Value.Should().Be("{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (c) HIGH: a:lnSpc paragraph line spacing
    // ─────────────────────────────────────────────────────────────────────────

    private static SlideShape MakeTextShape(uint id, Paragraph paragraph)
    {
        var body = new TextBody();
        body.Paragraphs.Add(paragraph);
        return new SlideShape
        {
            Id = id,
            Name = "Text " + id,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            TextBody = body,
        };
    }

    [Fact]
    public void RoundTrip_LineSpacingPercent_PreservedAndWrittenAsSpcPct()
    {
        var para = new Paragraph { LineSpacingPercent = 150.0 };
        para.Runs.Add(new Run { Text = "1.5x spaced" });

        var pres = new PresentationModel();
        var slide = new Slide();
        slide.Shapes.Add(MakeTextShape(1, para));
        pres.Slides.Add(slide);

        var bytes = WriteToBytes(pres);
        var slideXml = ReadZipEntryXml(bytes, "ppt/slides/slide1.xml");
        var lnSpc = slideXml.Descendants(A + "lnSpc").SingleOrDefault();
        lnSpc.Should().NotBeNull("a:lnSpc must be written for a paragraph with LineSpacingPercent set");
        lnSpc!.Element(A + "spcPct")?.Attribute("val")?.Value.Should().Be("150000");

        using var ms = new MemoryStream(bytes);
        var reloaded = PptxPackageReader.Read(ms);
        var reloadedPara = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        reloadedPara.LineSpacingPercent.Should().Be(150.0);
        reloadedPara.LineSpacingPointsExact.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_LineSpacingExactPoints_PreservedAndWrittenAsSpcPts()
    {
        var para = new Paragraph { LineSpacingPointsExact = 24.0 };
        para.Runs.Add(new Run { Text = "24pt exact spaced" });

        var pres = new PresentationModel();
        var slide = new Slide();
        slide.Shapes.Add(MakeTextShape(1, para));
        pres.Slides.Add(slide);

        var bytes = WriteToBytes(pres);
        var slideXml = ReadZipEntryXml(bytes, "ppt/slides/slide1.xml");
        var lnSpc = slideXml.Descendants(A + "lnSpc").SingleOrDefault();
        lnSpc.Should().NotBeNull();
        lnSpc!.Element(A + "spcPts")?.Attribute("val")?.Value.Should().Be("2400");

        using var ms = new MemoryStream(bytes);
        var reloaded = PptxPackageReader.Read(ms);
        var reloadedPara = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        reloadedPara.LineSpacingPointsExact.Should().Be(24.0);
        reloadedPara.LineSpacingPercent.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_NoLineSpacing_LeavesFieldsNullAndNoLnSpcElement()
    {
        // Sibling/no-regression: a paragraph that never authored a:lnSpc must not gain one, and
        // TextLayoutPlanner's resolved scale for it must be the neutral 1.0 (single spacing).
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "default spaced" });

        var pres = new PresentationModel();
        var slide = new Slide();
        slide.Shapes.Add(MakeTextShape(1, para));
        pres.Slides.Add(slide);

        var bytes = WriteToBytes(pres);
        var slideXml = ReadZipEntryXml(bytes, "ppt/slides/slide1.xml");
        slideXml.Descendants(A + "lnSpc").Should().BeEmpty();

        using var ms = new MemoryStream(bytes);
        var reloaded = PptxPackageReader.Read(ms);
        var reloadedPara = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        reloadedPara.LineSpacingPercent.Should().BeNull();
        reloadedPara.LineSpacingPointsExact.Should().BeNull();

        var resolved = new ResolvedParagraph { LineSpacingPercent = reloadedPara.LineSpacingPercent, LineSpacingPointsExact = reloadedPara.LineSpacingPointsExact };
        TextLayoutPlanner.ResolveParagraphLineSpacingScale(resolved, naturalHeightDip: 20.0).Should().Be(1.0);
    }

    [Fact]
    public void TextLayoutPlanner_ResolveParagraphLineSpacingScale_HonoursPercentAndExactPoints()
    {
        var pct = new ResolvedParagraph { LineSpacingPercent = 200.0 };
        TextLayoutPlanner.ResolveParagraphLineSpacingScale(pct, naturalHeightDip: 20.0).Should().Be(2.0);

        // 24pt exact / 96dpi = 32 DIP; naturalHeightDip 16 -> scale 2.0
        var exact = new ResolvedParagraph { LineSpacingPointsExact = 24.0 };
        TextLayoutPlanner.ResolveParagraphLineSpacingScale(exact, naturalHeightDip: 16.0).Should().BeApproximately(2.0, 0.001);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (d) MED: a:spcBef / a:spcAft spcPct (percentage) variant
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_SpaceBeforeAfterPercent_PreservedAndWrittenAsSpcPct()
    {
        var para = new Paragraph { SpaceBeforePercent = 50.0, SpaceAfterPercent = 200.0 };
        para.Runs.Add(new Run { Text = "pct spaced" });

        var pres = new PresentationModel();
        var slide = new Slide();
        slide.Shapes.Add(MakeTextShape(1, para));
        pres.Slides.Add(slide);

        var bytes = WriteToBytes(pres);
        var slideXml = ReadZipEntryXml(bytes, "ppt/slides/slide1.xml");

        var spcBef = slideXml.Descendants(A + "spcBef").SingleOrDefault();
        var spcAft = slideXml.Descendants(A + "spcAft").SingleOrDefault();
        spcBef.Should().NotBeNull("spcBef must be written for SpaceBeforePercent");
        spcAft.Should().NotBeNull("spcAft must be written for SpaceAfterPercent");
        spcBef!.Element(A + "spcPct")?.Attribute("val")?.Value.Should().Be("50000");
        spcAft!.Element(A + "spcPct")?.Attribute("val")?.Value.Should().Be("200000");
        spcBef.Element(A + "spcPts").Should().BeNull("spcPts and spcPct are mutually exclusive per ECMA-376");
        spcAft.Element(A + "spcPts").Should().BeNull();

        using var ms = new MemoryStream(bytes);
        var reloaded = PptxPackageReader.Read(ms);
        var reloadedPara = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        reloadedPara.SpaceBeforePercent.Should().Be(50.0);
        reloadedPara.SpaceAfterPercent.Should().Be(200.0);
        reloadedPara.SpaceBeforePt.Should().BeNull();
        reloadedPara.SpaceAfterPt.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_SpaceBeforeAfterPoints_StillPreferredOverPercentWhenBothSet()
    {
        // Sibling/no-regression: the pre-existing spcPts (absolute points) behavior must still
        // work unaffected -- this must not have regressed while adding spcPct support.
        var para = new Paragraph { SpaceBeforePt = 12.0, SpaceAfterPt = 6.0 };
        para.Runs.Add(new Run { Text = "pt spaced" });

        var pres = new PresentationModel();
        var slide = new Slide();
        slide.Shapes.Add(MakeTextShape(1, para));
        pres.Slides.Add(slide);

        var bytes = WriteToBytes(pres);
        var slideXml = ReadZipEntryXml(bytes, "ppt/slides/slide1.xml");
        var spcBef = slideXml.Descendants(A + "spcBef").Single();
        var spcAft = slideXml.Descendants(A + "spcAft").Single();
        spcBef.Element(A + "spcPts")?.Attribute("val")?.Value.Should().Be("1200");
        spcAft.Element(A + "spcPts")?.Attribute("val")?.Value.Should().Be("600");
        spcBef.Element(A + "spcPct").Should().BeNull();

        using var ms = new MemoryStream(bytes);
        var reloaded = PptxPackageReader.Read(ms);
        var reloadedPara = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        reloadedPara.SpaceBeforePt.Should().Be(12.0);
        reloadedPara.SpaceAfterPt.Should().Be(6.0);
        reloadedPara.SpaceBeforePercent.Should().BeNull();
        reloadedPara.SpaceAfterPercent.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (e) HIGH: group a:chOff / a:chExt child coordinate space
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Reader_GroupWithDivergentChOffChExt_ParsesChildSpaceOntoGroupShape()
    {
        var pres = new PresentationModel();
        var slide = new Slide();
        pres.Slides.Add(slide);
        var bytes = WriteToBytes(pres);

        // Splice a hand-authored group shape (off/ext != chOff/chExt, i.e. resized-after-creation)
        // directly into slide1.xml -- this is the shape of file an external authoring tool (or
        // PowerPoint itself, after a group resize) legitimately produces.
        byte[] patched;
        using (var msIn = new MemoryStream(bytes))
        using (var msOut = new MemoryStream())
        {
            using (var srcZip = new ZipArchive(msIn, ZipArchiveMode.Read))
            using (var dstZip = new ZipArchive(msOut, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in srcZip.Entries)
                {
                    var dstEntry = dstZip.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                    using var src = entry.Open();
                    using var dst = dstEntry.Open();
                    if (entry.FullName == "ppt/slides/slide1.xml")
                    {
                        var xml = XDocument.Load(src);
                        var spTree = xml.Descendants(P + "spTree").Single();
                        spTree.Add(new XElement(P + "grpSp",
                            new XElement(P + "nvGrpSpPr",
                                new XElement(P + "cNvPr", new XAttribute("id", "99"), new XAttribute("name", "TestGroup")),
                                new XElement(P + "cNvGrpSpPr"),
                                new XElement(P + "nvPr")),
                            new XElement(P + "grpSpPr",
                                new XElement(A + "xfrm",
                                    new XElement(A + "off", new XAttribute("x", "100000"), new XAttribute("y", "100000")),
                                    new XElement(A + "ext", new XAttribute("cx", "2000000"), new XAttribute("cy", "1000000")),
                                    new XElement(A + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                    new XElement(A + "chExt", new XAttribute("cx", "1000000"), new XAttribute("cy", "500000"))))));
                        xml.Save(dst, SaveOptions.DisableFormatting);
                    }
                    else
                    {
                        src.CopyTo(dst);
                    }
                }
            }
            patched = msOut.ToArray();
        }

        using var readStream = new MemoryStream(patched);
        var reloaded = PptxPackageReader.Read(readStream);
        var group = reloaded.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Group);

        group.OffsetXEmu.Should().Be(100000);
        group.OffsetYEmu.Should().Be(100000);
        group.ExtentCxEmu.Should().Be(2000000);
        group.ExtentCyEmu.Should().Be(1000000);
        group.ChildOffsetXEmu.Should().Be(0);
        group.ChildOffsetYEmu.Should().Be(0);
        group.ChildExtentCxEmu.Should().Be(1000000);
        group.ChildExtentCyEmu.Should().Be(500000);
    }

    [Fact]
    public void Compose_GroupWithDivergentChOffChExt_TransformsChildrenIntoAbsoluteSlideSpace()
    {
        // Group box: off=(100000,100000) ext=(2000000,1000000).
        // Child space: chOff=(0,0) chExt=(1000000,500000) -- half the size of the group's box,
        // i.e. scale factor 2x in both dimensions (the group was resized 2x after authoring).
        // Child (in child space): off=(200000,100000) ext=(300000,200000).
        // Expected absolute: off = groupOff + (childOff - chOff) * scale
        //                       = (100000 + 200000*2, 100000 + 100000*2) = (500000, 300000)
        //                    ext = childExt * scale = (600000, 400000)
        var group = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 100000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1000000,
            ChildOffsetXEmu = 0,
            ChildOffsetYEmu = 0,
            ChildExtentCxEmu = 1000000,
            ChildExtentCyEmu = 500000,
        };
        group.Children.Add(new SlideShape
        {
            Id = 2,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 200000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 300000,
            ExtentCyEmu = 200000,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33))),
        });

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(group);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var childOp = ops.OfType<DrawOp.Shape>().Single(o => o.ShapeId == 2);

        double expectedXDip = SlideTransformCore.EmuToDip(500000);
        double expectedYDip = SlideTransformCore.EmuToDip(300000);
        double expectedCxDip = SlideTransformCore.EmuToDip(600000);
        double expectedCyDip = SlideTransformCore.EmuToDip(400000);

        childOp.BoundsDip.X.Should().BeApproximately(expectedXDip, 0.01,
            "R133: group children authored in a resized child-space must be mapped into the group's absolute slide space");
        childOp.BoundsDip.Y.Should().BeApproximately(expectedYDip, 0.01);
        childOp.BoundsDip.Width.Should().BeApproximately(expectedCxDip, 0.01);
        childOp.BoundsDip.Height.Should().BeApproximately(expectedCyDip, 0.01);
    }

    [Fact]
    public void Compose_GroupWithIdentityChOffChExt_LeavesChildrenAtAuthoredAbsoluteCoords()
    {
        // Sibling/no-regression: the overwhelmingly common case (chOff==off, chExt==ext, i.e.
        // never resized, or absent entirely -- the only shape this app itself ever wrote before
        // this fix) must render children at exactly their authored coordinates, unchanged.
        var group = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 100000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1000000,
            // No ChildOffset/ChildExtent set -- mirrors both "absent in source" and "identity".
        };
        group.Children.Add(new SlideShape
        {
            Id = 2,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 300000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 400000,
            ExtentCyEmu = 300000,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33))),
        });

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(group);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var childOp = ops.OfType<DrawOp.Shape>().Single(o => o.ShapeId == 2);

        childOp.BoundsDip.X.Should().BeApproximately(SlideTransformCore.EmuToDip(300000), 0.01);
        childOp.BoundsDip.Y.Should().BeApproximately(SlideTransformCore.EmuToDip(200000), 0.01);
        childOp.BoundsDip.Width.Should().BeApproximately(SlideTransformCore.EmuToDip(400000), 0.01);
        childOp.BoundsDip.Height.Should().BeApproximately(SlideTransformCore.EmuToDip(300000), 0.01);
    }

    [Fact]
    public void RoundTrip_GroupChildSpace_WrittenVerbatimWhenDivergent()
    {
        // The writer must round-trip a preserved divergent child space verbatim rather than
        // forcing chOff==off/chExt==ext identity (which would silently discard the resize).
        var pres = new PresentationModel();
        var slide = new Slide();
        var group = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 100000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1000000,
            ChildOffsetXEmu = 0,
            ChildOffsetYEmu = 0,
            ChildExtentCxEmu = 1000000,
            ChildExtentCyEmu = 500000,
        };
        group.Children.Add(new SlideShape
        {
            Id = 2,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 200000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 300000,
            ExtentCyEmu = 200000,
        });
        slide.Shapes.Add(group);
        pres.Slides.Add(slide);

        var bytes = WriteToBytes(pres);
        var slideXml = ReadZipEntryXml(bytes, "ppt/slides/slide1.xml");
        // p:spTree itself carries its own p:grpSpPr (the slide root transform), so scope to the
        // nested p:grpSp we authored rather than taking the sole p:grpSpPr descendant.
        var grpXfrm = slideXml.Descendants(P + "grpSp").Single()
            .Element(P + "grpSpPr")!.Element(A + "xfrm")!;

        grpXfrm.Element(A + "chOff")!.Attribute("x")!.Value.Should().Be("0");
        grpXfrm.Element(A + "chOff")!.Attribute("y")!.Value.Should().Be("0");
        grpXfrm.Element(A + "chExt")!.Attribute("cx")!.Value.Should().Be("1000000");
        grpXfrm.Element(A + "chExt")!.Attribute("cy")!.Value.Should().Be("500000");

        using var ms = new MemoryStream(bytes);
        var reloaded = PptxPackageReader.Read(ms);
        var reloadedGroup = reloaded.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Group);
        reloadedGroup.ChildOffsetXEmu.Should().Be(0);
        reloadedGroup.ChildExtentCxEmu.Should().Be(1000000);
        reloadedGroup.ChildExtentCyEmu.Should().Be(500000);
    }
}
