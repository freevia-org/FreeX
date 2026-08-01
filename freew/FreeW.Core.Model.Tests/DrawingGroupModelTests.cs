namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit tests for <see cref="DrawingGroup"/> model construction, the
/// <see cref="GroupFloatingObjectsCommand"/>, and <see cref="UngroupFloatingObjectsCommand"/>
/// (Phase 4: floating object Group/Ungroup).
/// </summary>
public sealed class DrawingGroupModelTests
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

    private static DrawingGroup NestedGroup(double x, double y, int z = 3)
    {
        var group = new DrawingGroup
        {
            WidthPt = 96,
            HeightPt = 48,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = x,
                VerticalOffsetPt = y,
                ZOrderIndex = z
            }
        };
        group.Children.Add(new Shape(ShapeKind.Rectangle, 48, 24));
        group.Children.Add(new Shape(ShapeKind.Ellipse, 36, 24));
        group.ChildOffsets.Add((0, 0));
        group.ChildOffsets.Add((60, 24));
        return group;
    }

    /// <summary>
    /// Build a document with two floating objects (image + shape) in two paragraphs,
    /// execute GroupFloatingObjectsCommand, and assert the group run is placed at the
    /// first member's paragraph/run position.
    /// </summary>
    private static (TextDocument doc, DocumentCommandBus bus) TwoMemberDoc()
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();

        var p0 = new Paragraph();
        p0.Runs.Add(Run.FromImage(FloatingImage(36, 18, z: 1)));
        doc.Blocks.Add(p0);

        var p1 = new Paragraph();
        p1.Runs.Add(Run.FromShape(FloatingShape(108, 54, z: 2)));
        doc.Blocks.Add(p1);

        var bus = new DocumentCommandBus(new TestCtx(doc));
        return (doc, bus);
    }

    // ── DrawingGroup model ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void DrawingGroup_DefaultsAreValid()
    {
        var grp = new DrawingGroup();
        grp.WidthPt.Should().BeGreaterThan(0);
        grp.HeightPt.Should().BeGreaterThan(0);
        grp.Placement.Wrapping.Should().Be(ImageWrapping.Square);
        grp.Children.Should().BeEmpty();
        grp.IsFloating.Should().BeTrue();
    }

    [Fact]
    public void DrawingGroup_IsValidRequiresTwoChildren()
    {
        var grp = new DrawingGroup();
        grp.IsValid.Should().BeFalse("empty group");

        grp.Children.Add(new Shape(ShapeKind.Ellipse, 60, 30));
        grp.IsValid.Should().BeFalse("single-child group");

        grp.Children.Add(new Shape(ShapeKind.Rectangle, 72, 36));
        grp.IsValid.Should().BeTrue("two-child group");
    }

    [Fact]
    public void SetFloatingRotationCommand_UpdatesAndRevertsGroupTransform()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var group = new DrawingGroup();
        group.Children.Add(new Shape(ShapeKind.Rectangle, 60, 30));
        group.Children.Add(new Shape(ShapeKind.Ellipse, 72, 36));
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        doc.Blocks.Add(paragraph);
        var command = new SetFloatingRotationCommand(0, 0, 45, flipH: true, flipV: true);
        var context = new TestCtx(doc);

        command.Apply(context);

        group.RotationAngle.Should().Be(45);
        group.FlipH.Should().BeTrue();
        group.FlipV.Should().BeTrue();

        command.Revert(context);

        group.RotationAngle.Should().Be(0);
        group.FlipH.Should().BeFalse();
        group.FlipV.Should().BeFalse();
    }

    [Fact]
    public void SetDrawingGroupChildRotationCommand_UpdatesAndRevertsOnlyTheChild()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var group = new DrawingGroup();
        var first = new Shape(ShapeKind.Rectangle, 60, 30);
        var second = new Shape(ShapeKind.Ellipse, 72, 36);
        group.Children.Add(first);
        group.Children.Add(second);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        doc.Blocks.Add(paragraph);
        var command = new SetDrawingGroupChildRotationCommand(0, 0, 1, 30, flipH: true, flipV: false);
        var context = new TestCtx(doc);

        command.Apply(context);

        first.RotationAngle.Should().Be(0);
        second.RotationAngle.Should().Be(30);
        second.FlipH.Should().BeTrue();

        command.Revert(context);

        first.RotationAngle.Should().Be(0);
        second.RotationAngle.Should().Be(0);
        second.FlipH.Should().BeFalse();
    }

    [Fact]
    public void SetDrawingGroupChildRotationCommand_RotatesNestedChartAndSmartArt_and_round_trips_undo_redo()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var group = new DrawingGroup();
        var chart = Chart.Create(ChartKind.Column, ["A"], [1]);
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Step"]);
        group.Children.Add(chart);
        group.Children.Add(smartArt);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        doc.Blocks.Add(paragraph);
        var bus = new DocumentCommandBus(new TestCtx(doc));

        bus.Execute(new SetDrawingGroupChildRotationCommand(0, 0, 0, 37, flipH: true, flipV: false));
        bus.Execute(new SetDrawingGroupChildRotationCommand(0, 0, 1, -19, flipH: false, flipV: true));

        (chart.RotationAngle, chart.FlipH, chart.FlipV).Should().Be((37, true, false));
        (smartArt.RotationAngle, smartArt.FlipH, smartArt.FlipV).Should().Be((-19, false, true));
        bus.Undo().Should().BeTrue();
        (smartArt.RotationAngle, smartArt.FlipH, smartArt.FlipV).Should().Be((0, false, false));
        bus.Redo().Should().BeTrue();
        (smartArt.RotationAngle, smartArt.FlipH, smartArt.FlipV).Should().Be((-19, false, true));
    }

    [Fact]
    public void SetDrawingGroupChildPositionCommand_PersistsLocalOffsetAndUndoes()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var group = new DrawingGroup();
        group.Children.Add(new Shape(ShapeKind.Rectangle, 60, 30));
        group.Children.Add(new Shape(ShapeKind.Ellipse, 72, 36));
        group.ChildOffsets.Add((12, 18));
        group.ChildOffsets.Add((72, 36));
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        doc.Blocks.Add(paragraph);
        var context = new TestCtx(doc);
        var command = new SetDrawingGroupChildPositionCommand(0, 0, 1, 96, 54);

        command.Apply(context);

        group.ChildOffsets[0].Should().Be((12, 18));
        group.ChildOffsets[1].Should().Be((96, 54));

        command.Revert(context);

        group.ChildOffsets[1].Should().Be((72, 36));
    }

    [Fact]
    public void ChangeDrawingGroupChildZOrderCommand_ReordersNestedChildWithOffsetAndUndoRedo()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var sibling = new Shape(ShapeKind.Rectangle, 36, 22);
        var leaf = new Shape(ShapeKind.Ellipse, 44, 28);
        var inner = new DrawingGroup();
        inner.Children.Add(sibling);
        inner.ChildOffsets.Add((10, 8));
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((58, 30));
        var outer = new DrawingGroup();
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((28, 22));
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        document.Blocks.Add(paragraph);
        var bus = new DocumentCommandBus(new TestCtx(document));

        bus.Execute(new ChangeDrawingGroupChildZOrderCommand(
            0, 0, [0, 1], ZOrderOperation.SendBackward));

        inner.Children.Should().Equal(leaf, sibling);
        inner.ChildOffsets.Should().Equal((58, 30), (10, 8));
        DrawingGroupChildPathResolver.TryFindPath(outer, leaf, out var movedPath).Should().BeTrue();
        movedPath.Should().Equal(0, 0);
        bus.Undo().Should().BeTrue();
        inner.Children.Should().Equal(sibling, leaf);
        inner.ChildOffsets.Should().Equal((10, 8), (58, 30));
        bus.Redo().Should().BeTrue();
        inner.Children.Should().Equal(leaf, sibling);
        inner.ChildOffsets.Should().Equal((58, 30), (10, 8));
    }

    [Fact]
    public void SetDrawingGroupChildSizeCommand_PersistsShapeSizeAndUndoesWithoutChangingGroup()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var group = new DrawingGroup { WidthPt = 180, HeightPt = 96 };
        var first = new Shape(ShapeKind.Rectangle, 60, 30);
        var second = new Shape(ShapeKind.Ellipse, 72, 36);
        group.Children.Add(first);
        group.Children.Add(second);
        group.ChildOffsets.Add((0, 0));
        group.ChildOffsets.Add((72, 36));
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        doc.Blocks.Add(paragraph);
        var context = new TestCtx(doc);
        var command = new SetDrawingGroupChildSizeCommand(0, 0, 1, 108, 54);

        command.Apply(context);

        second.WidthPt.Should().Be(108);
        second.HeightPt.Should().Be(54);
        group.WidthPt.Should().Be(180);
        group.HeightPt.Should().Be(96);

        command.Revert(context);

        second.WidthPt.Should().Be(72);
        second.HeightPt.Should().Be(36);
    }

    [Fact]
    public void NestedChildPathCommands_EditLeafOnlyAndUndoWithoutChangingEitherOwningGroup()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var inner = new DrawingGroup
        {
            WidthPt = 120,
            HeightPt = 64,
            RotationAngle = -18,
            FlipV = true
        };
        var leaf = new Shape(ShapeKind.Ellipse, 42, 24);
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 30, 18));
        inner.ChildOffsets.Add((8, 6));
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((54, 26));

        var outer = new DrawingGroup
        {
            WidthPt = 240,
            HeightPt = 144,
            RotationAngle = 27,
            FlipH = true
        };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((24, 18));
        outer.Children.Add(new Shape(ShapeKind.Rectangle, 48, 30));
        outer.ChildOffsets.Add((156, 72));

        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        doc.Blocks.Add(paragraph);
        var context = new TestCtx(doc);
        var path = new[] { 0, 1 };
        var outerSize = (outer.WidthPt, outer.HeightPt);
        var innerSize = (inner.WidthPt, inner.HeightPt);
        var offsetBefore = inner.ChildOffsets[1];
        var sizeBefore = (leaf.WidthPt, leaf.HeightPt);

        var position = new SetDrawingGroupChildPositionCommand(0, 0, path, 82, 44);
        var size = new SetDrawingGroupChildSizeCommand(0, 0, path, 78, 46);
        position.Apply(context);
        size.Apply(context);

        inner.ChildOffsets[1].Should().Be((82, 44));
        (leaf.WidthPt, leaf.HeightPt).Should().Be((78, 46));
        (outer.WidthPt, outer.HeightPt).Should().Be(outerSize);
        (inner.WidthPt, inner.HeightPt).Should().Be(innerSize);

        size.Revert(context);
        position.Revert(context);

        inner.ChildOffsets[1].Should().Be(offsetBefore);
        (leaf.WidthPt, leaf.HeightPt).Should().Be(sizeBefore);
        (outer.WidthPt, outer.HeightPt).Should().Be(outerSize);
        (inner.WidthPt, inner.HeightPt).Should().Be(innerSize);
    }

    [Fact]
    public void NestedShapeFormattingCommands_TargetLeafAndUndoWithoutChangingSibling()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var leaf = new Shape(ShapeKind.Ellipse, 42, 24) { FillColorHex = "#111111" };
        var sibling = new Shape(ShapeKind.Rectangle, 30, 18) { FillColorHex = "#222222" };
        var inner = new DrawingGroup();
        inner.Children.Add(sibling);
        inner.ChildOffsets.Add((0, 0));
        inner.Children.Add(leaf);
        inner.ChildOffsets.Add((36, 18));
        var outer = new DrawingGroup();
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((12, 8));
        outer.Children.Add(new Shape(ShapeKind.Rectangle, 24, 16));
        outer.ChildOffsets.Add((96, 48));
        doc.Blocks.Add(new Paragraph { Runs = { Run.FromDrawingGroup(outer) } });
        var context = new TestCtx(doc);
        var path = new[] { 0, 1 };
        var fill = ShapeFill.Patterned("diagCross", "#ABCDEF", "#FFFFFF");
        var effects = new ShapeEffectLst { HasGlow = true, GlowColorHex = "ABCDEF" };
        var style = ShapeStylePreset.Catalog[1];

        var kindCommand = new SetShapeKindCommand(0, 0, ShapeKind.RoundedRectangle, path);
        kindCommand.Apply(context);
        leaf.Kind.Should().Be(ShapeKind.RoundedRectangle);
        kindCommand.Revert(context);
        leaf.Kind.Should().Be(ShapeKind.Ellipse);

        var altTextCommand = new SetShapeAltTextCommand(0, 0, "Nested leaf", path);
        altTextCommand.Apply(context);
        leaf.AltText.Should().Be("Nested leaf");
        altTextCommand.Revert(context);
        leaf.AltText.Should().BeNull();

        var solidCommand = new SetShapeFillCommand(0, 0, "#334455", path);
        solidCommand.Apply(context);
        leaf.FillColorHex.Should().Be("#334455");
        solidCommand.Revert(context);
        leaf.FillColorHex.Should().Be("#111111");

        var outlineCommand = new SetShapeOutlineCommand(0, 0, "#556677", 2.5, "dash", path);
        outlineCommand.Apply(context);
        (leaf.OutlineColorHex, leaf.OutlineWidthPt, leaf.OutlineDash)
            .Should().Be(("#556677", 2.5, "dash"));
        outlineCommand.Revert(context);

        var extendedFillCommand = new SetShapeExtendedFillCommand(0, 0, fill, path);
        extendedFillCommand.Apply(context);
        leaf.ExtendedFill.Should().BeSameAs(fill);
        extendedFillCommand.Revert(context);

        var effectsCommand = new SetShapeEffectsCommand(0, 0, effects, path);
        effectsCommand.Apply(context);
        leaf.Effects.Should().BeSameAs(effects);
        effectsCommand.Revert(context);

        var styleCommand = new ApplyShapeStyleCommand(0, 0, style, path);
        styleCommand.Apply(context);
        leaf.FillColorHex.Should().Be(style.FillColorHex);
        styleCommand.Revert(context);

        sibling.FillColorHex.Should().Be("#222222");
        sibling.Kind.Should().Be(ShapeKind.Rectangle);
        sibling.AltText.Should().BeNull();
        sibling.OutlineColorHex.Should().BeNull();
        sibling.ExtendedFill.Should().BeNull();
        sibling.Effects.Should().BeNull();
    }

    // ── GroupFloatingObjectsCommand ──────────────────────────────────────────────────────────────

    [Fact]
    public void Group_TwoMembers_CreatesGroupRun()
    {
        var (doc, bus) = TwoMemberDoc();
        bus.Execute(new GroupFloatingObjectsCommand([(0, 0), (1, 0)]));

        // After grouping: first paragraph should contain the group run; second's run removed.
        var p0 = (Paragraph)doc.Blocks[0];
        var p1 = (Paragraph)doc.Blocks[1];

        p0.Runs.Should().ContainSingle();
        p0.Runs[0].DrawingGroup.Should().NotBeNull();
        p0.Runs[0].DrawingGroup!.IsValid.Should().BeTrue();
        p1.Runs.Should().BeEmpty("member run should have been removed and replaced by the group");
    }

    [Fact]
    public void Group_TwoMembers_ChildrenPreserved()
    {
        var (doc, bus) = TwoMemberDoc();
        bus.Execute(new GroupFloatingObjectsCommand([(0, 0), (1, 0)]));

        var grp = ((Paragraph)doc.Blocks[0]).Runs[0].DrawingGroup!;
        grp.Children.Should().HaveCount(2);
        grp.Children[0].Should().BeOfType<InlineImage>();
        grp.Children[1].Should().BeOfType<Shape>();
    }

    [Fact]
    public void Group_PlacementOriginIsMinBoundingCorner()
    {
        var (doc, bus) = TwoMemberDoc();
        // image at (36,18), shape at (108,54)
        bus.Execute(new GroupFloatingObjectsCommand([(0, 0), (1, 0)]));

        var grp = ((Paragraph)doc.Blocks[0]).Runs[0].DrawingGroup!;
        // Group origin should be at the minimum of the two positions.
        grp.Placement.HorizontalOffsetPt.Should().BeApproximately(36, 0.5);
        grp.Placement.VerticalOffsetPt.Should().BeApproximately(18, 0.5);
    }

    [Fact]
    public void Group_ChildOffsetsAreRelativeToGroupOrigin()
    {
        var (doc, bus) = TwoMemberDoc();
        // image at (36,18), shape at (108,54)
        bus.Execute(new GroupFloatingObjectsCommand([(0, 0), (1, 0)]));

        var grp = ((Paragraph)doc.Blocks[0]).Runs[0].DrawingGroup!;
        grp.ChildOffsets.Should().HaveCount(2);
        grp.ChildOffsets[0].X.Should().BeApproximately(0, 0.5,   "image is at group origin → offset 0");
        grp.ChildOffsets[0].Y.Should().BeApproximately(0, 0.5);
        grp.ChildOffsets[1].X.Should().BeApproximately(72, 0.5,  "shape is 72 pts to the right of image");
        grp.ChildOffsets[1].Y.Should().BeApproximately(36, 0.5,  "shape is 36 pts below the image");
    }

    [Fact]
    public void Group_ValidNestedGroup_IsPreservedAsAChild()
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        var nested = NestedGroup(36, 18);
        var p0 = new Paragraph();
        p0.Runs.Add(Run.FromDrawingGroup(nested));
        doc.Blocks.Add(p0);
        var p1 = new Paragraph();
        p1.Runs.Add(Run.FromShape(FloatingShape(156, 54)));
        doc.Blocks.Add(p1);
        var bus = new DocumentCommandBus(new TestCtx(doc));

        bus.Execute(new GroupFloatingObjectsCommand([(0, 0), (1, 0)]));

        var outer = ((Paragraph)doc.Blocks[0]).Runs[0].DrawingGroup!;
        outer.Children.Should().HaveCount(2);
        outer.Children[0].Should().BeSameAs(nested);
        outer.ChildOffsets[0].X.Should().BeApproximately(0, 0.5);
        outer.ChildOffsets[0].Y.Should().BeApproximately(0, 0.5);

        bus.Undo();
        ((Paragraph)doc.Blocks[0]).Runs[0].DrawingGroup.Should().BeSameAs(nested);
        nested.Placement.HorizontalOffsetPt.Should().BeApproximately(36, 0.5);
    }

    // ── GroupFloatingObjectsCommand.Revert ───────────────────────────────────────────────────────

    [Fact]
    public void Group_Revert_RestoresBothMembers()
    {
        var (doc, bus) = TwoMemberDoc();
        bus.Execute(new GroupFloatingObjectsCommand([(0, 0), (1, 0)]));
        bus.Undo();

        var p0 = (Paragraph)doc.Blocks[0];
        var p1 = (Paragraph)doc.Blocks[1];
        p0.Runs.Should().ContainSingle();
        p1.Runs.Should().ContainSingle();
        p0.Runs[0].Image.Should().NotBeNull();
        p1.Runs[0].Shape.Should().NotBeNull();
    }

    [Fact]
    public void Group_Revert_RestoresPlacementsAndZOrder()
    {
        var (doc, bus) = TwoMemberDoc();
        bus.Execute(new GroupFloatingObjectsCommand([(0, 0), (1, 0)]));
        bus.Undo();

        var img = ((Paragraph)doc.Blocks[0]).Runs[0].Image!;
        var shp = ((Paragraph)doc.Blocks[1]).Runs[0].Shape!;
        img.HorizontalOffsetPt.Should().BeApproximately(36, 0.5);
        img.VerticalOffsetPt.Should().BeApproximately(18, 0.5);
        img.ZOrderIndex.Should().Be(1);
        shp.Placement!.HorizontalOffsetPt.Should().BeApproximately(108, 0.5);
        shp.Placement.VerticalOffsetPt.Should().BeApproximately(54, 0.5);
        shp.Placement.ZOrderIndex.Should().Be(2);
    }

    // ── UngroupFloatingObjectsCommand ────────────────────────────────────────────────────────────

    [Fact]
    public void Ungroup_RestoresBothMembers()
    {
        var (doc, bus) = TwoMemberDoc();
        bus.Execute(new GroupFloatingObjectsCommand([(0, 0), (1, 0)]));
        bus.Execute(new UngroupFloatingObjectsCommand(0, 0));

        var p0 = (Paragraph)doc.Blocks[0];
        p0.Runs.Should().HaveCount(2);
        p0.Runs.Any(r => r.Image is not null).Should().BeTrue();
        p0.Runs.Any(r => r.Shape is not null).Should().BeTrue();
    }

    [Fact]
    public void Ungroup_RestoresAbsoluteOffsets()
    {
        var (doc, bus) = TwoMemberDoc();
        bus.Execute(new GroupFloatingObjectsCommand([(0, 0), (1, 0)]));
        bus.Execute(new UngroupFloatingObjectsCommand(0, 0));

        var p0 = (Paragraph)doc.Blocks[0];
        var img = p0.Runs.First(r => r.Image is not null).Image!;
        var shp = p0.Runs.First(r => r.Shape is not null).Shape!;
        img.HorizontalOffsetPt.Should().BeApproximately(36, 0.5);
        img.VerticalOffsetPt.Should().BeApproximately(18, 0.5);
        shp.Placement!.HorizontalOffsetPt.Should().BeApproximately(108, 0.5);
        shp.Placement.VerticalOffsetPt.Should().BeApproximately(54, 0.5);
    }

    [Fact]
    public void Ungroup_RestoresNestedGroupAsFloatingRunWithAbsolutePlacement()
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        var nested = NestedGroup(36, 18);
        var p0 = new Paragraph();
        p0.Runs.Add(Run.FromDrawingGroup(nested));
        doc.Blocks.Add(p0);
        var p1 = new Paragraph();
        p1.Runs.Add(Run.FromShape(FloatingShape(156, 54)));
        doc.Blocks.Add(p1);
        var bus = new DocumentCommandBus(new TestCtx(doc));

        bus.Execute(new GroupFloatingObjectsCommand([(0, 0), (1, 0)]));
        bus.Execute(new UngroupFloatingObjectsCommand(0, 0));

        var runs = ((Paragraph)doc.Blocks[0]).Runs;
        runs.Should().HaveCount(2);
        var restoredNested = runs.Single(run => run.DrawingGroup is not null).DrawingGroup!;
        restoredNested.Should().BeSameAs(nested);
        restoredNested.Placement.HorizontalOffsetPt.Should().BeApproximately(36, 0.5);
        restoredNested.Placement.VerticalOffsetPt.Should().BeApproximately(18, 0.5);
        restoredNested.Placement.ZOrderIndex.Should().Be(3);

        bus.Undo();
        ((Paragraph)doc.Blocks[0]).Runs.Should().ContainSingle();
        ((Paragraph)doc.Blocks[0]).Runs[0].DrawingGroup!.Children.Should().HaveCount(2);
    }

    [Fact]
    public void Ungroup_Revert_RestoresGroup()
    {
        var (doc, bus) = TwoMemberDoc();
        bus.Execute(new GroupFloatingObjectsCommand([(0, 0), (1, 0)]));
        bus.Execute(new UngroupFloatingObjectsCommand(0, 0));
        bus.Undo();

        var p0 = (Paragraph)doc.Blocks[0];
        p0.Runs.Should().ContainSingle();
        p0.Runs[0].DrawingGroup.Should().NotBeNull();
        p0.Runs[0].DrawingGroup!.Children.Should().HaveCount(2);
    }

    // ── TestContext ──────────────────────────────────────────────────────────────────────────────

    private sealed class TestCtx(TextDocument doc) : IDocumentCommandContext
    {
        public TextDocument Document => doc;
    }
}
