namespace FreeP.App.Compositor.Tests;

public sealed class SelectionAdornerDrawPlannerTests
{
    [Fact]
    public void Single_selection_draws_one_box_with_handles_then_geometry_handles()
    {
        var state = new SelectionAdornerState();
        state.UpdateSelection([new SelectionAdornerSelectionPlan(1, new SelectionAdornerRect(10, 20, 30, 40))]);

        var ops = SelectionAdornerDrawPlanner.Plan(state);

        ops.Select(op => op.Kind).Should().Equal(
            SelectionAdornerDrawOpKind.SelectionRect,
            SelectionAdornerDrawOpKind.GeometryHandles);
        ops[0].DrawHandles.Should().BeTrue();
        ops[0].Rect.Should().Be(new SelectionAdornerRect(10, 20, 30, 40));
    }

    [Fact]
    public void Multi_selection_draws_member_boxes_without_handles_then_a_group_box_with_handles()
    {
        var state = new SelectionAdornerState();
        state.UpdateSelection(
        [
            new SelectionAdornerSelectionPlan(1, new SelectionAdornerRect(0, 0, 10, 10)),
            new SelectionAdornerSelectionPlan(2, new SelectionAdornerRect(20, 20, 10, 10)),
        ]);

        var ops = SelectionAdornerDrawPlanner.Plan(state);

        ops.Where(op => op.Kind == SelectionAdornerDrawOpKind.SelectionRect)
            .Select(op => op.DrawHandles)
            .Should().Equal(false, false, true);
        ops[2].Rect.Should().Be(new SelectionAdornerRect(0, 0, 30, 30));
    }

    [Fact]
    public void Marquee_and_previews_keep_the_shared_order()
    {
        var state = new SelectionAdornerState();
        state.UpdateMarquee(new SelectionAdornerRect(1, 2, 3, 4));
        state.UpdatePreview(new SelectionAdornerRect(5, 6, 7, 8), 15);

        var ops = SelectionAdornerDrawPlanner.Plan(state);

        ops.Select(op => op.Kind).Should().Equal(
            SelectionAdornerDrawOpKind.Marquee,
            SelectionAdornerDrawOpKind.GeometryHandles,
            SelectionAdornerDrawOpKind.PreviewRect);
        ops[^1].RotationDeg.Should().Be(15);
    }

    [Theory]
    [InlineData("FreeP.App.Rendering.Wpf", "SelectionAdorner.cs")]
    [InlineData("FreeP.App.Rendering.Avalonia", "SelectionAdornerLayer.cs")]
    public void Native_adorners_paint_from_the_shared_plan_and_style_table(string project, string file)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freep", project, file));

        source.Should().Contain("SelectionAdornerDrawPlanner.Plan(State)");
        source.Should().Contain("SelectionAdornerStyle.Selection");
        source.Should().NotContain("0x21, 0x96, 0xF3");
        source.Should().NotContain("0xE9, 0x1E, 0x63");
        source.Should().NotContain("if (State.Selections.Count == 1)");
    }
}
