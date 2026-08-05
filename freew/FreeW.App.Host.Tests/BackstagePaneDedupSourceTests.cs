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
        source.Should().Contain("SisterBackstagePaneResources");
        source.Should().Contain("SisterBackstagePaneResources.ForApp(");
        source.Should().Contain($"SisterBackstageAppKind.{(appFolder == "freew" ? "FreeW" : "FreeP")}");
        source.Should().Contain("BackstageStrings.Current.Get");
        source.Should().Contain("SisterBackstageHostController");
        source.Should().Contain("new SisterBackstageHostSpec(");
        source.Should().Contain("Chrome = BackstageRibbonChrome.Create()");
        source.Should().Contain("public void Show() => _backstage.Show();");
        source.Should().Contain("public void Hide() => _backstage.Hide();");
        if (appFolder == "freew")
        {
            source.Should().Contain("new FreeWBackstageSession(");
            source.Should().Contain("new FreeWBackstageActionBinder(");
            source.Should().Contain("SisterBackstagePaneSpecPlanner");
            source.Should().Contain("Panes.BuildInfoPane(");
            source.Should().Contain("_session.BuildInfoPane()");
            source.Should().Contain("Panes.BuildRecentPane(");
            source.Should().Contain("_session.BuildRecentPaneSpec(PaneSpecs)");
            source.Should().Contain("Panes.BuildTemplatePane(");
            source.Should().Contain("_session.BuildNewPaneSpec(PaneSpecs)");
            source.Should().Contain("Panes.BuildOptionsPane(");
            source.Should().Contain("_session.BuildOptionsPaneSpec(PaneSpecs)");
            source.Should().Contain("BuildHomePane = BuildHomePane");
            source.Should().Contain("UseNewPane = true");
            source.Should().Contain("Close = backstage.FrameCommand(_callbacks.CloseDocument)");
            source.Should().Contain("_session.BuildHomePane(");
            source.Should().Contain("var metrics = surface.VisualMetrics");
            source.Should().Contain("HomeActionRow(action, metrics)");
            source.Should().Contain("AutomationProperties.NameProperty");
            source.Should().Contain("return Kit.Scroll(panel)");
            source.Should().Contain("_session.BuildOpenPane(");
            source.Should().Contain("_session.BuildSaveAsPane(");
            source.Should().Contain("_session.BuildSharePane(");
            source.Should().Contain("_session.BuildExportPane(");
            source.Should().Contain("BackstageExportPaneSurfaceText.FromDescriptor(");
            source.Should().Contain("BuildOpenPane = BuildOpenPane");
            source.Should().Contain("BuildOpenSurface(");
            source.Should().Contain("surface.Search.AutomationName");
            source.Should().Contain("surface.Tabs.DocumentsTabLabel");
            source.Should().Contain("surface.Tabs.FoldersTabLabel");
            source.Should().Contain("BuildSharePane = BuildSharePane");
            source.Should().Contain("BuildSaveAsPane = BuildSaveAsPane");
            source.Should().Contain("BuildSaveAsInlineEditor");
            source.Should().Contain("_session.SaveInline(");
            source.Should().Contain("inline.FileNameHeading");
            source.Should().Contain("inline.SaveAsTypeHeading");
            source.Should().Contain("BuildPrintPane = BuildPrintPane");
            source.Should().Contain("_session.BuildPrintPane(");
            source.Should().Contain("SurfaceActionRow(action)");
            source.Should().Contain("BuildPrintEvidenceSection(surface.Evidence)");
            source.Should().Contain("BackstagePrintEvidenceTextFormatter.Format(row)");
            source.Should().Contain("BackstageViewTextResources.EvidenceSection");
            source.Should().Contain("PrintEvidence_");
            source.Should().Contain("BuildAccountPane = BuildAccountPane");
            source.Should().Contain("_session.BuildAccountPane(");
            source.Should().Contain("BackstagePaneRenderer.BuildAccountPane(Kit, surface)");
            source.Should().Contain("HideRecentPane = true");
            source.Should().Contain("BackstagePaneRenderer.BuildActionPane(Kit, surface)");
            source.Should().Contain("_backstage.ShowPane(\"Open\")");
            source.Should().NotContain("BackstagePaneSurfacePlanner.Build");
            source.Should().NotContain("SisterBackstageInfoPanePlanner.Build(");
            source.Should().NotContain("PrintEvidenceKindLabel(");
            source.Should().NotContain("PrintEvidenceStatusLabel(");
            source.Should().NotContain("FormatPrintEvidenceRequirement(");
        }
        else
        {
            source.Should().Contain("backstage.FrameCommand(_actions.New)");
            source.Should().Contain("_backstage.HideThen");
            source.Should().NotContain("BuildHomePane = BuildHomePane");
            source.Should().NotContain("UseNewPane = true");
            source.Should().Contain("BuildAccountPane = BuildAccountPane");
            source.Should().Contain("PresentationBackstagePanePlanner");
            source.Should().Contain("PresentationBackstagePrintSession");
            source.Should().NotContain("PresentationBackstagePrintSurfacePlanner.Build(");
            source.Should().Contain("PanePlans.BuildAccountPane(");
            source.Should().Contain("PanePlans.BuildExportPane(");
            source.Should().Contain("PanePlans.BuildInfoPane(");
            source.Should().Contain("PanePlans.BuildRecentPane(");
            source.Should().Contain("PanePlans.BuildNewPane(");
            source.Should().Contain("PanePlans.BuildOptionsPane(");
            source.Should().Contain("Panes.BuildAccountPane(");
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
    public void SharedPrintEvidenceFormatter_OwnsRendererNeutralEvidenceText()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Presentation",
            "Backstage",
            "BackstagePrintEvidenceTextFormatter.cs"));

        source.Should().Contain("BackstageViewTextResources.EvidenceScenariosLabel");
        source.Should().Contain("BackstageViewTextResources.EvidenceRequirementsLabel");
        source.Should().Contain("BackstagePrintEvidenceKind.PrintPreviewFidelity");
        source.Should().Contain("BackstagePrintEvidenceStatus.HostBacked");
        source.Should().Contain("FormatRequirement");
    }

    [Fact]
    public void FreeW_Avalonia_UsesPortableStandardPaneAndAccountSpecs()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Avalonia",
            "Backstage",
            "BackstageView.cs"));

        source.Should().Contain("SisterBackstagePaneSpecPlanner");
        source.Should().Contain("_session.BuildNewPaneSpec(PaneSpecs)");
        source.Should().Contain("_session.BuildOptionsPaneSpec(PaneSpecs)");
        source.Should().Contain("_session.BuildAccountPane(");
        source.Should().NotContain("ApplicationOptionsSummaryPlanner.Build(");
        source.Should().NotContain("new SisterBackstageAccountPaneContext(");
        source.Should().NotContain("SafeEnvironment(");
        source.Should().NotContain("SisterBackstageAccountPaneContextPlanner.BuildLocal(");
        source.Should().NotContain("PaneText.TemplateHeading");
        source.Should().NotContain("PaneText.OptionsDescription");
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
