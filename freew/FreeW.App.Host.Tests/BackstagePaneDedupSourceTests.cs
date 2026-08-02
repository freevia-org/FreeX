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
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            appFolder,
            projectFolder,
            "Backstage",
            "BackstageView.cs"));

        source.Should().Contain("BackstagePaneComposer");
        source.Should().Contain("SisterBackstageTheme.");
        source.Should().Contain("SisterBackstagePaneSpecPlanner");
        source.Should().Contain("SisterBackstagePaneResources");
        source.Should().Contain("SisterBackstagePaneResources.ForApp(");
        source.Should().Contain($"SisterBackstageAppKind.{(appFolder == "freew" ? "FreeW" : "FreeP")}");
        source.Should().Contain("BackstageStrings.Current.Get");
        source.Should().Contain("SisterBackstageHostController");
        source.Should().Contain("new SisterBackstageHostSpec(");
        source.Should().Contain("Chrome = BackstageRibbonChrome.Create()");
        source.Should().Contain("public void Show() => _backstage.Show();");
        source.Should().Contain("public void Hide() => _backstage.Hide();");
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
        source.Should().Contain("Panes.BuildAccountPane(");

        if (appFolder == "freew")
        {
            source.Should().Contain("BuildHomePane = BuildHomePane");
            source.Should().Contain("UseNewPane = true");
            source.Should().Contain("Close = backstage.FrameCommand(_actions.Close)");
            source.Should().Contain("BackstagePaneSurfacePlanner.BuildHomePane(");
            source.Should().Contain("var metrics = surface.VisualMetrics");
            source.Should().Contain("HomeActionRow(action, metrics)");
            source.Should().Contain("AutomationProperties.NameProperty");
            source.Should().Contain("return Kit.Scroll(panel)");
            source.Should().Contain("BackstagePaneSurfacePlanner.BuildOpenPane(");
            source.Should().Contain("BackstagePaneSurfacePlanner.BuildSaveAsPane(");
            source.Should().Contain("BackstagePaneSurfacePlanner.BuildSharePane(");
            source.Should().Contain("BackstagePaneSurfacePlanner.BuildExportPane(");
            source.Should().Contain("BackstageExportPaneSurfaceText.FromDescriptor(");
            source.Should().Contain("_file.SaveFormats");
            source.Should().Contain("BuildOpenPane = BuildOpenPane");
            source.Should().Contain("BuildOpenSurface(");
            source.Should().Contain("_file.RecentEntries");
            source.Should().Contain("surface.Search.AutomationName");
            source.Should().Contain("surface.Tabs.DocumentsTabLabel");
            source.Should().Contain("surface.Tabs.FoldersTabLabel");
            source.Should().Contain("OpenFolder");
            source.Should().Contain("BuildSharePane = BuildSharePane");
            source.Should().Contain("OpenContainingFolder");
            source.Should().Contain("BuildSaveAsPane = BuildSaveAsPane");
            source.Should().Contain("BuildSaveAsInlineEditor");
            source.Should().Contain("SaveAsSuggested");
            source.Should().Contain("inline.FileNameHeading");
            source.Should().Contain("inline.SaveAsTypeHeading");
            source.Should().Contain("BuildPrintPane = BuildPrintPane");
            source.Should().Contain("BackstagePaneSurfacePlanner.BuildPrintPane(");
            source.Should().Contain("SurfaceActionRow(action)");
            source.Should().Contain("BuildPrintEvidenceSection(surface.Evidence)");
            source.Should().Contain("BackstageViewTextResources.EvidenceSection");
            source.Should().Contain("BackstageViewTextResources.EvidenceRequirementsLabel");
            source.Should().Contain("FormatPrintEvidenceRequirement");
            source.Should().Contain("PrintEvidence_");
            source.Should().Contain("PrintPreview");
            source.Should().Contain("BuildAccountPane = BuildAccountPane");
            source.Should().Contain("BackstagePaneSurfacePlanner.BuildAccountPane(");
            source.Should().Contain("ToAccountPaneSpec(surface)");
            source.Should().Contain("HideRecentPane = true");
            source.Should().Contain("BackstagePaneSurfacePlanner.BuildInfoPane(");
            source.Should().Contain("document: model");
            source.Should().Contain("ToActionGroups(safetySurface.SafetyGroups)");
            source.Should().Contain("MarkAsFinal");
            source.Should().Contain("RestrictEditing");
            source.Should().Contain("InspectDocument");
            source.Should().Contain("CheckAccessibility");
            source.Should().Contain("Panes.BuildExportActionPane(");
            source.Should().Contain("ToActionPaneSpec(surface)");
            source.Should().Contain("_backstage.ShowPane(\"Open\")");
            source.Should().Contain("RecoverUnsaved");
        }
        else
        {
            source.Should().NotContain("BuildHomePane = BuildHomePane");
            source.Should().NotContain("UseNewPane = true");
            source.Should().Contain("BuildAccountPane = BuildAccountPane");
            source.Should().Contain("PaneSpecs.BuildAccountPaneSpec(");
            source.Should().Contain("PaneSpecs.BuildExportPaneSpec(");
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
        source.Should().NotContain("new BackstageAccountPaneSpec(");
        source.Should().NotContain("SisterBackstagePaneTextSpec.FreeW");
        source.Should().NotContain("SisterBackstagePaneTextSpec.FreeP");
        source.Should().NotContain("SisterBackstageAccountPanePlanner.Build(");
        source.Should().NotContain("BackstagePrintPanePlanner.Build(");
        source.Should().NotContain("BackstageInfoSafetyPanePlanner.Build(");
        source.Should().NotContain("Color.FromRgb(");
        source.Should().NotContain("BackstageAccent(");
        source.Should().NotContain("No recent documents.");
        source.Should().NotContain("No recent presentations.");
        source.Should().NotContain("Blank document");
        source.Should().NotContain("Blank presentation");
        source.Should().NotContain("Create PDF/XPS Document");
        source.Should().NotContain("Create PDF Copy");
        source.Should().NotContain("Create PDF or XPS");
        source.Should().NotContain("Export to PDF...");
        source.Should().NotContain("Publish a fixed-layout copy");
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
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "shared",
            "Free.Shared.Shell.Wpf",
            "SisterBackstageHostController.cs"));

        source.Should().Contain("new BackstageViewShell(");
        source.Should().Contain("SisterBackstageEntryBuilder.Build(");
        source.Should().Contain("public Action ShowPane(");
        source.Should().Contain("public Action FrameCommand(Action action)");
        source.Should().Contain("public Action HideThen(Action action)");
        source.Should().Contain("public Action<T> HideThen<T>(Action<T> action)");
        source.Should().Contain("public Action<T1, T2> HideThen<T1, T2>(Action<T1, T2> action)");

        File.Exists(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "shared",
            "Free.Shared.Ribbon.Wpf",
            "SisterBackstageHostController.cs")).Should().BeFalse();
    }

}
