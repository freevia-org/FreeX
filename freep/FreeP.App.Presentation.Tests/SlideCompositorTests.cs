using FreeP.App.Compositor;
using PresentationModel = FreeP.Core.Model.Presentation;

using System.Xml.Linq;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Unit tests for <see cref="SlideCompositor"/>, <see cref="PlaceholderResolver"/>,
/// and <see cref="ThemeColorResolver"/>.
/// </summary>
public sealed class SlideCompositorTests
{
    // â”€â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static PresentationModel MakePresentation(Action<PresentationModel>? configure = null)
    {
        var p = PresentationModel.CreateEmpty();
        configure?.Invoke(p);
        return p;
    }

    private static Slide FirstSlide(PresentationModel p) => p.Slides[0];

    // â”€â”€â”€ Basic composition â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_EmptyPresentation_ReturnsBackgroundOp()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var ops = SlideCompositor.Compose(p, FirstSlide(p));

        ops.Should().HaveCount(1);
        ops[0].Should().BeOfType<DrawOp.Background>();
    }

    [Fact]
    public void Compose_SlideWithOneShape_ReturnsBackgroundPlusShapeOp()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));

        ops.Should().HaveCount(2);
        ops[0].Should().BeOfType<DrawOp.Background>();
        ops[1].Should().BeOfType<DrawOp.Shape>();
    }

    [Fact]
    public void Compose_ZOrderPreserved_ShapesInInputOrder()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        for (uint i = 1; i <= 3; i++)
        {
            p.Slides[0].Shapes.Add(new SlideShape
            {
                Id = i,
                Name = $"Shape{i}",
                OffsetXEmu = i * 100000,
                OffsetYEmu = 100000,
                ExtentCxEmu = 900000,
                ExtentCyEmu = 500000
            });
        }

        var ops = SlideCompositor.Compose(p, FirstSlide(p));

        // Background + 3 shapes
        ops.Should().HaveCount(4);
        var shapeOps = ops.OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3);
        // bounds x should match the order: Shape1 < Shape2 < Shape3
        shapeOps[0].BoundsDip.X.Should().BeLessThan(shapeOps[1].BoundsDip.X);
        shapeOps[1].BoundsDip.X.Should().BeLessThan(shapeOps[2].BoundsDip.X);
    }

    // â”€â”€â”€ EMU â†’ DIP conversion â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_EmuToDip_ConvertsCorrectly()
    {
        // 1 DIP = 9525 EMU (at 96 DPI)
        const long emuX = 914400;   // = 1 inch = 96 DIP
        const long emuCx = 1828800; // = 2 inches = 192 DIP

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = emuX,
            OffsetYEmu = 0,
            ExtentCxEmu = emuCx,
            ExtentCyEmu = 457200  // 0.5 inch = 48 DIP
        });

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.BoundsDip.X.Should().BeApproximately(96.0, 0.1);
        shapeOp.BoundsDip.Width.Should().BeApproximately(192.0, 0.1);
        shapeOp.BoundsDip.Height.Should().BeApproximately(48.0, 0.1);
    }

    [Fact]
    public void Compose_ShapeTextInsets_UseSharedFrameInsetPolicy()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        var body = BodyWithText("Shape text");
        body.InsetLeftPt = 1.5;
        body.InsetTopPt = 2.0;
        body.InsetRightPt = 3.0;
        body.InsetBottomPt = 4.0;
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
            TextBody = body
        });

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var text = ops.OfType<DrawOp.Shape>().Single().Text;
        var expected = TextFrameLayoutPlanner.FromOptionalInsets(
            PointsToDip(1.5),
            PointsToDip(2.0),
            PointsToDip(3.0),
            PointsToDip(4.0),
            defaultHorizontal: 9.14,
            defaultVertical: 4.57);

        text.Should().NotBeNull();
        text!.InsetLeftDip.Should().BeApproximately(expected.Left, 1e-9);
        text.InsetTopDip.Should().BeApproximately(expected.Top, 1e-9);
        text.InsetRightDip.Should().BeApproximately(expected.Right, 1e-9);
        text.InsetBottomDip.Should().BeApproximately(expected.Bottom, 1e-9);
    }

    [Fact]
    public void Compose_TableCellTextInsets_UseSharedFrameInsetPolicy()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(914400);
        var row = new TableRow { HeightEmu = 457200 };
        row.Cells.Add(new TableCell
        {
            TextBody = BodyWithText("Cell text"),
            InsetTopPt = 3.0,
            InsetRightPt = 4.0
        });
        table.Rows.Add(row);
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
            Table = table
        });

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var text = ops.OfType<DrawOp.Table>().Single().Cells.Single().Text;
        var expected = TextFrameLayoutPlanner.FromOptionalInsets(
            left: null,
            top: PointsToDip(3.0),
            right: PointsToDip(4.0),
            bottom: null,
            defaultHorizontal: PointsToDip(7.0),
            defaultVertical: PointsToDip(3.6));

        text.Should().NotBeNull();
        text!.InsetLeftDip.Should().BeApproximately(expected.Left, 1e-9);
        text.InsetTopDip.Should().BeApproximately(expected.Top, 1e-9);
        text.InsetRightDip.Should().BeApproximately(expected.Right, 1e-9);
        text.InsetBottomDip.Should().BeApproximately(expected.Bottom, 1e-9);
    }

    [Fact]
    public void Compose_SlideBoundsMatchPresentationSize()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var bg = ops.OfType<DrawOp.Background>().Single();

        // Default 16:9: 12192000 x 6858000 EMU
        bg.BoundsDip.Width.Should().BeApproximately(p.SlideSizeCxEmu / 9525.0, 0.1);
        bg.BoundsDip.Height.Should().BeApproximately(p.SlideSizeCyEmu / 9525.0, 0.1);
    }

    [Fact]
    public void Compose_CanOmitDestinationBackgroundForZoomTransition()
    {
        var presentation = MakePresentation();
        var slide = FirstSlide(presentation);

        var ops = SlideCompositor.Compose(
            presentation,
            slide,
            includeBackground: false);

        ops.OfType<DrawOp.Background>().Should().BeEmpty();
    }

    [Fact]
    public void Compose_SummaryZoom_UsesAttachedPreviewTilesAndNativeLayout()
    {
        var presentation = new PresentationModel();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Id = "slide-1" });
        presentation.Slides.Add(new Slide { Id = "slide-2" });
        presentation.Sections.Add(new PresentationSection { Id = "section-1", Name = "One", SlideIds = { "slide-1" } });
        presentation.Sections.Add(new PresentationSection { Id = "section-2", Name = "Two", SlideIds = { "slide-2" } });

        var summaryZoom = SummaryZoomInsertionPlanner.CreateShape(
            presentation,
            new[] { "section-1", "section-2" });
        presentation.Slides[0].Shapes.Add(summaryZoom);

        var firstPreview = new byte[] { 1, 2, 3 };
        var secondPreview = new byte[] { 4, 5, 6 };
        SummaryZoomPreviewPlanner.AttachPreviewImages(
            presentation,
            summaryZoom,
            slideIndex => slideIndex == 0 ? firstPreview : secondPreview)
            .Should().Be(2);
        var summarySession = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        summarySession.SetZoomObjectProperties(
            summaryZoom.Id,
            new ZoomObjectProperties(
                ImageType: "preview",
                CropLeft: 15000,
                CropTop: 5000,
                CropRight: 25000,
                CropBottom: 10000)).Should().BeTrue();
        var allOps = SlideCompositor.Compose(presentation, presentation.Slides[0]);
        var pictures = allOps
            .OfType<DrawOp.Picture>()
            .ToArray();

        pictures.Should().HaveCount(2);
        pictures.Select(picture => picture.Bytes).Should().ContainInOrder(firstPreview, secondPreview);
        pictures.Should().AllSatisfy(picture =>
        {
            picture.CropLeft.Should().BeApproximately(0.15, 0.00001);
            picture.CropTop.Should().BeApproximately(0.05, 0.00001);
            picture.CropRight.Should().BeApproximately(0.25, 0.00001);
            picture.CropBottom.Should().BeApproximately(0.1, 0.00001);
        });

        var fullBounds = new LayoutRect(
            summaryZoom.OffsetXEmu / 9525d,
            summaryZoom.OffsetYEmu / 9525d,
            summaryZoom.ExtentCxEmu / 9525d,
            summaryZoom.ExtentCyEmu / 9525d);
        pictures[0].DestDip.Should().Be(new LayoutRect(
            fullBounds.X,
            fullBounds.Y,
            fullBounds.Width / 2,
            fullBounds.Height));
        pictures[1].DestDip.Should().Be(new LayoutRect(
            fullBounds.X + fullBounds.Width / 2,
            fullBounds.Y,
            fullBounds.Width / 2,
            fullBounds.Height));
    }

    [Fact]
    public void Compose_SlideAndSectionZoom_UsesAttachedSingleTargetPreviews()
    {
        var presentation = new PresentationModel();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Id = "slide-1" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        presentation.Sections.Add(new PresentationSection
        {
            Id = "section-target",
            Name = "Target section",
            SlideIds = { "slide-2" }
        });

        var slideZoom = SlideZoomInsertionPlanner.CreateShape(
            presentation,
            currentSlideIndex: 0,
            targetSlideId: "slide-2");
        var sectionZoom = SectionZoomInsertionPlanner.CreateShape(
            presentation,
            "section-target");
        presentation.Slides[0].Shapes.Add(slideZoom);
        presentation.Slides[0].Shapes.Add(sectionZoom);

        var preview = new byte[] { 7, 8, 9 };
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation,
            slideZoom,
            targetSlideIndex: 1,
            _ => preview).Should().BeTrue();
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation,
            sectionZoom,
            targetSlideIndex: 1,
            _ => preview).Should().BeTrue();
        var session = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        session.SetZoomObjectProperties(
            slideZoom.Id,
            new ZoomObjectProperties(
                ImageType: "preview",
                CropLeft: 20000,
                CropTop: 10000,
                CropRight: 30000,
                CropBottom: 5000,
                FrameBorderColor: "4472C4",
                FrameBorderWidthEmu: 25400,
                FrameBorderDash: OutlineDash.DashDot,
                FrameGeometry: "ellipse")).Should().BeTrue();

        var pictures = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Picture>()
            .ToArray();

        pictures.Should().HaveCount(2);
        pictures.Should().AllSatisfy(picture =>
        {
            picture.Bytes.Should().BeEquivalentTo(preview);
            picture.DestDip.Width.Should().BeGreaterThan(0);
            picture.DestDip.Height.Should().BeGreaterThan(0);
        });
        pictures[0].CropLeft.Should().BeApproximately(0.2, 0.00001);
        pictures[0].CropTop.Should().BeApproximately(0.1, 0.00001);
        pictures[0].CropRight.Should().BeApproximately(0.3, 0.00001);
        pictures[0].CropBottom.Should().BeApproximately(0.05, 0.00001);
        var border = pictures[0].Outline.Should().BeOfType<ResolvedOutline.Visible>().Subject;
        border.Color.Should().Be(new SrgbColor(0x44, 0x72, 0xC4));
        border.WidthDip.Should().BeApproximately(2 * (96.0 / 72.0), 0.00001);
        border.Dash.Should().Be(OutlineDash.DashDot);
        pictures[0].PictureFrameGeometry.Should().Be("ellipse");
        pictures[1].HasCrop.Should().BeFalse();

        session.SetZoomObjectProperties(
            slideZoom.Id,
            new ZoomObjectProperties(ImageType: "cover")).Should().BeTrue();
        var coverPictures = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Picture>()
            .ToArray();
        coverPictures[0].IsCover.Should().BeTrue();
        coverPictures[1].IsCover.Should().BeFalse();
    }

    [Fact]
    public void Compose_ZoomFrameBorder_ResolvesNativeThemeColorAndTransforms()
    {
        var presentation = PresentationModel.CreateEmpty();
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var theme = presentation.Theme;
        theme.ColorScheme[ThemeColorSlot.Accent1] = new SrgbColor(0x20, 0x40, 0x60);
        presentation.Theme = theme;

        var zoom = SlideZoomInsertionPlanner.CreateShape(presentation, 0, "slide-2");
        presentation.Slides[0].Shapes.Add(zoom);
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation, zoom, targetSlideIndex: 1, _ => new byte[] { 1, 2, 3 })
            .Should().BeTrue();

        var raw = XElement.Parse(zoom.PreservedObject!.RawXml);
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var shapeProperties = raw.Descendants()
            .Single(element => element.Name.LocalName == "spPr");
        var line = new XElement(drawing + "ln");
        shapeProperties.Add(line);
        line.Elements(drawing + "solidFill").Remove();
        line.Add(new XElement(drawing + "solidFill",
            new XElement(drawing + "schemeClr",
                new XAttribute("val", "accent1"),
                new XElement(drawing + "lumMod", new XAttribute("val", "50000")),
                new XElement(drawing + "tint", new XAttribute("val", "80000")))));
        zoom.PreservedObject.RawXml = raw.ToString(SaveOptions.DisableFormatting);

        var picture = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Picture>().Single();
        var outline = picture.Outline.Should().BeOfType<ResolvedOutline.Visible>().Subject;
        var expected = ThemeColorTransform.Apply(
            theme.ColorScheme[ThemeColorSlot.Accent1],
            lumMod: 0.5, lumOff: 0, tint: 0.8, shade: 1);
        outline.Color.Should().Be(expected);
    }

    [Fact]
    public void Compose_ZoomFrameBorder_ResolvesNativeTwoStopGradient()
    {
        var presentation = PresentationModel.CreateEmpty();
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var zoom = SlideZoomInsertionPlanner.CreateShape(presentation, 0, "slide-2");
        presentation.Slides[0].Shapes.Add(zoom);
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation, zoom, targetSlideIndex: 1, _ => new byte[] { 1, 2, 3 })
            .Should().BeTrue();

        var raw = XElement.Parse(zoom.PreservedObject!.RawXml);
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var line = raw.Descendants().Single(element => element.Name.LocalName == "spPr")
            .Element(drawing + "ln") ?? new XElement(drawing + "ln");
        if (line.Parent is null)
            raw.Descendants().Single(element => element.Name.LocalName == "spPr").Add(line);
        line.Add(new XElement(drawing + "gradFill",
            new XElement(drawing + "gsLst",
                new XElement(drawing + "gs", new XAttribute("pos", 0),
                    new XElement(drawing + "srgbClr", new XAttribute("val", "4472C4"))),
                new XElement(drawing + "gs", new XAttribute("pos", 100000),
                    new XElement(drawing + "srgbClr", new XAttribute("val", "FFFFFF")))),
            new XElement(drawing + "lin", new XAttribute("ang", 8100000))));
        zoom.PreservedObject.RawXml = raw.ToString(SaveOptions.DisableFormatting);

        var picture = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Picture>().Single();
        var outline = picture.Outline.Should().BeOfType<ResolvedOutline.Gradient>().Subject;
        outline.WidthDip.Should().BeApproximately(1, 0.00001);
        outline.Fill.AngleDegrees.Should().BeApproximately(135, 0.00001);
        outline.Fill.Stops.Select(stop => stop.Color).Should().Equal(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SrgbColor(0xFF, 0xFF, 0xFF));
    }

    [Fact]
    public void Compose_ZoomFrameBorder_ResolvesNativePatternFill()
    {
        var presentation = PresentationModel.CreateEmpty();
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var zoom = SlideZoomInsertionPlanner.CreateShape(presentation, 0, "slide-2");
        presentation.Slides[0].Shapes.Add(zoom);
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation, zoom, targetSlideIndex: 1, _ => new byte[] { 1, 2, 3 })
            .Should().BeTrue();

        var raw = XElement.Parse(zoom.PreservedObject!.RawXml);
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var line = raw.Descendants().Single(element => element.Name.LocalName == "spPr")
            .Element(drawing + "ln") ?? new XElement(drawing + "ln");
        if (line.Parent is null)
            raw.Descendants().Single(element => element.Name.LocalName == "spPr").Add(line);
        line.Add(new XElement(drawing + "pattFill",
            new XAttribute("prst", "pct50"),
            new XElement(drawing + "fgClr",
                new XElement(drawing + "srgbClr", new XAttribute("val", "4472C4"))),
            new XElement(drawing + "bgClr",
                new XElement(drawing + "srgbClr", new XAttribute("val", "FFFFFF")))));
        zoom.PreservedObject.RawXml = raw.ToString(SaveOptions.DisableFormatting);

        var picture = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Picture>().Single();
        var outline = picture.Outline.Should().BeOfType<ResolvedOutline.Pattern>().Subject;
        outline.Fill.Preset.Should().Be("pct50");
        outline.Fill.ForegroundColor.Should().Be(new SrgbColor(0x44, 0x72, 0xC4));
        outline.Fill.BackgroundColor.Should().Be(SrgbColor.White);
        outline.WidthDip.Should().BeApproximately(1, 0.00001);
    }

    [Fact]
    public void Compose_ZoomFrameBorder_NativeNoFillSuppressesOutline()
    {
        var presentation = PresentationModel.CreateEmpty();
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var zoom = SlideZoomInsertionPlanner.CreateShape(presentation, 0, "slide-2");
        presentation.Slides[0].Shapes.Add(zoom);
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation, zoom, targetSlideIndex: 1, _ => new byte[] { 1, 2, 3 })
            .Should().BeTrue();

        var raw = XElement.Parse(zoom.PreservedObject!.RawXml);
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var shapeProperties = raw.Descendants().Single(element => element.Name.LocalName == "spPr");
        var line = shapeProperties.Element(drawing + "ln") ?? new XElement(drawing + "ln");
        if (line.Parent is null)
            shapeProperties.Add(line);
        line.Add(new XElement(drawing + "noFill"));
        zoom.PreservedObject.RawXml = raw.ToString(SaveOptions.DisableFormatting);

        var picture = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Picture>().Single();
        picture.Outline.Should().BeOfType<ResolvedOutline.None>();
    }

    [Fact]
    public void Compose_ZoomFrameBorder_ResolvesNativeOuterShadow()
    {
        var presentation = PresentationModel.CreateEmpty();
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var zoom = SlideZoomInsertionPlanner.CreateShape(presentation, 0, "slide-2");
        presentation.Slides[0].Shapes.Add(zoom);
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation, zoom, targetSlideIndex: 1, _ => new byte[] { 1, 2, 3 })
            .Should().BeTrue();

        var raw = XElement.Parse(zoom.PreservedObject!.RawXml);
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var shapeProperties = raw.Descendants().Single(element => element.Name.LocalName == "spPr");
        shapeProperties.Add(new XElement(drawing + "effectLst",
            new XElement(drawing + "outerShdw",
                new XAttribute("blurRad", 50800),
                new XAttribute("dist", 38100),
                new XAttribute("dir", 2700000),
                new XElement(drawing + "srgbClr",
                    new XAttribute("val", "404040"),
                    new XElement(drawing + "alpha", new XAttribute("val", 50000))))));
        zoom.PreservedObject.RawXml = raw.ToString(SaveOptions.DisableFormatting);

        var picture = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Picture>().Single();
        picture.Effects.Should().NotBeNull();
        picture.Effects!.HasOuterShadow.Should().BeTrue();
        picture.Effects.OuterShadowColor.Should().Be(new SrgbColor(0x40, 0x40, 0x40));
        picture.Effects.OuterShadowAlpha.Should().Be(128);
        picture.Effects.OuterShadowBlurDip.Should().BeApproximately(50800 / 9525d, 0.00001);
        picture.Effects.OuterShadowDistDip.Should().BeApproximately(38100 / 9525d, 0.00001);
        picture.Effects.OuterShadowDirDeg.Should().BeApproximately(45, 0.00001);
    }

    [Fact]
    public void Compose_ZoomFrameBorder_ResolvesNativeGlow()
    {
        var presentation = PresentationModel.CreateEmpty();
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var zoom = SlideZoomInsertionPlanner.CreateShape(presentation, 0, "slide-2");
        presentation.Slides[0].Shapes.Add(zoom);
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation, zoom, targetSlideIndex: 1, _ => new byte[] { 1, 2, 3 })
            .Should().BeTrue();

        var raw = XElement.Parse(zoom.PreservedObject!.RawXml);
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var shapeProperties = raw.Descendants().Single(element => element.Name.LocalName == "spPr");
        shapeProperties.Add(new XElement(drawing + "effectLst",
            new XElement(drawing + "glow",
                new XAttribute("rad", 152400),
                new XElement(drawing + "srgbClr",
                    new XAttribute("val", "00AAFF"),
                    new XElement(drawing + "alpha", new XAttribute("val", 42000))))));
        zoom.PreservedObject.RawXml = raw.ToString(SaveOptions.DisableFormatting);

        var picture = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Picture>().Single();
        picture.Effects.Should().NotBeNull();
        picture.Effects!.HasGlow.Should().BeTrue();
        picture.Effects.GlowColor.Should().Be(new SrgbColor(0x00, 0xAA, 0xFF));
        picture.Effects.GlowAlpha.Should().Be(107);
        picture.Effects.GlowRadiusDip.Should().BeApproximately(152400 / 9525d, 0.00001);
    }

    [Fact]
    public void Compose_ZoomFrameBorder_ResolvesNativeSoftEdge()
    {
        var presentation = PresentationModel.CreateEmpty();
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var zoom = SlideZoomInsertionPlanner.CreateShape(presentation, 0, "slide-2");
        presentation.Slides[0].Shapes.Add(zoom);
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation, zoom, targetSlideIndex: 1, _ => new byte[] { 1, 2, 3 })
            .Should().BeTrue();

        var raw = XElement.Parse(zoom.PreservedObject!.RawXml);
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var shapeProperties = raw.Descendants().Single(element => element.Name.LocalName == "spPr");
        shapeProperties.Add(new XElement(drawing + "effectLst",
            new XElement(drawing + "softEdge", new XAttribute("rad", 158750))));
        zoom.PreservedObject.RawXml = raw.ToString(SaveOptions.DisableFormatting);

        var picture = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Picture>().Single();
        picture.Effects.Should().NotBeNull();
        picture.Effects!.HasSoftEdge.Should().BeTrue();
        picture.Effects.SoftEdgeRadiusDip.Should().BeApproximately(158750 / 9525d, 0.00001);
    }

    [Fact]
    public void Compose_ZoomFrameBorder_ResolvesNativeReflection()
    {
        var presentation = PresentationModel.CreateEmpty();
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var zoom = SlideZoomInsertionPlanner.CreateShape(presentation, 0, "slide-2");
        presentation.Slides[0].Shapes.Add(zoom);
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation, zoom, targetSlideIndex: 1, _ => new byte[] { 1, 2, 3 })
            .Should().BeTrue();

        var raw = XElement.Parse(zoom.PreservedObject!.RawXml);
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var shapeProperties = raw.Descendants().Single(element => element.Name.LocalName == "spPr");
        shapeProperties.Add(new XElement(drawing + "effectLst",
            new XElement(drawing + "reflection",
                new XAttribute("stA", 42000),
                new XAttribute("blurRad", 12700),
                new XAttribute("dist", 44450),
                new XAttribute("dir", 5400000),
                new XAttribute("sy", -75000),
                new XAttribute("endPos", 25000))));
        zoom.PreservedObject.RawXml = raw.ToString(SaveOptions.DisableFormatting);

        var picture = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Picture>().Single();
        picture.Effects.Should().NotBeNull();
        picture.Effects!.HasReflection.Should().BeTrue();
        picture.Effects.ReflectionAlpha.Should().Be(107);
        picture.Effects.ReflectionBlurDip.Should().BeApproximately(12700 / 9525d, 0.00001);
        picture.Effects.ReflectionDistDip.Should().BeApproximately(44450 / 9525d, 0.00001);
        picture.Effects.ReflectionScaleY.Should().BeApproximately(-0.75, 0.00001);
        picture.Effects.ReflectionEndPos.Should().BeApproximately(0.25, 0.00001);
    }

    private static TextBody BodyWithText(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static double PointsToDip(double points) => points * (96.0 / 72.0);

    // â”€â”€â”€ Placeholder inheritance â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void PlaceholderResolver_InheritsFromLayout_WhenShapeHasNoExtents()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Add a layout placeholder with known geometry
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        // Slide shape with a placeholder tag but NO geometry (ExtentCx = 0)
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Title",
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            // ExtentCxEmu = 0 (default) â†’ should inherit from layout
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.OffsetXEmu.Should().Be(457200);
        anchor.OffsetYEmu.Should().Be(274320);
        anchor.ExtentCxEmu.Should().Be(8229600);
        anchor.ExtentCyEmu.Should().Be(1143000);
    }

    [Fact]
    public void PlaceholderResolver_FallsBackToMaster_WhenLayoutHasNoMatch()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        // Master has a body placeholder
        master.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200,
            OffsetYEmu = 1600200,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 4525963
        });
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout has NO body placeholder
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            // No geometry â†’ inherit from master
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.ExtentCxEmu.Should().Be(8229600);
        anchor.ExtentCyEmu.Should().Be(4525963);
    }

    [Fact]
    public void PlaceholderResolver_UsesShapeGeometry_WhenPresent()
    {
        var p = PresentationModel.CreateEmpty();
        var shape = new SlideShape
        {
            Id = 10,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 100000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 300000,
            ExtentCyEmu = 400000
        };

        var anchor = PlaceholderResolver.ResolveAnchor(shape, p.Slides[0], p);

        // Shape has explicit geometry â†’ it wins over layout.
        anchor.OffsetXEmu.Should().Be(100000);
        anchor.ExtentCxEmu.Should().Be(300000);
    }

    // â"€â"€â"€ MM2: placeholder type-compatibility matching â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

    /// <summary>
    /// PowerPoint "Title and Content" layouts declare their content placeholder as
    /// ph type="obj" idx="1", while the slide’s placeholder has no explicit type
    /// and therefore defaults to Body.  Body and Object are in the same content group
    /// so the match must succeed and the layout geometry must be inherited.
    /// </summary>
    [Fact]
    public void PlaceholderResolver_BodySlide_MatchesObjectLayout_InheritsGeometry()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout declares the content placeholder as type=Object idx=1
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Object, Idx = 1 },
            OffsetXEmu  = 457200,
            OffsetYEmu  = 1371600,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 4525963
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        // Slide shape has no explicit type â†’ defaults to Body; idx=1
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            // No geometry â†’ must inherit from layout
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.OffsetXEmu.Should().Be(457200,   "should inherit layout X");
        anchor.OffsetYEmu.Should().Be(1371600,  "should inherit layout Y");
        anchor.ExtentCxEmu.Should().Be(8229600, "should inherit layout width");
        anchor.ExtentCyEmu.Should().Be(4525963, "should inherit layout height");
    }

    /// <summary>
    /// A slide ctrTitle placeholder must match a layout title placeholder (and vice-versa);
    /// they are interchangeable within the Title group.
    /// </summary>
    [Fact]
    public void PlaceholderResolver_CtrTitle_MatchesLayoutTitle_InheritsGeometry()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout declares a plain Title placeholder (idx 0)
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu  = 1524000,
            OffsetYEmu  = 1122363,
            ExtentCxEmu = 9144000,
            ExtentCyEmu = 2387600
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        // Slide uses CenteredTitle (idx 0) â€" must match the layout Title
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            // No geometry â†’ must inherit from layout
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.ExtentCxEmu.Should().Be(9144000, "ctrTitle slide ph must inherit geometry from layout title ph");
        anchor.ExtentCyEmu.Should().Be(2387600);
    }

    /// <summary>
    /// Negative test: a Body placeholder at idx=1 must NOT match a Footer placeholder at idx=1.
    /// Body and Footer are in different groups; idx alone is not enough.
    /// </summary>
    [Fact]
    public void PlaceholderResolver_Body_DoesNotMatch_Footer_SameIdx()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout only has a Footer placeholder at idx=1 (no body placeholder)
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Footer, Idx = 1 },
            OffsetXEmu  = 0,
            OffsetYEmu  = 6400000,
            ExtentCxEmu = 9999999,
            ExtentCyEmu = 457200
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        // Slide has a Body placeholder at idx=1 â€" must NOT match the layout Footer
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            // No geometry â†’ should fall through to zero (no match found)
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        // No match â†’ falls back to the shape’s own (zero) extents
        anchor.ExtentCxEmu.Should().Be(0, "Body ph must not match Footer ph at the same idx");
        anchor.ExtentCyEmu.Should().Be(0);
    }

    /// <summary>
    /// Exact type+idx match must still work (regression guard for existing behavior).
    /// </summary>
    [Fact]
    public void PlaceholderResolver_ExactTypeAndIdx_StillMatches()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.SubTitle, Idx = 2 },
            OffsetXEmu  = 914400,
            OffsetYEmu  = 4000000,
            ExtentCxEmu = 7315200,
            ExtentCyEmu = 1143000
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.SubTitle, Idx = 2 },
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.ExtentCxEmu.Should().Be(7315200, "exact type+idx match must still resolve geometry");
    }

    /// <summary>
    /// Master fallback via the same matching: an Object(idx=1) on the slide should fall through
    /// to the master when the layout has no content placeholder, matching a master Body(idx=1)
    /// via content-group compatibility.
    /// </summary>
    [Fact]
    public void PlaceholderResolver_ObjectSlide_FallsThrough_ToMasterBodyPlaceholder()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        // Master has Body idx=1
        master.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu  = 457200,
            OffsetYEmu  = 1600200,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 4525963
        });
        p.Masters.Add(master);

        // Layout has no content placeholder at all
        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        // Slide has Object idx=1 â†’ should match master’s Body idx=1 via content-group compat
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Object, Idx = 1 },
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.ExtentCxEmu.Should().Be(8229600, "Object ph must fall through to master Body ph via content-group compat");
        anchor.ExtentCyEmu.Should().Be(4525963);
    }

    [Fact]
    public void Compose_PlaceholderShape_InheritsPositionFromLayout()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
        };
        titleShape.Text = "Test Title";
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Should have inherited the layout placeholder position
        shapeOp.BoundsDip.X.Should().BeApproximately(457200 / 9525.0, 0.1);
        shapeOp.BoundsDip.Width.Should().BeApproximately(8229600 / 9525.0, 0.1);
    }

    [Fact]
    public void Compose_ExplicitZeroExtentPlaceholder_IsHiddenInsteadOfInheritingLayoutGeometry()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            HasExplicitZeroExtentTransform = true,
            Text = "Hidden title"
        });
        p.Slides.Add(slide);

        SlideCompositor.Compose(p, slide).OfType<DrawOp.Shape>().Should().BeEmpty();
    }

    // â”€â”€â”€ Theme color resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ThemeColorResolver_ReturnsPreResolved_WhenNoSchemeRef()
    {
        var color = new ThemeAwareColor(new SrgbColor(0xAB, 0xCD, 0xEF));
        var result = ThemeColorResolver.Resolve(color, null);

        result.R.Should().Be(0xAB);
        result.G.Should().Be(0xCD);
        result.B.Should().Be(0xEF);
    }

    [Fact]
    public void ThemeColorResolver_ResolvesAccent1FromTheme()
    {
        var theme = PresentationTheme.CreateDefault();
        var color = new ThemeAwareColor(
            SrgbColor.Black, // pre-resolved fallback â€” should be overridden
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1, LumMod = 1.0, LumOff = 0.0 });

        var result = ThemeColorResolver.Resolve(color, theme);

        // Accent1 in default theme = #4472C4
        result.R.Should().Be(0x44);
        result.G.Should().Be(0x72);
        result.B.Should().Be(0xC4);
    }

    [Fact]
    public void ThemeColorResolver_LumMod_DarkensColor()
    {
        var theme = PresentationTheme.CreateDefault();
        // Accent1 = #4472C4; apply lumMod=0.5 (50% darker)
        var color = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1, LumMod = 0.5, LumOff = 0.0 });

        var fullColor = ThemeColorResolver.Resolve(
            new ThemeAwareColor(SrgbColor.Black, new SchemeColorRef { Slot = ThemeColorSlot.Accent1, LumMod = 1.0 }),
            theme);

        var darkenedColor = ThemeColorResolver.Resolve(color, theme);

        // Darkened should be darker: all channels should be less than or equal
        // (luminance halved â†’ each channel should be smaller)
        var fullLuminance = (fullColor.R + fullColor.G + fullColor.B);
        var darkenedLuminance = (darkenedColor.R + darkenedColor.G + darkenedColor.B);
        darkenedLuminance.Should().BeLessThan(fullLuminance,
            "applying lumMod=0.5 should produce a darker color");
    }

    [Fact]
    public void ThemeColorResolver_LumOff_LightensColor()
    {
        var theme = PresentationTheme.CreateDefault();
        var baseColor = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { Slot = ThemeColorSlot.Dk2, LumMod = 1.0, LumOff = 0.0 });

        var lightenedColor = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { Slot = ThemeColorSlot.Dk2, LumMod = 1.0, LumOff = 0.5 });

        var baseResult = ThemeColorResolver.Resolve(baseColor, theme);
        var lightenedResult = ThemeColorResolver.Resolve(lightenedColor, theme);

        var baseLum = baseResult.R + baseResult.G + baseResult.B;
        var lightenedLum = lightenedResult.R + lightenedResult.G + lightenedResult.B;

        lightenedLum.Should().BeGreaterThan(baseLum,
            "applying lumOff=0.5 should produce a lighter color");
    }

    [Fact]
    public void ThemeColorResolver_WhiteRoundTrips()
    {
        var theme = PresentationTheme.CreateDefault();
        var color = new ThemeAwareColor(SrgbColor.White);

        var result = ThemeColorResolver.Resolve(color, theme);
        result.Should().Be(SrgbColor.White);
    }

    [Fact]
    public void ThemeColorResolver_BlackRoundTrips()
    {
        var theme = PresentationTheme.CreateDefault();
        var color = new ThemeAwareColor(SrgbColor.Black);

        var result = ThemeColorResolver.Resolve(color, theme);
        result.Should().Be(SrgbColor.Black);
    }

    // ─── MM1: clrMap indirection (ECMA-376 §19.3.1.20) ───────────────────────────────────────────

    /// <summary>
    /// A master with an inverted clrMap (tx1→lt1, bg1→dk1) must cause schemeClr val="tx1"
    /// to resolve to the theme's Lt1 color, NOT the default Dk1.
    /// </summary>
    [Fact]
    public void ThemeColorResolver_InvertedClrMap_Tx1ResolvesToLt1()
    {
        var theme = PresentationTheme.CreateDefault(); // Lt1=#FFFFFF, Dk1=#000000

        var color = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { RoleName = "tx1", Slot = ThemeColorSlot.Dk1, LumMod = 1.0 });

        // Without a clrMap: tx1 default → Dk1 → #000000
        var withoutMap = ThemeColorResolver.Resolve(color, theme, effectiveClrMap: null);
        withoutMap.Should().Be(new SrgbColor(0, 0, 0),
            "without clrMap, tx1 maps to Dk1 = black");

        // Inverted master clrMap: tx1 → lt1 → Lt1 → #FFFFFF
        var invertedClrMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "dk1", ["tx1"] = "lt1", ["bg2"] = "dk2", ["tx2"] = "lt2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };

        var withMap = ThemeColorResolver.Resolve(color, theme, effectiveClrMap: invertedClrMap);
        withMap.Should().Be(new SrgbColor(0xFF, 0xFF, 0xFF),
            "inverted clrMap (tx1→lt1) must resolve tx1 to Lt1 = white");
    }

    /// <summary>
    /// The default (no clrMap) mapping: bg1→Lt1. Regression guard.
    /// </summary>
    [Fact]
    public void ThemeColorResolver_DefaultClrMap_Bg1ResolvesToLt1()
    {
        var theme = PresentationTheme.CreateDefault(); // Lt1=#FFFFFF

        var color = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { RoleName = "bg1", Slot = ThemeColorSlot.Lt1, LumMod = 1.0 });

        var result = ThemeColorResolver.Resolve(color, theme, effectiveClrMap: null);
        result.Should().Be(new SrgbColor(0xFF, 0xFF, 0xFF),
            "default map: bg1 → lt1 slot = #FFFFFF");
    }

    /// <summary>
    /// accent1 resolves identically with and without a clrMap (maps identity).
    /// </summary>
    [Fact]
    public void ThemeColorResolver_Accent1_ResolvesCorrectly_ThroughClrMap()
    {
        var theme = PresentationTheme.CreateDefault(); // Accent1=#4472C4

        var color = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { RoleName = "accent1", Slot = ThemeColorSlot.Accent1, LumMod = 1.0 });

        var withoutMap = ThemeColorResolver.Resolve(color, theme, effectiveClrMap: null);
        withoutMap.R.Should().Be(0x44);
        withoutMap.G.Should().Be(0x72);
        withoutMap.B.Should().Be(0xC4);

        var clrMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "dk1", ["tx1"] = "lt1", ["bg2"] = "dk2", ["tx2"] = "lt2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };

        var withMap = ThemeColorResolver.Resolve(color, theme, effectiveClrMap: clrMap);
        withMap.R.Should().Be(0x44, "accent1 must resolve the same with any clrMap that keeps accent1→accent1");
        withMap.G.Should().Be(0x72);
        withMap.B.Should().Be(0xC4);
    }

    /// <summary>
    /// MM5: A slide with ColorMapOverride (inverted) must override the master map during Compose().
    /// Validates that SlideCompositor.Compose threads the override correctly.
    /// </summary>
    [Fact]
    public void Compose_SlideColorMapOverride_OverridesMasterClrMapForShapeFill()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault(); // Lt1=#FFFFFF, Dk1=#000000

        var master = new SlideMaster { Id = "m1" };
        master.ColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        // Shape fill references tx1 (parsed from XML with RoleName set).
        var shapeFill = new ShapeFill.Solid(new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { RoleName = "tx1", Slot = ThemeColorSlot.Dk1, LumMod = 1.0 }));

        // Slide without override: tx1 → master map (dk1) → Dk1 → black
        var slidePlain = new Slide { LayoutId = "l1" };
        slidePlain.Shapes.Add(new SlideShape
        {
            Id = 1, OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            Fill = shapeFill
        });
        p.Slides.Add(slidePlain);

        var opsPlain = SlideCompositor.Compose(p, slidePlain);
        ((ResolvedFill.Solid)opsPlain.OfType<DrawOp.Shape>().Single().Fill).Color
            .Should().Be(new SrgbColor(0, 0, 0),
            "plain slide: tx1 via master map → Dk1 = black");

        // Slide with inverted ColorMapOverride: tx1 → lt1 → Lt1 → white
        var slideOvr = new Slide { LayoutId = "l1" };
        slideOvr.ColorMapOverride = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "dk1", ["tx1"] = "lt1", ["bg2"] = "dk2", ["tx2"] = "lt2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };
        slideOvr.Shapes.Add(new SlideShape
        {
            Id = 1, OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            Fill = shapeFill
        });
        p.Slides.Add(slideOvr);

        var opsOvr = SlideCompositor.Compose(p, slideOvr);
        ((ResolvedFill.Solid)opsOvr.OfType<DrawOp.Shape>().Single().Fill).Color
            .Should().Be(new SrgbColor(0xFF, 0xFF, 0xFF),
            "override slide: tx1 via ColorMapOverride (inverted: tx1→lt1) → Lt1 = white");
    }

    //â”€â”€â”€ Background resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_SlideSolidFillBackground_IsResolvedCorrectly()
    {
        var p = MakePresentation();
        p.Slides[0].Background = new ShapeFill.Solid(new SrgbColor(0xFF, 0x00, 0x00));
        p.Slides[0].Shapes.Clear();

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var bg = ops.OfType<DrawOp.Background>().Single();

        bg.Fill.Should().BeOfType<ResolvedFill.Solid>();
        ((ResolvedFill.Solid)bg.Fill).Color.R.Should().Be(0xFF);
        ((ResolvedFill.Solid)bg.Fill).Color.G.Should().Be(0x00);
    }

    [Fact]
    public void Compose_NoBackgroundSet_DefaultsToWhite()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();
        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);
        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);
        var slide = new Slide { LayoutId = "l1" };
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var bg = ops.OfType<DrawOp.Background>().Single();

        bg.Fill.Should().BeOfType<ResolvedFill.Solid>();
        var solid = (ResolvedFill.Solid)bg.Fill;
        solid.Color.Should().Be(SrgbColor.White);
    }

    // â”€â”€â”€ Text layout resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_ShapeWithText_IncludesResolvedTextLayout()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600
        };
        var tb = new TextBody();
        var para = new Paragraph { Align = TextAlign.Center };
        para.Runs.Add(new Run
        {
            Text = "Hello World",
            FontSizePt = 24.0,
            Bold = true
        });
        tb.Paragraphs.Add(para);
        shape.TextBody = tb;
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Text.Should().NotBeNull();
        shapeOp.Text!.Paragraphs.Should().HaveCount(1);
        shapeOp.Text.Paragraphs[0].Runs.Should().HaveCount(1);
        shapeOp.Text.Paragraphs[0].Runs[0].Text.Should().Be("Hello World");
        shapeOp.Text.Paragraphs[0].Runs[0].FontSizePt.Should().Be(24.0);
        shapeOp.Text.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        shapeOp.Text.Paragraphs[0].Align.Should().Be(TextAlign.Center);
    }

    [Fact]
    public void Compose_TitlePlaceholder_UsesMajorFontDefault()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000
        };
        shape.Text = "My Title";  // Sets a run with no explicit font
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Title should default to major font (Calibri Light)
        var run = shapeOp.Text!.Paragraphs[0].Runs[0];
        run.FontFamily.Should().Be(p.Theme.FontScheme.MajorLatinFont);
    }

    [Fact]
    public void Compose_TitlePlaceholder_UsesLargeFontSizeDefault()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000
        };
        shape.Text = "Title";
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        var run = shapeOp.Text!.Paragraphs[0].Runs[0];
        run.FontSizePt.Should().BeGreaterThan(30.0, "title font default should be larger than body");
    }

    // â”€â”€â”€ Geometry â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_GeometryBoundsMatchConvertedEmuValues()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,  // 1 inch = 96 DIP
            OffsetYEmu = 0,
            ExtentCxEmu = 1828800, // 2 inches = 192 DIP
            ExtentCyEmu = 914400   // 1 inch = 96 DIP
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Geometry should be non-empty
        shapeOp.Geometry.Contours.Should().NotBeEmpty();

        // The bounds should be the converted values
        shapeOp.BoundsDip.X.Should().BeApproximately(96.0, 0.5);
        shapeOp.BoundsDip.Width.Should().BeApproximately(192.0, 0.5);
        shapeOp.BoundsDip.Height.Should().BeApproximately(96.0, 0.5);
    }

    [Fact]
    public void Compose_Picture_EmitsPictureOp()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
        var picShape = new SlideShape
        {
            Id = 5,
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            Picture = new ImagePart { Bytes = imageBytes, ContentType = "image/png" }
        };
        p.Slides[0].Shapes.Add(picShape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var picOp = ops.OfType<DrawOp.Picture>().Single();

        picOp.Bytes.Should().Equal(imageBytes);
        picOp.ContentType.Should().Be("image/png");
        picOp.DestDip.X.Should().BeApproximately(457200 / 9525.0, 0.1);
    }

    [Fact]
    public void Compose_OlePreview_PreservesAuthoredPictureTransforms()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var oleShape = new SlideShape
        {
            Id = 6,
            Kind = SlideShapeKind.Ole,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            RotationDeg = 90,
            FlipH = true,
            FlipV = true,
            Picture = new ImagePart { Bytes = [0x89, 0x50, 0x4E, 0x47], ContentType = "image/png" },
            OleObject = new OleObjectInfo { ProgId = "Excel.Sheet.12", EmbeddedBytes = [1, 2, 3] },
        };
        p.Slides[0].Shapes.Add(oleShape);

        var op = SlideCompositor.Compose(p, FirstSlide(p)).OfType<DrawOp.Picture>().Single();

        op.RotationDeg.Should().Be(90);
        op.FlipH.Should().BeTrue();
        op.FlipV.Should().BeTrue();
    }

    // â”€â”€â”€ Fill / outline â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_SolidFillShape_ResolvedColorMatches()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 900000,
            ExtentCyEmu = 500000,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x12, 0x34, 0x56), alpha: 140))
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.Solid>();
        var solid = (ResolvedFill.Solid)shapeOp.Fill;
        solid.Color.R.Should().Be(0x12);
        solid.Color.G.Should().Be(0x34);
        solid.Color.B.Should().Be(0x56);
        solid.Alpha.Should().Be(140);
    }

    [Fact]
    public void Compose_GradientFill_ResolvedToConcreteColors()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 900000,
            ExtentCyEmu = 500000,
            Fill = new ShapeFill.Gradient(
                new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
                new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
                45.0)
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.Gradient>();
        var grad = (ResolvedFill.Gradient)shapeOp.Fill;
        grad.StartColor.R.Should().Be(0xFF);
        grad.EndColor.B.Should().Be(0xFF);
        grad.AngleDegrees.Should().Be(45.0);
    }

    [Fact]
    public void Compose_GradientFill_PreservesStopAlpha()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 900000,
            ExtentCyEmu = 500000,
            Fill = new ShapeFill.Gradient(
                new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
                new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00), alpha: 0),
                90.0)
        });

        var shapeOp = SlideCompositor.Compose(p, FirstSlide(p)).OfType<DrawOp.Shape>().Single();
        var gradient = shapeOp.Fill.Should().BeOfType<ResolvedFill.Gradient>().Subject;

        gradient.Stops.Select(stop => stop.Alpha).Should().Equal(255, 0);
    }

    [Fact]
    public void Compose_MultiStopGradientFill_AllStopsResolved()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var stops = new[]
        {
            new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00))),
            new GradientStop(0.5, new ThemeAwareColor(new SrgbColor(0x00, 0xFF, 0x00))),
            new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF))),
        };
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 900000, ExtentCyEmu = 500000,
            Fill = new ShapeFill.Gradient(stops, GradientKind.Linear, angleDegrees: 90.0)
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.Gradient>();
        var grad = (ResolvedFill.Gradient)shapeOp.Fill;
        grad.Stops.Should().HaveCount(3, "all 3 stops must be resolved");
        grad.Kind.Should().Be(GradientKind.Linear);
        grad.Stops[0].Color.R.Should().Be(0xFF);
        grad.Stops[1].Color.G.Should().Be(0xFF);
        grad.Stops[2].Color.B.Should().Be(0xFF);
        grad.Stops[0].Position.Should().BeApproximately(0.0, 0.001);
        grad.Stops[1].Position.Should().BeApproximately(0.5, 0.001);
        grad.Stops[2].Position.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void Compose_RadialGradientFill_KindPreserved()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var stops = new[]
        {
            new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0xFF, 0xFF, 0xFF))),
            new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0x00))),
        };
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 900000, ExtentCyEmu = 500000,
            Fill = new ShapeFill.Gradient(stops, GradientKind.Radial, angleDegrees: 0.0)
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.Gradient>();
        var grad = (ResolvedFill.Gradient)shapeOp.Fill;
        grad.Kind.Should().Be(GradientKind.Radial);
        grad.Stops.Should().HaveCount(2);
    }

    [Fact]
    public void Compose_PictureFill_BytesPassedThrough()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // fake PNG header
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 900000, ExtentCyEmu = 500000,
            Fill = new ShapeFill.Picture(imageBytes, "image/png", tile: false)
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.Picture>();
        var pic = (ResolvedFill.Picture)shapeOp.Fill;
        pic.ImageBytes.Should().BeEquivalentTo(imageBytes);
        pic.Tile.Should().BeFalse();
    }

    [Fact]
    public void Compose_PatternFill_ColorsAndPresetResolved()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 900000, ExtentCyEmu = 500000,
            Fill = new ShapeFill.Pattern(
                "cross",
                new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
                new ThemeAwareColor(new SrgbColor(0xFF, 0xFF, 0xFF)))
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.PatternFill>();
        var pat = (ResolvedFill.PatternFill)shapeOp.Fill;
        pat.Preset.Should().Be("cross");
        pat.ForegroundColor.B.Should().Be(0xFF);
        pat.BackgroundColor.R.Should().Be(0xFF);
    }

    [Fact]
    public void Compose_VisibleOutline_ResolvedCorrectly()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 900000,
            ExtentCyEmu = 500000,
            Outline = new ShapeOutline.Visible(
                new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0x00), alpha: 90),
                widthPt: 1.5,
                dash: OutlineDash.Dash)
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Outline.Should().BeOfType<ResolvedOutline.Visible>();
        var vis = (ResolvedOutline.Visible)shapeOp.Outline;
        vis.Dash.Should().Be(OutlineDash.Dash);
        vis.Alpha.Should().Be(90);
        // 1.5pt â†’ DIP = 1.5 * 96/72 = 2.0 DIP
        vis.WidthDip.Should().BeApproximately(2.0, 0.05);
    }

    // â”€â”€â”€ Rotation / flip â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_RotatedShape_PreservesRotation()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 200000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 900000,
            ExtentCyEmu = 500000,
            RotationDeg = 45.0,
            FlipH = true
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.RotationDeg.Should().Be(45.0);
        shapeOp.FlipH.Should().BeTrue();
        shapeOp.FlipV.Should().BeFalse();
    }

    // â”€â”€â”€ Argument validation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_NullPresentation_Throws()
    {
        var slide = new Slide();
        var act = () => SlideCompositor.Compose(null!, slide);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Compose_NullSlide_Throws()
    {
        var p = MakePresentation();
        var act = () => SlideCompositor.Compose(p, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // â"€â"€â"€ P0: Placeholder text alignment + anchor inheritance (Wave 1G) â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

    [Fact]
    public void Compose_CenteredTitle_NoExplicitAlign_InheritsCenter_FromLayoutLstStyle()
    {
        // Arrange: presentation with a layout placeholder that carries lstStyle algn="ctr".
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutTitleBody = new TextBody { DefaultParaAlign = TextAlign.Center, Anchor = VerticalAnchor.Bottom };
        var layoutTitlePara = new Paragraph();
        layoutTitlePara.Runs.Add(new Run { Text = "Click to edit" });
        layoutTitleBody.Paragraphs.Add(layoutTitlePara);

        var layoutTitle = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            OffsetXEmu = 1524000, OffsetYEmu = 1122363,
            ExtentCxEmu = 9144000, ExtentCyEmu = 2387600,
            TextBody = layoutTitleBody
        };
        layout.Placeholders.Add(layoutTitle);
        p.Layouts.Add(layout);

        // Slide shape has no explicit geometry, no explicit align.
        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            // No xfrm — inherits from layout.
        };
        var titleBody = new TextBody();  // Anchor = null, DefaultParaAlign = null
        var titlePara = new Paragraph();  // Align = null — should inherit ctr from layout
        titlePara.Runs.Add(new Run { Text = "My Title" });
        titleBody.Paragraphs.Add(titlePara);
        titleShape.TextBody = titleBody;
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Assert: paragraph should be centered (inherited from layout lstStyle).
        shapeOp.Text.Should().NotBeNull();
        shapeOp.Text!.Paragraphs[0].Align.Should().Be(TextAlign.Center,
            "ctrTitle placeholder inherits algn=ctr from layout lstStyle");
    }

    [Fact]
    public void Compose_CenteredTitle_NoLayoutLstStyle_DefaultsToCenter()
    {
        // Arrange: a CenteredTitle placeholder with no layout lstStyle — should still default to Center.
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutTitle = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            OffsetXEmu = 1524000, OffsetYEmu = 1122363,
            ExtentCxEmu = 9144000, ExtentCyEmu = 2387600
            // No TextBody — so DefaultParaAlign is not set
        };
        layout.Placeholders.Add(layoutTitle);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
        };
        var titleBody = new TextBody();
        var titlePara = new Paragraph();
        titlePara.Runs.Add(new Run { Text = "Title" });
        titleBody.Paragraphs.Add(titlePara);
        titleShape.TextBody = titleBody;
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Assert: ctrTitle placeholder type triggers center default even without explicit lstStyle.
        shapeOp.Text!.Paragraphs[0].Align.Should().Be(TextAlign.Center,
            "ctrTitle placeholder type defaults to Center alignment");
    }

    [Fact]
    public void Compose_CenteredTitle_ExplicitAlignWins_OverInherited()
    {
        // Arrange: layout says ctr, but slide paragraph has explicit Left — explicit wins.
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutBody = new TextBody { DefaultParaAlign = TextAlign.Center };
        var layoutPara = new Paragraph();
        layoutPara.Runs.Add(new Run { Text = "placeholder" });
        layoutBody.Paragraphs.Add(layoutPara);
        var layoutTitle = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            OffsetXEmu = 1524000, OffsetYEmu = 1122363,
            ExtentCxEmu = 9144000, ExtentCyEmu = 2387600,
            TextBody = layoutBody
        };
        layout.Placeholders.Add(layoutTitle);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
        };
        var titleBody = new TextBody();
        var titlePara = new Paragraph { Align = TextAlign.Left }; // Explicit Left
        titlePara.Runs.Add(new Run { Text = "Title" });
        titleBody.Paragraphs.Add(titlePara);
        titleShape.TextBody = titleBody;
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Assert: explicit Left wins over inherited Center.
        shapeOp.Text!.Paragraphs[0].Align.Should().Be(TextAlign.Left,
            "explicit paragraph alignment overrides inherited default");
    }

    [Fact]
    public void Compose_CenteredTitle_VerticalAnchor_InheritedFromLayout()
    {
        // Arrange: layout placeholder has anchor=Bottom in its bodyPr.
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutBody = new TextBody { Anchor = VerticalAnchor.Bottom };
        var layoutPara = new Paragraph();
        layoutPara.Runs.Add(new Run { Text = "ph" });
        layoutBody.Paragraphs.Add(layoutPara);
        var layoutTitle = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            OffsetXEmu = 1524000, OffsetYEmu = 1122363,
            ExtentCxEmu = 9144000, ExtentCyEmu = 2387600,
            TextBody = layoutBody
        };
        layout.Placeholders.Add(layoutTitle);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
        };
        var titleBody = new TextBody(); // Anchor = null → inherit
        var titlePara = new Paragraph();
        titlePara.Runs.Add(new Run { Text = "Title" });
        titleBody.Paragraphs.Add(titlePara);
        titleShape.TextBody = titleBody;
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Assert: vertical anchor comes from the layout placeholder.
        shapeOp.Text!.Anchor.Should().Be(VerticalAnchor.Bottom,
            "vertical anchor is inherited from layout placeholder bodyPr");
    }

    [Fact]
    public void Compose_BodyPlaceholder_AutoFitKind_InheritedFromLayout_WhenSlideBodyPrOmitsIt()
    {
        // Arrange: layout placeholder declares Shrink-text-on-overflow (a:normAutofit), and the
        // slide's own placeholder instance doesn't repeat it — spec-legal, routine OOXML (see
        // PptxPackageReader.ReadTxBody, which sets AutoFitKind = None whenever the slide-level
        // bodyPr has no <a:normAutofit>/<a:spAutoFit> child).
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutBody = new TextBody { AutoFitKind = TextAutoFitKind.Normal };
        var layoutPara = new Paragraph();
        layoutPara.Runs.Add(new Run { Text = "ph" });
        layoutBody.Paragraphs.Add(layoutPara);
        var layoutBodyPh = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 1524000, OffsetYEmu = 1122363,
            ExtentCxEmu = 9144000, ExtentCyEmu = 2387600,
            TextBody = layoutBody
        };
        layout.Placeholders.Add(layoutBodyPh);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var bodyShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
        };
        var slideBody = new TextBody(); // AutoFitKind unset (None) → should inherit Normal.
        var slidePara = new Paragraph();
        slidePara.Runs.Add(new Run { Text = "Body text" });
        slideBody.Paragraphs.Add(slidePara);
        bodyShape.TextBody = slideBody;
        slide.Shapes.Add(bodyShape);
        p.Slides.Add(slide);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Assert: the resolved autofit kind comes from the layout placeholder, matching
        // PowerPoint, instead of silently collapsing to None.
        shapeOp.Text!.AutoFitKind.Should().Be(TextAutoFitKind.Normal,
            "a slide placeholder instance with no autofit of its own inherits the layout's Shrink-text-on-overflow");
    }

    [Fact]
    public void Compose_BodyPlaceholder_AutoFitKind_ShapeOwnValueWinsOverLayout()
    {
        // Sibling/no-regression case: when the slide shape's own bodyPr DOES specify an autofit
        // kind, that explicit value must still win over the layout placeholder's — the shape's
        // own value must never be silently overridden by inheritance.
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutBody = new TextBody { AutoFitKind = TextAutoFitKind.Normal };
        var layoutPara = new Paragraph();
        layoutPara.Runs.Add(new Run { Text = "ph" });
        layoutBody.Paragraphs.Add(layoutPara);
        var layoutBodyPh = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 1524000, OffsetYEmu = 1122363,
            ExtentCxEmu = 9144000, ExtentCyEmu = 2387600,
            TextBody = layoutBody
        };
        layout.Placeholders.Add(layoutBodyPh);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var bodyShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
        };
        // Explicit Shape (grow-shape-to-fit) on the slide instance — must win over layout's Normal.
        var slideBody = new TextBody { AutoFitKind = TextAutoFitKind.Shape };
        var slidePara = new Paragraph();
        slidePara.Runs.Add(new Run { Text = "Body text" });
        slideBody.Paragraphs.Add(slidePara);
        bodyShape.TextBody = slideBody;
        slide.Shapes.Add(bodyShape);
        p.Slides.Add(slide);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Assert: explicit Shape autofit on the slide shape is not overridden by the layout's Normal.
        shapeOp.Text!.AutoFitKind.Should().Be(TextAutoFitKind.Shape,
            "the slide shape's own explicit autofit kind must win over the layout placeholder's");
    }

    // --- Table compositor tests -------------------------------------------

    private static (PresentationModel pres, Slide slide, SlideShape shape)
        MakeTableShape(TableShape table, long offX = 0, long offY = 0, long cx = 9144000, long cy = 4572000)
    {
        var p = MakePresentation();
        var slide = p.Slides[0];
        var shape = new SlideShape
        {
            Id = 1,
            Name = "Table 1",
            Kind = SlideShapeKind.Table,
            OffsetXEmu = offX,
            OffsetYEmu = offY,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            Table = table
        };
        slide.Shapes.Add(shape);
        return (p, slide, shape);
    }

    [Fact]
    public void ComposeTable_EmptyTable_ProducesTableOp()
    {
        // Arrange: empty table (no rows)
        var table = new TableShape();
        var (p, slide, _) = MakeTableShape(table);

        // Act
        var ops = SlideCompositor.Compose(p, slide);

        // Assert: should get a Background op + a Table op
        ops.Should().ContainSingle(o => o is DrawOp.Table, "a Table draw op should be emitted");
    }

    [Fact]
    public void ComposeTable_CellRects_MatchCumulativeWidthsAndHeights()
    {
        // Arrange: 2-col x 2-row table
        // col0=3048000 EMU (320 DIP), col1=3048000 EMU (320 DIP)
        // row0=914400 EMU (96 DIP), row1=914400 EMU (96 DIP)
        const long colW  = 3048000L;
        const long rowH  = 914400L;
        const long offX  = 914400L;  // 96 DIP
        const long offY  = 914400L;  // 96 DIP

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(colW);
        table.ColumnWidthsEmu.Add(colW);

        for (int r = 0; r < 2; r++)
        {
            var row = new TableRow { HeightEmu = rowH };
            row.Cells.Add(new TableCell());
            row.Cells.Add(new TableCell());
            table.Rows.Add(row);
        }

        var (p, slide, _) = MakeTableShape(table, offX, offY, colW * 2, rowH * 2);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var tbl = ops.OfType<DrawOp.Table>().Single();

        // Assert: 4 non-merged cells
        tbl.Cells.Should().HaveCount(4);

        const double emu = 9525.0;
        double x0 = offX / emu;
        double y0 = offY / emu;
        double w  = colW  / emu;
        double h  = rowH  / emu;

        // Row 0, col 0
        tbl.Cells[0].BoundsDip.X.Should().BeApproximately(x0,         0.001);
        tbl.Cells[0].BoundsDip.Y.Should().BeApproximately(y0,         0.001);
        tbl.Cells[0].BoundsDip.Width.Should().BeApproximately(w,       0.001);
        tbl.Cells[0].BoundsDip.Height.Should().BeApproximately(h,      0.001);

        // Row 0, col 1
        tbl.Cells[1].BoundsDip.X.Should().BeApproximately(x0 + w,     0.001);
        tbl.Cells[1].BoundsDip.Y.Should().BeApproximately(y0,         0.001);

        // Row 1, col 0
        tbl.Cells[2].BoundsDip.X.Should().BeApproximately(x0,         0.001);
        tbl.Cells[2].BoundsDip.Y.Should().BeApproximately(y0 + h,     0.001);

        // Row 1, col 1
        tbl.Cells[3].BoundsDip.X.Should().BeApproximately(x0 + w,     0.001);
        tbl.Cells[3].BoundsDip.Y.Should().BeApproximately(y0 + h,     0.001);
    }

    [Fact]
    public void ComposeTable_PreservesCellTextOrientationForHostRenderers()
    {
        const long columnWidth = 3_048_000L;
        const long rowHeight = 1_828_800L;
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(columnWidth);
        table.Rows.Add(new TableRow
        {
            HeightEmu = rowHeight,
            Cells =
            {
                new TableCell
                {
                    TextBody = new TextBody
                    {
                        VerticalType = TextVerticalType.Vertical270,
                        Paragraphs =
                        {
                            new Paragraph { Runs = { new Run { Text = "Vertical" } } },
                        },
                    },
                },
            },
        });

        var (presentation, slide, _) = MakeTableShape(table, 0, 0, columnWidth, rowHeight);
        var cell = SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Table>()
            .Single()
            .Cells
            .Single();

        cell.Text.Should().NotBeNull();
        cell.Text!.VerticalType.Should().Be(TextVerticalType.Vertical270);
    }

    [Fact]
    public void ComposeTable_MergedCells_SkipsCoveredCells()
    {
        // Arrange: 3-col x 1-row; cell 0 has GridSpan=2, cell 1+2 are HMerge.
        const long colW = 3048000L;
        const long rowH = 914400L;

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(colW);
        table.ColumnWidthsEmu.Add(colW);
        table.ColumnWidthsEmu.Add(colW);

        var row = new TableRow { HeightEmu = rowH };
        row.Cells.Add(new TableCell { GridSpan = 2 });
        row.Cells.Add(new TableCell { HMerge = true });
        row.Cells.Add(new TableCell());
        table.Rows.Add(row);

        var (p, slide, _) = MakeTableShape(table, 0, 0, colW * 3, rowH);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var tbl = ops.OfType<DrawOp.Table>().Single();

        // Assert: only 2 cells emitted (origin + col2); HMerge cell skipped
        tbl.Cells.Should().HaveCount(2, "HMerge cells should be skipped");

        const double emu = 9525.0;
        // Origin cell should span 2 columns
        tbl.Cells[0].BoundsDip.Width.Should().BeApproximately(colW * 2 / emu, 0.001);
        // The last cell occupies only col 2
        tbl.Cells[1].BoundsDip.X.Should().BeApproximately(colW * 2 / emu, 0.001);
        tbl.Cells[1].BoundsDip.Width.Should().BeApproximately(colW / emu, 0.001);
    }

    [Fact]
    public void TableStyleData_EffectiveFill_FirstRowWins()
    {
        // Arrange: table with FirstRow flag + style that has distinct firstRow vs band fills.
        var wholeFill  = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xAA, 0xAA, 0xAA)));
        var firstFill  = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x1F, 0x4E, 0x79)));
        var band1Fill  = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xDD, 0xEE, 0xFF)));

        var styleData = new TableStyleData
        {
            StyleId  = "{test}",
            WholeTbl = new TableStyleEntry { Fill = wholeFill },
            FirstRow = new TableStyleEntry { Fill = firstFill },
            Band1H   = new TableStyleEntry { Fill = band1Fill }
        };

        var table = new TableShape
        {
            TableStyleId = "{test}",
            StyleData    = styleData
        };
        table.Flags.FirstRow = true;
        table.Flags.BandRow  = true;
        table.ColumnWidthsEmu.Add(914400);
        table.Rows.Add(new TableRow { HeightEmu = 457200 });
        table.Rows[0].Cells.Add(new TableCell());  // row 0, first row
        table.Rows.Add(new TableRow { HeightEmu = 457200 });
        table.Rows[1].Cells.Add(new TableCell());  // row 1, band1

        // Act: effective fill for row 0 (first row) should be firstFill
        var fill0 = table.ComputeEffectiveFill(0, 0, table.Rows[0].Cells[0]);
        fill0.Should().Be(firstFill, "first row flag should pick firstRow style fill over band");

        // row 1 = band 1 (after first row header, row 1 is band index 0 = Band1H)
        var fill1 = table.ComputeEffectiveFill(1, 0, table.Rows[1].Cells[0]);
        fill1.Should().Be(band1Fill, "second row (first data row) should use Band1H fill");
    }

    /// <summary>
    /// F2: BandCol (column banding) must drive ComputeEffectiveBorderOutline and
    /// ComputeEffectiveTextColor the same way it already drives ComputeEffectiveFill --
    /// alternating Band1V/Band2V per column, not the flat wholeTbl default. Mirrors
    /// <see cref="TableStyleData_EffectiveFill_FirstRowWins"/> but for BandCol + the two
    /// resolvers that previously had no "else if (Flags.BandCol)" branch at all.
    /// </summary>
    [Fact]
    public void TableStyleData_EffectiveBorderAndTextColor_BandColAlternates()
    {
        var wholeBorder = new ShapeOutline.Visible(new SrgbColor(0x80, 0x80, 0x80), 0.75);
        var band1Border  = new ShapeOutline.Visible(new SrgbColor(0x11, 0x22, 0x33), 1.0);
        var band2Border  = new ShapeOutline.Visible(new SrgbColor(0x44, 0x55, 0x66), 1.0);

        var wholeText = new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0x00));
        var band1Text = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00));
        var band2Text = new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF));

        var styleData = new TableStyleData
        {
            StyleId  = "{test-bandcol}",
            WholeTbl = new TableStyleEntry { BorderOutline = wholeBorder, TextColor = wholeText },
            Band1V   = new TableStyleEntry { BorderOutline = band1Border, TextColor = band1Text },
            Band2V   = new TableStyleEntry { BorderOutline = band2Border, TextColor = band2Text }
        };

        var table = new TableShape
        {
            TableStyleId = "{test-bandcol}",
            StyleData    = styleData
        };
        table.Flags.BandRow = false;
        table.Flags.BandCol = true;
        table.ColumnWidthsEmu.Add(914400);
        table.ColumnWidthsEmu.Add(914400);
        var row = new TableRow { HeightEmu = 457200 };
        row.Cells.Add(new TableCell());  // col 0 -> band1 (even column)
        row.Cells.Add(new TableCell());  // col 1 -> band2 (odd column)
        table.Rows.Add(row);

        var border0 = table.ComputeEffectiveBorderOutline(0, 0, table.Rows[0].Cells[0]);
        var border1 = table.ComputeEffectiveBorderOutline(0, 1, table.Rows[0].Cells[1]);
        border0.Should().Be(band1Border, "column 0 under BandCol should resolve Band1V border, not the flat wholeTbl border");
        border1.Should().Be(band2Border, "column 1 under BandCol should resolve Band2V border, not the flat wholeTbl border");

        var color0 = table.ComputeEffectiveTextColor(0, 0);
        var color1 = table.ComputeEffectiveTextColor(0, 1);
        color0.Should().Be(band1Text, "column 0 under BandCol should resolve Band1V text color, not the flat wholeTbl color");
        color1.Should().Be(band2Text, "column 1 under BandCol should resolve Band2V text color, not the flat wholeTbl color");
    }

    /// <summary>
    /// F1 (freep-table-styles): when BOTH Flags.BandRow and Flags.BandCol are true --
    /// exactly the state of every freshly-inserted table (EditingSession.InsertTable sets
    /// BandRow=true by default, and the user then also checks "Banded Column") -- the
    /// column-banded Band1V/Band2V entries must still be consulted for fill, border outline,
    /// and text color. Before the fix, ComputeEffectiveFill/ComputeEffectiveBorderOutline/
    /// ComputeEffectiveTextColor gated BandCol behind "else if (Flags.BandRow)", so BandCol
    /// was silently ignored whenever BandRow was also on.
    /// </summary>
    [Fact]
    public void TableStyleData_EffectiveFillBorderTextColor_BandColAppliesWhenBandRowAlsoOn()
    {
        var wholeFill   = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x99, 0x99, 0x99)));
        var band1VFill  = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x10, 0x20, 0x30)));
        var band2VFill  = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x40, 0x50, 0x60)));

        var wholeBorder = new ShapeOutline.Visible(new SrgbColor(0x80, 0x80, 0x80), 0.75);
        var band1VBorder = new ShapeOutline.Visible(new SrgbColor(0x11, 0x22, 0x33), 1.0);
        var band2VBorder = new ShapeOutline.Visible(new SrgbColor(0x44, 0x55, 0x66), 1.0);

        var wholeText = new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0x00));
        var band1VText = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00));
        var band2VText = new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF));

        var styleData = new TableStyleData
        {
            StyleId  = "{test-bandrow-and-bandcol}",
            WholeTbl = new TableStyleEntry { Fill = wholeFill, BorderOutline = wholeBorder, TextColor = wholeText },
            // Deliberately no Band1H/Band2H entries: if the row-band branch were still
            // (incorrectly) the only one consulted, these two column-adjacent cells would
            // both fall back to wholeTbl and read identically -- masking the bug.
            Band1V   = new TableStyleEntry { Fill = band1VFill, BorderOutline = band1VBorder, TextColor = band1VText },
            Band2V   = new TableStyleEntry { Fill = band2VFill, BorderOutline = band2VBorder, TextColor = band2VText }
        };

        var table = new TableShape
        {
            TableStyleId = "{test-bandrow-and-bandcol}",
            StyleData    = styleData
        };
        // This is exactly EditingSession.InsertTable's default state (BandRow=true) with the
        // user additionally checking "Banded Column" -- both flags on at once.
        table.Flags.BandRow = true;
        table.Flags.BandCol = true;
        table.ColumnWidthsEmu.Add(914400);
        table.ColumnWidthsEmu.Add(914400);
        var row = new TableRow { HeightEmu = 457200 };
        row.Cells.Add(new TableCell());  // row 0, col 0 -> band1 column
        row.Cells.Add(new TableCell());  // row 0, col 1 -> band2 column
        table.Rows.Add(row);

        var fill0 = table.ComputeEffectiveFill(0, 0, table.Rows[0].Cells[0]);
        var fill1 = table.ComputeEffectiveFill(0, 1, table.Rows[0].Cells[1]);
        fill0.Should().Be(band1VFill, "column 0 should resolve Band1V fill even though BandRow is also on");
        fill1.Should().Be(band2VFill, "column 1 should resolve Band2V fill even though BandRow is also on");

        var border0 = table.ComputeEffectiveBorderOutline(0, 0, table.Rows[0].Cells[0]);
        var border1 = table.ComputeEffectiveBorderOutline(0, 1, table.Rows[0].Cells[1]);
        border0.Should().Be(band1VBorder, "column 0 should resolve Band1V border even though BandRow is also on");
        border1.Should().Be(band2VBorder, "column 1 should resolve Band2V border even though BandRow is also on");

        var color0 = table.ComputeEffectiveTextColor(0, 0);
        var color1 = table.ComputeEffectiveTextColor(0, 1);
        color0.Should().Be(band1VText, "column 0 should resolve Band1V text color even though BandRow is also on");
        color1.Should().Be(band2VText, "column 1 should resolve Band2V text color even though BandRow is also on");
    }

    /// <summary>
    /// Sibling no-regression case for F1: with BandRow=true and BandCol=false (the plain
    /// default-inserted-table state), banding must still vary only by row, not by column --
    /// two cells in the same row must keep resolving to the identical row-band fill. Guards
    /// against an over-broad fix that made BandCol run unconditionally.
    /// </summary>
    [Fact]
    public void TableStyleData_EffectiveFill_BandRowOnly_StillIgnoresColumnPosition()
    {
        var band1HFill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xDD, 0xEE, 0xFF)));
        var band1VFill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x10, 0x20, 0x30)));

        var styleData = new TableStyleData
        {
            StyleId = "{test-bandrow-only}",
            Band1H  = new TableStyleEntry { Fill = band1HFill },
            // A distinct column-band fill that must NOT leak in while BandCol is off.
            Band1V  = new TableStyleEntry { Fill = band1VFill }
        };

        var table = new TableShape
        {
            TableStyleId = "{test-bandrow-only}",
            StyleData    = styleData
        };
        table.Flags.BandRow = true;
        table.Flags.BandCol = false;
        table.ColumnWidthsEmu.Add(914400);
        table.ColumnWidthsEmu.Add(914400);
        var row = new TableRow { HeightEmu = 457200 };
        row.Cells.Add(new TableCell());  // row 0, col 0
        row.Cells.Add(new TableCell());  // row 0, col 1
        table.Rows.Add(row);

        var fill0 = table.ComputeEffectiveFill(0, 0, table.Rows[0].Cells[0]);
        var fill1 = table.ComputeEffectiveFill(0, 1, table.Rows[0].Cells[1]);
        fill0.Should().Be(band1HFill, "with BandCol off, column 0 should still resolve the row-band fill");
        fill1.Should().Be(band1HFill, "with BandCol off, column 1 should match column 0 -- banding is by row only");
    }

    // ─── R133: freshly inserted/pasted tables must render borders/fill/banding ────

    /// <summary>
    /// EditingSession.InsertTable assigns the built-in "Medium Style 2 - Accent 1" GUID
    /// ({5C22544A-7EE6-4342-B048-85BDC9FD1C3A}) but never populates TableShape.StyleData
    /// (no ppt/tableStyles.xml exists for an in-memory insert). Before the
    /// BuiltInTableStyleCatalog fallback, every TableShape.ComputeEffective* method
    /// short-circuited to null because they all gated on StyleData != null, so the
    /// composed table had no fill and no border on any side — floating text with no grid.
    /// </summary>
    [Fact]
    public void ComposeTable_FreshlyInsertedTable_HasVisibleFillAndBorders()
    {
        var presentation = new PresentationModel();
        presentation.Slides.Add(new Slide());
        var bus     = new PresentationCommandBus(presentation);
        var session = new EditingSession(presentation, bus);

        var shape = session.InsertTable(2, 2);
        shape.Table!.StyleData.Should().BeNull(
            "InsertTable never sets StyleData directly — the catalog fallback must supply it at render time");

        var ops = SlideCompositor.Compose(presentation, session.CurrentSlide!);
        var tbl  = ops.OfType<DrawOp.Table>().Single();

        tbl.Cells.Should().NotBeEmpty();
        foreach (var cell in tbl.Cells)
        {
            cell.Fill.Should().NotBeOfType<ResolvedFill.None>(
                "a freshly inserted table must have a visible fill, not render as invisible");
            var anyBorder = cell.BorderLeft is not ResolvedOutline.None
                         || cell.BorderRight is not ResolvedOutline.None
                         || cell.BorderTop is not ResolvedOutline.None
                         || cell.BorderBottom is not ResolvedOutline.None;
            anyBorder.Should().BeTrue(
                "a freshly inserted table must have at least one visible border side, not render as a borderless grid");
        }

        // Header row (row 0, FirstRow flag) and the band row below it must resolve to
        // different fills — this is the "banding" the finding calls out, not just "some fill".
        tbl.Cells[0].Fill.Should().NotBe(tbl.Cells[2].Fill,
            "header row and first data row must use distinct style-driven fills (firstRow vs band)");
    }

    /// <summary>
    /// Sibling to <see cref="ComposeTable_FreshlyInsertedTable_HasVisibleFillAndBorders"/>:
    /// an explicit per-cell fill must still win over the built-in catalog style, proving the
    /// catalog fallback only fills the gap when StyleData is absent and never overrides
    /// explicit tcPr formatting (TableShape.ComputeEffectiveFill's documented "explicit tcPr
    /// fill always wins" contract).
    /// </summary>
    [Fact]
    public void ComposeTable_ExplicitCellFill_WinsOverBuiltInCatalogStyle()
    {
        var presentation = new PresentationModel();
        presentation.Slides.Add(new Slide());
        var bus     = new PresentationCommandBus(presentation);
        var session = new EditingSession(presentation, bus);

        var shape = session.InsertTable(1, 1);
        var explicitColor = new SrgbColor(0x12, 0x34, 0x56);
        shape.Table!.Rows[0].Cells[0].Fill = new ShapeFill.Solid(new ThemeAwareColor(explicitColor));

        var ops = SlideCompositor.Compose(presentation, session.CurrentSlide!);
        var cell = ops.OfType<DrawOp.Table>().Single().Cells.Single();

        var solid = cell.Fill.Should().BeOfType<ResolvedFill.Solid>().Subject;
        solid.Color.Should().Be(explicitColor,
            "an explicit cell fill must win over the built-in table style catalog fallback");
    }

    // ─── II3: slidenum field always shows correct slide number ────────────────

    /// <summary>
    /// A 3-slide deck with a slidenum field on each slide: compositing slide at
    /// index 2 (0-based) must render "3", not "1".
    /// Regression guard for the EnsureOps bug that left slideIndex=0 (default).
    /// </summary>
    [Fact]
    public void Compose_SlidenuFieldOnThirdSlide_RendersThree()
    {
        // Arrange: 3-slide presentation; each slide has a slidenum field.
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        for (int i = 0; i < 3; i++)
        {
            var slide = new Slide();
            var para  = new Paragraph();
            para.Runs.Add(new Run
            {
                Text  = (i + 1).ToString(),
                Field = new FieldRun { FieldType = "slidenum", CachedText = (i + 1).ToString() }
            });
            var body = new TextBody();
            body.Paragraphs.Add(para);
            slide.Shapes.Add(new SlideShape
            {
                Id          = (uint)(i + 1),
                Kind        = SlideShapeKind.AutoShape,
                OffsetXEmu  = 914400,
                OffsetYEmu  = 6400000,
                ExtentCxEmu = 4572000,
                ExtentCyEmu = 457200,
                TextBody    = body
            });
            p.Slides.Add(slide);
        }

        // Act: compose slide at 0-based index 2 (third slide).
        var thirdSlide = p.Slides[2];
        var ops        = SlideCompositor.Compose(p, thirdSlide, slideIndex: 2);
        var shapeOp    = ops.OfType<DrawOp.Shape>().Single();

        // Assert: rendered text must be "3", not "1".
        var runText = string.Concat(shapeOp.Text!.Paragraphs.SelectMany(par => par.Runs.Select(r => r.Text)));
        runText.Should().Be("3",
            "slidenum field on slide index 2 must render slide number 3");
    }

    [Fact]
    public void Compose_SlidenuField_IndexZeroGivesOne_IndexTwoGivesThree()
    {
        // Confirm that the slideIndex parameter is the only difference between
        // "shows 1" (old bug: always index 0) and "shows 3" (correct: index 2).
        var p     = new PresentationModel();
        p.Theme   = PresentationTheme.CreateDefault();
        var slide = new Slide();

        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text  = "?",
            Field = new FieldRun { FieldType = "slidenum", CachedText = "" }
        });
        var body = new TextBody();
        body.Paragraphs.Add(para);
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            TextBody = body
        });
        p.Slides.Add(slide);

        string TextAt(int idx) =>
            string.Concat(SlideCompositor.Compose(p, slide, idx)
                .OfType<DrawOp.Shape>().Single()
                .Text!.Paragraphs.SelectMany(par => par.Runs.Select(r => r.Text)));

        TextAt(0).Should().Be("1", "index 0 → slide 1");
        TextAt(2).Should().Be("3", "index 2 → slide 3");
    }

    // ─── II6: empty-cache field fallback ─────────────────────────────────────

    [Theory]
    [InlineData("datetime1")]
    [InlineData("datetime2")]
    [InlineData("datetime3")]
    [InlineData("datetime4")]
    public void ResolveField_DatetimeEmptyCache_RendersSelectedDateFormat_NotTypeToken(
        string fieldType)
    {
        // A datetime field with no cached text must NOT render the literal token
        // "datetime1" — it should render something date-like.
        var p     = new PresentationModel();
        p.Theme   = PresentationTheme.CreateDefault();
        var slide = new Slide();

        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text  = "",
            Field = new FieldRun { FieldType = fieldType, CachedText = "" }
        });
        var body = new TextBody();
        body.Paragraphs.Add(para);
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            TextBody = body
        });
        p.Slides.Add(slide);

        var ops    = SlideCompositor.Compose(p, slide, slideIndex: 0);
        var runText = string.Concat(ops.OfType<DrawOp.Shape>().Single()
            .Text!.Paragraphs.SelectMany(par => par.Runs.Select(r => r.Text)));

        runText.Should().Be(HeaderFooterDateTimeFormatter.Format(fieldType, DateTime.Now),
            "empty-cache datetime fields should use the selected automatic format");
        runText.Should().NotBe(fieldType,
            "empty-cache datetime field must not render the raw field-type token");
        runText.Should().MatchRegex(@"\d",
            "empty-cache datetime field should contain at least one digit (a date)");
    }

    [Theory]
    [InlineData("datetime1", "7/6/2026")]
    [InlineData("datetime2", "Monday, July 6, 2026")]
    [InlineData("datetime3", "6 July 2026")]
    [InlineData("datetime4", "July 6, 2026")]
    public void ResolveField_AutomaticDateFormatsUseFieldType(string fieldType, string expected)
    {
        HeaderFooterDateTimeFormatter.Format(fieldType, new DateTime(2026, 7, 6))
            .Should().Be(expected);
    }

    [Fact]
    public void ResolveField_FooterEmptyCache_RendersEmpty_NotTypeToken()
    {
        var p     = new PresentationModel();
        p.Theme   = PresentationTheme.CreateDefault();
        var slide = new Slide();

        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text  = "",
            Field = new FieldRun { FieldType = "footer", CachedText = "" }
        });
        var body = new TextBody();
        body.Paragraphs.Add(para);
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            TextBody = body
        });
        p.Slides.Add(slide);

        var ops     = SlideCompositor.Compose(p, slide, slideIndex: 0);
        var runText = string.Concat(ops.OfType<DrawOp.Shape>().Single()
            .Text!.Paragraphs.SelectMany(par => par.Runs.Select(r => r.Text)));

        runText.Should().BeEmpty(
            "empty-cache footer field must render empty, not the raw type token 'footer'");
    }

    [Fact]
    public void ResolveField_WithCachedText_AlwaysUsesCacheOverFallback()
    {
        // A field that HAS cached text must always render the cache, regardless of type.
        var p     = new PresentationModel();
        p.Theme   = PresentationTheme.CreateDefault();
        var slide = new Slide();

        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text  = "Custom Footer",
            Field = new FieldRun { FieldType = "footer", CachedText = "Custom Footer" }
        });
        var body = new TextBody();
        body.Paragraphs.Add(para);
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            TextBody = body
        });
        p.Slides.Add(slide);

        var ops     = SlideCompositor.Compose(p, slide, slideIndex: 0);
        var runText = string.Concat(ops.OfType<DrawOp.Shape>().Single()
            .Text!.Paragraphs.SelectMany(par => par.Runs.Select(r => r.Text)));

        runText.Should().Be("Custom Footer",
            "fields with non-empty cached text must always render the cache");
    }

    // ─── MM3: master/layout text-style inheritance ────────────────────────────────

    /// <summary>
    /// A master bodyStyle lvl1 defining sz=2800 (28pt) should supply the font size for a body
    /// run that has no explicit FontSizePt, instead of falling back to the hard-coded 18pt.
    /// </summary>
    [Fact]
    public void Compose_BodyRun_NoExplicitSize_InheritsFromMasterBodyStyle()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        // bodyStyle lvl1 (index 0) defines 28pt.
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { FontSizePt = 28.0 };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        // Run with NO explicit FontSizePt — should inherit 28pt from master bodyStyle.
        para.Runs.Add(new Run { Text = "Body text" });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.FontSizePt.Should().Be(28.0,
            "body run with no explicit size must inherit 28pt from master bodyStyle lvl1");
    }

    /// <summary>
    /// A master titleStyle lvl1 with a specific font and solid color must supply
    /// those defaults to a title run that has no explicit font or color.
    /// </summary>
    [Fact]
    public void Compose_TitleRun_NoExplicitFontOrColor_InheritsFromMasterTitleStyle()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        // titleStyle lvl1 defines a specific font and red color.
        master.TextStyles.TitleStyle[0] = new TextStyleLevel
        {
            LatinFont = "Arial",
            Color = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
            FontSizePt = 40.0
        };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200, OffsetYEmu = 274320,
            ExtentCxEmu = 8229600, ExtentCyEmu = 1143000
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        // Run has no explicit font or color — should inherit from master titleStyle.
        para.Runs.Add(new Run { Text = "Title text" });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.FontFamily.Should().Be("Arial",
            "title run with no explicit font must inherit 'Arial' from master titleStyle lvl1");
        run.Color.R.Should().Be(0xFF,
            "title run with no explicit color must inherit red from master titleStyle lvl1");
        run.Color.G.Should().Be(0x00);
        run.Color.B.Should().Be(0x00);
    }

    /// <summary>
    /// A layout placeholder's a:lstStyle overrides the master's p:txStyles for a body run.
    /// The layout wins over the master when both define font size at the same level.
    /// </summary>
    [Fact]
    public void Compose_BodyRun_LayoutLstStyleOverridesMasterBodyStyle()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        // Master defines 24pt for body lvl1.
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { FontSizePt = 24.0 };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout placeholder has its own lstStyle overriding to 32pt.
        var layoutLstStyle = new TextStyleLevels();
        layoutLstStyle[0] = new TextStyleLevel { FontSizePt = 32.0 };
        var layoutBodyPh = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963,
            TextBody = new TextBody { LstStyle = layoutLstStyle }
        };
        layout.Placeholders.Add(layoutBodyPh);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        // Run with no explicit size — layout (32pt) must beat master (24pt).
        para.Runs.Add(new Run { Text = "Body run" });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.FontSizePt.Should().Be(32.0,
            "layout lstStyle (32pt) must override master bodyStyle (24pt)");
    }

    /// <summary>
    /// A shape's OWN txBody-level a:lstStyle sits between direct paragraph properties and the
    /// layout in PowerPoint's inheritance chain, so it must override both the layout
    /// placeholder's lstStyle and the master's p:txStyles when all three define the same level.
    /// Regression guard for the "shape lstStyle never consulted" bug: before the fix,
    /// ResolveTextStyleInheritance skipped the shape's own lstStyle entirely and fell through
    /// straight to the layout, so this run rendered at the layout's 32pt instead of the shape's
    /// own 40pt.
    /// </summary>
    [Fact]
    public void Compose_BodyRun_ShapeLstStyleOverridesLayoutAndMasterStyle()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        // Master defines 24pt for body lvl1.
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { FontSizePt = 24.0 };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout placeholder's own lstStyle overrides the master to 32pt.
        var layoutLstStyle = new TextStyleLevels();
        layoutLstStyle[0] = new TextStyleLevel { FontSizePt = 32.0 };
        var layoutBodyPh = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963,
            TextBody = new TextBody { LstStyle = layoutLstStyle }
        };
        layout.Placeholders.Add(layoutBodyPh);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        // The shape's own txBody carries its own lstStyle overriding to 40pt.
        var shapeLstStyle = new TextStyleLevels();
        shapeLstStyle[0] = new TextStyleLevel { FontSizePt = 40.0 };
        var body = new TextBody { LstStyle = shapeLstStyle };
        var para = new Paragraph { Level = 0 };
        // Run with no explicit size — shape lstStyle (40pt) must beat layout (32pt) and master (24pt).
        para.Runs.Add(new Run { Text = "Body run" });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.FontSizePt.Should().Be(40.0,
            "the shape's own lstStyle (40pt) must win over both the layout lstStyle (32pt) " +
            "and the master bodyStyle (24pt) per PowerPoint's inheritance order");
    }

    /// <summary>
    /// Sibling no-regression guard: when the shape's own lstStyle has no entry at all for the
    /// paragraph's level (or is entirely absent), resolution must still fall back correctly
    /// through the layout's lstStyle. This pins down that adding the shape-lstStyle lookup did
    /// not break the pre-existing layout/master fallback chain.
    /// </summary>
    [Fact]
    public void Compose_BodyRun_ShapeLstStyleMissingLevel_StillFallsBackToLayoutLstStyle()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { FontSizePt = 24.0 };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutLstStyle = new TextStyleLevels();
        layoutLstStyle[0] = new TextStyleLevel { FontSizePt = 32.0 };
        var layoutBodyPh = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963,
            TextBody = new TextBody { LstStyle = layoutLstStyle }
        };
        layout.Placeholders.Add(layoutBodyPh);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        // Shape carries a lstStyle object, but it defines NO levels at all (HasAny == false in
        // spirit — every slot is null), so resolution must skip past it to the layout's 32pt.
        var shapeLstStyle = new TextStyleLevels();
        var body = new TextBody { LstStyle = shapeLstStyle };
        var para = new Paragraph { Level = 0 };
        para.Runs.Add(new Run { Text = "Body run" });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.FontSizePt.Should().Be(32.0,
            "when the shape's lstStyle has no entry at this level, resolution must still fall " +
            "back to the layout's lstStyle (32pt) rather than the master (24pt)");
    }

    /// <summary>
    /// An explicit run font size always wins over the master's inherited style.
    /// Regression guard: explicit formatting must not be overridden by the inheritance chain.
    /// </summary>
    [Fact]
    public void Compose_ExplicitRunSize_WinsOverMasterBodyStyle()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        // Master defines 28pt for body lvl1.
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { FontSizePt = 28.0 };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        // Run WITH explicit FontSizePt=12 — must win over master's 28pt.
        para.Runs.Add(new Run { Text = "Small text", FontSizePt = 12.0 });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.FontSizePt.Should().Be(12.0,
            "explicit run FontSizePt=12 must win over master bodyStyle's 28pt");
    }

    /// <summary>
    /// The +mn-lt font token in a master style must resolve to the theme's minor latin font.
    /// </summary>
    [Fact]
    public void Compose_MasterBodyStyle_PlusMinLt_ResolvesToThemeMinorFont()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        // Master bodyStyle defines font as "+mn-lt" (theme minor font token).
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { LatinFont = "+mn-lt" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        // Run with no explicit font — should inherit +mn-lt resolved to theme minor font.
        para.Runs.Add(new Run { Text = "Body" });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.FontFamily.Should().Be(p.Theme.FontScheme.MinorLatinFont,
            "+mn-lt must resolve to the theme minor latin font");
    }

    // ─── PP1: explicit b="0" / i="0" must override inherited bold/italic ─────────────────────────

    /// <summary>
    /// Regression case: master bodyStyle lvl1 bold=true; a slide body run with explicit b="0"
    /// (BoldSet=true, Bold=false) must render NON-bold.  Before the PP1 fix the OR logic forced
    /// bold=true because Bold=false was indistinguishable from "unset".
    /// </summary>
    [Fact]
    public void Compose_ExplicitBoldFalse_OverridesInheritedBold()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        // Master body style: lvl1 bold = true.
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { Bold = true };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        // Run with explicit b="0": BoldSet=true, Bold=false — must WIN over inherited bold.
        para.Runs.Add(new Run { Text = "Un-bolded", Bold = false, BoldSet = true });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.Bold.Should().BeFalse(
            "explicit b=\"0\" (BoldSet=true, Bold=false) must override inherited bold=true from master");
    }

    /// <summary>
    /// A run with NO @b attribute (BoldSet=false) over an inherited-bold master style must
    /// render BOLD — the inherited style wins when there is no explicit run value.
    /// </summary>
    [Fact]
    public void Compose_NoExplicitBold_InheritsBoldFromMaster()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { Bold = true };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        // Run with NO explicit bold (BoldSet=false) — must inherit true from master.
        para.Runs.Add(new Run { Text = "Inherited bold" }); // Bold=false, BoldSet=false (defaults)
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.Bold.Should().BeTrue(
            "run with no explicit @b must inherit bold=true from master bodyStyle");
    }

    /// <summary>
    /// An explicit b="1" run (BoldSet=true, Bold=true) over a non-bold inherited style renders bold.
    /// </summary>
    [Fact]
    public void Compose_ExplicitBoldTrue_RendersAsBold()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { Bold = false };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        // Explicit b="1": BoldSet=true, Bold=true.
        para.Runs.Add(new Run { Text = "Explicitly bold", Bold = true, BoldSet = true });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.Bold.Should().BeTrue(
            "explicit b=\"1\" must render bold regardless of inherited style");
    }

    /// <summary>
    /// PP1 italic variant: explicit i="0" run over an inherited-italic master style must render
    /// NON-italic.
    /// </summary>
    [Fact]
    public void Compose_ExplicitItalicFalse_OverridesInheritedItalic()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { Italic = true };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        // Explicit i="0": ItalicSet=true, Italic=false — must WIN over inherited italic.
        para.Runs.Add(new Run { Text = "Un-italicized", Italic = false, ItalicSet = true });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.Italic.Should().BeFalse(
            "explicit i=\"0\" (ItalicSet=true, Italic=false) must override inherited italic=true");
    }

    /// <summary>
    /// PP1 italic: run with NO @i inherits italic=true from master.
    /// </summary>
    [Fact]
    public void Compose_NoExplicitItalic_InheritsItalicFromMaster()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { Italic = true };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        // Run with no explicit italic (ItalicSet=false) — must inherit true from master.
        para.Runs.Add(new Run { Text = "Inherited italic" }); // Italic=false, ItalicSet=false
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.Italic.Should().BeTrue(
            "run with no explicit @i must inherit italic=true from master bodyStyle");
    }

    /// <summary>
    /// PP1 italic: explicit i="1" renders italic even with non-italic inherited style.
    /// </summary>
    [Fact]
    public void Compose_ExplicitItalicTrue_RendersAsItalic()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        master.TextStyles.BodyStyle[0] = new TextStyleLevel { Italic = false };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        para.Runs.Add(new Run { Text = "Explicitly italic", Italic = true, ItalicSet = true });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.Italic.Should().BeTrue(
            "explicit i=\"1\" must render italic regardless of inherited style");
    }

    /// <summary>
    /// F1 regression: text-style inheritance must merge PER PROPERTY across layers, not treat
    /// whichever layer has ANY entry at a level as the final whole answer. Here the layout's
    /// lstStyle overrides ONLY font size (32pt); it does not set a color. The master's bodyStyle
    /// supplies a red color (and a different, losing, font size). Before the fix,
    /// ResolveTextStyleInheritance returned the layout's TextStyleLevel object wholesale (since
    /// it had ANY entry at level 0) and never consulted the master for the color field the
    /// layout left null, so the run rendered black instead of red.
    /// </summary>
    [Fact]
    public void Compose_BodyRun_LayoutSetsOnlySize_MasterColorStillAppliesForUnsetProperty()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        // Master bodyStyle lvl1 supplies a color (red) that the layout does NOT override.
        master.TextStyles.BodyStyle[0] = new TextStyleLevel
        {
            Color = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
            FontSizePt = 24.0
        };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout placeholder's lstStyle overrides ONLY font size — no Color set here.
        var layoutLstStyle = new TextStyleLevels();
        layoutLstStyle[0] = new TextStyleLevel { FontSizePt = 32.0 };
        var layoutBodyPh = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963,
            TextBody = new TextBody { LstStyle = layoutLstStyle }
        };
        layout.Placeholders.Add(layoutBodyPh);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        // Run with no explicit size/color.
        para.Runs.Add(new Run { Text = "Body run" });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.FontSizePt.Should().Be(32.0,
            "the layout's own lstStyle sets font size and must win over the master's 24pt");
        run.Color.R.Should().Be(0xFF,
            "the layout's lstStyle left color unset, so the master's red bodyStyle color " +
            "must still apply instead of falling straight to the hard-coded black default");
        run.Color.G.Should().Be(0x00);
        run.Color.B.Should().Be(0x00);
    }

    /// <summary>
    /// Sibling no-regression guard for F1: when the SAME property is set at more than one
    /// layer, the most specific layer must still win (shape > layout > master), exactly as
    /// before the per-property merge fix. Pins that merging per-property did not change the
    /// precedence order for properties that collide across layers.
    /// </summary>
    [Fact]
    public void Compose_BodyRun_ColorSetAtBothLayoutAndMaster_LayoutColorWins()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.TextStyles = new MasterTextStyles();
        // Master supplies blue.
        master.TextStyles.BodyStyle[0] = new TextStyleLevel
        {
            Color = new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF))
        };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout overrides to green — same property (Color) as the master.
        var layoutLstStyle = new TextStyleLevels();
        layoutLstStyle[0] = new TextStyleLevel
        {
            Color = new ThemeAwareColor(new SrgbColor(0x00, 0xFF, 0x00))
        };
        var layoutBodyPh = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963,
            TextBody = new TextBody { LstStyle = layoutLstStyle }
        };
        layout.Placeholders.Add(layoutBodyPh);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200, OffsetYEmu = 1371600,
            ExtentCxEmu = 8229600, ExtentCyEmu = 4525963
        };
        var body = new TextBody();
        var para = new Paragraph { Level = 0 };
        para.Runs.Add(new Run { Text = "Body run" });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.Color.R.Should().Be(0x00);
        run.Color.G.Should().Be(0xFF,
            "when both layout and master set Color, the more specific layout layer must win");
        run.Color.B.Should().Be(0x00);
    }

    /// <summary>
    /// Regression: a plain non-placeholder shape with no master TextStyles should still
    /// compose correctly — inheriting defaults by placeholder type (or hard-coded fallback).
    /// </summary>
    [Fact]
    public void Compose_NonPlaceholderShape_NoMasterTextStyles_StillUsesHardCodedDefaults()
    {
        var p = MakePresentation();  // uses CreateEmpty — master has no TextStyles
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200, OffsetYEmu = 274320,
            ExtentCxEmu = 4572000, ExtentCyEmu = 1371600
        };
        var body = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Plain text" });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        // Hard-coded fallback: body font size = 18pt, minor font.
        run.FontSizePt.Should().Be(18.0, "non-placeholder shape without master styles falls back to 18pt");
        run.FontFamily.Should().Be(p.Theme.FontScheme.MinorLatinFont);
        run.Color.Should().Be(SrgbColor.Black);
    }

    // ── MM4: multi-master theme resolution ────────────────────────────────────────────────────

    /// <summary>
    /// A 2-master deck: master1 accent1=blue, master2 accent1=red.
    /// A slide on master1 must resolve accent1 to BLUE; a slide on master2 to RED.
    /// Before the MM4 fix, both resolved to the last-read master's theme (red), because
    /// SlideCompositor used the single presentation.Theme instead of the owning master's theme.
    /// </summary>
    [Fact]
    public void Compose_MultiMaster_EachSlideResolvesThemeFromOwnMaster()
    {
        var blue = SrgbColor.FromRgb(0x0000FF);
        var red  = SrgbColor.FromRgb(0xFF0000);

        var pres = new PresentationModel();

        var master1 = new SlideMaster { Id = "rId1" };
        master1.Theme = new PresentationTheme { Name = "Blue Theme" };
        master1.Theme.ColorScheme[ThemeColorSlot.Accent1] = blue;

        var master2 = new SlideMaster { Id = "rId2" };
        master2.Theme = new PresentationTheme { Name = "Red Theme" };
        master2.Theme.ColorScheme[ThemeColorSlot.Accent1] = red;

        pres.Masters.Add(master1);
        pres.Masters.Add(master2);
        // presentation.Theme = first master's theme (single-master compat convention).
        pres.Theme = master1.Theme;

        var layout1 = new SlideLayout { Id = "rIdL1", MasterId = "rId1", Name = "Blank", LayoutType = SlideLayoutType.Blank };
        var layout2 = new SlideLayout { Id = "rIdL2", MasterId = "rId2", Name = "Blank", LayoutType = SlideLayoutType.Blank };
        pres.Layouts.Add(layout1);
        pres.Layouts.Add(layout2);

        // Slide 1 on master1: solid fill using accent1 scheme color.
        var slide1 = new Slide { LayoutId = "rIdL1" };
        slide1.Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200, OffsetYEmu = 274320,
            ExtentCxEmu = 1000000, ExtentCyEmu = 500000,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(
                SrgbColor.Black, // placeholder resolved value — must be re-resolved by compositor
                new SchemeColorRef { RoleName = "accent1", Slot = ThemeColorSlot.Accent1 }))
        });
        pres.Slides.Add(slide1);

        // Slide 2 on master2: identical scheme color reference, must resolve differently.
        var slide2 = new Slide { LayoutId = "rIdL2" };
        slide2.Shapes.Add(new SlideShape
        {
            Id = 2,
            OffsetXEmu = 457200, OffsetYEmu = 274320,
            ExtentCxEmu = 1000000, ExtentCyEmu = 500000,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(
                SrgbColor.Black,
                new SchemeColorRef { RoleName = "accent1", Slot = ThemeColorSlot.Accent1 }))
        });
        pres.Slides.Add(slide2);

        // Compose slide1 → accent1 must resolve to BLUE.
        var ops1 = SlideCompositor.Compose(pres, slide1);
        var shapeOp1 = ops1.OfType<DrawOp.Shape>().First();
        shapeOp1.Fill.Should().BeOfType<ResolvedFill.Solid>("shape has solid fill");
        ((ResolvedFill.Solid)shapeOp1.Fill).Color.Should().Be(blue,
            "slide on master1 must resolve accent1 to BLUE (master1.Theme); before MM4 fix this was RED");

        // Compose slide2 → accent1 must resolve to RED.
        var ops2 = SlideCompositor.Compose(pres, slide2);
        var shapeOp2 = ops2.OfType<DrawOp.Shape>().First();
        shapeOp2.Fill.Should().BeOfType<ResolvedFill.Solid>("shape has solid fill");
        ((ResolvedFill.Solid)shapeOp2.Fill).Color.Should().Be(red,
            "slide on master2 must resolve accent1 to RED (master2.Theme)");
    }

    /// <summary>
    /// Single-master deck: when master.Theme is null (degenerate package), the compositor
    /// falls back to presentation.Theme — no regression.
    /// </summary>
    [Fact]
    public void Compose_SingleMaster_NullMasterTheme_FallsBackToPresentationTheme()
    {
        var pres = PresentationModel.CreateEmpty();
        // Explicitly null out master.Theme to simulate a degenerate package.
        pres.Masters[0].Theme = null;
        pres.Theme.ColorScheme[ThemeColorSlot.Accent1] = SrgbColor.FromRgb(0x00FF00); // green

        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200, OffsetYEmu = 274320,
            ExtentCxEmu = 1000000, ExtentCyEmu = 500000,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(
                SrgbColor.Black,
                new SchemeColorRef { RoleName = "accent1", Slot = ThemeColorSlot.Accent1 }))
        });

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var shapeOp = ops.OfType<DrawOp.Shape>().First();
        ((ResolvedFill.Solid)shapeOp.Fill).Color.Should().Be(SrgbColor.FromRgb(0x00FF00),
            "when master.Theme is null, falls back to presentation.Theme");
    }

    // ─── BV1: doughnut chart per-point slice colors ───────────────────────────────────────────

    [Fact]
    public void Compose_Chart_CarriesShapeRotationIntoDrawOp()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 8,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 3429000,
            RotationDeg = 27,
            Chart = new ChartShape { ChartType = ChartType.Line },
        });

        var chartOp = SlideCompositor.Compose(p, FirstSlide(p)).OfType<DrawOp.Chart>().Single();

        chartOp.RotationDeg.Should().Be(27);
    }

    /// <summary>
    /// BV1: A doughnut chart must expand seriesColors one-per-POINT (like pie), not one-per-series.
    /// Before the fix the per-series branch produced seriesColors.Length == 1 (single series)
    /// so the renderer mis-colored slices.
    /// </summary>
    [Fact]
    public void BV1_DoughnutChart_SeriesColors_OnePerPoint()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var chart = new ChartShape { ChartType = ChartType.Doughnut };
        chart.Categories.AddRange(new[] { "Slice A", "Slice B", "Slice C" });

        var series = new ChartSeries { Name = "Doughnut" };
        series.Values.AddRange(new double?[] { 40.0, 35.0, 25.0 });
        // Give slice 1 an explicit sRGB point color so we can verify it's used.
        series.PointColors[1] = new ThemeAwareColor(new SrgbColor(0x00, 0x80, 0x00));
        chart.Series.Add(series);

        var shape = new SlideShape
        {
            Id   = 9,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu  = 457200,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 3429000,
            Chart = chart
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var chartOp = ops.OfType<DrawOp.Chart>().Single();

        // SeriesColors must have one entry per data POINT (3), not per series (1).
        chartOp.SeriesColors.Should().HaveCount(3,
            "doughnut chart must expand one color per point, not per series (BV1 fix)");

        // Slice 1 had an explicit green point color — verify it was resolved.
        chartOp.SeriesColors[1].G.Should().Be(0x80,
            "explicit PointColors[1] (green) must be resolved into SeriesColors[1]");
    }

    [Fact]
    public void BV1_DoughnutChart_FillPlans_PreservePerPointFallbackColors()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var chart = new ChartShape { ChartType = ChartType.Doughnut };
        chart.Categories.AddRange(new[] { "Slice A", "Slice B" });
        var series = new ChartSeries { Name = "Doughnut" };
        series.Values.AddRange(new double?[] { 40.0, 60.0 });
        chart.Series.Add(series);
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 10,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 3429000,
            Chart = chart
        });

        var chartOp = SlideCompositor.Compose(p, FirstSlide(p)).OfType<DrawOp.Chart>().Single();
        var slices = ChartRenderPlanner.BuildDoughnutSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            chartOp.SeriesColors,
            chartOp.FillPlans);

        slices.Should().HaveCount(2);
        slices[0].Fill!.Value.Color.Should().Be(chartOp.SeriesColors[0]);
        slices[1].Fill!.Value.Color.Should().Be(chartOp.SeriesColors[1],
            "fill plans must keep doughnut's per-point accent cycle rather than collapsing to the first slice");
    }

    [Fact]
    public void Compose_ChartGradientFill_BuildsResolvedFillPlan()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });
        var series = new ChartSeries
        {
            Name = "Sales",
            Fill = new ShapeFill.Gradient(
                new[]
                {
                    new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0x10, 0x20, 0x30))),
                    new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0xD0, 0xE0, 0xF0)))
                },
                GradientKind.Linear,
                angleDegrees: 45)
        };
        series.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(series);
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 11,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 3429000,
            Chart = chart
        });

        var chartOp = SlideCompositor.Compose(p, FirstSlide(p)).OfType<DrawOp.Chart>().Single();

        chartOp.FillPlans.SeriesFills.Should().HaveCount(1);
        chartOp.FillPlans.SeriesFills[0].Fill.Should().BeOfType<ResolvedFill.Gradient>();
        var primitive = ChartRenderPlanner.BuildColumnPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            chartOp.SeriesColors,
            chartOp.FillPlans).First();
        primitive.Fill.Fill.Should().BeOfType<ResolvedFill.Gradient>();
    }

    [Fact]
    public void Compose_ChartPatternFills_BuildsResolvedFillPlansForSeriesPointAndMarker()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var chart = new ChartShape { ChartType = ChartType.LineMarkers };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });
        var series = new ChartSeries
        {
            Name = "Sales",
            Fill = new ShapeFill.Pattern(
                "diagStripe",
                new ThemeAwareColor(new SrgbColor(0x10, 0x20, 0x30)),
                new ThemeAwareColor(new SrgbColor(0xF0, 0xF1, 0xF2))),
            MarkerStyle = new ChartMarkerStyle
            {
                Symbol = ChartMarkerSymbol.Circle,
                Fill = new ShapeFill.Pattern(
                    "pct50",
                    new ThemeAwareColor(new SrgbColor(0x44, 0x55, 0x66)),
                    new ThemeAwareColor(new SrgbColor(0xAA, 0xBB, 0xCC)))
            }
        };
        series.Values.AddRange(new double?[] { 10, 20 });
        series.PointStyles[1] = new ChartPointStyle
        {
            Fill = new ShapeFill.Pattern(
                "cross",
                new ThemeAwareColor(new SrgbColor(0x20, 0x40, 0x60)),
                new ThemeAwareColor(new SrgbColor(0xE0, 0xD0, 0xC0)))
        };
        chart.Series.Add(series);
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 11,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 3429000,
            Chart = chart
        });

        var chartOp = SlideCompositor.Compose(p, FirstSlide(p)).OfType<DrawOp.Chart>().Single();

        var seriesPattern = chartOp.FillPlans.SeriesFills[0].Fill.Should().BeOfType<ResolvedFill.PatternFill>().Subject;
        seriesPattern.Preset.Should().Be("diagStripe");
        seriesPattern.ForegroundColor.Should().Be(new SrgbColor(0x10, 0x20, 0x30));
        seriesPattern.BackgroundColor.Should().Be(new SrgbColor(0xF0, 0xF1, 0xF2));

        var pointPattern = chartOp.FillPlans.PointFills[new ChartFillKey(0, 1)].Fill
            .Should()
            .BeOfType<ResolvedFill.PatternFill>()
            .Subject;
        pointPattern.Preset.Should().Be("cross");

        var markerPattern = chartOp.FillPlans.MarkerFills[new ChartFillKey(0, 1)].Fill
            .Should()
            .BeOfType<ResolvedFill.PatternFill>()
            .Subject;
        markerPattern.Preset.Should().Be("pct50");

        var primitive = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            withMarkers: true,
            chartOp.SeriesColors,
            chartOp.FillPlans).Single();
        primitive.Markers[1].Fill!.Value.Fill.Should().BeOfType<ResolvedFill.PatternFill>();
    }

    [Fact]
    public void Compose_TextPlaceholder_InheritsLayoutPlaceholderTextInsets()
    {
        var p = new PresentationModel { Theme = PresentationTheme.CreateDefault() };
        var master = new SlideMaster { Id = "m1" };
        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutTextBody = new TextBody
        {
            InsetLeftPt = 21,
            InsetTopPt = 6,
            InsetRightPt = 9,
            InsetBottomPt = 12
        };
        layout.Placeholders.Add(new SlideShape
        {
            Id = 10,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            TextBody = layoutTextBody
        });
        p.Masters.Add(master);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        slide.Shapes.Add(CreateTextPlaceholder(1, PlaceholderType.Body, 1, "Inherited layout insets"));
        p.Slides.Add(slide);

        var text = SlideCompositor.Compose(p, slide).OfType<DrawOp.Shape>().Single().Text!;

        text.InsetLeftDip.Should().BeApproximately(28.0, 0.001);
        text.InsetTopDip.Should().BeApproximately(8.0, 0.001);
        text.InsetRightDip.Should().BeApproximately(12.0, 0.001);
        text.InsetBottomDip.Should().BeApproximately(16.0, 0.001);
    }

    [Fact]
    public void Compose_TextPlaceholder_UsesMasterTextInsetsWhenLayoutDoesNotOverride()
    {
        var p = new PresentationModel { Theme = PresentationTheme.CreateDefault() };
        var master = new SlideMaster { Id = "m1" };
        master.Placeholders.Add(new SlideShape
        {
            Id = 20,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            TextBody = new TextBody
            {
                InsetLeftPt = 18,
                InsetTopPt = 3,
                InsetRightPt = 15,
                InsetBottomPt = 6
            }
        });
        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Masters.Add(master);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        slide.Shapes.Add(CreateTextPlaceholder(1, PlaceholderType.Title, 0, "Inherited master insets"));
        p.Slides.Add(slide);

        var text = SlideCompositor.Compose(p, slide).OfType<DrawOp.Shape>().Single().Text!;

        text.InsetLeftDip.Should().BeApproximately(24.0, 0.001);
        text.InsetTopDip.Should().BeApproximately(4.0, 0.001);
        text.InsetRightDip.Should().BeApproximately(20.0, 0.001);
        text.InsetBottomDip.Should().BeApproximately(8.0, 0.001);
    }

    /// <summary>
    /// master-layout-inheritance F1: a slide placeholder that omits Fill/Outline (the normal
    /// "inherit from layout" authoring pattern -- PowerPoint itself omits spPr fill/line on slide
    /// placeholders that inherit) must pick up the matching LAYOUT placeholder's fill and outline,
    /// not render as a transparent, borderless box.
    /// </summary>
    [Fact]
    public void Compose_TitlePlaceholder_InheritsLayoutPlaceholderFillAndOutline()
    {
        var p = new PresentationModel { Theme = PresentationTheme.CreateDefault() };
        var master = new SlideMaster { Id = "m1" };
        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutBlue = new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0xCC));
        var layoutRed = new ThemeAwareColor(new SrgbColor(0xCC, 0x11, 0x22));
        layout.Placeholders.Add(new SlideShape
        {
            Id = 10,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            Fill = new ShapeFill.Solid(layoutBlue),
            Outline = new ShapeOutline.Visible(layoutRed, widthPt: 3.0, dash: OutlineDash.Solid)
        });
        p.Masters.Add(master);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        // Slide's own placeholder omits Fill/Outline entirely -- the normal "inherit" state.
        slide.Shapes.Add(CreateTextPlaceholder(1, PlaceholderType.Title, 0, "Inherited fill/outline"));
        p.Slides.Add(slide);

        var shapeOp = SlideCompositor.Compose(p, slide).OfType<DrawOp.Shape>().Single();

        var fill = shapeOp.Fill.Should().BeOfType<ResolvedFill.Solid>(
            "the layout placeholder's blue fill should be inherited").Subject;
        fill.Color.Should().Be(new SrgbColor(0x11, 0x22, 0xCC));

        var outline = shapeOp.Outline.Should().BeOfType<ResolvedOutline.Visible>(
            "the layout placeholder's red outline should be inherited").Subject;
        outline.Color.Should().Be(new SrgbColor(0xCC, 0x11, 0x22));
        outline.WidthDip.Should().BeApproximately(4.0, 0.05); // 3pt -> DIP = 3 * 96/72 = 4.0
    }

    /// <summary>
    /// Sibling no-regression case: when the SLIDE'S OWN placeholder shape does specify its own
    /// Fill/Outline, that explicit authoring must still win over the layout placeholder's -- the
    /// inheritance fallback must never override an explicit slide-level value.
    /// </summary>
    [Fact]
    public void Compose_TitlePlaceholder_OwnFillAndOutlineOverrideLayoutPlaceholder_Regression()
    {
        var p = new PresentationModel { Theme = PresentationTheme.CreateDefault() };
        var master = new SlideMaster { Id = "m1" };
        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        layout.Placeholders.Add(new SlideShape
        {
            Id = 10,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0xCC))),
            Outline = new ShapeOutline.Visible(
                new ThemeAwareColor(new SrgbColor(0xCC, 0x11, 0x22)), widthPt: 3.0, dash: OutlineDash.Solid)
        });
        p.Masters.Add(master);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var ownShape = CreateTextPlaceholder(1, PlaceholderType.Title, 0, "Own fill/outline");
        ownShape.Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x00, 0xAA, 0x00)));
        ownShape.Outline = new ShapeOutline.Visible(
            new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0x00)), widthPt: 1.0, dash: OutlineDash.Solid);
        slide.Shapes.Add(ownShape);
        p.Slides.Add(slide);

        var shapeOp = SlideCompositor.Compose(p, slide).OfType<DrawOp.Shape>().Single();

        var fill = shapeOp.Fill.Should().BeOfType<ResolvedFill.Solid>().Subject;
        fill.Color.Should().Be(new SrgbColor(0x00, 0xAA, 0x00), "the slide's own explicit fill must win over the layout's");

        var outline = shapeOp.Outline.Should().BeOfType<ResolvedOutline.Visible>().Subject;
        outline.Color.Should().Be(new SrgbColor(0x00, 0x00, 0x00), "the slide's own explicit outline must win over the layout's");
    }

    /// <summary>
    /// BV1 regression: pie chart still gets per-point colors (not regressed by the fix).
    /// </summary>
    [Fact]
    public void BV1_PieChart_SeriesColors_OnePerPoint_Regression()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var chart = new ChartShape { ChartType = ChartType.Pie };
        chart.Categories.AddRange(new[] { "A", "B" });

        var series = new ChartSeries { Name = "Pie" };
        series.Values.AddRange(new double?[] { 60.0, 40.0 });
        chart.Series.Add(series);

        var shape = new SlideShape
        {
            Id   = 10,
            Kind = SlideShapeKind.Chart,
            OffsetXEmu  = 0, OffsetYEmu  = 0,
            ExtentCxEmu = 4572000, ExtentCyEmu = 3429000,
            Chart = chart
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var chartOp = ops.OfType<DrawOp.Chart>().Single();

        // Still two colors — one per slice (regression guard).
        chartOp.SeriesColors.Should().HaveCount(2,
            "pie chart must still expand one color per point (regression guard)");
    }

    [Fact]
    public void Compose_IncludesMasterAndLayoutDecorationButNotPlaceholderDefinitions()
    {
        var presentation = PresentationModel.CreateEmpty();
        var master = new SlideMaster { Id = "m1" };
        master.Placeholders.Add(new SlideShape
        {
            Id = 10,
            Name = "Master logo",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 100,
            OffsetYEmu = 200,
            ExtentCxEmu = 1000,
            ExtentCyEmu = 600,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33))),
        });
        master.Placeholders.Add(new SlideShape
        {
            Id = 11,
            Name = "Master title placeholder",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Placeholder = new Placeholder { Type = PlaceholderType.Title },
        });

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        layout.Placeholders.Add(new SlideShape
        {
            Id = 12,
            Name = "Layout footer decoration",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 120,
            OffsetYEmu = 800,
            ExtentCxEmu = 900,
            ExtentCyEmu = 300,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x44, 0x55, 0x66))),
        });

        presentation.Masters.Add(master);
        presentation.Layouts.Add(layout);
        var slide = new Slide { Id = "s1", LayoutId = "l1" };
        presentation.Slides.Add(slide);

        var shapes = SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .ToArray();

        shapes.Should().HaveCount(2);
        shapes.Select(shape => shape.ShapeId).Should().Equal(10u, 12u);
    }

    [Fact]
    public void Compose_HidesMasterDecorationWhenShowMasterShapesIsFalse()
    {
        var presentation = PresentationModel.CreateEmpty();
        presentation.ShowMasterShapes = false;
        var master = new SlideMaster { Id = "m1" };
        master.Placeholders.Add(new SlideShape
        {
            Id = 10,
            Name = "Master logo",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 100,
            OffsetYEmu = 200,
            ExtentCxEmu = 1000,
            ExtentCyEmu = 600,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33))),
        });
        presentation.Masters.Add(master);
        var slide = new Slide { Id = "s1" };
        presentation.Slides.Add(slide);

        SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .Should().BeEmpty();
    }

    private static SlideShape CreateTextPlaceholder(
        uint id,
        PlaceholderType type,
        int idx,
        string text)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });

        return new SlideShape
        {
            Id = id,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Placeholder = new Placeholder { Type = type, Idx = idx },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
            TextBody = new TextBody
            {
                Paragraphs = { paragraph }
            }
        };
    }

    // ─── Round 156 (shared-theme-fonts F3): East-Asian / complex-script run fonts ─────────────

    [Fact]
    public void Compose_RunWithEastAsianText_UsesRunsEastAsiaFontFamily_NotLatinFont()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run
        {
            Text = "你好",       // "你好" -- needs the a:ea typeface, not a:latin
            FontFamily = "Arial",
            EastAsiaFontFamily = "SimSun",
        });
        shape.TextBody = new TextBody { Paragraphs = { paragraph } };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.FontFamily.Should().Be("SimSun",
            "a CJK run tagged with a distinct a:ea typeface must render with that typeface, not the Latin a:latin font");
    }

    [Fact]
    public void Compose_RunWithLatinTextAndEastAsiaOverride_StillUsesLatinFontFamily()
    {
        // Sibling/no-regression case: Office commonly writes an a:ea override on every run in a
        // CJK-themed deck, even runs whose text is pure Latin. The fix must key off the run's
        // actual text content, not merely the presence of an EastAsiaFontFamily value, or every
        // Latin run in such a deck would wrongly start rendering with the East-Asian typeface.
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run
        {
            Text = "Hello World",
            FontFamily = "Arial",
            EastAsiaFontFamily = "SimSun",
        });
        shape.TextBody = new TextBody { Paragraphs = { paragraph } };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.FontFamily.Should().Be("Arial",
            "plain Latin text must keep rendering with the Latin typeface even when the run also carries an unused a:ea override");
    }

    [Fact]
    public void Compose_RunWithEastAsianText_AndNoExplicitEaFont_UsesThemeMinorEastAsiaToken()
    {
        // Covers the "very common theme-token form" from the finding: nothing on the run
        // overrides a:ea at all, so it must resolve all the way up to the theme's own minor
        // East-Asian font (the render-time equivalent of an implicit "+mn-ea").
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Theme.NativeFontSchemeXml =
            "<a:fontScheme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office\">" +
            "<a:majorFont><a:latin typeface=\"Calibri Light\"/><a:ea typeface=\"YuGothic\"/><a:cs typeface=\"\"/></a:majorFont>" +
            "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"MS Gothic\"/><a:cs typeface=\"\"/></a:minorFont>" +
            "</a:fontScheme>";

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "こんにちは" }); // こんにちは, no font at all
        shape.TextBody = new TextBody { Paragraphs = { paragraph } };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var run = ops.OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0].Runs[0];

        run.FontFamily.Should().Be("MS Gothic",
            "an unset a:ea ultimately resolves to the theme's minor East-Asian font, not the Latin fallback font");
    }

    // ─── Round 156 (freep-master-inheritance F2): shape-effects placeholder inheritance ───────

    [Fact]
    public void Compose_PlaceholderWithNoOwnEffects_InheritsShadowFromLayoutPlaceholder()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000,
            Effects = new ShapeEffects
            {
                HasOuterShadow = true,
                OuterShadowColor = new SrgbColor(0x40, 0x40, 0x40),
                OuterShadowAlpha = 128,
                OuterShadowBlurRadEmu = 50800,
                OuterShadowDistEmu = 38100,
                OuterShadowDirDeg = 45,
            },
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            // No Effects of its own -- the normal "inherit from layout" authoring pattern.
        };
        titleShape.Text = "Test Title";
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Effects.Should().NotBeNull("a slide placeholder should inherit its layout placeholder's shadow");
        shapeOp.Effects!.HasOuterShadow.Should().BeTrue();
        shapeOp.Effects.OuterShadowColor.Should().Be(new SrgbColor(0x40, 0x40, 0x40));
        shapeOp.Effects.OuterShadowAlpha.Should().Be(128);
    }

    [Fact]
    public void Compose_PlaceholderWithOwnEffects_KeepsOwnEffects_NotLayouts()
    {
        // Sibling/no-regression case: a placeholder that DOES author its own effects must keep
        // using them, never picking up the layout placeholder's on top of or instead of its own.
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000,
            Effects = new ShapeEffects
            {
                HasGlow = true,
                GlowColor = new SrgbColor(0x00, 0xAA, 0xFF),
                GlowRadiusEmu = 152400,
            },
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            Effects = new ShapeEffects
            {
                HasSoftEdge = true,
                SoftEdgeRadEmu = 158750,
            },
        };
        titleShape.Text = "Test Title";
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Effects.Should().NotBeNull();
        shapeOp.Effects!.HasSoftEdge.Should().BeTrue("the shape's own effects take priority over the layout placeholder's");
        shapeOp.Effects.HasGlow.Should().BeFalse("the layout's glow must not leak in when the shape authored its own effects");
    }
}
