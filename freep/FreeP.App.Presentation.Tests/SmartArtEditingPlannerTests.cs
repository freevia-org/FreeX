using System.Text;
using System.Xml.Linq;
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
    public void MoveDown_ReordersFlatNodesAndLiveLayout()
    {
        var data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan"), ("n2", "Build"), ("n3", "Ship"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.MoveDown("n1"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("n1");
        data.Nodes.Select(node => node.ModelId).Should().Equal("n2", "n1", "n3");
        result.Outline.Select(item => item.Text).Should().Equal("Build", "Plan", "Ship");

        var liveTexts = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!
            .Where(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .OrderBy(shape => shape.OffsetXEmu)
            .Select(shape => shape.TextBody!.Paragraphs[0].Runs[0].Text);

        liveTexts.Should().Equal("Build", "Plan", "Ship");
    }

    [Fact]
    public void MoveUp_FirstSibling_IsRejected()
    {
        var data = MakeFlatData(SmartArtFamily.Process, ("n1", "Only"), ("n2", "Later"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.MoveUp("n1"));

        result.Applied.Should().BeFalse();
        result.Message.Should().Be("The SmartArt node is already first.");
        data.Nodes.Select(node => node.ModelId).Should().Equal("n1", "n2");
    }

    [Fact]
    public void Promote_ChildBecomesSiblingAfterParentAndNormalizesLevels()
    {
        var root = new SmartArtNode { ModelId = "root", Text = "Leader", Level = 0 };
        var child = new SmartArtNode { ModelId = "child", Text = "Manager", Level = 7 };
        var grandchild = new SmartArtNode { ModelId = "grandchild", Text = "Report", Level = 9 };
        child.Children.Add(grandchild);
        root.Children.Add(child);

        var data = new SmartArtData { Family = SmartArtFamily.Hierarchy };
        data.Nodes.Add(root);

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Promote("child"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("child");
        data.Nodes.Select(node => node.ModelId).Should().Equal("root", "child");
        root.Children.Should().BeEmpty();
        child.Level.Should().Be(0);
        grandchild.Level.Should().Be(1);
        result.Outline.Select(item => (item.ModelId, item.Level))
            .Should().Equal(("root", 0), ("child", 0), ("grandchild", 1));
    }

    [Fact]
    public void Promote_RootNode_IsRejected()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Promote("root"));

        result.Applied.Should().BeFalse();
        result.Message.Should().Be("A root SmartArt node cannot be promoted.");
        data.Nodes.Should().ContainSingle();
    }

    [Fact]
    public void Demote_MakesNodeChildOfPreviousSiblingAndUpdatesHierarchyLayout()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("n1", "Leader"), ("n2", "Manager"), ("n3", "Peer"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Demote("n2"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("n2");
        data.Nodes.Select(node => node.ModelId).Should().Equal("n1", "n3");
        data.Nodes[0].Children.Should().ContainSingle();
        data.Nodes[0].Children[0].ModelId.Should().Be("n2");
        result.Outline.Select(item => (item.ModelId, item.Level, item.SiblingIndex))
            .Should().Equal(("n1", 0, 0), ("n2", 1, 0), ("n3", 0, 1));

        var liveTexts = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!
            .Where(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Select(shape => shape.TextBody!.Paragraphs[0].Runs[0].Text);

        liveTexts.Should().Contain(["Leader", "Manager", "Peer"]);
    }

    [Fact]
    public void Demote_FirstSibling_IsRejected()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("n1", "Leader"), ("n2", "Manager"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Demote("n1"));

        result.Applied.Should().BeFalse();
        result.Message.Should().Be("The first SmartArt sibling cannot be demoted.");
        data.Nodes.Select(node => node.ModelId).Should().Equal("n1", "n2");
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

    [Fact]
    public void RewriteDataPart_AfterSharedOutlineEdit_RegeneratesNativeDiagramData()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("manager", "Manager"));
        var smartArt = new SmartArtShape { Data = data };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };

        SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.ChangeText("manager", "Delivery Lead"));
        SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Demote("manager"));

        var result = SmartArtEditingPlanner.RewriteDataPart(smartArt);

        result.Applied.Should().BeTrue();
        result.DataPartPath.Should().Be("ppt/diagrams/data1.xml");
        result.NodeCount.Should().Be(2);
        result.ConnectionCount.Should().Be(1);

        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var doc = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/data1.xml"].Bytes));

        doc.Descendants(dgm + "pt")
            .Select(pt => (Id: (string?)pt.Attribute("modelId"), Text: pt.Descendants(a + "t").Single().Value))
            .Should().Equal(("root", "Leader"), ("manager", "Delivery Lead"));

        doc.Descendants(dgm + "cxn")
            .Select(cxn => (
                Type: (string?)cxn.Attribute("type"),
                Source: (string?)cxn.Attribute("srcId"),
                Destination: (string?)cxn.Attribute("destId")))
            .Should().ContainSingle()
            .Which.Should().Be(("parOf", "root", "manager"));
    }

    [Fact]
    public void RegenerateDrawingCache_AfterSharedOutlineEdit_RewritesDspDrawingFromLivePlan()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("manager", "Manager"));
        var smartArt = new SmartArtShape { Data = data, DrawingPartPath = "ppt/diagrams/drawing1.xml" };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };

        SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.ChangeText("manager", "Delivery Lead"))
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Demote("manager"))
            .Applied.Should().BeTrue();

        var result = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            FrameX,
            FrameY,
            FrameCx,
            FrameCy,
            DefaultTheme());

        result.Applied.Should().BeTrue();
        result.DrawingPartPath.Should().Be("ppt/diagrams/drawing1.xml");
        result.NodeCount.Should().Be(2);
        result.ShapeCount.Should().Be(3, "the shared hierarchy plan emits two node boxes plus one connector");
        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Should().Equal("Leader", "Delivery Lead");

        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var doc = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes));

        doc.Root!.Name.Should().Be(dsp + "drawing");
        doc.Descendants(dsp + "sp").Should().HaveCount(3);
        doc.Descendants(a + "t").Select(t => t.Value)
            .Should().Contain(["Leader", "Delivery Lead"])
            .And.NotContain("Manager");
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
