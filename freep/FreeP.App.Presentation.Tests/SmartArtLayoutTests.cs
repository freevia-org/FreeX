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

    // ── Unknown family → null ──────────────────────────────────────────────────────

    [Fact]
    public void UnknownFamily_ReturnsNull()
    {
        var data = MakeData(SmartArtFamily.Unknown, "A", "B");
        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());
        result.Should().BeNull("unknown family must return null so compositor uses cached drawing");
    }

    [Fact]
    public void EmptyNodes_SupportedFamily_ReturnsEmptyList()
    {
        var data = new SmartArtData { Family = SmartArtFamily.Process };
        // No nodes added
        var result = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());
        result.Should().NotBeNull("supported family with no nodes returns empty list (not null)");
        result!.Should().BeEmpty();
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
    }
}
