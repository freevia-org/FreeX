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
        source.Should().Contain("SisterBackstagePaneResources");
        source.Should().Contain("SisterBackstageHostController");
        source.Should().Contain("new SisterBackstageHostSpec(");
        source.Should().Contain("backstage.FrameCommand(_actions.New)");
        source.Should().Contain("_backstage.HideThen");
        source.Should().Contain("Panes.BuildInfoPane(");
        source.Should().Contain("SisterBackstageInfoPanePlanner.Build(");
        source.Should().Contain("Panes.BuildRecentPane(");
        source.Should().Contain("PaneSpecs.BuildRecentPaneSpec(");
        source.Should().Contain("Panes.BuildTemplatePane(");
        source.Should().Contain("PaneSpecs.BuildNewPaneSpec(");
        source.Should().Contain("Panes.BuildOptionsPane(");
        source.Should().Contain("PaneSpecs.BuildOptionsPaneSpec(");
        source.Should().Contain("SisterBackstageAccountPanePlanner.Build(");
        source.Should().Contain("Panes.BuildAccountPane(");

        if (appFolder == "freew")
        {
            source.Should().Contain("BuildHomePane = BuildHomePane");
            source.Should().Contain("UseNewPane = true");
            source.Should().Contain("Close = backstage.FrameCommand(_actions.Close)");
            source.Should().Contain("BackstageSaveAsFileTypePlanner.Build(");
            source.Should().Contain("_file.SaveFormats");
            source.Should().Contain("BackstageExportFileTypePlanner.BuildChangeFileTypeGroup(");
            source.Should().Contain("BuildOpenPane = BuildOpenPane");
            source.Should().Contain("BackstageOpenPanePlanner.BuildPlan(");
            source.Should().Contain("_file.RecentEntries");
            source.Should().Contain("Search recent documents");
            source.Should().Contain("new TabItem { Header = \"Documents\"");
            source.Should().Contain("new TabItem { Header = \"Folders\"");
            source.Should().Contain("OpenFolder");
            source.Should().Contain("BuildSharePane = BuildSharePane");
            source.Should().Contain("BackstageSharePanePlanner.Build(");
            source.Should().Contain("OpenContainingFolder");
            source.Should().Contain("BuildSaveAsPane = BuildSaveAsPane");
            source.Should().Contain("BuildSaveAsInlineEditor");
            source.Should().Contain("SaveAsSuggested");
            source.Should().Contain("File name");
            source.Should().Contain("Save as type");
            source.Should().Contain("BuildPrintPane = BuildPrintPane");
            source.Should().Contain("BackstagePrintPanePlanner.Build(");
            source.Should().Contain("PrintPreview");
            source.Should().Contain("BuildAccountPane = BuildAccountPane");
            source.Should().Contain("HideRecentPane = true");
            source.Should().Contain("BackstageInfoSafetyPanePlanner.Build(");
            source.Should().Contain("SafetyAction(action.Kind)");
            source.Should().Contain("MarkAsFinal");
            source.Should().Contain("RestrictEditing");
            source.Should().Contain("InspectDocument");
            source.Should().Contain("CheckAccessibility");
            source.Should().Contain("Panes.BuildActionPane(");
            source.Should().Contain("new BackstageActionPaneSpec(");
            source.Should().Contain("Heading: \"Home\"");
            source.Should().Contain("BackstageHomePanePlanner.Build(");
            source.Should().Contain("_backstage.ShowPane(\"Open\")");
            source.Should().Contain("RecoverUnsaved");
        }
        else
        {
            source.Should().NotContain("BuildHomePane = BuildHomePane");
            source.Should().NotContain("UseNewPane = true");
            source.Should().Contain("BuildAccountPane = BuildAccountPane");
        }

        source.Should().NotContain("BackstageEntry.Pane(\"Info\"");
        source.Should().NotContain("BackstageEntry.Command(\"Save\"");
        source.Should().NotContain("new BackstageViewShell(");
        source.Should().NotContain("SisterBackstageEntryBuilder.Build(");
        source.Should().NotContain("Hide(); _actions");
        source.Should().NotContain("_shell.Show");
        source.Should().NotContain("BackstageCorePropertiesPlanner.Build(");
        source.Should().NotContain("BackstageApplicationOptionsPanePlanner.Build(");
        source.Should().NotContain("new BackstageRecentPaneSpec(");
        source.Should().NotContain("new BackstageTemplatePaneSpec(");
        source.Should().NotContain("new BackstageInfoPaneSpec(");
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

    [Fact]
    public void SharedSisterBackstageHostController_OwnsHostShellEntryBuilderAndActionAdapters()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "shared",
            "Free.Shared.Ribbon.Wpf",
            "SisterBackstageHostController.cs"));

        source.Should().Contain("new BackstageViewShell(");
        source.Should().Contain("SisterBackstageEntryBuilder.Build(");
        source.Should().Contain("public Action ShowPane(");
        source.Should().Contain("public Action FrameCommand(Action action)");
        source.Should().Contain("public Action HideThen(Action action)");
        source.Should().Contain("public Action<T> HideThen<T>(Action<T> action)");
        source.Should().Contain("public Action<T1, T2> HideThen<T1, T2>(Action<T1, T2> action)");
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
