using System.IO;

public sealed class ChartProtectionSourceTests
{
    [Fact]
    public void Avalonia_ChartDialogsRespectImportedProtectionPolicy()
    {
        var endpoints = File.ReadAllText(RepoFile(
            "freep", "RendererShared", "MainWindow.ChartDialogEndpoints.cs"));
        var planner = File.ReadAllText(RepoFile(
            "freep", "FreeP.App.Presentation", "PresentationDomainDialogLaunchPlanner.cs"));

        endpoints.Should().Contain(
            "OpenChartDialog(PresentationDomainDialogKind.ChartData")
            .And.Contain("OpenChartDialog(PresentationDomainDialogKind.ChartDisplayOptions")
            .And.Contain("if (!_workareaSession.CanOpenDomainDialog(kind))")
            .And.Contain("ShowDomainDialog(createDialog());");
        planner.Should().Contain(
            "PresentationDomainDialogKind.ChartData => editor.CanEditSelectedChartData");
        planner.Should().Contain("editor.CanEditSelectedChartFormatting");
    }

    [Fact]
    public void Avalonia_ChartProtectionDialogIsRegistered()
    {
        var actions = File.ReadAllText(RepoFile(
            "freep", "RendererShared", "MainWindow.RibbonActionProfile.cs"));
        var endpoints = File.ReadAllText(RepoFile(
            "freep", "RendererShared", "MainWindow.ChartDialogEndpoints.cs"));
        var workflow = File.ReadAllText(RepoFile(
            "freep", "FreeP.App.Presentation", "Ribbon", "FreePRibbonCommandWorkflow.cs"));

        workflow.Should().Contain("ChartProtectionOptionsPlanner.CommandId");
        actions.Should().Contain("OpenChartProtectionOptions = OpenChartProtectionOptionsDialog");
        endpoints.Should().Contain(
            "OpenChartDialog(PresentationDomainDialogKind.ChartProtectionOptions");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
