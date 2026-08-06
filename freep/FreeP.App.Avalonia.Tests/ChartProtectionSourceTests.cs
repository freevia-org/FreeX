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

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(parts)}");
    }
}
