using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA integration tests for Phase 4 floating-object Group/Ungroup and multi-select.
/// Covers:
///   - multi-select add/remove via SelectFloatingObject
///   - GroupSelectedFloatingObjects builds a DrawingGroup run and removes member runs
///   - UngroupSelectedFloatingObject restores individual member runs
///   - single-select path and inline objects are unaffected
/// </summary>
public sealed class DrawingGroupHostTests
{
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47];

    private static InlineImage FloatingImage(double x, double y, int z = 1) =>
        new(Png(), 60, 60)
        {
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = x,
            VerticalOffsetPt = y,
            ZOrderIndex = z
        };

    private static Shape FloatingShape(double x, double y, int z = 2) =>
        new(ShapeKind.Rectangle, 72, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = x,
                VerticalOffsetPt = y,
                ZOrderIndex = z
            }
        };

    private static TextDocument TwoMemberDoc(out InlineImage img, out Shape shp)
    {
        img = FloatingImage(36, 18, z: 1);
        shp = FloatingShape(108, 54, z: 2);
        var doc = new TextDocument();
        var p0 = new Paragraph();
        p0.Runs.Add(Run.FromImage(img));
        doc.Blocks.Add(p0);
        var p1 = new Paragraph();
        p1.Runs.Add(Run.FromShape(shp));
        doc.Blocks.Add(p1);
        return doc;
    }

    private static TextDocument NestedGroupDoc(out DrawingGroup nested, out Shape sibling)
    {
        nested = new DrawingGroup
        {
            WidthPt = 96,
            HeightPt = 48,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 1
            }
        };
        nested.Children.Add(new Shape(ShapeKind.Rectangle, 48, 24));
        nested.Children.Add(new Shape(ShapeKind.Ellipse, 36, 24));
        nested.ChildOffsets.Add((0, 0));
        nested.ChildOffsets.Add((60, 24));

        sibling = FloatingShape(156, 54, z: 2);
        var doc = new TextDocument();
        doc.Blocks.Clear();
        var p0 = new Paragraph();
        p0.Runs.Add(Run.FromDrawingGroup(nested));
        doc.Blocks.Add(p0);
        var p1 = new Paragraph();
        p1.Runs.Add(Run.FromShape(sibling));
        doc.Blocks.Add(p1);
        return doc;
    }

    private static TextDocument NestedChildDoc(
        out DrawingGroup outer,
        out DrawingGroup inner,
        out Shape leaf)
    {
        inner = new DrawingGroup
        {
            WidthPt = 126,
            HeightPt = 72,
            RotationAngle = -16,
            FlipV = true
        };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 36, 22));
        inner.ChildOffsets.Add((10, 8));
        leaf = new Shape(ShapeKind.Ellipse, 44, 28);
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((58, 30));

        outer = new DrawingGroup
        {
            WidthPt = 252,
            HeightPt = 144,
            RotationAngle = 24,
            FlipH = true,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 72,
                VerticalOffsetPt = 36,
                ZOrderIndex = 4
            }
        };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((28, 22));
        outer.Children.Add(new Shape(ShapeKind.Rectangle, 54, 34));
        outer.ChildOffsets.Add((168, 76));

        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static TextDocument ChartSmartArtGroupDoc(
        out DrawingGroup outer,
        out Chart chart,
        out SmartArt smartArt)
    {
        chart = Chart.Create(ChartKind.Column, ["A", "B"], [1, 2]);
        smartArt = SmartArt.Create(SmartArtKind.Process, ["Step"]);
        var inner = new DrawingGroup { WidthPt = 120, HeightPt = 72 };
        inner.Children.Add(smartArt);
        inner.ChildOffsets.Add((12, 8));

        outer = new DrawingGroup
        {
            WidthPt = 300,
            HeightPt = 180,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 24,
                ZOrderIndex = 1
            }
        };
        outer.Children.Add(chart);
        outer.ChildOffsets.Add((18, 14));
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((156, 54));

        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        document.Blocks.Add(paragraph);
        return document;
    }

    // ── Multi-select management ──────────────────────────────────────────────────────────────────

    [StaFact]
    public void SelectFloatingObject_SingleSelect_SetsOneItem()
    {
        var doc = TwoMemberDoc(out var img, out var shp);
        var view = new DocumentView();
        view.LoadModel(doc);

        view.SelectFloatingImage(img);

        view.SelectedFloatingObjects.Should().ContainSingle();
        view.SelectedFloatingObjects[0].Should().BeSameAs(img);
        view.HasMultipleFloatingObjectsSelected.Should().BeFalse();
    }

    [StaFact]
    public void SelectFloatingObject_MultiSelect_AddsBoth()
    {
        var doc = TwoMemberDoc(out var img, out var shp);
        var view = new DocumentView();
        view.LoadModel(doc);

        view.SelectFloatingImage(img);
        view.SelectFloatingObject(shp, addToMultiSelect: true);

        view.SelectedFloatingObjects.Should().HaveCount(2);
        view.HasMultipleFloatingObjectsSelected.Should().BeTrue();
    }

    [StaFact]
    public void SelectFloatingObject_MultiSelectRemove_RemovesAlreadySelected()
    {
        var doc = TwoMemberDoc(out var img, out var shp);
        var view = new DocumentView();
        view.LoadModel(doc);

        view.SelectFloatingImage(img);
        view.SelectFloatingObject(shp, addToMultiSelect: true);
        // Ctrl-clicking an already-selected item should deselect it.
        view.SelectFloatingObject(shp, addToMultiSelect: true);

        view.SelectedFloatingObjects.Should().ContainSingle();
        view.SelectedFloatingObjects[0].Should().BeSameAs(img);
    }

    [StaFact]
    public void SelectFloatingObject_SingleClickClearsPrior_MultiSelect()
    {
        var doc = TwoMemberDoc(out var img, out var shp);
        var view = new DocumentView();
        view.LoadModel(doc);

        view.SelectFloatingImage(img);
        view.SelectFloatingObject(shp, addToMultiSelect: true);
        // Plain single-click on shape: should replace the set.
        view.SelectFloatingObject(shp, addToMultiSelect: false);

        view.SelectedFloatingObjects.Should().ContainSingle();
        view.SelectedFloatingObjects[0].Should().BeSameAs(shp);
        view.HasMultipleFloatingObjectsSelected.Should().BeFalse();
    }

    // ── GroupSelectedFloatingObjects ─────────────────────────────────────────────────────────────

    [StaFact]
    public void GroupSelected_TwoMembers_CreatesGroupRun()
    {
        var doc = TwoMemberDoc(out var img, out var shp);
        var view = new DocumentView();
        view.LoadModel(doc);
        view.SelectFloatingImage(img);
        view.SelectFloatingObject(shp, addToMultiSelect: true);

        view.GroupSelectedFloatingObjects();
        view.CommitToModel();
        var recovered = view.Model;

        // First paragraph should hold the group run.
        var p0 = (Paragraph)recovered.Blocks[0];
        p0.Runs.Should().ContainSingle();
        p0.Runs[0].DrawingGroup.Should().NotBeNull("group command must produce a DrawingGroup run");
        p0.Runs[0].DrawingGroup!.Children.Should().HaveCount(2);
    }

    [StaFact]
    public void GroupSelected_ClearsMultiSelect()
    {
        var doc = TwoMemberDoc(out var img, out var shp);
        var view = new DocumentView();
        view.LoadModel(doc);
        view.SelectFloatingImage(img);
        view.SelectFloatingObject(shp, addToMultiSelect: true);

        view.GroupSelectedFloatingObjects();

        view.SelectedFloatingObjects.Should().BeEmpty("group command clears multi-select");
        view.HasMultipleFloatingObjectsSelected.Should().BeFalse();
    }

    // ── UngroupSelectedFloatingObject ────────────────────────────────────────────────────────────

    [StaFact]
    public void UngroupSelected_RestoresBothMembers()
    {
        var doc = TwoMemberDoc(out var img, out var shp);
        var view = new DocumentView();
        view.LoadModel(doc);
        view.SelectFloatingImage(img);
        view.SelectFloatingObject(shp, addToMultiSelect: true);
        view.GroupSelectedFloatingObjects();

        // Now select the group and ungroup.
        view.CommitToModel();
        // Reload so we can reference the newly created group.
        view.LoadModel(view.Model);
        var grpRun = ((Paragraph)view.Model.Blocks[0]).Runs.First(r => r.DrawingGroup is not null);
        view.SelectFloatingObject(grpRun.DrawingGroup!);
        view.IsGroupSelected.Should().BeTrue("group should be selected before ungroup");

        view.UngroupSelectedFloatingObject();
        view.CommitToModel();
        var recovered = view.Model;

        var p0 = (Paragraph)recovered.Blocks[0];
        p0.Runs.Should().HaveCount(2, "two individual member runs should be restored");
        p0.Runs.Any(r => r.Image is not null).Should().BeTrue();
        p0.Runs.Any(r => r.Shape is not null).Should().BeTrue();
    }

    [StaFact]
    public void NestedGroup_CanBeGroupedUngroupedAndUndoneThroughWpfHost()
    {
        var doc = NestedGroupDoc(out var nested, out var sibling);
        var view = new DocumentView();
        view.LoadModel(doc);
        view.SelectFloatingObject(nested);
        view.SelectFloatingObject(sibling, addToMultiSelect: true);
        view.HasMultipleFloatingObjectsSelected.Should().BeTrue();

        view.GroupSelectedFloatingObjects();
        view.CommitToModel();
        var outer = ((Paragraph)view.Model.Blocks[0]).Runs[0].DrawingGroup!;
        outer.Children.Should().HaveCount(2);
        outer.Children[0].Should().BeSameAs(nested);

        view.LoadModel(view.Model);
        var outerReloaded = ((Paragraph)view.Model.Blocks[0]).Runs[0].DrawingGroup!;
        view.SelectFloatingObject(outerReloaded);
        view.IsGroupSelected.Should().BeTrue();
        view.UngroupSelectedFloatingObject();

        var restoredNested = ((Paragraph)view.Model.Blocks[0]).Runs[0].DrawingGroup!;
        restoredNested.Placement.HorizontalOffsetPt.Should().BeApproximately(36, 0.5);
        restoredNested.Placement.VerticalOffsetPt.Should().BeApproximately(18, 0.5);

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().ContainSingle();
        ((Paragraph)view.Model.Blocks[0]).Runs[0].DrawingGroup!.Children.Should().HaveCount(2);
    }

    [StaFact]
    public void SelectedGroupChild_MoveAndResizeUseSharedLocalCommands()
    {
        var doc = NestedGroupDoc(out var nested, out _);
        var view = new DocumentView();
        view.LoadModel(doc);
        view.SelectFloatingObject(nested);
        view.SelectFloatingGroupChild(nested, 1);
        view.SelectedFloatingGroupChild.Should().NotBeNull();
        view.SelectedFloatingGroupChild!.Value.ChildIndex.Should().Be(1);

        var child = nested.Children[1].Should().BeOfType<Shape>().Subject;
        var groupWidthBefore = nested.WidthPt;
        var groupHeightBefore = nested.HeightPt;
        var offsetBefore = nested.ChildOffsets[1];
        var widthBefore = child.WidthPt;

        view.MoveSelectedFloatingGroupChild(18, 12).Should().BeTrue();
        view.ResizeSelectedFloatingGroupChild(54, 30).Should().BeTrue();
        view.CommitToModel();

        nested.ChildOffsets[1].Should().Be((offsetBefore.X + 18, offsetBefore.Y + 12));
        child.WidthPt.Should().Be(54);
        child.HeightPt.Should().Be(30);
        child.WidthPt.Should().NotBe(widthBefore);
        nested.WidthPt.Should().Be(groupWidthBefore);
        nested.HeightPt.Should().Be(groupHeightBefore);

        view.Undo();
        view.Undo();
        nested.ChildOffsets[1].Should().Be(offsetBefore);
        child.WidthPt.Should().Be(widthBefore);
    }

    [StaFact]
    public void NestedGroupChild_MoveResizeUndoThroughWpfHost_KeepBothOwningGroupsUnchanged()
    {
        var doc = NestedChildDoc(out var outer, out var inner, out var leaf);
        var view = new DocumentView();
        view.LoadModel(doc);
        view.SelectFloatingObject(outer);
        view.SelectFloatingGroupChild(outer, [0, 1]);

        view.SelectedFloatingGroupChildPath.Should().Equal([0, 1]);
        var outerSize = (outer.WidthPt, outer.HeightPt);
        var innerSize = (inner.WidthPt, inner.HeightPt);
        var offsetBefore = inner.ChildOffsets[1];
        var sizeBefore = (leaf.WidthPt, leaf.HeightPt);

        view.MoveSelectedFloatingGroupChild(21, 13).Should().BeTrue();
        view.ResizeSelectedFloatingGroupChild(82, 52).Should().BeTrue();
        view.CommitToModel();

        inner.ChildOffsets[1].Should().Be((offsetBefore.X + 21, offsetBefore.Y + 13));
        (leaf.WidthPt, leaf.HeightPt).Should().Be((82, 52));
        (outer.WidthPt, outer.HeightPt).Should().Be(outerSize);
        (inner.WidthPt, inner.HeightPt).Should().Be(innerSize);

        view.Undo();
        view.Undo();
        inner.ChildOffsets[1].Should().Be(offsetBefore);
        (leaf.WidthPt, leaf.HeightPt).Should().Be(sizeBefore);
        (outer.WidthPt, outer.HeightPt).Should().Be(outerSize);
        (inner.WidthPt, inner.HeightPt).Should().Be(innerSize);
    }

    [StaFact]
    public void NestedGroupShape_FormattingRoutesTargetLeafAndUndoThroughWpfHost()
    {
        var doc = NestedChildDoc(out var outer, out var inner, out var leaf);
        var sibling = (Shape)outer.Children[1];
        var outerPosition = (
            outer.Placement.HorizontalOffsetPt,
            outer.Placement.VerticalOffsetPt,
            outer.Placement.HorizontalAnchor,
            outer.Placement.VerticalAnchor);
        leaf.FillColorHex = "#111111";
        sibling.FillColorHex = "#222222";
        var view = new DocumentView();
        view.LoadModel(doc);
        view.SelectFloatingObject(outer);
        view.SelectFloatingGroupChild(outer, [0, 1]);

        view.GetSelectedShapePosition().Should().Be((58d, 30d,
            HorizontalAnchor.Column, VerticalAnchor.Paragraph, true));
        view.SetSelectedShapePosition(75, 41, HorizontalAnchor.Page, VerticalAnchor.Page);
        view.SetSelectedShapeSize(80, 50);
        view.SetSelectedShapeKind(ShapeKind.RoundedRectangle);
        view.SetSelectedShapeAltText(" Nested leaf ");
        view.SetSelectedShapeFill("#ABCDEF");
        view.SetSelectedShapeOutline("#123456", 2, "dash");

        inner.ChildOffsets[1].Should().Be((75, 41));
        (outer.Placement.HorizontalOffsetPt,
            outer.Placement.VerticalOffsetPt,
            outer.Placement.HorizontalAnchor,
            outer.Placement.VerticalAnchor).Should().Be(outerPosition);
        (leaf.WidthPt, leaf.HeightPt).Should().Be((80, 50));
        leaf.Kind.Should().Be(ShapeKind.RoundedRectangle);
        leaf.AltText.Should().Be("Nested leaf");
        leaf.FillColorHex.Should().Be("#ABCDEF");
        leaf.OutlineColorHex.Should().Be("#123456");
        sibling.Kind.Should().Be(ShapeKind.Rectangle);
        sibling.AltText.Should().BeNull();
        sibling.FillColorHex.Should().Be("#222222");
        sibling.OutlineColorHex.Should().BeNull();
        view.Undo();
        leaf.OutlineColorHex.Should().BeNull();
        view.Undo();
        leaf.FillColorHex.Should().Be("#111111");
        view.Undo();
        leaf.AltText.Should().BeNull();
        view.Undo();
        leaf.Kind.Should().Be(ShapeKind.Ellipse);
        view.Undo();
        (leaf.WidthPt, leaf.HeightPt).Should().Be((44, 28));
        view.Undo();
        inner.ChildOffsets[1].Should().Be((58, 30));
        (outer.Placement.HorizontalOffsetPt,
            outer.Placement.VerticalOffsetPt,
            outer.Placement.HorizontalAnchor,
            outer.Placement.VerticalAnchor).Should().Be(outerPosition);
    }

    [StaFact]
    public void WpfRibbon_RotatesAndFlipsDirectGroupedChartChild_AndUndoRedoRestoresIt()
    {
        var document = ChartSmartArtGroupDoc(out var group, out var chart, out _);
        var view = new DocumentView();
        view.LoadModel(document);
        view.SelectFloatingObject(group);
        view.SelectFloatingGroupChild(group, [0]);
        view.SelectedChart().Should().BeSameAs(chart);

        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet("freew.shape-rotate-right90", out var rotate).Should().BeTrue();
        registry.TryGet("freew.shape-flip-horizontal", out var flip).Should().BeTrue();

        rotate!.Execute(RibbonCommandContext.Empty);
        flip!.Execute(RibbonCommandContext.Empty);
        (chart.RotationAngle, chart.FlipH, chart.FlipV).Should().Be((90, true, false));

        view.Undo();
        (chart.RotationAngle, chart.FlipH, chart.FlipV).Should().Be((90, false, false));
        view.Undo();
        (chart.RotationAngle, chart.FlipH, chart.FlipV).Should().Be((0, false, false));
        view.Redo();
        view.Redo();
        (chart.RotationAngle, chart.FlipH, chart.FlipV).Should().Be((90, true, false));
    }

    [StaFact]
    public void WpfRibbon_RotatesAndFlipsNestedGroupedSmartArtChild_AndUndoRedoRestoresIt()
    {
        var document = ChartSmartArtGroupDoc(out var group, out _, out var smartArt);
        var view = new DocumentView();
        view.LoadModel(document);
        view.SelectFloatingObject(group);
        view.SelectFloatingGroupChild(group, [1, 0]);
        view.SelectedSmartArt().Should().BeSameAs(smartArt);

        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet("freew.shape-rotate-left90", out var rotate).Should().BeTrue();
        registry.TryGet("freew.shape-flip-vertical", out var flip).Should().BeTrue();

        rotate!.Execute(RibbonCommandContext.Empty);
        flip!.Execute(RibbonCommandContext.Empty);
        (smartArt.RotationAngle, smartArt.FlipH, smartArt.FlipV).Should().Be((270, false, true));

        view.Undo();
        (smartArt.RotationAngle, smartArt.FlipH, smartArt.FlipV).Should().Be((270, false, false));
        view.Undo();
        (smartArt.RotationAngle, smartArt.FlipH, smartArt.FlipV).Should().Be((0, false, false));
        view.Redo();
        view.Redo();
        (smartArt.RotationAngle, smartArt.FlipH, smartArt.FlipV).Should().Be((270, false, true));
    }

    // ── IsGroupSelected ──────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void IsGroupSelected_FalseWhenImageSelected()
    {
        var doc = TwoMemberDoc(out var img, out _);
        var view = new DocumentView();
        view.LoadModel(doc);

        view.SelectFloatingImage(img);

        view.IsGroupSelected.Should().BeFalse();
    }

    // ── Inline objects unaffected ────────────────────────────────────────────────────────────────

    [StaFact]
    public void InlineShape_NotAffectedByGroupPath()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(new Shape(ShapeKind.Ellipse, 60, 30)));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);
        view.CommitToModel();
        var recovered = view.Model;

        var s = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.Shape is not null).Shape!;
        s.IsFloating.Should().BeFalse("inline shapes must not be treated as floating");
    }
}
