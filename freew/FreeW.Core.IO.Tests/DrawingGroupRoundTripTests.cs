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
    private static readonly XNamespace W   = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A   = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Wp  = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace Wpg = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";
    private static readonly XNamespace Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace C   = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace Dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace R   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Ct  = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

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

    private static byte[] RewriteDocumentXml(byte[] docx, Action<XDocument> mutate)
    {
        using var sourceStream = new MemoryStream(docx);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read);
        var documentEntry = source.GetEntry("word/document.xml")!;
        XDocument document;
        using (var documentStream = documentEntry.Open())
            document = XDocument.Load(documentStream);
        mutate(document);

        using var output = new MemoryStream();
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var sourceEntry in source.Entries)
            {
                var destinationEntry = destination.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                using var destinationStream = destinationEntry.Open();
                if (sourceEntry.FullName == "word/document.xml")
                {
                    document.Save(destinationStream);
                    continue;
                }

                using var sourceEntryStream = sourceEntry.Open();
                sourceEntryStream.CopyTo(destinationStream);
            }
        }

        return output.ToArray();
    }

    private static XDocument EntryXml(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var stream = zip.GetEntry(entryPath)!.Open();
        return XDocument.Load(stream);
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

    private static DrawingGroup NestedGroup()
    {
        var inner = new DrawingGroup
        {
            WidthPt = 90,
            HeightPt = 48,
            RotationAngle = 15,
            FlipH = true
        };
        inner.Children.Add(new Shape(ShapeKind.Ellipse, 30, 24, "#70AD47"));
        inner.ChildOffsets.Add((6, 8));
        inner.Children.Add(new WordArt("Inner", WordArtStyle.GlowGold, 18));
        inner.ChildOffsets.Add((42, 12));
        inner.Children.Add(new InlineImage(Png(), 18, 18));
        inner.ChildOffsets.Add((66, 24));

        var outer = new DrawingGroup { WidthPt = 180, HeightPt = 96 };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((12, 18));
        outer.Children.Add(new Shape(ShapeKind.Rectangle, 54, 36, "#4472C4"));
        outer.ChildOffsets.Add((108, 24));
        return outer;
    }

    private static TextDocument DocumentWith(DrawingGroup grp)
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromDrawingGroup(grp));
        doc.Blocks.Add(para);
        return doc;
    }

    /// <summary>Rehomes a native chart + SmartArt group from document.xml into a header with local relationships.</summary>
    private static byte[] AuthorHeaderChartAndSmartArtGroupPackage()
    {
        var sourceBytes = WriteBytes(DocumentWith(ChartAndSmartArtGroup()));
        using var sourceStream = new MemoryStream(sourceBytes);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read);

        static XDocument ReadEntry(ZipArchive zip, string path)
        {
            using var stream = zip.GetEntry(path)!.Open();
            return XDocument.Load(stream);
        }

        var sourceDocument = ReadEntry(source, "word/document.xml");
        var drawing = new XElement(sourceDocument.Descendants(W + "drawing").Single());
        var sourceRelationships = ReadEntry(source, "word/_rels/document.xml.rels");
        var allRelationships = sourceRelationships.Root!.Elements(Rel + "Relationship").ToList();
        var groupRelationships = allRelationships
            .Where(relationship => relationship.Attribute("Type")!.Value.EndsWith("/chart", StringComparison.Ordinal)
                                || relationship.Attribute("Type")!.Value.Contains("diagram", StringComparison.OrdinalIgnoreCase))
            .Select(relationship => new XElement(relationship))
            .ToList();
        groupRelationships.Should().HaveCount(6);

        var documentRelationships = new XDocument(new XElement(Rel + "Relationships",
            allRelationships
                .Where(relationship => !groupRelationships.Any(candidate => candidate.Attribute("Id")!.Value == relationship.Attribute("Id")!.Value))
                .Select(relationship => new XElement(relationship)),
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rIdHeader1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header"),
                new XAttribute("Target", "header1.xml"))));
        var headerRelationships = new XDocument(new XElement(Rel + "Relationships", groupRelationships));
        var document = new XDocument(new XElement(W + "document",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
            new XElement(W + "body",
                new XElement(W + "p", new XElement(W + "r", new XElement(W + "t", "Body text"))),
                new XElement(W + "sectPr", new XElement(W + "headerReference",
                    new XAttribute(W + "type", "default"),
                    new XAttribute(R + "id", "rIdHeader1"))))));
        var header = new XDocument(new XElement(W + "hdr",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
            new XElement(W + "p", new XElement(W + "r", drawing))));
        var contentTypes = ReadEntry(source, "[Content_Types].xml");
        contentTypes.Root!.Add(new XElement(Ct + "Override",
            new XAttribute("PartName", "/word/header1.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml")));

        using var output = new MemoryStream();
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void AddXml(string path, XDocument xml)
            {
                var entry = destination.CreateEntry(path, CompressionLevel.Optimal);
                using var stream = entry.Open();
                xml.Save(stream);
            }

            foreach (var sourceEntry in source.Entries)
            {
                if (sourceEntry.FullName is "[Content_Types].xml" or "word/document.xml" or "word/_rels/document.xml.rels")
                    continue;
                var entry = destination.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                using var input = sourceEntry.Open();
                using var target = entry.Open();
                input.CopyTo(target);
            }

            AddXml("[Content_Types].xml", contentTypes);
            AddXml("word/document.xml", document);
            AddXml("word/_rels/document.xml.rels", documentRelationships);
            AddXml("word/header1.xml", header);
            AddXml("word/_rels/header1.xml.rels", headerRelationships);
        }
        return output.ToArray();
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

    [Fact]
    public void HeaderDrawingGroup_PreservesItsStoryLocalChartAndSmartArtGraph()
    {
        var source = AuthorHeaderChartAndSmartArtGroupPackage();
        var read = DocxReader.Read(new MemoryStream(source));
        var runs = read.FinalSectionHeadersFooters.Header!.Paragraphs.SelectMany(paragraph => paragraph.Runs).ToList();
        runs.Should().ContainSingle(run => run.PreservedDrawing != null);
        runs.Should().NotContain(run => run.DrawingGroup != null);

        var rewritten = WriteBytes(read);
        using var sourceZip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var rewrittenZip = new ZipArchive(new MemoryStream(rewritten), ZipArchiveMode.Read);
        foreach (var part in new[]
                 {
                     "word/charts/chart1.xml", "word/diagrams/data1.xml", "word/diagrams/layout1.xml",
                     "word/diagrams/quickStyle1.xml", "word/diagrams/colors1.xml", "word/diagrams/drawing1.xml"
                 })
        {
            using var sourcePart = sourceZip.GetEntry(part)!.Open();
            using var rewrittenPart = rewrittenZip.GetEntry(part)!.Open();
            using var sourceBytes = new MemoryStream();
            using var rewrittenBytes = new MemoryStream();
            sourcePart.CopyTo(sourceBytes);
            rewrittenPart.CopyTo(rewrittenBytes);
            rewrittenBytes.ToArray().Should().Equal(sourceBytes.ToArray());
        }

        var headerRels = EntryXml(rewritten, "word/_rels/header1.xml.rels").Root!.Elements(Rel + "Relationship").ToList();
        headerRels.Should().HaveCount(6);
        headerRels.Should().Contain(relationship => relationship.Attribute("Type")!.Value.EndsWith("/chart", StringComparison.Ordinal));
        headerRels.Count(relationship => relationship.Attribute("Type")!.Value.Contains("diagram", StringComparison.OrdinalIgnoreCase)).Should().Be(5);
        EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship")
            .Should().NotContain(relationship => relationship.Attribute("Type")!.Value.EndsWith("/chart", StringComparison.Ordinal)
                                           || relationship.Attribute("Type")!.Value.Contains("diagram", StringComparison.OrdinalIgnoreCase));

        var secondRead = DocxReader.Read(new MemoryStream(rewritten));
        secondRead.FinalSectionHeadersFooters.Header!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().ContainSingle(run => run.PreservedDrawing != null);
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

    [Fact]
    public void DrawingGroup_ChildEditGeometry_RoundTripsExactLocalOffsetAndSize()
    {
        var group = TwoMemberGroup();
        group.RotationAngle = 37;
        group.FlipH = true;
        group.ChildOffsets[1] = (101.25, 42.75);
        var shape = group.Children[1].Should().BeOfType<Shape>().Subject;
        shape.WidthPt = 91.5;
        shape.HeightPt = 44.25;

        var recovered = RoundTrip(DocumentWith(group));
        var read = ((Paragraph)recovered.Blocks[0]).Runs.Single().DrawingGroup!;
        var readShape = read.Children[1].Should().BeOfType<Shape>().Subject;

        read.RotationAngle.Should().BeApproximately(37, 0.001);
        read.FlipH.Should().BeTrue();
        read.ChildOffsets[1].X.Should().BeApproximately(101.25, 0.01);
        read.ChildOffsets[1].Y.Should().BeApproximately(42.75, 0.01);
        readShape.WidthPt.Should().BeApproximately(91.5, 0.01);
        readShape.HeightPt.Should().BeApproximately(44.25, 0.01);
    }

    [Fact]
    public void DrawingGroup_Transform_RoundTripsAndEmitsGroupXfrmAttributes()
    {
        var group = TwoMemberGroup();
        group.RotationAngle = 45;
        group.FlipH = true;
        group.FlipV = true;

        var xml = DocXml(DocumentWith(group));
        var xfrm = xml.Descendants(Wpg + "grpSpPr").Single().Element(A + "xfrm");
        xfrm.Should().NotBeNull();
        xfrm!.Attribute("rot")!.Value.Should().Be("2700000");
        xfrm.Attribute("flipH")!.Value.Should().Be("1");
        xfrm.Attribute("flipV")!.Value.Should().Be("1");

        var recovered = RoundTrip(DocumentWith(group));
        var read = ((Paragraph)recovered.Blocks[0]).Runs.Single().DrawingGroup!;
        read.RotationAngle.Should().BeApproximately(45, 0.001);
        read.FlipH.Should().BeTrue();
        read.FlipV.Should().BeTrue();
    }

    [Fact]
    public void DrawingGroup_NestedGroup_RoundTripsLocalTransformAndRichChildren()
    {
        var document = DocumentWith(NestedGroup());
        var xml = DocXml(document);
        xml.Descendants(Wpg + "wgp").Should().HaveCount(2, "nested groups remain native wpg:wgp payloads");

        var recovered = RoundTrip(document);
        var outer = ((Paragraph)recovered.Blocks[0]).Runs.Single().DrawingGroup!;
        outer.Children.Should().HaveCount(2);
        outer.ChildOffsets[0].X.Should().BeApproximately(12, 0.1);
        outer.ChildOffsets[0].Y.Should().BeApproximately(18, 0.1);

        var inner = outer.Children[0].Should().BeOfType<DrawingGroup>().Subject;
        inner.WidthPt.Should().BeApproximately(90, 0.1);
        inner.HeightPt.Should().BeApproximately(48, 0.1);
        inner.RotationAngle.Should().BeApproximately(15, 0.001);
        inner.FlipH.Should().BeTrue();
        inner.Children.Should().HaveCount(3);
        inner.Children[0].Should().BeOfType<Shape>().Which.FillColorHex.Should().Be("#70AD47");
        inner.Children[1].Should().BeOfType<WordArt>().Which.Text.Should().Be("Inner");
        inner.Children[2].Should().BeOfType<InlineImage>().Which.Bytes.Should().Equal(Png());
    }

    [Fact]
    public void DrawingGroup_NestedChildEditGeometry_RoundTripsExactLeafOffsetAndSize()
    {
        var inner = new DrawingGroup
        {
            WidthPt = 126,
            HeightPt = 72,
            RotationAngle = -16,
            FlipV = true
        };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 36, 22));
        inner.ChildOffsets.Add((10, 8));
        inner.Children.Add(new Shape(ShapeKind.Ellipse, 82, 52)
        {
            RotationAngle = 13,
            FlipH = true,
            FlipV = true
        });
        inner.ChildOffsets.Add((79, 43));

        var outer = new DrawingGroup
        {
            WidthPt = 252,
            HeightPt = 144,
            RotationAngle = 24,
            FlipH = true
        };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((28, 22));
        outer.Children.Add(new Shape(ShapeKind.Rectangle, 54, 34));
        outer.ChildOffsets.Add((168, 76));

        var recovered = RoundTrip(DocumentWith(outer));
        var readOuter = ((Paragraph)recovered.Blocks[0]).Runs.Single().DrawingGroup!;
        var readInner = readOuter.Children[0].Should().BeOfType<DrawingGroup>().Subject;
        var readLeaf = readInner.Children[1].Should().BeOfType<Shape>().Subject;

        readOuter.WidthPt.Should().BeApproximately(252, 0.001);
        readOuter.HeightPt.Should().BeApproximately(144, 0.001);
        readInner.WidthPt.Should().BeApproximately(126, 0.001);
        readInner.HeightPt.Should().BeApproximately(72, 0.001);
        readInner.ChildOffsets[1].X.Should().BeApproximately(79, 0.001);
        readInner.ChildOffsets[1].Y.Should().BeApproximately(43, 0.001);
        readLeaf.WidthPt.Should().BeApproximately(82, 0.001);
        readLeaf.HeightPt.Should().BeApproximately(52, 0.001);
        readLeaf.RotationAngle.Should().BeApproximately(13, 0.001);
        readLeaf.FlipH.Should().BeTrue();
        readLeaf.FlipV.Should().BeTrue();
    }

    [Fact]
    public void DrawingGroup_NestedShapeFormattingCommand_RoundTripsLeafOnly()
    {
        var inner = new DrawingGroup { WidthPt = 126, HeightPt = 72 };
        var sibling = new Shape(ShapeKind.Rectangle, 36, 22) { FillColorHex = "#222222" };
        var leaf = new Shape(ShapeKind.Ellipse, 82, 52) { FillColorHex = "#111111" };
        inner.Children.Add(sibling);
        inner.ChildOffsets.Add((10, 8));
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((54, 26));
        var outer = new DrawingGroup { WidthPt = 252, HeightPt = 144 };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((28, 22));
        outer.Children.Add(new Shape(ShapeKind.Rectangle, 54, 34));
        outer.ChildOffsets.Add((168, 76));
        var document = DocumentWith(outer);
        var context = new CommandContext(document);
        var path = new[] { 0, 1 };

        new SetDrawingGroupChildPositionCommand(0, 0, path, 61, 33).Apply(context);
        new SetDrawingGroupChildSizeCommand(0, 0, path, 96, 58).Apply(context);
        new SetShapeKindCommand(0, 0, ShapeKind.RoundedRectangle, path).Apply(context);
        new SetShapeAltTextCommand(0, 0, "Nested leaf", path).Apply(context);
        new SetShapeFillCommand(0, 0, "#ABCDEF", path).Apply(context);
        new SetShapeOutlineCommand(0, 0, "#123456", 2.5, "dash", path).Apply(context);
        new SetShapeEffectsCommand(
            0, 0, new ShapeEffectLst { HasGlow = true, GlowColorHex = "ABCDEF" }, path).Apply(context);

        var recovered = RoundTrip(document);
        var readOuter = ((Paragraph)recovered.Blocks[0]).Runs.Single().DrawingGroup!;
        var readInner = readOuter.Children[0].Should().BeOfType<DrawingGroup>().Subject;
        var readSibling = readInner.Children[0].Should().BeOfType<Shape>().Subject;
        var readLeaf = readInner.Children[1].Should().BeOfType<Shape>().Subject;

        readInner.ChildOffsets[1].X.Should().BeApproximately(61, 0.01);
        readInner.ChildOffsets[1].Y.Should().BeApproximately(33, 0.01);
        readLeaf.WidthPt.Should().BeApproximately(96, 0.01);
        readLeaf.HeightPt.Should().BeApproximately(58, 0.01);
        readLeaf.Kind.Should().Be(ShapeKind.RoundedRectangle);
        readLeaf.AltText.Should().Be("Nested leaf");
        readLeaf.FillColorHex.Should().Be("#ABCDEF");
        readLeaf.OutlineColorHex.Should().Be("#123456");
        readLeaf.OutlineWidthPt.Should().BeApproximately(2.5, 0.01);
        readLeaf.OutlineDash.Should().Be("dash");
        readLeaf.Effects.Should().NotBeNull();
        readLeaf.Effects!.HasGlow.Should().BeTrue();
        readLeaf.Effects.GlowColorHex.Should().Be("ABCDEF");
        readSibling.Kind.Should().Be(ShapeKind.Rectangle);
        readSibling.AltText.Should().BeNull();
        readSibling.FillColorHex.Should().Be("#222222");
        readSibling.OutlineColorHex.Should().BeNull();
        readSibling.Effects.Should().BeNull();
    }

    [Fact]
    public void DrawingGroup_ChartAndSmartArtChildTransforms_RoundTripThroughDocx()
    {
        var group = ChartAndSmartArtGroup();
        var chart = group.Children.OfType<Chart>().Single();
        var smartArt = group.Children.OfType<SmartArt>().Single();
        chart.RotationAngle = 37;
        chart.FlipH = true;
        smartArt.RotationAngle = -19;
        smartArt.FlipV = true;

        var recovered = RoundTrip(DocumentWith(group));
        var read = ((Paragraph)recovered.Blocks[0]).Runs.Single().DrawingGroup!;
        var readChart = read.Children.OfType<Chart>().Single();
        var readSmartArt = read.Children.OfType<SmartArt>().Single();
        readChart.RotationAngle.Should().BeApproximately(37, 0.001);
        readChart.FlipH.Should().BeTrue();
        readSmartArt.RotationAngle.Should().BeApproximately(-19, 0.001);
        readSmartArt.FlipV.Should().BeTrue();
    }

    [Fact]
    public void DrawingGroup_ChildCoordinateSpace_IsMappedIntoRenderedGroupBounds()
    {
        var bytes = RewriteDocumentXml(WriteBytes(DocumentWith(TwoMemberGroup())), document =>
        {
            var groupXfrm = document.Descendants(Wpg + "grpSpPr").Single().Element(A + "xfrm")!;
            var childOrigin = groupXfrm.Element(A + "chOff")!;
            childOrigin.SetAttributeValue("x", 36 * 12700);
            childOrigin.SetAttributeValue("y", 18 * 12700);
            var childExtent = groupXfrm.Element(A + "chExt")!;
            childExtent.SetAttributeValue("cx", 360 * 12700);
            childExtent.SetAttributeValue("cy", 180 * 12700);
        });

        var recovered = DocxReader.Read(new MemoryStream(bytes));
        var group = ((Paragraph)recovered.Blocks[0]).Runs.Single().DrawingGroup!;
        var image = group.Children[0].Should().BeOfType<InlineImage>().Subject;
        var shape = group.Children[1].Should().BeOfType<Shape>().Subject;

        group.ChildOffsets[0].X.Should().BeApproximately(-18, 0.01);
        group.ChildOffsets[0].Y.Should().BeApproximately(-9, 0.01);
        image.WidthPt.Should().BeApproximately(30, 0.01);
        image.HeightPt.Should().BeApproximately(30, 0.01);
        group.ChildOffsets[1].X.Should().BeApproximately(27, 0.01);
        group.ChildOffsets[1].Y.Should().BeApproximately(6, 0.01);
        shape.WidthPt.Should().BeApproximately(36, 0.01);
        shape.HeightPt.Should().BeApproximately(18, 0.01);
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

file sealed class CommandContext(TextDocument document) : IDocumentCommandContext
{
    public TextDocument Document { get; } = document;
}
