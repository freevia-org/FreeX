using System.Text.RegularExpressions;

namespace FreeP.App.Compositor.Tests;

public sealed class SmartArtTreeWidthSourceTests
{
    [Fact]
    public void TopDownHierarchy_IndexesVisibleSubtreeWidthsOnce()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Presentation",
            "SmartArtLayoutEngine.cs"));
        var layout = ExtractMethod(source, "private static IReadOnlyList<SlideShape> LayoutTopDownHierarchy(");
        var render = ExtractMethod(source, "private static void RenderBasicHierarchyNode(");
        var index = ExtractMethod(source, "private static int IndexTreeWidths(");

        layout.Should()
            .Contain("new Dictionary<SmartArtNode, int>(ReferenceEqualityComparer.Instance)")
            .And.Contain("roots.Sum(root => IndexTreeWidths(root, treeWidths))")
            .And.Contain("int rootWidth = treeWidths[root]")
            .And.NotContain("GetTreeWidth(");
        render.Should()
            .Contain("IReadOnlyDictionary<SmartArtNode, int> treeWidths")
            .And.Contain("Math.Max(treeWidths[node], 1)")
            .And.Contain("int childWidth = treeWidths[child]")
            .And.NotContain("GetTreeWidth(");
        index.Should()
            .Contain("node.Children.Sum(child => IndexTreeWidths(child, treeWidths))")
            .And.Contain("treeWidths.Add(node, width)");
        source.Should().Contain("private static int GetTreeWidth(SmartArtNode node)",
            "other hierarchy layout paths retain their existing metric helper");
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"method '{signature}' should exist");

        var nextMethod = Regex.Match(
            source[(start + signature.Length)..],
            @"\r?\n    (private|internal|public) static ");

        return nextMethod.Success
            ? source[start..(start + signature.Length + nextMethod.Index)]
            : source[start..];
    }
}
