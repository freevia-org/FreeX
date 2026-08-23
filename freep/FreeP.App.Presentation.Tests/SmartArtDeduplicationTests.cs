namespace FreeP.App.Compositor.Tests;

public sealed class SmartArtDeduplicationTests
{
    [Fact]
    public void FlattenPreorder_PreservesRootAndChildOrdering()
    {
        var firstRoot = Node("root-1", Node("child-1", Node("grandchild")), Node("child-2"));
        var secondRoot = Node("root-2", Node("child-3"));
        var data = new SmartArtData();
        data.Nodes.Add(firstRoot);
        data.Nodes.Add(secondRoot);

        var nodes = SmartArtNodeTraversal.FlattenPreorder(data);

        nodes.Select(node => node.ModelId).Should().Equal(
            "root-1",
            "child-1",
            "grandchild",
            "child-2",
            "root-2",
            "child-3");
        nodes[0].Should().BeSameAs(firstRoot);
        nodes[4].Should().BeSameAs(secondRoot);
    }

    [Fact]
    public void FlattenPreorder_EmptyDataReturnsEmptyList()
    {
        SmartArtNodeTraversal.FlattenPreorder(new SmartArtData()).Should().BeEmpty();
    }

    [Fact]
    public void SmartArtPackageCode_UsesSharedPathAndTraversalHelpers()
    {
        var planner = Read("freep", "FreeP.App.Presentation", "SmartArtEditingPlanner.cs");
        var reader = Read("freep", "FreeP.Core.IO", "PptxPackageReader.cs");

        planner.Should().Contain("OpcPathHelper.GetDirectoryName(");
        planner.Should().Contain("OpcPathHelper.GetRelativeZipPath(");
        planner.Should().Contain("OpcPathHelper.ResolveRelativeZipPath(");
        planner.Should().Contain("SmartArtNodeTraversal.FlattenPreorder(");
        planner.Should().NotContain("private static string MakeRelativeZipPath(");
        planner.Should().NotContain("private static string ResolveRelativeZipPath(");
        planner.Should().NotContain("private static string GetDirectoryName(");
        planner.Should().NotContain("private static List<SmartArtNode> FlattenNodes(");

        reader.Should().Contain("SmartArtNodeTraversal.FlattenPreorder(");
        reader.Should().NotContain("FlattenSmartArtNodes(");
    }

    private static SmartArtNode Node(string modelId, params SmartArtNode[] children)
    {
        var node = new SmartArtNode { ModelId = modelId };
        node.Children.AddRange(children);
        return node;
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
