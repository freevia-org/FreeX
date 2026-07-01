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

        source.Should().Contain("SlidePanePlanner.BuildEntries(_presentation.Slides, _presentation.Sections)");
        source.Should().Contain("SlidePaneEntryKind.SectionHeader");
        source.Should().Contain("BuildSlidePaneSectionHeader(entry)");
        source.Should().Contain("Text                = entry.Text");
        source.Should().Contain("Width        = SlidePanePlanner.DefaultThumbnailWidth");
        source.Should().Contain("Height       = SlidePanePlanner.DefaultThumbnailHeight");
        source.Should().Contain("Tag         = entry.SlideIndex");
        source.Should().Contain("ContextMenu = BuildSlidePaneContextMenu(entry.SlideIndex)");
        source.Should().Contain("SlidePanePlanner.BuildContextActions(_presentation.Slides.Count, slideIndex)");
        source.Should().Contain("SlidePanePlanner.TryApplyAction(Editor, action)");
        source.Should().Contain("SelectSlidePaneItem(Editor.CurrentSlideIndex)");
        source.Should().NotContain("for (int i = 0; i < _presentation.Slides.Count; i++)");
        source.Should().NotContain("Text                = $\"{slideIdx + 1}\"");
        source.Should().NotContain("Width        = 148");
        source.Should().NotContain("Height       = 84");
        source.Should().NotContain("_slidePaneList.SelectedIndex = Editor.CurrentSlideIndex");
        source.Should().NotContain("Editor.DuplicateCurrentSlide();");
        source.Should().NotContain("Editor.DeleteCurrentSlide();");
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
