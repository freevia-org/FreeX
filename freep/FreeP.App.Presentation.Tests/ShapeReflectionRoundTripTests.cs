using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r143 freep-shape-reflection-unsupported: shape/picture-level a:reflection was read by
/// nothing, had no model field, was silently dropped on every save, and (for ordinary
/// shapes/pictures) never reached the renderer even though the render passes for it already
/// existed for the Zoom-frame-border and WordArt-text-run cases. These tests exercise the real
/// production PptxPackageReader/PptxPackageWriter/SlideCompositor (not stubs) to prove the
/// effect is now modeled, round-trips losslessly, and is wired into the render plan.
/// </summary>
public sealed class ShapeReflectionRoundTripTests
{
    [Fact]
    public void ShapeReflection_IsReadModeledAndRoundTripsOnSave()
    {
        const string shapeXml = """
            <p:sp xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                  xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:nvSpPr>
                <p:cNvPr id="55" name="ReflectedShape"/>
                <p:cNvSpPr/>
                <p:nvPr/>
              </p:nvSpPr>
              <p:spPr>
                <a:xfrm>
                  <a:off x="457200" y="457200"/>
                  <a:ext cx="1828800" cy="914400"/>
                </a:xfrm>
                <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                <a:effectLst>
                  <a:reflection blurRad="12700" stA="52000" stPos="0" endA="300" endPos="35000"
                                dist="6350" dir="5400000" fadeDir="5400000" sx="100000" sy="-100000"
                                kx="0" ky="0" algn="bl" rotWithShape="0"/>
                </a:effectLst>
              </p:spPr>
            </p:sp>
            """;

        // Read a PowerPoint-authored shape carrying a:reflection via the real production reader.
        var pres1 = PptxPackageReader.Read(BuildPptxWithShapeXml(shapeXml));
        var shape1 = pres1.Slides[0].Shapes.Single(s => s.Name == "ReflectedShape");

        shape1.Effects.Should().NotBeNull("the reflection must be modeled, not silently dropped");
        var reflection1 = shape1.Effects!.Reflection;
        reflection1.Should().NotBeNull();
        reflection1!.BlurRadEmu.Should().Be(12700);
        reflection1.StartAlpha.Should().Be(52000);
        reflection1.StartPos.Should().Be(0);
        reflection1.EndAlpha.Should().Be(300);
        reflection1.EndPos.Should().Be(35000);
        reflection1.DistEmu.Should().Be(6350);
        reflection1.DirDeg.Should().Be(90);
        reflection1.FadeDirDeg.Should().Be(90);
        reflection1.ScaleXPercent.Should().Be(100);
        reflection1.ScaleYPercent.Should().Be(-100);
        reflection1.Align.Should().Be("bl");
        reflection1.RotWithShape.Should().BeFalse();

        // Re-save with the real production writer and confirm the effect is not dropped.
        var pres2 = PptxPackageReader.Read(WritePptxToMemory(pres1));
        var shape2 = pres2.Slides[0].Shapes.Single(s => s.Name == "ReflectedShape");

        shape2.Effects.Should().NotBeNull("the reflection must survive a save/reload cycle");
        shape2.Effects!.Reflection.Should().BeEquivalentTo(reflection1,
            "a shape-level reflection effect must round-trip losslessly through save");

        // The saved XML must actually carry the a:reflection element (not just the in-memory model).
        var reflectionEl = ExtractSlide1Xml(WritePptxToMemory(pres1))
            .Descendants().Single(e => e.Name.LocalName == "reflection");
        reflectionEl.Attribute("sy")!.Value.Should().Be("-100000");
        reflectionEl.Attribute("algn")!.Value.Should().Be("bl");
    }

