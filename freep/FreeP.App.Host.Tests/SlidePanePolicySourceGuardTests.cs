using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SlidePanePolicySourceGuardTests
{
    [Fact]
    public void SlidePane_DelegatesSectionProjectionAndDragMathToPresentationPlanner()
    {
        var source = ReadHostSource("SlidePane.cs");

        source.Should().Contain("SlidePanePlanner.BuildEntries(");
        source.Should().Contain("SlidePanePlanner.HitTestInsertionPoint(");
        source.Should().Contain("SlidePanePlanner.ComputeInsertionIndicatorOffset(");
        source.Should().Contain("SlidePanePlanner.BuildContextActions(");
        source.Should().Contain("SlidePanePlanner.PlanMoveAction(");
        source.Should().Contain("SlidePanePlanner.TryApplyAction(");
        source.Should().Contain("SlidePanePlanner.NewSlideButtonText");
        source.Should().Contain("SlideSectionPlanner.BuildSlideContextActions(");
        source.Should().Contain("SlideSectionPlanner.BuildSectionHeaderActions(");
        source.Should().Contain("_editor.AddSectionAtSlide(action.SlideIndex, name)");
        source.Should().Contain("_editor.RenameSection(action.SectionIndex, name)");
        source.Should().Contain("_editor.RemoveSection(action.SectionIndex)");
        source.Should().Contain("_editor.RemoveAllSections()");
        source.Should().NotContain("new Dictionary<int, PresentationSection>");
        source.Should().NotContain("sectionHeaderBefore");
        source.Should().NotContain("const double SectionHeaderHeight");
        source.Should().NotContain("runningY + ItemHeight * 0.5");
        source.Should().NotContain("\"+ New Slide\"");
        source.Should().NotContain("\"Duplicate Slide\"");
        source.Should().NotContain("_editor.DuplicateCurrentSlide();");
        source.Should().NotContain("_editor.DeleteCurrentSlide();");
        source.Should().NotContain("_editor.MoveSlide(from, to);");
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
