using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Free.Shared.Drawing;
using Free.Shared.Opc;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 1B+1C round-trip unit tests: write a Presentation → .pptx → read back → assert structural equality.
/// </summary>
public sealed class PptxRoundTripTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.PptxTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. Slide count and size
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_SlideCount_Preserved()
    {
        var pres = BuildTestPresentation();
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides.Should().HaveCount(2, "we wrote 2 slides");
    }

    [Fact]
    public void RoundTrip_ParagraphAlternateContent_UsesChoiceOrFallbackWithoutDroppingText()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Alternate paragraph text",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 914400,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph { Runs = { new Run { Text = "choice source" } } },
                    new Paragraph { Runs = { new Run { Text = "fallback source" } } },
                }
            }
        });

        var sourcePath = WriteToPptx(pres);
        using var patched = RewriteSlideXml(sourcePath, slideXml =>
        {
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
            XNamespace x99 = "urn:freex:test-extension";

            var paragraphs = slideXml.Descendants(a + "p")
                .Where(paragraph => paragraph.Descendants(a + "t")
                    .Any(text => text.Value is "choice source" or "fallback source"))
                .ToArray();
            paragraphs.Should().HaveCountGreaterThanOrEqualTo(2);

            Wrap(paragraphs[0], supportedChoice: true);
            Wrap(paragraphs[1], supportedChoice: false);
            slideXml.Root!.Add(new XAttribute(XNamespace.Xmlns + "x99", x99.NamespaceName));

            void Wrap(XElement paragraph, bool supportedChoice)
            {
                var sourceRun = paragraph.Elements(a + "r").First();
                var fallbackRun = new XElement(sourceRun);
                var choice = supportedChoice
                    ? new XElement(a + "r", new XElement(a + "t", "choice branch"))
                    : new XElement(x99 + "unsupported");

                sourceRun.Remove();
                paragraph.Add(new XElement(
                    mc + "AlternateContent",
                    new XElement(mc + "Choice", new XAttribute("Requires", "x99"), choice),
                    new XElement(mc + "Fallback", fallbackRun)));
            }

            return slideXml;
        });

        var reloaded = PptxPackageReader.Read(patched);
        var paragraphs = reloaded.Slides[0].Shapes.Single(shape => shape.Id == 42)
            .TextBody!.Paragraphs;

        paragraphs[0].Runs.Should().ContainSingle().Which.Text.Should().Be("choice branch");
        paragraphs[1].Runs.Should().ContainSingle().Which.Text.Should().Be("fallback source");
    }

    [Fact]
    public void RoundTrip_AuthoredPictureBullet_WritesAndReadsBuBlipMedia()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Picture Bullet Text",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = DrawingMlCoordinateUnits.EmuPerInch * 2,
            ExtentCyEmu = DrawingMlCoordinateUnits.EmuPerInch,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        BulletKind = BulletKind.Image,
                        BulletImage = new ImagePart
                        {
                            Bytes = [0x89, 0x50, 0x4E, 0x47],
                            ContentType = "image/png"
                        },
                        Runs = { new Run { Text = "Picture bullet" } }
                    }
                }
            }
        });

        var path = WriteToPptx(pres);

        using (var zip = ZipFile.OpenRead(path))
        {
            var slideXml = new StreamReader(zip.GetEntry("ppt/slides/slide1.xml")!.Open()).ReadToEnd();
            var relsXml = new StreamReader(zip.GetEntry("ppt/slides/_rels/slide1.xml.rels")!.Open()).ReadToEnd();
            slideXml.Should().Contain("buBlip");
            slideXml.Should().Contain("rIdBulletImg");
            relsXml.Should().Contain("relationships/image");
            zip.Entries.Any(entry => entry.FullName.StartsWith("ppt/media/slide1_bullet", StringComparison.Ordinal))
                .Should().BeTrue();
        }

        var reloaded = PptxPackageReader.Read(path);
        var paragraph = reloaded.Slides[0].Shapes.Single(s => s.Id == 42).TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Image);
        paragraph.BulletImage.Should().NotBeNull();
        paragraph.BulletImage!.ContentType.Should().Be("image/png");
        paragraph.BulletImage.Bytes.Should().Equal(0x89, 0x50, 0x4E, 0x47);
    }

    [Fact]
    public void RoundTrip_SlideSize_Preserved()
    {
        var pres = new Presentation { SlideSizeCxEmu = 9144000, SlideSizeCyEmu = 6858000 };
        pres.Slides.Add(new Slide());

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.SlideSizeCxEmu.Should().Be(9144000);
        reloaded.SlideSizeCyEmu.Should().Be(6858000);
    }

    [Fact]
    public void RoundTrip_RecordingMediaArtifactManifest_Preserved()
    {
        var pres = Presentation.CreateEmpty();
        var payload = System.Text.Encoding.UTF8.GetBytes("deterministic narration payload");
        var payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        pres.RecordingMediaArtifacts.Add(new PresentationRecordingMediaArtifact(
            PresentationRecordingMediaArtifactKind.NarrationAudio,
            SlideIndex: 0,
            SuggestedFileName: "slide-001-narration.m4a",
            ContentType: "audio/mp4",
            PackagePath: "ppt/media/recordings/slide-001-narration.m4a",
            ContentLengthBytes: payload.Length,
            ContentSha256: payloadHash,
            DurationMs: 2400,
            CapturedByHost: "Capture evidence",
            StatusText: "Capture evidence: Narration audio captured",
            PayloadBytes: payload));
        var captionPayload = System.Text.Encoding.UTF8.GetBytes("WEBVTT\r\n\r\n00:00:00.000 --> 00:00:02.400\r\nIntro narration captured.\r\n");
        var captionHash = Convert.ToHexString(SHA256.HashData(captionPayload)).ToLowerInvariant();
        pres.RecordingMediaArtifacts.Add(new PresentationRecordingMediaArtifact(
            PresentationRecordingMediaArtifactKind.NarrationCaption,
            SlideIndex: 0,
            SuggestedFileName: "slide-001-narration-captions.vtt",
            ContentType: "text/vtt",
            PackagePath: "ppt/media/recording-captions/slide-001-narration-captions.vtt",
            ContentLengthBytes: captionPayload.Length,
            ContentSha256: captionHash,
            DurationMs: 2400,
            CapturedByHost: "Capture evidence",
            StatusText: "Capture evidence: Narration captions authored",
            PayloadBytes: captionPayload));

        var path = WriteToPptx(pres);

        using (var archive = System.IO.Compression.ZipFile.OpenRead(path))
        {
            archive.GetEntry("ppt/media/recordingArtifacts.xml").Should().NotBeNull();
            var mediaEntry = archive.GetEntry("ppt/media/recordings/slide-001-narration.m4a");
            mediaEntry.Should().NotBeNull();
            mediaEntry!.Length.Should().Be(payload.Length);
            var captionEntry = archive.GetEntry("ppt/media/recording-captions/slide-001-narration-captions.vtt");
            captionEntry.Should().NotBeNull();
            captionEntry!.Length.Should().Be(captionPayload.Length);

            using var contentTypesStream = archive.GetEntry("[Content_Types].xml")!.Open();
            var contentTypes = XDocument.Load(contentTypesStream);
            var contentTypesNamespace = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
            var hasM4aContentType = contentTypes.Root!.Elements(contentTypesNamespace + "Default")
                .Any(element =>
                    string.Equals(element.Attribute("Extension")?.Value, "m4a", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(element.Attribute("ContentType")?.Value, "audio/mp4", StringComparison.OrdinalIgnoreCase));
            hasM4aContentType.Should().BeTrue();
            var hasVttContentType = contentTypes.Root!.Elements(contentTypesNamespace + "Default")
                .Any(element =>
                    string.Equals(element.Attribute("Extension")?.Value, "vtt", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(element.Attribute("ContentType")?.Value, "text/vtt", StringComparison.OrdinalIgnoreCase));
            hasVttContentType.Should().BeTrue();
        }

        var reloaded = PptxPackageReader.Read(path);

        reloaded.RecordingMediaArtifacts.Should().HaveCount(2);
        reloaded.RecordingMediaArtifacts.Should().BeEquivalentTo(pres.RecordingMediaArtifacts);
        reloaded.RecordingMediaArtifacts.Single(artifact => artifact.Kind == PresentationRecordingMediaArtifactKind.NarrationAudio)
            .PayloadBytes.Should().Equal(payload);
        reloaded.RecordingMediaArtifacts.Single(artifact => artifact.Kind == PresentationRecordingMediaArtifactKind.NarrationCaption)
            .PayloadBytes.Should().Equal(captionPayload);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. Shape anchor / kind
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Rectangle_AnchorAndKind()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 10,
            Name = "Rect1",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "Rect1");
        s.Kind.Should().Be(SlideShapeKind.AutoShape);
        s.AutoShapeKind.Should().Be(DrawingShapeKind.Rectangle);
        s.OffsetXEmu.Should().Be(914400);
        s.OffsetYEmu.Should().Be(457200);
        s.ExtentCxEmu.Should().Be(2743200);
        s.ExtentCyEmu.Should().Be(1828800);
    }

    [Fact]
    public void RoundTrip_ShapeAlternativeTextMetadata_PreservedInPptxNonVisualProperties()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 11,
            Name = "Sales chart",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            AlternativeTextTitle = "Sales chart summary",
            AlternativeText = "Quarterly sales by region.",
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 12,
            Name = "Decorative divider",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Hyperlink = new Hyperlink
            {
                Url = "https://example.com/details",
                Tooltip = "Open details"
            },
            IsDecorative = true,
            OffsetXEmu = 914400,
            OffsetYEmu = 2743200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 228600
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);

        using (var archive = System.IO.Compression.ZipFile.OpenRead(path))
        using (var slideStream = archive.GetEntry("ppt/slides/slide1.xml")!.Open())
        using (var reader = new StreamReader(slideStream))
        {
            var slideXml = reader.ReadToEnd();
            slideXml.Should().Contain("title=\"Sales chart summary\"");
            slideXml.Should().Contain("descr=\"Quarterly sales by region.\"");
            slideXml.Should().Contain("adec:decorative");
            slideXml.Should().Contain("val=\"1\"");
            slideXml.IndexOf("<a:hlinkClick", StringComparison.Ordinal)
                .Should().BeLessThan(slideXml.IndexOf("<a:extLst>", StringComparison.Ordinal));
        }

        var reloaded = PptxPackageReader.Read(path);
        var salesChart = reloaded.Slides[0].Shapes.Single(shape => shape.Id == 11);
        salesChart.AlternativeTextTitle.Should().Be("Sales chart summary");
        salesChart.AlternativeText.Should().Be("Quarterly sales by region.");
        salesChart.IsDecorative.Should().BeFalse();
        var decorative = reloaded.Slides[0].Shapes.Single(shape => shape.Id == 12);
        decorative.IsDecorative.Should().BeTrue();
        decorative.AlternativeTextTitle.Should().BeEmpty();
        decorative.AlternativeText.Should().BeEmpty();
    }

    [Fact]
    public void RoundTrip_VariousShapeKinds()
    {
        var kinds = new[]
        {
            DrawingShapeKind.Ellipse,
            DrawingShapeKind.Triangle,
            DrawingShapeKind.Diamond,
            DrawingShapeKind.RoundedRectangle,
            DrawingShapeKind.Chevron,
            DrawingShapeKind.Pentagon,
            DrawingShapeKind.Hexagon,
            DrawingShapeKind.Star5,
            DrawingShapeKind.RightTriangle,
            DrawingShapeKind.MinusSign,
            DrawingShapeKind.MultiplySign,
            DrawingShapeKind.DivideSign,
            DrawingShapeKind.EqualSign,
            DrawingShapeKind.NotEqualSign,
            DrawingShapeKind.Wave,
            DrawingShapeKind.RectangularCallout,
            DrawingShapeKind.RoundedRectangularCallout,
            DrawingShapeKind.OvalCallout,
            DrawingShapeKind.Explosion,
            DrawingShapeKind.Ribbon,
            DrawingShapeKind.FlowchartProcess,
            DrawingShapeKind.FlowchartDecision,
            DrawingShapeKind.FlowchartData,
            DrawingShapeKind.FlowchartPredefinedProcess,
            DrawingShapeKind.FlowchartDocument,
            DrawingShapeKind.FlowchartTerminator,
            DrawingShapeKind.LineCallout,
            DrawingShapeKind.Cylinder,
            DrawingShapeKind.Chord,
            DrawingShapeKind.Heart
        };

        var pres = new Presentation();
        var slide = new Slide();
        uint id = 1;
        foreach (var k in kinds)
        {
            slide.Shapes.Add(new SlideShape
            {
                Id = id++,
                Name = k.ToString(),
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = k,
                ExtentCxEmu = 914400,
                ExtentCyEmu = 914400
            });
        }
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        foreach (var k in kinds)
        {
            var shape = reloaded.Slides[0].Shapes.FirstOrDefault(s => s.Name == k.ToString());
            shape.Should().NotBeNull($"shape {k} should survive round-trip");
            shape!.AutoShapeKind.Should().Be(k, $"kind {k} should be preserved");
        }
    }

    [Fact]
    public void RoundTrip_RightArrow_PreservesAuthoredAdjustmentGuides()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var arrow = new SlideShape
        {
            Id = 1,
            Name = "Adjusted right arrow",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RightArrow,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
        };
        arrow.PresetGeometryAdjustments["adj1"] = 18553;
        arrow.PresetGeometryAdjustments["adj2"] = 81447;
        slide.Shapes.Add(arrow);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);
        var reloadedArrow = reloaded.Slides[0].Shapes.Single(shape => shape.Id == 1);

        reloadedArrow.AutoShapeKind.Should().Be(DrawingShapeKind.RightArrow);
        reloadedArrow.PresetGeometryAdjustments["adj1"].Should().Be(18553);
        reloadedArrow.PresetGeometryAdjustments["adj2"].Should().Be(81447);
    }

    [Fact]
    public void RoundTrip_ChevronAndHomePlate_PreserveAuthoredPointDepth()
    {
        var pres = new Presentation();
        var slide = new Slide();
        uint id = 1;
        foreach (var kind in new[] { DrawingShapeKind.Chevron, DrawingShapeKind.HomePlate })
        {
            var shape = new SlideShape
            {
                Id = id++,
                Name = kind.ToString(),
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = kind,
                ExtentCxEmu = 914400,
                ExtentCyEmu = 457200,
            };
            shape.PresetGeometryAdjustments["adj"] = 75000;
            slide.Shapes.Add(shape);
        }
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        foreach (var kind in new[] { DrawingShapeKind.Chevron, DrawingShapeKind.HomePlate })
        {
            var shape = reloaded.Slides[0].Shapes.Single(candidate => candidate.Name == kind.ToString());
            shape.AutoShapeKind.Should().Be(kind);
            shape.PresetGeometryAdjustments["adj"].Should().Be(75000);
        }
    }

    [Fact]
    public void RoundTrip_CrossFamily_PreservesAuthoredBarInset()
    {
        var pres = new Presentation();
        var slide = new Slide();
        uint id = 1;
        foreach (var kind in new[] { DrawingShapeKind.Cross, DrawingShapeKind.PlusSign })
        {
            var shape = new SlideShape
            {
                Id = id++,
                Name = kind.ToString(),
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = kind,
                ExtentCxEmu = 914400,
                ExtentCyEmu = 457200,
            };
            shape.PresetGeometryAdjustments["adj"] = 45000;
            slide.Shapes.Add(shape);
        }
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        foreach (var kind in new[] { DrawingShapeKind.Cross, DrawingShapeKind.PlusSign })
        {
            var shape = reloaded.Slides[0].Shapes.Single(candidate => candidate.Name == kind.ToString());
            shape.AutoShapeKind.Should().Be(kind);
            shape.PresetGeometryAdjustments["adj"].Should().Be(45000);
        }
    }

    [Fact]
    public void RoundTrip_TrapezoidAndParallelogram_PreserveAuthoredSlant()
    {
        var pres = new Presentation();
        var slide = new Slide();
        uint id = 1;
        foreach (var kind in new[] { DrawingShapeKind.Trapezoid, DrawingShapeKind.Parallelogram })
        {
            var shape = new SlideShape
            {
                Id = id++,
                Name = kind.ToString(),
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = kind,
                ExtentCxEmu = 914400,
                ExtentCyEmu = 457200,
            };
            shape.PresetGeometryAdjustments["adj"] = 65000;
            slide.Shapes.Add(shape);
        }
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        foreach (var kind in new[] { DrawingShapeKind.Trapezoid, DrawingShapeKind.Parallelogram })
        {
            var shape = reloaded.Slides[0].Shapes.Single(candidate => candidate.Name == kind.ToString());
            shape.AutoShapeKind.Should().Be(kind);
            shape.PresetGeometryAdjustments["adj"].Should().Be(65000);
        }
    }

    [Fact]
    public void RoundTrip_Star5_PreservesAuthoredPointDepth()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var star = new SlideShape
        {
            Id = 1,
            Name = "Adjusted star",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Star5,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
        };
        star.PresetGeometryAdjustments["adj"] = 72000;
        slide.Shapes.Add(star);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);
        var reloadedStar = reloaded.Slides[0].Shapes.Single(shape => shape.Id == 1);

        reloadedStar.AutoShapeKind.Should().Be(DrawingShapeKind.Star5);
        reloadedStar.PresetGeometryAdjustments["adj"].Should().Be(72000);
    }

    [Fact]
    public void RoundTrip_Star8_PreservesAuthoredPointDepth()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var star = new SlideShape
        {
            Id = 1,
            Name = "Adjusted eight-point star",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Star8,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
        };
        star.PresetGeometryAdjustments["adj"] = 72000;
        slide.Shapes.Add(star);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);
        var reloadedStar = reloaded.Slides[0].Shapes.Single(shape => shape.Id == 1);

        reloadedStar.AutoShapeKind.Should().Be(DrawingShapeKind.Star8);
        reloadedStar.PresetGeometryAdjustments["adj"].Should().Be(72000);
    }

    [Fact]
    public void RoundTrip_Explosion_PreservesAuthoredSpikeDepth()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var explosion = new SlideShape
        {
            Id = 1,
            Name = "Adjusted explosion",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Explosion,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
        };
        explosion.PresetGeometryAdjustments["adj"] = 82000;
        slide.Shapes.Add(explosion);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);
        var reloadedExplosion = reloaded.Slides[0].Shapes.Single(shape => shape.Id == 1);

        reloadedExplosion.AutoShapeKind.Should().Be(DrawingShapeKind.Explosion);
        reloadedExplosion.PresetGeometryAdjustments["adj"].Should().Be(82000);
    }

    [Fact]
    public void RoundTrip_CompoundArrows_PreserveAuthoredAdjustmentGuides()
    {
        var pres = new Presentation();
        var slide = new Slide();
        uint id = 1;
        foreach (var kind in new[] { DrawingShapeKind.LeftRightArrow, DrawingShapeKind.UpDownArrow })
        {
            var shape = new SlideShape
            {
                Id = id++,
                Name = kind.ToString(),
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = kind,
                ExtentCxEmu = 914400,
                ExtentCyEmu = 457200,
            };
            shape.PresetGeometryAdjustments["adj1"] = 25000;
            shape.PresetGeometryAdjustments["adj2"] = 75000;
            slide.Shapes.Add(shape);
        }
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        foreach (var kind in new[] { DrawingShapeKind.LeftRightArrow, DrawingShapeKind.UpDownArrow })
        {
            var shape = reloaded.Slides[0].Shapes.Single(candidate => candidate.Name == kind.ToString());
            shape.AutoShapeKind.Should().Be(kind);
            shape.PresetGeometryAdjustments["adj1"].Should().Be(25000);
            shape.PresetGeometryAdjustments["adj2"].Should().Be(75000);
        }
    }

    [Fact]
    public void RoundTrip_Rotation_And_Flip()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "RotShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            RotationDeg = 45.0,
            FlipH = true,
            FlipV = false,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "RotShape");
        s.RotationDeg.Should().BeApproximately(45.0, 0.001);
        s.FlipH.Should().BeTrue();
        s.FlipV.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. Fill round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_SolidFill_SrgbColor()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "FilledRect",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = new ShapeFill.Solid(new SrgbColor(0x44, 0x72, 0xC4)), // accent1 blue
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "FilledRect");
        s.Fill.Should().BeOfType<ShapeFill.Solid>();
        var solid = (ShapeFill.Solid)s.Fill!;
        solid.Color.Resolved.R.Should().Be(0x44);
        solid.Color.Resolved.G.Should().Be(0x72);
        solid.Color.Resolved.B.Should().Be(0xC4);
    }

    [Fact]
    public void RoundTrip_SolidFill_SchemeColor()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var schemeRef = new SchemeColorRef { Slot = ThemeColorSlot.Accent1, LumMod = 0.75, LumOff = 0.0 };
        var color = new ThemeAwareColor(SrgbColor.FromRgb(0x305496), schemeRef);

        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "SchemeShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ellipse,
            Fill = new ShapeFill.Solid(color),
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "SchemeShape");
        s.Fill.Should().BeOfType<ShapeFill.Solid>();
        var solid = (ShapeFill.Solid)s.Fill!;
        solid.Color.SchemeColor.Should().NotBeNull("scheme color ref should be preserved");
        solid.Color.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent1);
        solid.Color.SchemeColor.LumMod.Should().BeApproximately(0.75, 0.0001);
    }

    [Fact]
    public void RoundTrip_SolidFillAndOutlineAlpha_Preserved()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "TransparentShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4), alpha: 128)),
            Outline = new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0xC00000), alpha: 64), 1.5),
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        using (var zip = ZipFile.OpenRead(path))
        {
            var slideXml = new StreamReader(zip.GetEntry("ppt/slides/slide1.xml")!.Open()).ReadToEnd();
            slideXml.Should().Contain("<a:alpha val=\"50196\"");
            slideXml.Should().Contain("<a:alpha val=\"25098\"");
        }

        var reloaded = PptxPackageReader.Read(path);
        var shape = reloaded.Slides[0].Shapes.First(x => x.Name == "TransparentShape");
        var fill = shape.Fill.Should().BeOfType<ShapeFill.Solid>().Subject;
        var outline = shape.Outline.Should().BeOfType<ShapeOutline.Visible>().Subject;

        fill.Color.Alpha.Should().Be(128);
        outline.Color.Alpha.Should().Be(64);
    }

    [Fact]
    public void RoundTrip_NoFill()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "NoFillShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = ShapeFill.None.Instance,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "NoFillShape");
        s.Fill.Should().BeOfType<ShapeFill.None>();
    }

    [Fact]
    public void RoundTrip_GradientFill()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "GradShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = new ShapeFill.Gradient(
                new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
                new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
                angleDegrees: 90.0),
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "GradShape");
        s.Fill.Should().BeOfType<ShapeFill.Gradient>();
        var grad = (ShapeFill.Gradient)s.Fill!;
        grad.StartColor.Resolved.R.Should().Be(0xFF);
        grad.EndColor.Resolved.B.Should().Be(0xFF);
        grad.AngleDegrees.Should().BeApproximately(90.0, 0.1);
    }

    [Fact]
    public void RoundTrip_MultiStopGradientFill_ThreeStops()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var stops = new[]
        {
            new GradientStop(0.0,  new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00))),
            new GradientStop(0.5,  new ThemeAwareColor(new SrgbColor(0x00, 0xFF, 0x00))),
            new GradientStop(1.0,  new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF))),
        };
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "Grad3Shape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = new ShapeFill.Gradient(stops, GradientKind.Linear, angleDegrees: 45.0),
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "Grad3Shape");
        s.Fill.Should().BeOfType<ShapeFill.Gradient>();
        var grad = (ShapeFill.Gradient)s.Fill!;
        grad.Stops.Should().HaveCount(3, "all 3 stops must survive round-trip");
        grad.Kind.Should().Be(GradientKind.Linear);
        grad.AngleDegrees.Should().BeApproximately(45.0, 0.1);
        grad.Stops[0].Color.Resolved.R.Should().Be(0xFF);
        grad.Stops[1].Color.Resolved.G.Should().Be(0xFF);
        grad.Stops[2].Color.Resolved.B.Should().Be(0xFF);
        grad.Stops[0].Position.Should().BeApproximately(0.0, 0.001);
        grad.Stops[1].Position.Should().BeApproximately(0.5, 0.001);
        grad.Stops[2].Position.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void RoundTrip_RadialGradientFill()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var stops = new[]
        {
            new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0xFF, 0xFF, 0xFF))),
            new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0x00))),
        };
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "RadialShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ellipse,
            Fill = new ShapeFill.Gradient(stops, GradientKind.Radial, angleDegrees: 0.0),
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "RadialShape");
        s.Fill.Should().BeOfType<ShapeFill.Gradient>();
        var grad = (ShapeFill.Gradient)s.Fill!;
        grad.Kind.Should().Be(GradientKind.Radial);
        grad.Stops.Should().HaveCount(2);
    }

    [Fact]
    public void RoundTrip_PictureFill()
    {
        // Minimal 1x1 PNG (89 bytes)
        var pngBytes = new byte[]
        {
            0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A, // PNG signature
            0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52, // IHDR chunk
            0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
            0x08,0x02,0x00,0x00,0x00,0x90,0x77,0x53,
            0xDE,0x00,0x00,0x00,0x0C,0x49,0x44,0x41, // IDAT chunk
            0x54,0x08,0xD7,0x63,0xF8,0xCF,0xC0,0x00,
            0x00,0x00,0x02,0x00,0x01,0xE2,0x21,0xBC,
            0x33,0x00,0x00,0x00,0x00,0x49,0x45,0x4E, // IEND chunk
            0x44,0xAE,0x42,0x60,0x82
        };
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "PicFillShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = new ShapeFill.Picture(pngBytes, "image/png", tile: false),
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "PicFillShape");
        s.Fill.Should().BeOfType<ShapeFill.Picture>("picture fill must survive round-trip");
        var pic = (ShapeFill.Picture)s.Fill!;
        pic.ImageBytes.Should().NotBeEmpty("image bytes must be preserved");
        pic.Tile.Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_PatternFill()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "PatternShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = new ShapeFill.Pattern(
                preset: "diagStripe",
                foregroundColor: new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
                backgroundColor: new ThemeAwareColor(new SrgbColor(0xFF, 0xFF, 0xFF))),
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "PatternShape");
        s.Fill.Should().BeOfType<ShapeFill.Pattern>("pattern fill must survive round-trip");
        var pat = (ShapeFill.Pattern)s.Fill!;
        pat.Preset.Should().Be("diagStripe");
        pat.ForegroundColor.Resolved.B.Should().Be(0xFF);
        pat.BackgroundColor.Resolved.R.Should().Be(0xFF);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4. Outline round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Outline_Visible()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "OutlineShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Outline = new ShapeOutline.Visible(
                new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
                widthPt: 2.0,
                dash: OutlineDash.Dash),
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "OutlineShape");
        s.Outline.Should().BeOfType<ShapeOutline.Visible>();
        var vis = (ShapeOutline.Visible)s.Outline!;
        vis.WidthPt.Should().BeApproximately(2.0, 0.01);
        vis.Dash.Should().Be(OutlineDash.Dash);
        vis.Color.Resolved.R.Should().Be(0xFF);
    }

    [Fact]
    public void RoundTrip_ConnectorTriangleLineEnds_WritesAndReadsVisibleOutline()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Triangle Arrow Connector",
            Kind = SlideShapeKind.Connector,
            AutoShapeKind = DrawingShapeKind.Line,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
            Outline = new ShapeOutline.Visible(
                new SrgbColor(0xC0, 0x00, 0x00),
                widthPt: 2.25,
                beginLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle),
                endLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle))
        });

        var path = WriteToPptx(pres);

        using (var archive = ZipFile.OpenRead(path))
        using (var slideStream = archive.GetEntry("ppt/slides/slide1.xml")!.Open())
        {
            var doc = XDocument.Load(slideStream);
            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var connector = doc.Descendants(p + "cxnSp")
                .Single(sp => sp.Descendants(p + "cNvPr")
                    .Any(c => c.Attribute("name")?.Value == "Triangle Arrow Connector"));
            var line = connector.Element(p + "spPr")!.Element(a + "ln")!;

            line.Element(a + "headEnd")!.Attribute("type")!.Value.Should().Be("triangle");
            line.Element(a + "tailEnd")!.Attribute("type")!.Value.Should().Be("triangle");
        }

        var reloaded = PptxPackageReader.Read(path);
        var outline = reloaded.Slides[0].Shapes.Single(shape => shape.Name == "Triangle Arrow Connector")
            .Outline.Should().BeOfType<ShapeOutline.Visible>().Subject;
        outline.BeginLineEnd.Should().Be(new ShapeLineEnd(ShapeLineEndKind.Triangle));
        outline.EndLineEnd.Should().Be(new ShapeLineEnd(ShapeLineEndKind.Triangle));
    }

    [Fact]
    public void RoundTrip_LineTriangleLineEnds_WritesAndReadsGradientOutline()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 43,
            Name = "Gradient Triangle Line",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Line,
            OffsetXEmu = 914400,
            OffsetYEmu = 1828800,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
            Outline = new ShapeOutline.GradientVisible(
                new ShapeFill.Gradient(
                    new ThemeAwareColor(new SrgbColor(0x10, 0x20, 0x30)),
                    new ThemeAwareColor(new SrgbColor(0xD0, 0xE0, 0xF0))),
                widthPt: 3.0,
                beginLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle),
                endLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle))
        });

        var path = WriteToPptx(pres);

        using (var archive = ZipFile.OpenRead(path))
        using (var slideStream = archive.GetEntry("ppt/slides/slide1.xml")!.Open())
        {
            var doc = XDocument.Load(slideStream);
            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var shape = doc.Descendants(p + "sp")
                .Single(sp => sp.Descendants(p + "cNvPr")
                    .Any(c => c.Attribute("name")?.Value == "Gradient Triangle Line"));
            var line = shape.Element(p + "spPr")!.Element(a + "ln")!;

            line.Element(a + "gradFill").Should().NotBeNull();
            line.Element(a + "headEnd")!.Attribute("type")!.Value.Should().Be("triangle");
            line.Element(a + "tailEnd")!.Attribute("type")!.Value.Should().Be("triangle");
        }

        var reloaded = PptxPackageReader.Read(path);
        var outline = reloaded.Slides[0].Shapes.Single(shape => shape.Name == "Gradient Triangle Line")
            .Outline.Should().BeOfType<ShapeOutline.GradientVisible>().Subject;
        outline.BeginLineEnd.Should().Be(new ShapeLineEnd(ShapeLineEndKind.Triangle));
        outline.EndLineEnd.Should().Be(new ShapeLineEnd(ShapeLineEndKind.Triangle));
    }

    [Fact]
    public void RoundTrip_Outline_None()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "NoOutline",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Outline = ShapeOutline.None.Instance,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "NoOutline");
        s.Outline.Should().BeOfType<ShapeOutline.None>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 5. Text / TextBody round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_TextBody_TwoRuns()
    {
        var pres = new Presentation();
        var slide = new Slide();

        var shape = new SlideShape
        {
            Id = 1, Name = "TextShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 3048000, ExtentCyEmu = 1524000
        };

        var body = new TextBody { Anchor = VerticalAnchor.Middle };
        var para = new Paragraph { Align = TextAlign.Center };
        para.Runs.Add(new Run
        {
            Text = "Hello",
            Bold = true,
            FontSizePt = 24.0,
            FontFamily = "Arial",
            Color = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00))
        });
        para.Runs.Add(new Run
        {
            Text = " World",
            Italic = true,
            FontSizePt = 18.0
        });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "TextShape");
        s.TextBody.Should().NotBeNull();
        s.TextBody!.Anchor.Should().Be(VerticalAnchor.Middle);
        s.TextBody.Paragraphs.Should().HaveCount(1);

        var p0 = s.TextBody.Paragraphs[0];
        p0.Align.Should().Be(TextAlign.Center);
        p0.Runs.Should().HaveCount(2);

        var r0 = p0.Runs[0];
        r0.Text.Should().Be("Hello");
        r0.Bold.Should().BeTrue();
        r0.FontSizePt.Should().BeApproximately(24.0, 0.01);
        r0.FontFamily.Should().Be("Arial");
        r0.Color.Should().NotBeNull();
        r0.Color!.Resolved.R.Should().Be(0xFF);

        var r1 = p0.Runs[1];
        r1.Text.Should().Be(" World");
        r1.Italic.Should().BeTrue();
        r1.FontSizePt.Should().BeApproximately(18.0, 0.01);
    }

    [Fact]
    public void RoundTrip_PlaceholderShape()
    {
        var pres = new Presentation();
        var slide = new Slide();

        var titleShape = new SlideShape
        {
            Id = 1, Name = "Title 1",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            ExtentCxEmu = 8229600, ExtentCyEmu = 1143000
        };
        titleShape.Text = "My Title";
        slide.Shapes.Add(titleShape);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.FirstOrDefault(x => x.Placeholder?.Type == PlaceholderType.Title);
        s.Should().NotBeNull("title placeholder should survive round-trip");
        s!.PlainText.Should().Be("My Title");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 6. Picture round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Picture_BytesPreserved()
    {
        // Minimal valid 1×1 PNG
        var pngBytes = CreateMinimalPng();

        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "Pic1",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = pngBytes, ContentType = "image/png" },
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.FirstOrDefault(x => x.Kind == SlideShapeKind.Picture);
        s.Should().NotBeNull("picture shape should survive");
        s!.Picture.Should().NotBeNull();
        s.Picture!.Bytes.Should().BeEquivalentTo(pngBytes, "image bytes must be preserved exactly");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 7. Theme round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ThemeColors()
    {
        var pres = new Presentation();
        pres.Theme.Name = "TestTheme";
        pres.Theme.ColorScheme[ThemeColorSlot.Accent1] = new SrgbColor(0x12, 0x34, 0x56);
        pres.Theme.ColorScheme[ThemeColorSlot.Accent6] = new SrgbColor(0xAB, 0xCD, 0xEF);
        pres.Theme.FontScheme.MajorLatinFont = "Trebuchet MS";
        pres.Theme.FontScheme.MinorLatinFont = "Georgia";
        pres.Slides.Add(new Slide());

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Theme.ColorScheme[ThemeColorSlot.Accent1].R.Should().Be(0x12);
        reloaded.Theme.ColorScheme[ThemeColorSlot.Accent1].G.Should().Be(0x34);
        reloaded.Theme.ColorScheme[ThemeColorSlot.Accent1].B.Should().Be(0x56);
        reloaded.Theme.ColorScheme[ThemeColorSlot.Accent6].R.Should().Be(0xAB);
        reloaded.Theme.FontScheme.MajorLatinFont.Should().Be("Trebuchet MS");
        reloaded.Theme.FontScheme.MinorLatinFont.Should().Be("Georgia");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 8. Core properties round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_CoreProperties()
    {
        var pres = new Presentation();
        pres.Properties.Title = "Q3 Review";
        pres.Properties.Author = "Jane Smith";
        pres.Properties.Subject = "Finance";
        pres.Properties.Keywords = "quarterly, revenue";
        pres.Slides.Add(new Slide());

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Properties.Title.Should().Be("Q3 Review");
        reloaded.Properties.Author.Should().Be("Jane Smith");
        reloaded.Properties.Subject.Should().Be("Finance");
        reloaded.Properties.Keywords.Should().Be("quarterly, revenue");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 9. Connector shape
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ConnectorShape()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "Conn1",
            Kind = SlideShapeKind.Connector,
            AutoShapeKind = DrawingShapeKind.ElbowConnector,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 1828800, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "Conn1");
        s.Kind.Should().Be(SlideShapeKind.Connector);
        s.AutoShapeKind.Should().Be(DrawingShapeKind.ElbowConnector);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 10. Full composite round-trip (kitchen sink)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_KitchenSink_FullPresentation()
    {
        var pres = BuildTestPresentation();
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides.Should().HaveCount(2);
        reloaded.SlideSizeCxEmu.Should().Be(pres.SlideSizeCxEmu);
        reloaded.SlideSizeCyEmu.Should().Be(pres.SlideSizeCyEmu);

        // Slide 0: title + rect with solid fill
        var slide0 = reloaded.Slides[0];
        slide0.Shapes.Should().NotBeEmpty();
        var rectShape = slide0.Shapes.FirstOrDefault(s => s.Name == "TheRect");
        rectShape.Should().NotBeNull("the rectangle shape should survive");
        rectShape!.AutoShapeKind.Should().Be(DrawingShapeKind.Rectangle);
        rectShape.Fill.Should().BeOfType<ShapeFill.Solid>();

        // Slide 1: textbox
        var slide1 = reloaded.Slides[1];
        var textShape = slide1.Shapes.FirstOrDefault(s => s.Name == "TheText");
        textShape.Should().NotBeNull();
        textShape!.TextBody.Should().NotBeNull();
        textShape.TextBody!.Paragraphs.Should().HaveCount(1);
        textShape.TextBody.Paragraphs[0].Runs[0].Text.Should().Be("Bold run");
        textShape.TextBody.Paragraphs[0].Runs[1].Text.Should().Be(" normal run");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 11. .pptx file is a valid zip (PowerPoint-openable)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Written_Pptx_IsValidZip()
    {
        var pres = BuildTestPresentation();
        var path = WriteToPptx(pres);

        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        archive.Entries.Should().Contain(e => e.FullName == "[Content_Types].xml",
            "every valid .pptx must contain [Content_Types].xml");
        archive.Entries.Should().Contain(e => e.FullName == "ppt/presentation.xml",
            "every valid .pptx must contain ppt/presentation.xml");
        archive.Entries.Should().Contain(e => e.FullName == "_rels/.rels");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_CustomShows_PreservedAndDanglingMembersOmitted()
    {
        var pres = new Presentation();
        pres.Slides.Add(new Slide { Id = "slide-a", Title = "Intro" });
        pres.Slides.Add(new Slide { Id = "slide-b", Title = "Deep dive" });
        pres.Slides.Add(new Slide { Id = "slide-c", Title = "Appendix" });

        var customShow = new PresentationCustomShow { Id = 7, Name = "Executive review" };
        customShow.SlideIds.Add("slide-c");
        customShow.SlideIds.Add("missing-slide");
        customShow.SlideIds.Add("slide-a");
        pres.CustomShows.Add(customShow);

        var path = WriteToPptx(pres);

        using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
        using (var presStream = zip.GetEntry("ppt/presentation.xml")!.Open())
        {
            var presXml = System.Xml.Linq.XDocument.Load(presStream);
            var P = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
            var R = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");

            var assignedRelIds = presXml.Descendants(P + "sldId")
                .Select(el => el.Attribute(R + "id")?.Value)
                .Where(value => value is not null)
                .ToHashSet();

            var customShowEl = presXml.Descendants(P + "custShow").Single();
            customShowEl.Attribute("name")?.Value.Should().Be("Executive review");
            customShowEl.Attribute("id")?.Value.Should().Be("7");

            var customShowSlideRelIds = customShowEl.Descendants(P + "sld")
                .Select(el => el.Attribute(R + "id")?.Value)
                .Where(value => value is not null)
                .ToList();

            customShowSlideRelIds.Should().HaveCount(2, "dangling custom-show slide members must be skipped");
            customShowSlideRelIds.Should().OnlyContain(relId => assignedRelIds.Contains(relId));
        }

        var reloaded = PptxPackageReader.Read(path);
        reloaded.CustomShows.Should().ContainSingle();
        reloaded.CustomShows[0].Name.Should().Be("Executive review");
        reloaded.CustomShows[0].Id.Should().Be(7);
        reloaded.CustomShows[0].SlideIds.Should().HaveCount(2);

        var reloadedTitlesById = reloaded.Slides.ToDictionary(slide => slide.Id, slide => slide.Title);
        reloaded.CustomShows[0].SlideIds.Select(id => reloadedTitlesById[id])
            .Should().Equal("Appendix", "Intro");
    }

    [Fact]
    public void Read_CustomShows_TranslatesNumericSlideIdsToSlideIds()
    {
        var pres = new Presentation();
        pres.Slides.Add(new Slide { Title = "Intro" });
        pres.Slides.Add(new Slide { Title = "Deep dive" });
        pres.Slides.Add(new Slide { Title = "Appendix" });
        var path = WriteToPptx(pres);

        using var patched = RewritePresentationXml(path, presXml =>
        {
            var P = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
            var notesSz = presXml.Root!.Element(P + "notesSz");
            notesSz!.AddAfterSelf(
                new System.Xml.Linq.XElement(P + "custShowLst",
                    new System.Xml.Linq.XElement(P + "custShow",
                        new System.Xml.Linq.XAttribute("name", "Numeric route"),
                        new System.Xml.Linq.XAttribute("id", "3"),
                        new System.Xml.Linq.XElement(P + "sldLst",
                            new System.Xml.Linq.XElement(P + "sld", new System.Xml.Linq.XAttribute("id", "258")),
                            new System.Xml.Linq.XElement(P + "sld", new System.Xml.Linq.XAttribute("id", "256")),
                            new System.Xml.Linq.XElement(P + "sld", new System.Xml.Linq.XAttribute("id", "999"))))));
            return presXml;
        });

        var reloaded = PptxPackageReader.Read(patched);

        reloaded.CustomShows.Should().ContainSingle();
        reloaded.CustomShows[0].Name.Should().Be("Numeric route");
        reloaded.CustomShows[0].Id.Should().Be(3);
        reloaded.CustomShows[0].SlideIds.Should().Equal(
            reloaded.Slides[2].Id,
            reloaded.Slides[0].Id);
    }

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    private static MemoryStream RewritePresentationXml(
        string path,
        Func<System.Xml.Linq.XDocument, System.Xml.Linq.XDocument> rewrite)
    {
        var destination = new MemoryStream();
        using (var sourceZip = System.IO.Compression.ZipFile.OpenRead(path))
        using (var destinationZip = new System.IO.Compression.ZipArchive(
            destination,
            System.IO.Compression.ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var entry in sourceZip.Entries)
            {
                var destinationEntry = destinationZip.CreateEntry(
                    entry.FullName,
                    System.IO.Compression.CompressionLevel.Fastest);
                using var source = entry.Open();
                using var target = destinationEntry.Open();

                if (entry.FullName == "ppt/presentation.xml")
                {
                    var presXml = System.Xml.Linq.XDocument.Load(source);
                    var rewritten = rewrite(presXml);
                    rewritten.Save(target, System.Xml.Linq.SaveOptions.DisableFormatting);
                }
                else
                {
                    source.CopyTo(target);
                }
            }
        }

        destination.Position = 0;
        return destination;
    }

    private static MemoryStream RewriteSlideXml(
        string path,
        Func<System.Xml.Linq.XDocument, System.Xml.Linq.XDocument> rewrite)
    {
        var destination = new MemoryStream();
        using (var sourceZip = System.IO.Compression.ZipFile.OpenRead(path))
        using (var destinationZip = new System.IO.Compression.ZipArchive(
            destination,
            System.IO.Compression.ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var entry in sourceZip.Entries)
            {
                var destinationEntry = destinationZip.CreateEntry(
                    entry.FullName,
                    System.IO.Compression.CompressionLevel.Fastest);
                using var source = entry.Open();
                using var target = destinationEntry.Open();

                if (entry.FullName == "ppt/slides/slide1.xml")
                {
                    var slideXml = System.Xml.Linq.XDocument.Load(source);
                    rewrite(slideXml).Save(target, System.Xml.Linq.SaveOptions.DisableFormatting);
                }
                else
                {
                    source.CopyTo(target);
                }
            }
        }

        destination.Position = 0;
        return destination;
    }

    private static Presentation BuildTestPresentation()
    {
        var pres = new Presentation
        {
            SlideSizeCxEmu = 12192000,
            SlideSizeCyEmu = 6858000
        };
        pres.Properties.Title = "Test Deck";
        pres.Properties.Author = "Test Author";

        // Slide 0: a rectangle with solid blue fill + dashed red outline
        var slide0 = new Slide();
        var rect = new SlideShape
        {
            Id = 2, Name = "TheRect",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1828800,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x44, 0x72, 0xC4))),
            Outline = new ShapeOutline.Visible(
                new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
                widthPt: 1.5,
                dash: OutlineDash.Dash)
        };
        slide0.Shapes.Add(rect);
        pres.Slides.Add(slide0);

        // Slide 1: a textbox with two runs
        var slide1 = new Slide();
        var textShape = new SlideShape
        {
            Id = 3, Name = "TheText",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 457200,
            ExtentCxEmu = 5486400, ExtentCyEmu = 1828800,
            Fill = ShapeFill.None.Instance
        };
        var body = new TextBody { Anchor = VerticalAnchor.Top };
        var para = new Paragraph { Align = TextAlign.Left };
        para.Runs.Add(new Run { Text = "Bold run", Bold = true, FontSizePt = 20.0 });
        para.Runs.Add(new Run { Text = " normal run", FontSizePt = 16.0 });
        body.Paragraphs.Add(para);
        textShape.TextBody = body;
        slide1.Shapes.Add(textShape);
        pres.Slides.Add(slide1);

        return pres;
    }

    /// <summary>Creates a minimal valid 1×1 white PNG (67 bytes).</summary>
    private static byte[] CreateMinimalPng()
    {
        // PNG signature + IHDR + IDAT (1x1 white pixel) + IEND
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    }

    // -------------------------------------------------------------------------
    // Table round-trip tests
    // -------------------------------------------------------------------------

    [Fact]
    public void RoundTrip_TableShape_ColsRowsCellsPreserved()
    {
        // Arrange: 2-col x 2-row table with header row flag.
        var pres = new Presentation();
        var slide = new Slide();

        var table = new TableShape
        {
            TableStyleId = "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}"
        };
        table.Flags.FirstRow = true;
        table.Flags.BandRow  = true;
        table.ColumnWidthsEmu.Add(2743200L);  // ~288 DIP
        table.ColumnWidthsEmu.Add(2743200L);

        var row0 = new TableRow { HeightEmu = 685800L };
        row0.Cells.Add(new TableCell
        {
            TextBody = MakeBody("Header A")
        });
        row0.Cells.Add(new TableCell
        {
            TextBody = MakeBody("Header B")
        });
        table.Rows.Add(row0);

        var row1 = new TableRow { HeightEmu = 685800L };
        row1.Cells.Add(new TableCell { TextBody = MakeBody("Cell 1") });
        row1.Cells.Add(new TableCell { TextBody = MakeBody("Cell 2"), GridSpan = 1 });
        table.Rows.Add(row1);

        var shape = new SlideShape
        {
            Id = 10, Name = "Table 1",
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 457200, OffsetYEmu = 457200,
            ExtentCxEmu = 5486400, ExtentCyEmu = 1371600,
            Table = table
        };
        slide.Shapes.Add(shape);
        pres.Slides.Add(slide);

        // Act: write → read
        var path = Path.Combine(_tempDir, "table-rt.pptx");
        PptxPackageWriter.Write(pres, path);
        var read = PptxPackageReader.Read(path);

        // Assert: shape present
        var readSlide = read.Slides[0];
        var tableShape = readSlide.Shapes.SingleOrDefault(s => s.Kind == SlideShapeKind.Table);
        tableShape.Should().NotBeNull("table shape should survive round-trip");
        tableShape!.Table.Should().NotBeNull();

        var rt = tableShape.Table!;
        rt.ColumnWidthsEmu.Should().HaveCount(2, "column count preserved");
        rt.Rows.Should().HaveCount(2, "row count preserved");
        rt.Flags.FirstRow.Should().BeTrue("FirstRow flag preserved");

        // Header text
        rt.Rows[0].Cells[0].TextBody?.Paragraphs[0].Runs[0].Text.Should().Be("Header A");
        rt.Rows[0].Cells[1].TextBody?.Paragraphs[0].Runs[0].Text.Should().Be("Header B");

        // Style ID preserved
        rt.TableStyleId.Should().Be("{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}");
    }

    [Fact]
    public void RoundTrip_TableMergedCell_SpanAttributesPreserved()
    {
        // Arrange: 3-col x 1-row table with a 2-column merged cell.
        var pres = new Presentation();
        var slide = new Slide();

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(2000000L);
        table.ColumnWidthsEmu.Add(2000000L);
        table.ColumnWidthsEmu.Add(2000000L);

        var row = new TableRow { HeightEmu = 685800L };
        row.Cells.Add(new TableCell { GridSpan = 2 });
        row.Cells.Add(new TableCell { HMerge = true });
        row.Cells.Add(new TableCell());
        table.Rows.Add(row);

        var shape = new SlideShape
        {
            Id = 11, Name = "Table 2",
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 457200, OffsetYEmu = 1000000,
            ExtentCxEmu = 6000000, ExtentCyEmu = 685800,
            Table = table
        };
        slide.Shapes.Add(shape);
        pres.Slides.Add(slide);

        // Act
        var path = Path.Combine(_tempDir, "table-merge.pptx");
        PptxPackageWriter.Write(pres, path);
        var read = PptxPackageReader.Read(path);

        // Assert
        var rt = read.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Table).Table!;
        rt.ColumnWidthsEmu.Should().HaveCount(3);
        rt.Rows[0].Cells[0].GridSpan.Should().Be(2, "gridSpan=2 preserved");
        rt.Rows[0].Cells[1].HMerge.Should().BeTrue("hMerge flag preserved");
        rt.Rows[0].Cells[2].HMerge.Should().BeFalse("last cell is not merged");
    }

    [Fact]
    public void RoundTrip_PointBasedDrawingMlCoordinateUnits_PreservedInPptxIo()
    {
        var pres = new Presentation();
        var slide = new Slide();

        var body = new TextBody
        {
            InsetLeftPt = 1.25,
            InsetRightPt = 2.5,
            InsetTopPt = 3.75,
            InsetBottomPt = 4.5
        };
        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text = "Units",
            TextOutline = new ShapeOutline.Visible(new SrgbColor(0x11, 0x22, 0x33), widthPt: 1.25),
            TextShadow = new RunTextShadow
            {
                Color = new ThemeAwareColor(new SrgbColor(0x44, 0x55, 0x66)),
                Alpha = 255,
                BlurPt = 1.5,
                DistPt = 2.25,
                DirDeg = 30
            }
        });
        body.Paragraphs.Add(para);

        slide.Shapes.Add(new SlideShape
        {
            Id = 21,
            Name = "UnitShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 914400,
            TextBody = body,
            Outline = new ShapeOutline.Visible(new SrgbColor(0x77, 0x88, 0x99), widthPt: 1.75)
        });

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(1828800);
        var row = new TableRow { HeightEmu = 685800 };
        row.Cells.Add(new TableCell
        {
            InsetLeftPt = 5.5,
            InsetRightPt = 6.25,
            InsetTopPt = 7.0,
            InsetBottomPt = 8.75,
            Borders = new TableCellBorders
            {
                Left = new ShapeOutline.Visible(new SrgbColor(0x10, 0x20, 0x30), widthPt: 0.5),
                Top = new ShapeOutline.GradientVisible(
                    new ShapeFill.Gradient(
                        new ThemeAwareColor(new SrgbColor(0x20, 0x30, 0x40)),
                        new ThemeAwareColor(new SrgbColor(0x60, 0x70, 0x80))),
                    widthPt: 1.25)
            },
            TextBody = MakeBody("Cell")
        });
        table.Rows.Add(row);
        slide.Shapes.Add(new SlideShape
        {
            Id = 22,
            Name = "UnitTable",
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 457200,
            OffsetYEmu = 1600200,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 685800,
            Table = table
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);

        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = archive.GetEntry("ppt/slides/slide1.xml");
        entry.Should().NotBeNull();
        using var stream = entry!.Open();
        var doc = System.Xml.Linq.XDocument.Load(stream);
        var p = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var a = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        var unitShape = doc.Descendants(p + "sp")
            .Single(sp => sp.Descendants(p + "cNvPr").Any(c => c.Attribute("name")?.Value == "UnitShape"));
        var bodyPr = unitShape.Descendants(a + "bodyPr").Single();
        bodyPr.Attribute("lIns")?.Value.Should().Be(EmuText(1.25));
        bodyPr.Attribute("rIns")?.Value.Should().Be(EmuText(2.5));
        bodyPr.Attribute("tIns")?.Value.Should().Be(EmuText(3.75));
        bodyPr.Attribute("bIns")?.Value.Should().Be(EmuText(4.5));
        unitShape.Element(p + "spPr")!.Element(a + "ln")!.Attribute("w")?.Value.Should().Be(EmuText(1.75));
        unitShape.Descendants(a + "rPr").Single().Element(a + "ln")!.Attribute("w")?.Value.Should().Be(EmuText(1.25));
        var outerShadow = unitShape.Descendants(a + "outerShdw").Single();
        outerShadow.Attribute("blurRad")?.Value.Should().Be(EmuText(1.5));
        outerShadow.Attribute("dist")?.Value.Should().Be(EmuText(2.25));

        var tcPr = doc.Descendants(a + "tcPr").Single();
        tcPr.Attribute("marL")?.Value.Should().Be(EmuText(5.5));
        tcPr.Attribute("marR")?.Value.Should().Be(EmuText(6.25));
        tcPr.Attribute("marT")?.Value.Should().Be(EmuText(7.0));
        tcPr.Attribute("marB")?.Value.Should().Be(EmuText(8.75));
        tcPr.Element(a + "lnL")!.Attribute("w")?.Value.Should().Be(EmuText(0.5));
        tcPr.Element(a + "lnT")!.Attribute("w")?.Value.Should().Be(EmuText(1.25));

        var reloaded = PptxPackageReader.Read(path);
        var reloadedShape = reloaded.Slides[0].Shapes.Single(s => s.Name == "UnitShape");
        reloadedShape.TextBody!.InsetLeftPt.Should().BeApproximately(1.25, 1e-9);
        reloadedShape.TextBody.InsetRightPt.Should().BeApproximately(2.5, 1e-9);
        reloadedShape.TextBody.InsetTopPt.Should().BeApproximately(3.75, 1e-9);
        reloadedShape.TextBody.InsetBottomPt.Should().BeApproximately(4.5, 1e-9);
        var reloadedRun = reloadedShape.TextBody.Paragraphs[0].Runs[0];
        reloadedRun.TextShadow!.BlurPt.Should().BeApproximately(1.5, 1e-9);
        reloadedRun.TextShadow.DistPt.Should().BeApproximately(2.25, 1e-9);

        var reloadedCell = reloaded.Slides[0].Shapes.Single(s => s.Name == "UnitTable").Table!.Rows[0].Cells[0];
        reloadedCell.InsetLeftPt.Should().BeApproximately(5.5, 1e-9);
        reloadedCell.InsetRightPt.Should().BeApproximately(6.25, 1e-9);
        reloadedCell.InsetTopPt.Should().BeApproximately(7.0, 1e-9);
        reloadedCell.InsetBottomPt.Should().BeApproximately(8.75, 1e-9);
    }

    private static TextBody MakeBody(string text)
    {
        var body = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(para);
        return body;
    }

    private static string EmuText(double points) =>
        DrawingMlCoordinateUnits.PointsToEmu(points).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ─────────────────────────────────────────────────────────────────────────────
    // Bug-fix regression tests (Q1–Q7)
    // ─────────────────────────────────────────────────────────────────────────────

    // Q1: p:bg must be the FIRST child of p:cSld, not a sibling of it.
    [Fact]
    public void Q1_SlideBackground_IsInsideCsld()
    {
        var pres = new Presentation();
        var slide = new Slide
        {
            Background = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x12, 0x34, 0x56)))
        };
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);

        // Read the raw XML from the zip to verify structure, not just the round-tripped model.
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = archive.GetEntry("ppt/slides/slide1.xml");
        entry.Should().NotBeNull("slide1.xml must exist");
        using var stream = entry!.Open();
        var doc = System.Xml.Linq.XDocument.Load(stream);

        var p = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var sld  = doc.Root!;
        var cSld = sld.Element(p + "cSld");
        cSld.Should().NotBeNull("p:cSld must be present");

        var bgInsideCsld = cSld!.Element(p + "bg");
        bgInsideCsld.Should().NotBeNull("p:bg must be the first child of p:cSld (Q1)");

        // Confirm p:bg is NOT a direct child of p:sld (the old wrong placement).
        var bgAtSldLevel = sld.Elements(p + "bg").FirstOrDefault();
        bgAtSldLevel.Should().BeNull("p:bg must NOT be a direct child of p:sld (Q1)");
    }

    // Q2: Content-type Default entries must cover every media extension written.
    [Fact]
    public void Q2_GifContentType_HasDefaultEntry()
    {
        // Build a minimal GIF (1x1 pixel) to exercise a non-png/jpg media type
        // that has no Default in the old code.
        var gifBytes = Convert.FromBase64String(
            "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAEALAAAAAABAAEAAAICTAEAOw==");

        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "GifPic",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = gifBytes, ContentType = "image/gif" },
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);

        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var ctEntry = archive.GetEntry("[Content_Types].xml");
        ctEntry.Should().NotBeNull();
        using var stream = ctEntry!.Open();
        var doc = System.Xml.Linq.XDocument.Load(stream);

        var ct = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
        var gifDefault = doc.Root!
            .Elements(ct + "Default")
            .FirstOrDefault(e => (string?)e.Attribute("Extension") == "gif");

        gifDefault.Should().NotBeNull("a Default entry for 'gif' must exist (Q2)");
        gifDefault!.Attribute("ContentType")?.Value.Should().Be("image/gif");

        // Also verify there is no Override pointing at the wrong /ppt/media/media_{id}.gif path.
        var wrongOverride = doc.Root!
            .Elements(ct + "Override")
            .FirstOrDefault(e => ((string?)e.Attribute("PartName") ?? "").Contains("/media/media_"));
        wrongOverride.Should().BeNull("wrong per-shape media Override must not exist (Q2)");
    }

    // Q3+Q4: Two pictures with the same (empty) Name must not throw and must keep distinct images.
    [Fact]
    public void Q3Q4_TwoSameNamedPictures_DoNotThrowAndKeepDistinctImages()
    {
        var png1 = CreateMinimalPng();
        // Build a second slightly different PNG (2x1) so we can distinguish them.
        var png2 = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVQI12P4z8BQDwAEgAF/QualIQAAAABJRU5ErkJggg==");

        var pres = new Presentation();
        var slide = new Slide();
        // Both shapes have empty Name — the old code would throw ArgumentException here.
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = png1, ContentType = "image/png" },
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2, Name = "",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = png2, ContentType = "image/png" },
            OffsetXEmu = 914400,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);  // Must not throw (Q3).
        var reloaded = PptxPackageReader.Read(path);

        var pics = reloaded.Slides[0].Shapes.Where(s => s.Kind == SlideShapeKind.Picture).ToList();
        pics.Should().HaveCount(2, "both picture shapes must survive round-trip (Q4)");

        // Verify each picture has its own bytes and they are not identical
        // (the old code would have both pointing at the first shape's rId, yielding identical bytes).
        var bytes0 = pics[0].Picture!.Bytes!;
        var bytes1 = pics[1].Picture!.Bytes!;
        bytes0.Should().BeEquivalentTo(png1, "shape Id=1 must round-trip its own image (Q4)");
        bytes1.Should().BeEquivalentTo(png2, "shape Id=2 must round-trip its own image (Q4)");
        bytes0.Should().NotBeEquivalentTo(bytes1, "the two pictures must not share the same embedded image (Q4)");
    }

    // Q5: Scheme-color tint/shade must survive round-trip.
    [Fact]
    public void Q5_SchemeColorTintShade_RoundTrips()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var schemeRef = new SchemeColorRef
        {
            Slot = ThemeColorSlot.Accent2,
            LumMod = 1.0,
            LumOff = 0.0,
            Tint  = 0.5,   // non-default → must be emitted
            Shade = 0.75   // non-default → must be emitted
        };
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "TintShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0), schemeRef)),
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "TintShape");
        var sc = ((ShapeFill.Solid)s.Fill!).Color.SchemeColor;
        sc.Should().NotBeNull();
        sc!.Tint .Should().BeApproximately(0.5,  0.001, "tint must round-trip (Q5)");
        sc .Shade.Should().BeApproximately(0.75, 0.001, "shade must round-trip (Q5)");
    }

    // Q6: Group shape must emit p:grpSpPr (not p:spPr) with chOff/chExt.
    [Fact]
    public void Q6_GroupShape_EmitsGrpSpPr_WithChOffChExt()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var group = new SlideShape
        {
            Id = 10, Name = "Grp1",
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 457200, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1828800
        };
        group.Children.Add(new SlideShape
        {
            Id = 11, Name = "Inner",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 457200,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        slide.Shapes.Add(group);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);

        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = archive.GetEntry("ppt/slides/slide1.xml");
        entry.Should().NotBeNull();
        using var stream = entry!.Open();
        var doc = System.Xml.Linq.XDocument.Load(stream);

        var p = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var a = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var grpSp = doc.Descendants(p + "grpSp").FirstOrDefault();
        grpSp.Should().NotBeNull("a p:grpSp must be present");

        var grpSpPr = grpSp!.Element(p + "grpSpPr");
        grpSpPr.Should().NotBeNull("p:grpSp must have p:grpSpPr, not p:spPr (Q6)");

        // Must NOT have p:spPr (wrong element name).
        grpSp.Element(p + "spPr").Should().BeNull("p:grpSp must NOT have p:spPr (Q6)");

        // Must NOT have a prstGeom inside grpSpPr.
        var prstGeom = grpSpPr!.Descendants(a + "prstGeom").FirstOrDefault();
        prstGeom.Should().BeNull("grpSpPr must not contain prstGeom (Q6)");

        // Must have chOff and chExt inside the xfrm.
        var xfrm = grpSpPr.Element(a + "xfrm");
        xfrm.Should().NotBeNull("grpSpPr must have a:xfrm (Q6)");
        xfrm!.Element(a + "chOff").Should().NotBeNull("a:xfrm must have a:chOff (Q6)");
        xfrm .Element(a + "chExt").Should().NotBeNull("a:xfrm must have a:chExt (Q6)");
    }

    // Q7: Absent bandRow attribute must default to false, not true.
    [Fact]
    public void Q7_AbsentBandRowAttribute_DefaultsFalse()
    {
        var pres = new Presentation();
        var slide = new Slide();

        // Table with NO BandRow flag set → writer omits the attribute → reader must read false.
        var table = new TableShape();
        table.Flags.BandRow = false;  // explicit false; writer will omit the attribute
        table.ColumnWidthsEmu.Add(2000000L);
        var row = new TableRow { HeightEmu = 685800L };
        row.Cells.Add(new TableCell());
        table.Rows.Add(row);

        slide.Shapes.Add(new SlideShape
        {
            Id = 20, Name = "NoBandTable",
            Kind = SlideShapeKind.Table,
            ExtentCxEmu = 2000000, ExtentCyEmu = 685800,
            Table = table
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var rt = reloaded.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Table).Table!;
        rt.Flags.BandRow.Should().BeFalse("absent bandRow attribute must default to false (Q7)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Wave 18B: slide backgrounds, tab stops, vertical text
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void W18B_SlideGradientBackground_RoundTrip()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Background = new ShapeFill.Gradient(
            new[]
            {
                new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0x00, 0x40, 0x80))),
                new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0xA0, 0xC0, 0xFF))),
            }, GradientKind.Linear, 90.0);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var loaded = PptxPackageReader.Read(path);

        loaded.Slides[0].Background.Should().BeOfType<ShapeFill.Gradient>(
            "gradient background must survive write→read");
        var grad = (ShapeFill.Gradient)loaded.Slides[0].Background!;
        grad.Stops.Should().HaveCount(2);
        grad.AngleDegrees.Should().BeApproximately(90.0, 1.0);
    }

    [Fact]
    public void W18B_LayoutPictureBackground_RoundTrip()
    {
        // Minimal 1×1 PNG
        var pngBytes = new byte[]
        {
            0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
            0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
            0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
            0x08,0x02,0x00,0x00,0x00,0x90,0x77,0x53,
            0xDE,0x00,0x00,0x00,0x0C,0x49,0x44,0x41,
            0x54,0x08,0xD7,0x63,0xF8,0xCF,0xC0,0x00,
            0x00,0x00,0x02,0x00,0x01,0xE2,0x21,0xBC,
            0x33,0x00,0x00,0x00,0x00,0x49,0x45,0x4E,
            0x44,0xAE,0x42,0x60,0x82
        };
        var pres = new Presentation();
        var layout = pres.Layouts.FirstOrDefault();
        if (layout is null)
        {
            layout = new SlideLayout { Id = "L1", MasterId = pres.Masters.FirstOrDefault()?.Id ?? "M1" };
            pres.Layouts.Add(layout);
        }
        layout.Background = new ShapeFill.Picture(pngBytes, "image/png", tile: false);
        pres.Slides.Add(new Slide { LayoutId = layout.Id });

        var path = WriteToPptx(pres);
        var loaded = PptxPackageReader.Read(path);

        var reloadedLayout = loaded.Layouts.FirstOrDefault(l => l.Id == layout.Id);
        reloadedLayout?.Background.Should().BeOfType<ShapeFill.Picture>(
            "picture background on layout must survive write→read");
    }

    [Fact]
    public void W18B_BackgroundResolution_SlideOverridesLayout()
    {
        // Compositor resolves slide.Background in preference to master.Background.
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];

        var masterBg = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x10, 0x10, 0x10)));
        var slideBg  = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)));

        if (pres.Masters.Count > 0) pres.Masters[0].Background = masterBg;
        slide.Background = slideBg;

        var ops = FreeP.App.Compositor.SlideCompositor.Compose(pres, slide);
        var bgOp = ops.OfType<FreeP.App.Compositor.DrawOp.Background>().First();

        bgOp.Fill.Should().BeOfType<FreeP.App.Compositor.ResolvedFill.Solid>(
            "slide bg must override master bg");
        var solid = (FreeP.App.Compositor.ResolvedFill.Solid)bgOp.Fill;
        solid.Color.R.Should().Be(0xFF);
    }

    [Fact]
    public void W18B_TabStops_RoundTrip()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var para = new Paragraph();
        para.TabStops.Add(new TabStop { PositionEmu = 914400,  Alignment = TabStopAlignment.Left   });
        para.TabStops.Add(new TabStop { PositionEmu = 1828800, Alignment = TabStopAlignment.Center });
        para.TabStops.Add(new TabStop { PositionEmu = 2743200, Alignment = TabStopAlignment.Right  });
        para.Runs.Add(new Run { Text = "A\tB\tC" });
        var shape = new SlideShape
        {
            Id = 1, Name = "TabShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 3657600, ExtentCyEmu = 914400,
            TextBody = new TextBody()
        };
        shape.TextBody!.Paragraphs.Add(para);
        slide.Shapes.Add(shape);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var loaded = PptxPackageReader.Read(path);

        var reloadedShape = loaded.Slides[0].Shapes.First(s => s.Name == "TabShape");
        var reloadedPara = reloadedShape.TextBody!.Paragraphs[0];
        reloadedPara.TabStops.Should().HaveCount(3, "all three tab stops must survive write→read");
        reloadedPara.TabStops[0].PositionEmu.Should().Be(914400);
        reloadedPara.TabStops[0].Alignment.Should().Be(TabStopAlignment.Left);
        reloadedPara.TabStops[1].Alignment.Should().Be(TabStopAlignment.Center);
        reloadedPara.TabStops[2].Alignment.Should().Be(TabStopAlignment.Right);
    }

    [Fact]
    public void W18B_TabAdvance_UsesStopPosition()
    {
        // Compositor converts a tab stop at 914400 EMU (=96 DIP) into a ResolvedTabStop.
        // Use a fresh presentation (not CreateEmpty) so the only shape is the tab shape.
        var pres = new Presentation();
        var slide = new Slide();
        pres.Slides.Add(slide);
        var para = new Paragraph();
        para.TabStops.Add(new TabStop { PositionEmu = 914400, Alignment = TabStopAlignment.Left });
        para.Runs.Add(new Run { Text = "A\tB" });
        var shape = new SlideShape
        {
            Id = 1, Name = "TabCalc",
            Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 5000000, ExtentCyEmu = 1000000,
            TextBody = new TextBody()
        };
        shape.TextBody!.Paragraphs.Add(para);
        slide.Shapes.Add(shape);

        var ops = FreeP.App.Compositor.SlideCompositor.Compose(pres, slide);
        // The only shape with Text is our TabCalc shape.
        var shapeDraw = ops.OfType<FreeP.App.Compositor.DrawOp.Shape>()
            .FirstOrDefault(s => s.Text is not null && s.Text.Paragraphs.Any(p => p.TabStops.Count > 0));
        shapeDraw.Should().NotBeNull("compositor must produce a DrawOp.Shape with resolved tab stops");
        var resolvedPara = shapeDraw!.Text!.Paragraphs.First(p => p.TabStops.Count > 0);
        resolvedPara.TabStops.Should().HaveCount(1);
        resolvedPara.TabStops[0].PositionDip.Should().BeApproximately(914400.0 / 9525.0, 0.5,
            "tab stop must convert from EMU to DIP correctly");
    }

    [Fact]
    public void W18B_VerticalText_RoundTrip()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 1, Name = "VertText",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400, ExtentCyEmu = 2743200,
            TextBody = new TextBody { VerticalType = TextVerticalType.Vertical }
        };
        var vertPara = new Paragraph();
        vertPara.Runs.Add(new Run { Text = "Vertical" });
        shape.TextBody.Paragraphs.Add(vertPara);
        slide.Shapes.Add(shape);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var loaded = PptxPackageReader.Read(path);

        var s = loaded.Slides[0].Shapes.First(x => x.Name == "VertText");
        s.TextBody!.VerticalType.Should().Be(TextVerticalType.Vertical,
            "vert= attribute must survive write→read");
    }

    [Fact]
    public void W18B_VerticalText270_RoundTrip()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 1, Name = "Vert270",
            Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400, ExtentCyEmu = 2743200,
            TextBody = new TextBody { VerticalType = TextVerticalType.Vertical270 }
        };
        var vert270Para = new Paragraph();
        vert270Para.Runs.Add(new Run { Text = "Up" });
        shape.TextBody.Paragraphs.Add(vert270Para);
        slide.Shapes.Add(shape);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var loaded = PptxPackageReader.Read(path);

        var s = loaded.Slides[0].Shapes.First(x => x.Name == "Vert270");
        s.TextBody!.VerticalType.Should().Be(TextVerticalType.Vertical270,
            "vert270 attribute must survive write→read");
    }

    [Theory]
    [InlineData(TextVerticalType.EastAsianVertical)]
    [InlineData(TextVerticalType.WordArtVertical)]
    [InlineData(TextVerticalType.WordArtVerticalRtl)]
    public void W18B_StackedVerticalTextTypes_RoundTrip(TextVerticalType verticalType)
    {
        var pres = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 1,
            Name = $"Stacked-{verticalType}",
            Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 2743200,
            TextBody = new TextBody { VerticalType = verticalType }
        };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Stacked" });
        shape.TextBody.Paragraphs.Add(para);
        slide.Shapes.Add(shape);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var loaded = PptxPackageReader.Read(path);

        var s = loaded.Slides[0].Shapes.First(x => x.Name == $"Stacked-{verticalType}");
        s.TextBody!.VerticalType.Should().Be(verticalType,
            "stacked vertical PowerPoint vert values must survive write/read");
    }

    [Fact]
    public void W18B_VerticalText_Compositor_EmitsOrientation()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        var shape = new SlideShape
        {
            Id = 1, Name = "VT",
            Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400, ExtentCyEmu = 2743200,
            TextBody = new TextBody { VerticalType = TextVerticalType.Vertical }
        };
        var vtPara = new Paragraph();
        vtPara.Runs.Add(new Run { Text = "V" });
        shape.TextBody.Paragraphs.Add(vtPara);
        slide.Shapes.Add(shape);

        var ops = FreeP.App.Compositor.SlideCompositor.Compose(pres, slide);
        var shapeDraw = ops.OfType<FreeP.App.Compositor.DrawOp.Shape>()
            .FirstOrDefault(s => s.Text?.VerticalType == TextVerticalType.Vertical);
        shapeDraw.Should().NotBeNull("compositor must propagate VerticalType to ResolvedTextLayout");
    }
}

