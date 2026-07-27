using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class RightToLeftParagraphParityTests
{
    private const string HebrewSample = "\u05D0\u05D1\u05D2";
    private const string HebrewLongSample = "\u05D0\u05D1\u05D2\u05D3";

    private static Paragraph Paragraph(string text, bool? rightToLeft = null) =>
        new()
        {
            RightToLeft = rightToLeft,
            Runs = { new Run { Text = text, FontFamily = "Arial", FontSizePt = 18 } },
        };

    private static Presentation MakePresentation(TextBody body)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 3000000,
            TextBody = body,
        });
        return presentation;
    }

    [Fact]
    public void PptxParagraphRtl_ExplicitTrueFalseAndAbsent_RoundTripTruthfully()
    {
        var body = new TextBody();
        body.Paragraphs.Add(Paragraph(HebrewSample, true));
        body.Paragraphs.Add(Paragraph("LTR override", false));
        body.Paragraphs.Add(Paragraph("Inherited"));

        using var package = new MemoryStream();
        PptxPackageWriter.Write(MakePresentation(body), package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (var stream = archive.GetEntry("ppt/slides/slide1.xml")!.Open())
        {
            var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
            var values = XDocument.Load(stream)
                .Descendants(a + "p")
                .Select(p => p.Element(a + "pPr")?.Attribute("rtl")?.Value)
                .ToArray();
            values.Should().ContainInOrder("1", "0", null);
        }

        package.Position = 0;
        var restored = PptxPackageReader.Read(package);
        var paragraphs = restored.Slides[0].Shapes[0].TextBody!.Paragraphs;
        paragraphs.Select(p => p.RightToLeft).Should().ContainInOrder(true, false, null);
    }

    [Fact]
    public void PptxParagraphRtl_StyleDirection_IsResolvedWhenParagraphOmitsAttribute()
    {
        var body = new TextBody
        {
            DefaultParaRightToLeft = true,
            LstStyle = new TextStyleLevels(),
        };
        body.LstStyle[0] = new TextStyleLevel { RightToLeft = true };
        body.Paragraphs.Add(Paragraph("Inherited"));
        body.Paragraphs.Add(Paragraph("Explicit LTR", false));

        var presentation = MakePresentation(body);
        var layout = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Shape>()
            .Single()
            .Text!;

        layout.Paragraphs.Select(p => p.RightToLeft).Should().ContainInOrder(true, false);
    }

    [Fact]
    public void PptxBodyDefaultParaRightToLeft_Lvl1PPr_RoundTripsTrueAndFalse()
    {
        foreach (bool expected in new[] { true, false })
        {
            var body = new TextBody { DefaultParaRightToLeft = expected };
            body.Paragraphs.Add(Paragraph("body default"));
            using var package = new MemoryStream();
            PptxPackageWriter.Write(MakePresentation(body), package);

            package.Position = 0;
            using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
            using (var stream = archive.GetEntry("ppt/slides/slide1.xml")!.Open())
            {
                var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
                XDocument.Load(stream)
                    .Descendants(a + "lvl1pPr")
                    .Single()
                    .Attribute("rtl")!.Value
                    .Should().Be(expected ? "1" : "0");
            }

            package.Position = 0;
            var restored = PptxPackageReader.Read(package);
            restored.Slides[0].Shapes[0].TextBody!.DefaultParaRightToLeft
                .Should().Be(expected);
        }
    }

    [Fact]
    public void ExternalRtf_RtlparAndLtrpar_PreserveExplicitParagraphDirection()
    {
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi\rtlpar Arabic \par\ltrpar English\par}");

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(rtf);

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs.Select(p => p.RightToLeft)
            .Should().ContainInOrder(true, false);
    }

    [Fact]
    public void SharedClipboard_RtlAndExplicitLtrSurviveSerialization()
    {
        var source = new TextBody();
        source.Paragraphs.Add(Paragraph(HebrewSample, true));
        source.Paragraphs.Add(Paragraph("abc", false));
        var payload = InCanvasRichClipboardPlanner.Capture(
            source,
            new InCanvasEditorTextSelection(0, 7));

        var restored = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));

        restored.Should().NotBeNull();
        restored!.Body.Paragraphs.Select(p => p.RightToLeft)
            .Should().ContainInOrder(true, false);
    }

    [Fact]
    public void LtrParagraph_WithAbsentDirection_RemainsResolvedLtr()
    {
        var presentation = MakePresentation(new TextBody
        {
            Paragraphs = { Paragraph("ordinary LTR") },
        });

        var paragraph = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Shape>()
            .Single()
            .Text!
            .Paragraphs
            .Single();

        paragraph.RightToLeft.Should().BeFalse();
    }

    [Fact]
    public void MixedRuns_RtlParagraph_UsesVisualOrderAndStrongRunDirections()
    {
        var paragraph = new ResolvedParagraph
        {
            RightToLeft = true,
            Align = TextAlign.Left,
            Runs =
            [
                new ResolvedRun { Text = HebrewLongSample },
                new ResolvedRun { Text = " / " },
                new ResolvedRun { Text = "LTR" },
            ],
        };

        var placements = TextLayoutPlanner.PlanRunPlacements(
            paragraph,
            startX: 10,
            availableWidth: 0,
            (run, rightToLeft) => run.Text.Length * 10);

        placements.Should().HaveCount(3);
        placements[0].RunIndex.Should().Be(2);
        placements[0].X.Should().Be(10);
        placements[0].Width.Should().Be(30);
        placements[0].RightToLeft.Should().BeFalse();
        placements[1].RunIndex.Should().Be(1);
        placements[1].X.Should().Be(40);
        placements[1].Width.Should().Be(30);
        placements[1].RightToLeft.Should().BeTrue();
        placements[2].RunIndex.Should().Be(0);
        placements[2].X.Should().Be(70);
        placements[2].Width.Should().Be(40);
        placements[2].RightToLeft.Should().BeTrue();
    }

    [Fact]
    public void MixedRuns_LtrParagraph_KeepsLogicalOrderAndCoordinates()
    {
        var paragraph = new ResolvedParagraph
        {
            RightToLeft = false,
            Runs =
            [
                new ResolvedRun { Text = "abcd" },
                new ResolvedRun { Text = " / " },
                new ResolvedRun { Text = "LTR" },
            ],
        };

        var placements = TextLayoutPlanner.PlanRunPlacements(
            paragraph,
            startX: 10,
            availableWidth: 0,
            (run, rightToLeft) => run.Text.Length * 10);

        placements.Select(p => p.RunIndex).Should().ContainInOrder(0, 1, 2);
        placements.Select(p => p.X).Should().ContainInOrder(10, 50, 80);
        placements.Select(p => p.RightToLeft).Should().ContainInOrder(false, false, false);
    }
}
