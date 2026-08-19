using System.Text;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Unit tests for <see cref="EditingSession"/>.
/// </summary>
public sealed class EditingSessionTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>Creates a session with <paramref name="slideCount"/> blank slides (no shapes).</summary>
    private static EditingSession Make(int slideCount = 1)
    {
        var p = new Presentation();
        for (int i = 0; i < slideCount; i++)
            p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);
        return new EditingSession(p, bus);
    }

    private static SlideShape MakeShape(uint id = 1) => new()
    {
        Id          = id,
        Name        = $"S{id}",
        Kind        = SlideShapeKind.AutoShape,
        OffsetXEmu  = 0,
        OffsetYEmu  = 0,
        ExtentCxEmu = 100,
        ExtentCyEmu = 100,
    };

    private static (EditingSession Session, SmartArtShape SmartArt) MakeSmartArtSession()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var smartArt = new SmartArtShape
        {
            Data = new SmartArtData
            {
                Family = SmartArtFamily.Process,
                LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicProcess",
                IsLiveLayoutSupported = true,
            },
            DrawingPartPath = "ppt/diagrams/drawing1.xml",
        };
        smartArt.Data.Nodes.Add(new SmartArtNode { ModelId = "n1", Text = "Plan", Level = 0 });
        smartArt.Data.Nodes.Add(new SmartArtNode { ModelId = "n2", Text = "Build", Level = 0 });
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = System.Text.Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />"),
        };
        smartArt.Parts["ppt/diagrams/layout1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/layout1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml",
            Bytes = System.Text.Encoding.UTF8.GetBytes(
                "<dgm:layoutDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" uniqueId=\"old\" />"),
        };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = System.Text.Encoding.UTF8.GetBytes(
                "<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />"),
        };

        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.SmartArt,
            SmartArt = smartArt,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 7_315_200,
            ExtentCyEmu = 3_657_600,
        };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        return (new EditingSession(presentation, new PresentationCommandBus(presentation)), smartArt);
    }

    [Fact]
    public void ReplaceSmartArtNodePicture_UpdatesExistingImportedCacheOnlyPicture()
    {
        var (session, smartArt) = MakeSmartArtSession();
        var oldBytes = new byte[] { 1, 2, 3 };
        var newBytes = new byte[] { 9, 8, 7, 6 };
        smartArt.Data!.LayoutUniqueId =
            "urn:microsoft.com/office/officeart/2005/8/layout/nonDirectionalCycle";
        smartArt.Data.IsLiveLayoutSupported = false;
        smartArt.Data.Nodes[0].Picture = new ImagePart { Bytes = oldBytes, ContentType = "image/png" };
        smartArt.FallbackShapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            Fill = new ShapeFill.Picture(oldBytes.ToArray(), "image/png"),
        });

        smartArt.Parts[smartArt.DrawingPartPath!] = new DiagramPart
        {
            PartPath = smartArt.DrawingPartPath!,
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = System.Text.Encoding.UTF8.GetBytes(
                "<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" " +
                "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<dsp:spTree><dsp:sp modelId=\"n1\"><dsp:spPr><a:blipFill>" +
                "<a:blip r:embed=\"rIdPic1\"/></a:blipFill></dsp:spPr></dsp:sp></dsp:spTree></dsp:drawing>"),
        };
        smartArt.PartRels[smartArt.DrawingPartPath!] = System.Text.Encoding.UTF8.GetBytes(
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rIdPic1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" " +
            "Target=\"../media/image1.png\"/></Relationships>");
        smartArt.Parts["ppt/media/image1.png"] = new DiagramPart
        {
            PartPath = "ppt/media/image1.png",
            ContentType = "image/png",
            Bytes = oldBytes.ToArray(),
        };

        var result = session.ReplaceSmartArtNodePicture(7, "n1", newBytes, "image/png");

        result.Applied.Should().BeTrue(result.Message);
        var updated = session.CurrentSlide!.Shapes.Single().SmartArt!;
        updated.Data!.Nodes[0].Picture!.Bytes.Should().Equal(newBytes);
        updated.Parts["ppt/media/image1.png"].Bytes.Should().Equal(newBytes);
        updated.FallbackShapes.Single().Fill.Should().BeOfType<ShapeFill.Picture>()
            .Which.ImageBytes.Should().Equal(newBytes);

        var cleared = session.ClearSmartArtNodePicture(7, "n1");

        cleared.Applied.Should().BeTrue(cleared.Message);
        var clearedSmartArt = session.CurrentSlide!.Shapes.Single().SmartArt!;
        clearedSmartArt.Data!.Nodes[0].Picture.Should().BeNull();
        clearedSmartArt.Parts.Should().NotContainKey("ppt/media/image1.png");
        clearedSmartArt.FallbackShapes.Should().BeEmpty();
        Encoding.UTF8.GetString(clearedSmartArt.Parts[clearedSmartArt.DrawingPartPath!].Bytes)
            .Should().NotContain("<dsp:pic");
        Encoding.UTF8.GetString(clearedSmartArt.PartRels[clearedSmartArt.DrawingPartPath!])
            .Should().NotContain("/image");
    }

    [Fact]
    public void ReplaceSmartArtNodePicture_AttachesToExistingCachedShapeWithoutImageSlot()
    {
        var (session, smartArt) = MakeSmartArtSession();
        var drawingPath = smartArt.DrawingPartPath!;
        smartArt.Data!.LayoutUniqueId =
            "urn:microsoft.com/office/officeart/2005/8/layout/nonDirectionalCycle";
        smartArt.Data.IsLiveLayoutSupported = false;
        smartArt.FallbackShapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 100,
            OffsetYEmu = 200,
            ExtentCxEmu = 3_000,
            ExtentCyEmu = 2_000,
        });
        smartArt.Parts[drawingPath] = new DiagramPart
        {
            PartPath = drawingPath,
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" " +
                "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
                "<dsp:spTree><dsp:sp modelId=\"n1\"><dsp:nvSpPr><dsp:cNvPr id=\"1\" name=\"Node 1\"/>" +
                "</dsp:nvSpPr><dsp:spPr><a:xfrm><a:off x=\"100\" y=\"200\"/>" +
                "<a:ext cx=\"3000\" cy=\"2000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/>" +
                "</a:prstGeom></dsp:spPr></dsp:sp></dsp:spTree></dsp:drawing>"),
        };
        smartArt.PartRels[drawingPath] = Encoding.UTF8.GetBytes(
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\" />");

        var imageBytes = new byte[] { 4, 5, 6, 7 };
        var result = session.ReplaceSmartArtNodePicture(7, "n1", imageBytes, "image/png");

        result.Applied.Should().BeTrue(result.Message);
        var updated = session.CurrentSlide!.Shapes.Single().SmartArt!;
        updated.FallbackShapes.Single().Fill.Should().BeOfType<ShapeFill.Picture>()
            .Which.ImageBytes.Should().Equal(imageBytes);
        var drawingXml = Encoding.UTF8.GetString(updated.Parts[drawingPath].Bytes);
        drawingXml.Should().Contain("blipFill");
        drawingXml.Should().Contain("rIdFreePSmartArtPic");
        Encoding.UTF8.GetString(updated.PartRels[drawingPath])
            .Should().Contain("/image");
        updated.Parts.Keys.Should().Contain(path => path.Contains("freep-smartart-picture", StringComparison.Ordinal));

        var cleared = session.ClearSmartArtNodePicture(7, "n1");

        cleared.Applied.Should().BeTrue(cleared.Message);
        var clearedSmartArt = session.CurrentSlide!.Shapes.Single().SmartArt!;
        clearedSmartArt.FallbackShapes.Should().BeEmpty();
        Encoding.UTF8.GetString(clearedSmartArt.Parts[drawingPath].Bytes)
            .Should().NotContain("blipFill");
        Encoding.UTF8.GetString(clearedSmartArt.PartRels[drawingPath])
            .Should().NotContain("/image");
        clearedSmartArt.Parts.Keys.Should().NotContain(path =>
            path.Contains("freep-smartart-picture", StringComparison.Ordinal));
    }

    // ── Construction ──────────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_SingleSlidePresentation_CurrentSlideIsSlide0()
    {
        var sess = Make();
        sess.CurrentSlideIndex.Should().Be(0);
        sess.CurrentSlide.Should().NotBeNull();
    }

    [Fact]
    public void Ctor_EmptyPresentation_CurrentSlideIsMinusOne()
    {
        var p   = new Presentation();
        var bus = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        sess.CurrentSlideIndex.Should().Be(-1);
        sess.CurrentSlide.Should().BeNull();
    }

    [Fact]
    public void SetSlideNotesText_UsesTargetSlideAndRemainsUndoable()
    {
        var sess = Make(2);

        sess.SetSlideNotesText(1, "First line\nSecond line");

        sess.CurrentSlideIndex.Should().Be(0, "Presenter View navigation must not move editor selection");
        sess.Presentation.Slides[0].Notes.Should().BeNull();
        sess.Presentation.Slides[1].Notes!.Paragraphs
            .Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text)))
            .Should().Equal("First line", "Second line");

        sess.Undo();
        sess.Presentation.Slides[1].Notes.Should().BeNull();
        sess.Redo();
        sess.Presentation.Slides[1].Notes!.Paragraphs
            .Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text)))
            .Should().Equal("First line", "Second line");
    }

    [Fact]
    public void SetSlideNotesText_PreservesMixedRunFormattingAcrossEditedLines()
    {
        var sess = Make();
        sess.Presentation.Slides[0].Notes = new TextBody
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new Run { Text = "Bold", Bold = true, BoldSet = true },
                        new Run { Text = " and italic", Italic = true, ItalicSet = true },
                    }
                }
            }
        };

        sess.SetCurrentSlideNotesText("Bold and italic plus");

        var runs = sess.CurrentSlideNotes!.Paragraphs.Single().Runs;
        runs.Select(run => run.Text).Should().Equal("Bold", " and italic", " plus");
        runs[0].Bold.Should().BeTrue();
        runs[0].Italic.Should().BeFalse();
        runs[1].Italic.Should().BeTrue();
        runs[1].Bold.Should().BeFalse();
        runs[2].Italic.Should().BeTrue();

        sess.Undo();
        sess.CurrentSlideNotes!.Paragraphs.Single().Runs.Select(run => run.Text)
            .Should().Equal("Bold", " and italic");
        sess.Redo();
        sess.CurrentSlideNotes!.Paragraphs.Single().Runs.Select(run => run.Text)
            .Should().Equal("Bold", " and italic", " plus");
    }

    [Fact]
    public void TryApplyCurrentSlideNotesTextFormat_UsesLogicalSelectionAndUndo()
    {
        var sess = Make();
        sess.SetCurrentSlideNotesText("first line\nsecond line");

        sess.TryApplyCurrentSlideNotesTextFormat(
            TableCellTextFormatKind.Bold,
            (12, 23),
            "first line\r\nsecond line").Should().BeTrue();

        var notes = sess.CurrentSlideNotes!;
        notes.Paragraphs[0].Runs.Single().Bold.Should().BeFalse();
        notes.Paragraphs[1].Runs.Single().Bold.Should().BeTrue();
        notes.Paragraphs[1].Runs.Single().BoldSet.Should().BeTrue();

        sess.Undo();
        sess.CurrentSlideNotes!.Paragraphs.SelectMany(p => p.Runs)
            .Should().OnlyContain(run => !run.Bold);
        sess.Redo();
        sess.CurrentSlideNotes!.Paragraphs[1].Runs.Single().Bold.Should().BeTrue();
    }

    [Fact]
    public void TryApplyCurrentSlideNotesTextFormat_Strikethrough_IsUndoable()
    {
        var sess = Make();
        sess.SetCurrentSlideNotesText("speaker note");

        sess.TryApplyCurrentSlideNotesTextFormat(
            TableCellTextFormatKind.Strikethrough,
            (0, 12),
            "speaker note").Should().BeTrue();

        var run = sess.CurrentSlideNotes!.Paragraphs.Single().Runs.Single();
        run.Strikethrough.Should().BeTrue();
        run.StrikeStyleToken.Should().Be("sngStrike");

        sess.Undo();
        var revertedRun = sess.CurrentSlideNotes!.Paragraphs.Single().Runs.Single();
        revertedRun.Strikethrough.Should().BeFalse();
        revertedRun.StrikeStyleToken.Should().BeNull();
    }

    [Fact]
    public void TryApplyCurrentSlideNotesValueFormat_UsesLogicalSelectionAndUndo()
    {
        var sess = Make();
        sess.SetCurrentSlideNotesText("first line\nsecond line");

        sess.TryApplyCurrentSlideNotesValueFormat(
            TableCellTextValueFormatKind.FontFamily,
            "Arial",
            (12, 23),
            "first line\r\nsecond line").Should().BeTrue();
        sess.TryApplyCurrentSlideNotesValueFormat(
            TableCellTextValueFormatKind.FontSize,
            18d,
            (12, 23),
            "first line\r\nsecond line").Should().BeTrue();
        sess.TryApplyCurrentSlideNotesValueFormat(
            TableCellTextValueFormatKind.Color,
            new ThemeAwareColor(new SrgbColor(0x12, 0x34, 0x56)),
            (12, 23),
            "first line\r\nsecond line").Should().BeTrue();

        var first = sess.CurrentSlideNotes!.Paragraphs[0].Runs.Single();
        var second = sess.CurrentSlideNotes.Paragraphs[1].Runs.Single();
        first.FontFamily.Should().BeNull();
        second.FontFamily.Should().Be("Arial");
        second.FontSizePt.Should().Be(18d);
        second.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x123456));

        sess.Undo();
        sess.Undo();
        sess.Undo();
        sess.CurrentSlideNotes.Paragraphs.SelectMany(p => p.Runs)
            .Should().OnlyContain(run => run.FontFamily == null && run.FontSizePt == null && run.Color == null);
    }

    [Fact]
    public void TryApplyCurrentSlideNotesParagraphFormat_UpdatesSelectedParagraphs()
    {
        var sess = Make();
        sess.SetCurrentSlideNotesText("first\nsecond");
        var displayText = "first\r\nsecond";

        sess.TryApplyCurrentSlideNotesParagraphFormat(
            TableCellParagraphFormatKind.Alignment,
            TextAlign.Center,
            (0, displayText.Length),
            displayText).Should().BeTrue();
        sess.TryApplyCurrentSlideNotesParagraphFormat(
            TableCellParagraphFormatKind.BulletToggle,
            selection: (0, displayText.Length),
            displayText: displayText).Should().BeTrue();
        sess.TryApplyCurrentSlideNotesParagraphFormat(
            TableCellParagraphFormatKind.Indent,
            selection: (0, displayText.Length),
            displayText: displayText).Should().BeTrue();

        sess.CurrentSlideNotes!.Paragraphs.Should().OnlyContain(paragraph =>
            paragraph.Align == TextAlign.Center
            && paragraph.BulletKind == BulletKind.Char
            && paragraph.Level == 1
            && paragraph.MarginLeftEmu.HasValue
            && paragraph.MarginLeftEmu.Value > 0);

        sess.Undo();
        sess.Undo();
        sess.Undo();
        sess.CurrentSlideNotes.Paragraphs.Should().OnlyContain(paragraph =>
            paragraph.Align == null
            && paragraph.BulletKind == BulletKind.None
            && paragraph.Level == 0);
    }

    [Fact]
    public void CustomGeometryVertexInsertAndDelete_RouteThroughUndoableSession()
    {
        var session = Make();
        var shape = MakeShape(1);
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 100, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 50, Y: 100));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
        shape.CustomGeometry.Add(path);
        session.CurrentSlide!.Shapes.Add(shape);

        session.TryInsertCustomGeometryPoint(1, "custom:0:1").Should().BeTrue();
        path.Segments.Should().HaveCount(5);
        session.Bus.Undo();
        path.Segments.Should().HaveCount(4);
        session.Bus.Redo();
        path.Segments.Should().HaveCount(5);

        session.TryDeleteCustomGeometryPoint(1, "custom:0:2").Should().BeTrue();
        path.Segments.Should().HaveCount(4);
        session.Bus.Undo();
        path.Segments.Should().HaveCount(5);
    }

    [Fact]
    public void GroupedChildPictureAndGeometryEdits_RouteThroughUndoableSession()
    {
        var session = Make();
        var group = new SlideShape { Id = 10, Name = "Group", Kind = SlideShapeKind.Group };
        var custom = MakeShape(11);
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 100, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 50, Y: 100));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
        custom.CustomGeometry.Add(path);
        var picture = new SlideShape
        {
            Id = 12,
            Name = "Grouped Picture",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = [1, 2, 3], ContentType = "image/png" },
        };
        group.Children.Add(custom);
        group.Children.Add(picture);
        session.CurrentSlide!.Shapes.Add(group);

        session.TryInsertCustomGeometryPoint(11, "custom:0:1").Should().BeTrue();
        session.TryDeleteCustomGeometryPoint(11, "custom:0:2").Should().BeTrue();
        session.SetPictureCrop(12, new PictureCropValues(0.1, 0.2, 0.1, 0.05)).Should().BeTrue();
        session.SetPictureColorEffects(12, PictureColorEffectAuthoringPlanner.Grayscale()).Should().BeTrue();

        picture.PictureFormat.Should().NotBeNull();
        picture.PictureFormat!.CropLeft.Should().Be(0.1);
        picture.PictureFormat.CropTop.Should().Be(0.2);
        picture.PictureFormat.Grayscale.Should().BeTrue();
        session.Bus.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void ApplySmartArtLayout_RefreshesNativeDataAndDrawingCacheThroughSharedSession()
    {
        var (session, _) = MakeSmartArtSession();

        session.ApplySmartArtLayout(7, SmartArtLayoutPreset.BasicProcess).Should().BeTrue();

        var saved = session.CurrentSlide!.Shapes.Single().SmartArt!;
        saved.Data!.LayoutUniqueId.Should().EndWith("/layout/basicProcess");
        saved.FallbackShapes.Should().NotBeEmpty();
        saved.Parts["ppt/diagrams/data1.xml"].Bytes.Should().NotBeEmpty();
        saved.Parts["ppt/diagrams/drawing1.xml"].Bytes.Should().Contain((byte)'P');
        session.Bus.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void RegenerateSmartArtDrawingCache_PreservesAuthoredEffectsAndTextFormattingByModelId()
    {
        var (_, smartArt) = MakeSmartArtSession();
        var container = new SlideShape
        {
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 7_315_200,
            ExtentCyEmu = 3_657_600,
        };

        SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            container.OffsetXEmu,
            container.OffsetYEmu,
            container.ExtentCxEmu,
            container.ExtentCyEmu,
            new PresentationTheme()).Applied.Should().BeTrue();

        var drawingPath = smartArt.DrawingPartPath!;
        var drawing = System.Xml.Linq.XDocument.Parse(
            System.Text.Encoding.UTF8.GetString(smartArt.Parts[drawingPath].Bytes));
        var dsp = System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var a = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var sourceShape = drawing.Descendants(dsp + "sp").First();
        var modelId = sourceShape.Attribute("modelId")!.Value;
        smartArt.FallbackShapes.Select(shape => shape.Id.ToString()).Should().Contain(modelId);
        var sourceParagraph = sourceShape.Element(dsp + "txBody")!.Element(a + "p")!;
        sourceParagraph.AddFirst(new System.Xml.Linq.XElement(
            a + "pPr",
            new System.Xml.Linq.XAttribute("algn", "ctr")));
        sourceParagraph.Element(a + "r")!.Element(a + "rPr")!
            .SetAttributeValue("b", "1");
        sourceShape.Element(dsp + "spPr")!.Add(
            new System.Xml.Linq.XElement(
                a + "effectLst",
                new System.Xml.Linq.XElement(
                    a + "outerShdw",
                    new System.Xml.Linq.XAttribute("blurRad", "50800"),
                    new System.Xml.Linq.XAttribute("dist", "38100"),
                    new System.Xml.Linq.XAttribute("dir", "2700000"),
                    new System.Xml.Linq.XAttribute("algn", "ctr"))));
        smartArt.Parts[drawingPath].Bytes = System.Text.Encoding.UTF8.GetBytes(drawing.ToString());
        smartArt.Data!.Nodes[0].Text = "Updated";

        SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            container.OffsetXEmu,
            container.OffsetYEmu,
            container.ExtentCxEmu,
            container.ExtentCyEmu,
            new PresentationTheme()).Applied.Should().BeTrue();

        var updated = System.Xml.Linq.XDocument.Parse(
            System.Text.Encoding.UTF8.GetString(smartArt.Parts[drawingPath].Bytes));
        var updatedShape = updated.Descendants(dsp + "sp")
            .Single(shape => shape.Attribute("modelId")?.Value == modelId);
        var updatedProperties = updatedShape.Element(dsp + "spPr");
        updatedProperties.Should().NotBeNull();
        var updatedEffects = updatedProperties?.Element(a + "effectLst");
        updatedEffects.Should().NotBeNull();
        updatedEffects!.Element(a + "outerShdw")!.Attribute("blurRad")!.Value.Should().Be("50800");
        var updatedParagraph = updatedShape.Element(dsp + "txBody")!.Element(a + "p")!;
        updatedParagraph.Element(a + "pPr")!.Attribute("algn")!.Value.Should().Be("ctr");
        updatedParagraph.Element(a + "r")!.Element(a + "rPr")!.Attribute("b")!.Value.Should().Be("1");
        updatedShape.Descendants(a + "t").Should().Contain(element => element.Value == "Updated");
    }

    [Fact]
    public void SynchronizePreservedDrawingText_UpdatesOneCachedShapeWithoutRebuildingLayout()
    {
        var (_, smartArt) = MakeSmartArtSession();
        smartArt.Data!.IsLiveLayoutSupported = false;
        var previousData = SlideCloner.CloneSmartArt(smartArt).Data!;
        var drawingPart = smartArt.Parts[smartArt.DrawingPartPath!];
        drawingPart.Bytes = System.Text.Encoding.UTF8.GetBytes(
            "<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><dsp:spTree>" +
            "<dsp:sp><dsp:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr b=\"1\"/><a:t>Plan</a:t></a:r></a:p></dsp:txBody></dsp:sp>" +
            "</dsp:spTree></dsp:drawing>");
        smartArt.FallbackShapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Plan", Bold = true } },
                    },
                },
            },
        });

        SmartArtEditingPlanner.Apply(
            smartArt.Data,
            SmartArtNodeEditIntent.ChangeText("n1", "Plan revised"))
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();

        var regenerated = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt, 0, 0, 7_315_200, 3_657_600, new PresentationTheme());
        regenerated.Applied.Should().BeFalse();

        var synchronized = SmartArtEditingPlanner.SynchronizePreservedDrawingText(
            smartArt, previousData);
        synchronized.Applied.Should().BeTrue(synchronized.Message);
        smartArt.FallbackShapes.Single().PlainText.Should().Be("Plan revised");
        System.Text.Encoding.UTF8.GetString(drawingPart.Bytes).Should().Contain("Plan revised");
        System.Text.Encoding.UTF8.GetString(drawingPart.Bytes).Should().Contain("b=\"1\"");
    }

    [Fact]
    public void SynchronizePreservedDrawingText_RejectsStructuralChanges()
    {
        var (_, smartArt) = MakeSmartArtSession();
        smartArt.Data!.IsLiveLayoutSupported = false;
        var previousData = SlideCloner.CloneSmartArt(smartArt).Data!;

        smartArt.Data.Nodes.Add(new SmartArtNode { ModelId = "n3", Text = "Ship", Level = 0 });

        SmartArtEditingPlanner.SynchronizePreservedDrawingText(smartArt, previousData)
            .Applied.Should().BeFalse();
    }

    [Fact]
    public void SynchronizePreservedDrawingText_UpdatesMultipleCachedShapesWithoutRebuildingLayout()
    {
        var (_, smartArt) = MakeSmartArtSession();
        smartArt.Data!.IsLiveLayoutSupported = false;
        var previousData = SlideCloner.CloneSmartArt(smartArt).Data!;
        var drawingPart = smartArt.Parts[smartArt.DrawingPartPath!];
        drawingPart.Bytes = System.Text.Encoding.UTF8.GetBytes(
            "<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><dsp:spTree>" +
            "<dsp:sp><dsp:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Plan</a:t></a:r></a:p></dsp:txBody></dsp:sp>" +
            "<dsp:sp><dsp:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Build</a:t></a:r></a:p></dsp:txBody></dsp:sp>" +
            "</dsp:spTree></dsp:drawing>");
        smartArt.FallbackShapes.AddRange(new[] { "Plan", "Build" }.Select(text => new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph { Runs = { new Run { Text = text } } },
                },
            },
        }));

        SmartArtEditingPlanner.Apply(
            smartArt.Data,
            SmartArtNodeEditIntent.ChangeText("n1", "Discover")).Applied.Should().BeTrue();
        SmartArtEditingPlanner.Apply(
            smartArt.Data,
            SmartArtNodeEditIntent.ChangeText("n2", "Construct")).Applied.Should().BeTrue();
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();

        SmartArtEditingPlanner.SynchronizePreservedDrawingText(smartArt, previousData)
            .Should().Match<SmartArtDrawingCacheRegenerationResult>(result =>
                result.Applied && result.Message.StartsWith("2 text edits", StringComparison.Ordinal));
        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Equal("Discover", "Construct");
        var raw = System.Text.Encoding.UTF8.GetString(drawingPart.Bytes);
        raw.Should().Contain("Discover").And.Contain("Construct")
            .And.NotContain(">Plan<").And.NotContain(">Build<");
    }

    [Fact]
    public void SynchronizePreservedDrawingText_UpdatesDuplicateSourceTextByVerifiedOrdinalMapping()
    {
        var (_, smartArt) = MakeSmartArtSession();
        smartArt.Data!.IsLiveLayoutSupported = false;
        smartArt.Data.Nodes[1].Text = "Plan";
        var previousData = SlideCloner.CloneSmartArt(smartArt).Data!;
        var drawingPart = smartArt.Parts[smartArt.DrawingPartPath!];
        drawingPart.Bytes = System.Text.Encoding.UTF8.GetBytes(
            "<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><dsp:spTree>" +
            "<dsp:sp><dsp:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Plan</a:t></a:r></a:p></dsp:txBody></dsp:sp>" +
            "<dsp:sp><dsp:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Plan</a:t></a:r></a:p></dsp:txBody></dsp:sp>" +
            "</dsp:spTree></dsp:drawing>");
        smartArt.FallbackShapes.AddRange(Enumerable.Range(0, 2).Select(_ => new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph { Runs = { new Run { Text = "Plan" } } },
                },
            },
        }));

        SmartArtEditingPlanner.Apply(
            smartArt.Data,
            SmartArtNodeEditIntent.ChangeText("n1", "Discover")).Applied.Should().BeTrue();
        SmartArtEditingPlanner.Apply(
            smartArt.Data,
            SmartArtNodeEditIntent.ChangeText("n2", "Construct")).Applied.Should().BeTrue();
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();

        var synchronized = SmartArtEditingPlanner.SynchronizePreservedDrawingText(smartArt, previousData);

        synchronized.Applied.Should().BeTrue(synchronized.Message);
        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Equal("Discover", "Construct");
        var raw = System.Text.Encoding.UTF8.GetString(drawingPart.Bytes);
        raw.Should().Contain("Discover").And.Contain("Construct");
    }

    [Fact]
    public void SynchronizePreservedDrawingText_RejectsParagraphShapeChangesAtomically()
    {
        var (_, smartArt) = MakeSmartArtSession();
        smartArt.Data!.IsLiveLayoutSupported = false;
        var previousData = SlideCloner.CloneSmartArt(smartArt).Data!;
        var drawingPart = smartArt.Parts[smartArt.DrawingPartPath!];
        drawingPart.Bytes = System.Text.Encoding.UTF8.GetBytes(
            "<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><dsp:spTree>" +
            "<dsp:sp><dsp:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Plan</a:t></a:r></a:p></dsp:txBody></dsp:sp>" +
            "</dsp:spTree></dsp:drawing>");
        smartArt.FallbackShapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph { Runs = { new Run { Text = "Plan" } } },
                },
            },
        });

        SmartArtEditingPlanner.Apply(
            smartArt.Data,
            SmartArtNodeEditIntent.ChangeText("n1", "Discover\nMore")).Applied.Should().BeTrue();
        var originalBytes = drawingPart.Bytes.ToArray();
        var originalFallback = smartArt.FallbackShapes.Single().PlainText;

        var result = SmartArtEditingPlanner.SynchronizePreservedDrawingText(smartArt, previousData);

        result.Applied.Should().BeFalse();
        result.Message.Should().Contain("does not fit");
        drawingPart.Bytes.Should().Equal(originalBytes);
        smartArt.FallbackShapes.Single().PlainText.Should().Be(originalFallback);
    }

    [Fact]
    public void GroupedSmartArt_LayoutAndConvertRoutesRemainUndoable()
    {
        var (session, _) = MakeSmartArtSession();
        var slide = session.CurrentSlide!;
        var smartArt = slide.Shapes.Single();
        slide.Shapes.Clear();
        var group = new SlideShape { Id = 70, Name = "Group", Kind = SlideShapeKind.Group };
        group.Children.Add(smartArt);
        slide.Shapes.Add(group);

        session.ApplySmartArtLayout(7, SmartArtLayoutPreset.BasicProcess).Should().BeTrue();
        ShapeHitTester.FindShape(slide, 7)!.SmartArt!.Data!.LayoutUniqueId
            .Should().EndWith("/layout/basicProcess");
        session.Undo();
        session.Redo();

        session.ConvertSmartArtToShapes(7).Should().BeTrue();
        group.Children.Should().NotContain(child => child.Id == 7);
        group.Children.Should().NotBeEmpty();
        session.Undo();
        group.Children.Should().ContainSingle(child => child.Id == 7 && child.Kind == SlideShapeKind.SmartArt);
    }

    [Fact]
    public void ApplySmartArtPictureLayout_RefreshesPlaceholdersAndRemainsUndoableWithoutImages()
    {
        var (session, _) = MakeSmartArtSession();

        session.ApplySmartArtLayout(7, SmartArtLayoutPreset.PictureCaptionList).Should().BeTrue();

        var saved = session.CurrentSlide!.Shapes.Single().SmartArt!;
        saved.Data!.LayoutUniqueId.Should().EndWith("/layout/pictureCaptionList");
        saved.FallbackShapes.Should().Contain(shape => shape.PlainText == "Add picture");
        saved.FallbackShapes.Should().Contain(shape => shape.PlainText == "Plan");
        saved.FallbackShapes.Should().Contain(shape => shape.PlainText == "Build");
        session.Bus.CanUndo.Should().BeTrue();

        session.Undo();
        session.CurrentSlide.Shapes.Single().SmartArt!.Data!.LayoutUniqueId
            .Should().EndWith("/layout/basicProcess");
        session.Redo();
        session.CurrentSlide.Shapes.Single().SmartArt!.Data!.LayoutUniqueId
            .Should().EndWith("/layout/pictureCaptionList");
    }

    [Fact]
    public void ApplySmartArtLayout_WhenDrawingCacheIsMissing_RecreatesAndRemainsUndoable()
    {
        var (session, _) = MakeSmartArtSession();
        var smartArt = session.CurrentSlide!.Shapes.Single().SmartArt!;
        var originalLayout = smartArt.Data!.LayoutUniqueId;
        smartArt.Parts.Remove("ppt/diagrams/drawing1.xml");

        session.ApplySmartArtLayout(7, SmartArtLayoutPreset.BasicCycle).Should().BeTrue();

        var updated = session.CurrentSlide.Shapes.Single().SmartArt!;
        updated.Data!.LayoutUniqueId.Should().EndWith("/layout/basicCycle");
        updated.Parts.Should().ContainKey("ppt/diagrams/drawing1.xml");
        updated.FallbackShapes.Should().NotBeEmpty();
        session.Bus.CanUndo.Should().BeTrue();

        session.Undo();
        session.CurrentSlide.Shapes.Single().SmartArt!.Data!.LayoutUniqueId.Should().Be(originalLayout);
        session.CurrentSlide.Shapes.Single().SmartArt!.Parts.Should()
            .NotContainKey("ppt/diagrams/drawing1.xml");
        session.Redo();
        session.CurrentSlide.Shapes.Single().SmartArt!.Parts.Should()
            .ContainKey("ppt/diagrams/drawing1.xml");
    }

    [Fact]
    public void ApplySmartArtQuickStyle_UsesNativePartWhenLiveDataIsMissing()
    {
        var (session, smartArt) = MakeSmartArtSession();
        smartArt.Data = null;
        smartArt.FallbackShapes.Add(new SlideShape
        {
            Id = 20,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4))),
            TextBody = new TextBody
            {
                Paragraphs = { new Paragraph { Runs = { new Run { Text = "Cached node" } } } }
            }
        });
        smartArt.Parts["ppt/diagrams/quickStyle1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/quickStyle1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml",
            Bytes = System.Text.Encoding.UTF8.GetBytes(
                "<dgm:styleDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />"),
        };
        var originalBytes = smartArt.Parts["ppt/diagrams/quickStyle1.xml"].Bytes.ToArray();
        var originalFill = smartArt.FallbackShapes.Single().Fill;

        session.ApplySmartArtQuickStyle(7, SmartArtQuickStylePreset.Polished).Should().BeTrue();

        var saved = session.CurrentSlide!.Shapes.Single().SmartArt!;
        saved.Data.Should().BeNull();
        saved.QuickStyle!.UniqueId.Should().Contain("/quickstyle/3d1");
        saved.Parts["ppt/diagrams/quickStyle1.xml"].Bytes.Should().NotEqual(originalBytes);
        saved.FallbackShapes.Single().Fill.Should().NotBeSameAs(originalFill);
        saved.FallbackShapes.Single().Outline.Should().BeOfType<ShapeOutline.Visible>();
        saved.FallbackShapes.Single().TextBody!.Paragraphs.Single().Runs.Single().Color!.Resolved
            .Should().Be(SrgbColor.White);

        session.Undo();
        session.CurrentSlide.Shapes.Single().SmartArt!.Parts["ppt/diagrams/quickStyle1.xml"].Bytes
            .Should().Equal(originalBytes);
        session.Redo();
        session.CurrentSlide.Shapes.Single().SmartArt!.QuickStyle!.UniqueId
            .Should().Contain("/quickstyle/3d1");
        session.CurrentSlide.Shapes.Single().SmartArt!.FallbackShapes.Single().Outline
            .Should().BeOfType<ShapeOutline.Visible>();
    }

    [Fact]
    public void ApplySmartArtColor_UsesNativePartWhenLiveDataIsMissing()
    {
        var (session, smartArt) = MakeSmartArtSession();
        smartArt.Data = null;
        smartArt.FallbackShapes.Add(new SlideShape
        {
            Id = 20,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4))),
            TextBody = new TextBody
            {
                Paragraphs = { new Paragraph { Runs = { new Run { Text = "Cached node" } } } }
            }
        });
        smartArt.Parts["ppt/diagrams/colors1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/colors1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml",
            Bytes = System.Text.Encoding.UTF8.GetBytes(
                "<dgm:colorsDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><dgm:styleLbl name=\"node0\"><dgm:fillClrLst><a:schemeClr val=\"accent1\" /></dgm:fillClrLst></dgm:styleLbl></dgm:colorsDef>"),
        };

        session.ApplySmartArtColor(7, SmartArtColorPreset.ColoredFillAccent2).Should().BeTrue();

        var saved = session.CurrentSlide!.Shapes.Single().SmartArt!;
        saved.Data.Should().BeNull();
        saved.Colors!.Palette.Should().NotBeEmpty();
        saved.FallbackShapes.Single().Fill.Should().BeOfType<ShapeFill.Solid>()
            .Which.Color.Resolved.Should().Be(SrgbColor.FromRgb(0xED7D31));
        saved.Parts["ppt/diagrams/colors1.xml"].Bytes.Should().Contain((byte)'2');
        session.Bus.CanUndo.Should().BeTrue();

        session.Undo();
        session.CurrentSlide.Shapes.Single().SmartArt!.FallbackShapes.Single().Fill
            .Should().BeOfType<ShapeFill.Solid>()
            .Which.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
    }

    [Fact]
    public void ApplySmartArtQuickStyle_UsesCachedFallbackWhenLiveLayoutIsUnsupported()
    {
        var (session, smartArt) = MakeSmartArtSession();
        smartArt.Data!.Family = SmartArtFamily.Unknown;
        smartArt.Data.IsLiveLayoutSupported = false;
        smartArt.FallbackShapes.Add(new SlideShape
        {
            Id = 20,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4))),
            TextBody = new TextBody
            {
                Paragraphs = { new Paragraph { Runs = { new Run { Text = "Imported node" } } } }
            }
        });
        smartArt.Parts["ppt/diagrams/quickStyle1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/quickStyle1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml",
            Bytes = System.Text.Encoding.UTF8.GetBytes(
                "<dgm:styleDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />"),
        };
        var originalBytes = smartArt.Parts["ppt/diagrams/quickStyle1.xml"].Bytes.ToArray();

        session.ApplySmartArtQuickStyle(7, SmartArtQuickStylePreset.Polished).Should().BeTrue();

        var saved = session.CurrentSlide!.Shapes.Single().SmartArt!;
        saved.Data!.IsLiveLayoutSupported.Should().BeFalse();
        saved.QuickStyle!.UniqueId.Should().Contain("/quickstyle/3d1");
        saved.Parts["ppt/diagrams/quickStyle1.xml"].Bytes.Should().NotEqual(originalBytes);
        saved.FallbackShapes.Single().Outline.Should().BeOfType<ShapeOutline.Visible>();
        session.Bus.CanUndo.Should().BeTrue();

        session.Undo();
        session.CurrentSlide.Shapes.Single().SmartArt!.FallbackShapes.Single().Outline
            .Should().BeNull();
        session.Redo();
        session.CurrentSlide.Shapes.Single().SmartArt!.FallbackShapes.Single().Outline
            .Should().BeOfType<ShapeOutline.Visible>();
    }

    [Fact]
    public void ApplySmartArtColor_UsesCachedFallbackWhenLiveLayoutIsUnsupported()
    {
        var (session, smartArt) = MakeSmartArtSession();
        smartArt.Data!.Family = SmartArtFamily.Unknown;
        smartArt.Data.IsLiveLayoutSupported = false;
        smartArt.FallbackShapes.Add(new SlideShape
        {
            Id = 20,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4))),
            TextBody = new TextBody
            {
                Paragraphs = { new Paragraph { Runs = { new Run { Text = "Imported node" } } } }
            }
        });
        smartArt.Parts["ppt/diagrams/colors1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/colors1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml",
            Bytes = System.Text.Encoding.UTF8.GetBytes(
                "<dgm:colorsDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><dgm:styleLbl name=\"node0\"><dgm:fillClrLst><a:schemeClr val=\"accent1\" /></dgm:fillClrLst></dgm:styleLbl></dgm:colorsDef>"),
        };

        session.ApplySmartArtColor(7, SmartArtColorPreset.ColoredFillAccent2).Should().BeTrue();

        var saved = session.CurrentSlide!.Shapes.Single().SmartArt!;
        saved.Data!.IsLiveLayoutSupported.Should().BeFalse();
        saved.Colors!.Palette.Should().NotBeEmpty();
        saved.FallbackShapes.Single().Fill.Should().BeOfType<ShapeFill.Solid>()
            .Which.Color.Resolved.Should().Be(SrgbColor.FromRgb(0xED7D31));
        session.Bus.CanUndo.Should().BeTrue();

        session.Undo();
        session.CurrentSlide.Shapes.Single().SmartArt!.FallbackShapes.Single().Fill
            .Should().BeOfType<ShapeFill.Solid>()
            .Which.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
    }

    [Fact]
    public void ApplySmartArtLayout_UsesNativePartWhenLiveDataIsMissing()
    {
        var (session, smartArt) = MakeSmartArtSession();
        smartArt.Data = null;
        smartArt.Parts["ppt/diagrams/layout1.xml"].Bytes = System.Text.Encoding.UTF8.GetBytes(
            "<dgm:layoutDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" uniqueId=\"old\" />");
        var originalBytes = smartArt.Parts["ppt/diagrams/layout1.xml"].Bytes.ToArray();

        session.ApplySmartArtLayout(7, SmartArtLayoutPreset.BasicProcess).Should().BeTrue();

        var saved = session.CurrentSlide!.Shapes.Single().SmartArt!;
        saved.Data.Should().BeNull();
        saved.Parts["ppt/diagrams/layout1.xml"].Bytes.Should().NotEqual(originalBytes);
        saved.Parts["ppt/diagrams/layout1.xml"].Bytes.Should().Contain((byte)'b');
        session.Bus.CanUndo.Should().BeTrue();

        session.Undo();
        session.CurrentSlide.Shapes.Single().SmartArt!.Parts["ppt/diagrams/layout1.xml"].Bytes
            .Should().Equal(originalBytes);
        session.Redo();
        session.CurrentSlide.Shapes.Single().SmartArt!.Parts["ppt/diagrams/layout1.xml"].Bytes
            .Should().NotEqual(originalBytes);
    }

    [Fact]
    public void ApplySmartArtLayout_RecoversMissingNativeLayoutPartAndRemainsUndoable()
    {
        var (session, smartArt) = MakeSmartArtSession();
        smartArt.Parts.Remove("ppt/diagrams/layout1.xml");
        smartArt.DiagramRelIds.Remove("lo");

        session.ApplySmartArtLayout(7, SmartArtLayoutPreset.BasicCycle).Should().BeTrue();

        var saved = session.CurrentSlide!.Shapes.Single().SmartArt!;
        saved.Parts.Values.Should().ContainSingle(part =>
            part.ContentType.Contains("diagramLayout", StringComparison.OrdinalIgnoreCase));
        saved.DiagramRelIds.Should().ContainKey("lo");
        saved.Data!.LayoutUniqueId.Should().EndWith("/layout/basicCycle");
        saved.FallbackShapes.Should().NotBeEmpty();
        session.Bus.CanUndo.Should().BeTrue();

        session.Bus.Undo();
        session.CurrentSlide.Shapes.Single().SmartArt!.Parts.Should()
            .NotContainKey("ppt/diagrams/layout1.xml");
        session.CurrentSlide.Shapes.Single().SmartArt!.DiagramRelIds.Should()
            .NotContainKey("lo");

        session.Bus.Redo();
        session.CurrentSlide.Shapes.Single().SmartArt!.Parts.Should()
            .ContainKey("ppt/diagrams/layout1.xml");
    }

    [Fact]
    public void ApplySmartArtLayout_ResolvesNestedGroupChild()
    {
        var (session, smartArt) = MakeSmartArtSession();
        var smartArtShape = session.CurrentSlide!.Shapes.Single();
        session.CurrentSlide.Shapes.Remove(smartArtShape);

        var group = new SlideShape { Id = 20, Kind = SlideShapeKind.Group };
        group.Children.Add(smartArtShape);
        session.CurrentSlide.Shapes.Add(group);

        session.ApplySmartArtLayout(7, SmartArtLayoutPreset.BasicCycle).Should().BeTrue();

        var savedSmartArt = group.Children.Single(shape => shape.Id == 7).SmartArt;
        savedSmartArt.Should().NotBeNull();
        savedSmartArt!.Data!.LayoutUniqueId.Should().EndWith("/layout/basicCycle");
        session.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void ConvertSmartArtToShapes_ReplacesAtSameSlotAndUndoRestoresGraphic()
    {
        var (session, _) = MakeSmartArtSession();
        session.CurrentSlide!.Shapes.Insert(0, MakeShape(2));
        session.CurrentSlide.Shapes.Add(MakeShape(90));

        session.ConvertSmartArtToShapes(7).Should().BeTrue();

        session.CurrentSlide.Shapes.Should().NotContain(shape => shape.Kind == SlideShapeKind.SmartArt);
        session.CurrentSlide.Shapes.First().Id.Should().Be(2);
        session.CurrentSlide.Shapes.Last().Id.Should().Be(90);
        session.CurrentSlide.Shapes.Select(shape => shape.Id).Should().OnlyHaveUniqueItems();
        session.SelectedShapeIds.Should().NotContain(7);
        session.SelectedShapeIds.Should().NotBeEmpty();

        session.Undo();
        session.CurrentSlide.Shapes.Should().HaveCount(3);
        session.CurrentSlide.Shapes[1].Kind.Should().Be(SlideShapeKind.SmartArt);
        session.CurrentSlide.Shapes[1].Id.Should().Be(7);

        session.Redo();
        session.CurrentSlide.Shapes.Should().NotContain(shape => shape.Kind == SlideShapeKind.SmartArt);
        session.CurrentSlide.Shapes.Select(shape => shape.Id).Should().OnlyHaveUniqueItems();
    }

    // ── Slide operations ──────────────────────────────────────────────────────────

    [Fact]
    public void ConvertSmartArtToShapes_DetachesConnectedEndpoints_AndUndoRestoresThem()
    {
        var (session, _) = MakeSmartArtSession();
        var retained = MakeShape(91);
        var connector = new SlideShape
        {
            Id = 90,
            Kind = SlideShapeKind.Connector,
            ConnectionStart = new ConnectorAttachment { ShapeId = 7, SiteIndex = 2 },
            ConnectionEnd = new ConnectorAttachment { ShapeId = retained.Id, SiteIndex = 0 },
        };
        session.CurrentSlide!.Shapes.Add(retained);
        session.CurrentSlide.Shapes.Add(connector);

        session.ConvertSmartArtToShapes(7).Should().BeTrue();

        connector.ConnectionStart.Should().BeNull();
        connector.ConnectionEnd.Should().NotBeNull();
        connector.ConnectionEnd!.ShapeId.Should().Be(retained.Id);

        session.Undo();

        connector.ConnectionStart.Should().NotBeNull();
        connector.ConnectionStart!.ShapeId.Should().Be(7u);
        connector.ConnectionEnd!.ShapeId.Should().Be(retained.Id);

        session.Redo();

        connector.ConnectionStart.Should().BeNull();
        connector.ConnectionEnd!.ShapeId.Should().Be(retained.Id);
    }

    [Fact]
    public void ConvertSmartArtToShapes_UsesCachedFallbackWhenLiveDataIsMissing()
    {
        var (session, smartArt) = MakeSmartArtSession();
        smartArt.Data = null;
        smartArt.FallbackShapes.Add(new SlideShape
        {
            Id = 41,
            Name = "Cached SmartArt node",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 2_000_000,
            ExtentCyEmu = 1_000_000,
        });

        session.ConvertSmartArtToShapes(7).Should().BeTrue();
        session.CurrentSlide!.Shapes.Should().ContainSingle(shape =>
            shape.Kind == SlideShapeKind.AutoShape && shape.Name == "Cached SmartArt node");
        session.SelectedShapeIds.Should().ContainSingle();

        session.Bus.Undo();
        session.CurrentSlide.Shapes.Should().ContainSingle(shape => shape.Kind == SlideShapeKind.SmartArt);
        session.Bus.Redo();
        session.CurrentSlide.Shapes.Should().ContainSingle(shape =>
            shape.Kind == SlideShapeKind.AutoShape && shape.Name == "Cached SmartArt node");
    }

    [Fact]
    public void InsertSlide_IncreasesSlideCount()
    {
        var sess = Make();
        sess.InsertSlide();
        sess.Presentation.Slides.Should().HaveCount(2);
    }

    [Fact]
    public void InsertSlide_UpdatesCurrentSlideIndex()
    {
        var sess = Make();
        sess.InsertSlide();
        sess.CurrentSlideIndex.Should().Be(1, "new slide inserted after current");
    }

    [Fact]
    public void DeleteCurrentSlide_DecreasesSlideCount()
    {
        var sess = Make(2);
        sess.DeleteCurrentSlide();
        sess.Presentation.Slides.Should().HaveCount(1);
    }

    [Fact]
    public void DeleteCurrentSlide_ClampsIndexWhenDeletingLast()
    {
        var sess = Make(2);
        sess.SelectSlide(1);
        sess.DeleteCurrentSlide();
        sess.CurrentSlideIndex.Should().Be(0);
    }

    [Fact]
    public void DeleteCurrentSlide_NoOp_WhenNoSlides()
    {
        var p   = new Presentation();
        var bus = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        var act = () => sess.DeleteCurrentSlide();
        act.Should().NotThrow();
    }

    [Fact]
    public void DuplicateCurrentSlide_IncreasesSlideCount()
    {
        var sess = Make();
        sess.DuplicateCurrentSlide();
        sess.Presentation.Slides.Should().HaveCount(2);
    }

    [Fact]
    public void DuplicateCurrentSlide_MovesCurrentToClone()
    {
        var sess = Make();
        sess.DuplicateCurrentSlide();
        sess.CurrentSlideIndex.Should().Be(1);
    }

    [Fact]
    public void MoveSlide_ReordersSlides()
    {
        var sess  = Make(3);
        var first = sess.Presentation.Slides[0];
        // Move slide 0 to index 2: [A,B,C] => [B,C,A]
        sess.MoveSlide(0, 2);
        sess.Presentation.Slides[2].Should().BeSameAs(first);
    }

    [Fact]
    public void ToggleCurrentSlideHidden_IsUndoableAndRedoable()
    {
        var sess = Make();

        sess.ToggleCurrentSlideHidden().Should().BeTrue();
        sess.CurrentSlide!.IsHidden.Should().BeTrue();

        sess.Undo();
        sess.CurrentSlide!.IsHidden.Should().BeFalse();

        sess.Redo();
        sess.CurrentSlide!.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void SelectSlide_ChangesCurrentSlide()
    {
        var sess = Make(2);
        sess.SelectSlide(1);
        sess.CurrentSlideIndex.Should().Be(1);
    }

    [Fact]
    public void SelectSlide_ClearsSelection()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.SelectSlide(0);
        sess.SelectedShapeIds.Should().BeEmpty();
    }

    [Fact]
    public void ChangeSelectedAutoShapeKind_IsUndoableAndRetainsTextAndFrame()
    {
        var sess = Make();
        var shape = MakeShape(1);
        shape.AutoShapeKind = DrawingShapeKind.Rectangle;
        shape.TextBody = new TextBody();
        shape.TextBody.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Keep me" } } });
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(shape.Id);

        sess.ChangeSelectedAutoShapeKind(DrawingShapeKind.Ellipse).Should().BeTrue();
        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Ellipse);
        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Keep me");
        shape.ExtentCxEmu.Should().Be(100);

        sess.Undo();
        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Rectangle);
        sess.Redo();
        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Ellipse);
    }

    // ── Undo/redo through session ─────────────────────────────────────────────────

    [Fact]
    public void Undo_AfterInsertSlide_RestoresPreviousCount()
    {
        var sess = Make();
        sess.InsertSlide();
        sess.Undo();
        sess.Presentation.Slides.Should().HaveCount(1);
    }

    [Fact]
    public void Undo_ClampsCurrentSlideIndex()
    {
        var sess = Make();
        sess.InsertSlide(); // now at index 1
        sess.Undo();        // slide removed, index must clamp to 0
        sess.CurrentSlideIndex.Should().Be(0);
    }

    [Fact]
    public void Redo_AfterUndoInsert_ReappliesInsert()
    {
        var sess = Make();
        sess.InsertSlide();
        sess.Undo();
        sess.Redo();
        sess.Presentation.Slides.Should().HaveCount(2);
    }

    // ── Selection ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Select_AddsShapeToSelection()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.SelectedShapeIds.Should().Contain(1u);
    }

    [Fact]
    public void Select_WithoutAdd_ReplacesSelection()
    {
        var sess = Make();
        var s1   = MakeShape(1);
        var s2   = MakeShape(2);
        sess.CurrentSlide!.Shapes.AddRange([s1, s2]);
        sess.Select(1u);
        sess.Select(2u, addToSelection: false);
        sess.SelectedShapeIds.Should().HaveCount(1).And.Contain(2u);
    }

    [Fact]
    public void Select_WithAdd_ExtendsSelection()
    {
        var sess = Make();
        var s1   = MakeShape(1);
        var s2   = MakeShape(2);
        sess.CurrentSlide!.Shapes.AddRange([s1, s2]);
        sess.Select(1u);
        sess.Select(2u, addToSelection: true);
        sess.SelectedShapeIds.Should().HaveCount(2);
    }

    [Fact]
    public void ClearSelection_EmptiesSelection()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.ClearSelection();
        sess.SelectedShapeIds.Should().BeEmpty();
    }

    [Fact]
    public void SelectAll_SelectsAllShapesOnCurrentSlide()
    {
        var sess = Make();
        sess.CurrentSlide!.Shapes.AddRange([MakeShape(1), MakeShape(2), MakeShape(3)]);
        sess.SelectAll();
        sess.SelectedShapeIds.Should().HaveCount(3);
    }

    [Fact]
    public void SelectionChanged_FiresOnSelect()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        int fired = 0;
        sess.SelectionChanged += (_, _) => fired++;
        sess.Select(1u);
        fired.Should().Be(1);
    }

    [Fact]
    public void CurrentSlideChanged_FiresOnInsert()
    {
        var sess  = Make();
        int fired = 0;
        sess.CurrentSlideChanged += (_, _) => fired++;
        sess.InsertSlide();
        fired.Should().BeGreaterThan(0);
    }

    // ── Shape operations through session ─────────────────────────────────────────

    [Fact]
    public void AddShape_AddsShapeToCurrentSlide_AndIsUndoable()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.AddShape(shape);
        sess.CurrentSlide!.Shapes.Should().Contain(shape);
        sess.Undo();
        sess.CurrentSlide!.Shapes.Should().NotContain(shape);
    }

    [Fact]
    public void DeleteSelected_RemovesSelectedShape_AndIsUndoable()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        // Add shape directly to the slide model (not through bus so undo stack is clean).
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.DeleteSelected();
        sess.CurrentSlide!.Shapes.Should().NotContain(shape);
        // Undo the delete — shape returns.
        sess.Undo();
        sess.CurrentSlide!.Shapes.Should().Contain(shape);
    }

    [Fact]
    public void DeleteSelected_PromotedAnimationHeadTriggerIsCorrectedToOnClick()
    {
        // freep-animation F1 sibling: the Delete key (EditingSession.DeleteSelected ->
        // DeleteShapeCommand) removes ShapeAnimation entries the same way the Animation Pane's
        // own Remove/Reorder buttons do, so it must apply the same main-sequence-head trigger
        // correction they do.
        var sess   = Make();
        var shape1 = MakeShape(1);
        var shape2 = MakeShape(2);
        sess.CurrentSlide!.Shapes.AddRange([shape1, shape2]);
        var head      = new ShapeAnimation { ShapeId = 1, Trigger = AnimationTrigger.OnClick };
        var promotee  = new ShapeAnimation { ShapeId = 2, Trigger = AnimationTrigger.WithPrevious };
        sess.CurrentSlide!.Animations.Add(head);
        sess.CurrentSlide!.Animations.Add(promotee);

        sess.Select(1u);
        sess.DeleteSelected();

        var anims = sess.CurrentSlide!.Animations;
        anims.Should().ContainSingle();
        anims[0].ShapeId.Should().Be(2u);
        anims[0].Trigger.Should().Be(AnimationTrigger.OnClick);
        promotee.Trigger.Should().Be(AnimationTrigger.OnClick);
    }

    [Fact]
    public void MoveSelected_TranslatesShape()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.MoveSelected(200, 150);
        shape.OffsetXEmu.Should().Be(200);
        shape.OffsetYEmu.Should().Be(150);
    }

    [Fact]
    public void MoveSelected_NoOp_WhenNothingSelected()
    {
        var sess = Make();
        var act  = () => sess.MoveSelected(100, 100);
        act.Should().NotThrow();
    }

    [Fact]
    public void BringForward_IncrementsZOrder()
    {
        var sess = Make();
        var s1   = MakeShape(1);
        var s2   = MakeShape(2);
        sess.CurrentSlide!.Shapes.AddRange([s1, s2]);
        sess.Select(1u);
        sess.BringForward();
        // s1 was at index 0; after BringForward it should be at index 1.
        sess.CurrentSlide!.Shapes[1].Should().BeSameAs(s1);
    }

    [Fact]
    public void SendBackward_DecrementsZOrder()
    {
        var sess = Make();
        var s1   = MakeShape(1);
        var s2   = MakeShape(2);
        sess.CurrentSlide!.Shapes.AddRange([s1, s2]);
        sess.Select(2u);
        sess.SendBackward();
        // s2 was at index 1; after SendBackward it should be at index 0.
        sess.CurrentSlide!.Shapes[0].Should().BeSameAs(s2);
    }

    // ── Default shape factories ───────────────────────────────────────────────────

    [Fact]
    public void InsertDefaultTextBox_AddsShapeAtCenterPosition()
    {
        var sess  = Make();
        var shape = sess.InsertDefaultTextBox();
        shape.Should().NotBeNull();
        shape.Kind.Should().Be(SlideShapeKind.AutoShape);
        shape.OffsetXEmu.Should().BeGreaterThan(0);
        shape.TextBody.Should().NotBeNull();
    }

    [Fact]
    public void InsertDefaultTextBox_AllocatesIdAfterGroupedDescendants()
    {
        var sess = Make();
        var group = new SlideShape { Id = 1, Kind = SlideShapeKind.Group };
        group.Children.Add(MakeShape(80));
        sess.CurrentSlide!.Shapes.Add(group);

        var inserted = sess.InsertDefaultTextBox();

        inserted.Id.Should().Be(81);
    }

    [Fact]
    public void InsertMedia_AddsEmbeddedVideoAndIsUndoable()
    {
        var sess = Make();
        var shape = sess.InsertMedia(new byte[] { 1, 2, 3 }, true, "video/mp4");

        shape.Kind.Should().Be(SlideShapeKind.Media);
        shape.Media!.IsVideo.Should().BeTrue();
        sess.CurrentSlide!.Shapes.Should().ContainSingle();

        sess.Undo();
        sess.CurrentSlide.Shapes.Should().BeEmpty();
        sess.Redo();
        sess.CurrentSlide.Shapes.Should().ContainSingle();
    }

    [Fact]
    public void InsertDefaultRectangle_AddsRectangle()
    {
        var sess  = Make();
        var shape = sess.InsertDefaultRectangle();
        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Rectangle);
    }

    [Fact]
    public void InsertDefaultEllipse_AddsEllipse()
    {
        var sess  = Make();
        var shape = sess.InsertDefaultEllipse();
        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Ellipse);
    }

    [Fact]
    public void InsertDefaultConnector_AttachesSelectedShapesAtFacingSites()
    {
        var sess = Make();
        var left = MakeShape(1);
        left.AutoShapeKind = DrawingShapeKind.Rectangle;
        left.ExtentCxEmu = 100;
        left.ExtentCyEmu = 100;
        var right = MakeShape(2);
        right.AutoShapeKind = DrawingShapeKind.Rectangle;
        right.OffsetXEmu = 500;
        right.ExtentCxEmu = 100;
        right.ExtentCyEmu = 100;
        sess.CurrentSlide!.Shapes.AddRange([left, right]);
        sess.Select(left.Id);
        sess.Select(right.Id, addToSelection: true);

        var connector = sess.InsertDefaultConnector();

        connector.Kind.Should().Be(SlideShapeKind.Connector);
        connector.AutoShapeKind.Should().Be(DrawingShapeKind.Line);
        connector.ConnectionStart!.ShapeId.Should().Be(left.Id);
        connector.ConnectionStart.SiteIndex.Should().Be(2);
        connector.ConnectionEnd!.ShapeId.Should().Be(right.Id);
        connector.ConnectionEnd.SiteIndex.Should().Be(0);
        connector.OffsetXEmu.Should().Be(100);
        connector.ExtentCxEmu.Should().Be(400);
        sess.Undo();
        sess.CurrentSlide.Shapes.Should().HaveCount(2);
    }

    [Fact]
    public void RotateSelectedShapes_RotatesBothDirectionsAsOneUndoableOperation()
    {
        var sess = Make();
        var first = MakeShape(54);
        first.RotationDeg = 15;
        var second = MakeShape(55);
        second.RotationDeg = 30;
        sess.CurrentSlide!.Shapes.Add(first);
        sess.CurrentSlide.Shapes.Add(second);
        sess.Select(first.Id);
        sess.Select(second.Id, addToSelection: true);

        sess.RotateSelectedRight90();
        first.RotationDeg.Should().Be(105);
        second.RotationDeg.Should().Be(120);
        sess.Undo();
        first.RotationDeg.Should().Be(15);
        second.RotationDeg.Should().Be(30);

        sess.RotateSelectedLeft90();
        first.RotationDeg.Should().Be(-75);
        second.RotationDeg.Should().Be(-60);
        sess.Undo();
        first.RotationDeg.Should().Be(15);
        second.RotationDeg.Should().Be(30);
    }

    [Fact]
    public void NestedGroupedSelection_ResolvesConnectorHyperlinkAutoShapeAndUngroupRoutes()
    {
        var sess = Make();
        var left = MakeShape(21);
        left.Hyperlink = new Hyperlink { Url = "https://example.test/child" };
        var right = MakeShape(22);
        right.OffsetXEmu = 500;
        var inner = new SlideShape { Id = 23, Kind = SlideShapeKind.Group };
        inner.Children.Add(left);
        inner.Children.Add(right);
        var outer = new SlideShape { Id = 24, Kind = SlideShapeKind.Group };
        outer.Children.Add(inner);
        sess.CurrentSlide!.Shapes.Add(outer);

        sess.Select(left.Id);
        sess.SelectedShapeHyperlink!.Url.Should().Be("https://example.test/child");
        sess.ChangeSelectedAutoShapeKind(DrawingShapeKind.Diamond).Should().BeTrue();
        left.AutoShapeKind.Should().Be(DrawingShapeKind.Diamond);

        sess.Select(left.Id);
        sess.Select(right.Id, addToSelection: true);
        var connector = sess.InsertDefaultConnector();
        connector.ConnectionStart.Should().NotBeNull();
        connector.ConnectionEnd.Should().NotBeNull();
        ConnectionSiteHelper.Resolve(connector.ConnectionStart, sess.CurrentSlide)
            .Should().NotBe((0L, 0L));

        sess.Select(inner.Id);
        sess.UngroupSelected();
        outer.Children.Should().Contain(left).And.Contain(right);
        outer.Children.Should().NotContain(inner);
        sess.Undo();
        outer.Children.Should().ContainSingle(shape => shape.Id == inner.Id);
    }

    [Fact]
    public void UngroupSelected_PromotedAnimationHeadTriggerIsCorrectedToOnClick()
    {
        // freep-animation F1 sibling: UngroupShapeCommand (reached from EditingSession.
        // UngroupSelected) drops the group's own animation the same way DeleteShapeCommand
        // drops a deleted shape's, so it must apply the same main-sequence-head correction.
        var sess  = Make();
        var group = new SlideShape { Id = 40, Kind = SlideShapeKind.Group };
        group.Children.Add(MakeShape(41));
        sess.CurrentSlide!.Shapes.Add(group);
        var head     = new ShapeAnimation { ShapeId = 40, Trigger = AnimationTrigger.OnClick };
        var promotee = new ShapeAnimation { ShapeId = 42, Trigger = AnimationTrigger.AfterPrevious };
        sess.CurrentSlide!.Animations.Add(head);
        sess.CurrentSlide!.Animations.Add(promotee);

        sess.Select(40u);
        sess.UngroupSelected();

        var anims = sess.CurrentSlide!.Animations;
        anims.Should().ContainSingle();
        anims[0].ShapeId.Should().Be(42u);
        anims[0].Trigger.Should().Be(AnimationTrigger.OnClick);
        promotee.Trigger.Should().Be(AnimationTrigger.OnClick);
    }

    [Fact]
    public void NestedGroupedSelection_AlignAndZOrderUseContainingSiblingList()
    {
        var sess = Make();
        var first = MakeShape(31);
        first.OffsetYEmu = 100;
        var second = MakeShape(32);
        second.OffsetXEmu = 400;
        second.OffsetYEmu = 300;
        var inner = new SlideShape { Id = 33, Kind = SlideShapeKind.Group };
        inner.Children.Add(first);
        inner.Children.Add(second);
        var outer = new SlideShape { Id = 34, Kind = SlideShapeKind.Group };
        outer.Children.Add(inner);
        sess.CurrentSlide!.Shapes.Add(outer);

        sess.Select(first.Id);
        sess.Select(second.Id, addToSelection: true);
        sess.AlignTop();
        first.OffsetYEmu.Should().Be(second.OffsetYEmu);
        sess.Undo();
        first.OffsetYEmu.Should().Be(100);
        second.OffsetYEmu.Should().Be(300);

        sess.Select(first.Id);
        sess.BringToFront();
        inner.Children[^1].Id.Should().Be(first.Id);
        sess.Undo();
        inner.Children[0].Id.Should().Be(first.Id);
    }

    [Fact]
    public void GroupSelectedShapes_GroupsNestedChildrenAndUndoRestoresParentList()
    {
        var sess = Make();
        var first = MakeShape(41);
        var second = MakeShape(42);
        second.OffsetXEmu = 400;
        var parent = new SlideShape { Id = 43, Kind = SlideShapeKind.Group };
        parent.Children.Add(first);
        parent.Children.Add(second);
        sess.CurrentSlide!.Shapes.Add(parent);

        sess.Select(first.Id);
        sess.Select(second.Id, addToSelection: true);
        sess.GroupSelectedShapes();

        sess.SelectedShapeIds.Should().ContainSingle();
        parent.Children.Should().ContainSingle();
        var nestedGroup = parent.Children[0];
        nestedGroup.Kind.Should().Be(SlideShapeKind.Group);
        nestedGroup.Children.Select(shape => shape.Id).Should().Equal(first.Id, second.Id);

        sess.Undo();
        parent.Children.Select(shape => shape.Id).Should().Equal(first.Id, second.Id);
        sess.Redo();
        parent.Children.Should().ContainSingle(shape => shape.Id == nestedGroup.Id);
    }

    [Theory]
    [InlineData(DrawingShapeKind.ElbowConnector)]
    [InlineData(DrawingShapeKind.CurvedConnector)]
    public void InsertDefaultConnector_VariantPreservesSelectedAttachments(DrawingShapeKind kind)
    {
        var sess = Make();
        var first = MakeShape(1);
        first.AutoShapeKind = DrawingShapeKind.Rectangle;
        var second = MakeShape(2);
        second.AutoShapeKind = DrawingShapeKind.Rectangle;
        second.OffsetXEmu = 500;
        sess.CurrentSlide!.Shapes.AddRange([first, second]);
        sess.Select(first.Id);
        sess.Select(second.Id, addToSelection: true);

        var connector = sess.InsertDefaultConnector(kind);

        connector.AutoShapeKind.Should().Be(kind);
        connector.ConnectionStart!.ShapeId.Should().Be(first.Id);
        connector.ConnectionEnd!.ShapeId.Should().Be(second.Id);
    }

    [Fact]
    public void FlipSelectedShapes_TogglesAxisAndUndoRestoresIt()
    {
        var sess = Make();
        var first = MakeShape(51);
        var second = MakeShape(52);
        sess.CurrentSlide!.Shapes.Add(first);
        sess.CurrentSlide.Shapes.Add(second);

        sess.Select(first.Id);
        sess.Select(second.Id, addToSelection: true);
        sess.FlipSelectedHorizontal();

        first.FlipH.Should().BeTrue();
        second.FlipH.Should().BeTrue();
        first.FlipV.Should().BeFalse();
        second.FlipV.Should().BeFalse();

        sess.Undo();
        first.FlipH.Should().BeFalse();
        second.FlipH.Should().BeFalse();
        sess.Redo();
        first.FlipH.Should().BeTrue();
        second.FlipH.Should().BeTrue();

        sess.FlipSelectedVertical();
        first.FlipV.Should().BeTrue();
        second.FlipV.Should().BeTrue();
        sess.Undo();
        first.FlipV.Should().BeFalse();
        second.FlipV.Should().BeFalse();
    }

    [Fact]
    public void FlipShape_TogglesExistingStateAndCanBeUndone()
    {
        var sess = Make();
        var shape = MakeShape(53);
        shape.FlipV = true;
        sess.CurrentSlide!.Shapes.Add(shape);

        sess.FlipShape(shape.Id, horizontal: false);
        shape.FlipV.Should().BeFalse();
        sess.Undo();
        shape.FlipV.Should().BeTrue();
    }

    // ── Format toggles ────────────────────────────────────────────────────────────

    [Fact]
    public void ToggleBoldOnSelection_TogglesBoldOnAllRuns()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        var tb    = new TextBody();
        var para  = new Paragraph();
        var run   = new Run { Text = "hi", Bold = false };
        para.Runs.Add(run);
        tb.Paragraphs.Add(para);
        shape.TextBody = tb;
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.ToggleBoldOnSelection();
        run.Bold.Should().BeTrue();
    }

    [Fact]
    public void NestedGroupChild_TextFormattingAndTextFrameEdits_RouteThroughSession()
    {
        var sess = Make();
        var child = MakeShape(3);
        var run = new Run { Text = "Grouped text" };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(run);
        child.TextBody = new TextBody { Paragraphs = { paragraph } };

        var inner = new SlideShape { Id = 2, Kind = SlideShapeKind.Group };
        inner.Children.Add(child);
        var outer = new SlideShape { Id = 1, Kind = SlideShapeKind.Group };
        outer.Children.Add(inner);
        sess.CurrentSlide!.Shapes.Add(outer);
        sess.Select(child.Id);

        sess.ToggleBoldOnSelection();
        sess.SetFontFamilyOnSelection("Verdana");
        sess.SetFontSizeOnSelection(18);
        sess.SetColorOnSelection(new ThemeAwareColor(new SrgbColor(0x12, 0x34, 0x56)));
        sess.SetTextAutoFitOnSelection(TextAutoFitKind.Normal).Should().Be(1);
        sess.SetTextVerticalTypeOnSelection(TextVerticalType.Vertical270).Should().Be(1);
        sess.SetTextColumnCountOnSelection(2).Should().Be(1);
        sess.SetTextColumnSpacingOnSelection(152_400).Should().Be(1);

        run.Bold.Should().BeTrue();
        run.FontFamily.Should().Be("Verdana");
        run.FontSizePt.Should().Be(18);
        run.Color!.Resolved.Should().Be(new SrgbColor(0x12, 0x34, 0x56));
        child.TextBody.AutoFitKind.Should().Be(TextAutoFitKind.Normal);
        child.TextBody.VerticalType.Should().Be(TextVerticalType.Vertical270);
        child.TextBody.ColumnCount.Should().Be(2);
        child.TextBody.ColumnSpacingEmu.Should().Be(152_400);
    }

    [Fact]
    public void NestedGroupChild_ZOrderCommandsUseContainingSiblingList()
    {
        var sess = Make();
        var first = MakeShape(3);
        var second = MakeShape(4);
        var group = new SlideShape { Id = 1, Kind = SlideShapeKind.Group };
        group.Children.Add(first);
        group.Children.Add(second);
        sess.CurrentSlide!.Shapes.Add(group);
        sess.Select(first.Id);

        sess.BringForward();
        group.Children.Select(shape => shape.Id).Should().Equal(second.Id, first.Id);
        sess.SendBackward();
        group.Children.Select(shape => shape.Id).Should().Equal(first.Id, second.Id);

    }

    [Fact]
    public void GroupedChildTextFormatting_UsesSharedUndoableRoutes()
    {
        var session = Make();
        var group = new SlideShape { Id = 20, Name = "Group", Kind = SlideShapeKind.Group };
        var child = MakeShape(21);
        var run = new Run { Text = "grouped", Bold = false };
        child.TextBody = new TextBody
        {
            Paragraphs = { new Paragraph { Runs = { run } } },
        };
        group.Children.Add(child);
        session.CurrentSlide!.Shapes.Add(group);
        session.Select(child.Id);

        session.ToggleBoldOnSelection();
        session.SetFontOnSelection("Arial");
        session.SetFontSizeOnSelection(18);
        session.SetTextAutoFitOnSelection(TextAutoFitKind.Shape).Should().Be(1);

        run.Bold.Should().BeTrue();
        run.FontFamily.Should().Be("Arial");
        run.FontSizePt.Should().Be(18);
        child.TextBody.AutoFitKind.Should().Be(TextAutoFitKind.Shape);
    }

    [Fact]
    public void ToggleBoldOnSelection_NoOp_WhenNothingSelected()
    {
        var sess = Make();
        var act  = () => sess.ToggleBoldOnSelection();
        act.Should().NotThrow();
    }

    // ── undo-transactions F1: font family/size/color on a multi-shape selection ────

    private static SlideShape MakeTwoRunTextShape(uint id)
    {
        var shape = MakeShape(id);
        var tb = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "a" });
        para.Runs.Add(new Run { Text = "b" });
        tb.Paragraphs.Add(para);
        shape.TextBody = tb;
        return shape;
    }

    [Fact]
    public void SetFontOnSelection_MultiShapeSelection_IsOneUndoStep()
    {
        var sess = Make();
        var first = MakeTwoRunTextShape(1);
        var second = MakeTwoRunTextShape(2);
        sess.CurrentSlide!.Shapes.Add(first);
        sess.CurrentSlide.Shapes.Add(second);
        sess.Select(first.Id);
        sess.Select(second.Id, addToSelection: true);

        sess.SetFontOnSelection("Arial");

        first.TextBody!.Paragraphs[0].Runs.Select(r => r.FontFamily).Should().Equal("Arial", "Arial");
        second.TextBody!.Paragraphs[0].Runs.Select(r => r.FontFamily).Should().Equal("Arial", "Arial");

        sess.Undo();

        first.TextBody!.Paragraphs[0].Runs.Select(r => r.FontFamily).Should().AllBeEquivalentTo((string?)null);
        second.TextBody!.Paragraphs[0].Runs.Select(r => r.FontFamily).Should().AllBeEquivalentTo((string?)null);
        sess.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetFontSizeOnSelection_MultiShapeSelection_IsOneUndoStep()
    {
        var sess = Make();
        var first = MakeTwoRunTextShape(1);
        var second = MakeTwoRunTextShape(2);
        sess.CurrentSlide!.Shapes.Add(first);
        sess.CurrentSlide.Shapes.Add(second);
        sess.Select(first.Id);
        sess.Select(second.Id, addToSelection: true);

        sess.SetFontSizeOnSelection(18);

        first.TextBody!.Paragraphs[0].Runs.Select(r => r.FontSizePt).Should().Equal(18, 18);
        second.TextBody!.Paragraphs[0].Runs.Select(r => r.FontSizePt).Should().Equal(18, 18);

        sess.Undo();

        first.TextBody!.Paragraphs[0].Runs.Select(r => r.FontSizePt).Should().AllBeEquivalentTo((double?)null);
        second.TextBody!.Paragraphs[0].Runs.Select(r => r.FontSizePt).Should().AllBeEquivalentTo((double?)null);
        sess.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetColorOnSelection_MultiShapeSelection_IsOneUndoStep()
    {
        var sess = Make();
        var first = MakeTwoRunTextShape(1);
        var second = MakeTwoRunTextShape(2);
        sess.CurrentSlide!.Shapes.Add(first);
        sess.CurrentSlide.Shapes.Add(second);
        sess.Select(first.Id);
        sess.Select(second.Id, addToSelection: true);
        var color = new ThemeAwareColor(new SrgbColor(0x12, 0x34, 0x56));

        sess.SetColorOnSelection(color);

        first.TextBody!.Paragraphs[0].Runs.Should().OnlyContain(r => r.Color!.Resolved == color.Resolved);
        second.TextBody!.Paragraphs[0].Runs.Should().OnlyContain(r => r.Color!.Resolved == color.Resolved);

        sess.Undo();

        first.TextBody!.Paragraphs[0].Runs.Should().OnlyContain(r => r.Color == null);
        second.TextBody!.Paragraphs[0].Runs.Should().OnlyContain(r => r.Color == null);
        sess.CanUndo.Should().BeFalse();
    }

    /// <summary>Sibling no-regression case: a single-shape, single-run selection still undoes
    /// cleanly in exactly one step (the pre-fix per-run loop already worked correctly here since
    /// there was only ever one command).</summary>
    [Fact]
    public void SetFontOnSelection_SingleRunSelection_StillOneUndoStep()
    {
        var sess = Make();
        var shape = MakeShape(1);
        var tb = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "hi" });
        tb.Paragraphs.Add(para);
        shape.TextBody = tb;
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(shape.Id);

        sess.SetFontOnSelection("Calibri");
        shape.TextBody.Paragraphs[0].Runs[0].FontFamily.Should().Be("Calibri");

        sess.Undo();
        shape.TextBody.Paragraphs[0].Runs[0].FontFamily.Should().BeNull();
        sess.CanUndo.Should().BeFalse();
    }

    // ── undo-transactions F2: shape effects / transparency on a multi-shape selection ──

    [Fact]
    public void SetSelectedShapeShadow_MultiShapeSelection_IsOneUndoStep()
    {
        var sess = Make();
        var shapes = new[] { MakeShape(1), MakeShape(2), MakeShape(3) };
        foreach (var s in shapes) sess.CurrentSlide!.Shapes.Add(s);
        sess.Select(shapes[0].Id);
        sess.Select(shapes[1].Id, addToSelection: true);
        sess.Select(shapes[2].Id, addToSelection: true);

        var applied = sess.SetSelectedShapeShadow(ShapeEffectAuthoringPlanner.Subtle());
        applied.Should().Be(3);
        shapes.Should().OnlyContain(s => s.Effects != null && s.Effects.HasOuterShadow);

        sess.Undo();

        shapes.Should().OnlyContain(s => s.Effects == null || !s.Effects.HasOuterShadow);
        sess.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedFillTransparency_MultiShapeSelection_IsOneUndoStep()
    {
        var sess = Make();
        var first = MakeShape(1);
        first.Fill = new ShapeFill.Solid(SrgbColor.Black);
        var second = MakeShape(2);
        second.Fill = new ShapeFill.Solid(SrgbColor.Black);
        sess.CurrentSlide!.Shapes.Add(first);
        sess.CurrentSlide.Shapes.Add(second);
        sess.Select(first.Id);
        sess.Select(second.Id, addToSelection: true);

        sess.SetSelectedFillTransparency(50);

        ((ShapeFill.Solid)first.Fill!).Color.Alpha.Should().Be(ShapeTransparencyPlanner.ToAlpha(50));
        ((ShapeFill.Solid)second.Fill!).Color.Alpha.Should().Be(ShapeTransparencyPlanner.ToAlpha(50));

        sess.Undo();

        ((ShapeFill.Solid)first.Fill!).Color.Alpha.Should().Be((byte)255);
        ((ShapeFill.Solid)second.Fill!).Color.Alpha.Should().Be((byte)255);
        sess.CanUndo.Should().BeFalse();
    }

    /// <summary>Sibling no-regression case: a single selected shape still undoes cleanly in one
    /// step (the pre-fix per-shape loop already worked correctly when only one shape qualified).</summary>
    [Fact]
    public void SetSelectedShapeShadow_SingleShapeSelection_StillOneUndoStep()
    {
        var sess = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(shape.Id);

        sess.SetSelectedShapeShadow(ShapeEffectAuthoringPlanner.Subtle()).Should().Be(1);
        shape.Effects!.HasOuterShadow.Should().BeTrue();

        sess.Undo();
        (shape.Effects == null || !shape.Effects.HasOuterShadow).Should().BeTrue();
        sess.CanUndo.Should().BeFalse();
    }
}
