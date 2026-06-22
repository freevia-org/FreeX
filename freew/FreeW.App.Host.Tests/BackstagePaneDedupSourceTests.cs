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
        source.Should().Contain("SisterBackstageTheme.");
        source.Should().Contain("SisterBackstagePaneSpecPlanner");
        source.Should().Contain("SisterBackstageEntryBuilder.Build(");
        source.Should().Contain("Panes.BuildInfoPane(");
        source.Should().Contain("BackstageCorePropertiesPlanner.Build(");
        source.Should().Contain("Panes.BuildRecentPane(");
        source.Should().Contain("PaneSpecs.BuildRecentPaneSpec(");
        source.Should().Contain("Panes.BuildTemplatePane(");
        source.Should().Contain("PaneSpecs.BuildNewPaneSpec(");
        source.Should().Contain("Panes.BuildOptionsPane(");
        source.Should().Contain("PaneSpecs.BuildOptionsPaneSpec(");

        if (appFolder == "freew")
        {
            source.Should().Contain("BuildHomePane = BuildHomePane");
            source.Should().Contain("UseNewPane = true");
            source.Should().Contain("Close = _actions.Close");
            source.Should().Contain("BackstageSaveAsFileTypePlanner.Build(");
            source.Should().Contain("_file.SaveFormats");
            source.Should().Contain("BuildOpenPane = BuildOpenPane");
            source.Should().Contain("BuildSaveAsPane = BuildSaveAsPane");
            source.Should().Contain("Panes.BuildActionPane(");
            source.Should().Contain("new BackstageActionPaneSpec(");
            source.Should().Contain("Heading: \"Home\"");
        }
        else
        {
            source.Should().NotContain("BuildHomePane = BuildHomePane");
            source.Should().NotContain("UseNewPane = true");
        }

        source.Should().NotContain("BackstageEntry.Pane(\"Info\"");
        source.Should().NotContain("BackstageEntry.Command(\"Save\"");
        source.Should().NotContain("BackstageApplicationOptionsPanePlanner.Build(");
        source.Should().NotContain("new BackstageRecentPaneSpec(");
        source.Should().NotContain("new BackstageTemplatePaneSpec(");
        source.Should().NotContain("Color.FromRgb(");
        source.Should().NotContain("BackstageAccent(");
        source.Should().NotContain("No recent documents.");
        source.Should().NotContain("No recent presentations.");
        source.Should().NotContain("Blank document");
        source.Should().NotContain("Blank presentation");
        source.Should().NotContain("new(\"Recent files kept\"");
        source.Should().NotContain("new(\"Default save format\"");
        source.Should().NotContain("new(\"UI language\"");
        source.Should().NotContain("new(\"Data folder\"");
        source.Should().NotContain("new(\"Title\"");
        source.Should().NotContain("new(\"Author\"");
        source.Should().NotContain("new(\"Subject\"");
        source.Should().NotContain("new(\"Keywords\"");
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
