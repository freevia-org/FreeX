namespace FreeP.App.Compositor.Tests;

public sealed class TableCellPointerPlannerTests
{
    private const double EmuPerDip = 9525.0;

    [Fact]
    public void Press_off_any_shape_commits_an_open_cell_edit()
    {
        var (presentation, slide) = TablePresentation();

        var plan = TableCellPointerPlanner.Plan(slide, presentation, 900, 500, clickCount: 1);

        plan.Action.Should().Be(TableCellPointerAction.CommitCellEdit);
        plan.HitCell.Should().BeFalse();
    }

    [Fact]
    public void Press_on_a_non_table_shape_commits_an_open_cell_edit()
    {
        var (presentation, slide) = TablePresentation();
        slide.Shapes.Add(new SlideShape
        {
            Id = 8,
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = (long)(500 * EmuPerDip),
            OffsetYEmu = (long)(300 * EmuPerDip),
            ExtentCxEmu = (long)(100 * EmuPerDip),
            ExtentCyEmu = (long)(100 * EmuPerDip),
        });

        var plan = TableCellPointerPlanner.Plan(slide, presentation, 520, 320, clickCount: 1);

        plan.Action.Should().Be(TableCellPointerAction.CommitCellEdit);
    }

    [Fact]
    public void Single_press_on_a_cell_selects_it_and_double_press_opens_the_editor()
    {
        var (presentation, slide) = TablePresentation();

        var single = TableCellPointerPlanner.Plan(slide, presentation, 150, 120, clickCount: 1);
        var dbl = TableCellPointerPlanner.Plan(slide, presentation, 150, 120, clickCount: 2);

        single.Action.Should().Be(TableCellPointerAction.SelectCell);
        single.ShapeId.Should().Be(42);
        single.HitCell.Should().BeTrue();
        dbl.Action.Should().Be(TableCellPointerAction.ActivateCellEdit);
        (dbl.Row, dbl.Col).Should().Be((single.Row, single.Col));
    }

    [Fact]
    public void Highlight_plan_uses_the_shared_style_for_the_active_cell()
    {
        var (_, slide) = TablePresentation();

        var highlight = TableCellPointerPlanner.PlanHighlight(
            slide,
            [42],
            (0, 0),
            new SlideTransformCore(1, 0, 0, 960, 540));

        highlight.Should().NotBeNull();
        highlight!.StrokeArgb.Should().Be(TableCellHighlightPlan.DefaultStrokeArgb);
        highlight.Placement.Width.Should().BeGreaterThan(0);
        TableCellPointerPlanner.PlanHighlight(slide, [], null, new SlideTransformCore(1, 0, 0, 960, 540))
            .Should().BeNull();
    }

    [Theory]
    [InlineData("FreeP.App.Rendering.Wpf", "InCanvasTableCellEditor.cs")]
    [InlineData("FreeP.App.Rendering.Avalonia", "AvaloniaInCanvasTextEditor.cs")]
    public void Native_editors_route_pointer_presses_and_highlight_through_the_shared_planner(
        string project,
        string file)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freep", project, file));

        source.Should().Contain("TableCellPointerPlanner.Plan(");
        source.Should().Contain("TableCellPointerPlanner.PlanHighlight(");
        source.Should().NotContain("TableCellHitTester.HitTest(");
        source.Should().NotContain("0x21, 0x96, 0xF3");
    }

    private static (Presentation Presentation, Slide Slide) TablePresentation()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();

        var table = new TableShape();
        table.ColumnWidthsEmu.Add((long)(96 * EmuPerDip));
        table.ColumnWidthsEmu.Add((long)(96 * EmuPerDip));
        for (var r = 0; r < 2; r++)
        {
            var row = new TableRow { HeightEmu = (long)(48 * EmuPerDip) };
            row.Cells.Add(new TableCell { TextBody = new TextBody() });
            row.Cells.Add(new TableCell { TextBody = new TextBody() });
            table.Rows.Add(row);
        }

        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Table,
            OffsetXEmu = (long)(100 * EmuPerDip),
            OffsetYEmu = (long)(100 * EmuPerDip),
            ExtentCxEmu = (long)(192 * EmuPerDip),
            ExtentCyEmu = (long)(96 * EmuPerDip),
            Table = table,
        });
        return (presentation, slide);
    }
}
