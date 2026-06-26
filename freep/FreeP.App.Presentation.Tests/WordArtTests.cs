using FreeP.App.Compositor;
using FreeP.Core.IO;
using System.IO;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Wave 16A — WordArt / text-effects tests.
/// Covers: model round-trip (IO), compositor resolution (DrawOps), and warp preset smoke test.
/// </summary>
public sealed class WordArtTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.WordArtTests", Guid.NewGuid().ToString("N"));

    public WordArtTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private Presentation BuildPres(Action<Slide> config)
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        config(pres.Slides[0]);
        return pres;
    }

    private static SlideShape TextShape(Action<TextBody> tbConfig)
    {
        var tb = new TextBody();
        var para = new TextParagraph();
        var run = new Run { Text = "Hello" };
        para.Runs.Add(run);
        tb.Paragraphs.Add(para);
        tbConfig(tb);

        return new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
            TextBody = tb
        };
    }

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    // ─── model round-trip ────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_TextShadow_PreservedThroughWriteRead()
    {
        var shadow = new RunTextShadow
        {
            Color    = new ThemeAwareColor(new SrgbColor(0x20, 0x20, 0x20)),
            Alpha    = 180,
            BlurPt   = 3.0,
            DistPt   = 2.5,
            DirDeg   = 45.0
        };

        var pres = BuildPres(slide =>
        {
            var shape = TextShape(tb =>
            {
                tb.Paragraphs[0].Runs[0].TextShadow = shadow;
            });
            slide.Shapes.Add(shape);
        });

        var reloaded = PptxPackageReader.Read(WriteToPptx(pres));
        var run = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0];

        run.TextShadow.Should().NotBeNull("shadow was set");
        run.TextShadow!.Alpha.Should().Be(180);
        run.TextShadow.BlurPt.Should().BeApproximately(3.0, 0.5, "EMU round-trip tolerance");
        run.TextShadow.DistPt.Should().BeApproximately(2.5, 0.5);
        run.TextShadow.DirDeg.Should().BeApproximately(45.0, 1.0);
    }

    [Fact]
    public void RoundTrip_TextOutline_PreservedThroughWriteRead()
    {
        var outline = new ShapeOutline.Visible(
            new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
            widthPt: 1.5,
            dash: OutlineDash.Solid);

        var pres = BuildPres(slide =>
        {
            var shape = TextShape(tb =>
            {
                tb.Paragraphs[0].Runs[0].TextOutline = outline;
            });
            slide.Shapes.Add(shape);
        });

        var reloaded = PptxPackageReader.Read(WriteToPptx(pres));
        var run = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0];

        run.TextOutline.Should().NotBeNull("outline was set");
        run.TextOutline.Should().BeOfType<ShapeOutline.Visible>();
        var vis = (ShapeOutline.Visible)run.TextOutline!;
        vis.Color.Resolved.R.Should().Be(0x00);
        vis.Color.Resolved.B.Should().Be(0xFF);
    }

    [Fact]
    public void RoundTrip_TextFill_Gradient_PreservedThroughWriteRead()
    {
        var fill = new ShapeFill.Gradient(
            new ThemeAwareColor(new SrgbColor(0xFF, 0x66, 0x00)),
            new ThemeAwareColor(new SrgbColor(0xCC, 0x00, 0x00)),
            angleDegrees: 90.0);

        var pres = BuildPres(slide =>
        {
            var shape = TextShape(tb =>
            {
                tb.Paragraphs[0].Runs[0].TextFill = fill;
            });
            slide.Shapes.Add(shape);
        });

        var reloaded = PptxPackageReader.Read(WriteToPptx(pres));
        var run = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0];

        run.TextFill.Should().NotBeNull("text fill was set");
        run.TextFill.Should().BeOfType<ShapeFill.Gradient>("gradient fill survives round-trip");
        var grad = (ShapeFill.Gradient)run.TextFill!;
        grad.AngleDegrees.Should().BeApproximately(90.0, 1.0, "angle preserved");
    }

    [Fact]
    public void RoundTrip_WarpPreset_PreservedThroughWriteRead()
    {
        var pres = BuildPres(slide =>
        {
            var shape = TextShape(tb => { tb.WarpPreset = "textArchUp"; });
            slide.Shapes.Add(shape);
        });

        var reloaded = PptxPackageReader.Read(WriteToPptx(pres));
        var tb = reloaded.Slides[0].Shapes[0].TextBody!;

        tb.WarpPreset.Should().Be("textArchUp");
    }

    // ─── compositor resolution ───────────────────────────────────────────────

    private static Presentation MakePres() => Presentation.CreateEmpty();

    [Fact]
    public void Compositor_TextShadow_ResolvedOntoResolvedRun()
    {
        var shadow = new RunTextShadow
        {
            Color  = new ThemeAwareColor(new SrgbColor(0x10, 0x20, 0x30)),
            Alpha  = 200,
            BlurPt = 2.0,
            DistPt = 3.0,
            DirDeg = 135.0
        };

        var p = MakePres();
        p.Slides[0].Shapes.Clear();

        var tb = new TextBody();
        var run = new Run { Text = "Shadow", TextShadow = shadow };
        tb.Paragraphs.Add(new TextParagraph { Runs = { run } });

        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 3000000, ExtentCyEmu = 1000000,
            TextBody = tb
        });

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var textOp = ops.OfType<DrawOp.Shape>()
                        .Single(s => s.TextLayout is not null);

        var resolvedRun = textOp.TextLayout!.Paragraphs[0].Runs[0];
        resolvedRun.TextShadow.Should().NotBeNull("shadow must be resolved");
        resolvedRun.TextShadow!.Alpha.Should().Be(200);
        resolvedRun.TextShadow.Color.R.Should().Be(0x10);
        resolvedRun.TextShadow.DirDeg.Should().Be(135.0);
        // Pt → DIP: 3pt * 96/72 ≈ 4 DIP
        resolvedRun.TextShadow.DistDip.Should().BeApproximately(3.0 * 96.0 / 72.0, 0.1);
    }

    [Fact]
    public void Compositor_TextFill_Gradient_ResolvedToResolvedFillGradient()
    {
        var fill = new ShapeFill.Gradient(
            new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
            new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
            angleDegrees: 45.0);

        var p = MakePres();
        p.Slides[0].Shapes.Clear();

        var tb = new TextBody();
        var run = new Run { Text = "GradFill", TextFill = fill };
        tb.Paragraphs.Add(new TextParagraph { Runs = { run } });

        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 3000000, ExtentCyEmu = 1000000,
            TextBody = tb
        });

        var ops  = SlideCompositor.Compose(p, p.Slides[0]);
        var textOp = ops.OfType<DrawOp.Shape>().Single(s => s.TextLayout is not null);
        var resolvedRun = textOp.TextLayout!.Paragraphs[0].Runs[0];

        resolvedRun.TextFill.Should().BeOfType<ResolvedFill.Gradient>();
        var grad = (ResolvedFill.Gradient)resolvedRun.TextFill!;
        grad.AngleDegrees.Should().Be(45.0);
        grad.StartColor.R.Should().Be(0xFF);
        grad.EndColor.B.Should().Be(0xFF);
    }

    [Fact]
    public void Compositor_WarpPreset_PropagatedToResolvedTextLayout()
    {
        var p = MakePres();
        p.Slides[0].Shapes.Clear();

        var tb = new TextBody { WarpPreset = "textWave1" };
        tb.Paragraphs.Add(new TextParagraph { Runs = { new Run { Text = "Warp" } } });

        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 3000000, ExtentCyEmu = 1000000,
            TextBody = tb
        });

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var textOp = ops.OfType<DrawOp.Shape>().Single(s => s.TextLayout is not null);

        textOp.TextLayout!.WarpPreset.Should().Be("textWave1");
    }

    // ─── SlideCloner ─────────────────────────────────────────────────────────

    [Fact]
    public void SlideCloner_CopiesTextEffects_DeepCopy()
    {
        var shadow = new RunTextShadow
        {
            Color = new ThemeAwareColor(new SrgbColor(0x40, 0x40, 0x40)),
            Alpha = 128, BlurPt = 4.0, DistPt = 3.0, DirDeg = 45.0
        };
        var fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)));

        var tb = new TextBody { WarpPreset = "textCircle" };
        var run = new Run { Text = "Clone", TextFill = fill, TextShadow = shadow };
        tb.Paragraphs.Add(new TextParagraph { Runs = { run } });

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 3000000, ExtentCyEmu = 1000000,
            TextBody = tb
        };

        var src = new Slide();
        src.Shapes.Add(shape);

        var dst = SlideCloner.Clone(src);

        var clonedShape = dst.Shapes[0];
        var clonedTb    = clonedShape.TextBody!;
        var clonedRun   = clonedTb.Paragraphs[0].Runs[0];

        clonedTb.WarpPreset.Should().Be("textCircle");
        clonedRun.TextFill.Should().NotBeNull();
        clonedRun.TextShadow.Should().NotBeNull();
        clonedRun.TextShadow!.BlurPt.Should().Be(4.0);

        // Verify deep copy — mutating source must not affect clone
        run.TextShadow.BlurPt = 99.0;
        clonedRun.TextShadow.BlurPt.Should().Be(4.0, "deep copy must be independent");
    }
}
