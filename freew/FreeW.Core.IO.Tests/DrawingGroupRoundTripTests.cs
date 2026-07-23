using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip (write → read) tests for <see cref="DrawingGroup"/> (wpg:wgp).
/// Verifies that group placement, member count, member types, and child offsets
/// survive a DOCX serialization cycle (Phase 4).
/// </summary>
public sealed class DrawingGroupRoundTripTests
{
    private static readonly XNamespace A   = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Wp  = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace Wpg = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";
    private static readonly XNamespace Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace C   = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace Dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace R   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47];

    private static TextDocument RoundTrip(TextDocument doc)
    {
        using var ms = new MemoryStream();
        DocxWriter.Write(doc, ms);
        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    private static XDocument DocXml(TextDocument doc)
    {
        using var ms = new MemoryStream();
        DocxWriter.Write(doc, ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml")!;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static byte[] WriteBytes(TextDocument doc)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        return stream.ToArray();
    }

    private static DrawingGroup TwoMemberGroup()
    {
        var grp = new DrawingGroup
        {
            WidthPt  = 180,
            HeightPt = 90
        };
        grp.Placement.Wrapping             = ImageWrapping.Square;
        grp.Placement.HorizontalOffsetPt   = 36;
        grp.Placement.VerticalOffsetPt     = 18;
        grp.Placement.HorizontalAnchor     = HorizontalAnchor.Margin;
        grp.Placement.VerticalAnchor       = VerticalAnchor.Page;
        grp.Placement.ZOrderIndex          = 7;

        grp.Children.Add(new InlineImage(Png(), 60, 60));
        grp.ChildOffsets.Add((0, 0));

        grp.Children.Add(new Shape(ShapeKind.Rectangle, 72, 36));
        grp.ChildOffsets.Add((90, 30));

        return grp;
    }

    private static DrawingGroup ThreeMemberGroup()
    {
        var grp = new DrawingGroup { WidthPt = 200, HeightPt = 100 };
        grp.Placement.Wrapping           = ImageWrapping.Square;
        grp.Placement.HorizontalOffsetPt = 72;
        grp.Placement.VerticalOffsetPt   = 36;
        grp.Placement.ZOrderIndex        = 3;

        grp.Children.Add(new Shape(ShapeKind.Ellipse, 50, 50));
        grp.ChildOffsets.Add((0, 0));

        grp.Children.Add(new InlineImage(Png(), 60, 60));
        grp.ChildOffsets.Add((60, 0));

        grp.Children.Add(new Shape(ShapeKind.Rectangle, 60, 40));
        grp.ChildOffsets.Add((130, 20));

        return grp;
    }

    private static DrawingGroup RichShapeAndWordArtGroup()
    {
        var grp = new DrawingGroup { WidthPt = 220, HeightPt = 120 };
        grp.Placement.Wrapping = ImageWrapping.Square;

        var shape = Shape.TextBoxWith("Grouped child", 90, 42, "#CFE2F3");
        shape.OutlineColorHex = "#1155CC";
        shape.OutlineWidthPt = 1.5;
        shape.OutlineDash = "dash";
        shape.Effects = new ShapeEffectLst
        {
            HasShadow = true,
            ShadowColorHex = "222222",
            ShadowAlpha = 32000,
            HasGlow = true,
            GlowColorHex = "70AD47",
            GlowRad = 63500,
            HasReflection = true
        };

        grp.Children.Add(shape);
        grp.ChildOffsets.Add((12, 8));

        var wordArt = new WordArt("Group FX", WordArtStyle.GlowGold, 24)
        {
            Warp = WordArtWarp.Wave1
        };
        grp.Children.Add(wordArt);
        grp.ChildOffsets.Add((118, 22));

        return grp;
    }

    private static DrawingGroup ChartAndSmartArtGroup()
    {
        var chart = Chart.Create(ChartKind.Column, ["Jan", "Feb"], [5.0, 8.0], title: "Grouped sales");
        chart.WidthPt = 90;
        chart.HeightPt = 48;

        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Ship"]);
        smartArt.WidthPt = 84;
        smartArt.HeightPt = 42;

        var group = new DrawingGroup { WidthPt = 200, HeightPt = 90 };
        group.Children.Add(chart);
        group.ChildOffsets.Add((0, 0));
        group.Children.Add(smartArt);
        group.ChildOffsets.Add((108, 18));
        return group;
    }

    private static TextDocument DocumentWith(DrawingGroup grp)
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromDrawingGroup(grp));
        doc.Blocks.Add(para);
        return doc;
    }

    // ── XML structure ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DrawingGroup_EmitsWpAnchorWithWpgWgp()
    {
        var xml = DocXml(DocumentWith(TwoMemberGroup()));
        xml.Descendants(Wp + "anchor").Should().NotBeEmpty("group should be floating → anchor");
        xml.Descendants(Wp + "inline").Should().BeEmpty();
        xml.Descendants(Wpg + "wgp").Should().NotBeEmpty("wpg:wgp element must be present");
    }

    [Fact]
    public void DrawingGroup_EmitsGrpSpPr()
    {
        var xml = DocXml(DocumentWith(TwoMemberGroup()));
        var wgp = xml.Descendants(Wpg + "wgp").Single();
        wgp.Element(Wpg + "grpSpPr").Should().NotBeNull("wpg:grpSpPr must be present inside wpg:wgp");
    }

    [Fact]
    public void DrawingGroup_EmitsRichShapeAndWordArtChildEffects()
    {
        var xml = DocXml(DocumentWith(RichShapeAndWordArtGroup()));
        var children = xml.Descendants(Wpg + "wgp").Single().Elements(Wps + "wsp").ToList();

        children.Should().HaveCount(2);
        children[0].Descendants(A + "solidFill").Should().NotBeEmpty();
        children[0].Descendants(A + "ln").Should().NotBeEmpty();
        children[0].Descendants(A + "effectLst").Single().Elements().Select(e => e.Name.LocalName)
            .Should().Contain(["outerShdw", "glow", "reflection"]);
        children[0].Descendants(Wps + "txbx").Should().NotBeEmpty();

        children[1].Descendants(A + "glow").Should().NotBeEmpty();
        children[1].Descendants(A + "prstTxWarp").Single().Attribute("prst")?.Value.Should().Be("textWave1");
    }

    [Fact]
    public void DrawingGroup_NativeImageChild_EmitsMediaAndRoundTripsPayload()
    {
        var imageBytes = Png();
        var group = new DrawingGroup { WidthPt = 144, HeightPt = 72 };
        group.Children.Add(new InlineImage(imageBytes, 48, 42));
        group.ChildOffsets.Add((0, 0));
        group.Children.Add(new Shape(ShapeKind.Rectangle, 48, 42));
        group.ChildOffsets.Add((72, 0));

        var document = DocumentWith(group);
        var bytes = WriteBytes(document);
        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            zip.GetEntry("word/media/image1.png").Should().NotBeNull();
        }

        var xml = DocXml(document);
        var wgp = xml.Descendants(Wpg + "wgp").Single();
        wgp.Elements(Pic + "pic").Should().ContainSingle()
            .Which.Descendants(A + "blip").Should().ContainSingle();
        wgp.Elements(Wps + "wsp").Should().ContainSingle("the image is native while the shape remains a native wps child");

        var recovered = DocxReader.Read(new MemoryStream(bytes));
        var roundTripped = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.DrawingGroup is not null).DrawingGroup!;
        roundTripped.Children.Should().HaveCount(2);
        roundTripped.Children[0].Should().BeOfType<InlineImage>().Which.Bytes.Should().Equal(imageBytes);
        roundTripped.Children[1].Should().BeOfType<Shape>();
    }

    [Fact]
    public void DrawingGroup_NativeChartAndSmartArtChildren_EmitGraphicFramesAndRoundTrip()
    {
        var document = DocumentWith(ChartAndSmartArtGroup());
        var bytes = WriteBytes(document);
        var xml = DocXml(document);
        var frames = xml.Descendants(Wpg + "wgp").Single().Elements(Wpg + "graphicFrame").ToList();

        frames.Should().HaveCount(2);
        foreach (var frame in frames)
        {
            frame.Element(Wpg + "cNvPr").Should().NotBeNull();
            frame.Element(Wpg + "cNvFrPr").Should().NotBeNull();
            frame.Element(Wpg + "xfrm").Should().NotBeNull();
        }
        frames.Single(frame => frame.Descendants(C + "chart").Any())
            .Descendants(C + "chart").Single().Attribute(R + "id").Should().NotBeNull();
        frames.Single(frame => frame.Descendants(Dgm + "relIds").Any())
            .Descendants(Dgm + "relIds").Single().Attributes().Where(attribute => attribute.Name.Namespace == R)
            .Select(attribute => attribute.Name.LocalName)
            .Should().BeEquivalentTo(["dm", "lo", "qs", "cs"]);

        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            zip.GetEntry("word/charts/chart1.xml").Should().NotBeNull();
            zip.GetEntry("word/diagrams/data1.xml").Should().NotBeNull();
            zip.GetEntry("word/diagrams/layout1.xml").Should().NotBeNull();
            zip.GetEntry("word/diagrams/quickStyle1.xml").Should().NotBeNull();
            zip.GetEntry("word/diagrams/colors1.xml").Should().NotBeNull();
        }

        var recovered = DocxReader.Read(new MemoryStream(bytes));
        var group = ((Paragraph)recovered.Blocks[0]).Runs.Single(run => run.DrawingGroup is not null).DrawingGroup!;
        var chart = group.Children[0].Should().BeOfType<Chart>().Subject;
        chart.Title.Should().Be("Grouped sales");
        chart.Categories.Should().Equal("Jan", "Feb");
        var smartArt = group.Children[1].Should().BeOfType<SmartArt>().Subject;
        smartArt.Kind.Should().Be(SmartArtKind.Process);
        smartArt.Nodes.Select(node => node.Text).Should().Equal("Plan", "Ship");
        group.ChildOffsets[1].X.Should().BeApproximately(108, 0.1);
        group.ChildOffsets[1].Y.Should().BeApproximately(18, 0.1);
    }

    // ── Two-member round-trip ────────────────────────────────────────────────────────────────────

    [Fact]
    public void DrawingGroup_TwoMembers_RoundTrips_MemberCount()
    {
        var recovered = RoundTrip(DocumentWith(TwoMemberGroup()));
        var grp = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.DrawingGroup is not null).DrawingGroup!;
        grp.Children.Should().HaveCount(2);
    }

    [Fact]
    public void DrawingGroup_TwoMembers_RoundTrips_MemberTypes()
    {
        var recovered = RoundTrip(DocumentWith(TwoMemberGroup()));
        var grp = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.DrawingGroup is not null).DrawingGroup!;
        grp.Children[0].Should().BeOfType<InlineImage>();
        grp.Children[1].Should().BeOfType<Shape>();
    }

    [Fact]
    public void DrawingGroup_TwoMembers_RoundTrips_Placement()
    {
        var recovered = RoundTrip(DocumentWith(TwoMemberGroup()));
        var grp = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.DrawingGroup is not null).DrawingGroup!;
        grp.Placement.Wrapping.Should().Be(ImageWrapping.Square);
        grp.Placement.HorizontalOffsetPt.Should().BeApproximately(36, 0.5);
        grp.Placement.VerticalOffsetPt.Should().BeApproximately(18, 0.5);
        grp.Placement.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        grp.Placement.VerticalAnchor.Should().Be(VerticalAnchor.Page);
        grp.Placement.ZOrderIndex.Should().Be(7);
    }

    [Fact]
    public void DrawingGroup_TwoMembers_RoundTrips_ChildOffsets()
    {
        var recovered = RoundTrip(DocumentWith(TwoMemberGroup()));
        var grp = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.DrawingGroup is not null).DrawingGroup!;
        grp.ChildOffsets.Should().HaveCount(2);
        grp.ChildOffsets[0].X.Should().BeApproximately(0,  1.0);
        grp.ChildOffsets[0].Y.Should().BeApproximately(0,  1.0);
        grp.ChildOffsets[1].X.Should().BeApproximately(90, 1.0);
        grp.ChildOffsets[1].Y.Should().BeApproximately(30, 1.0);
    }

    [Fact]
    public void DrawingGroup_TwoMembers_RoundTrips_GroupSize()
    {
        var recovered = RoundTrip(DocumentWith(TwoMemberGroup()));
        var grp = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.DrawingGroup is not null).DrawingGroup!;
        grp.WidthPt.Should().BeApproximately(180, 1.0);
        grp.HeightPt.Should().BeApproximately(90, 1.0);
    }

    // ── Three-member round-trip ──────────────────────────────────────────────────────────────────

    [Fact]
    public void DrawingGroup_ThreeMembers_RoundTrips()
    {
        var recovered = RoundTrip(DocumentWith(ThreeMemberGroup()));
        var grp = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.DrawingGroup is not null).DrawingGroup!;
        grp.Children.Should().HaveCount(3);
        grp.Placement.ZOrderIndex.Should().Be(3);
        grp.Placement.HorizontalOffsetPt.Should().BeApproximately(72, 0.5);
    }

    [Fact]
    public void DrawingGroup_ShapeAndWordArtChildren_RoundTripRichFormattingAndEffects()
    {
        var recovered = RoundTrip(DocumentWith(RichShapeAndWordArtGroup()));
        var grp = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.DrawingGroup is not null).DrawingGroup!;

        grp.Children.Should().HaveCount(2);
        grp.ChildOffsets[0].X.Should().BeApproximately(12, 1.0);
        grp.ChildOffsets[1].Y.Should().BeApproximately(22, 1.0);

        var shape = grp.Children[0].Should().BeOfType<Shape>().Subject;
        shape.Kind.Should().Be(ShapeKind.TextBox);
        shape.WidthPt.Should().BeApproximately(90, 1.0);
        shape.HeightPt.Should().BeApproximately(42, 1.0);
        shape.FillColorHex.Should().Be("#CFE2F3");
        shape.OutlineColorHex.Should().Be("#1155CC");
        shape.OutlineWidthPt.Should().BeApproximately(1.5, 0.1);
        shape.OutlineDash.Should().Be("dash");
        shape.PlainText.Should().Be("Grouped child");
        shape.Effects.Should().NotBeNull();
        shape.Effects!.HasShadow.Should().BeTrue();
        shape.Effects.HasGlow.Should().BeTrue();
        shape.Effects.HasReflection.Should().BeTrue();
        shape.Effects.GlowColorHex.Should().Be("70AD47");

        var wordArt = grp.Children[1].Should().BeOfType<WordArt>().Subject;
        wordArt.Text.Should().Be("Group FX");
        wordArt.Style.Should().Be(WordArtStyle.GlowGold);
        wordArt.FontSizePt.Should().BeApproximately(24, 0.1);
        wordArt.Warp.Should().Be(WordArtWarp.Wave1);
    }

    // ── Existing floating objects are unaffected ─────────────────────────────────────────────────

    [Fact]
    public void StandaloneFloatingShape_Unaffected_WhenGroupAlsoPresentInSameDoc()
    {
        var doc = new TextDocument();
        var p0 = new Paragraph();
        var shape = new Shape(ShapeKind.Ellipse, 60, 30)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 100,
                ZOrderIndex = 1
            }
        };
        p0.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(p0);

        var p1 = new Paragraph();
        p1.Runs.Add(Run.FromDrawingGroup(TwoMemberGroup()));
        doc.Blocks.Add(p1);

        var recovered = RoundTrip(doc);

        // Paragraph 0 → standalone shape
        var s = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.Shape is not null).Shape!;
        s.IsFloating.Should().BeTrue();
        s.Placement!.HorizontalOffsetPt.Should().BeApproximately(100, 0.5);

        // Paragraph 1 → group
        var grp = ((Paragraph)recovered.Blocks[1]).Runs.Single(r => r.DrawingGroup is not null).DrawingGroup!;
        grp.Children.Should().HaveCount(2);
    }
}
