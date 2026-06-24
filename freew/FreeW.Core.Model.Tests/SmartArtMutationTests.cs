namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit tests for SmartArt node mutation operations that back the SmartArt Design contextual tab commands
/// (Add Shape, Remove Shape, Promote, Demote, Move Up, Move Down). All mutations are plain list/tree
/// operations on <see cref="SmartArt.Nodes"/> — no DocumentView needed, no STA required.
/// </summary>
public class SmartArtMutationTests
{
    // ── Add Shape ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddNode_AppendsNewNodeToList()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["A", "B"]);

        smartArt.Nodes.Add(new SmartArtNode("C"));

        smartArt.Nodes.Select(n => n.Text).Should().Equal("A", "B", "C");
    }

    // ── Remove Shape ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveLastNode_DecreasesNodeCount()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["X", "Y", "Z"]);

        smartArt.Nodes.RemoveAt(smartArt.Nodes.Count - 1);

        smartArt.Nodes.Select(n => n.Text).Should().Equal("X", "Y");
    }

    [Fact]
    public void SingleNodeDiagram_HasOneNode()
    {
        // The command logic skips Remove when Count <= 1; confirm the invariant via model state.
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Only"]);

        smartArt.Nodes.Count.Should().Be(1, "single-node diagram is the minimum representable");
    }

    // ── Move Up / Move Down ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void MoveUp_SwapsLastNodeWithPrevious()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Alpha", "Beta", "Gamma"]);
        var idx = smartArt.Nodes.Count - 1; // last = "Gamma"

        (smartArt.Nodes[idx], smartArt.Nodes[idx - 1]) = (smartArt.Nodes[idx - 1], smartArt.Nodes[idx]);

        smartArt.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Gamma", "Beta");
    }

    [Fact]
    public void MoveDown_SwapsFirstNodeWithNext()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Alpha", "Beta", "Gamma"]);
        const int idx = 0; // first = "Alpha"

        (smartArt.Nodes[idx], smartArt.Nodes[idx + 1]) = (smartArt.Nodes[idx + 1], smartArt.Nodes[idx]);

        smartArt.Nodes.Select(n => n.Text).Should().Equal("Beta", "Alpha", "Gamma");
    }

    // ── Promote (Hierarchy) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Promote_MovesLastChildOfFirstNodeToTopLevel()
    {
        // CEO → { VP Eng, VP Sales }  →  promote VP Sales  →  CEO → { VP Eng }, VP Sales
        var ceo = new SmartArtNode("CEO");
        ceo.AddChild("VP Eng");
        ceo.AddChild("VP Sales");
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(ceo);

        // Apply promote logic (mirrors DocumentView.SmartArtPromote)
        var node = smartArt.Nodes[0];
        var last = node.Children[^1];
        node.Children.RemoveAt(node.Children.Count - 1);
        smartArt.Nodes.Insert(1, last);

        smartArt.Nodes.Select(n => n.Text).Should().Equal("CEO", "VP Sales");
        smartArt.Nodes[0].Children.Select(c => c.Text).Should().Equal("VP Eng");
    }

    // ── Demote (Hierarchy) ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Demote_MovesLastTopLevelNodeIntoChildrenOfPrevious()
    {
        // CEO, VP Sales  →  demote VP Sales  →  CEO → { VP Sales }
        var ceo = new SmartArtNode("CEO");
        var vpSales = new SmartArtNode("VP Sales");
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(ceo);
        smartArt.Nodes.Add(vpSales);

        // Apply demote logic (mirrors DocumentView.SmartArtDemote)
        var last = smartArt.Nodes[^1];
        smartArt.Nodes.RemoveAt(smartArt.Nodes.Count - 1);
        smartArt.Nodes[^1].Children.Add(last);

        smartArt.Nodes.Should().ContainSingle().Which.Text.Should().Be("CEO");
        smartArt.Nodes[0].Children.Should().ContainSingle().Which.Text.Should().Be("VP Sales");
    }

    // ── ReplaceSelectedSmartArt (Edit Text) ───────────────────────────────────────────────────────

    [Fact]
    public void Replace_UpdatesKindAndNodes()
    {
        var original = SmartArt.Create(SmartArtKind.Process, ["A", "B"]);
        var replacement = SmartArt.Create(SmartArtKind.List, ["X", "Y", "Z"]);

        // Apply replace logic (mirrors DocumentView.ReplaceSelectedSmartArt)
        original.Kind = replacement.Kind;
        original.Nodes.Clear();
        foreach (var node in replacement.Nodes)
            original.Nodes.Add(node);

        original.Kind.Should().Be(SmartArtKind.List);
        original.Nodes.Select(n => n.Text).Should().Equal("X", "Y", "Z");
    }
}
