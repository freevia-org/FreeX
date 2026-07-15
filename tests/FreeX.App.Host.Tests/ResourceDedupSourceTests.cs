using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ResourceDedupSourceTests
{
    [Fact]
    public void MainWindowResources_UsesThemeResourcesForTitleBarBrush()
    {
        var source = DialogSourceTestSupport.ReadHostSources("Resources\\MainWindowResources.xaml");

        source.Should().Contain("Source=\"ThemeResources.xaml\"");
        source.Should().NotContain("<SolidColorBrush x:Key=\"FreeXTitleBarBrush\"");
    }

    [Fact]
    public void CanonicalWatchIcons_ArePackagedForBothOutputAndPublish()
    {
        var root = FindRepositoryRoot();
        var iconDirectory = Path.Combine(root, "src", "FreeX.Ribbon.Definitions", "Resources", "CommandIconsSvg");
        var project = File.ReadAllText(Path.Combine(root, "src", "FreeX.Ribbon.Definitions", "FreeX.Ribbon.Definitions.csproj"));

        foreach (var fileName in new[] { "watch-add.svg", "watch-delete.svg" })
            File.Exists(Path.Combine(iconDirectory, fileName)).Should().BeTrue();

        foreach (var alias in new[] { "add-watch.svg", "delete-watch.svg" })
            File.Exists(Path.Combine(iconDirectory, alias)).Should().BeFalse();

        project.Should().Contain("<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>");
        project.Should().Contain("<CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>");
        project.Should().Contain("Resources\\CommandIconsSvg\\**\\*.svg");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
