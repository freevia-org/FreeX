namespace FreeP.App.Compositor.Tests;

public sealed class PresentationPaneAccessibilityDedupSourceTests
{
    [Fact]
    public void Renderer_adapters_keep_only_native_accessibility_tree_writes()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var adapters = new[]
        {
            Read(root, "freep", "FreeP.App.Host", "PresentationPaneAccessibilityAdapter.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "PresentationPaneAccessibilityAdapter.cs"),
        };

        foreach (var source in adapters)
        {
            source.Should().Contain("PresentationPaneAccessibilitySession")
                .And.Contain("PresentationPaneAccessibilityPlanner.ProjectPane(")
                .And.Contain("PresentationPaneAccessibilityPlanner.ProjectItem(")
                .And.NotContain("Dictionary<")
                .And.NotContain("FormatStatus(")
                .And.NotContain("FormatItemStatus(")
                .And.NotContain("\"Visible\"")
                .And.NotContain("\"Hidden\"")
                .And.NotContain("\"; Order \"");
        }
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
