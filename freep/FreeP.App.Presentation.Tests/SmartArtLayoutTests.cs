using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Theme 17: Unit tests for the SmartArt live layout engine and compositor integration.
/// </summary>
public sealed class SmartArtLayoutTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static PresentationTheme DefaultTheme() =>
        PresentationModel.CreateEmpty().Theme!;

    // EMU constants for a standard slide frame
    private const long FrameX  = 914_400L;
    private const long FrameY  = 457_200L;
    private const long FrameCx = 7_315_200L;
    private const long FrameCy = 3_657_600L;

    private static SmartArtData MakeData(SmartArtFamily family, params string[] nodeTexts)
    {
        var data = new SmartArtData { Family = family };
        foreach (var text in nodeTexts)
        {
            data.Nodes.Add(new SmartArtNode { Text = text, Level = 0 });
        }
        return data;
    }

    private static SmartArtData MakeChevronData(string layout, params string[] nodeTexts)
    {
        var data = MakeData(SmartArtFamily.Process, nodeTexts);
        data.LayoutUniqueId = $"urn:microsoft.com/office/officeart/2005/8/layout/{layout}";
        return data;
    }

    private static SmartArtData MakeHierarchyData(string rootText, params string[] childTexts)
    {
        var root = new SmartArtNode { Text = rootText, Level = 0 };
        foreach (var t in childTexts)
            root.Children.Add(new SmartArtNode { Text = t, Level = 1 });

        var data = new SmartArtData { Family = SmartArtFamily.Hierarchy };
        data.Nodes.Add(root);
        return data;
    }

    private static byte[] Minimal1x1Png() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x60, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC, 0x33, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static SrgbColor SolidFill(SlideShape shape) =>
        ((ShapeFill.Solid)shape.Fill!).Color.Resolved;

    private static SrgbColor SolidDrawFill(DrawOp op) =>
        ((ResolvedFill.Solid)((DrawOp.Shape)op).Fill).Color;

    [Fact]
    public void WhiteOutlineQuickStyle_UsesWhiteNodeAndConnectorOutlines()
    {
        var plan = SmartArtStylePlanner.Build(
            SmartArtFamily.List,
            new SmartArtQuickStyleMetadata
            {
                UniqueId = "simple2",
                Title = "White Outline",
                Category = "Simple"
            },
            colors: null,
            DefaultTheme());

        plan.GetNodeStyle(0, 0, SmartArtFamily.List).Outline.Resolved.Should().Be(SrgbColor.White);
        plan.Connector.Outline.Resolved.Should().Be(SrgbColor.White);
        plan.Connector.WidthPt.Should().Be(1.25);
    }

    [Fact]
    public void NativeSimpleQuickStylesUseDistinctLiveProfiles()
    {
        var baseColor = SrgbColor.FromRgb(0x4472C4);

        SmartArtStylePlan Build(string id, string title) => SmartArtStylePlanner.Build(
            SmartArtFamily.List,
            new SmartArtQuickStyleMetadata { UniqueId = id, Title = title },
            colors: new SmartArtColorMetadata
            {
                Palette = { new ThemeAwareColor(baseColor) }
            },
            DefaultTheme());

        var simple = Build("simple1", "Simple Fill");
        var subtle = Build("simple3", "Subtle Effect");
        var moderate = Build("simple4", "Moderate Effect");
        var intense = Build("simple5", "Intense Effect");

        simple.GetNodeStyle(0, 0, SmartArtFamily.List).Fill.Resolved.Should().Be(baseColor);
        subtle.GetNodeStyle(0, 0, SmartArtFamily.List).Fill.Resolved
            .Should().Be(ThemeColorTransform.ApplyTint(baseColor, 0.32));
        moderate.GetNodeStyle(0, 0, SmartArtFamily.List).Fill.Resolved
            .Should().Be(ThemeColorTransform.ApplyTint(baseColor, 0.88));
        intense.GetNodeStyle(0, 0, SmartArtFamily.List).Fill.Resolved
            .Should().Be(ThemeColorTransform.ApplyShade(baseColor, 0.72));

        simple.GetNodeStyle(0, 0, SmartArtFamily.List).OutlineWidthPt.Should().Be(1.0);
        subtle.GetNodeStyle(0, 0, SmartArtFamily.List).OutlineWidthPt.Should().Be(0.85);
        moderate.GetNodeStyle(0, 0, SmartArtFamily.List).OutlineWidthPt.Should().Be(1.1);
        intense.GetNodeStyle(0, 0, SmartArtFamily.List).OutlineWidthPt.Should().Be(1.4);
    }

    [Fact]
    public void NativeSceneQuickStylesUseDistinctLiveProfiles()
    {
        var baseColor = SrgbColor.FromRgb(0x4472C4);
        var ids = Enumerable.Range(1, 9).Select(index => $"3d{index}").ToArray();

        var signatures = ids.Select(id =>
        {
            var plan = SmartArtStylePlanner.Build(
                SmartArtFamily.List,
                new SmartArtQuickStyleMetadata { UniqueId = id },
                new SmartArtColorMetadata { Palette = { new ThemeAwareColor(baseColor) } },
                DefaultTheme());
            var node = plan.GetNodeStyle(0, 0, SmartArtFamily.List);
            return new
            {
                Fill = node.Fill.Resolved,
                Outline = node.Outline.Resolved,
                node.OutlineWidthPt,
                Connector = plan.Connector.Outline.Resolved,
                ConnectorWidth = plan.Connector.WidthPt
            };
        }).ToArray();

        signatures.Distinct().Should().HaveCount(9,
            "each native 3D scene quick style must produce its own live style profile");
    }

    // ── Family classification tests ───────────────────────────────────────────────

    [Theory]
    [InlineData(SmartArtFamily.Process)]
    [InlineData(SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtFamily.Cycle)]
    [InlineData(SmartArtFamily.List)]
    public void LayoutEngine_SupportedFamily_ReturnsNonNull(SmartArtFamily family)
    {
        var data = new SmartArtData { Family = family };
        data.Nodes.Add(new SmartArtNode { Text = "Node", Level = 0 });

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());
        result.Should().NotBeNull($"supported family {family} must produce live shapes");
    }

    // ── Process layout ────────────────────────────────────────────────────────────

    [Fact]
    public void Process_ThreeNodes_ProducesThreeBoxesPlusTwoConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "Step A", "Step B", "Step C");
        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        // 3 boxes + 2 connectors = 5 shapes
        shapes!.Count.Should().Be(5);

        var boxes      = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        var connectors = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line).ToList();

        boxes.Should().HaveCount(3,      "one rounded-rect box per node");
        connectors.Should().HaveCount(2, "one connector between each adjacent pair");
    }

    [Fact]
    public void HorizontalBulletList_UsesLiveRowMajorGridAndPreservesNodeOrder()
    {
        var data = MakeData(SmartArtFamily.List, "One", "Two", "Three", "Four");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/horizontalBulletList";
        data.IsLiveLayoutSupported = true;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        var boxes = shapes!.Where(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Should().HaveCount(4);
        boxes.Select(shape => shape.PlainText).Should().Equal("One", "Two", "Three", "Four");
        boxes[0].OffsetXEmu.Should().BeLessThan(boxes[1].OffsetXEmu);
        boxes[2].OffsetYEmu.Should().BeGreaterThan(boxes[0].OffsetYEmu);
        boxes[3].OffsetYEmu.Should().BeGreaterThan(boxes[1].OffsetYEmu);
    }

    [Fact]
    public void HorizontalBlockList_UsesLiveSingleRowBlocksAndPreservesNodeOrder()
    {
        var data = MakeData(SmartArtFamily.List, "One", "Two", "Three", "Four");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/horizontalBlockList";
        data.IsLiveLayoutSupported = true;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        var boxes = shapes!.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Rectangle).ToList();
        boxes.Should().HaveCount(4);
        boxes.Select(shape => shape.PlainText).Should().Equal("One", "Two", "Three", "Four");
        boxes.Select(shape => shape.OffsetXEmu).Should().BeInAscendingOrder();
        boxes.Select(shape => shape.OffsetYEmu).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void VerticalBlockList_UsesLiveRectangularStackAndPreservesAuthoredIndent()
    {
        var data = MakeData(SmartArtFamily.List, "Overview", "Detail", "Next");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalBlockList";
        data.IsLiveLayoutSupported = true;
        data.Nodes[1].Level = 1;
        data.Nodes[2].Level = 0;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("verticalBlockList is a supported shared list layout");
        var blocks = shapes!.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Rectangle).ToList();
        blocks.Should().HaveCount(3, "one editable rectangular block per authored node");
        blocks.Select(shape => shape.PlainText).Should().Equal("Overview", "Detail", "Next");
        blocks.Select(shape => shape.OffsetYEmu).Should().BeInAscendingOrder();
        blocks[1].OffsetXEmu.Should().BeGreaterThan(blocks[0].OffsetXEmu,
            "nested authored list levels retain a bounded left inset");
        blocks[1].ExtentCxEmu.Should().BeLessThan(blocks[0].ExtentCxEmu,
            "nested blocks consume the same bounded inset from their available width");
    }

    [Fact]
    public void TrapezoidList_UsesLiveListGeometryAndPreservesNodeOrder()
    {
        var data = MakeData(SmartArtFamily.List, "One", "Two", "Three");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/trapezoidList";
        data.IsLiveLayoutSupported = true;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        var boxes = shapes!.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Trapezoid).ToList();
        boxes.Should().HaveCount(3);
        boxes.Select(shape => shape.PlainText).Should().Equal("One", "Two", "Three");
        boxes.Select(shape => shape.OffsetYEmu).Should().BeInAscendingOrder();
        boxes.Should().OnlyContain(shape =>
            shape.OffsetXEmu >= FrameX &&
            shape.OffsetYEmu >= FrameY &&
            shape.OffsetXEmu + shape.ExtentCxEmu <= FrameX + FrameCx &&
            shape.OffsetYEmu + shape.ExtentCyEmu <= FrameY + FrameCy);
        boxes.Should().OnlyContain(shape => shape.PresetGeometryAdjustments.ContainsKey("adj"));
        boxes.Select(shape => shape.PresetGeometryAdjustments["adj"])
            .Should().OnlyContain(adjustment => adjustment == 25000);
    }

    [Fact]
    public void Process_BoxesAreLeftToRight_Increasing_X()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C");
        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;

        var boxes = shapes
            .Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .OrderBy(s => s.OffsetXEmu)
            .ToList();

        for (int i = 1; i < boxes.Count; i++)
            boxes[i].OffsetXEmu.Should().BeGreaterThan(boxes[i - 1].OffsetXEmu,
                "process boxes must be ordered left-to-right");
    }

    [Fact]
    public void Process_BoxesHaveCorrectText()
    {
        var data = MakeData(SmartArtFamily.Process, "Alpha", "Beta", "Gamma");
        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;

        var boxes = shapes
            .Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .ToList();

        var texts = boxes.Select(b => b.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text ?? "");
        texts.Should().BeEquivalentTo(new[] { "Alpha", "Beta", "Gamma" });
    }

    [Fact]
    public void Process_ParOfChain_RendersAllNodesInConnectionOrder()
    {
        var root = new SmartArtNode { ModelId = "n1", Text = "Plan", Level = 0 };
        var design = new SmartArtNode { ModelId = "n2", Text = "Design", Level = 1 };
        var build = new SmartArtNode { ModelId = "n3", Text = "Build", Level = 2 };
        var test = new SmartArtNode { ModelId = "n4", Text = "Test", Level = 3 };
        var deploy = new SmartArtNode { ModelId = "n5", Text = "Deploy", Level = 4 };
        root.Children.Add(design);
        design.Children.Add(build);
        build.Children.Add(test);
        test.Children.Add(deploy);

        var data = new SmartArtData
        {
            Family = SmartArtFamily.Process,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/process1"
        };
        data.Nodes.Add(root);

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;

        var boxes = shapes
            .Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .OrderBy(s => s.OffsetXEmu)
            .ToList();
        var texts = boxes.Select(b => b.TextBody?.Paragraphs.First().Runs.First().Text).ToList();

        boxes.Should().HaveCount(5, "process parOf chains from live SmartArt data represent visible ordered steps");
        texts.Should().Equal("Plan", "Design", "Build", "Test", "Deploy");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(4, "five ordered process nodes need one connector between each pair");
    }

    [Fact]
    public void LayoutEngine_UsesSmartArtColorMetadataPaletteForLiveBoxes()
    {
        var data = MakeData(SmartArtFamily.Process, "Alpha", "Beta");
        var colors = new SmartArtColorMetadata
        {
            UniqueId = "urn:smartart:colors:colorful-accent",
            Title = "Colorful Accent"
        };
        colors.Palette.Add(new ThemeAwareColor(SrgbColor.FromRgb(0x990000)));
        colors.Palette.Add(new ThemeAwareColor(SrgbColor.FromRgb(0x009900)));

        var quickStyle = new SmartArtQuickStyleMetadata
        {
            UniqueId = "urn:smartart:style:moderate-effect",
            Title = "Moderate Effect"
        };

        var shapes = SmartArtLayoutEngine.Layout(
            data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme(),
            quickStyle: quickStyle,
            colors: colors)!;

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Should().HaveCount(2);
        SolidFill(boxes[0]).Should().Be(ThemeColorTransform.ApplyTint(SrgbColor.FromRgb(0x990000), 0.88));
        SolidFill(boxes[1]).Should().Be(ThemeColorTransform.ApplyTint(SrgbColor.FromRgb(0x009900), 0.88));
    }

    [Fact]
    public void Process_BoxesAreWithinFrame()
    {
        var data = MakeData(SmartArtFamily.Process, "X", "Y", "Z");
        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;

        foreach (var shape in shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle))
        {
            shape.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX, "box left edge must be inside frame");
            shape.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY, "box top edge must be inside frame");
            (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx, "box right edge must be inside frame");
            (shape.OffsetYEmu + shape.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy, "box bottom edge must be inside frame");
        }
    }

    // ── List layout ───────────────────────────────────────────────────────────────

    [Fact]
    public void List_FourNodes_ProducesFourBoxes_NoConnectors()
    {
        var data = MakeData(SmartArtFamily.List, "Item 1", "Item 2", "Item 3", "Item 4");
        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Should().HaveCount(4, "list layout: one box per node, no connectors");

        shapes.Should().OnlyContain(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle,
            "list nodes should be rounded-rect boxes");
    }

    [Fact]
    public void List_BoxesAreVerticallyStacked_IncreasingY()
    {
        var data = MakeData(SmartArtFamily.List, "A", "B", "C");
        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;

        var ordered = shapes.OrderBy(s => s.OffsetYEmu).ToList();
        for (int i = 1; i < ordered.Count; i++)
            ordered[i].OffsetYEmu.Should().BeGreaterThan(ordered[i - 1].OffsetYEmu,
                "list boxes should stack top-to-bottom");
    }

    // ── Cycle layout ─────────────────────────────────────────────────────────────

    [Fact]
    public void PictureCaptionList_WithNodePictures_ProducesPicturesAndCaptions()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha", "Beta");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList";
        data.IsLiveLayoutSupported = true;
        foreach (var node in data.Nodes)
            node.Picture = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("pictureCaptionList keeps a live layout while picture nodes are present");
        shapes!.Should().HaveCount(4, "each node emits one shared picture shape and one shared caption shape");
        shapes.Where(s => s.Kind == SlideShapeKind.Picture).Should().HaveCount(2);
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public void PictureCaptionList_MissingNodePicture_EmitsAddPicturePlaceholder()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList";
        data.IsLiveLayoutSupported = true;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("an authored new node can temporarily have no picture payload");
        shapes!.Where(s => s.Kind == SlideShapeKind.Picture).Should().BeEmpty();
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().Contain("Add picture");
    }

    [Fact]
    public void PictureGrid_WithNodePictures_UsesTwoColumnsAndCaptions()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha", "Beta", "Gamma");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureGrid";
        data.IsLiveLayoutSupported = true;
        foreach (var node in data.Nodes)
            node.Picture = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Where(s => s.Kind == SlideShapeKind.Picture).Should().HaveCount(3);
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().Equal("Alpha", "Beta", "Gamma");
        var pictures = shapes.Where(s => s.Kind == SlideShapeKind.Picture).OrderBy(s => s.OffsetYEmu).ThenBy(s => s.OffsetXEmu).ToArray();
        pictures[1].OffsetXEmu.Should().BeGreaterThan(pictures[0].OffsetXEmu);
        pictures[2].OffsetYEmu.Should().BeGreaterThan(pictures[0].OffsetYEmu);
    }

    [Fact]
    public void PictureGrid_MissingNodePicture_EmitsAddPicturePlaceholder()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureGrid";
        data.IsLiveLayoutSupported = true;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Where(s => s.Kind == SlideShapeKind.Picture).Should().BeEmpty();
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().Contain("Add picture");
    }

    [Fact]
    public void PictureAccentList_WithNodePictures_UsesAccentBarsAndCaptions()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha", "Beta");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureAccentList";
        data.IsLiveLayoutSupported = true;
        foreach (var node in data.Nodes)
            node.Picture = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Where(s => s.Kind == SlideShapeKind.Picture).Should().HaveCount(2);
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape && s.Fill is ShapeFill.Solid)
            .Should().HaveCount(2, "each row has one accent bar");
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().Contain("Alpha").And.Contain("Beta");
    }

    [Fact]
    public void PictureAccentList_MissingNodePicture_EmitsAddPicturePlaceholder()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureAccentList";
        data.IsLiveLayoutSupported = true;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Where(s => s.Kind == SlideShapeKind.Picture).Should().BeEmpty();
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().Contain("Add picture");
    }

    [Fact]
    public void PictureStack_WithNodePictures_UsesStackedPicturesAndCaptions()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha", "Beta", "Gamma");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureStack";
        data.IsLiveLayoutSupported = true;
        foreach (var node in data.Nodes)
            node.Picture = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Where(s => s.Kind == SlideShapeKind.Picture).Should().HaveCount(3);
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().ContainInOrder("Alpha", "Beta", "Gamma");
        var pictures = shapes.Where(s => s.Kind == SlideShapeKind.Picture).OrderBy(s => s.OffsetYEmu).ToArray();
        pictures[1].OffsetYEmu.Should().BeGreaterThan(pictures[0].OffsetYEmu);
        pictures[2].OffsetYEmu.Should().BeGreaterThan(pictures[1].OffsetYEmu);
    }

    [Fact]
    public void PictureStack_MissingNodePicture_EmitsAddPicturePlaceholder()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureStack";
        data.IsLiveLayoutSupported = true;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Where(s => s.Kind == SlideShapeKind.Picture).Should().BeEmpty();
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().Contain("Add picture");
    }

    [Fact]
    public void PictureLineup_WithNodePictures_UsesHorizontalPicturesAndCaptions()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha", "Beta", "Gamma");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureLineup";
        data.IsLiveLayoutSupported = true;
        foreach (var node in data.Nodes)
            node.Picture = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Where(s => s.Kind == SlideShapeKind.Picture).Should().HaveCount(3);
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().ContainInOrder("Alpha", "Beta", "Gamma");
        var pictures = shapes.Where(s => s.Kind == SlideShapeKind.Picture).OrderBy(s => s.OffsetXEmu).ToArray();
        pictures[1].OffsetXEmu.Should().BeGreaterThan(pictures[0].OffsetXEmu);
        pictures[2].OffsetXEmu.Should().BeGreaterThan(pictures[1].OffsetXEmu);
    }

    [Fact]
    public void PictureLineup_MissingNodePicture_EmitsAddPicturePlaceholder()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureLineup";
        data.IsLiveLayoutSupported = true;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Where(s => s.Kind == SlideShapeKind.Picture).Should().BeEmpty();
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().Contain("Add picture");
    }

    [Fact]
    public void PictureStrips_WithNodePictures_UsesLivePictureLineupGeometry()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha", "Beta", "Gamma");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureStrips";
        data.IsLiveLayoutSupported = true;
        foreach (var node in data.Nodes)
            node.Picture = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Where(s => s.Kind == SlideShapeKind.Picture).Should().HaveCount(3);
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().ContainInOrder("Alpha", "Beta", "Gamma");
    }

    [Fact]
    public void ContinuousPictureList_WithNodePictures_UsesHorizontalPicturesAndCaptions()
    {
        var data = MakeData(SmartArtFamily.List, "Alpha", "Beta", "Gamma");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/continuousPictureList";
        data.IsLiveLayoutSupported = true;
        foreach (var node in data.Nodes)
            node.Picture = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Where(s => s.Kind == SlideShapeKind.Picture).Should().HaveCount(3);
        shapes.Where(s => s.Kind == SlideShapeKind.AutoShape)
            .Select(s => s.PlainText)
            .Should().ContainInOrder("Alpha", "Beta", "Gamma");
    }

    [Fact]
    public void PictureAccentProcess_WithAndWithoutPictures_UsesSharedRailAndAccentBlocks()
    {
        var data = MakeData(SmartArtFamily.Process, "Plan", "Build", "Share");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureAccentProcess";
        data.Nodes[0].Picture = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();
        shapes!.Where(shape => shape.Kind == SlideShapeKind.Picture).Should().ContainSingle();
        shapes.Where(shape => shape.Name.StartsWith("SmartArt_PicturePlaceholder_", StringComparison.Ordinal))
            .Should().HaveCount(2, "missing node media remains an editable Add picture slot");
        shapes.Where(shape => shape.Kind == SlideShapeKind.AutoShape
                              && shape.AutoShapeKind == DrawingShapeKind.Rectangle
                              && shape.Name.StartsWith("SmartArt_Box_", StringComparison.Ordinal))
            .Should().HaveCount(3, "each process node gets one shared accent block");
        shapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(2, "the process rail connects adjacent picture stages");
        shapes.Where(shape => shape.Kind == SlideShapeKind.AutoShape
                              && shape.AutoShapeKind == DrawingShapeKind.Rectangle
                              && shape.Name.StartsWith("SmartArt_Box_", StringComparison.Ordinal))
            .Select(shape => shape.PlainText)
            .Should().Equal("Plan", "Build", "Share");
    }

    [Fact]
    public void Cycle_FiveNodes_ProducesFiveBoxesPlusFiveConnectors()
    {
        var data = MakeData(SmartArtFamily.Cycle, "A", "B", "C", "D", "E");
        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();

        var boxes      = shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        var connectors = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line).ToList();

        boxes.Should().HaveCount(5,      "one box per cycle node");
        connectors.Should().HaveCount(5, "one connector per edge in the cycle (N nodes → N connectors)");
    }

    [Fact]
    public void Cycle_BoxesAreWithinFrame()
    {
        var data = MakeData(SmartArtFamily.Cycle, "N1", "N2", "N3", "N4");
        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;

        foreach (var s in shapes.Where(b => b.AutoShapeKind == DrawingShapeKind.RoundedRectangle))
        {
            s.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            s.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
        }
    }

    [Fact]
    public void BasicCycle_ReturnsLiveCircularBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Discover", "Plan", "Build", "Review");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicCycle";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("basicCycle is a bounded shared cycle-family layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live box should be emitted per cycle node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(4, "basicCycle should reuse the shared circular connector planner");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().BeEquivalentTo(new[] { "Discover", "Plan", "Build", "Review" });
    }

    [Fact]
    public void MultidirectionalCycle_UsesLiveCircularBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Discover", "Plan", "Build", "Review");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/multidirectionalCycle";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("multidirectionalCycle is admitted through the shared cycle-family planner");
        shapes!.Count(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).Should().Be(4);
        shapes.Count(s => s.AutoShapeKind == DrawingShapeKind.Line).Should().Be(4);
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().BeEquivalentTo(new[] { "Discover", "Plan", "Build", "Review" });
    }

    [Fact]
    public void Cycle2_UsesNativeEllipseRingAndTangentArrows()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Idea", "Plan", "Execute", "Review", "Improve");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/cycle2";
        data.IsLiveLayoutSupported = true;
        var theme = DefaultTheme();

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, theme);

        shapes.Should().NotBeNull("cycle2 is admitted through its bounded native geometry");
        shapes!.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Should().HaveCount(5, "cycle2 has one ellipse per editable node");
        shapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.RightArrow)
            .Should().HaveCount(5, "cycle2 has one tangent arrow between each pair of nodes");
        shapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Idea", "Plan", "Execute", "Review", "Improve");
        shapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Should().OnlyContain(shape =>
                shape.OffsetXEmu >= FrameX && shape.OffsetYEmu >= FrameY
                && shape.OffsetXEmu + shape.ExtentCxEmu <= FrameX + FrameCx
                && shape.OffsetYEmu + shape.ExtentCyEmu <= FrameY + FrameCy);
        var arrows = shapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.RightArrow).ToArray();
        arrows.Select(shape => shape.RotationDeg)
            .Should().OnlyContain(rotation => Math.Abs(rotation) > 0.1);
        arrows.Select(shape => shape.Fill)
            .Should().AllBeOfType<ShapeFill.Solid>();
        arrows.Select(shape => ((ShapeFill.Solid)shape.Fill!).Color.Resolved)
            .Should().OnlyContain(color => color == SmartArtStylePlanner.ResolveNeutralConnector(theme));
    }

    [Fact]
    public void Cycle2_RejectsMoreThanNativeChildLimitForCachedFallback()
    {
        var data = MakeData(
            SmartArtFamily.Cycle,
            Enumerable.Range(1, 8).Select(index => $"Item {index}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/cycle2";
        data.IsLiveLayoutSupported = true;

        SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())
            .Should().BeNull("cycle2's native definition caps the live geometry at seven nodes");
    }

    [Fact]
    public void ContinuousCycle_ReturnsLiveCircularBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Plan", "Build", "Review", "Launch");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/continuousCycle";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("continuousCycle is admitted through the shared cycle-family layout path");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live box should be emitted per cycle node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(4, "continuousCycle should reuse the shared circular connector planner");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().BeEquivalentTo(new[] { "Plan", "Build", "Review", "Launch" });
    }

    [Fact]
    public void RadialCycle_ReturnsLiveCircularBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Identify", "Analyze", "Act", "Review");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/radialCycle";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("radialCycle is a bounded shared cycle-family layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live box should be emitted per radial-cycle node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(4, "radialCycle should reuse the shared circular connector planner");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().BeEquivalentTo(new[] { "Identify", "Analyze", "Act", "Review" });
    }

    [Fact]
    public void RadialList_ReturnsLiveCircularBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Discover", "Plan", "Build", "Review");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/radialList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("radialList is a bounded shared cycle-family layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live box should be emitted per radial-list node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(4, "radialList should reuse the shared circular connector planner");

        var centerX = FrameX + FrameCx / 2;
        var centerY = FrameY + FrameCy / 2;
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().OnlyContain(connector =>
                connector.OffsetXEmu <= centerX && connector.OffsetXEmu + connector.ExtentCxEmu >= centerX &&
                connector.OffsetYEmu <= centerY && connector.OffsetYEmu + connector.ExtentCyEmu >= centerY,
                "radialList uses four spokes from an implicit center rather than a closed adjacent-item loop");
    }

    [Theory]
    [InlineData(9)]
    [InlineData(16)]
    public void RadialList_PreservesAllItemsBeyondOriginalEightItemCutoff(int itemCount)
    {
        var data = MakeData(
            SmartArtFamily.Cycle,
            Enumerable.Range(1, itemCount).Select(index => $"Item {index}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/radialList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("radialList should remain live for every parsed item");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(itemCount);
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(itemCount);
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, itemCount).Select(index => $"Item {index}"));
    }

    [Fact]
    public void BasicRadial_ReturnsHubAndSpokeLiveGeometry()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Core", "Branch A", "Branch B", "Branch C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/radial1";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("radial1 is the native PowerPoint Basic Radial layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Should().ContainSingle("the first node is the central radial topic");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "each remaining node is a spoke box");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(3, "each spoke is connected to the central topic");

        shapes.Single(s => s.AutoShapeKind == DrawingShapeKind.Ellipse)
            .TextBody!.Paragraphs.First().Runs.First().Text.Should().Be("Core");
    }

    [Fact]
    public void RadialCluster_ReturnsCentralAndSurroundingLiveGeometry()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Theme", "North", "East", "South");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2008/layout/RadialCluster";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("RadialCluster should remain live for editable central and Level 2 nodes");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Should().HaveCount(4);
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(3);
        shapes.Where(s => s.TextBody is not null)
            .Select(s => s.TextBody!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Theme", "North", "East", "South");
    }

    [Fact]
    public void GearCycle_ReturnsLiveCircularBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Initiate", "Coordinate", "Deliver", "Improve");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/gearCycle";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("gearCycle is admitted as a bounded shared cycle-family approximation");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live box should be emitted per gear-cycle node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(4, "gearCycle should reuse the shared circular connector planner");
        shapes.Should().OnlyContain(
            s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle || s.AutoShapeKind == DrawingShapeKind.Line,
            "the current shared planner is honest renderer-neutral cycle geometry, not true gear-tooth geometry");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().BeEquivalentTo(new[] { "Initiate", "Coordinate", "Deliver", "Improve" });
    }

    [Fact]
    public void TextCycle_ReturnsLiveCircularBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Plan", "Draft", "Review", "Publish");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/textCycle";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("textCycle is admitted as a bounded shared cycle-family layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live box should be emitted per text-cycle node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(4, "textCycle should reuse the shared circular connector planner");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().BeEquivalentTo(new[] { "Plan", "Draft", "Review", "Publish" });
    }

    [Fact]
    public void BlockCycle_ReturnsLiveCircularBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Sense", "Decide", "Act", "Learn");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/blockCycle";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("blockCycle is admitted as a bounded shared cycle-family layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live box should be emitted per block-cycle node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(4, "blockCycle should reuse the shared circular connector planner");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().BeEquivalentTo(new[] { "Sense", "Decide", "Act", "Learn" });
    }

    // ── Hierarchy layout ──────────────────────────────────────────────────────────

    [Fact]
    public void NonDirectionalCycle_ReturnsLiveCircularBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Observe", "Align", "Deliver", "Adapt");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/nonDirectionalCycle";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("nonDirectionalCycle is admitted as a bounded shared cycle-family approximation");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live box should be emitted per non-directional cycle node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(4, "nonDirectionalCycle should reuse the shared circular connector planner");
        shapes.Should().OnlyContain(
            s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle || s.AutoShapeKind == DrawingShapeKind.Line,
            "the shared planner remains honest renderer-neutral cycle geometry, not PowerPoint-specific non-directional artwork");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().BeEquivalentTo(new[] { "Observe", "Align", "Deliver", "Adapt" });
    }

    [Fact]
    public void Hierarchy_RootWithThreeChildren_ProducesRootAboveChildren()
    {
        var data = MakeHierarchyData("CEO", "VP Sales", "VP Eng", "VP Marketing");
        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull();

        var boxes = shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Should().HaveCount(4, "root + 3 children");

        // The root box is the topmost (smallest Y)
        var rootBox = boxes.OrderBy(b => b.OffsetYEmu).First();
        var rootText = rootBox.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text ?? "";
        rootText.Should().Be("CEO", "root node must be the topmost box");

        // All children must be below the root
        foreach (var child in boxes.Skip(1).OrderBy(b => b.OffsetYEmu).Skip(0))
        {
            // Children should have Y > root's Y
        }

        var nonRootBoxes = boxes.Where(b => b != rootBox).ToList();
        foreach (var childBox in nonRootBoxes)
        {
            childBox.OffsetYEmu.Should().BeGreaterThan(rootBox.OffsetYEmu,
                "children must be below the root");
        }
    }

    [Fact]
    public void Hierarchy_HasConnectors()
    {
        var data = MakeHierarchyData("Root", "Child1", "Child2");
        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;

        var connectors = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line).ToList();
        connectors.Should().HaveCountGreaterThan(0, "hierarchy must have connector lines");
    }

    [Fact]
    public void BasicHierarchy_ReturnsLiveTreeBoxesAndConnectors()
    {
        var data = MakeHierarchyData("CEO", "Sales", "Engineering");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicHierarchy";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("basicHierarchy reuses the bounded shared hierarchy tree planner");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "root plus two child boxes should be emitted from the hierarchy tree");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(2, "basicHierarchy should reuse shared parent-child connector geometry");
    }

    [Fact]
    public void Hierarchy3_ReturnsLiveTreeBoxesAndConnectors()
    {
        var data = MakeHierarchyData("CEO", "Sales", "Engineering");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("hierarchy3 authoring uses the shared hierarchy tree planner");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "hierarchy3 should emit the root and two child boxes");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(2, "hierarchy3 should emit one connector per parent-child relationship");

        var boxesByText = shapes
            .Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .ToDictionary(
                s => s.TextBody!.Paragraphs.First().Runs.First().Text,
                StringComparer.Ordinal);
        boxesByText["Sales"].OffsetXEmu.Should().BeGreaterThan(boxesByText["CEO"].OffsetXEmu,
            "hierarchy3's native hierChild/fromL semantics place children to the right of the root");
        boxesByText["Engineering"].OffsetXEmu.Should().Be(boxesByText["Sales"].OffsetXEmu,
            "hierarchy3 sibling branches share a depth column");
    }

    [Fact]
    public void OrgChart_ReturnsLiveTreeBoxesAndConnectors()
    {
        var data = MakeHierarchyData("CEO", "Sales", "Engineering");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/orgChart";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("orgChart is admitted as a bounded shared hierarchy-family approximation");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "root plus two report boxes should be emitted from the hierarchy tree");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(2, "orgChart should reuse shared parent-child connector geometry");
        shapes.Should().OnlyContain(
            s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle || s.AutoShapeKind == DrawingShapeKind.Line,
            "the shared planner emits renderer-neutral tree boxes and connectors, not org-chart-specific geometry");
    }

    [Fact]
    public void NameAndTitleOrgChart_ReturnsLiveTreeBoxesAndConnectors()
    {
        var data = MakeHierarchyData("CEO", "Sales", "Engineering");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/nameAndTitleOrgChart";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("nameAndTitleOrgChart is a supported organization-chart layout variant");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "the name-and-title variant reuses the shared organization-chart tree boxes");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(2, "the name-and-title variant preserves parent-child connectors");
        shapes.Should().OnlyContain(
            s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle || s.AutoShapeKind == DrawingShapeKind.Line);
    }

    [Fact]
    public void SmartArtLiveBoxesPreserveAuthoredNodeParagraphs()
    {
        var data = MakeHierarchyData("Jane Doe\nChief Executive Officer", "Sales", "Engineering");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/nameAndTitleOrgChart";

        var manager = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!
            .Single(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text == "Jane Doe");

        manager.TextBody!.Paragraphs.Select(paragraph => paragraph.Runs.Single().Text)
            .Should().Equal("Jane Doe", "Chief Executive Officer");
    }

    [Fact]
    public void OrgChart_AssistantNode_UsesSideSlotBeforeRegularReports()
    {
        var root = new SmartArtNode { Text = "CEO", Level = 0 };
        var assistant = new SmartArtNode { Text = "Assistant", Level = 1, IsAssistant = true };
        root.Children.Add(assistant);
        root.Children.Add(new SmartArtNode { Text = "Sales", Level = 1 });
        root.Children.Add(new SmartArtNode { Text = "Engineering", Level = 1 });

        var data = new SmartArtData
        {
            Family = SmartArtFamily.Hierarchy,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/orgChart"
        };
        data.Nodes.Add(root);

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("orgChart assistant nodes are a bounded shared geometry nuance");
        shapes!.Where(s => s.TextBody is not null)
            .Should().HaveCount(4, "manager, assistant, and two regular reports should all render live");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(3, "assistant and report relationships still use shared connector ops");

        var boxesByText = shapes
            .Where(s => s.TextBody is not null)
            .ToDictionary(
                s => s.TextBody!.Paragraphs.First().Runs.First().Text,
                StringComparer.Ordinal);

        var managerBox = boxesByText["CEO"];
        var assistantBox = boxesByText["Assistant"];
        var salesBox = boxesByText["Sales"];
        var engineeringBox = boxesByText["Engineering"];

        assistantBox.OffsetYEmu.Should().BeGreaterThan(managerBox.OffsetYEmu,
            "assistant boxes sit below the manager");
        salesBox.OffsetYEmu.Should().BeGreaterThan(assistantBox.OffsetYEmu,
            "regular reports move below the assistant band");
        engineeringBox.OffsetYEmu.Should().Be(salesBox.OffsetYEmu,
            "regular reports stay in the same report row");
        assistantBox.OffsetXEmu.Should().BeGreaterThan(managerBox.OffsetXEmu + managerBox.ExtentCxEmu / 2,
            "assistant placement uses the side slot rather than the ordinary report row");
        assistantBox.ExtentCxEmu.Should().BeLessThan(salesBox.ExtentCxEmu,
            "assistant boxes use a smaller bounded side-slot width");
    }

    [Fact]
    public void OrgChart_UsesDedicatedAssistantAwareBoxPlan()
    {
        var root = new SmartArtNode { Text = "CEO", Level = 0 };
        root.Children.Add(new SmartArtNode
        {
            Text = "Assistant",
            Level = 1,
            IsAssistant = true,
        });
        root.Children.Add(new SmartArtNode { Text = "Director", Level = 1 });

        var data = new SmartArtData
        {
            Family = SmartArtFamily.Hierarchy,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/orgChart",
        };
        data.Nodes.Add(root);

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("orgChart uses the dedicated bounded shared hierarchy plan");
        var boxes = shapes!.Where(s => s.TextBody is not null).ToList();
        boxes.Should().HaveCount(3, "the root, assistant, and regular report all render as live boxes");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(2, "each report relationship uses the shared connector plan");
        boxes.Should().OnlyContain(box => box.Name.StartsWith("SmartArt_OrgChartBox_", StringComparison.Ordinal));
        boxes.Single(box => box.TextBody!.Paragraphs[0].Runs[0].Text == "Assistant")
            .AutoShapeKind.Should().Be(DrawingShapeKind.Rectangle,
                "assistant nodes use the dedicated rectangular org-chart box plan");
        boxes.Single(box => box.TextBody!.Paragraphs[0].Runs[0].Text == "Director")
            .AutoShapeKind.Should().Be(DrawingShapeKind.RoundedRectangle);
    }

    [Fact]
    public void BasicHierarchy_AssistantTypedNode_RemainsInRegularChildRow()
    {
        var root = new SmartArtNode { Text = "CEO", Level = 0 };
        root.Children.Add(new SmartArtNode { Text = "Assistant", Level = 1, IsAssistant = true });
        root.Children.Add(new SmartArtNode { Text = "Sales", Level = 1 });

        var data = new SmartArtData
        {
            Family = SmartArtFamily.Hierarchy,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicHierarchy"
        };
        data.Nodes.Add(root);

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;

        var boxesByText = shapes
            .Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .ToDictionary(
                s => s.TextBody!.Paragraphs.First().Runs.First().Text,
                StringComparer.Ordinal);

        boxesByText["Assistant"].OffsetYEmu.Should().Be(boxesByText["Sales"].OffsetYEmu,
            "assistant side-slot geometry is gated to orgChart, not every hierarchy layout");
    }

    [Fact]
    public void VerticalBulletList_UsesFlatEditableBulletRowsWithoutConnectors()
    {
        var data = MakeHierarchyData("Project", "Scope", "Timeline", "Risks");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalBulletList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("verticalBulletList is a flat editable list layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.Rectangle)
            .Should().HaveCount(4, "root and child nodes should become ordered list rows");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().BeEmpty("a bullet list does not have org-chart connectors");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Rectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Project", "Scope", "Timeline", "Risks");
        boxes.Select(s => s.OffsetYEmu).Should().BeInAscendingOrder();
        boxes.Select(s => s.TextBody!.Paragraphs[0].BulletKind).Should()
            .OnlyContain(kind => kind == BulletKind.Char);
        boxes.Select(s => s.TextBody!.Paragraphs[0].BulletChar).Should()
            .OnlyContain(value => value == "•");
    }

    // ── Unknown family → null ──────────────────────────────────────────────────────

    [Fact]
    public void HorizontalHierarchy_ReturnsLiveLeftToRightTreeBoxesAndConnectors()
    {
        var root = new SmartArtNode { Text = "Portfolio", Level = 0 };
        var product = new SmartArtNode { Text = "Product", Level = 1 };
        product.Children.Add(new SmartArtNode { Text = "Roadmap", Level = 2 });
        root.Children.Add(product);
        root.Children.Add(new SmartArtNode { Text = "Operations", Level = 1 });

        var data = new SmartArtData
        {
            Family = SmartArtFamily.Hierarchy,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/horizontalHierarchy"
        };
        data.Nodes.Add(root);

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("horizontalHierarchy is a bounded shared hierarchy-family layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "root, children, and one grandchild should be emitted as live boxes");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(3, "horizontalHierarchy should reuse shared parent-child connector geometry");

        var boxesByText = shapes
            .Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .ToDictionary(
                s => s.TextBody!.Paragraphs.First().Runs.First().Text,
                StringComparer.Ordinal);

        boxesByText["Product"].OffsetXEmu.Should().BeGreaterThan(boxesByText["Portfolio"].OffsetXEmu,
            "child/report nodes should sit to the right of the root");
        boxesByText["Operations"].OffsetXEmu.Should().Be(boxesByText["Product"].OffsetXEmu,
            "sibling reports share the same horizontal hierarchy depth column");
        boxesByText["Roadmap"].OffsetXEmu.Should().BeGreaterThan(boxesByText["Product"].OffsetXEmu,
            "deeper descendants advance to the next right-hand column");
    }

    [Fact]
    public void UnknownFamily_ReturnsNull()
    {
        var data = MakeData(SmartArtFamily.Unknown, "A", "B");
        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());
        result.Should().BeNull("unknown family must return null so compositor uses cached drawing");
    }

    [Fact]
    public void ContinuousBlockProcess_ReturnsLiveProcessBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/continuousBlockProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("continuousBlockProcess is a bounded shared process layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "one live box should be emitted per process node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(2, "adjacent continuous-block process nodes need shared connectors");
    }

    [Fact]
    public void BasicProcess_ReturnsLiveProcessBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("basicProcess is a bounded shared process layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "one live box should be emitted per process node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(2, "adjacent basic-process nodes need shared connectors");
    }

    [Fact]
    public void BasicTimeline_ReturnsRailMarkersAlternatingBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "Plan", "Build", "Test", "Ship");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicTimeline";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("basicTimeline is a bounded shared timeline layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live text box should be emitted per timeline node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Ellipse)
            .Should().HaveCount(4, "one timeline marker should be emitted per node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(5, "the rail and node stems are shared live geometry");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Plan", "Build", "Test", "Ship");
        boxes[0].OffsetYEmu.Should().BeLessThan(boxes[1].OffsetYEmu);
        boxes[1].OffsetYEmu.Should().BeGreaterThan(boxes[2].OffsetYEmu);
    }

    [Fact]
    public void StepDownProcess_ReturnsDescendingLiveBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "One", "Two", "Three", "Four");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/StepDownProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("StepDownProcess is a native PowerPoint process layout");
        var boxes = shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Should().HaveCount(4);
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(3, "each step after the first connects to its predecessor");
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("One", "Two", "Three", "Four");
        boxes.Select(s => s.OffsetYEmu).Should().BeInAscendingOrder(
            "StepDownProcess should descend in display order");
    }

    [Fact]
    public void VerticalProcess_ReturnsTopToBottomLiveBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C", "D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("verticalProcess is a bounded shared process-family layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live box should be emitted per vertical-process node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(3, "adjacent vertical-process nodes need shared connector ops");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C", "D");
        boxes.Select(s => s.OffsetYEmu)
            .Should().BeInAscendingOrder("verticalProcess should lay process boxes out top-to-bottom");
        boxes.Select(s => s.OffsetXEmu)
            .Should().OnlyContain(x => x == boxes[0].OffsetXEmu, "verticalProcess uses a single centered process column");
    }

    [Fact]
    public void SegmentedProcess_ReturnsLiveProcessBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/segmentedProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("segmentedProcess is a bounded ordered-stage process layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "one live box should be emitted per segmented-process node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(2, "adjacent segmented-process nodes need shared connectors");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C");
        boxes.Select(s => s.OffsetXEmu)
            .Should().BeInAscendingOrder("segmentedProcess should reuse the shared process-family geometry");
    }

    [Fact]
    public void ChevronProcess_ReturnsLiveChevronStagesWithPowerPointLikeOverlap()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/chevronProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("chevronProcess is a bounded ordered-stage process layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.Chevron)
            .Should().HaveCount(3, "one live chevron should be emitted per chevron-process node");
        shapes.Should().NotContain(s => s.AutoShapeKind == DrawingShapeKind.Line,
            "the chevron polygons provide the process direction without renderer-local connectors");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Chevron).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C");
        boxes.Select(s => s.OffsetXEmu)
            .Should().BeInAscendingOrder("chevronProcess stages should remain left to right");
        boxes[1].OffsetXEmu.Should().BeLessThan(boxes[0].OffsetXEmu + boxes[0].ExtentCxEmu,
            "adjacent chevrons should overlap instead of being separated by a generic connector gap");
        boxes.All(s => s.PresetGeometryAdjustments.TryGetValue("adj", out var value) && value > 0)
            .Should().BeTrue("the shared Chevron preset should carry its point-depth adjustment");
        boxes.Select(s => s.PresetGeometryAdjustments["adj"])
            .Should().OnlyContain(value => value == 24000,
                "the Chevron preset uses its normalized 24% DrawingML guide value");
    }

    [Fact]
    public void BasicChevronProcess_ReturnsLiveChevronStagesWithSharedGeometry()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicChevronProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("basicChevronProcess is a bounded ordered-stage process layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.Chevron)
            .Should().HaveCount(3, "one live chevron should be emitted per basic-chevron-process node");
        shapes.Should().NotContain(s => s.AutoShapeKind == DrawingShapeKind.Line);

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Chevron).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C");
        boxes.Select(s => s.OffsetXEmu)
            .Should().BeInAscendingOrder("basicChevronProcess stages should remain left to right");
        boxes.Select(s => s.PresetGeometryAdjustments["adj"])
            .Should().OnlyContain(value => value > 0);
    }

    [Fact]
    public void ClosedChevronProcess_UsesTheSameEvidenceBackedChevronGeometry()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/closedChevronProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("closedChevronProcess is a bounded ordered-stage process layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.Chevron)
            .Should().HaveCount(3, "one live chevron should be emitted per closed-chevron-process node");
        shapes.Should().NotContain(s => s.AutoShapeKind == DrawingShapeKind.Line);

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Chevron).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C");
        boxes.Select(s => s.OffsetXEmu)
            .Should().BeInAscendingOrder("closedChevronProcess stages should remain left to right");
        var standard = SmartArtLayoutEngine.Layout(
            MakeChevronData("chevronProcess", "A", "B", "C"),
            FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!
            .Where(s => s.AutoShapeKind == DrawingShapeKind.Chevron)
            .ToList();
        boxes.Select(s => (s.OffsetXEmu, s.ExtentCxEmu, s.ExtentCyEmu))
            .Should().Equal(standard.Select(s => (s.OffsetXEmu, s.ExtentCxEmu, s.ExtentCyEmu)),
                "the corpus provides no evidence for a distinct closed-chevron geometry");
    }

    [Fact]
    public void ChevronProcess_MalformedInput_UsesCachedFallback()
    {
        var data = MakeChevronData("basicChevronProcess");
        SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())
            .Should().BeNull("a chevron process with no nodes must keep the cached drawing authoritative");
    }

    [Theory]
    [InlineData(13)]
    [InlineData(20)]
    public void ChevronProcess_PreservesAllStagesBeyondOriginalTwelveItemCutoff(int nodeCount)
    {
        var data = MakeChevronData(
            "chevronProcess",
            Enumerable.Range(1, nodeCount).Select(index => $"Stage {index}").ToArray());

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("Chevron stage geometry scales its interlocking step by node count");
        shapes!.Should().HaveCount(nodeCount);
        shapes.Should().AllSatisfy(shape => shape.AutoShapeKind.Should().Be(DrawingShapeKind.Chevron));
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, nodeCount).Select(index => $"Stage {index}"));
    }

    [Fact]
    public void BendingProcess_ReturnsLiveProcessBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/bendingProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("bendingProcess is admitted as a bounded shared process-family approximation");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "one live box should be emitted per bending-process node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(2, "adjacent bending-process nodes need shared connectors");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C");
        boxes.Select(s => s.OffsetXEmu)
            .Should().BeInAscendingOrder("bendingProcess should reuse the shared process-family geometry");
        boxes.Select(s => s.OffsetYEmu)
            .Distinct()
            .Should().HaveCountGreaterThan(1, "bendingProcess uses its two-track zig-zag geometry");
        boxes[1].OffsetYEmu.Should().BeGreaterThan(boxes[0].OffsetYEmu);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(20)]
    public void BendingProcess_PreservesAllNodesBeyondOriginalTwelveItemCutoff(int nodeCount)
    {
        var data = MakeData(SmartArtFamily.Process,
            Enumerable.Range(1, nodeCount).Select(index => $"Node {index}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/bendingProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("bendingProcess scales its shared two-track geometry by node count");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(nodeCount);
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(nodeCount - 1);
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, nodeCount).Select(index => $"Node {index}"));
    }

    [Theory]
    [InlineData("chevronProcess")]
    [InlineData("basicChevronProcess")]
    [InlineData("closedChevronProcess")]
    public void ChevronProcess_KeepsAuthoredLongNodeTextOnLiveLayout(string layout)
    {
        var longText = string.Concat(Enumerable.Repeat("PowerPoint keeps this authored SmartArt text editable. ", 16));
        var data = MakeChevronData(layout, longText, "Next step");

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("valid long node text should remain on the live SmartArt path");
        shapes!.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Chevron)
            .Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain(longText);
    }

    [Fact]
    public void AlternatingProcess_ReturnsUpperLowerTrackBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C", "D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/alternatingProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("alternatingProcess has bounded shared alternating-track geometry");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live box should be emitted per alternating-process node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(3, "adjacent alternating-process nodes need shared connectors");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C", "D");
        boxes[1].OffsetYEmu.Should().BeGreaterThan(boxes[0].OffsetYEmu,
            "the second node should move to the lower alternating track");
        boxes[2].OffsetYEmu.Should().Be(boxes[0].OffsetYEmu,
            "the third node should return to the upper alternating track");
        boxes[2].OffsetXEmu.Should().BeGreaterThan(boxes[0].OffsetXEmu,
            "the next upper-track pair should advance horizontally");
        boxes[3].OffsetYEmu.Should().Be(boxes[1].OffsetYEmu,
            "the fourth node should share the lower alternating track");

        foreach (var box in boxes)
        {
            box.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            box.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (box.OffsetXEmu + box.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (box.OffsetYEmu + box.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Fact]
    public void ArrowRibbon_ReturnsRibbonSegmentsAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "Discover", "Plan", "Deliver");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/arrowRibbon";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("arrowRibbon has bounded shared process-family ribbon geometry");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.Ribbon)
            .Should().HaveCount(3, "one live ribbon segment should be emitted per arrow-ribbon node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(2, "adjacent arrow-ribbon segments need shared connector ops");

        var ribbons = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Ribbon).ToList();
        ribbons.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Discover", "Plan", "Deliver");
        ribbons.Select(s => s.OffsetXEmu)
            .Should().BeInAscendingOrder("arrowRibbon segments should advance left-to-right");
        ribbons.Select(s => s.OffsetYEmu).Distinct()
            .Should().ContainSingle("arrowRibbon keeps a single centered ribbon track");

        foreach (var ribbon in ribbons)
        {
            ribbon.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            ribbon.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (ribbon.OffsetXEmu + ribbon.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (ribbon.OffsetYEmu + ribbon.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Fact]
    public void CircleProcess_ReturnsCircularStageBoxesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "Discover", "Plan", "Build", "Review");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/circleProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("circleProcess has bounded shared circular process geometry");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4, "one live process box should be emitted per circle-process node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(4, "circleProcess closes the process loop with shared connector ops");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Discover", "Plan", "Build", "Review");
        boxes[0].OffsetYEmu.Should().BeLessThan(boxes[1].OffsetYEmu,
            "the first node starts at the top of the circular process");
        boxes[1].OffsetXEmu.Should().BeGreaterThan(boxes[0].OffsetXEmu,
            "the second node moves clockwise to the right side");
        boxes[2].OffsetYEmu.Should().BeGreaterThan(boxes[1].OffsetYEmu,
            "the third node moves clockwise to the bottom");
        boxes[3].OffsetXEmu.Should().BeLessThan(boxes[0].OffsetXEmu,
            "the fourth node moves clockwise to the left side");
    }

    [Fact]
    public void CircleArrowProcess_RegeneratesLiveCircularStagesUnderNativeLayoutIdentity()
    {
        var data = MakeData(SmartArtFamily.Process, "Discover", "Plan", "Build", "Review");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/circleArrowProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("circleArrowProcess is a live authoring layout, not a cached-only fallback");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(4);
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(4, "the live process loop must remain connected after text edits and cache regeneration");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Discover", "Plan", "Build", "Review");
    }

    [Fact]
    public void IncreasingCircleProcess_ReturnsGrowingCirclesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C", "D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/increasingCircleProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("increasingCircleProcess is a live authoring layout");
        var circles = shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.Ellipse).ToList();
        circles.Should().HaveCount(4);
        circles.Select(s => s.ExtentCxEmu).Should().BeInAscendingOrder();
        circles.Select(s => s.ExtentCyEmu).Should().Equal(circles.Select(s => s.ExtentCxEmu));
        circles.Select(s => s.OffsetYEmu + s.ExtentCyEmu).Distinct().Should().ContainSingle(
            "increasing circles share a bottom baseline");
        circles.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C", "D");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(3, "adjacent circles remain connected after text edits");
    }

    [Fact]
    public void FunnelProcess_ReturnsNarrowingStageSegmentsAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B", "C", "D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/funnelProcess";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("funnelProcess has bounded shared funnel-stage geometry");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.Trapezoid)
            .Should().HaveCount(4, "one live trapezoid segment should be emitted per funnel-process node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().HaveCount(3, "adjacent funnel-process stages need shared connector ops");

        var segments = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Trapezoid).ToList();
        segments.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C", "D");
        segments.Select(s => s.OffsetYEmu)
            .Should().BeInAscendingOrder("funnel stages should stack top-to-bottom");
        segments.Select(s => s.ExtentCxEmu)
            .Should().BeInDescendingOrder("funnel stages should narrow toward the bottom");

        foreach (var segment in segments)
        {
            segment.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            segment.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (segment.OffsetXEmu + segment.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (segment.OffsetYEmu + segment.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Fact]
    public void BasicList_ReturnsLiveVerticalListBoxesWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.List, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/list1";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("list1 is a bounded shared list-family layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "one live box should be emitted per Basic List node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().BeEmpty("the shared list planner renders Basic List without connectors");
    }

    [Fact]
    public void List2_ReturnsLiveVerticalListBoxesWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.List, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/list2";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("list2 reuses the bounded shared list-family geometry");
        shapes!.Where(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "one live box should be emitted per list2 node");
        shapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Line)
            .Should().BeEmpty("list2 should render as a vertical list without connectors");
    }

    [Fact]
    public void BasicBlockList_ReturnsLiveVerticalListBoxesWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.List, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicBlockList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("basicBlockList is a bounded shared list-family layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "one live box should be emitted per list node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().BeEmpty("the shared list planner renders a vertical box list without connectors");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C");
        boxes.Select(s => s.OffsetYEmu)
            .Should().BeInAscendingOrder("basicBlockList should reuse the vertical list-family geometry");
    }

    [Fact]
    public void VerticalBoxList_ReturnsLiveVerticalListBoxesWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.List, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalBoxList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("verticalBoxList is a bounded shared list-family layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "one live box should be emitted per list node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().BeEmpty("the shared list planner renders a vertical box list without connectors");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C");
        boxes.Select(s => s.OffsetYEmu)
            .Should().BeInAscendingOrder("verticalBoxList should reuse the shared vertical list-family geometry");
    }

    [Fact]
    public void VerticalChevronList_ReturnsOrderedLiveChevronsWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.List, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalChevronList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("verticalChevronList is admitted to the shared list-family layout planner");
        shapes!.Should().HaveCount(3);
        shapes.Select(s => s.AutoShapeKind).Should().AllBeEquivalentTo(DrawingShapeKind.Chevron);
        shapes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C");
        shapes.Select(s => s.OffsetYEmu)
            .Should().BeInAscendingOrder("verticalChevronList preserves the authored node order");
    }

    [Fact]
    public void VerticalArrowList_ReturnsOrderedLiveDownArrowsWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.List, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalArrowList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("verticalArrowList is admitted to the shared list-family layout planner");
        shapes!.Should().HaveCount(3);
        shapes.Select(s => s.AutoShapeKind).Should().AllBeEquivalentTo(DrawingShapeKind.DownArrow);
        shapes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C");
        shapes.Select(s => s.OffsetYEmu)
            .Should().BeInAscendingOrder("verticalArrowList preserves the authored node order");
    }

    [Theory]
    [InlineData(13)]
    [InlineData(20)]
    public void VerticalChevronList_PreservesAllNodesBeyondOriginalTwelveItemCutoff(int nodeCount)
    {
        var data = MakeData(
            SmartArtFamily.List,
            Enumerable.Range(1, nodeCount).Select(index => $"Step {index}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalChevronList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("verticalChevronList scales row height until its independent minimum-height guard");
        shapes!.Should().HaveCount(nodeCount);
        shapes.Should().AllSatisfy(shape => shape.AutoShapeKind.Should().Be(DrawingShapeKind.Chevron));
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, nodeCount).Select(index => $"Step {index}"));
    }

    [Fact]
    public void StackedList_ReturnsLiveVerticalListBoxesWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.List, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/stackedList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("stackedList is a bounded shared list-family layout");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "one live box should be emitted per stacked-list node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().BeEmpty("the shared stacked-list planner renders vertical boxes without connectors");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C");
        boxes.Select(s => s.OffsetYEmu)
            .Should().BeInAscendingOrder("stackedList should reuse the shared vertical list-family geometry");
    }

    [Fact]
    public void DescendingBlockList_ReturnsRightAlignedDescendingBlocksWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.List, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/descendingBlockList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("descendingBlockList has bounded shared descending-block geometry");
        shapes!.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Should().HaveCount(3, "one live box should be emitted per descending-block-list node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().BeEmpty("the shared list planner renders descendingBlockList without connectors");

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("A", "B", "C");
        boxes.Select(s => s.OffsetYEmu)
            .Should().BeInAscendingOrder("descendingBlockList blocks stack top-to-bottom");
        boxes.Select(s => s.ExtentCxEmu)
            .Should().BeInDescendingOrder("descendingBlockList blocks should narrow toward the bottom");

        var rightEdge = boxes[0].OffsetXEmu + boxes[0].ExtentCxEmu;
        foreach (var box in boxes)
        {
            (box.OffsetXEmu + box.ExtentCxEmu).Should().Be(rightEdge,
                "descendingBlockList should keep the right edge aligned in shared geometry");
            box.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            box.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (box.OffsetXEmu + box.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (box.OffsetYEmu + box.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Fact]
    public void BasicPyramid_ReturnsCenteredWideningSegmentsWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.List, "Vision", "Strategy", "Execution", "Proof");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicPyramid";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("basicPyramid has bounded shared pyramid-segment geometry");
        shapes!.Should().HaveCount(4, "one live segment should be emitted per pyramid node");
        shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.Line)
            .Should().BeEmpty("basicPyramid emits segment shapes without connector ops");

        var segments = shapes.ToList();
        segments[0].AutoShapeKind.Should().Be(DrawingShapeKind.Triangle, "the top segment is the pyramid cap");
        segments.Skip(1).Should().OnlyContain(s => s.AutoShapeKind == DrawingShapeKind.Trapezoid,
            "lower pyramid rows are renderer-neutral trapezoid segments");
        segments.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Vision", "Strategy", "Execution", "Proof");
        segments.Select(s => s.OffsetYEmu)
            .Should().BeInAscendingOrder("basicPyramid segments stack top-to-bottom");
        segments.Select(s => s.ExtentCxEmu)
            .Should().BeInAscendingOrder("basicPyramid segments widen toward the base");

        foreach (var segment in segments)
        {
            segment.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            segment.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (segment.OffsetXEmu + segment.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (segment.OffsetYEmu + segment.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Fact]
    public void PyramidList_ReturnsCenteredNarrowingSegmentsWithoutConnectors()
    {
        var data = MakeData(
            SmartArtFamily.List,
            "Top", "Middle", "Base");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pyramidList";

        var shapes = SmartArtLayoutEngine.Layout(
            data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;

        shapes.Should().HaveCount(3);
        shapes.Should().OnlyContain(shape => shape.Kind == SlideShapeKind.AutoShape);
        shapes.Select(shape => shape.ExtentCxEmu).Should().BeInDescendingOrder();
        shapes[^1].AutoShapeKind.Should().Be(DrawingShapeKind.Triangle);
    }

    [Fact]
    public void InvertedPyramid_ReturnsLiveDescendingBandsWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.List, "Market", "Product", "Team", "Task");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/invertedPyramid";

        var shapes = SmartArtLayoutEngine.Layout(
            data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("invertedPyramid remains live for editable list nodes");
        shapes!.Should().HaveCount(4);
        shapes.Should().OnlyContain(shape => shape.Kind == SlideShapeKind.AutoShape);
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Market", "Product", "Team", "Task");
        shapes.Select(shape => shape.ExtentCxEmu).Should().BeInDescendingOrder();
        shapes[^1].AutoShapeKind.Should().Be(DrawingShapeKind.Triangle);
    }

    [Fact]
    public void BasicMatrix_ReturnsLiveQuadrantBoxesWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.Matrix, "People", "Process", "Platform", "Proof");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("basicMatrix is a bounded shared matrix-family layout");
        shapes!.Should().HaveCount(4, "one shared quadrant shape should be emitted per matrix node");
        shapes.Should().OnlyContain(s => s.AutoShapeKind == DrawingShapeKind.Rectangle,
            "the matrix planner emits quadrant boxes without connector ops");

        var boxes = shapes.ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("People", "Process", "Platform", "Proof");

        boxes[1].OffsetXEmu.Should().BeGreaterThan(boxes[0].OffsetXEmu,
            "the second quadrant should be to the right of the first");
        boxes[2].OffsetYEmu.Should().BeGreaterThan(boxes[0].OffsetYEmu,
            "the third quadrant should start the lower row");
        boxes[3].OffsetXEmu.Should().BeGreaterThan(boxes[2].OffsetXEmu,
            "the fourth quadrant should be to the right of the third");
    }

    [Fact]
    public void TitledMatrix_ReturnsLiveTitleBandAndBodyCells()
    {
        var data = MakeData(SmartArtFamily.Matrix, "Title", "North", "East", "South");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("titledMatrix is a bounded shared matrix-family layout");
        shapes!.Should().HaveCount(4, "one title band plus one body shape should be emitted per body node");
        shapes.Should().OnlyContain(s => s.AutoShapeKind == DrawingShapeKind.Rectangle,
            "the titled matrix planner emits rectangular title/body cells without connector ops");

        var boxes = shapes.ToList();
        boxes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Title", "North", "East", "South");

        boxes[0].ExtentCxEmu.Should().BeGreaterThan(boxes[1].ExtentCxEmu,
            "the title band should span the full matrix width");
        boxes[1].OffsetYEmu.Should().BeGreaterThan(boxes[0].OffsetYEmu,
            "body cells should be below the title band");
        boxes[2].OffsetXEmu.Should().BeGreaterThan(boxes[1].OffsetXEmu,
            "the second body cell should be to the right of the first");
        boxes[3].OffsetYEmu.Should().BeGreaterThan(boxes[1].OffsetYEmu,
            "the third body cell should start the lower row");
    }

    [Fact]
    public void TitledMatrix_MissingTitleFallsBackToCachedDrawing()
    {
        var data = MakeData(
            SmartArtFamily.Matrix,
            "", "North", "East");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix";

        SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())
            .Should().BeNull("a titled matrix without title text must keep the imported cache authoritative");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(16)]
    public void TitledMatrix_PreservesAllBodyNodesBeyondOriginalNineItemCutoff(int nodeCount)
    {
        var data = MakeData(
            SmartArtFamily.Matrix,
            Enumerable.Range(0, nodeCount).Select(i => i == 0 ? "Title" : $"Node{i}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("titledMatrix should remain live for every parsed body node");
        shapes!.Should().HaveCount(nodeCount);
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(0, nodeCount).Select(i => i == 0 ? "Title" : $"Node{i}"));
        shapes.Skip(1).Select(shape => shape.OffsetXEmu).Distinct().Should().HaveCount(2,
            "larger titled matrices continue in two aligned body columns");
    }

    [Fact]
    public void Matrix_MoreThanFourNodes_ContinuesWithLiveRows()
    {
        var data = MakeData(SmartArtFamily.Matrix, "A", "B", "C", "D", "E", "F");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix";

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().NotBeNull("matrix editing should remain live when the node count grows beyond one quadrant");
        result!.Should().HaveCount(6);
        result.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Rectangle);

        result.Select(shape => shape.OffsetXEmu).Distinct().Should().HaveCount(2,
            "larger matrices continue in two aligned columns");
        result.Select(shape => shape.OffsetYEmu).Distinct().Should().HaveCount(3,
            "six nodes continue into three live rows");
        foreach (var shape in result)
        {
            shape.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            shape.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (shape.OffsetYEmu + shape.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Theory]
    [InlineData(5, 2, 3)]
    [InlineData(6, 2, 3)]
    [InlineData(7, 2, 4)]
    [InlineData(9, 2, 5)]
    public void BasicMatrix_PreservesDeterministicTwoColumnRowMajorGrid(
        int nodeCount, int expectedColumns, int expectedRows)
    {
        var nodeTexts = Enumerable.Range(0, nodeCount).Select(i => $"Node {i + 1}").ToArray();
        var data = MakeData(SmartArtFamily.Matrix, nodeTexts);
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix";

        var result = SmartArtLayoutEngine.Layout(
            data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().NotBeNull();
        var boxes = result!.ToList();
        boxes.Should().HaveCount(nodeCount);
        boxes.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Rectangle);
        boxes.Select(shape => shape.PlainText).Should().Equal(nodeTexts);
        boxes.Select(shape => shape.OffsetXEmu).Distinct().Should().HaveCount(expectedColumns);
        boxes.Select(shape => shape.OffsetYEmu).Distinct().Should().HaveCount(expectedRows);

        for (var i = 0; i < boxes.Count; i++)
        {
            var shape = boxes[i];
            shape.TextBody.Should().NotBeNull();
            shape.TextBody!.Wrap.Should().BeTrue();
            shape.TextBody.Anchor.Should().Be(VerticalAnchor.Middle);
            shape.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            shape.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (shape.OffsetYEmu + shape.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);

            var row = i / expectedColumns;
            var column = i % expectedColumns;
            if (column > 0)
                shape.OffsetXEmu.Should().BeGreaterThan(boxes[i - 1].OffsetXEmu + boxes[i - 1].ExtentCxEmu);
            if (row > 0)
                shape.OffsetYEmu.Should().BeGreaterThan(boxes[i - expectedColumns].OffsetYEmu + boxes[i - expectedColumns].ExtentCyEmu);
        }
    }

    [Fact]
    public void TitledMatrix_TitleOnly_UsesLiveTitleBand()
    {
        var data = MakeData(SmartArtFamily.Matrix, "Title only");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix";

        var shapes = SmartArtLayoutEngine.Layout(
            data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("a title-only matrix remains a valid editable SmartArt state");
        shapes!.Should().ContainSingle();
        shapes[0].PlainText.Should().Be("Title only");
        shapes[0].ExtentCxEmu.Should().BeLessThanOrEqualTo(FrameCx);
        shapes[0].ExtentCyEmu.Should().BeLessThanOrEqualTo(FrameCy);
    }

    [Fact]
    public void DivergingRadial_EmitsCentralNodeOuterNodesAndConnectors()
    {
        var data = MakeData(SmartArtFamily.Relationship, "Central", "North", "East", "South");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/divergingRadial";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("divergingRadial should remain live for editable relationship nodes");
        shapes!.Should().HaveCount(7, "one central node, three connectors, and three outer nodes");
        shapes.Count(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse).Should().Be(4);
        shapes.Count(shape => shape.AutoShapeKind == DrawingShapeKind.Line).Should().Be(3);
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Where(text => text is not null)
            .Should().Equal("Central", "North", "East", "South");
        foreach (var shape in shapes.Where(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse))
        {
            shape.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            shape.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (shape.OffsetYEmu + shape.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Fact]
    public void BasicVenn_ReturnsOverlappingTranslucentEllipsesWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.Relationship, "Audience", "Need", "Offer");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicVenn";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("basicVenn has bounded shared relationship-family geometry");
        shapes!.Should().HaveCount(3, "one live ellipse should be emitted per Venn node");
        shapes.Should().OnlyContain(s => s.AutoShapeKind == DrawingShapeKind.Ellipse,
            "basicVenn emits overlapping ellipse shapes without connector ops");

        var ellipses = shapes.ToList();
        ellipses.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Audience", "Need", "Offer");

        for (int i = 1; i < ellipses.Count; i++)
        {
            ellipses[i].OffsetXEmu.Should().BeGreaterThan(ellipses[i - 1].OffsetXEmu);
            ellipses[i].OffsetXEmu.Should().BeLessThan(
                ellipses[i - 1].OffsetXEmu + ellipses[i - 1].ExtentCxEmu,
                "adjacent Venn circles should overlap horizontally");
        }

        foreach (var ellipse in ellipses)
        {
            ((ShapeFill.Solid)ellipse.Fill!).Color.Alpha.Should().BeLessThan(255,
                "Venn fills must remain translucent so intersections are visible in shared renderers");
            ellipse.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            ellipse.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (ellipse.OffsetXEmu + ellipse.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (ellipse.OffsetYEmu + ellipse.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void BasicVenn_MoreThanFourNodes_ContinuesWithLiveOverlappingEllipses(int nodeCount)
    {
        var data = MakeData(
            SmartArtFamily.Relationship,
            Enumerable.Range(0, nodeCount).Select(index => $"Node {index + 1}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicVenn";

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().NotBeNull("basicVenn should remain live when the authored node count grows");
        result!.Should().HaveCount(nodeCount);
        result.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse);
        result.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, nodeCount).Select(index => $"Node {index}"));

        foreach (var ellipse in result)
        {
            ellipse.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            ellipse.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (ellipse.OffsetXEmu + ellipse.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (ellipse.OffsetYEmu + ellipse.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Fact]
    public void RadialVenn_ReturnsRadialOverlappingTranslucentEllipsesWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.Relationship, "Customer", "Product", "Market", "Proof");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/radialVenn";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("radialVenn has bounded shared relationship-family geometry");
        shapes!.Should().HaveCount(4, "one live ellipse should be emitted per radial Venn node");
        shapes.Should().OnlyContain(s => s.AutoShapeKind == DrawingShapeKind.Ellipse,
            "radialVenn emits overlapping ellipse shapes without connector ops");

        var ellipses = shapes.ToList();
        ellipses.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Customer", "Product", "Market", "Proof");

        var centerXs = ellipses.Select(s => s.OffsetXEmu + s.ExtentCxEmu / 2).ToList();
        var centerYs = ellipses.Select(s => s.OffsetYEmu + s.ExtentCyEmu / 2).ToList();
        centerXs.Distinct().Should().HaveCountGreaterThan(1,
            "radialVenn should place nodes around the center, not in a single horizontal row");
        centerYs.Distinct().Should().HaveCountGreaterThan(1,
            "radialVenn should place nodes around the center, not in a single vertical stack");

        long averageCenterX = (long)centerXs.Average();
        long averageCenterY = (long)centerYs.Average();
        foreach (var ellipse in ellipses)
        {
            ((ShapeFill.Solid)ellipse.Fill!).Color.Alpha.Should().BeLessThan(255,
                "radial Venn fills must remain translucent so intersections are visible in shared renderers");
            ellipse.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            ellipse.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (ellipse.OffsetXEmu + ellipse.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (ellipse.OffsetYEmu + ellipse.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
            Math.Abs(ellipse.OffsetXEmu + ellipse.ExtentCxEmu / 2 - averageCenterX)
                .Should().BeLessThan(ellipse.ExtentCxEmu,
                    "radial Venn ellipses should overlap near a shared center");
            Math.Abs(ellipse.OffsetYEmu + ellipse.ExtentCyEmu / 2 - averageCenterY)
                .Should().BeLessThan(ellipse.ExtentCyEmu,
                    "radial Venn ellipses should overlap near a shared center");
        }
    }

    [Fact]
    public void RadialVenn_BelowMinimumNodeCount_ReturnsNullForCachedFallback()
    {
        var data = MakeData(SmartArtFamily.Relationship, "N1", "N2");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/radialVenn";

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().BeNull("radialVenn needs at least three relationship nodes");
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void RadialVenn_LargerNodeCountsRemainLive(int nodeCount)
    {
        var data = MakeData(
            SmartArtFamily.Relationship,
            Enumerable.Range(1, nodeCount).Select(i => $"Node {i}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/radialVenn";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("radialVenn should remain live as authored node count grows");
        shapes!.Should().HaveCount(nodeCount);
        shapes.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse);
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, nodeCount).Select(i => $"Node {i}"));
        shapes.Should().AllSatisfy(shape =>
        {
            shape.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            shape.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (shape.OffsetYEmu + shape.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        });
    }

    [Fact]
    public void TargetList_ReturnsConcentricEllipsesWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.Relationship, "Market", "Segment", "Account", "Champion");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/targetList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("targetList has bounded shared relationship-family geometry");
        shapes!.Should().HaveCount(4, "one live target ellipse should be emitted per node");
        shapes.Should().OnlyContain(s => s.AutoShapeKind == DrawingShapeKind.Ellipse,
            "targetList emits concentric ellipse shapes without connector ops");

        var ellipses = shapes.ToList();
        ellipses.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Market", "Segment", "Account", "Champion");
        ellipses.Select(s => s.ExtentCxEmu)
            .Should().BeInDescendingOrder("targetList ellipses should shrink inward toward the target center");
        ellipses.Select(s => s.ExtentCyEmu)
            .Should().BeInDescendingOrder("targetList ellipses should shrink inward toward the target center");

        long expectedCenterX = ellipses[0].OffsetXEmu + ellipses[0].ExtentCxEmu / 2;
        long expectedCenterY = ellipses[0].OffsetYEmu + ellipses[0].ExtentCyEmu / 2;
        foreach (var ellipse in ellipses)
        {
            ((ShapeFill.Solid)ellipse.Fill!).Color.Alpha.Should().BeLessThan(255,
                "targetList fills should remain translucent enough for nested rings to stay visible");
            (ellipse.OffsetXEmu + ellipse.ExtentCxEmu / 2).Should().BeCloseTo(expectedCenterX, 2,
                "targetList ellipses should share a center point");
            (ellipse.OffsetYEmu + ellipse.ExtentCyEmu / 2).Should().BeCloseTo(expectedCenterY, 2,
                "targetList ellipses should share a center point");
            ellipse.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            ellipse.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (ellipse.OffsetXEmu + ellipse.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (ellipse.OffsetYEmu + ellipse.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    public void TargetList_PreservesEveryNodeBeyondOriginalFiveNodeCutoff(int nodeCount)
    {
        var data = MakeData(
            SmartArtFamily.Relationship,
            Enumerable.Range(1, nodeCount).Select(i => $"Node {i}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/targetList";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("targetList should remain live for every parsed node");
        shapes!.Should().HaveCount(nodeCount,
            "a larger targetList must not silently fall back to its cached drawing");
        shapes.Should().OnlyContain(s => s.AutoShapeKind == DrawingShapeKind.Ellipse);
        shapes.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, nodeCount).Select(i => $"Node {i}"));
        shapes.Select(s => s.ExtentCxEmu).Should().BeInDescendingOrder();
    }

    [Fact]
    public void StackedVenn_ReturnsOffsetTranslucentEllipsesWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.Relationship, "Market", "Product", "Proof");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/stackedVenn";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("stackedVenn has bounded shared relationship-family geometry");
        shapes!.Should().HaveCount(3, "one live ellipse should be emitted per stacked Venn node");
        shapes.Should().OnlyContain(s => s.AutoShapeKind == DrawingShapeKind.Ellipse,
            "stackedVenn emits offset translucent ellipse shapes without connector ops");

        var ellipses = shapes.ToList();
        ellipses.Select(s => s.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Market", "Product", "Proof");
        ellipses.Select(s => s.OffsetXEmu)
            .Should().BeInAscendingOrder("stackedVenn ellipses should step down and right");
        ellipses.Select(s => s.OffsetYEmu)
            .Should().BeInAscendingOrder("stackedVenn ellipses should step down and right");

        for (int i = 1; i < ellipses.Count; i++)
        {
            ellipses[i].OffsetXEmu.Should().BeLessThan(
                ellipses[i - 1].OffsetXEmu + ellipses[i - 1].ExtentCxEmu,
                "adjacent stacked Venn ellipses should overlap horizontally");
            ellipses[i].OffsetYEmu.Should().BeLessThan(
                ellipses[i - 1].OffsetYEmu + ellipses[i - 1].ExtentCyEmu,
                "adjacent stacked Venn ellipses should overlap vertically");
        }

        foreach (var ellipse in ellipses)
        {
            ((ShapeFill.Solid)ellipse.Fill!).Color.Alpha.Should().BeLessThan(255,
                "stacked Venn fills must remain translucent so overlaps are visible in shared renderers");
            ellipse.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            ellipse.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (ellipse.OffsetXEmu + ellipse.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (ellipse.OffsetYEmu + ellipse.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Fact]
    public void StackedVenn_BelowMinimumNodeCount_ReturnsNullForCachedFallback()
    {
        var data = MakeData(SmartArtFamily.Relationship, "N1");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/stackedVenn";

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().BeNull("stackedVenn needs at least two relationship nodes");
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void StackedVenn_LargerNodeCountsRemainLive(int nodeCount)
    {
        var data = MakeData(
            SmartArtFamily.Relationship,
            Enumerable.Range(1, nodeCount).Select(i => $"Node {i}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/stackedVenn";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("stackedVenn should remain live as authored node count grows");
        shapes!.Should().HaveCount(nodeCount);
        shapes.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse);
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, nodeCount).Select(i => $"Node {i}"));
        shapes.Should().AllSatisfy(shape =>
        {
            shape.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            shape.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (shape.OffsetYEmu + shape.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        });
    }

    [Fact]
    public void InterlockingRings_ReturnsOverlappingTranslucentEllipsesInNodeOrder()
    {
        var data = MakeData(SmartArtFamily.Relationship, "Plan", "Build", "Review", "Share");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/interlockingRings";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("Interlocking Rings has bounded shared relationship geometry");
        shapes!.Should().HaveCount(4);
        shapes.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse);
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Plan", "Build", "Review", "Share");
        shapes.Select(shape => shape.OffsetXEmu).Should().BeInAscendingOrder();
        shapes.Select(shape => shape.ExtentCxEmu).Distinct().Should().ContainSingle();
        shapes.Select(shape => shape.ExtentCyEmu).Distinct().Should().ContainSingle();

        foreach (var shape in shapes)
        {
            ((ShapeFill.Solid)shape.Fill!).Color.Alpha.Should().BeLessThan(255);
            shape.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            shape.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (shape.OffsetYEmu + shape.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Fact]
    public void InterlockingRings_BelowMinimumNodeCount_ReturnsNullForCachedFallback()
    {
        var data = MakeData(SmartArtFamily.Relationship, "N1");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/interlockingRings";

        SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())
            .Should().BeNull("interlockingRings needs at least two relationship nodes");
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void InterlockingRings_LargerNodeCountsRemainLive(int nodeCount)
    {
        var data = MakeData(
            SmartArtFamily.Relationship,
            Enumerable.Range(1, nodeCount).Select(i => $"Node {i}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/interlockingRings";

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("interlockingRings should remain live as authored node count grows");
        shapes!.Should().HaveCount(nodeCount);
        shapes.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse);
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, nodeCount).Select(i => $"Node {i}"));
        shapes.Should().AllSatisfy(shape =>
        {
            shape.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            shape.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (shape.OffsetYEmu + shape.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        });
    }

    [Fact]
    public void UnsupportedKnownProcessSibling_ReturnsNull()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/arrowRibbon";
        data.IsLiveLayoutSupported = false;

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().BeNull("process-family layouts outside the bounded live planner should use cached drawing");
    }

    [Fact]
    public void UnsupportedCycleSibling_ReturnsNull()
    {
        var data = MakeData(SmartArtFamily.Cycle, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/nonDirectionalCycle";
        data.IsLiveLayoutSupported = false;

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().BeNull("cycle-family layouts outside the bounded live planner should use cached drawing");
    }

    [Fact]
    public void UnsupportedHierarchySibling_ReturnsNull()
    {
        var data = MakeHierarchyData("Root", "Child");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/tableHierarchy";
        data.IsLiveLayoutSupported = false;

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().BeNull("hierarchy-family layouts outside the bounded live planner should use cached drawing");
    }

    [Fact]
    public void UnsupportedMatrixSibling_ReturnsNull()
    {
        var data = MakeData(SmartArtFamily.Matrix, "A", "B", "C", "D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/gridMatrix";
        data.IsLiveLayoutSupported = false;

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().BeNull("matrix-family layouts outside the bounded live planner should use cached drawing");
    }

    [Fact]
    public void GridMatrix_UsesLiveMultiRowMatrixGeometry()
    {
        var data = MakeData(SmartArtFamily.Matrix, "A", "B", "C", "D", "E", "F");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/gridMatrix";

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().NotBeNull("gridMatrix is admitted to the shared live Matrix engine");
        result!.Should().HaveCount(6);
        result.Select(shape => shape.OffsetXEmu).Distinct().Should().HaveCount(2);
        result.Select(shape => shape.OffsetYEmu).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void BasicRelationship_UsesOverlappingEllipsesWithoutConnectors()
    {
        var data = MakeData(SmartArtFamily.Relationship, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/relationship1";
        data.IsLiveLayoutSupported = true;

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().NotBeNull("relationship1 is admitted to the shared live Relationship engine");
        result!.Should().HaveCount(3);
        result.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse);
        result.Select(shape => shape.OffsetXEmu).Should().BeInAscendingOrder();
        result[1].OffsetXEmu.Should().BeLessThan(result[0].OffsetXEmu + result[0].ExtentCxEmu);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void BasicRelationship_LargerNodeCountsRemainLive(int nodeCount)
    {
        var data = MakeData(
            SmartArtFamily.Relationship,
            Enumerable.Range(1, nodeCount).Select(i => $"Node {i}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/relationship1";
        data.IsLiveLayoutSupported = true;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("relationship1 should remain live as authored node count grows");
        shapes!.Should().HaveCount(nodeCount);
        shapes.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Ellipse);
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, nodeCount).Select(i => $"Node {i}"));
        shapes.Should().AllSatisfy(shape =>
        {
            shape.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            shape.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (shape.OffsetYEmu + shape.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        });
    }

    [Fact]
    public void OpposingIdeas_UsesInwardFacingArrows()
    {
        var data = MakeData(SmartArtFamily.Relationship, "For", "Against");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/opposingIdeas";
        data.IsLiveLayoutSupported = true;

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().NotBeNull("opposingIdeas is admitted to the shared live Relationship engine");
        result!.Should().HaveCount(2);
        result[0].AutoShapeKind.Should().Be(DrawingShapeKind.RightArrow);
        result[1].AutoShapeKind.Should().Be(DrawingShapeKind.LeftArrow);
        result[0].OffsetXEmu.Should().BeLessThan(result[1].OffsetXEmu);
        result[0].OffsetYEmu.Should().Be(result[1].OffsetYEmu);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void OpposingIdeas_LargerNodeCountsRemainLive(int nodeCount)
    {
        var data = MakeData(
            SmartArtFamily.Relationship,
            Enumerable.Range(1, nodeCount).Select(i => $"Node {i}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/opposingIdeas";
        data.IsLiveLayoutSupported = true;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("opposingIdeas should remain live as authored node count grows");
        shapes!.Should().HaveCount(nodeCount);
        shapes.Should().OnlyContain(shape =>
            shape.AutoShapeKind == DrawingShapeKind.LeftArrow ||
            shape.AutoShapeKind == DrawingShapeKind.RightArrow);
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, nodeCount).Select(i => $"Node {i}"));
        shapes.Should().AllSatisfy(shape =>
        {
            shape.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            shape.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (shape.OffsetYEmu + shape.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        });
    }

    [Fact]
    public void ConvergingRadial_UsesCompassArrowsPointingToCenter()
    {
        var data = MakeData(SmartArtFamily.Relationship, "Top", "Right", "Bottom", "Left");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/convergingRadial";
        data.IsLiveLayoutSupported = true;

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().NotBeNull("convergingRadial is admitted to the shared live Relationship engine");
        result!.Should().HaveCount(4);
        result.Select(shape => shape.AutoShapeKind).Should().Equal(
            DrawingShapeKind.DownArrow,
            DrawingShapeKind.LeftArrow,
            DrawingShapeKind.UpArrow,
            DrawingShapeKind.RightArrow);
        result[0].OffsetYEmu.Should().BeLessThan(result[2].OffsetYEmu);
        result[3].OffsetXEmu.Should().BeLessThan(result[1].OffsetXEmu);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void ConvergingRadial_LargerNodeCountsRemainLive(int nodeCount)
    {
        var data = MakeData(
            SmartArtFamily.Relationship,
            Enumerable.Range(1, nodeCount).Select(i => $"Node {i}").ToArray());
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/convergingRadial";
        data.IsLiveLayoutSupported = true;

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("convergingRadial should remain live as authored node count grows");
        shapes!.Should().HaveCount(nodeCount);
        shapes.Should().OnlyContain(shape =>
            shape.AutoShapeKind == DrawingShapeKind.LeftArrow ||
            shape.AutoShapeKind == DrawingShapeKind.RightArrow ||
            shape.AutoShapeKind == DrawingShapeKind.UpArrow ||
            shape.AutoShapeKind == DrawingShapeKind.DownArrow);
        shapes.Select(shape => shape.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(Enumerable.Range(1, nodeCount).Select(i => $"Node {i}"));
        shapes.Should().AllSatisfy(shape =>
        {
            shape.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            shape.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (shape.OffsetYEmu + shape.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        });
    }

    // BI2: when nodes parse to zero, Layout returns null so compositor uses cached-drawing fallback.
    [Fact]
    public void EmptyNodes_SupportedFamily_ReturnsNull_SoCompositorUsesFallback()
    {
        var data = new SmartArtData { Family = SmartArtFamily.Process };
        // No nodes added — supported family but zero nodes
        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());
        result.Should().BeNull(
            "BI2: supported family with 0 nodes must return null so the compositor proceeds " +
            "to the cached-drawing fallback instead of rendering blank");
    }

    [Theory]
    [InlineData(SmartArtFamily.List)]
    [InlineData(SmartArtFamily.Cycle)]
    [InlineData(SmartArtFamily.Hierarchy)]
    public void EmptyNodes_AllSupportedFamilies_ReturnNull(SmartArtFamily family)
    {
        var data = new SmartArtData { Family = family };
        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());
        result.Should().BeNull($"BI2: {family} with 0 nodes must return null");
    }

    // ── Compositor integration ─────────────────────────────────────────────────────

    [Fact]
    public void Compositor_LiveLayout_UsedWhenFamilySupported()
    {
        // Build a SmartArt shape with Data.Family = Process (no fallback shapes)
        var data = MakeData(SmartArtFamily.Process, "Step 1", "Step 2", "Step 3");
        var smart = new SmartArtShape { Data = data };

        var container = new SlideShape
        {
            Id          = 50,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        // 1 background + N live shapes (3 boxes + 2 connectors = 5 shape ops)
        ops.Should().HaveCountGreaterThan(1, "live layout must produce shape ops");
        ops.Skip(1).Should().AllBeOfType<DrawOp.Shape>("all live shapes are DrawOp.Shape");
        ops.Should().HaveCount(6, "background + 3 boxes + 2 connectors");
    }

    [Fact]
    public void Compositor_LiveLayout_UsesSharedSmartArtStylePlan()
    {
        var data = MakeData(SmartArtFamily.Process, "Step 1", "Step 2");
        var smart = new SmartArtShape { Data = data };
        smart.QuickStyle = new SmartArtQuickStyleMetadata
        {
            UniqueId = "urn:smartart:style:intense-effect",
            Title = "Intense Effect"
        };
        smart.Colors = new SmartArtColorMetadata
        {
            UniqueId = "urn:smartart:colors:colorful-accent",
            Title = "Colorful Accent"
        };
        smart.Colors.Palette.Add(new ThemeAwareColor(SrgbColor.FromRgb(0x203864)));
        smart.Colors.Palette.Add(new ThemeAwareColor(SrgbColor.FromRgb(0x70AD47)));

        var container = new SlideShape
        {
            Id          = 51,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);
        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();

        shapeOps.Should().HaveCount(3, "two live boxes plus one connector");
        SolidDrawFill(shapeOps[0]).Should().Be(ThemeColorTransform.ApplyShade(SrgbColor.FromRgb(0x203864), 0.72));
        SolidDrawFill(shapeOps[2]).Should().Be(ThemeColorTransform.ApplyShade(SrgbColor.FromRgb(0x70AD47), 0.72));
    }

    [Fact]
    public void Compositor_CachedSimpleAccentHierarchy_UsesPowerPointConnectorColor()
    {
        var smart = new SmartArtShape
        {
            Data = new SmartArtData
            {
                Family = SmartArtFamily.Hierarchy,
                LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3"
            },
            QuickStyle = new SmartArtQuickStyleMetadata
            {
                UniqueId = "urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1"
            },
            Colors = new SmartArtColorMetadata
            {
                UniqueId = "urn:microsoft.com/office/officeart/2005/8/colors/accent1_2"
            }
        };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 70,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx / 2,
            ExtentCyEmu = FrameCy / 2,
            Outline = new ShapeOutline.Visible(SrgbColor.FromRgb(0x0D3A4E), 1.0)
        });

        var container = new SlideShape
        {
            Id = 71,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt = smart
        };
        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var line = SlideCompositor.Compose(pres, pres.Slides[0])
            .OfType<DrawOp.Shape>()
            .Single(op => op.Outline is ResolvedOutline.Visible);

        ((ResolvedOutline.Visible)line.Outline).Color.Should().Be(SrgbColor.FromRgb(0x0E4B66));
    }

    [Fact]
    public void Compositor_Hierarchy3_UsesSharedLivePlan()
    {
        var data = MakeHierarchyData("CEO", "VP Sales", "VP Engineering");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3";
        var smartArt = new SmartArtShape { Data = data };
        var container = new SlideShape
        {
            Id = 52,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt = smartArt
        };
        var presentation = PresentationModel.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(container);
        var shapeOps = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Shape>()
            .ToList();

        var textOps = shapeOps
            .Select(op => new
            {
                Text = op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text,
                X = op.BoundsDip.X
            })
            .Where(op => !string.IsNullOrWhiteSpace(op.Text))
            .ToDictionary(op => op.Text!, StringComparer.Ordinal);
        textOps.Keys.Should().Contain(["CEO", "VP Sales", "VP Engineering"]);
        textOps["VP Sales"].X.Should().BeGreaterThan(textOps["CEO"].X,
            "the compositor consumes hierarchy3's shared left-to-right plan");
        textOps["VP Engineering"].X.Should().Be(textOps["VP Sales"].X);
    }

    [Fact]
    public void Compositor_FallsBackToCachedDrawing_WhenFamilyUnknown()
    {
        // SmartArt with Unknown family + fallback shapes
        var data = new SmartArtData { Family = SmartArtFamily.Unknown };
        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 1,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 3,
            ExtentCyEmu   = FrameCy
        });

        var container = new SlideShape
        {
            Id          = 60,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        // 1 background + 1 fallback shape
        ops.Should().HaveCount(2, "unknown family + 1 fallback shape = background + 1 shape op");
    }

    [Fact]
    public void Compositor_ContinuousBlockProcess_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/continuousBlockProcess";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 10,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 61,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3, "continuousBlockProcess should render two live boxes plus one connector");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().NotContain("Cached fallback");
    }

    [Fact]
    public void Compositor_BasicProcess_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicProcess";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 10,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 63,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3, "basicProcess should render two live boxes plus one connector");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().NotContain("Cached fallback");
    }

    [Fact]
    public void Compositor_SegmentedProcess_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/segmentedProcess";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 10,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 64,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3, "segmentedProcess should render two live boxes plus one connector");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().NotContain("Cached fallback");
    }

    [Fact]
    public void Compositor_ChevronProcess_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/chevronProcess";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 10,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 66,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(2, "chevronProcess should render two shared live chevron stages");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().NotContain("Cached fallback");
        shapeOps.All(op => op.Text is not null).Should().BeTrue(
            "the shared chevron geometry carries the process direction");
    }

    [Fact]
    public void Compositor_BasicChevronProcess_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicChevronProcess";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 10,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 68,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(2, "basicChevronProcess should render two shared live chevron stages");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().NotContain("Cached fallback");
        shapeOps.All(op => op.Text is not null).Should().BeTrue();
    }

    [Fact]
    public void Compositor_ClosedChevronProcess_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/closedChevronProcess";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 10,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 68,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(2, "closedChevronProcess should render two shared live chevron stages");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().NotContain("Cached fallback");
        shapeOps.All(op => op.Text is not null).Should().BeTrue();
    }

    [Fact]
    public void Compositor_FallsBackToCachedDrawing_WhenKnownFamilyLayoutIsUnsupported()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/arrowRibbon";
        data.IsLiveLayoutSupported = false;

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 10,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 62,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().ContainSingle("unsupported process variants should render cached drawing, not live boxes");
        shapeOps[0].Text?.Paragraphs[0].Runs[0].Text.Should().Be("Cached fallback");
    }

    [Fact]
    public void Compositor_FunnelProcess_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B", "Live C", "Live D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/funnelProcess";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 20,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached funnel fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 64,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(7, "funnelProcess should render four live stages plus three connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain(["Live A", "Live B", "Live C", "Live D"]);
        renderedText.Should().NotContain("Cached funnel fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(3, "WPF and Avalonia hosts consume shared funnel connector DrawOps");
        shapeOps.Where(op => op.Text is not null).Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("hosts consume shared top-to-bottom funnel DrawOp geometry");
        shapeOps.Where(op => op.Text is not null).Select(op => op.BoundsDip.Width)
            .Should().BeInDescendingOrder("hosts consume shared narrowing funnel segment geometry");
    }

    [Fact]
    public void Compositor_AlternatingProcess_UsesSharedLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B", "Live C", "Live D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/alternatingProcess";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 10,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached alternating fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 72,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(7, "alternatingProcess should render four live boxes plus three connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().Contain("Live C");
        renderedText.Should().Contain("Live D");
        renderedText.Should().NotContain("Cached alternating fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(3, "WPF and Avalonia hosts consume the shared alternating-process connector DrawOps");
    }

    [Fact]
    public void Compositor_ArrowRibbon_UsesSharedLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B", "Live C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/arrowRibbon";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 10,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx / 2,
            ExtentCyEmu = FrameCy / 2,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached arrow ribbon fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id = 73,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(5, "arrowRibbon should render three live ribbon segments plus two connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().Contain("Live C");
        renderedText.Should().NotContain("Cached arrow ribbon fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(2, "WPF and Avalonia hosts consume the shared arrow-ribbon connector DrawOps");
        shapeOps.Where(op => op.Text is not null).Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("hosts consume shared left-to-right arrow-ribbon DrawOp geometry");
    }

    [Fact]
    public void Compositor_CircleProcess_UsesSharedLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B", "Live C", "Live D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/circleProcess";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 902,
            Name = "CachedCircleProcessFallback",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914_400,
            OffsetYEmu = 914_400,
            ExtentCxEmu = 1_828_800,
            ExtentCyEmu = 914_400,
            Fill = new ShapeFill.Solid(new SrgbColor(0xEE, 0xEE, 0xEE)),
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached circle process fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id = 190,
            Name = "CircleProcess SmartArt",
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(8, "circleProcess should render four live boxes plus four connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain(["Live A", "Live B", "Live C", "Live D"]);
        renderedText.Should().NotContain("Cached circle process fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(4, "WPF and Avalonia hosts consume the shared circle-process connector DrawOps");
    }

    [Fact]
    public void Compositor_BasicBlockList_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.List, "Live A", "Live B", "Live C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicBlockList";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 11,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached list fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 64,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3, "basicBlockList should render three live list boxes and no connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().Contain("Live C");
        renderedText.Should().NotContain("Cached list fallback");
        shapeOps.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("hosts consume the shared vertical list DrawOp geometry");
    }

    [Fact]
    public void Compositor_VerticalBoxList_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.List, "Live A", "Live B", "Live C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalBoxList";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 16,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached vertical box list fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 68,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3, "verticalBoxList should render three live list boxes and no connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().Contain("Live C");
        renderedText.Should().NotContain("Cached vertical box list fallback");
        shapeOps.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("hosts consume the shared vertical list DrawOp geometry");
    }

    [Fact]
    public void Compositor_StackedList_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.List, "Live A", "Live B", "Live C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/stackedList";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 17,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached stacked list fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 69,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3, "stackedList should render three live list boxes and no connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().Contain("Live C");
        renderedText.Should().NotContain("Cached stacked list fallback");
        shapeOps.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("hosts consume the shared stacked-list DrawOp geometry");
    }

    [Fact]
    public void Compositor_DescendingBlockList_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.List, "Live A", "Live B", "Live C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/descendingBlockList";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 19,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached descending block list fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 72,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3, "descendingBlockList should render three live list boxes and no connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live B");
        renderedText.Should().Contain("Live C");
        renderedText.Should().NotContain("Cached descending block list fallback");
        shapeOps.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("hosts consume the shared descending-block-list DrawOp geometry");
        shapeOps.Select(op => op.BoundsDip.Width)
            .Should().BeInDescendingOrder("hosts consume shared descending-block width geometry");

        var rightEdge = shapeOps[0].BoundsDip.X + shapeOps[0].BoundsDip.Width;
        foreach (var op in shapeOps)
        {
            (op.BoundsDip.X + op.BoundsDip.Width).Should().BeApproximately(rightEdge, 0.01,
                "shared descending-block DrawOps should keep a common right edge");
        }
    }

    [Fact]
    public void Compositor_BasicPyramid_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.List, "Live A", "Live B", "Live C", "Live D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicPyramid";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 20,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached pyramid fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 73,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(4, "basicPyramid should render four live pyramid segments and no connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain(["Live A", "Live B", "Live C", "Live D"]);
        renderedText.Should().NotContain("Cached pyramid fallback");
        shapeOps.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("hosts consume shared top-to-bottom pyramid DrawOp geometry");
        shapeOps.Select(op => op.BoundsDip.Width)
            .Should().BeInAscendingOrder("hosts consume shared widening pyramid segment geometry");
    }

    [Fact]
    public void Compositor_BasicVenn_UsesSharedLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Relationship, "Audience", "Need", "Offer");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicVenn";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 21,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx / 2,
            ExtentCyEmu = FrameCy / 2,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached Venn fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id = 74,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3, "basicVenn should render three live ellipses and no connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain(["Audience", "Need", "Offer"]);
        renderedText.Should().NotContain("Cached Venn fallback");
        for (int i = 1; i < shapeOps.Count; i++)
        {
            shapeOps[i].BoundsDip.X.Should().BeGreaterThan(shapeOps[i - 1].BoundsDip.X);
            shapeOps[i].BoundsDip.X.Should().BeLessThan(
                shapeOps[i - 1].BoundsDip.X + shapeOps[i - 1].BoundsDip.Width,
                "WPF and Avalonia hosts consume overlapping shared Venn ellipse DrawOps");
        }
    }

    [Fact]
    public void Compositor_RadialVenn_UsesSharedLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Relationship, "Customer", "Product", "Market", "Proof");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/radialVenn";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 22,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx / 2,
            ExtentCyEmu = FrameCy / 2,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached radial Venn fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id = 75,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(4, "radialVenn should render four live ellipses and no connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain(["Customer", "Product", "Market", "Proof"]);
        renderedText.Should().NotContain("Cached radial Venn fallback");
        shapeOps.Select(op => op.BoundsDip.X).Distinct().Should().HaveCountGreaterThan(1,
            "hosts consume shared radial placement instead of a single stacked fallback shape");
        shapeOps.Select(op => op.BoundsDip.Y).Distinct().Should().HaveCountGreaterThan(1,
            "hosts consume shared radial placement instead of a single row fallback shape");
    }

    [Fact]
    public void Compositor_StackedVenn_UsesSharedLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Relationship, "Market", "Product", "Proof");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/stackedVenn";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 23,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx / 2,
            ExtentCyEmu = FrameCy / 2,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached stacked Venn fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id = 76,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3, "stackedVenn should render three live ellipses and no connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain(["Market", "Product", "Proof"]);
        renderedText.Should().NotContain("Cached stacked Venn fallback");
        shapeOps.Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("hosts consume shared stacked Venn rightward offsets");
        shapeOps.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("hosts consume shared stacked Venn downward offsets");
    }

    [Fact]
    public void Compositor_BasicCycle_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Live A", "Live B", "Live C", "Live D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicCycle";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 13,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached cycle fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 66,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(8, "basicCycle should render four live boxes plus four connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live D");
        renderedText.Should().NotContain("Cached cycle fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(4, "hosts consume the shared cycle connector DrawOps");
    }

    [Fact]
    public void Compositor_RadialCycle_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Live A", "Live B", "Live C", "Live D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/radialCycle";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 16,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached radial cycle fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 69,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(8, "radialCycle should render four live boxes plus four connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live D");
        renderedText.Should().NotContain("Cached radial cycle fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(4, "hosts consume the shared radial-cycle connector DrawOps");
    }

    [Fact]
    public void Compositor_GearCycle_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Live A", "Live B", "Live C", "Live D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/gearCycle";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 18,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached gear cycle fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 71,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(8, "gearCycle should render four live boxes plus four connectors through the shared cycle approximation");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live D");
        renderedText.Should().NotContain("Cached gear cycle fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(4, "hosts consume shared gear-cycle connector DrawOps without renderer-local SmartArt policy");
    }

    [Fact]
    public void Compositor_TextCycle_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Live A", "Live B", "Live C", "Live D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/textCycle";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 19,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached text cycle fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 72,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(8, "textCycle should render four live boxes plus four connectors through the shared cycle planner");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live D");
        renderedText.Should().NotContain("Cached text cycle fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(4, "hosts consume shared text-cycle connector DrawOps without renderer-local SmartArt policy");
    }

    [Fact]
    public void Compositor_BlockCycle_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Live A", "Live B", "Live C", "Live D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/blockCycle";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 21,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached block cycle fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 121,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(8, "blockCycle should render four live boxes plus four connectors through the shared cycle planner");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live D");
        renderedText.Should().NotContain("Cached block cycle fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(4, "hosts consume shared block-cycle connector DrawOps without renderer-local SmartArt policy");
    }

    [Fact]
    public void Compositor_NonDirectionalCycle_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Cycle, "Live A", "Live B", "Live C", "Live D");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/nonDirectionalCycle";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 22,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached non-directional cycle fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 122,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(8, "nonDirectionalCycle should render four live boxes plus four connectors through the shared cycle planner");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Live A");
        renderedText.Should().Contain("Live D");
        renderedText.Should().NotContain("Cached non-directional cycle fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(4, "hosts consume shared non-directional-cycle connector DrawOps without renderer-local SmartArt policy");
    }

    [Fact]
    public void Compositor_BasicMatrix_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Matrix, "North", "East", "South", "West");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 22,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx / 2,
            ExtentCyEmu = FrameCy / 2,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached matrix fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id = 74,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(4, "basicMatrix should render four live quadrant ops");
        shapeOps.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain(["North", "East", "South", "West"]);
        shapeOps.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().NotContain("Cached matrix fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().BeEmpty("matrix SmartArt emits quadrant boxes only, with no connector DrawOps");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    public void Compositor_BasicMatrix_MoreThanFourNodes_UsesLiveGridOverCachedDrawing(int nodeCount)
    {
        var nodeTexts = Enumerable.Range(0, nodeCount).Select(i => $"Live {i + 1}").ToArray();
        var data = MakeData(SmartArtFamily.Matrix, nodeTexts);
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 24,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx / 2,
            ExtentCyEmu = FrameCy / 2,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph { Runs = { new Run { Text = "Cached matrix fallback" } } }
                }
            }
        });

        var container = new SlideShape
        {
            Id = 76,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var shapeOps = SlideCompositor.Compose(pres, pres.Slides[0])
            .Skip(1)
            .OfType<DrawOp.Shape>()
            .ToList();

        shapeOps.Should().HaveCount(nodeCount);
        shapeOps.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal(nodeTexts);
        shapeOps.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().NotContain("Cached matrix fallback");
        shapeOps.Should().OnlyContain(op => op.Text != null && op.BoundsDip.Width > 0 && op.BoundsDip.Height > 0);
    }

    [Fact]
    public void Compositor_TitledMatrix_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeData(SmartArtFamily.Matrix, "Title", "North", "East", "South");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 23,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx / 2,
            ExtentCyEmu = FrameCy / 2,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached titled matrix fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id = 75,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(4, "titledMatrix should render four shared live matrix ops");
        shapeOps.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain(["Title", "North", "East", "South"]);
        shapeOps.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().NotContain("Cached titled matrix fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().BeEmpty("matrix-family SmartArt emits quadrant boxes only, with no connector DrawOps");
    }

    [Fact]
    public void Compositor_BasicHierarchy_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeHierarchyData("CEO", "Sales", "Engineering");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/basicHierarchy";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 14,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached hierarchy fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 67,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(5, "basicHierarchy should render three live boxes plus two connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("CEO");
        renderedText.Should().Contain("Sales");
        renderedText.Should().Contain("Engineering");
        renderedText.Should().NotContain("Cached hierarchy fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(2, "hosts consume the shared hierarchy connector DrawOps");
    }

    [Fact]
    public void Compositor_HorizontalHierarchy_UsesSharedLiveLayoutOverCachedDrawing()
    {
        var data = MakeHierarchyData("Portfolio", "Product", "Operations");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/horizontalHierarchy";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 21,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached horizontal hierarchy fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 72,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(5, "horizontalHierarchy should render three live boxes plus two connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Portfolio");
        renderedText.Should().Contain("Product");
        renderedText.Should().Contain("Operations");
        renderedText.Should().NotContain("Cached horizontal hierarchy fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(2, "WPF and Avalonia hosts consume shared horizontal hierarchy connector DrawOps");
    }

    [Fact]
    public void Compositor_LabeledHierarchy_UsesSharedLiveLayoutOverCachedDrawing()
    {
        var data = MakeHierarchyData("Initiative", "Owner", "Outcome");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/labeledHierarchy";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 22,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached labeled hierarchy fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 73,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(5, "labeledHierarchy uses the bounded shared hierarchy approximation: three live boxes plus two connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Initiative");
        renderedText.Should().Contain("Owner");
        renderedText.Should().Contain("Outcome");
        renderedText.Should().NotContain("Cached labeled hierarchy fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(2, "WPF and Avalonia hosts consume the same shared connector DrawOps");

        var label = shapeOps.Single(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text == "Initiative");
        var childBoxes = shapeOps
            .Where(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text is "Owner" or "Outcome")
            .ToList();
        childBoxes.Should().HaveCount(2);
        childBoxes.Should().OnlyContain(op => op.BoundsDip.X > label.BoundsDip.X,
            "labeled hierarchy places branch content to the right of its section label");
    }

    [Fact]
    public void Compositor_TableHierarchy_UsesSharedLiveLayoutOverCachedDrawing()
    {
        var data = MakeHierarchyData("Portfolio", "Owners", "Milestones");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/tableHierarchy";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 23,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached table hierarchy fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 74,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3, "tableHierarchy uses one root header and two table-group cells");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Portfolio");
        renderedText.Should().Contain("Owners");
        renderedText.Should().Contain("Milestones");
        renderedText.Should().NotContain("Cached table hierarchy fallback");
        shapeOps.All(op => op.Text is not null).Should().BeTrue(
            "tableHierarchy's authored definition has no connecting lines");
    }

    [Fact]
    public void TableHierarchy_UsesAlignedGroupsWithoutConnectors()
    {
        var root = new SmartArtNode { ModelId = "root", Text = "Portfolio", Level = 0 };
        var owners = new SmartArtNode { ModelId = "owners", Text = "Owners", Level = 1 };
        owners.Children.Add(new SmartArtNode { ModelId = "owner-detail", Text = "Delivery", Level = 2 });
        var milestones = new SmartArtNode { ModelId = "milestones", Text = "Milestones", Level = 1 };
        milestones.Children.Add(new SmartArtNode { ModelId = "milestone-detail", Text = "Launch", Level = 2 });
        root.Children.Add(owners);
        root.Children.Add(milestones);

        var data = new SmartArtData
        {
            Family = SmartArtFamily.Hierarchy,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/tableHierarchy"
        };
        data.Nodes.Add(root);

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        shapes.Should().NotBeNull("tableHierarchy has a bounded shared table-cell plan");
        shapes!.Should().HaveCount(5, "root, two group headers, and two vertically stacked details");
        shapes.Should().OnlyContain(shape => shape.AutoShapeKind == DrawingShapeKind.Rectangle,
            "tableHierarchy uses renderer-neutral rectangular cells rather than generic hierarchy connectors");
        shapes.Should().NotContain(shape => shape.AutoShapeKind == DrawingShapeKind.Line,
            "the authored tableHierarchy definition does not contain connecting lines");

        var cells = shapes.ToDictionary(
            shape => shape.TextBody!.Paragraphs.First().Runs.First().Text,
            StringComparer.Ordinal);
        cells["Portfolio"].ExtentCxEmu.Should().BeGreaterThan(cells["Owners"].ExtentCxEmu,
            "the root is a full-width table header");
        cells["Owners"].OffsetXEmu.Should().Be(cells["Delivery"].OffsetXEmu,
            "a group's hierarchy stays in one aligned column");
        cells["Milestones"].OffsetXEmu.Should().Be(cells["Launch"].OffsetXEmu,
            "each table group keeps its descendants aligned");
        cells["Milestones"].OffsetXEmu.Should().BeGreaterThan(cells["Owners"].OffsetXEmu,
            "sibling groups occupy separate columns");

        foreach (var cell in cells.Values)
        {
            cell.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX);
            cell.OffsetYEmu.Should().BeGreaterThanOrEqualTo(FrameY);
            (cell.OffsetXEmu + cell.ExtentCxEmu).Should().BeLessThanOrEqualTo(FrameX + FrameCx);
            (cell.OffsetYEmu + cell.ExtentCyEmu).Should().BeLessThanOrEqualTo(FrameY + FrameCy);
        }
    }

    [Fact]
    public void Compositor_OrgChart_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeHierarchyData("CEO", "Sales", "Engineering");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/orgChart";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 18,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached org chart fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 71,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(5, "orgChart should render three live boxes plus two connectors");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("CEO");
        renderedText.Should().Contain("Sales");
        renderedText.Should().Contain("Engineering");
        renderedText.Should().NotContain("Cached org chart fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().HaveCount(2, "hosts consume shared orgChart connector DrawOps");
    }

    [Fact]
    public void Compositor_VerticalBulletList_UsesLiveLayoutOverCachedDrawing()
    {
        var data = MakeHierarchyData("Project", "Scope", "Timeline", "Risks");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalBulletList";

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 17,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached vertical bullet fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 69,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(4, "verticalBulletList should render four live bullet rows");
        var renderedText = shapeOps
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .ToList();
        renderedText.Should().Contain("Project");
        renderedText.Should().Contain("Scope");
        renderedText.Should().Contain("Timeline");
        renderedText.Should().Contain("Risks");
        renderedText.Should().NotContain("Cached vertical bullet fallback");
        shapeOps.Where(op => op.Text is null)
            .Should().BeEmpty("a flat bullet list has no hierarchy connector DrawOps");
        shapeOps.SelectMany(op => op.Text!.Paragraphs)
            .Should().OnlyContain(paragraph => paragraph.BulletKind == BulletKind.Char);
    }

    [Fact]
    public void Compositor_PictureCaptionList_EmitsSharedPictureAndCaptionOps()
    {
        var data = MakeData(SmartArtFamily.List, "Live A", "Live B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList";
        data.IsLiveLayoutSupported = true;
        foreach (var node in data.Nodes)
            node.Picture = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id = 12,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx / 2,
            ExtentCyEmu = FrameCy / 2,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached picture caption fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id = 65,
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = FrameX,
            OffsetYEmu = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        ops.OfType<DrawOp.Picture>().Should().HaveCount(2);
        ops.OfType<DrawOp.Shape>()
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain(["Live A", "Live B"]);
        ops.OfType<DrawOp.Shape>()
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().NotContain("Cached picture caption fallback");
    }

    [Fact]
    public void Compositor_FallsBackToCachedDrawing_WhenListFamilyLayoutIsUnsupported()
    {
        var data = MakeData(SmartArtFamily.List, "Live A", "Live B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList";
        data.IsLiveLayoutSupported = false;

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 12,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached unsupported list fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 65,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().ContainSingle("unsupported list siblings should render cached drawing, not live boxes");
        shapeOps[0].Text?.Paragraphs[0].Runs[0].Text.Should().Be("Cached unsupported list fallback");
    }

    [Fact]
    public void Compositor_FallsBackToCachedDrawing_WhenHierarchyFamilyLayoutIsUnsupported()
    {
        var data = MakeHierarchyData("Root", "Child");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/unknownHierarchy";
        data.IsLiveLayoutSupported = false;

        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 15,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2,
            TextBody      = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cached unsupported hierarchy fallback" } }
                    }
                }
            }
        });

        var container = new SlideShape
        {
            Id          = 68,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        var shapeOps = ops.Skip(1).OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().ContainSingle("unsupported hierarchy siblings should render cached drawing, not live boxes");
        shapeOps[0].Text?.Paragraphs[0].Runs[0].Text.Should().Be("Cached unsupported hierarchy fallback");
    }

    [Fact]
    public void Compositor_FallsBackToPlaceholderRect_WhenNoDataAndNoFallback()
    {
        // SmartArt with neither Data nor FallbackShapes
        var smart = new SmartArtShape(); // no Data, no FallbackShapes

        var container = new SlideShape
        {
            Id          = 70,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        ops.Should().HaveCount(2, "background + grey placeholder rectangle");
        ops[1].Should().BeOfType<DrawOp.Shape>();
    }

    // ── SmartArtNode model ──────────────────────────────────────────────────────

    [Fact]
    public void SmartArtNode_ChildrenTreeBuildsCorrectly()
    {
        var root = new SmartArtNode { ModelId = "R", Text = "Root", Level = 0 };
        root.Children.Add(new SmartArtNode { ModelId = "C1", Text = "Child1", Level = 1 });
        root.Children.Add(new SmartArtNode { ModelId = "C2", Text = "Child2", Level = 1 });
        root.Children[0].Children.Add(new SmartArtNode { ModelId = "GC1", Text = "GrandChild1", Level = 2 });

        root.Children.Should().HaveCount(2);
        root.Children[0].Children.Should().HaveCount(1);
        root.Children[0].Children[0].Text.Should().Be("GrandChild1");
    }

    [Fact]
    public void SmartArtData_FamilyAndNodesDefault()
    {
        var d = new SmartArtData();
        d.Family.Should().Be(SmartArtFamily.Unknown);
        d.Nodes.Should().BeEmpty();
        d.LayoutUniqueId.Should().BeEmpty();
        d.IsLiveLayoutSupported.Should().BeTrue("manually constructed test data remains live-capable unless the reader disables it");
    }

    // ── BI1: unbalanced-tree no-overlap ──────────────────────────────────────────

    /// <summary>
    /// BI1: Root → A(3 leaves) + B(1 leaf).
    /// With even slot distribution, A's children each get availW/8 but boxW=availW/4
    /// → boxX goes negative → overlap.  With proportional slots (GetTreeWidth) each
    /// child gets a slot at least as wide as boxW, so boxes must be disjoint.
    /// </summary>
    [Fact]
    public void Hierarchy_UnbalancedTree_BoxesDontOverlap()
    {
        // Root
        //   A  (3 children: L1, L2, L3)
        //   B  (1 child:    L4)
        var root = new SmartArtNode { Text = "Root", Level = 0 };
        var a    = new SmartArtNode { Text = "A",    Level = 1 };
        var b    = new SmartArtNode { Text = "B",    Level = 1 };
        a.Children.Add(new SmartArtNode { Text = "L1", Level = 2 });
        a.Children.Add(new SmartArtNode { Text = "L2", Level = 2 });
        a.Children.Add(new SmartArtNode { Text = "L3", Level = 2 });
        b.Children.Add(new SmartArtNode { Text = "L4", Level = 2 });
        root.Children.Add(a);
        root.Children.Add(b);

        var data = new SmartArtData { Family = SmartArtFamily.Hierarchy };
        data.Nodes.Add(root);

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;
        shapes.Should().NotBeNull();

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Should().HaveCountGreaterThan(0);

        // Assert all box rects are disjoint (no two boxes overlap).
        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var bi = boxes[i];
                var bj = boxes[j];

                // Two rects overlap iff neither is entirely to the left/right/above/below the other.
                // Only check boxes on the same level (same Y band) for horizontal overlap.
                bool sameLevel = Math.Abs(bi.OffsetYEmu - bj.OffsetYEmu) < bi.ExtentCyEmu / 2;
                if (!sameLevel) continue;

                long riRight  = bi.OffsetXEmu + bi.ExtentCxEmu;
                long rjRight  = bj.OffsetXEmu + bj.ExtentCxEmu;
                long riLeft   = bi.OffsetXEmu;
                long rjLeft   = bj.OffsetXEmu;

                bool horizontalOverlap = riLeft < rjRight && rjLeft < riRight;
                horizontalOverlap.Should().BeFalse(
                    $"BI1: box {i} [{riLeft}..{riRight}] overlaps box {j} [{rjLeft}..{rjRight}] on the same level");
            }
        }
    }

    /// <summary>BI1: A balanced tree (root + 2 children each with 1 leaf) should still lay out correctly.</summary>
    [Fact]
    public void Hierarchy_BalancedTree_BoxesDontOverlap()
    {
        var root = new SmartArtNode { Text = "Root", Level = 0 };
        var c1   = new SmartArtNode { Text = "C1",   Level = 1 };
        var c2   = new SmartArtNode { Text = "C2",   Level = 1 };
        c1.Children.Add(new SmartArtNode { Text = "L1", Level = 2 });
        c2.Children.Add(new SmartArtNode { Text = "L2", Level = 2 });
        root.Children.Add(c1);
        root.Children.Add(c2);

        var data = new SmartArtData { Family = SmartArtFamily.Hierarchy };
        data.Nodes.Add(root);

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;
        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Should().HaveCount(5, "root + 2 mid + 2 leaves");

        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var bi = boxes[i];
                var bj = boxes[j];
                bool sameLevel = Math.Abs(bi.OffsetYEmu - bj.OffsetYEmu) < bi.ExtentCyEmu / 2;
                if (!sameLevel) continue;

                bool horizontalOverlap = bi.OffsetXEmu < bj.OffsetXEmu + bj.ExtentCxEmu
                                      && bj.OffsetXEmu < bi.OffsetXEmu + bi.ExtentCxEmu;
                horizontalOverlap.Should().BeFalse($"BI1: balanced tree — box {i} overlaps box {j}");
            }
        }
    }

    // ── BI3: many-node process fits within frame ──────────────────────────────────

    [Fact]
    public void Process_18Nodes_AllBoxesWithinFrame()
    {
        // 18 nodes far exceeds the threshold at which fixed gap/connectorW overflow the frame
        var nodes = Enumerable.Range(1, 18).Select(i => $"Step{i}").ToArray();
        var data  = MakeData(SmartArtFamily.Process, nodes);

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;
        shapes.Should().NotBeNull();

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        boxes.Should().HaveCount(18, "one box per node");

        long frameRight = FrameX + FrameCx;
        foreach (var box in boxes)
        {
            long boxRight = box.OffsetXEmu + box.ExtentCxEmu;
            boxRight.Should().BeLessThanOrEqualTo(frameRight,
                $"BI3: box right edge {boxRight} must not exceed frame right {frameRight}");
            box.OffsetXEmu.Should().BeGreaterThanOrEqualTo(FrameX,
                "BI3: box left edge must not be left of frame left");
            box.ExtentCxEmu.Should().BeGreaterThan(0, "BI3: box width must be positive");
        }
    }

    // ── BI4: multi-root hierarchy renders all roots ───────────────────────────────

    [Fact]
    public void Hierarchy_TwoRoots_BothRootsRender()
    {
        // Two independent root nodes, each with one child
        var root1 = new SmartArtNode { Text = "Root1", Level = 0 };
        root1.Children.Add(new SmartArtNode { Text = "Child1", Level = 1 });

        var root2 = new SmartArtNode { Text = "Root2", Level = 0 };
        root2.Children.Add(new SmartArtNode { Text = "Child2", Level = 1 });

        var data = new SmartArtData { Family = SmartArtFamily.Hierarchy };
        data.Nodes.Add(root1);
        data.Nodes.Add(root2);

        var shapes = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;
        shapes.Should().NotBeNull();

        var boxes = shapes.Where(s => s.AutoShapeKind == DrawingShapeKind.RoundedRectangle).ToList();
        // 2 roots + 2 children = 4 boxes
        boxes.Should().HaveCount(4, "BI4: both roots and their children must render");

        var texts = boxes
            .Select(b => b.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text ?? "")
            .ToHashSet();
        texts.Should().Contain("Root1", "BI4: first root must render");
        texts.Should().Contain("Root2", "BI4: second root must render");
        texts.Should().Contain("Child1");
        texts.Should().Contain("Child2");
    }

    // ── BI2 compositor integration: empty parse → fallback ────────────────────────

    [Fact]
    public void Compositor_EmptyParsedNodes_UsesCachedDrawingFallback()
    {
        // SmartArt with a supported family but zero nodes, plus a fallback shape.
        // Before BI2 fix: Layout returned Array.Empty → compositor emitted nothing → blank.
        // After BI2 fix: Layout returns null → compositor uses FallbackShapes.
        var data  = new SmartArtData { Family = SmartArtFamily.Process }; // no nodes
        var smart = new SmartArtShape { Data = data };
        smart.FallbackShapes.Add(new SlideShape
        {
            Id            = 9,
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = FrameX,
            OffsetYEmu    = FrameY,
            ExtentCxEmu   = FrameCx / 2,
            ExtentCyEmu   = FrameCy / 2
        });

        var container = new SlideShape
        {
            Id          = 80,
            Kind        = SlideShapeKind.SmartArt,
            OffsetXEmu  = FrameX,
            OffsetYEmu  = FrameY,
            ExtentCxEmu = FrameCx,
            ExtentCyEmu = FrameCy,
            SmartArt    = smart
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(container);

        var ops = SlideCompositor.Compose(pres, pres.Slides[0]);

        // 1 background + 1 fallback shape (NOT zero live shapes from the empty live result)
        ops.Should().HaveCount(2,
            "BI2: empty live layout must fall through to cached-drawing fallback (1 bg + 1 fallback)");
    }
}
