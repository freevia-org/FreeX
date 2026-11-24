namespace FreeP.App.Compositor.Tests;

public sealed class PresentationViewModeLayoutPlannerTests
{
    [Theory]
    [InlineData(PresentationViewMode.Normal, true, true)]
    [InlineData(PresentationViewMode.Normal, false, false)]
    [InlineData(PresentationViewMode.Outline, true, true)]
    [InlineData(PresentationViewMode.NotesPage, false, true)]
    [InlineData(PresentationViewMode.SlideSorter, true, false)]
    [InlineData(PresentationViewMode.SlideMaster, true, false)]
    public void Notes_pane_visibility_follows_the_view_mode(PresentationViewMode mode, bool showNotes, bool expected)
    {
        var state = new PresentationViewModeState(mode);

        PresentationViewModeLayoutPlanner.IsNotesPaneVisible(state, showNotes).Should().Be(expected);
        PresentationViewModeLayoutPlanner.Plan(state, showNotes).NotesPaneVisible.Should().Be(expected);
    }

    [Fact]
    public void Slide_sorter_fills_the_pane_column_and_hides_the_canvas()
    {
        var plan = PresentationViewModeLayoutPlanner.Plan(
            new PresentationViewModeState(PresentationViewMode.SlideSorter),
            showNotesPane: true);

        plan.PaneColumn.Should().Be(PresentationViewModeColumnSize.Fill);
        plan.CanvasColumn.Should().Be(PresentationViewModeColumnSize.Hidden);
        plan.SlidePaneFillsWidth.Should().BeTrue();
        plan.CanvasHostVisible.Should().BeFalse();
    }

    [Fact]
    public void Notes_page_hides_the_pane_column_and_uses_the_page_surface()
    {
        var plan = PresentationViewModeLayoutPlanner.Plan(
            new PresentationViewModeState(PresentationViewMode.NotesPage),
            showNotesPane: false);

        plan.PaneColumn.Should().Be(PresentationViewModeColumnSize.Hidden);
        plan.CanvasColumn.Should().Be(PresentationViewModeColumnSize.Fill);
        plan.NotesPageSurface.Should().BeTrue();
        plan.CanvasHostVisible.Should().BeTrue();
    }

    [Fact]
    public void Normal_view_keeps_the_auto_sized_pane()
    {
        var plan = PresentationViewModeLayoutPlanner.Plan(PresentationViewModeState.Normal, showNotesPane: true);

        plan.PaneColumn.Should().Be(PresentationViewModeColumnSize.Auto);
        plan.SlidePaneFillsWidth.Should().BeFalse();
    }

    [Theory]
    [InlineData("FreeP.App.Host")]
    [InlineData("FreeP.App.Avalonia")]
    public void Hosts_apply_view_mode_layout_from_the_shared_plan(string project)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freep", project, "MainWindow.cs"));

        source.Should().Contain("PresentationViewModeLayoutPlanner.Plan(state, _viewShowState.ShowNotesPane)");
        source.Should().Contain("PresentationViewModeLayoutPlanner.IsNotesPaneVisible(");
        source.Should().NotContain("isSlideSorter || isNotesPage ? double.NaN");
    }
}
