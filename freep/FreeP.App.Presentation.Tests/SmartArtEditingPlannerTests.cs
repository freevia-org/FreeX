using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SmartArtEditingPlannerTests
{
    private const long FrameX = 914_400L;
    private const long FrameY = 457_200L;
    private const long FrameCx = 7_315_200L;
    private const long FrameCy = 3_657_600L;

    [Fact]
    public void ChangeText_UpdatesTargetNodeAndLiveLayoutText()
    {
        var data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan"), ("n2", "Build"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.ChangeText("n2", "Validate"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("n2");
        data.Nodes[1].Text.Should().Be("Validate");

        var texts = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!
            .Where(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Select(shape => shape.TextBody!.Paragraphs[0].Runs[0].Text);

        texts.Should().Equal("Plan", "Validate");
    }

    [Fact]
    public void AddSiblingAfter_InsertsNodeAfterTargetWithStableGeneratedId()
    {
        var data = MakeFlatData(SmartArtFamily.List, ("n1", "North"), ("n2", "South"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.AddSiblingAfter("n1", "Center"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("freep-smartart-node-3");
        data.Nodes.Select(node => node.Text).Should().Equal("North", "Center", "South");
        data.Nodes.Select(node => node.Level).Should().Equal(0, 0, 0);
        result.Outline.Select(item => item.Text).Should().Equal("North", "Center", "South");
    }

    [Fact]
    public void AddChild_AppendsNestedNodeAndNormalizesLevels()
    {
        var root = new SmartArtNode { ModelId = "root", Text = "Leader", Level = 4 };
        var data = new SmartArtData { Family = SmartArtFamily.Hierarchy };
        data.Nodes.Add(root);

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.AddChild("root", "Report"));

        result.Applied.Should().BeTrue();
        root.Level.Should().Be(0);
        root.Children.Should().ContainSingle();
        root.Children[0].ModelId.Should().Be("freep-smartart-node-2");
        root.Children[0].Level.Should().Be(1);
        root.Children[0].Text.Should().Be("Report");
        result.Outline.Select(item => item.Level).Should().Equal(0, 1);
    }

    [Fact]
    public void Remove_RemovesSubtreeButKeepsNextSelection()
    {
        var data = MakeFlatData(SmartArtFamily.Process, ("n1", "Draft"), ("n2", "Review"), ("n3", "Ship"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Remove("n2"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("n3");
        data.Nodes.Select(node => node.Text).Should().Equal("Draft", "Ship");
    }

    [Fact]
    public void Remove_LastRemainingNode_IsRejected()
    {
        var data = MakeFlatData(SmartArtFamily.Process, ("n1", "Only"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Remove("n1"));

        result.Applied.Should().BeFalse();
        data.Nodes.Should().ContainSingle();
        result.Message.Should().Be("At least one SmartArt node must remain.");
    }

    [Fact]
    public void CloneShape_DeepClonesEditableSmartArtData()
    {
        var shape = new SlideShape
        {
            Id = 12,
            Kind = SlideShapeKind.SmartArt,
            SmartArt = new SmartArtShape
            {
                Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Source"))
            }
        };
        shape.SmartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/xml",
            Bytes = [1, 2, 3]
        };

        var clone = SlideCloner.CloneShape(shape);
        SmartArtEditingPlanner.Apply(clone.SmartArt!.Data, SmartArtNodeEditIntent.ChangeText("n1", "Clone"));

        shape.SmartArt!.Data!.Nodes[0].Text.Should().Be("Source");
        clone.SmartArt.Data!.Nodes[0].Text.Should().Be("Clone");
        clone.SmartArt.Parts.Should().ContainKey("ppt/diagrams/data1.xml");
        clone.SmartArt.Parts.Should().NotBeSameAs(shape.SmartArt.Parts);
    }

    private static PresentationTheme DefaultTheme() =>
        Presentation.CreateEmpty().Theme!;

    private static SmartArtData MakeFlatData(SmartArtFamily family, params (string Id, string Text)[] nodes)
    {
        var data = new SmartArtData { Family = family };
        foreach (var (id, text) in nodes)
            data.Nodes.Add(new SmartArtNode { ModelId = id, Text = text, Level = 0 });
        return data;
    }
}