    [Fact]
    public void ShapeReflection_IsWiredIntoTheRenderPlan()
    {
        // The reflection render passes already existed for the Zoom-frame-border and
        // WordArt-text-run cases; ResolveEffects (used for ordinary AutoShape/Picture) never
        // read fx.Reflection. Prove SlideCompositor.Compose now surfaces HasReflection on the
        // real DrawOp.Shape.Effects for an ordinary autoshape, using the real compositor.
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 71,
            Name = "RenderedReflectionShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Effects = new ShapeEffects
            {
                Reflection = new ReflectionInfo
                {
                    StartAlpha = 42000,
                    BlurRadEmu = 12700,
                    DistEmu = 44450,
                    DirDeg = 90,
                    ScaleYPercent = -75,
                    EndPos = 25000,
                },
            },
        });

        var shapeOp = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Shape>().Single(s => s.ShapeId == 71);

        shapeOp.Effects.Should().NotBeNull("a shape with only a reflection effect must still resolve");
        shapeOp.Effects!.HasReflection.Should().BeTrue();
        shapeOp.Effects.ReflectionAlpha.Should().Be(107);
        shapeOp.Effects.ReflectionBlurDip.Should().BeApproximately(12700 / 9525d, 0.00001);
        shapeOp.Effects.ReflectionDistDip.Should().BeApproximately(44450 / 9525d, 0.00001);
        shapeOp.Effects.ReflectionScaleY.Should().BeApproximately(-0.75, 0.00001);
        shapeOp.Effects.ReflectionEndPos.Should().BeApproximately(0.25, 0.00001);
    }

    [Fact]
    public void ShapeEffects_GlowAndSoftEdge_StillRoundTripAlongsideReflection()
    {
        // Sibling coverage: confirm glow and soft edge (the effects already supported before
        // this fix) keep round-tripping correctly now that reflection shares BuildEffectLstEl
        // and the ReadSpPr "hasSomething" gate.
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 61,
            Name = "GlowSoftEdgeReflectionShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
            Effects = new ShapeEffects
            {
                HasGlow = true,
                GlowColor = new SrgbColor(0x11, 0x22, 0x33),
                GlowRadiusEmu = 90000,
                HasSoftEdge = true,
                SoftEdgeRadEmu = 63500,
                Reflection = new ReflectionInfo
                {
                    BlurRadEmu = 12700,
                    StartAlpha = 52000,
                    EndPos = 35000,
                    DistEmu = 6350,
                    ScaleYPercent = -100,
                    Align = "bl",
                    RotWithShape = false,
                },
            },
        });
        presentation.Slides.Add(slide);

        var reopened = PptxPackageReader.Read(WritePptxToMemory(presentation));
        var reopenedShape = reopened.Slides[0].Shapes.Single(s => s.Name == "GlowSoftEdgeReflectionShape");

        reopenedShape.Effects.Should().NotBeNull();
        reopenedShape.Effects!.HasGlow.Should().BeTrue();
        reopenedShape.Effects.GlowColor.Should().Be(new SrgbColor(0x11, 0x22, 0x33));
        reopenedShape.Effects.GlowRadiusEmu.Should().Be(90000);
        reopenedShape.Effects.HasSoftEdge.Should().BeTrue();
        reopenedShape.Effects.SoftEdgeRadEmu.Should().Be(63500);
        reopenedShape.Effects.Reflection.Should().NotBeNull();
        reopenedShape.Effects.Reflection!.BlurRadEmu.Should().Be(12700);
        reopenedShape.Effects.Reflection.ScaleYPercent.Should().Be(-100);
        reopenedShape.Effects.Reflection.Align.Should().Be("bl");
        reopenedShape.Effects.Reflection.RotWithShape.Should().BeFalse();
    }

    // ── Fixture builders (mirrors freep/FreeP.App.Host.Tests/ModernObjectsRoundTripTests.cs) ──

    private static MemoryStream BuildPptxWithShapeXml(string shapeXml)
    {
        var basePres = new Presentation();
        basePres.Slides.Add(new Slide());
        var ms = new MemoryStream();
        PptxPackageWriter.Write(basePres, ms);
        ms.Position = 0;

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            const string slidePath = "ppt/slides/slide1.xml";
            var slideEntry = zip.GetEntry(slidePath)!;
            string slideXml;
            using (var sr = new StreamReader(slideEntry.Open())) slideXml = sr.ReadToEnd();
            var slideDoc = XDocument.Parse(slideXml);
            var spTree = slideDoc.Descendants().First(e => e.Name.LocalName == "spTree");
            spTree.Add(XElement.Parse(shapeXml));
            slideEntry.Delete();
            var newSlide = zip.CreateEntry(slidePath, CompressionLevel.Optimal);
            using (var sw = new StreamWriter(newSlide.Open())) slideDoc.Save(sw);
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream WritePptxToMemory(Presentation pres)
    {
        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        return ms;
    }

    private static XDocument ExtractSlide1Xml(MemoryStream pptxStream)
    {
        pptxStream.Position = 0;
        using var zip = new ZipArchive(pptxStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry("ppt/slides/slide1.xml")!;
        using var sr = new StreamReader(entry.Open());
        return XDocument.Parse(sr.ReadToEnd());
    }
}
