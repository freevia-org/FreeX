namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit coverage for the <see cref="SmartArt"/> / <see cref="SmartArtNode"/> / <see cref="Run.SmartArt"/>
/// model (roadmap item Y1): the inline-run-mark API, the node tree and the convenience factory.
/// </summary>
public class SmartArtTests
{
    [Fact]
    public void Create_BuildsFlatDiagramFromNodeTexts()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["First", "Second", "Third"]);

        smartArt.Kind.Should().Be(SmartArtKind.List);
        smartArt.Nodes.Select(n => n.Text).Should().Equal("First", "Second", "Third");
        smartArt.Nodes.Should().OnlyContain(n => n.Children.Count == 0);
    }

    [Fact]
    public void SmartArt_DefaultsToListWithWordTypicalSize()
    {
        var smartArt = new SmartArt();

        smartArt.Kind.Should().Be(SmartArtKind.List);
        smartArt.Nodes.Should().BeEmpty();
        smartArt.WidthPt.Should().Be(468);
        smartArt.HeightPt.Should().Be(216);
    }

    [Fact]
    public void Node_AddChild_AppendsAndReturnsTheChild()
    {
        var root = new SmartArtNode("Root");

        var child = root.AddChild("Child");

        child.Text.Should().Be("Child");
        root.Children.Should().ContainSingle().Which.Should().BeSameAs(child);
    }

    [Fact]
    public void Node_ConstructorAcceptsChildren()
    {
        var node = new SmartArtNode("Parent", [new SmartArtNode("A"), new SmartArtNode("B")]);

        node.Children.Select(c => c.Text).Should().Equal("A", "B");
    }

    [Fact]
    public void FromSmartArt_BuildsTextlessInlineRunMark()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Do"]);

        var run = Run.FromSmartArt(smartArt);

        run.Text.Should().BeEmpty();
        run.SmartArt.Should().BeSameAs(smartArt);
    }
}
