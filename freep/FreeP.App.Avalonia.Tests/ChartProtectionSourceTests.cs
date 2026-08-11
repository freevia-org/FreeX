using System.IO;

public sealed class ChartProtectionSourceTests
{
    [Fact]
    public void Avalonia_ChartDialogsRespectImportedProtectionPolicy()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var planner = File.ReadAllText(RepoFile(
            "freep", "FreeP.App.Presentation", "PresentationDomainDialogLaunchPlanner.cs"));

        source.Should().Contain(
            "_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartData)");
        source.Should().Contain(
            "_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartDisplayOptions)");
        planner.Should().Contain(
            "PresentationDomainDialogKind.ChartData => editor.CanEditSelectedChartData");
        planner.Should().Contain("editor.CanEditSelectedChartFormatting");
    }

    [Fact]
    public void Avalonia_ChartProtectionDialogIsRegistered()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var workflow = File.ReadAllText(RepoFile(
            "freep", "FreeP.App.Presentation", "Ribbon", "FreePRibbonCommandWorkflow.cs"));

        workflow.Should().Contain("ChartProtectionOptionsPlanner.CommandId");
        source.Should().Contain("OpenChartProtectionOptionsDialog");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
