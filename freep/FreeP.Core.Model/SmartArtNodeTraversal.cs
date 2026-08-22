namespace FreeP.Core.Model;

public static class SmartArtNodeTraversal
{
    public static List<SmartArtNode> FlattenPreorder(SmartArtData data)
    {
        var nodes = new List<SmartArtNode>();
        foreach (var root in data.Nodes)
            Collect(root);
        return nodes;

        void Collect(SmartArtNode node)
        {
            nodes.Add(node);
            foreach (var child in node.Children)
                Collect(child);
        }
    }
}
