using System.IO;
using FreeP.App.Compositor;
using Free.Shared.Drawing;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 12A unit tests: Group/Ungroup, Align/Distribute, BringToFront/SendToBack.
///
/// All tests run pure model logic (no WPF required — no [StaFact] needed).
/// EditingSession is constructed directly with a real Presentation + bus.
/// </summary>
public sealed class ArrangeGroupAlignTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static (Presentation pres, EditingSession session) CreateSession(int slideCount = 1)
    {
        var pres = Presentation.CreateEmpty();
        // CreateEmpty gives 1 slide; clear its placeholder shapes so tests start with a clean canvas.
        foreach (var s in pres.Slides)
            s.Shapes.Clear();
        // Add more slides if needed.
        while (pres.Slides.Count < slideCount)
        {
            var s = new Slide();
            pres.Slides.Add(s);
        }
        var bus     = new PresentationCommandBus(pres);
        var session = new EditingSession(pres, bus);
        return (pres, session);
    }

    private static SlideShape MakeRect(uint id, long x, long y, long cx, long cy) =>
        new SlideShape
        {
            Id          = id,
            Name        = $"Shape{id}",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = x,
            OffsetYEmu  = y,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
        };

    // ── Group ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void GroupSelectedShapes_TwoShapes_CreatesGroupWithTwoChildren()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 100, 200, 300, 400));
        slide.Shapes.Add(MakeRect(11, 500, 600, 300, 400));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);

        session.GroupSelectedShapes();

        // Slide should have exactly 1 shape (the group).
        slide.Shapes.Should().HaveCount(1);
        var group = slide.Shapes[0];
        group.Kind.Should().Be(SlideShapeKind.Group);
        group.Children.Should().HaveCount(2);
    }

    [Fact]
    public void GroupSelectedShapes_UnionBoundingBox_IsCorrect()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        // Shape A: x=100, y=200, cx=300, cy=400  → right=400, bottom=600
        // Shape B: x=500, y=100, cx=200, cy=500  → right=700, bottom=600
        // Union:   x=100, y=100, cx=600, cy=500
        slide.Shapes.Add(MakeRect(10, 100, 200, 300, 400));
        slide.Shapes.Add(MakeRect(11, 500, 100, 200, 500));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.GroupSelectedShapes();

        var group = slide.Shapes[0];
        group.OffsetXEmu.Should().Be(100);
        group.OffsetYEmu.Should().Be(100);
        group.ExtentCxEmu.Should().Be(600);
        group.ExtentCyEmu.Should().Be(500);
    }

    [Fact]
    public void GroupSelectedShapes_ChildrenRetainAbsoluteOffsets()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 100, 200, 300, 400));
        slide.Shapes.Add(MakeRect(11, 500, 100, 200, 500));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.GroupSelectedShapes();

        var group = slide.Shapes[0];
        // Children keep their original absolute offsets.
        var child10 = group.Children.First(c => c.Id == 10);
        var child11 = group.Children.First(c => c.Id == 11);

        child10.OffsetXEmu.Should().Be(100);
        child10.OffsetYEmu.Should().Be(200);
        child11.OffsetXEmu.Should().Be(500);
        child11.OffsetYEmu.Should().Be(100);
    }

    [Fact]
    public void GroupSelectedShapes_SelectsGroup()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 200, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.GroupSelectedShapes();

        session.SelectedShapeIds.Should().HaveCount(1);
        var selectedId = session.SelectedShapeIds[0];
        slide.Shapes.Single(s => s.Id == selectedId).Kind.Should().Be(SlideShapeKind.Group);
    }

    [Fact]
    public void GroupSelectedShapes_LessThanTwo_DoesNothing()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.GroupSelectedShapes();

        slide.Shapes.Should().HaveCount(1);
        slide.Shapes[0].Kind.Should().Be(SlideShapeKind.AutoShape);
    }

    [Fact]
    public void GroupSelectedShapes_IsUndoable()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 200, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.GroupSelectedShapes();

        slide.Shapes.Should().HaveCount(1, "group was created");

        session.Undo();

        slide.Shapes.Should().HaveCount(2, "undo restored originals");
        slide.Shapes.Any(s => s.Kind == SlideShapeKind.Group).Should().BeFalse();
    }

    // ── Ungroup ───────────────────────────────────────────────────────────────────

    [Fact]
    public void UngroupSelected_Group_ReleasesChildrenToSlide()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 200, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.GroupSelectedShapes();

        // Now ungroup.
        session.UngroupSelected();

        slide.Shapes.Should().HaveCount(2);
        slide.Shapes.Any(s => s.Kind == SlideShapeKind.Group).Should().BeFalse();
    }

    [Fact]
    public void UngroupSelected_RestoresAbsoluteOffsets()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 100, 200, 300, 400));
        slide.Shapes.Add(MakeRect(11, 500, 100, 200, 500));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.GroupSelectedShapes();

        session.UngroupSelected();

        var shape10 = slide.Shapes.First(s => s.Id == 10);
        var shape11 = slide.Shapes.First(s => s.Id == 11);

        shape10.OffsetXEmu.Should().Be(100);
        shape10.OffsetYEmu.Should().Be(200);
        shape11.OffsetXEmu.Should().Be(500);
        shape11.OffsetYEmu.Should().Be(100);
    }

    [Fact]
    public void UngroupSelected_IsUndoable()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 200, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.GroupSelectedShapes();
        session.UngroupSelected();

        slide.Shapes.Should().HaveCount(2, "ungrouped");

        session.Undo();

        slide.Shapes.Should().HaveCount(1, "group re-formed by undo");
        slide.Shapes[0].Kind.Should().Be(SlideShapeKind.Group);
    }

    // ── Group round-trip via PPTX writer/reader ──────────────────────────────────

    [Fact]
    public void GroupShape_PptxRoundTrip_ChildrenPreserved()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Clear();

        // Build group manually.
        var group = new SlideShape
        {
            Id          = 1,
            Name        = "Group 1",
            Kind        = SlideShapeKind.Group,
            OffsetXEmu  = 100,
            OffsetYEmu  = 100,
            ExtentCxEmu = 700,
            ExtentCyEmu = 500,
        };
        group.Children.Add(MakeRect(2, 100, 100, 300, 400));
        group.Children.Add(MakeRect(3, 500, 100, 200, 500));
        slide.Shapes.Add(group);

        // Round-trip through PPTX bytes.
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            PptxPackageWriter.Write(pres, ms);
            bytes = ms.ToArray();
        }

        Presentation loaded;
        using (var ms = new MemoryStream(bytes))
            loaded = PptxPackageReader.Read(ms);

        var loadedSlide = loaded.Slides[0];
        loadedSlide.Shapes.Should().HaveCount(1);
        var loadedGroup = loadedSlide.Shapes[0];
        loadedGroup.Kind.Should().Be(SlideShapeKind.Group);
        loadedGroup.Children.Should().HaveCount(2);
        loadedGroup.OffsetXEmu.Should().Be(100);
        loadedGroup.OffsetYEmu.Should().Be(100);
        loadedGroup.ExtentCxEmu.Should().Be(700);
        loadedGroup.ExtentCyEmu.Should().Be(500);
    }

    // ── Align ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void AlignLeft_MovesAllShapesToSameLeftEdge()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        // Shape A at x=100, Shape B at x=300; bounding box minX=100
        slide.Shapes.Add(MakeRect(10, 100, 0, 200, 100));
        slide.Shapes.Add(MakeRect(11, 300, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.AlignLeft();

        slide.Shapes.First(s => s.Id == 10).OffsetXEmu.Should().Be(100);
        slide.Shapes.First(s => s.Id == 11).OffsetXEmu.Should().Be(100);
    }

    [Fact]
    public void AlignRight_MovesAllShapesToSameRightEdge()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        // A: x=100, cx=200, right=300; B: x=400, cx=100, right=500; bboxRight=500
        slide.Shapes.Add(MakeRect(10, 100, 0, 200, 100));
        slide.Shapes.Add(MakeRect(11, 400, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.AlignRight();

        slide.Shapes.First(s => s.Id == 10).OffsetXEmu.Should().Be(300); // 500 - 200
        slide.Shapes.First(s => s.Id == 11).OffsetXEmu.Should().Be(400); // 500 - 100, unchanged
    }

    [Fact]
    public void AlignTop_MovesAllShapesToSameTopEdge()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 100, 100, 200));
        slide.Shapes.Add(MakeRect(11, 0, 300, 100, 200));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.AlignTop();

        slide.Shapes.First(s => s.Id == 10).OffsetYEmu.Should().Be(100);
        slide.Shapes.First(s => s.Id == 11).OffsetYEmu.Should().Be(100);
    }

    [Fact]
    public void AlignBottom_MovesAllShapesToSameBottomEdge()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        // A: y=100, cy=200, bottom=300; B: y=0, cy=400, bottom=400; bboxBottom=400
        slide.Shapes.Add(MakeRect(10, 0, 100, 100, 200));
        slide.Shapes.Add(MakeRect(11, 0, 0,   100, 400));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.AlignBottom();

        slide.Shapes.First(s => s.Id == 10).OffsetYEmu.Should().Be(200); // 400 - 200
        slide.Shapes.First(s => s.Id == 11).OffsetYEmu.Should().Be(0);   // 400 - 400
    }

    [Fact]
    public void AlignCenterH_CentersShapesHorizontally()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        // bbox: x=0 to x=600 (cx=600), center at x=300
        // A: cx=200 → should be at x=200 (so center is at 300)
        // B: cx=400 → should be at x=100 (so center is at 300)
        slide.Shapes.Add(MakeRect(10, 0,   0, 200, 100));  // right=200
        slide.Shapes.Add(MakeRect(11, 200, 0, 400, 100));  // right=600

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.AlignCenterH();

        slide.Shapes.First(s => s.Id == 10).OffsetXEmu.Should().Be(200); // (0 + 600-200)/2 = 200
        slide.Shapes.First(s => s.Id == 11).OffsetXEmu.Should().Be(100); // (0 + 600-400)/2 = 100
    }

    [Fact]
    public void AlignMiddle_CentersShapesVertically()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        // bbox: y=0 to y=600 (cy=600), center at y=300
        // A: cy=200 → y=200; B: cy=400 → y=100
        slide.Shapes.Add(MakeRect(10, 0, 0,   200, 200));
        slide.Shapes.Add(MakeRect(11, 0, 100, 200, 400));  // bottom=500... wait let me recalculate
        // A: y=0, cy=200, bottom=200; B: y=100, cy=400, bottom=500 → bboxMinY=0, bboxMaxY=500, bboxCy=500
        // center of bbox = 250; A target y = 250 - 100 = 150; B target y = 250 - 200 = 50

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.AlignMiddle();

        slide.Shapes.First(s => s.Id == 10).OffsetYEmu.Should().Be(150); // 0 + (500-200)/2 = 150
        slide.Shapes.First(s => s.Id == 11).OffsetYEmu.Should().Be(50);  // 0 + (500-400)/2 = 50
    }

    [Fact]
    public void AlignLeft_IsUndoableInOneStep()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 100, 0, 200, 100));
        slide.Shapes.Add(MakeRect(11, 300, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.AlignLeft();

        // Both moved to x=100.
        slide.Shapes.First(s => s.Id == 11).OffsetXEmu.Should().Be(100);

        session.Undo();

        // Both restored.
        slide.Shapes.First(s => s.Id == 10).OffsetXEmu.Should().Be(100, "original position restored");
        slide.Shapes.First(s => s.Id == 11).OffsetXEmu.Should().Be(300, "original position restored");
    }

    /// <summary>
    /// A group's children store ABSOLUTE slide-space coordinates (see
    /// GroupShapesCommand.Apply), so aligning a group must translate its children by the
    /// same delta as the group itself -- otherwise the group's own offset moves while its
    /// members are left behind, the exact symptom MoveShapeCommand/ResizeShapeCommand/
    /// PasteShapeCopies were fixed to remove. Reproduces the live repro through
    /// EditingSession.AlignLeft(): a group and a second shape are selected, and the group
    /// starts to the right of the standalone shape so AlignLeft actually has to move it.
    /// </summary>
    [Fact]
    public void AlignLeft_GroupChildMovesWithGroup()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        // Two shapes will be grouped; a third stays standalone as the alignment anchor.
        slide.Shapes.Add(MakeRect(10, 300, 0, 100, 100)); // group child A
        slide.Shapes.Add(MakeRect(11, 400, 0, 100, 100)); // group child B
        slide.Shapes.Add(MakeRect(12, 100, 0, 100, 100)); // standalone, left-most

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.GroupSelectedShapes();

        var groupId = session.SelectedShapeIds[0];
        var group = slide.Shapes.Single(s => s.Id == groupId);
        group.OffsetXEmu.Should().Be(300, "sanity: group starts at the union of its children");

        // Select the group plus the standalone shape and align left. The bounding box's
        // left edge is shape 12's x=100, so the group must move by -200.
        session.Select(groupId);
        session.Select(12, addToSelection: true);
        session.AlignLeft();

        group.OffsetXEmu.Should().Be(100, "group moved to the selection's left edge");
        var childA = group.Children.First(c => c.Id == 10);
        var childB = group.Children.First(c => c.Id == 11);
        childA.OffsetXEmu.Should().Be(100, "child A must translate with the group, not stay behind");
        childB.OffsetXEmu.Should().Be(200, "child B must translate with the group, not stay behind");
        slide.Shapes.First(s => s.Id == 12).OffsetXEmu.Should().Be(100, "already at the bbox left edge");

        // Undo must restore the group AND its children to their pre-align absolute positions.
        session.Undo();

        group.OffsetXEmu.Should().Be(300, "undo restores the group's original position");
        childA.OffsetXEmu.Should().Be(300, "undo restores child A's original absolute position");
        childB.OffsetXEmu.Should().Be(400, "undo restores child B's original absolute position");
        slide.Shapes.First(s => s.Id == 12).OffsetXEmu.Should().Be(100, "standalone shape unaffected");
    }

    /// <summary>
    /// Sibling no-regression check: aligning a selection with NO group in it must keep
    /// behaving exactly as before -- SlideShapeTraversal.TranslateWithDescendants is a no-op
    /// on a leaf shape (empty Children), so plain shapes must move by exactly the same
    /// amount they did before this fix.
    /// </summary>
    [Fact]
    public void AlignLeft_PlainShapesUnaffectedByGroupTranslationFix()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 100, 0, 200, 100));
        slide.Shapes.Add(MakeRect(11, 300, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.AlignLeft();

        slide.Shapes.First(s => s.Id == 10).OffsetXEmu.Should().Be(100);
        slide.Shapes.First(s => s.Id == 11).OffsetXEmu.Should().Be(100);

        session.Undo();

        slide.Shapes.First(s => s.Id == 10).OffsetXEmu.Should().Be(100, "original position restored");
        slide.Shapes.First(s => s.Id == 11).OffsetXEmu.Should().Be(300, "original position restored");
    }

    // ── Distribute ────────────────────────────────────────────────────────────────

    [Fact]
    public void AlignToSlide_UsesCanvasEdgesAndCenter()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 100, 200, 200, 100));
        slide.Shapes.Add(MakeRect(11, 300, 400, 100, 200));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.AlignCenterHToSlide();

        slide.Shapes.First(s => s.Id == 10).OffsetXEmu.Should().Be((pres.SlideSizeCxEmu - 200) / 2);
        slide.Shapes.First(s => s.Id == 11).OffsetXEmu.Should().Be((pres.SlideSizeCxEmu - 100) / 2);

        session.AlignBottomToSlide();
        slide.Shapes.First(s => s.Id == 10).OffsetYEmu.Should().Be(pres.SlideSizeCyEmu - 100);
        slide.Shapes.First(s => s.Id == 11).OffsetYEmu.Should().Be(pres.SlideSizeCyEmu - 200);
    }

    [Fact]
    public void AlignToSlide_IsUndoableInOneStep()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 100, 200, 200, 100));
        session.SelectSlide(0);
        session.Select(10);

        session.AlignRightToSlide();
        slide.Shapes[0].OffsetXEmu.Should().Be(pres.SlideSizeCxEmu - 200);

        session.Undo();
        slide.Shapes[0].OffsetXEmu.Should().Be(100);
    }

    /// <summary>
    /// Same group-children-are-absolute-coordinates defect as
    /// <see cref="AlignLeft_GroupChildMovesWithGroup"/>, exercised through
    /// AlignShapesToSlideCommand (EditingSession.AlignLeftToSlide).
    /// </summary>
    [Fact]
    public void AlignToSlide_GroupChildMovesWithGroup()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 200, 0, 100, 100)); // group child A
        slide.Shapes.Add(MakeRect(11, 300, 0, 100, 100)); // group child B

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.GroupSelectedShapes();
        var groupId = session.SelectedShapeIds[0];
        var group = slide.Shapes.Single(s => s.Id == groupId);
        group.OffsetXEmu.Should().Be(200, "sanity: group starts at the union of its children");

        session.Select(groupId);
        session.AlignLeftToSlide();

        group.OffsetXEmu.Should().Be(0, "group moved to the slide's left edge");
        var childA = group.Children.First(c => c.Id == 10);
        var childB = group.Children.First(c => c.Id == 11);
        childA.OffsetXEmu.Should().Be(0, "child A must translate with the group, not stay behind");
        childB.OffsetXEmu.Should().Be(100, "child B must translate with the group, not stay behind");

        session.Undo();

        group.OffsetXEmu.Should().Be(200, "undo restores the group's original position");
        childA.OffsetXEmu.Should().Be(200, "undo restores child A's original absolute position");
        childB.OffsetXEmu.Should().Be(300, "undo restores child B's original absolute position");
    }

    [Fact]
    public void SetSelectedRotation_AppliesToAllSelectedAndUndoRestores()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 100, 0, 200, 100));
        slide.Shapes.Add(MakeRect(11, 300, 0, 100, 100));
        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);

        session.SetSelectedRotation(-90).Should().BeTrue();
        slide.Shapes.ForEach(shape => shape.RotationDeg.Should().Be(270));

        session.Undo();
        slide.Shapes.ForEach(shape => shape.RotationDeg.Should().Be(0));
    }

    [Fact]
    public void DistributeHorizontally_ThreeShapes_EvensSpacing()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        // A: x=0,   cx=100 → right=100
        // B: x=300, cx=100 → right=400
        // C: x=600, cx=100 → right=700
        // span = 0..700 = 700; totalWidth = 300; gaps = 2; gapTotal=400; gapPerSlot=200
        // after distribute: A=0, B=0+100+200=300 (unchanged), C=300+100+200=600 (unchanged)
        // Actually all three end up evenly spread: spacing between each pair = 200
        slide.Shapes.Add(MakeRect(10, 0,   0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 300, 0, 100, 100));
        slide.Shapes.Add(MakeRect(12, 600, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.Select(12, addToSelection: true);
        session.DistributeHorizontally();

        var s10 = slide.Shapes.First(s => s.Id == 10);
        var s11 = slide.Shapes.First(s => s.Id == 11);
        var s12 = slide.Shapes.First(s => s.Id == 12);

        s10.OffsetXEmu.Should().Be(0);
        s11.OffsetXEmu.Should().Be(300);
        s12.OffsetXEmu.Should().Be(600);
    }

    [Fact]
    public void DistributeHorizontally_ThreeUnevenShapes_EvensGaps()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        // A: x=0,  cx=100 → right=100
        // B: x=50, cx=100 → right=150  (overlapping initially)
        // C: x=900, cx=100 → right=1000
        // span=0..1000=1000; totalWidth=300; gapTotal=700; gaps=2; gapPerSlot=350
        // A stays at 0; B=0+100+350=450; C=450+100+350=900 (unchanged)
        slide.Shapes.Add(MakeRect(10, 0,   0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 50,  0, 100, 100));
        slide.Shapes.Add(MakeRect(12, 900, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.Select(12, addToSelection: true);
        session.DistributeHorizontally();

        var s10 = slide.Shapes.First(s => s.Id == 10);
        var s11 = slide.Shapes.First(s => s.Id == 11);
        var s12 = slide.Shapes.First(s => s.Id == 12);

        s10.OffsetXEmu.Should().Be(0);
        s11.OffsetXEmu.Should().Be(450);
        s12.OffsetXEmu.Should().Be(900);
    }

    [Fact]
    public void DistributeVertically_ThreeShapes_EvensSpacing()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0,   100, 100));
        slide.Shapes.Add(MakeRect(11, 0, 150, 100, 100));
        slide.Shapes.Add(MakeRect(12, 0, 400, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.Select(12, addToSelection: true);
        session.DistributeVertically();

        // span=0..500=500; totalHeight=300; gapTotal=200; gaps=2; gapPerSlot=100
        // A stays at 0; B=0+100+100=200; C=200+100+100=400
        // But current B=150 → becomes 200; C=400 → stays 400
        var s10 = slide.Shapes.First(s => s.Id == 10);
        var s11 = slide.Shapes.First(s => s.Id == 11);
        var s12 = slide.Shapes.First(s => s.Id == 12);

        s10.OffsetYEmu.Should().Be(0);
        s11.OffsetYEmu.Should().Be(200);
        s12.OffsetYEmu.Should().Be(400);
    }

    [Fact]
    public void DistributeHorizontally_IsUndoableInOneStep()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0,  0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 50, 0, 100, 100));
        slide.Shapes.Add(MakeRect(12, 900, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.Select(12, addToSelection: true);
        session.DistributeHorizontally();

        var s11 = slide.Shapes.First(s => s.Id == 11);
        s11.OffsetXEmu.Should().NotBe(50, "distribute moved shape 11");

        session.Undo();

        s11.OffsetXEmu.Should().Be(50, "undo restored original position");
    }

    /// <summary>
    /// Same group-children-are-absolute-coordinates defect as
    /// <see cref="AlignLeft_GroupChildMovesWithGroup"/>, exercised through
    /// DistributeShapesCommand (EditingSession.DistributeHorizontally). One of the three
    /// distribute targets is a group; the other two are standalone shapes.
    /// </summary>
    [Fact]
    public void DistributeHorizontally_GroupChildMovesWithGroup()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(20, 0,   0, 100, 100)); // standalone A: right=100
        slide.Shapes.Add(MakeRect(10, 300, 0, 100, 100)); // group child (will union to x=300, cx=200)
        slide.Shapes.Add(MakeRect(11, 400, 0, 100, 100));
        slide.Shapes.Add(MakeRect(30, 600, 0, 100, 100)); // standalone C: right=700

        session.SelectSlide(0);
        session.Select(10);
        session.Select(11, addToSelection: true);
        session.GroupSelectedShapes();
        var groupId = session.SelectedShapeIds[0];
        var group = slide.Shapes.Single(s => s.Id == groupId);
        group.OffsetXEmu.Should().Be(300, "sanity: group starts at the union of its children");

        // span=0..700=700; totalWidth=100+200+100=400; gapTotal=300; gaps=2; gapPerSlot=150.
        // A stays at 0; group moves to 0+100+150=250; C stays at 600 (unchanged).
        session.Select(20);
        session.Select(groupId, addToSelection: true);
        session.Select(30, addToSelection: true);
        session.DistributeHorizontally();

        group.OffsetXEmu.Should().Be(250, "group moved by the distribute pass");
        var childA = group.Children.First(c => c.Id == 10);
        var childB = group.Children.First(c => c.Id == 11);
        childA.OffsetXEmu.Should().Be(250, "child A must translate with the group, not stay behind");
        childB.OffsetXEmu.Should().Be(350, "child B must translate with the group, not stay behind");
        slide.Shapes.First(s => s.Id == 20).OffsetXEmu.Should().Be(0);
        slide.Shapes.First(s => s.Id == 30).OffsetXEmu.Should().Be(600);

        session.Undo();

        group.OffsetXEmu.Should().Be(300, "undo restores the group's original position");
        childA.OffsetXEmu.Should().Be(300, "undo restores child A's original absolute position");
        childB.OffsetXEmu.Should().Be(400, "undo restores child B's original absolute position");
    }

    // ── Z-order: BringToFront / SendToBack ────────────────────────────────────────

    [Fact]
    public void BringToFront_MovesShapeToTopOfZOrder()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(12, 0, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10); // at index 0 (back)
        session.BringToFront();

        slide.Shapes.Last().Id.Should().Be(10, "shape 10 should now be at the front (last in list)");
    }

    [Fact]
    public void SendToBack_MovesShapeToBottomOfZOrder()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(12, 0, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(12); // at index 2 (front)
        session.SendToBack();

        slide.Shapes.First().Id.Should().Be(12, "shape 12 should now be at the back (index 0)");
    }

    [Fact]
    public void BringToFront_IsUndoable()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 0, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.BringToFront();

        slide.Shapes.Last().Id.Should().Be(10);

        session.Undo();

        slide.Shapes[0].Id.Should().Be(10, "10 restored to index 0");
        slide.Shapes[1].Id.Should().Be(11);
    }

    [Fact]
    public void SendToBack_IsUndoable()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(12, 0, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(12); // front → send to back
        session.SendToBack();

        slide.Shapes[0].Id.Should().Be(12);

        session.Undo();

        slide.Shapes[2].Id.Should().Be(12, "12 restored to index 2");
    }

    // ── FF1/FF3: grpSpPr chOff/chExt PPTX XML assertions ────────────────────────

    /// <summary>
    /// FF1: The emitted grpSpPr a:xfrm must have chOff == off so that PowerPoint renders
    /// children at their stored absolute positions (identity group→child transform).
    /// Previously chOff was (0,0) which caused PowerPoint to displace every grouped shape
    /// by the group origin.
    /// </summary>
    [Fact]
    public void GroupShape_PptxXml_ChOffEqualsOff()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Clear();

        var group = new SlideShape
        {
            Id          = 1,
            Name        = "Group 1",
            Kind        = SlideShapeKind.Group,
            OffsetXEmu  = 914400,   // 1 inch
            OffsetYEmu  = 457200,   // 0.5 inch
            ExtentCxEmu = 2743200,  // 3 inches
            ExtentCyEmu = 1371600,  // 1.5 inches
        };
        group.Children.Add(MakeRect(2, 914400,  457200,  914400, 914400));
        group.Children.Add(MakeRect(3, 2286000, 457200, 1371600, 914400));
        slide.Shapes.Add(group);

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            PptxPackageWriter.Write(pres, ms);
            bytes = ms.ToArray();
        }

        // Open the zip and read the slide XML to inspect chOff/chExt.
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
        var slideEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase));
        slideEntry.Should().NotBeNull("slide XML must exist in PPTX");

        System.Xml.Linq.XDocument doc;
        using (var stream = slideEntry!.Open())
            doc = System.Xml.Linq.XDocument.Load(stream);

        System.Xml.Linq.XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        System.Xml.Linq.XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";

        // Find the p:grpSp element (the actual group, not the slide-level spTree wrapper).
        var grpSp = doc.Descendants(p + "grpSp").FirstOrDefault();
        grpSp.Should().NotBeNull("p:grpSp must be present in slide XML");

        // The group's xfrm lives in p:grpSp > p:grpSpPr > a:xfrm.
        var grpSpPr = grpSp!.Element(p + "grpSpPr")?.Element(a + "xfrm");
        grpSpPr.Should().NotBeNull("grpSpPr xfrm must be present");

        var off   = grpSpPr!.Element(a + "off");
        var ext   = grpSpPr.Element(a + "ext");
        var chOff = grpSpPr.Element(a + "chOff");
        var chExt = grpSpPr.Element(a + "chExt");

        off.Should().NotBeNull();
        ext.Should().NotBeNull();
        chOff.Should().NotBeNull("chOff must be present in grpSpPr");
        chExt.Should().NotBeNull("chExt must be present in grpSpPr");

        // FF1: chOff must equal off (identity mapping for absolute child coords).
        chOff!.Attribute("x")?.Value.Should().Be(off!.Attribute("x")?.Value,
            "chOff.x must equal off.x so PowerPoint renders children at absolute positions");
        chOff.Attribute("y")?.Value.Should().Be(off.Attribute("y")?.Value,
            "chOff.y must equal off.y so PowerPoint renders children at absolute positions");

        // FF1: chExt must equal ext.
        chExt!.Attribute("cx")?.Value.Should().Be(ext!.Attribute("cx")?.Value,
            "chExt.cx must equal ext.cx for identity child scale");
        chExt.Attribute("cy")?.Value.Should().Be(ext.Attribute("cy")?.Value,
            "chExt.cy must equal ext.cy for identity child scale");
    }

    /// <summary>
    /// FF1 + round-trip: children read back after write still carry their original absolute offsets,
    /// confirming the reader/compositor pipeline is consistent with chOff=off.
    /// </summary>
    [Fact]
    public void GroupShape_PptxRoundTrip_ChildrenRetainAbsoluteOffsets_AfterFF1Fix()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Clear();

        var group = new SlideShape
        {
            Id          = 1,
            Name        = "Group 1",
            Kind        = SlideShapeKind.Group,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1371600,
        };
        group.Children.Add(MakeRect(2, 914400,  457200,  914400, 914400));   // child A at absolute (914400, 457200)
        group.Children.Add(MakeRect(3, 2286000, 457200, 1371600, 914400));   // child B at absolute (2286000, 457200)
        slide.Shapes.Add(group);

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            PptxPackageWriter.Write(pres, ms);
            bytes = ms.ToArray();
        }

        Presentation loaded;
        using (var ms = new MemoryStream(bytes))
            loaded = PptxPackageReader.Read(ms);

        var loadedGroup = loaded.Slides[0].Shapes[0];
        var childA = loadedGroup.Children.First(c => c.Id == 2);
        var childB = loadedGroup.Children.First(c => c.Id == 3);

        // Children must read back at their original ABSOLUTE slide offsets.
        childA.OffsetXEmu.Should().Be(914400,  "child A absolute X preserved after round-trip");
        childA.OffsetYEmu.Should().Be(457200,  "child A absolute Y preserved after round-trip");
        childB.OffsetXEmu.Should().Be(2286000, "child B absolute X preserved after round-trip");
        childB.OffsetYEmu.Should().Be(457200,  "child B absolute Y preserved after round-trip");
    }

    /// <summary>
    /// FF3: A degenerate group (zero extent) must emit chExt cx/cy ≥ 1 EMU so PowerPoint
    /// does not divide by zero during rendering.
    /// </summary>
    [Fact]
    public void GroupShape_DegenerateZeroExtent_ChExtClamped()
    {
        var pres = Presentation.CreateEmpty();
        var slide = pres.Slides[0];
        slide.Shapes.Clear();

        // Degenerate group: all children at the same point → extent = (0,0).
        var group = new SlideShape
        {
            Id          = 1,
            Name        = "Degenerate",
            Kind        = SlideShapeKind.Group,
            OffsetXEmu  = 500,
            OffsetYEmu  = 500,
            ExtentCxEmu = 0,   // degenerate
            ExtentCyEmu = 0,   // degenerate
        };
        group.Children.Add(MakeRect(2, 500, 500, 0, 0));
        slide.Shapes.Add(group);

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            PptxPackageWriter.Write(pres, ms);
            bytes = ms.ToArray();
        }

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
        var slideEntry = zip.Entries.First(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase));
        System.Xml.Linq.XDocument doc;
        using (var stream = slideEntry.Open())
            doc = System.Xml.Linq.XDocument.Load(stream);

        System.Xml.Linq.XNamespace pNs = "http://schemas.openxmlformats.org/presentationml/2006/main";
        System.Xml.Linq.XNamespace a   = "http://schemas.openxmlformats.org/drawingml/2006/main";

        // Navigate to p:grpSp > p:grpSpPr > a:xfrm (the group's xfrm, not the slide spTree xfrm).
        var grpSp = doc.Descendants(pNs + "grpSp").First();
        var xfrm  = grpSp.Element(pNs + "grpSpPr")!.Element(a + "xfrm")!;
        var ext   = xfrm.Element(a + "ext");
        var chExt = xfrm.Element(a + "chExt");

        long extCx   = long.Parse(ext!.Attribute("cx")!.Value);
        long extCy   = long.Parse(ext.Attribute("cy")!.Value);
        long chExtCx = long.Parse(chExt!.Attribute("cx")!.Value);
        long chExtCy = long.Parse(chExt.Attribute("cy")!.Value);

        extCx.Should().BeGreaterThanOrEqualTo(1, "ext.cx must be ≥ 1 EMU (FF3 clamp)");
        extCy.Should().BeGreaterThanOrEqualTo(1, "ext.cy must be ≥ 1 EMU (FF3 clamp)");
        chExtCx.Should().BeGreaterThanOrEqualTo(1, "chExt.cx must be ≥ 1 EMU (FF3 clamp)");
        chExtCy.Should().BeGreaterThanOrEqualTo(1, "chExt.cy must be ≥ 1 EMU (FF3 clamp)");
    }

    // ── FF2: multi-select BringToFront / SendToBack ───────────────────────────────

    /// <summary>
    /// FF2: BringToFront with 3 selected shapes must move ALL of them to the top,
    /// preserving their relative z-order.
    /// Setup: z-order = [A(10), B(11), C(12), D(13), E(14)]; select B(11), C(12), E(14).
    /// Expected: [A(10), D(13), B(11), C(12), E(14)] — non-selected stay below, selected
    /// arrive on top preserving B<C<E relative order.
    /// </summary>
    [Fact]
    public void BringToFront_MultiSelect_AllShapesMoveToTopPreservingRelativeOrder()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100)); // z=0
        slide.Shapes.Add(MakeRect(11, 0, 0, 100, 100)); // z=1
        slide.Shapes.Add(MakeRect(12, 0, 0, 100, 100)); // z=2
        slide.Shapes.Add(MakeRect(13, 0, 0, 100, 100)); // z=3
        slide.Shapes.Add(MakeRect(14, 0, 0, 100, 100)); // z=4

        session.SelectSlide(0);
        session.Select(11);
        session.Select(12, addToSelection: true);
        session.Select(14, addToSelection: true);

        session.BringToFront();

        // Expected final order: [10, 13, 11, 12, 14]
        slide.Shapes.Select(s => s.Id).Should().Equal(new uint[] { 10, 13, 11, 12, 14 },
            "BringToFront should move all selected to top preserving their relative order");
    }

    /// <summary>
    /// FF2: SendToBack with 3 selected shapes must move ALL of them to the bottom,
    /// preserving their relative z-order.
    /// Setup: z-order = [A(10), B(11), C(12), D(13), E(14)]; select A(10), C(12), D(13).
    /// Expected: [A(10), C(12), D(13), B(11), E(14)].
    /// </summary>
    [Fact]
    public void SendToBack_MultiSelect_AllShapesMoveToBottomPreservingRelativeOrder()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100)); // z=0
        slide.Shapes.Add(MakeRect(11, 0, 0, 100, 100)); // z=1
        slide.Shapes.Add(MakeRect(12, 0, 0, 100, 100)); // z=2
        slide.Shapes.Add(MakeRect(13, 0, 0, 100, 100)); // z=3
        slide.Shapes.Add(MakeRect(14, 0, 0, 100, 100)); // z=4

        session.SelectSlide(0);
        session.Select(10);
        session.Select(12, addToSelection: true);
        session.Select(13, addToSelection: true);

        session.SendToBack();

        // Expected final order: [10, 12, 13, 11, 14]
        slide.Shapes.Select(s => s.Id).Should().Equal(new uint[] { 10, 12, 13, 11, 14 },
            "SendToBack should move all selected to bottom preserving their relative order");
    }

    /// <summary>
    /// FF2: BringToFront multi-select must be undoable in a single Undo step,
    /// restoring the original z-order of all shapes.
    /// </summary>
    [Fact]
    public void BringToFront_MultiSelect_SingleUndoRestoresOriginalOrder()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(12, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(13, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(14, 0, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(11);
        session.Select(12, addToSelection: true);
        session.Select(14, addToSelection: true);
        session.BringToFront();

        // Verify moved.
        slide.Shapes.Last().Id.Should().Be(14);

        // Single undo.
        session.Undo();

        // Original order restored.
        slide.Shapes.Select(s => s.Id).Should().Equal(new uint[] { 10, 11, 12, 13, 14 },
            "single Undo must restore all shapes to original z-order");
    }

    // ── BringForward / SendBackward (existing, sanity check) ─────────────────────

    [Fact]
    public void BringForward_IncrementsZIndex()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(12, 0, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(10);
        session.BringForward(); // 10 moves from 0 to 1

        slide.Shapes[1].Id.Should().Be(10);
    }

    [Fact]
    public void SendBackward_DecrementsZIndex()
    {
        var (pres, session) = CreateSession();
        var slide = pres.Slides[0];
        slide.Shapes.Add(MakeRect(10, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(11, 0, 0, 100, 100));
        slide.Shapes.Add(MakeRect(12, 0, 0, 100, 100));

        session.SelectSlide(0);
        session.Select(12);
        session.SendBackward(); // 12 moves from 2 to 1

        slide.Shapes[1].Id.Should().Be(12);
    }
}
