using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SlidePanePolicySourceGuardTests
{
    [Fact]
    public void SlidePane_DelegatesSectionProjectionAndDragMathToPresentationPlanner()
    {
        var source = ReadHostSource("SlidePane.cs");

        source.Should().Contain("SlidePanePlanner.BuildEntries(");
        source.Should().Contain("SlidePanePlanner.BuildThumbnailVisualPlan(");
        source.Should().Contain("_collapsedSectionIds");
        source.Should().Contain("ToggleSection(entry.SectionId)");
        source.Should().Contain("SlidePanePlanner.HitTestInsertionPoint(");
        source.Should().Contain("SlidePanePlanner.BuildDropVisualPlan(");
        source.Should().Contain("SlidePanePlanner.DefaultDragStartThreshold");
        source.Should().Contain("SlidePanePlanner.DefaultDropIndicatorThickness");
        source.Should().Contain("SlidePanePlanner.DefaultDropIndicatorAccentHex");
        source.Should().Contain("SlidePanePlanner.BuildContextActions(");
        source.Should().Contain("SlidePanePlanner.PlanMoveAction(");
        source.Should().Contain("SlidePanePlanner.TryApplyAction(");
        source.Should().Contain("SlidePanePlanner.NewSlideButtonText");
        source.Should().Contain("Width            = plan.ThumbnailWidth");
        source.Should().Contain("Height           = plan.ThumbnailHeight");
        source.Should().Contain("ToolTip         = plan.ToolTipText");
        source.Should().Contain("SlideSectionPlanner.BuildSlideContextActions(");
        source.Should().Contain("SlideSectionPlanner.BuildSectionHeaderActions(");
        source.Should().Contain("SlideSectionPlanner.BuildExecutionPlan(action)");
        source.Should().Contain("SlideSectionPlanner.TryApplyAction(_editor, execution, promptedName)");
        source.Should().NotContain("private const double ThumbWidth");
        source.Should().NotContain("private const double ThumbHeight");
        source.Should().NotContain("private const double ItemPadding");
        source.Should().NotContain("private const double LabelHeight");
        source.Should().NotContain("new Dictionary<int, PresentationSection>");
        source.Should().NotContain("sectionHeaderBefore");
        source.Should().NotContain("const double SectionHeaderHeight");
        source.Should().NotContain("runningY + ItemHeight * 0.5");
        source.Should().NotContain("Math.Abs(pos.Y - _dragStartPoint.Y) < 5");
        source.Should().NotContain("new Thickness(0, indicatorY - 1, 0, 0)");
        source.Should().NotContain("\"+ New Slide\"");
        source.Should().NotContain("\"Duplicate Slide\"");
        source.Should().NotContain("_editor.DuplicateCurrentSlide();");
        source.Should().NotContain("_editor.DeleteCurrentSlide();");
        source.Should().NotContain("_editor.MoveSlide(from, to);");
        source.Should().NotContain("_editor.AddSectionAtSlide(action.SlideIndex");
        source.Should().NotContain("_editor.RenameSection(action.SectionIndex");
        source.Should().NotContain("_editor.RemoveSection(action.SectionIndex)");
        source.Should().NotContain("_editor.RemoveAllSections()");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "freep", "FreeP.App.Host", fileName);
        return File.ReadAllText(path);
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
