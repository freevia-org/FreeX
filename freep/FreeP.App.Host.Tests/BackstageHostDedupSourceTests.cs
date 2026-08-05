using System.IO;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;

namespace FreeP.App.Host.Tests;

public sealed class BackstageHostDedupSourceTests
{
    [Fact]
    public void FreeP_wpf_entry_spec_uses_the_shared_thirteen_entry_order()
    {
        static UIElement Pane() => new Border();
        var entries = SisterBackstageEntryBuilder.Build(new SisterBackstageEntrySpec(
            Pane, static () => { }, static () => { }, static () => { }, static () => { },
            Pane, Pane, Pane)
        {
            BuildPrintPane = Pane,
            BuildExportPane = Pane,
            BuildAccountPane = Pane,
        });

        entries.Select(entry => entry.Separator ? "|" : entry.Label)
            .Should().Equal(
                "Info", "New", "Open", "|", "Save", "Save As", "Print", "Export",
                "Recent", "New from template", "Account", "Options", "Close");
        entries.Should().HaveCount(13);
    }

    [Fact]
    public void BackstageViews_DelegatePaneAndWorkflowSemanticsToPortablePlanners()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpfSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "Backstage",
            "BackstageView.cs"));
        var avaloniaSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "Backstage",
            "BackstageView.cs"));

        wpfSource.Should().Contain("SisterBackstageHostController");
        wpfSource.Should().Contain("new SisterBackstageHostSpec(");
        wpfSource.Should().Contain("Chrome = BackstageRibbonChrome.Create()");
        wpfSource.Should().Contain("public void Show() => _backstage.Show();");
        wpfSource.Should().Contain("public void Hide() => _backstage.Hide();");
        wpfSource.Should().Contain("backstage.FrameCommand(_actions.New)");
        wpfSource.Should().Contain("PresentationBackstagePanePlanner");
        wpfSource.Should().Contain("PresentationBackstagePrintSurfacePlanner.Build(plan)");
        wpfSource.Should().Contain("PanePlans.BuildInfoPane(");
        wpfSource.Should().Contain("PanePlans.BuildExportPane(");
        wpfSource.Should().Contain("PanePlans.BuildRecentPane(");
        wpfSource.Should().Contain("PanePlans.BuildNewPane(");
        wpfSource.Should().Contain("PanePlans.BuildOptionsPane(");
        wpfSource.Should().Contain("PanePlans.BuildAccountPane(");
        wpfSource.Should().Contain("PresentationBackstageExportActions(");

        avaloniaSource.Should().Contain("PresentationBackstagePanePlanner");
        avaloniaSource.Should().Contain("PresentationBackstagePrintSurfacePlanner.Build(plan)");
        avaloniaSource.Should().Contain("AvaloniaBackstagePaneComposer");
        avaloniaSource.Should().Contain("Panes.BuildInfoPane(");
        avaloniaSource.Should().Contain("Panes.BuildRecentPane(");
        avaloniaSource.Should().Contain("Panes.BuildTemplatePane(");
        avaloniaSource.Should().Contain("Panes.BuildOptionsPane(");
        avaloniaSource.Should().Contain("Panes.BuildAccountPane(");
        avaloniaSource.Should().Contain("Panes.BuildActionPane(");

        foreach (var source in new[] { wpfSource, avaloniaSource })
        {
            source.Should().NotContain("PresentationExportPlanner.BuildBackstageExportPlan(");
            source.Should().NotContain("PresentationExportPlanner.PdfExportCommandId");
            source.Should().NotContain("SisterBackstageInfoPanePlanner.Build(");
            source.Should().NotContain("SisterBackstageAccountPanePlanner.Build(");
            source.Should().NotContain("ApplicationOptionsSummaryPlanner.Build(");
            source.Should().NotContain("plan.OutputOptionChoices");
            source.Should().NotContain("plan.LayoutChoices");
            source.Should().NotContain("plan.RangeChoices");
            source.Should().NotContain("plan.NativePrintHandoff.Can");
            source.Should().NotContain("new SisterBackstageAccountPaneContext(");
            source.Should().NotContain("SafeEnvironment(");
        }

        wpfSource.Should().NotContain("new BackstageViewShell(");
        wpfSource.Should().NotContain("SisterBackstageEntryBuilder.Build(");
        wpfSource.Should().NotContain("Hide(); _actions");
        wpfSource.Should().NotContain("_shell.Show");
    }

}