/// <summary>
/// Regression tests for bugs U5/U6/U7/U8 (3D shape XML order + schema validity)
/// and U1/U4 (motion animation delay + packed path strings).
/// </summary>
public sealed class Shape3dAndMotionRegressionTests
{
    private static (Presentation pres, SlideShape shape) MakeShapeWithEffects()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        var shape = new SlideShape
        {
            Id = 5, Name = "3DShape", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1828800,
        };
        slide.Shapes.Add(shape);
        return (pres, shape);
    }

    // ── helper: open a PPTX zip MemoryStream and parse slide1.xml ───────────────

    private static System.Xml.Linq.XDocument LoadSlide1Xml(MemoryStream pptxStream)
    {
        pptxStream.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(pptxStream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry("ppt/slides/slide1.xml")!;
        using var sr = entry.Open();
        return System.Xml.Linq.XDocument.Load(sr);
    }

    // ── U5: scene3d MUST precede sp3d in the written XML ────────────────────────

    [Fact]
    public void U5_SpPr_Scene3dPrecedesSp3d_InXml()
    {
        var (pres, shape) = MakeShapeWithEffects();
        shape.Effects = new ShapeEffects
        {
            BevelTop = new BevelInfo { WidthEmu = 76200, HeightEmu = 76200 },
            Scene3d  = new Scene3dInfo
            {
                CameraPreset = "orthographicFront",
                LightRig     = "threePt",
                LightRigDir  = "t",
            },
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        // Inspect slide XML to verify element order inside spPr.
        var doc = LoadSlide1Xml(ms);
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/presentationml/2006/main";
        System.Xml.Linq.XNamespace a  = "http://schemas.openxmlformats.org/drawingml/2006/main";
        // Find the spPr that actually contains 3D elements (skip empty placeholder spPr elements).
        var spPr = doc.Descendants(ns + "spPr")
            .First(el => el.Descendants().Any(e => e.Name.LocalName is "scene3d" or "sp3d"));
        var children = spPr.Elements().Select(e => e.Name.LocalName).ToList();

        var scene3dIdx = children.IndexOf("scene3d");
        var sp3dIdx    = children.IndexOf("sp3d");

        scene3dIdx.Should().BeGreaterThanOrEqualTo(0, "scene3d must be present");
        sp3dIdx.Should().BeGreaterThanOrEqualTo(0, "sp3d must be present");
        scene3dIdx.Should().BeLessThan(sp3dIdx, "scene3d must precede sp3d per CT_ShapeProperties order (U5)");
    }

    // ── U7: camera is always emitted (even when CameraPreset is empty) ───────────

    [Fact]
    public void U7_Scene3d_AlwaysEmitsCamera_WhenCameraPresetEmpty()
    {
        var (pres, shape) = MakeShapeWithEffects();
        // Simulate a lightRig-only scene (CameraPreset empty, as produced by
        // ReadScene3d when only <a:lightRig> was present in the original file).
        shape.Effects = new ShapeEffects
        {
            Scene3d = new Scene3dInfo
            {
                CameraPreset = string.Empty,
                LightRig     = "threePt",
                LightRigDir  = "t",
            },
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        var doc = LoadSlide1Xml(ms);
        System.Xml.Linq.XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var scene3d = doc.Descendants(a + "scene3d").First();

        var camera = scene3d.Element(a + "camera");
        camera.Should().NotBeNull("CT_Scene3D requires <a:camera> minOccurs=1 (U7)");
        camera!.Attribute("prst")?.Value.Should().Be("orthographicFront",
            "default preset must be emitted when CameraPreset is empty");
    }

    // ── U6: bare <a:lightRig/> must NOT be emitted when rig/dir are absent ───────

    [Fact]
    public void U6_Scene3d_OmitsLightRig_WhenRigOrDirEmpty()
    {
        var (pres, shape) = MakeShapeWithEffects();
        // Scene with camera only — no light rig data (LightRig/LightRigDir empty).
        shape.Effects = new ShapeEffects
        {
            Scene3d = new Scene3dInfo
            {
                CameraPreset = "perspectiveFront",
                LightRig     = string.Empty,
                LightRigDir  = string.Empty,
            },
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        var doc = LoadSlide1Xml(ms);
        System.Xml.Linq.XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var scene3d = doc.Descendants(a + "scene3d").First();

        // Camera must be present.
        scene3d.Element(a + "camera").Should().NotBeNull("camera is always required");
        // lightRig must be absent — a bare <a:lightRig/> is schema-invalid (U6).
        scene3d.Element(a + "lightRig").Should().BeNull(
            "lightRig must be omitted when rig/dir are empty to avoid schema-invalid bare element");
    }

    // ── U7+U6 combined: full scene3d round-trips correctly ───────────────────────

    [Fact]
    public void U7_U6_Scene3d_FullRoundTrip()
    {
        var (pres, shape) = MakeShapeWithEffects();
        shape.Effects = new ShapeEffects
        {
            Scene3d = new Scene3dInfo
            {
                CameraPreset = "perspectiveRelaxed",
                LightRig     = "threePt",
                LightRigDir  = "t",
            },
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var fx = loaded.Slides[0].Shapes.Single(s => s.Name == "3DShape").Effects;
        fx.Should().NotBeNull();
        fx!.Scene3d.Should().NotBeNull();
        fx.Scene3d!.CameraPreset.Should().Be("perspectiveRelaxed");
        fx.Scene3d.LightRig.Should().Be("threePt");
        fx.Scene3d.LightRigDir.Should().Be("t");
    }

    // ── U8: zero-width bevel must round-trip as 0, not become the 76200 default ──

    [Fact]
    public void U8_Bevel_ZeroWidthHeight_RoundTrips()
    {
        var (pres, shape) = MakeShapeWithEffects();
        shape.Effects = new ShapeEffects
        {
            BevelTop = new BevelInfo { WidthEmu = 0, HeightEmu = 0, PresetName = "circle" },
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var fx = loaded.Slides[0].Shapes.Single(s => s.Name == "3DShape").Effects;
        fx.Should().NotBeNull();
        fx!.BevelTop.Should().NotBeNull();
        fx.BevelTop!.WidthEmu.Should().Be(0,
            "explicit zero bevel width must round-trip as 0, not restore to the 76200 default (U8)");
        fx.BevelTop.HeightEmu.Should().Be(0,
            "explicit zero bevel height must round-trip as 0, not restore to the 76200 default (U8)");
    }

    // ── U1: motion animation DelayMs round-trips ──────────────────────────────────

    [Fact]
    public void U1_MotionAnimation_DelayMs_RoundTrips()
    {
        // The writer coerces the FIRST animation in a click group to OnClick.
        // To test delay on a motion animation, it must be the SECOND animation
        // in a click group (AfterPrevious with non-zero delay).
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 2, Name = "Leader", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 3, Name = "Mover", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
        });

        // First animation in the click group (OnClick leader).
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = 2,
            Kind       = AnimationKind.Entrance,
            Preset     = AnimationPreset.Appear,
            Trigger    = AnimationTrigger.OnClick,
            DurationMs = 500,
        });

        // Second animation: motion, AfterPrevious + delay — the bug case.
        var motion = new MotionPath { Origin = "parent" };
        motion.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        motion.Segments.Add(MotionPathSegment.LineTo(0.5, 0));

        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = 3,
            Kind       = AnimationKind.Motion,
            Trigger    = AnimationTrigger.AfterPrevious,
            DurationMs = 1000,
            DelayMs    = 750,
            Motion     = motion,
        });

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var anim = loaded.Slides[0].Animations.Single(a => a.Kind == AnimationKind.Motion);
        anim.DelayMs.Should().Be(750, "motion animation DelayMs must survive round-trip (U1)");
        anim.Trigger.Should().Be(AnimationTrigger.AfterPrevious,
            "AfterPrevious trigger must survive round-trip");
    }

    [Fact]
    public void ParagraphBuildList_PreservesThroughReadWriteRoundTrip()
    {
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].AnimationBuildListXml = new XElement(p + "bldLst",
            new XElement(p + "bldP",
                new XAttribute("spid", "2"),
                new XAttribute("grpId", "0"),
                new XAttribute("build", "p"),
                new XAttribute("advAuto", "1"))).ToString(SaveOptions.DisableFormatting);

        using var source = new MemoryStream();
        PptxPackageWriter.Write(pres, source);
        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        loaded.Slides[0].AnimationBuildListXml.Should().Contain("bldP");
        loaded.Slides[0].AnimationBuildListXml.Should().Contain("build=\"p\"");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        saved.Position = 0;
        var reloaded = PptxPackageReader.Read(saved);

        reloaded.Slides[0].AnimationBuildListXml.Should().Contain("spid=\"2\"");
        reloaded.Slides[0].AnimationBuildListXml.Should().Contain("advAuto=\"1\"");
    }

    // Wheel spoke metadata writes and reads through the PPTX timing tree.

    [Fact]
    public void WheelAnimation_SpokeCount_RoundTripsAndWritesFilter()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "WheelTarget",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 2,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Wheel,
            DurationMs = 700,
            WheelSpokeCount = 8,
        });

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;

        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
        {
            var slideXmlEntry = archive.GetEntry("ppt/slides/slide1.xml");
            slideXmlEntry.Should().NotBeNull();
            using var reader = new StreamReader(slideXmlEntry!.Open());
            var slideXml = reader.ReadToEnd();
            slideXml.Should().Contain("wheel(spokes=8)");
        }

        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var anim = loaded.Slides[0].Animations.Single(a => a.Preset == AnimationPreset.Wheel);
        anim.WheelSpokeCount.Should().Be(8);
        anim.DurationMs.Should().Be(700);
    }

    // ── U4: packed path strings ("M0 0 L.5 0 E") parse to correct segments ───────

    [Fact]
    public void U4_ParseMotionPath_PackedString_TwoSegments()
    {
        // "M0 0 L.5 0 E" — command letter glued to first number (packed PowerPoint format).
        // Should parse to: MoveTo(0,0), LineTo(0.5,0), Close — 3 segments.
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 3, Name = "PackedMover", Kind = SlideShapeKind.AutoShape,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
        });

        // We inject the packed path directly by writing a raw PPTX with it.
        // Simplest approach: verify via a spaced-then-packed round-trip using the reader directly.
        // Write a normal animation, then read back a hand-built XML stream with a packed path.
        var packedPathXml = BuildPackedPathPptxBytes(slide.Shapes[0].Id);
        var loaded = PptxPackageReader.Read(new MemoryStream(packedPathXml));

        var anim = loaded.Slides[0].Animations.SingleOrDefault(a => a.Kind == AnimationKind.Motion);
        anim.Should().NotBeNull("motion animation must be parsed from packed path XML (U4)");
        anim!.Motion.Should().NotBeNull();

        var segs = anim.Motion!.Segments;
        segs.Should().HaveCountGreaterThanOrEqualTo(2, "packed 'M0 0 L.5 0' must produce at least 2 segments");
        segs[0].Kind.Should().Be(MotionPathSegmentKind.Move, "first segment is MoveTo");
        segs[0].X.Should().BeApproximately(0, 1e-4);
        segs[0].Y.Should().BeApproximately(0, 1e-4);
        segs[1].Kind.Should().Be(MotionPathSegmentKind.Line, "second segment is LineTo");
        segs[1].X.Should().BeApproximately(0.5, 1e-4);
        segs[1].Y.Should().BeApproximately(0, 1e-4);
    }

    // ── U4 helper: build a minimal .pptx byte array with a packed motion path ────

    private static byte[] BuildPackedPathPptxBytes(uint shapeId)
    {
        // Write a normal presentation with a spaced motion path, then patch the
        // slide1.xml entry inside the zip to use a packed path ("M0 0 L.5 0 E").
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = shapeId, Name = "PackedMover", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
        });

        var motion = new MotionPath { Origin = "parent" };
        motion.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        motion.Segments.Add(MotionPathSegment.LineTo(0.5, 0));
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = shapeId, Kind = AnimationKind.Motion,
            Trigger = AnimationTrigger.OnClick, DurationMs = 500, Motion = motion,
        });

        var srcMs = new MemoryStream();
        PptxPackageWriter.Write(pres, srcMs);

        // Open the zip, read slide1.xml, patch the path= attribute, rewrite into a new zip.
        srcMs.Position = 0;
        var dstMs = new MemoryStream();
        using (var srcZip = new System.IO.Compression.ZipArchive(srcMs, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
        using (var dstZip = new System.IO.Compression.ZipArchive(dstMs, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in srcZip.Entries)
            {
                var dstEntry = dstZip.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.Fastest);
                using var srcStream = entry.Open();
                using var dstStream = dstEntry.Open();

                if (entry.FullName == "ppt/slides/slide1.xml")
                {
                    // Patch path= attribute to use packed form.
                    var xml = new System.IO.StreamReader(srcStream).ReadToEnd();
                    var patched = System.Text.RegularExpressions.Regex.Replace(
                        xml,
                        @"path=""[^""]*""",
                        @"path=""M0 0 L.5 0 E""");
                    var bytes = System.Text.Encoding.UTF8.GetBytes(patched);
                    dstStream.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    srcStream.CopyTo(dstStream);
                }
            }
        }

        return dstMs.ToArray();
    }

}
