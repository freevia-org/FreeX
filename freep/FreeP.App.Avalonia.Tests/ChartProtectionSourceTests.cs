using System.IO;

public sealed class ChartProtectionSourceTests
{
    [Fact]
    public void Avalonia_ChartDialogsRespectImportedProtectionPolicy()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("if (!Editor.CanEditSelectedChartData)");
        source.Should().Contain("if (!Editor.CanEditSelectedChartFormatting)");
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
