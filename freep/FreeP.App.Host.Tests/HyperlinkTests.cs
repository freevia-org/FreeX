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
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.HyperlinkTests", Guid.NewGuid().ToString("N"));

    public HyperlinkTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

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
    public void OpenExternalUrl_FileScheme_DoesNotThrow()
    {
        // file:// is rejected silently (no Process.Start call), so no exception.
        var ex = Record.Exception(() => SlideShowWindow.OpenExternalUrl("file:///C:/secret"));
        ex.Should().BeNull("scheme guard must silently ignore file:// without throwing");
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
}
