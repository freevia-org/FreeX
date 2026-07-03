using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlidePanePolicySourceGuardTests
{
    [Fact]
    public void MainWindow_DelegatesSlidePaneProjectionToPresentationPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));

        source.Should().Contain("SlidePanePlanner.BuildEntries(");
        source.Should().Contain("SlidePanePlanner.BuildThumbnailVisualPlan(");
        source.Should().Contain("_slidePaneCollapsedSectionIds");
        source.Should().Contain("SlidePaneEntryKind.SectionHeader");
        source.Should().Contain("BuildSlidePaneSectionHeader(entry)");
        source.Should().Contain("Text              = entry.Text");
        source.Should().Contain("ToggleSlidePaneSection(entry.SectionId)");
        source.Should().Contain("ContextMenu = BuildSlidePaneSectionContextMenu(entry)");
        source.Should().Contain("Width        = plan.ThumbnailWidth");
        source.Should().Contain("Height       = plan.ThumbnailHeight");
        source.Should().Contain("Tag         = plan.SlideIndex");
        source.Should().Contain("IsSelected  = plan.IsSelected");
        source.Should().Contain("ToolTip.SetTip(item, plan.ToolTipText)");
        source.Should().Contain("ContextMenu = BuildSlidePaneContextMenu(plan.SlideIndex)");
        source.Should().Contain("Content                    = SlidePanePlanner.NewSlideButtonText");
        source.Should().Contain("button.Click += (_, _) => InsertSlideFromSlidePaneAffordance();");
        source.Should().Contain("slidePaneHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });");
        source.Should().Contain("slidePaneListHost.Children.Add(_slidePaneList);");
        source.Should().Contain("slidePaneHost.Children.Add(_slidePaneNewSlideButton);");
        source.Should().Contain("SlidePanePlanner.BuildContextActions(_presentation.Slides.Count, slideIndex)");
        source.Should().Contain("SlidePanePlanner.TryApplyAction(Editor, action)");
        source.Should().Contain("SlideSectionPlanner.BuildSlideContextActions(");
        source.Should().Contain("SlideSectionPlanner.BuildSectionHeaderActions(");
        source.Should().Contain("SlideSectionPlanner.BuildExecutionPlan(action)");
        source.Should().Contain("SlideSectionPlanner.TryApplyAction(Editor, execution, promptedName)");
        source.Should().Contain("PointerPressed += OnSlidePaneItemPointerPressed");
        source.Should().Contain("PointerMoved += OnSlidePaneItemPointerMoved");
        source.Should().Contain("PointerReleased += OnSlidePaneItemPointerReleased");
        source.Should().Contain("SlidePanePlanner.HitTestInsertionPoint(");
        source.Should().Contain("SlidePanePlanner.BuildDropVisualPlan(");
        source.Should().Contain("SlidePanePlanner.DefaultDragStartThreshold");
        source.Should().Contain("SlidePanePlanner.DefaultSlideItemHeight");
        source.Should().Contain("SlidePanePlanner.DefaultDropIndicatorThickness");
        source.Should().Contain("SlidePanePlanner.DefaultDropIndicatorAccentHex");
        source.Should().Contain("SlidePanePlanner.PlanMoveAction(");
        source.Should().Contain("private void InsertSlideFromSlidePaneAffordance()");
        source.Should().Contain("SelectSlidePaneItem(Editor.CurrentSlideIndex)");
        source.Should().NotContain("SlidePaneAvaloniaSlideItemHeight");
        source.Should().NotContain("for (int i = 0; i < _presentation.Slides.Count; i++)");
        source.Should().NotContain("Text                = $\"{slideIdx + 1}\"");
        source.Should().NotContain("Width        = 148");
        source.Should().NotContain("Height       = 84");
        source.Should().NotContain("Math.Abs(itemPosition.Y - _slidePaneDragStartPoint.Y) < 5");
        source.Should().NotContain("new Thickness(0, indicatorY - 1, 0, 0)");
        source.Should().NotContain("_slidePaneList.SelectedIndex = Editor.CurrentSlideIndex");
        source.Should().NotContain("Editor.DuplicateCurrentSlide();");
        source.Should().NotContain("Editor.DeleteCurrentSlide();");
        source.Should().NotContain("Editor.MoveSlide(");
        source.Should().NotContain("Editor.AddSectionAtSlide(action.SlideIndex");
        source.Should().NotContain("Editor.RenameSection(action.SectionIndex");
        source.Should().NotContain("Editor.RemoveSection(action.SectionIndex)");
        source.Should().NotContain("Editor.RemoveAllSections()");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
