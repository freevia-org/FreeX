namespace FreeP.RenderCompare.Tests;

public sealed class VisualEvidenceHostOwnershipTests
{
    [Fact]
    public void Shipping_hosts_do_not_own_capture_orchestration()
    {
        var shippingProjects = new[]
        {
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "FreeP.App.Host", "FreeP.App.Host.csproj"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj"),
        };

        shippingProjects.Should().AllSatisfy(source =>
            source.Should().NotContain("TestSupport\\VisualEvidence\\FreeP.VisualEvidence.csproj"));

        var shippingSources = new[]
        {
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "FreeP.App.Host", "Program.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "FreeP.App.Avalonia", "Program.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "FreeP.App.Avalonia", "App.cs"),
        };

        shippingSources.Should().AllSatisfy(source =>
        {
            source.Should().NotContain("VisualEvidenceCapture");
            source.Should().NotContain("VisualEvidenceOutputRoot");
        });
    }

    [Fact]
    public void Capture_implementations_are_owned_only_by_tool_projects()
    {
        var root = FindWorkspaceRoot();
        var productionFiles = Directory.EnumerateFiles(Path.Combine(root, "freep"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}TestSupport{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToArray();

        productionFiles.Should().AllSatisfy(source =>
        {
            source.Should().NotContain("class WpfDialogPaneVisualEvidenceCapture");
            source.Should().NotContain("class WpfWholeWindowVisualEvidenceCapture");
            source.Should().NotContain("class AvaloniaDialogPaneVisualEvidenceCapture");
            source.Should().NotContain("class AvaloniaWholeWindowVisualEvidenceCapture");
        });
    }

    private static string FindWorkspaceRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "FreeX.slnx")))
                return current.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the workspace root containing FreeX.slnx.");
    }
}
