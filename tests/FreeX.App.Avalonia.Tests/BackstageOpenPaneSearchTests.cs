using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class BackstageOpenPaneSearchTests
{
    [Fact]
    public void OpenPane_UsesLocalizedSearchAndFiltersRecentRows()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.LiveBackstage.cs"));
        var start = source.IndexOf("private Control BuildLiveBackstageOpenPane()", StringComparison.Ordinal);
        var end = source.IndexOf("// WPF \"Blank workbook\" card", start, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        var openPane = source[start..end];

        openPane.Should().Contain("BackstageOpenSearchBox");
        openPane.Should().Contain("LiveBackstageOpenPanePlan.SearchAutomationNameKey");
        openPane.Should().Contain("_liveBackstageOpenFilter");
        openPane.Should().Contain("BackstageRecentFileListPlanner.Build(");
        openPane.Should().Contain("FreeXBackstageOpenPanePlanner.ResolveEmptyStateKey(");
        openPane.Should().Contain("BackstageOpenEmptyState");
    }

    [Fact]
    public void WpfOpenPane_UsesAccessibleSearchAndFiltersRecentRows()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "MainWindow.BackstageFrame.cs"));

        source.Should().Contain("BackstageOpenSearchBox");
        source.Should().Contain("UiText.Get(plan.SearchAutomationNameKey)");
        source.Should().Contain("_backstageOpenRecentFilter = filter;");
        source.Should().Contain("BackstageRecentFileListPlanner.Build(");
        source.Should().Contain("FreeXBackstageOpenPanePlanner.ResolveEmptyStateKey(");
        source.Should().Contain("BackstageOpenEmptyState");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
