using System.IO;
using System.IO.Compression;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Wave 22B — Tests for text columns (numCol/spcCol round-trip + column layout)
/// and gradient outline (stops/angle round-trip + ResolvedOutline.Gradient).
/// </summary>
public sealed class TextColumnsGradOutlineTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static PresentationModel MakePresentation(Action<PresentationModel>? configure = null)
    {
        var p = PresentationModel.CreateEmpty();
        configure?.Invoke(p);
        return p;
    }

    private static SlideShape MakeTextShape(TextBody tb) => new SlideShape
    {
        Id = 1,
        Kind = SlideShapeKind.AutoShape,
        AutoShapeKind = DrawingShapeKind.Rectangle,
        OffsetXEmu = 457200,
        OffsetYEmu = 274320,
        ExtentCxEmu = 4572000,
        ExtentCyEmu = 1371600,
        TextBody = tb,
    };

    private static GradientStop MakeGStop(double pos, byte r, byte g, byte b) =>
        new GradientStop(pos, new ThemeAwareColor(new SrgbColor(r, g, b)));

    // ── Part 1: TextBody model ────────────────────────────────────────────────────

    [Fact]
    public void TextBody_DefaultColumnCount_IsOne()
    {
        var tb = new TextBody();
        tb.ColumnCount.Should().Be(1);
        tb.ColumnSpacingEmu.Should().Be(0);
    }

    [Fact]
    public void TextBody_ColumnCountSet_Roundtrips()
    {
        var tb = new TextBody { ColumnCount = 3, ColumnSpacingEmu = 457200 };
        tb.ColumnCount.Should().Be(3);
        tb.ColumnSpacingEmu.Should().Be(457200);
    }

    // ── Part 2: I/O round-trip for numCol/spcCol ─────────────────────────────────

    [Fact]
    public void Writer_EmitsNumColSpcCol_WhenGreaterThanOne()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        var tb = new TextBody
        {
            ColumnCount = 2,
            ColumnSpacingEmu = 457200,
        };
        tb.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Column text" } } });
        p.Slides[0].Shapes.Add(MakeTextShape(tb));

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(p, ms);
        ms.Position = 0;

        // Inspect the XML inside the zip
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var slideEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        slideEntry.Should().NotBeNull();

        using var sr = new StreamReader(slideEntry!.Open());
        var xml = sr.ReadToEnd();
        xml.Should().Contain("numCol=\"2\"");
        xml.Should().Contain("spcCol=\"457200\"");
    }

    [Fact]
    public void Writer_DoesNotEmitNumCol_WhenColumnCountIsOne()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        var tb = new TextBody { ColumnCount = 1 };
        tb.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "No columns" } } });
        p.Slides[0].Shapes.Add(MakeTextShape(tb));

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(p, ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var slideEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        using var sr = new StreamReader(slideEntry!.Open());
        var xml = sr.ReadToEnd();
        xml.Should().NotContain("numCol=");
    }

    [Fact]
    public void RoundTrip_NumColSpcCol_Preserved()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        var tb = new TextBody
        {
            ColumnCount = 2,
            ColumnSpacingEmu = 457200,
        };
        tb.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Col1" } } });
        tb.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Col2" } } });
        p.Slides[0].Shapes.Add(MakeTextShape(tb));

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = PptxPackageReader.Read(ms);

        var shape2 = p2.Slides[0].Shapes.First(s => s.TextBody is not null);
        shape2.TextBody!.ColumnCount.Should().Be(2);
        shape2.TextBody.ColumnSpacingEmu.Should().Be(457200);
    }

    // ── Part 3: Column layout helper ─────────────────────────────────────────────

    [Fact]
    public void Compositor_SingleColumn_TextLayout_HasColumnCountOne()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        var tb = new TextBody { ColumnCount = 1 };
        tb.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Hello" } } });
        p.Slides[0].Shapes.Add(MakeTextShape(tb));

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var shapeOp = ops.OfType<DrawOp.Shape>().First();
        shapeOp.Text.Should().NotBeNull();
        shapeOp.Text!.ColumnCount.Should().Be(1);
    }

    [Fact]
    public void Compositor_TwoColumns_TextLayout_HasColumnCountTwo()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        var tb = new TextBody { ColumnCount = 2, ColumnSpacingEmu = 457200 };
        tb.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Para 1" } } });
        tb.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Para 2" } } });
        p.Slides[0].Shapes.Add(MakeTextShape(tb));

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var shapeOp = ops.OfType<DrawOp.Shape>().First();
        shapeOp.Text.Should().NotBeNull();
        shapeOp.Text!.ColumnCount.Should().Be(2);
        shapeOp.Text.ColumnSpacingDip.Should().BeApproximately(457200 / 9525.0, 0.01);
    }

    // ── Part 4: ShapeOutline.GradientVisible model ────────────────────────────────

    [Fact]
    public void GradientVisible_StoresGradientAndWidth()
    {
        var gradient = new ShapeFill.Gradient(
            new[] { MakeGStop(0, 255, 0, 0), MakeGStop(1, 0, 0, 255) },
            GradientKind.Linear, 90);
        var outline = new ShapeOutline.GradientVisible(gradient, 2.0, OutlineDash.Solid);

        outline.WidthPt.Should().Be(2.0);
        outline.Gradient.Should().BeSameAs(gradient);
        outline.Gradient.Stops.Should().HaveCount(2);
        outline.Gradient.AngleDegrees.Should().Be(90);
    }

    // ── Part 5: Gradient outline I/O round-trip ────────────────────────────────

    [Fact]
    public void Writer_EmitsGradFill_InsideLnElement_ForGradientOutline()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        var gradient = new ShapeFill.Gradient(
            new[] { MakeGStop(0, 255, 0, 0), MakeGStop(1, 0, 0, 255) },
            GradientKind.Linear, 0);
        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
            Outline = new ShapeOutline.GradientVisible(gradient, 3.0),
        };
        p.Slides[0].Shapes.Add(shape);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(p, ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var slideEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        using var sr = new StreamReader(slideEntry!.Open());
        var xml = sr.ReadToEnd();
        xml.Should().Contain("gradFill");
        // Width of 3pt = 38100 EMU
        xml.Should().Contain("w=\"38100\"");
    }

    [Fact]
    public void RoundTrip_GradientOutline_StopsAndAnglePreserved()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        var gradient = new ShapeFill.Gradient(
            new[] { MakeGStop(0, 255, 0, 0), MakeGStop(1, 0, 0, 255) },
            GradientKind.Linear, 45.0);
        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 274320,
            ExtentCxEmu = 4572000, ExtentCyEmu = 1371600,
            Outline = new ShapeOutline.GradientVisible(gradient, 2.0),
        };
        p.Slides[0].Shapes.Add(shape);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = PptxPackageReader.Read(ms);

        var shape2 = p2.Slides[0].Shapes.First(s => s.Outline is ShapeOutline.GradientVisible);
        shape2.Outline.Should().BeOfType<ShapeOutline.GradientVisible>();
        var gv = (ShapeOutline.GradientVisible)shape2.Outline!;
        gv.WidthPt.Should().BeApproximately(2.0, 0.01);
        gv.Gradient.Stops.Should().HaveCount(2);
        gv.Gradient.AngleDegrees.Should().BeApproximately(45.0, 0.1);
    }

    // ── Part 6: ResolvedOutline.Gradient in compositor ──────────────────────────

    [Fact]
    public void Compositor_GradientOutline_ResolvesToResolvedGradient()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        var gradient = new ShapeFill.Gradient(
            new[] { MakeGStop(0, 255, 0, 0), MakeGStop(1, 0, 0, 255) },
            GradientKind.Linear, 90);
        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 274320,
            ExtentCxEmu = 4572000, ExtentCyEmu = 1371600,
            Outline = new ShapeOutline.GradientVisible(gradient, 2.0),
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var shapeOp = ops.OfType<DrawOp.Shape>().First();
        shapeOp.Outline.Should().BeOfType<ResolvedOutline.Gradient>();
        var rg = (ResolvedOutline.Gradient)shapeOp.Outline;
        rg.WidthDip.Should().BeApproximately(2.0 * (96.0 / 72.0), 0.01);
        rg.Fill.Stops.Should().HaveCount(2);
        rg.Fill.AngleDegrees.Should().BeApproximately(90, 0.1);
    }

    // ── Part 7: Solid outline + single-column still works ─────────────────────

    [Fact]
    public void Compositor_SolidOutline_StillResolvesToResolvedVisible()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 274320,
            ExtentCxEmu = 4572000, ExtentCyEmu = 1371600,
            Outline = new ShapeOutline.Visible(new SrgbColor(255, 0, 0), 1.0),
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var shapeOp = ops.OfType<DrawOp.Shape>().First();
        shapeOp.Outline.Should().BeOfType<ResolvedOutline.Visible>();
    }

    [Fact]
    public void SlideCloner_CopiesColumnFields()
    {
        var tb = new TextBody
        {
            ColumnCount = 3,
            ColumnSpacingEmu = 914400,
        };
        tb.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "A" } } });

        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
            TextBody = tb,
        };
        var cloned = SlideCloner.CloneShape(shape);
        cloned.TextBody.Should().NotBeNull();
        cloned.TextBody!.ColumnCount.Should().Be(3);
        cloned.TextBody.ColumnSpacingEmu.Should().Be(914400);
    }
}
