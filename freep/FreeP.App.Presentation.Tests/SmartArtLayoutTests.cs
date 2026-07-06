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

    private static SmartArtData MakeHierarchyData(string rootText, params string[] childTexts)
    {
        var root = new SmartArtNode { Text = rootText, Level = 0 };
        foreach (var t in childTexts)
            root.Children.Add(new SmartArtNode { Text = t, Level = 1 });

        var data = new SmartArtData { Family = SmartArtFamily.Hierarchy };
        data.Nodes.Add(root);
        return data;
    }

    private static SrgbColor SolidFill(SlideShape shape) =>
        ((ShapeFill.Solid)shape.Fill!).Color.Resolved;

    private static SrgbColor SolidDrawFill(DrawOp op) =>
        ((ResolvedFill.Solid)((DrawOp.Shape)op).Fill).Color;

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

    // ── Hierarchy layout ──────────────────────────────────────────────────────────

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

    // ── Unknown family → null ──────────────────────────────────────────────────────

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
    public void UnsupportedKnownLayout_ReturnsNull()
    {
        var data = MakeData(SmartArtFamily.Process, "A", "B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/chevronProcess";
        data.IsLiveLayoutSupported = false;

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().BeNull("process-family layouts outside the bounded live planner should use cached drawing");
    }

    [Fact]
    public void UnsupportedCycleSibling_ReturnsNull()
    {
        var data = MakeData(SmartArtFamily.Cycle, "A", "B", "C");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/radialCycle";
        data.IsLiveLayoutSupported = false;

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().BeNull("cycle-family layouts outside the bounded live planner should use cached drawing");
    }

    [Fact]
    public void UnsupportedHierarchySibling_ReturnsNull()
    {
        var data = MakeHierarchyData("Root", "Child");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalBulletList";
        data.IsLiveLayoutSupported = false;

        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Should().BeNull("hierarchy-family layouts outside the bounded live planner should use cached drawing");
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
    public void Compositor_FallsBackToCachedDrawing_WhenKnownFamilyLayoutIsUnsupported()
    {
        var data = MakeData(SmartArtFamily.Process, "Live A", "Live B");
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/chevronProcess";
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
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalBulletList";
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
