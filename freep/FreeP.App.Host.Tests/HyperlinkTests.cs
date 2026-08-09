using System.IO;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 11A unit tests:
///  - I/O round-trip for external and internal hyperlinks (shape-level and run-level)
///  - SlideCloner copies hyperlinks
///  - SetShapeHyperlinkCommand Apply / Revert
///  - SlideShowWindow: scheme guard, HitTestHyperlink, OpenExternalUrl
/// </summary>
public sealed class HyperlinkTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.HyperlinkTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private string WriteToPptx(Presentation p)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(p, path);
        return path;
    }

    private static SlideShape MakeShape(uint id = 1, string name = "Shape1") => new()
    {
        Id          = id,
        Name        = name,
        Kind        = SlideShapeKind.AutoShape,
        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
        OffsetXEmu  = 914400,
        OffsetYEmu  = 914400,
        ExtentCxEmu = 2000000,
        ExtentCyEmu = 1000000,
    };

    private static Presentation TwoSlidePresentation()
    {
        var p = new Presentation();
        var s1 = new Slide(); s1.Id = "rId2"; p.Slides.Add(s1);
        var s2 = new Slide(); s2.Id = "rId3"; p.Slides.Add(s2);
        return p;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. External hyperlink round-trip (shape-level)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ShapeExternalHyperlink_PreservesUrlAndTooltip()
    {
        var p    = new Presentation();
        var slide = new Slide();
        var shape = MakeShape();
        shape.Hyperlink = new Hyperlink
        {
            Url     = "https://example.com",
            Tooltip = "Go here"
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var path     = WriteToPptx(p);
        var reloaded = PptxPackageReader.Read(path);

        var rtShape = reloaded.Slides[0].Shapes.First(s => s.Name == "Shape1");
        rtShape.Hyperlink.Should().NotBeNull("external hyperlink must survive round-trip");
        rtShape.Hyperlink!.Url.Should().Be("https://example.com");
        rtShape.Hyperlink.Tooltip.Should().Be("Go here");
        rtShape.Hyperlink.IsExternal.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. External hyperlink round-trip (run-level)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_RunExternalHyperlink_PreservesUrl()
    {
        var p    = new Presentation();
        var slide = new Slide();
        var shape = MakeShape();
        shape.TextBody = new TextBody();
        var para = new Paragraph();
        var run  = new Run { Text = "Click me" };
        run.Hyperlink = new Hyperlink { Url = "https://runlink.example.com" };
        para.Runs.Add(run);
        shape.TextBody.Paragraphs.Add(para);
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var path     = WriteToPptx(p);
        var reloaded = PptxPackageReader.Read(path);

        var rtRun = reloaded.Slides[0].Shapes.First(s => s.Name == "Shape1")
                            .TextBody!.Paragraphs[0].Runs[0];
        rtRun.Hyperlink.Should().NotBeNull("run-level hyperlink must survive round-trip");
        rtRun.Hyperlink!.Url.Should().Be("https://runlink.example.com");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. Internal hyperlink round-trip (shape-level)
    //    This requires a 2-slide presentation so the slide lookup works.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ShapeInternalHyperlink_TargetSlideIndexPreserved()
    {
        var p   = TwoSlidePresentation();
        var s1  = p.Slides[0];
        var shape = MakeShape();
        // Point to slide 2 by Id.
        shape.Hyperlink = new Hyperlink { TargetSlideId = p.Slides[1].Id };
        s1.Shapes.Add(shape);

        var path     = WriteToPptx(p);
        var reloaded = PptxPackageReader.Read(path);

        var rtShape = reloaded.Slides[0].Shapes.First(s => s.Name == "Shape1");
        rtShape.Hyperlink.Should().NotBeNull("internal hyperlink must survive round-trip");
        rtShape.Hyperlink!.IsExternal.Should().BeFalse();
        // TargetSlideId should point to the second slide in the reloaded presentation.
        var targetId = rtShape.Hyperlink.TargetSlideId;
        targetId.Should().NotBeNull();
        reloaded.Slides.Should().Contain(s => s.Id == targetId, "TargetSlideId must map to a real slide");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4. SlideCloner copies hyperlinks
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SlideCloner_CopiesShapeHyperlink()
    {
        var slide = new Slide();
        var shape = MakeShape();
        shape.Hyperlink = new Hyperlink { Url = "https://example.com", Tooltip = "tip" };
        slide.Shapes.Add(shape);

        var cloned = SlideCloner.CloneSlide(slide);

        var clonedShape = cloned.Shapes.First(s => s.Name == "Shape1");
        clonedShape.Hyperlink.Should().NotBeNull();
        clonedShape.Hyperlink!.Url.Should().Be("https://example.com");
        clonedShape.Hyperlink.Tooltip.Should().Be("tip");
        clonedShape.Hyperlink.Should().NotBeSameAs(shape.Hyperlink, "clone must be a new object");
    }

    [Fact]
    public void SlideCloner_CopiesRunHyperlink()
    {
        var slide = new Slide();
        var shape = MakeShape();
        shape.TextBody = new TextBody();
        var para = new Paragraph();
        var run  = new Run { Text = "x" };
        run.Hyperlink = new Hyperlink { Url = "mailto:a@b.com" };
        para.Runs.Add(run);
        shape.TextBody.Paragraphs.Add(para);
        slide.Shapes.Add(shape);

        var cloned = SlideCloner.CloneSlide(slide);

        var clonedRun = cloned.Shapes[0].TextBody!.Paragraphs[0].Runs[0];
        clonedRun.Hyperlink.Should().NotBeNull();
        clonedRun.Hyperlink!.Url.Should().Be("mailto:a@b.com");
        clonedRun.Hyperlink.Should().NotBeSameAs(run.Hyperlink, "clone must be a new object");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 5. SetShapeHyperlinkCommand Apply / Revert
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SetShapeHyperlinkCommand_ApplyAndRevert_IsUndoable()
    {
        var p     = new Presentation();
        var slide = new Slide();
        var shape = MakeShape();
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var newLink = new Hyperlink { Url = "https://example.com" };
        var cmd     = new SetShapeHyperlinkCommand(slideIndex: 0, shapeId: 1, link: newLink);

        cmd.Apply(p);
        shape.Hyperlink.Should().BeSameAs(newLink, "Apply must set the hyperlink");

        cmd.Revert(p);
        shape.Hyperlink.Should().BeNull("Revert must restore null (the previous state)");
    }

    [Fact]
    public void SetShapeHyperlinkCommand_Revert_RestoresPreviousLink()
    {
        var p      = new Presentation();
        var slide  = new Slide();
        var shape  = MakeShape();
        var prev   = new Hyperlink { Url = "https://prev.example.com" };
        shape.Hyperlink = prev;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var newLink = new Hyperlink { Url = "https://new.example.com" };
        var cmd     = new SetShapeHyperlinkCommand(slideIndex: 0, shapeId: 1, link: newLink);

        cmd.Apply(p);
        shape.Hyperlink.Should().BeSameAs(newLink);

        cmd.Revert(p);
        shape.Hyperlink.Should().BeSameAs(prev, "Revert must restore the original hyperlink");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 6. Scheme guard: SlideShowWindow.OpenExternalUrl
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OpenExternalUrl_RoutesThroughSharedLauncher()
    {
        var source = ReadHostSource("SlideShowWindow.cs");

        source.Should().Contain("ExternalUriLauncher.Open(");
        source.Should().NotContain("new Uri(url");
        source.Should().NotContain("uri.Scheme is not");
    }

    [Fact]
    public void OpenExternalUrl_FileScheme_DoesNotThrow()
    {
        // The launcher may be unavailable in a test process, but a local-file link must not throw.
        var ex = Record.Exception(() => SlideShowWindow.OpenExternalUrl("file:///C:/secret"));
        ex.Should().BeNull("external-link activation must swallow launch failures");
    }

    [Fact]
    public void OpenExternalUrl_InvalidUrl_DoesNotThrow()
    {
        var ex = Record.Exception(() => SlideShowWindow.OpenExternalUrl("not a url %%"));
        ex.Should().BeNull("invalid URL must be swallowed, not thrown");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 7. SlideShowWindow.HitTestHyperlink
    // ─────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void HitTestHyperlink_ClickInsideHyperlinkedShape_ReturnsHyperlink()
    {
        // Shape occupies (100 dip, 100 dip) → (200 dip, 200 dip)
        // in slide coords (EMU converted: 1 DIP = 9525 EMU).
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id          = 1,
            Name        = "Linked",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = (long)(100 * 9525.0),
            OffsetYEmu  = (long)(100 * 9525.0),
            ExtentCxEmu = (long)(100 * 9525.0),
            ExtentCyEmu = (long)(100 * 9525.0),
        };
        var hlink = new Hyperlink { Url = "https://test.example.com" };
        shape.Hyperlink = hlink;
        slide.Shapes.Add(shape);

        var p    = new Presentation();
        p.Slides.Add(slide);

        // Presentation slide dimensions: standard 12192000 x 6858000 EMU → 1280 x 720 DIP.
        // Canvas matches slide 1:1 (no scaling needed for this test).
        p.SlideSizeCxEmu = 12_192_000;
        p.SlideSizeCyEmu = 6_858_000;

        var win = new SlideShowWindow(p);

        // Click at (150, 150) in canvas coords: inside the shape (100..200, 100..200).
        var result = win.HitTestHyperlink(slide, canvasX: 150, canvasY: 150);
        result.Should().BeSameAs(hlink, "a click inside the hyperlinked shape should return its hyperlink");
    }

    [StaFact]
    public void HitTestHyperlink_ClickOutsideAllShapes_ReturnsNull()
    {
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id          = 1,
            Name        = "Linked",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = (long)(100 * 9525.0),
            OffsetYEmu  = (long)(100 * 9525.0),
            ExtentCxEmu = (long)(100 * 9525.0),
            ExtentCyEmu = (long)(100 * 9525.0),
            Hyperlink   = new Hyperlink { Url = "https://test.example.com" }
        };
        slide.Shapes.Add(shape);

        var p = new Presentation();
        p.SlideSizeCxEmu = 12_192_000;
        p.SlideSizeCyEmu = 6_858_000;
        p.Slides.Add(slide);

        var win = new SlideShowWindow(p);

        // Click at (50, 50) — outside the shape's bounds (100..200, 100..200).
        var result = win.HitTestHyperlink(slide, canvasX: 50, canvasY: 50);
        result.Should().BeNull("a click outside all shapes should return null");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // BB1: internal slide-jump resolves by part path, not filename digit
    //      Construct a raw PPTX zip where slideN.xml filenames don't match
    //      presentation order: sldIdLst lists slide3.xml first (rId2), slide1.xml second (rId3).
    //      A hyperlink on slide 1 (slide3.xml) targets slide 2 (slide1.xml).
    //      The correct TargetSlideId must be the rId of slide1.xml (rId3), NOT allSlides[0].Id.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InternalHyperlink_ReorderedDeck_ResolvesTargetByPartPath_NotFilenameDigit()
    {
        // Build a minimal .pptx in a MemoryStream where the slide part filename order
        // differs from the presentation sldIdLst order.
        //
        // Presentation order: sldIdLst = [ rId2 → slide3.xml, rId3 → slide1.xml ]
        //   allSlides[0] = rId2 (slide3.xml)  — the "first" slide in deck order
        //   allSlides[1] = rId3 (slide1.xml)  — the "second" slide in deck order
        //
        // Hyperlink on slide3.xml (allSlides[0]): r:id="rH1" → rel target="../slides/slide1.xml"
        //   Correct resolution: TargetSlideId = "rId3" (the rId of slide1.xml in the presentation)
        //   Buggy resolution:   TargetSlideId = allSlides[1-1].Id = allSlides[0].Id = "rId2" (wrong!)
        //     (the bug reads "1" from "slide1.xml" and does allSlides[1-1])

        using var ms = BuildReorderedPptx();
        var pres = PptxPackageReader.Read(ms);

        pres.Slides.Should().HaveCount(2, "two slides in the deck");

        // First slide in presentation order came from part slide3.xml (rId2).
        var firstSlide = pres.Slides[0];
        firstSlide.Id.Should().Be("rId2", "first in sldIdLst is rId2 → slide3.xml");

        // Second slide in presentation order came from part slide1.xml (rId3).
        var secondSlide = pres.Slides[1];
        secondSlide.Id.Should().Be("rId3", "second in sldIdLst is rId3 → slide1.xml");

        // The hyperlink on the first slide targets slide1.xml → should resolve to rId3 (second slide).
        var hlink = firstSlide.Shapes.FirstOrDefault()?.Hyperlink;
        hlink.Should().NotBeNull("the hyperlink on the first slide must be preserved");
        hlink!.IsExternal.Should().BeFalse("this is an internal slide-jump link");
        hlink.TargetSlideId.Should().Be(secondSlide.Id,
            "hyperlink target must resolve to the slide whose PART is slide1.xml (rId3), " +
            "not to the slide at filename-digit index 1 (rId2)");
    }

    /// <summary>
    /// Builds a minimal well-formed .pptx zip in memory where:
    ///   Presentation order: slide3.xml (rId2) first, slide1.xml (rId3) second.
    ///   Hyperlink on slide3.xml (first slide) targets slide1.xml (second slide) via rH1.
    /// </summary>
    private static MemoryStream BuildReorderedPptx()
    {
        var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            // _rels/.rels
            WriteEntry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>
                </Relationships>
                """);

            // ppt/_rels/presentation.xml.rels
            // rId2 → slide3.xml (first in deck), rId3 → slide1.xml (second in deck)
            WriteEntry(zip, "ppt/_rels/presentation.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide3.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>
                </Relationships>
                """);

            // ppt/presentation.xml — sldIdLst: id 256 → rId2, id 257 → rId3
            WriteEntry(zip, "ppt/presentation.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <p:sldSz cx="9144000" cy="6858000"/>
                  <p:sldIdLst>
                    <p:sldId id="256" r:id="rId2"/>
                    <p:sldId id="257" r:id="rId3"/>
                  </p:sldIdLst>
                </p:presentation>
                """);

            // slide3.xml — first slide (deck order 0); has an internal hyperlink targeting slide1.xml
            WriteEntry(zip, "ppt/slides/_rels/slide3.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rH1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="../slides/slide1.xml"/>
                </Relationships>
                """);

            WriteEntry(zip, "ppt/slides/slide3.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                       xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <p:cSld>
                    <p:spTree>
                      <p:sp>
                        <p:nvSpPr>
                          <p:cNvPr id="2" name="LinkedShape">
                            <a:hlinkClick r:id="rH1" action="ppaction://hlinksldjump"/>
                          </p:cNvPr>
                          <p:cNvSpPr/><p:nvPr/>
                        </p:nvSpPr>
                        <p:spPr>
                          <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
                          <a:prstGeom prst="rect"/>
                        </p:spPr>
                      </p:sp>
                    </p:spTree>
                  </p:cSld>
                </p:sld>
                """);

            // slide1.xml — second slide (deck order 1); the hyperlink target
            WriteEntry(zip, "ppt/slides/_rels/slide1.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                </Relationships>
                """);

            WriteEntry(zip, "ppt/slides/slide1.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                       xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:cSld><p:spTree/></p:cSld>
                </p:sld>
                """);

            // [Content_Types].xml — minimal
            WriteEntry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
                  <Override PartName="/ppt/slides/slide3.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
                  <Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
                </Types>
                """);
        }

        ms.Position = 0;
        return ms;
    }

    private static void WriteEntry(System.IO.Compression.ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new System.IO.StreamWriter(entry.Open(), System.Text.Encoding.UTF8);
        writer.Write(content.Trim());
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // BB2: grouped-shape hyperlink is hit-tested
    // ─────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void HitTestHyperlink_GroupedShapeWithHyperlink_IsFound()
    {
        // Group occupies (50..250 dip, 50..250 dip) — matched by HitTestShape on the group.
        // Child inside group at (100..200 dip, 100..200 dip) — matched on child hit-test.
        var hlink = new Hyperlink { Url = "https://grouped.example.com" };

        var child = new SlideShape
        {
            Id          = 2,
            Name        = "GroupedChild",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = (long)(100 * 9525.0),
            OffsetYEmu  = (long)(100 * 9525.0),
            ExtentCxEmu = (long)(100 * 9525.0),
            ExtentCyEmu = (long)(100 * 9525.0),
            Hyperlink   = hlink,
        };

        var group = new SlideShape
        {
            Id          = 1,
            Name        = "Group1",
            Kind        = SlideShapeKind.Group,
            OffsetXEmu  = (long)(50 * 9525.0),
            OffsetYEmu  = (long)(50 * 9525.0),
            ExtentCxEmu = (long)(200 * 9525.0),
            ExtentCyEmu = (long)(200 * 9525.0),
        };
        group.Children.Add(child);

        var slide = new Slide();
        slide.Shapes.Add(group);

        var p = new Presentation { SlideSizeCxEmu = 12_192_000, SlideSizeCyEmu = 6_858_000 };
        p.Slides.Add(slide);

        var win = new SlideShowWindow(p);

        // Click at (150, 150) dip — inside both the group bounds and the child bounds.
        var result = win.HitTestHyperlink(slide, canvasX: 150, canvasY: 150);
        result.Should().BeSameAs(hlink,
            "clicking inside a grouped shape's bounds should return its hyperlink (BB2 fix)");
    }

    [StaFact]
    public void HitTestHyperlink_GroupedShapeOutsideChildBounds_ReturnsNull()
    {
        // Group occupies (50..250 dip). Child is at (200..300 dip) — outside the test point.
        var hlink = new Hyperlink { Url = "https://grouped.example.com" };

        var child = new SlideShape
        {
            Id          = 2,
            Name        = "GroupedChild",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = (long)(200 * 9525.0),
            OffsetYEmu  = (long)(200 * 9525.0),
            ExtentCxEmu = (long)(100 * 9525.0),
            ExtentCyEmu = (long)(100 * 9525.0),
            Hyperlink   = hlink,
        };

        var group = new SlideShape
        {
            Id          = 1,
            Name        = "Group1",
            Kind        = SlideShapeKind.Group,
            OffsetXEmu  = (long)(50 * 9525.0),
            OffsetYEmu  = (long)(50 * 9525.0),
            ExtentCxEmu = (long)(300 * 9525.0),
            ExtentCyEmu = (long)(300 * 9525.0),
        };
        group.Children.Add(child);

        var slide = new Slide();
        slide.Shapes.Add(group);

        var p = new Presentation { SlideSizeCxEmu = 12_192_000, SlideSizeCyEmu = 6_858_000 };
        p.Slides.Add(slide);

        var win = new SlideShowWindow(p);

        // Click at (100, 100) dip — inside the group, but NOT inside the child (200..300).
        var result = win.HitTestHyperlink(slide, canvasX: 100, canvasY: 100);
        result.Should().BeNull("click is inside group but not inside the child with the hyperlink");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"), "freep", "FreeP.App.Host", fileName);
        return File.ReadAllText(path);
    }

}
