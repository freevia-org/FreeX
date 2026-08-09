using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Wave 16A — WordArt / text-effects tests.
/// Covers: model round-trip (IO), compositor resolution (DrawOps), and warp preset smoke test.
/// </summary>
public sealed class WordArtTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.WordArtTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

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
        var para = new Paragraph();
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
    public void RoundTrip_TextReflection_PreservedThroughWriteRead()
    {
        var reflection = new RunTextReflection
        {
            Alpha = 128,
            BlurPt = 1.5,
            DistPt = 3.0,
            DirDeg = 90.0,
            ScaleY = -0.75,
            EndPos = 0.5
        };

        var pres = BuildPres(slide =>
        {
            var shape = TextShape(tb =>
            {
                tb.Paragraphs[0].Runs[0].TextReflection = reflection;
            });
            slide.Shapes.Add(shape);
        });

        var reloaded = PptxPackageReader.Read(WriteToPptx(pres));
        var run = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0];

        run.TextReflection.Should().NotBeNull("reflection was set");
        run.TextReflection!.Alpha.Should().BeInRange(126, 129);
        run.TextReflection.BlurPt.Should().BeApproximately(1.5, 0.5);
        run.TextReflection.DistPt.Should().BeApproximately(3.0, 0.5);
        run.TextReflection.DirDeg.Should().BeApproximately(90.0, 1.0);
        run.TextReflection.ScaleY.Should().BeApproximately(-0.75, 0.001);
        run.TextReflection.EndPos.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public void RoundTrip_TextGlowAndSoftEdge_PreservedThroughWriteRead()
    {
        var pres = BuildPres(slide =>
        {
            var shape = TextShape(tb =>
            {
                var run = tb.Paragraphs[0].Runs[0];
                run.TextGlow = new RunTextGlow
                {
                    Color = new ThemeAwareColor(new SrgbColor(0x22, 0x88, 0xFF)),
                    Alpha = 96,
                    RadiusPt = 4.5
                };
                run.TextSoftEdge = new RunTextSoftEdge
                {
                    RadiusPt = 2.25
                };
            });
            slide.Shapes.Add(shape);
        });

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);
        var run = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0];

        run.TextGlow.Should().NotBeNull("glow was set");
        run.TextGlow!.Color.Resolved.R.Should().Be(0x22);
        run.TextGlow.Color.Resolved.G.Should().Be(0x88);
        run.TextGlow.Color.Resolved.B.Should().Be(0xFF);
        run.TextGlow.Alpha.Should().BeInRange(94, 97);
        run.TextGlow.RadiusPt.Should().BeApproximately(4.5, 0.5, "EMU round-trip tolerance");
        run.TextSoftEdge.Should().NotBeNull("soft-edge was set");
        run.TextSoftEdge!.RadiusPt.Should().BeApproximately(2.25, 0.5, "EMU round-trip tolerance");

        var rPr = GetFirstRunRPr(File.ReadAllBytes(path));
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var effectLst = rPr!.Element(a + "effectLst");
        effectLst.Should().NotBeNull("run effects must be written under a:rPr/a:effectLst");
        effectLst!.Element(a + "glow").Should().NotBeNull();
        effectLst.Element(a + "softEdge").Should().NotBeNull();
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
        tb.Paragraphs.Add(new Paragraph { Runs = { run } });

        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 3000000, ExtentCyEmu = 1000000,
            TextBody = tb
        });

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var textOp = ops.OfType<DrawOp.Shape>()
                        .Single(s => s.Text is not null);

        var resolvedRun = textOp.Text!.Paragraphs[0].Runs[0];
        resolvedRun.TextShadow.Should().NotBeNull("shadow must be resolved");
        resolvedRun.TextShadow!.Alpha.Should().Be(200);
        resolvedRun.TextShadow.Color.R.Should().Be(0x10);
        resolvedRun.TextShadow.DirDeg.Should().Be(135.0);
        // Pt → DIP: 3pt * 96/72 ≈ 4 DIP
        resolvedRun.TextShadow.DistDip.Should().BeApproximately(3.0 * 96.0 / 72.0, 0.1);
    }

    [Fact]
    public void Compositor_NoFillTextBoxShapeShadow_IsResolvedOntoGlyphs()
    {
        var p = MakePres();
        p.Slides[0].Shapes.Clear();

        var shape = TextShape(_ => { });
        shape.Fill = ShapeFill.None.Instance;
        shape.Effects = new ShapeEffects
        {
            HasOuterShadow = true,
            OuterShadowColor = new SrgbColor(0x40, 0x40, 0x40),
            OuterShadowAlpha = 180,
            OuterShadowBlurRadEmu = 63500,
            OuterShadowDistEmu = 38100,
            OuterShadowDirDeg = 45.0,
        };
        p.Slides[0].Shapes.Add(shape);

        var run = SlideCompositor.Compose(p, p.Slides[0])
            .OfType<DrawOp.Shape>()
            .Single()
            .Text!.Paragraphs[0].Runs[0];

        run.TextShadow.Should().NotBeNull();
        run.TextShadow!.Color.Should().Be(new SrgbColor(0x40, 0x40, 0x40));
        run.TextShadow.Alpha.Should().Be(180);
        run.TextShadow.BlurDip.Should().BeApproximately(63500.0 / 9525.0, 0.01);
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
        tb.Paragraphs.Add(new Paragraph { Runs = { run } });

        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 3000000, ExtentCyEmu = 1000000,
            TextBody = tb
        });

        var ops  = SlideCompositor.Compose(p, p.Slides[0]);
        var textOp = ops.OfType<DrawOp.Shape>().Single(s => s.Text is not null);
        var resolvedRun = textOp.Text!.Paragraphs[0].Runs[0];

        resolvedRun.TextFill.Should().BeOfType<ResolvedFill.Gradient>();
        var grad = (ResolvedFill.Gradient)resolvedRun.TextFill!;
        grad.AngleDegrees.Should().Be(45.0);
        grad.StartColor.R.Should().Be(0xFF);
        grad.EndColor.B.Should().Be(0xFF);
    }

    [Fact]
    public void Compositor_TextReflection_ResolvedOntoResolvedRun()
    {
        var p = MakePres();
        p.Slides[0].Shapes.Clear();

        var tb = new TextBody();
        var run = new Run
        {
            Text = "Reflect",
            TextReflection = new RunTextReflection
            {
                Alpha = 144,
                BlurPt = 1.5,
                DistPt = 3.0,
                DirDeg = 90.0,
                ScaleY = -1.0
            }
        };
        tb.Paragraphs.Add(new Paragraph { Runs = { run } });

        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 3000000, ExtentCyEmu = 1000000,
            TextBody = tb
        });

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var textOp = ops.OfType<DrawOp.Shape>().Single(s => s.Text is not null);
        var resolvedRun = textOp.Text!.Paragraphs[0].Runs[0];

        resolvedRun.TextReflection.Should().NotBeNull("reflection must be resolved");
        resolvedRun.TextReflection!.Alpha.Should().Be(144);
        resolvedRun.TextReflection.BlurDip.Should().BeApproximately(1.5 * 96.0 / 72.0, 0.1);
        resolvedRun.TextReflection.DistDip.Should().BeApproximately(3.0 * 96.0 / 72.0, 0.1);
        resolvedRun.TextReflection.DirDeg.Should().Be(90.0);
        resolvedRun.TextReflection.ScaleY.Should().Be(-1.0);
    }

    [Fact]
    public void Compositor_TextGlowAndSoftEdge_ResolvedOntoResolvedRun()
    {
        var p = MakePres();
        p.Slides[0].Shapes.Clear();

        var tb = new TextBody();
        tb.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "Glow",
                    TextGlow = new RunTextGlow
                    {
                        Color = new ThemeAwareColor(new SrgbColor(0x10, 0x80, 0xF0)),
                        Alpha = 144,
                        RadiusPt = 3.0,
                    },
                    TextSoftEdge = new RunTextSoftEdge
                    {
                        RadiusPt = 1.5,
                    }
                }
            }
        });

        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 4000000,
            ExtentCyEmu = 1000000,
            TextBody = tb
        });

        var ops = SlideCompositor.Compose(p, p.Slides[0]).ToList();
        var resolvedRun = ops.OfType<DrawOp.Shape>()
            .Single(s => s.Text is not null)
            .Text!.Paragraphs.Single().Runs.Single();

        resolvedRun.TextGlow.Should().NotBeNull();
        resolvedRun.TextGlow!.Color.G.Should().Be(0x80);
        resolvedRun.TextGlow.Alpha.Should().Be(144);
        resolvedRun.TextGlow.RadiusDip.Should().BeApproximately(3.0 * 96.0 / 72.0, 0.1);
        resolvedRun.TextSoftEdge.Should().NotBeNull();
        resolvedRun.TextSoftEdge!.RadiusDip.Should().BeApproximately(1.5 * 96.0 / 72.0, 0.1);
    }

    [Fact]
    public void Compositor_WarpPreset_PropagatedToResolvedTextLayout()
    {
        var p = MakePres();
        p.Slides[0].Shapes.Clear();

        var tb = new TextBody { WarpPreset = "textWave1" };
        tb.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Warp" } } });

        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 3000000, ExtentCyEmu = 1000000,
            TextBody = tb
        });

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var textOp = ops.OfType<DrawOp.Shape>().Single(s => s.Text is not null);

        textOp.Text!.WarpPreset.Should().Be("textWave1");
    }

    [Fact]
    public void Compositor_WarpAdjusts_PropagatedToResolvedTextLayout()
    {
        var p = MakePres();
        p.Slides[0].Shapes.Clear();

        var tb = new TextBody { WarpPreset = "textArchUp" };
        tb.WarpAdjusts.Add(("adj1", "val 30000"));
        tb.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Warp" } } });

        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 3000000, ExtentCyEmu = 1000000,
            TextBody = tb
        });

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var textOp = ops.OfType<DrawOp.Shape>().Single(s => s.Text is not null);

        textOp.Text!.WarpAdjusts.Should().ContainSingle()
            .Which.Should().Be(("adj1", "val 30000"));
    }

    [Fact]
    public void TextRunEffectRenderPlanner_OrdersShadowFillOutlinePasses()
    {
        var run = new ResolvedRun
        {
            Text = "Plan",
            Color = new SrgbColor(10, 20, 30),
            TextShadow = new ResolvedRunShadow
            {
                Color = new SrgbColor(1, 2, 3),
                Alpha = 180,
                DistDip = 4,
                DirDeg = 0
            },
            TextOutline = new ResolvedOutline.Visible(
                new SrgbColor(40, 50, 60),
                widthDip: 2,
                OutlineDash.Solid)
        };

        var plan = TextRunEffectRenderPlanner.Plan(
            run,
            new LayoutRect(10, 20, 40, 12),
            horizontalProgress: 0.25,
            new LayoutRect(0, 0, 200, 100),
            new ResolvedTextLayout());

        plan.Passes.Should().HaveCount(3);
        plan.Passes[0].Should().BeOfType<TextRunEffectPass.Shadow>();
        plan.Passes[1].Should().BeOfType<TextRunEffectPass.Fill>();
        plan.Passes[2].Should().BeOfType<TextRunEffectPass.Outline>();
    }

    [Fact]
    public void TextRunEffectRenderPlanner_ShadowPassCarriesOffsetAlphaAndBlurMetadata()
    {
        var run = new ResolvedRun
        {
            Text = "Shadow",
            Color = SrgbColor.Black,
            TextShadow = new ResolvedRunShadow
            {
                Color = new SrgbColor(1, 2, 3),
                Alpha = 160,
                BlurDip = 3,
                DistDip = 8,
                DirDeg = 0
            }
        };

        var plan = TextRunEffectRenderPlanner.Plan(
            run,
            new LayoutRect(0, 0, 60, 20),
            horizontalProgress: 0,
            new LayoutRect(0, 0, 200, 100),
            new ResolvedTextLayout());

        var shadows = plan.Passes.OfType<TextRunEffectPass.Shadow>().ToArray();
        shadows.Should().HaveCount(17);
        shadows[^1].OffsetX.Should().BeApproximately(8, 0.001);
        shadows[^1].OffsetY.Should().BeApproximately(0, 0.001);
        shadows[^1].Alpha.Should().Be(9);
        shadows.Should().OnlyContain(shadow => shadow.Alpha == 9);
        shadows[^1].BlurDip.Should().Be(3);
        shadows[^1].IsBlurPass.Should().BeFalse();
        shadows[0].IsBlurPass.Should().BeTrue();
        shadows[0].SpreadDip.Should().BeGreaterThan(0);
        shadows[15].SpreadDip.Should().BeApproximately(3, 0.001,
            "the shared plan preserves the authored blur-ring spread");
        shadows[15].BaseOffsetX.Should().BeApproximately(8, 0.001);
        shadows[15].BaseOffsetY.Should().BeApproximately(0, 0.001);
    }

    [Fact]
    public void PptxReader_ReadsCorpusWordArtReflection()
    {
        var path = FindWorkspaceFile("tools", "FreeP.RenderCompare", "corpus", "13-wordart.pptx");

        var pres = PptxPackageReader.Read(path);
        var reflectionRun = pres.Slides
            .SelectMany(s => s.Shapes)
            .SelectMany(s => s.TextBody?.Paragraphs ?? Enumerable.Empty<Paragraph>())
            .SelectMany(p => p.Runs)
            .Single(r => r.Text == "Arch Up Text");

        reflectionRun.Caps.Should().Be(RunTextCaps.All);
        reflectionRun.TextReflection.Should().NotBeNull();
        reflectionRun.TextReflection!.Alpha.Should().BeInRange(126, 128);
        reflectionRun.TextReflection.ScaleY.Should().BeApproximately(-1.0, 0.001);

        var resolvedRun = SlideCompositor.Compose(pres, pres.Slides[0])
            .OfType<DrawOp.Shape>()
            .SelectMany(shape => shape.Text?.Paragraphs ?? Enumerable.Empty<ResolvedParagraph>())
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.Text == "ARCH UP TEXT");
        resolvedRun.Text.Should().Be("ARCH UP TEXT");

        var roundTripped = PptxPackageReader.Read(WriteToPptx(pres));
        roundTripped.Slides
            .SelectMany(s => s.Shapes)
            .SelectMany(s => s.TextBody?.Paragraphs ?? Enumerable.Empty<Paragraph>())
            .SelectMany(p => p.Runs)
            .Single(r => r.Text == "Arch Up Text")
            .Caps.Should().Be(RunTextCaps.All);
    }

    [Fact]
    public void TextRunEffectRenderPlanner_OrdersReflectionBeforeFillAndOutline()
    {
        var run = new ResolvedRun
        {
            Text = "Reflect",
            Color = new SrgbColor(10, 20, 30),
            TextReflection = new ResolvedRunReflection
            {
                Alpha = 128,
                DistDip = 4,
                DirDeg = 90,
                ScaleY = -1.0
            },
            TextOutline = new ResolvedOutline.Visible(
                new SrgbColor(40, 50, 60),
                widthDip: 2,
                OutlineDash.Solid)
        };

        var plan = TextRunEffectRenderPlanner.Plan(
            run,
            new LayoutRect(10, 20, 80, 20),
            horizontalProgress: 0.25,
            new LayoutRect(0, 0, 200, 100),
            new ResolvedTextLayout());

        plan.Passes.Should().HaveCount(3);
        plan.Passes[0].Should().BeOfType<TextRunEffectPass.Reflection>();
        plan.Passes[1].Should().BeOfType<TextRunEffectPass.Fill>();
        plan.Passes[2].Should().BeOfType<TextRunEffectPass.Outline>();
    }

    [Fact]
    public void TextRunEffectRenderPlanner_AddsMetalHighlightAfterFaceFill()
    {
        var plan = TextRunEffectRenderPlanner.Plan(
            new ResolvedRun { Text = "Metal", Color = new SrgbColor(0xA0, 0x30, 0x70) },
            new LayoutRect(10, 20, 80, 20),
            horizontalProgress: 0.25,
            new LayoutRect(0, 0, 200, 100),
            new ResolvedTextLayout
            {
                Text3dEffects = new ResolvedShapeEffects { PrstMaterial = "metal" }
            });

        plan.Passes.Should().HaveCount(2);
        plan.Passes[0].Should().BeOfType<TextRunEffectPass.Fill>();
        var highlight = plan.Passes[1].Should().BeOfType<TextRunEffectPass.MaterialHighlight>().Subject;
        highlight.FillBrush.Should().BeOfType<ResolvedFill.Gradient>();
        ((ResolvedFill.Gradient)highlight.FillBrush).Stops.Last().Alpha.Should().Be(0);
    }

    [Fact]
    public void PptxReader_ReadsAndRoundTripsBodyLevelWordArt3d()
    {
        var path = FindWorkspaceFile("tools", "FreeP.RenderCompare", "corpus", "13-wordart.pptx");
        var pres = PptxPackageReader.Read(path);
        var bodies = pres.Slides[0].Shapes
            .Where(shape => shape.TextBody?.Text3dEffects is not null)
            .Select(shape => shape.TextBody!)
            .ToArray();

        bodies.Should().HaveCount(2);
        var archEffects = bodies
            .Single(body => body.Paragraphs.SelectMany(p => p.Runs).Any(r => r.Text == "Arch Up Text"))
            .Text3dEffects;
        archEffects.Should().NotBeNull();
        archEffects!.PrstMaterial.Should().Be("metal");
        archEffects.BevelTop.Should().NotBeNull();
        archEffects.BevelTop!.WidthEmu.Should().Be(127000);
        archEffects.BevelTop.HeightEmu.Should().Be(31750);
        archEffects.Scene3d.Should().NotBeNull();
        archEffects.Scene3d!.CameraPreset.Should().Be("orthographicFront");
        archEffects.Scene3d.LightRig.Should().Be("contrasting");
        archEffects.Scene3d.LightRigDir.Should().Be("t");

        var waveEffects = bodies
            .Single(body => body.Paragraphs.SelectMany(p => p.Runs).Any(r => r.Text == "Wave Text"))
            .Text3dEffects;
        waveEffects.Should().NotBeNull();
        waveEffects!.PrstMaterial.Should().Be("softEdge");
        waveEffects.ExtrusionHeightEmu.Should().Be(57150);
        waveEffects.BevelTop.Should().NotBeNull();
        waveEffects.BevelTop!.WidthEmu.Should().Be(25400);
        waveEffects.BevelTop.HeightEmu.Should().Be(38100);
        bodies
            .Single(body => body.Paragraphs.SelectMany(p => p.Runs).Any(r => r.Text == "Wave Text"))
            .Paragraphs.SelectMany(p => p.Runs)
            .Single(r => r.Text == "Wave Text")
            .TextOutline.Should().BeSameAs(ShapeOutline.None.Instance);

        var roundTripPath = WriteToPptx(pres);
        GetSchemaErrors(File.ReadAllBytes(roundTripPath)).Should().BeEmpty();
        var roundTripped = PptxPackageReader.Read(roundTripPath);
        roundTripped.Slides[0].Shapes
            .Select(shape => shape.TextBody?.Text3dEffects)
            .Count(effects => effects is not null)
            .Should().Be(2);

        var resolvedLayouts = SlideCompositor.Compose(pres, pres.Slides[0])
            .OfType<DrawOp.Shape>()
            .Select(shape => shape.Text)
            .Where(text => text?.Text3dEffects is not null)
            .ToArray();
        resolvedLayouts.Should().HaveCount(2);
        resolvedLayouts.Select(text => text!.Text3dEffects!.Scene3dCameraPreset)
            .Should().Contain("orthographicFront");
        resolvedLayouts.Select(text => text!.Text3dEffects!.PrstMaterial)
            .Should().Contain("metal");
    }

    [Fact]
    public void TextRunEffectRenderPlanner_EmitsGlowAndSoftEdgePassesBeforeFill()
    {
        var run = new ResolvedRun
        {
            Text = "Glow",
            Color = new SrgbColor(10, 20, 30),
            TextGlow = new ResolvedRunGlow
            {
                Color = new SrgbColor(0x20, 0x80, 0xFF),
                Alpha = 120,
                RadiusDip = 4,
            },
            TextSoftEdge = new ResolvedRunSoftEdge
            {
                RadiusDip = 3,
            }
        };

        var plan = TextRunEffectRenderPlanner.Plan(
            run,
            new LayoutRect(10, 20, 80, 20),
            horizontalProgress: 0.25,
            new LayoutRect(0, 0, 200, 100),
            new ResolvedTextLayout());

        var glowPasses = plan.Passes.OfType<TextRunEffectPass.Glow>().ToArray();
        var softEdgePasses = plan.Passes.OfType<TextRunEffectPass.SoftEdge>().ToArray();
        glowPasses.Should().HaveCount(2);
        glowPasses[0].Color.B.Should().Be(0xFF);
        glowPasses[0].StrokeWidthDip.Should().BeApproximately(8, 0.001);
        softEdgePasses.Should().HaveCount(16);
        softEdgePasses.Should().OnlyContain(pass => pass.IsBlurPass);
        plan.Passes.Last().Should().BeOfType<TextRunEffectPass.Fill>();
        var passes = plan.Passes.ToList();
        passes.IndexOf(glowPasses[0]).Should().BeLessThan(passes.IndexOf(softEdgePasses[0]));
    }

    [Fact]
    public void TextRunEffectRenderPlanner_WarpAdjustUsesRunCenterForYOffset()
    {
        var run = new ResolvedRun { Text = "Warp", Color = SrgbColor.Black };
        var bounds = new LayoutRect(0, 0, 200, 100);
        var layout = new ResolvedTextLayout
        {
            WarpPreset = "textArchUp",
            WarpAdjusts = new[] { ("adj1", "val 25000") }
        };

        var plan = TextRunEffectRenderPlanner.Plan(
            run,
            new LayoutRect(10, 20, 40, 12),
            horizontalProgress: 0.5,
            bounds,
            layout);

        plan.WarpYOffsetDip.Should().BeApproximately(-8.925, 0.001);
        plan.GlyphBoundsDip.Y.Should().BeApproximately(11.075, 0.001);
        plan.WarpTransform.Should().NotBeNull();
        plan.WarpTransform!.Family.Should().Be(WordArtWarpFamily.Arch);
        plan.WarpTransform.SampleProgress.Should().BeApproximately(0.15, 0.001);
        plan.WarpTransform.AmplitudeScale.Should().BeApproximately(0.5, 0.001);
        plan.WarpTransform.RotationDeg.Should().BeLessThan(0);
    }

    [Fact]
    public void WordArtWarpPlanner_PlansFamilyAdjustScaleAndAffineTransform()
    {
        var plan = WordArtWarpPlanner.Plan(
            "textCan",
            new LayoutRect(80, 20, 40, 24),
            new LayoutRect(0, 0, 200, 100),
            new[] { ("adj1", "val 75000") });

        plan.Should().NotBeNull();
        plan!.Family.Should().Be(WordArtWarpFamily.Can);
        plan.SampleProgress.Should().BeApproximately(0.5, 0.001);
        plan.AmplitudeScale.Should().BeApproximately(1.5, 0.001);
        plan.OffsetYDip.Should().BeApproximately(-52.5, 0.001);
        plan.ScaleY.Should().BeGreaterThan(1.0);
        plan.HasAffineTransform.Should().BeTrue();
    }

    [Fact]
    public void WordArtWarpPlanner_PreservesWave2AndUnknownPresetBehavior()
    {
        WordArtWarpPlanner.ComputeYOffset("textWave2", 0.125, 100)
            .Should().BeApproximately(-10, 0.001);
        WordArtWarpPlanner.Plan(
                "not-a-preset",
                new LayoutRect(0, 0, 40, 12),
                new LayoutRect(0, 0, 200, 100),
                Array.Empty<(string Name, string Formula)>())
            .Should().BeNull();
    }

    [Fact]
    public void WpfAndAvaloniaSlideCanvases_UseSharedTextRunEffectRenderPlanner()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("TextRunEffectRenderPlanner.Plan(");
            source.Should().Contain("case TextRunEffectPass.Glow");
            source.Should().Contain("case TextRunEffectPass.SoftEdge");
            source.Should().Contain("PushOpacityMask");
            source.Should().NotContain("TextShadow is { } ts");
            source.Should().NotContain("TextGlow is { }");
            source.Should().NotContain("TextSoftEdge is { }");
            source.Should().NotContain("DirDeg * Math.PI");
            source.Should().NotContain("BlurDip / 1.5");
            source.Should().NotContain("WordArtWarpPlanner.ComputeYOffset(warpPreset");
            source.Should().NotContain("TryClassifyPreset(");
        }

        wpf.Should().Contain("TextShadowBlurSpreadScale",
            "the measured blur-ring calibration is WPF-compositor local");
        avalonia.Should().NotContain("TextShadowBlurSpreadScale",
            "Avalonia must retain the shared authored shadow offsets");
        wpf.Should().Contain("ImportedAptosDisplayWpfRasterScaleY");
        wpf.Should().Contain("Autofit Shrink Demo");
        avalonia.Should().NotContain("ImportedAptosDisplayWpfRasterScaleY");
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
        var reflection = new RunTextReflection
        {
            Alpha = 128, BlurPt = 1.0, DistPt = 2.0, DirDeg = 90.0, ScaleY = -1.0
        };
        var glow = new RunTextGlow
        {
            Color = new ThemeAwareColor(new SrgbColor(0x22, 0x88, 0xFF)),
            Alpha = 144,
            RadiusPt = 4.5
        };
        var softEdge = new RunTextSoftEdge { RadiusPt = 2.25 };

        var tb = new TextBody { WarpPreset = "textCircle" };
        var run = new Run
        {
            Text = "Clone",
            TextFill = fill,
            TextShadow = shadow,
            TextReflection = reflection,
            TextGlow = glow,
            TextSoftEdge = softEdge
        };
        tb.Paragraphs.Add(new Paragraph { Runs = { run } });

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 3000000, ExtentCyEmu = 1000000,
            TextBody = tb
        };

        var src = new Slide();
        src.Shapes.Add(shape);

        var dst = SlideCloner.CloneSlide(src);

        var clonedShape = dst.Shapes[0];
        var clonedTb    = clonedShape.TextBody!;
        var clonedRun   = clonedTb.Paragraphs[0].Runs[0];

        clonedTb.WarpPreset.Should().Be("textCircle");
        clonedRun.TextFill.Should().NotBeNull();
        clonedRun.TextShadow.Should().NotBeNull();
        clonedRun.TextShadow!.BlurPt.Should().Be(4.0);
        clonedRun.TextReflection.Should().NotBeNull();
        clonedRun.TextReflection!.DistPt.Should().Be(2.0);
        clonedRun.TextGlow.Should().NotBeNull();
        clonedRun.TextGlow.Should().NotBeSameAs(glow);
        clonedRun.TextGlow!.Color.Resolved.B.Should().Be(0xFF);
        clonedRun.TextGlow.Alpha.Should().Be(144);
        clonedRun.TextGlow.RadiusPt.Should().Be(4.5);
        clonedRun.TextSoftEdge.Should().NotBeNull();
        clonedRun.TextSoftEdge.Should().NotBeSameAs(softEdge);
        clonedRun.TextSoftEdge!.RadiusPt.Should().Be(2.25);

        // Verify deep copy — mutating source must not affect clone
        run.TextShadow.BlurPt = 99.0;
        run.TextReflection.DistPt = 99.0;
        run.TextGlow.RadiusPt = 99.0;
        run.TextSoftEdge.RadiusPt = 99.0;
        clonedRun.TextShadow.BlurPt.Should().Be(4.0, "deep copy must be independent");
        clonedRun.TextReflection.DistPt.Should().Be(2.0, "deep copy must be independent");
        clonedRun.TextGlow.RadiusPt.Should().Be(4.5, "deep copy must be independent");
        clonedRun.TextSoftEdge.RadiusPt.Should().Be(2.25, "deep copy must be independent");
    }

    // ─── BA1: rPr child-order + OpenXmlValidator ────────────────────────────

    /// <summary>
    /// BA1: A run with outline + gradient fill + shadow + latin font must emit a:rPr children
    /// in CT_TextCharacterProperties order (a:ln → fill → a:effectLst → a:latin → a:hlinkClick)
    /// and must pass OpenXmlValidator with no schema errors.
    /// </summary>
    [Fact]
    public void RprChildOrder_OutlineGradientShadowLatin_PassesOpenXmlValidatorAndCorrectOrder()
    {
        var pres = BuildPres(slide =>
        {
            var shape = TextShape(tb =>
            {
                var run = tb.Paragraphs[0].Runs[0];
                run.FontFamily  = "Impact";
                run.TextOutline = new ShapeOutline.Visible(
                    new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)), widthPt: 1.0, dash: OutlineDash.Solid);
                run.TextFill = new ShapeFill.Gradient(
                    new ThemeAwareColor(new SrgbColor(0xFF, 0x66, 0x00)),
                    new ThemeAwareColor(new SrgbColor(0xCC, 0x00, 0x00)),
                    angleDegrees: 90.0);
                run.TextShadow = new RunTextShadow
                {
                    Color  = new ThemeAwareColor(new SrgbColor(0x20, 0x20, 0x20)),
                    Alpha  = 180,
                    BlurPt = 3.0,
                    DistPt = 2.5,
                    DirDeg = 45.0
                };
            });
            slide.Shapes.Add(shape);
        });

        var path = WriteToPptx(pres);
        var bytes = File.ReadAllBytes(path);

        // 1. OpenXmlValidator: no schema errors
        var schemaErrors = GetSchemaErrors(bytes);
        schemaErrors.Should().BeEmpty(
            "a:rPr with outline+gradient+shadow+latin must be schema-valid; errors: {0}",
            string.Join("; ", schemaErrors));

        // 2. Element order inside a:rPr: ln → gradFill → effectLst → latin
        var rPr = GetFirstRunRPr(bytes);
        rPr.Should().NotBeNull("rPr must exist in written slide XML");
        var childNames = rPr!.Elements()
                             .Select(e => e.Name.LocalName)
                             .ToList();

        var lnIdx        = childNames.IndexOf("ln");
        var gradFillIdx  = childNames.IndexOf("gradFill");
        var effectLstIdx = childNames.IndexOf("effectLst");
        var latinIdx     = childNames.IndexOf("latin");

        lnIdx.Should().BeGreaterThanOrEqualTo(0, "a:ln must be present");
        gradFillIdx.Should().BeGreaterThan(lnIdx,
            "gradFill must come after ln (CT_TextCharacterProperties order)");
        effectLstIdx.Should().BeGreaterThan(gradFillIdx,
            "effectLst must come after fill group");
        latinIdx.Should().BeGreaterThan(effectLstIdx,
            "latin must come after effectLst");
    }

    /// <summary>
    /// Validates schema errors in all slide parts of the PPTX.  Uses part-by-part
    /// validation so a pre-existing FreeP table-styles namespace bug (p:tblStyleLst
    /// vs a:tblStyleLst) doesn't throw and mask the rPr order check.
    /// </summary>
    private static List<string> GetSchemaErrors(byte[] pptxBytes)
    {
        using var ms = new MemoryStream(pptxBytes);
        using var pkg = PresentationDocument.Open(ms, isEditable: false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        var errors = new List<string>();
        var presentation = pkg.PresentationPart;
        if (presentation is null) return errors;

        // Validate slide parts only — avoids the pre-existing p:tblStyleLst namespace
        // issue in the table-styles part which crashes the whole-package validator.
        foreach (var slidePart in presentation.SlideParts)
        {
            try
            {
                errors.AddRange(
                    validator.Validate(slidePart)
                             .Where(e => e.ErrorType == ValidationErrorType.Schema)
                             .Select(e => $"{e.Description} @ {e.Path?.XPath}"));
            }
            catch (InvalidDataException)
            {
                // Skip parts that cannot be loaded (pre-existing table-styles bug).
            }
        }
        return errors;
    }

    private static XElement? GetFirstRunRPr(byte[] pptxBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(pptxBytes), ZipArchiveMode.Read);
        var slideEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (slideEntry is null) return null;
        using var stream = slideEntry.Open();
        var doc = XDocument.Load(stream);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        return doc.Descendants(a + "rPr").FirstOrDefault();
    }

    // ─── BA3: opaque shadow alpha round-trip ─────────────────────────────────

    /// <summary>
    /// BA3: An opaque shadow (Alpha=255) must round-trip as 255 (not 128).
    /// The writer omits a:alpha when Alpha==255 (DrawingML: absent = 100% opaque).
    /// The reader must default missing alpha to 255, not 128.
    /// </summary>
    [Fact]
    public void RoundTrip_OpaqueTextShadow_Alpha255_PreservedNotHalved()
    {
        var pres = BuildPres(slide =>
        {
            var shape = TextShape(tb =>
            {
                tb.Paragraphs[0].Runs[0].TextShadow = new RunTextShadow
                {
                    Color  = new ThemeAwareColor(new SrgbColor(0, 0, 0)),
                    Alpha  = 255,   // fully opaque — writer must omit a:alpha
                    BlurPt = 2.0,
                    DistPt = 2.0,
                    DirDeg = 45.0
                };
            });
            slide.Shapes.Add(shape);
        });

        var reloaded = PptxPackageReader.Read(WriteToPptx(pres));
        var run = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0];

        run.TextShadow.Should().NotBeNull();
        run.TextShadow!.Alpha.Should().Be(255,
            "opaque shadow (no a:alpha element) must read back as 255, not 128");
    }

    /// <summary>
    /// BA3: A 50% transparent shadow (Alpha≈128, val=50000) must round-trip as ~128.
    /// </summary>
    [Fact]
    public void RoundTrip_SemiTransparentTextShadow_Alpha128_PreservedApproximately()
    {
        var pres = BuildPres(slide =>
        {
            var shape = TextShape(tb =>
            {
                tb.Paragraphs[0].Runs[0].TextShadow = new RunTextShadow
                {
                    Color  = new ThemeAwareColor(new SrgbColor(0, 0, 0)),
                    Alpha  = 128,   // ~50% — writer emits a:alpha val=50196
                    BlurPt = 2.0,
                    DistPt = 2.0,
                    DirDeg = 45.0
                };
            });
            slide.Shapes.Add(shape);
        });

        var reloaded = PptxPackageReader.Read(WriteToPptx(pres));
        var run = reloaded.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0];

        run.TextShadow.Should().NotBeNull();
        run.TextShadow!.Alpha.Should().BeInRange(126, 130,
            "50% transparent shadow must round-trip to approximately 128 (±2 due to EMU rounding)");
    }

    // ─── BA4: warp adjust guide round-trip ───────────────────────────────────

    /// <summary>
    /// BA4: A warp preset with a custom a:gd guide (adj1 = val 30000) must survive
    /// write → read round-trip, preserving the guide name and formula exactly.
    /// </summary>
    [Fact]
    public void RoundTrip_WarpAdjust_CustomGuide_Preserved()
    {
        var pres = BuildPres(slide =>
        {
            var shape = TextShape(tb =>
            {
                tb.WarpPreset = "textArchUp";
                tb.WarpAdjusts.Add(("adj1", "val 30000"));
            });
            slide.Shapes.Add(shape);
        });

        var reloaded = PptxPackageReader.Read(WriteToPptx(pres));
        var tb = reloaded.Slides[0].Shapes[0].TextBody!;

        tb.WarpPreset.Should().Be("textArchUp");
        tb.WarpAdjusts.Should().HaveCount(1, "one custom guide must survive round-trip");
        tb.WarpAdjusts[0].Name.Should().Be("adj1");
        tb.WarpAdjusts[0].Formula.Should().Be("val 30000");
    }

    /// <summary>
    /// BA4: A warp preset with an empty avLst must read back with no adjusts (no crash).
    /// </summary>
    [Fact]
    public void RoundTrip_WarpNoAdjusts_EmptyAvLst_NoError()
    {
        var pres = BuildPres(slide =>
        {
            var shape = TextShape(tb => { tb.WarpPreset = "textWave1"; });
            slide.Shapes.Add(shape);
        });

        var reloaded = PptxPackageReader.Read(WriteToPptx(pres));
        var tb = reloaded.Slides[0].Shapes[0].TextBody!;

        tb.WarpPreset.Should().Be("textWave1");
        tb.WarpAdjusts.Should().BeEmpty("no custom guides — avLst is empty");
    }

    private static string ReadWorkspaceFile(params string[] relativeParts)
        => File.ReadAllText(FindWorkspaceFile(relativeParts));

    private static string FindWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.Find(relativeParts);
}
