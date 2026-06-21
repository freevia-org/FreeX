using System;
using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class BackstagePaneDedupSourceTests
{
    [Theory]
    [InlineData("freew", "FreeW.App.Host")]
    [InlineData("freep", "FreeP.App.Host")]
    public void SisterAppBackstageViews_UseSharedPaneComposer(string appFolder, string projectFolder)
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            appFolder,
            projectFolder,
            "Backstage",
            "BackstageView.cs"));

        source.Should().Contain("BackstagePaneComposer");
        source.Should().Contain("SisterBackstageEntryBuilder.Build(");
        source.Should().Contain("Panes.BuildInfoPane(");
        source.Should().Contain("Panes.BuildRecentPane(");
        source.Should().Contain("Panes.BuildTemplatePane(");
        source.Should().Contain("Panes.BuildOptionsPane(");
        source.Should().NotContain("BackstageEntry.Pane(\"Info\"");
        source.Should().NotContain("BackstageEntry.Command(\"Save\"");
        source.Should().NotContain("TextTrimming = TextTrimming.CharacterEllipsis");
        source.Should().NotContain("Path.GetFileName(path)");
        source.Should().NotContain("var gallery = new WrapPanel");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
