using System.IO;
using System.Linq;
using System.Xml.Linq;
using Free.Shared.Shell;

namespace FreeW.App.Host.Tests;

public sealed class SharedBackstageInfoPlanDedupTests
{
    [Fact]
    public void CorePropertiesPlanner_UsesThePortableMissingValueFallback()
    {
        var rows = BackstageCorePropertiesPlanner.Build(new BackstageCoreProperties(
            Title: "Title",
            Author: "",
            Subject: null,
            Keywords: "Keywords"));

        rows.Select(row => row.Value).Should().Equal("Title", "—", "—", "Keywords");
    }

    [Fact]
    public void SisterBackstageInfoPanePlanner_BuildsOnePlanForBothRenderers()
    {
        var plan = SisterBackstageInfoPanePlanner.Build(new SisterBackstageInfoPaneContext(
            DocumentKindLabel: "Document",
            DisplayName: "Budget.docx",
            IsDirty: true,
            Location: @"C:\Work\Budget.docx",
            CoreProperties: new BackstageCoreProperties("Budget", "Ada", null, null),
            Statistics: [new("Words", "42")]));

        plan.DisplayName.Should().Be("Budget.docx");
        plan.IsDirty.Should().BeTrue();
        plan.Properties.Select(row => row.Value).Should().Equal("Budget", "Ada", "—", "—");
        plan.Statistics.Should().ContainSingle(row => row.Label == "Words" && row.Value == "42");
    }

    [Fact]
    public void InfoPlanTypes_LiveInPortableShell_AndAvaloniaConsumesTheSamePlan()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sharedInfo = File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Shell", "BackstageInfoPaneSpec.cs"));
        var sharedPlanner = File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Shell", "SisterBackstageInfoPanePlanner.cs"));
        var session = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Presentation", "Backstage", "FreeWBackstageSession.cs"));
        var wpfComposer = File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Shell.Wpf", "BackstagePaneComposer.cs"));
        var avaloniaComposer = File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Shell.Avalonia", "AvaloniaBackstagePaneComposer.cs"));
        var wpfView = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Backstage", "BackstageView.cs"));
        var avaloniaView = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Backstage", "BackstageView.cs"));

        File.Exists(Path.Combine(root, "shared", "Free.Shared.Shell.Wpf", "BackstageCorePropertiesPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(root, "shared", "Free.Shared.Shell.Wpf", "SisterBackstageInfoPanePlanner.cs")).Should().BeFalse();
        sharedInfo.Should().Contain("namespace Free.Shared.Shell;");
        sharedPlanner.Should().Contain("namespace Free.Shared.Shell;");
        session.Should().Contain("SisterBackstageInfoPanePlanner.Build(");
        session.Should().Contain("BackstageInfoStatisticsPlanner.Build(document)");
        wpfComposer.Should().Contain("BuildInfoPane(BackstageInfoPaneSpec spec)");
        wpfComposer.Should().NotContain("public sealed record BackstageInfoPaneSpec");
        avaloniaComposer.Should().Contain("BuildInfoPane(BackstageInfoPaneSpec spec)");
        avaloniaComposer.Should().NotContain("public sealed record BackstageInfoPaneSpec");
        wpfView.Should().Contain("_session.BuildInfoPane()");
        avaloniaView.Should().Contain("_session.BuildInfoPane()");
        wpfView.Should().NotContain("SisterBackstageInfoPanePlanner.Build(");
        avaloniaView.Should().NotContain("SisterBackstageInfoPanePlanner.Build(");
        avaloniaView.Should().NotContain("BackstagePaneSurfacePlanner.BuildInfoPane(");
    }

    [Fact]
    public void BackstageSidebarResources_HaveOneWpfOwner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var shellResource = Path.Combine(root, "shared", "Free.Shared.Shell.Wpf", "BackstageChromeResources.xaml");
        var ribbonResource = Path.Combine(root, "shared", "Free.Shared.Ribbon.Wpf", "SharedChromeResources.xaml");
        var ribbonChrome = File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Ribbon.Wpf", "BackstageRibbonChrome.cs"));

        var shellKeys = ReadResourceKeys(shellResource);
        var ribbonKeys = ReadResourceKeys(ribbonResource);
        var backstageKeys = shellKeys
            .Concat(ribbonKeys)
            .Where(key => key.StartsWith("BackstageSidebar", StringComparison.Ordinal) ||
                          key.StartsWith("ChromeBackstageSidebar", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        backstageKeys.Should().BeEquivalentTo(
            "ChromeBackstageSidebarBrush",
            "ChromeBackstageSidebarHoverBrush",
            "ChromeBackstageSidebarSelectedBrush",
            "ChromeBackstageSidebarSeparatorBrush",
            "BackstageSidebarNavButton",
            "BackstageSidebarNavButtonActive",
            "BackstageSidebarBackButton");

        foreach (var key in backstageKeys)
        {
            shellKeys.Count(candidate => candidate == key).Should().Be(1);
            ribbonKeys.Count(candidate => candidate == key).Should().Be(0);
        }

        ribbonChrome.Should().Contain("Free.Shared.Shell.Wpf;component/BackstageChromeResources.xaml");
    }

    private static string[] ReadResourceKeys(string path) =>
        XDocument.Load(path)
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName == "Key")
            .Select(attribute => attribute.Value)
            .ToArray();

}
