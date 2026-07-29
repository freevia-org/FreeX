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
