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

    [Theory]
    [InlineData(SmartArtLayoutPreset.BasicProcess, "basicProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.AlternatingProcess, "alternatingProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.ArrowRibbon, "arrowRibbon", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.CircleProcess, "circleProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.FunnelProcess, "funnelProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.VerticalProcess, "verticalProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.VerticalBoxList, "verticalBoxList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.BasicCycle, "basicCycle", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.BasicBlockList, "basicBlockList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.StackedList, "stackedList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.DescendingBlockList, "descendingBlockList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.BasicPyramid, "basicPyramid", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.RadialCycle, "radialCycle", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.BasicMatrix, "basicMatrix", SmartArtFamily.Matrix)]
    [InlineData(SmartArtLayoutPreset.BasicVenn, "basicVenn", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.RadialVenn, "radialVenn", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.TargetList, "targetList", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.StackedVenn, "stackedVenn", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.BasicHierarchy, "basicHierarchy", SmartArtFamily.Hierarchy)]
    public void ApplyLayoutPreset_UpdatesLiveModelAndNativeLayoutPart(
        SmartArtLayoutPreset preset,
        string expectedId,
        SmartArtFamily expectedFamily)
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan"), ("n2", "Build")),
        };
        var layoutPart = new DiagramPart
        {
            PartPath = "ppt/diagrams/layout1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dgm:layoutDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" uniqueId=\"old\" />")
        };
        smartArt.Parts[layoutPart.PartPath] = layoutPart;

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, preset);

        result.Applied.Should().BeTrue();
        result.LayoutUniqueId.Should().EndWith($"/layout/{expectedId}");
        result.Family.Should().Be(expectedFamily);
        smartArt.Data!.LayoutUniqueId.Should().Be(result.LayoutUniqueId);
        smartArt.Data.Family.Should().Be(expectedFamily);
        XDocument.Parse(Encoding.UTF8.GetString(layoutPart.Bytes))
            .Root!.Attribute("uniqueId")!.Value.Should().Be(result.LayoutUniqueId);
    }

    [Fact]
    public void ApplyLayoutPreset_RequiresNativeLayoutPart()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan"))
        };

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(
            smartArt,
            SmartArtLayoutPreset.BasicCycle);

        result.Applied.Should().BeFalse();
        result.Message.Should().Contain("native layout definition");
    }

    [Theory]
    [InlineData(SmartArtQuickStylePreset.Simple, "simple1", "Simple")]
    [InlineData(SmartArtQuickStylePreset.Moderate, "moderate1", "Moderate")]
    [InlineData(SmartArtQuickStylePreset.Intense, "intense1", "Intense")]
    public void ApplyQuickStylePreset_UpdatesMetadataAndNativeStylePart(
        SmartArtQuickStylePreset preset,
        string expectedId,
        string expectedTitle)
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan")),
            QuickStyle = new SmartArtQuickStyleMetadata { UniqueId = "old-style", Title = "Old" },
        };
        var stylePart = new DiagramPart
        {
            PartPath = "ppt/diagrams/quickStyle1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dgm:styleDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" uniqueId=\"old-style\"><dgm:title val=\"Old\" /></dgm:styleDef>")
        };
        smartArt.Parts[stylePart.PartPath] = stylePart;

        var result = SmartArtAuthoringPlanner.ApplyQuickStylePreset(smartArt, preset);

        result.Applied.Should().BeTrue();
        result.StyleUniqueId.Should().EndWith($"/quickstyle/{expectedId}");
        smartArt.QuickStyle!.UniqueId.Should().Be(result.StyleUniqueId);
        smartArt.QuickStyle.Title.Should().Be(expectedTitle);
        var root = XDocument.Parse(Encoding.UTF8.GetString(stylePart.Bytes)).Root!;
        root.Attribute("uniqueId")!.Value.Should().Be(result.StyleUniqueId);
        root.Element(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram") + "title")!
            .Attribute("val")!.Value.Should().Be(expectedTitle);
    }

    [Fact]
    public void ApplyQuickStylePreset_CreatesMissingNativeStylePart()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan"))
        };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };

        var result = SmartArtAuthoringPlanner.ApplyQuickStylePreset(smartArt, SmartArtQuickStylePreset.Intense);

        result.Applied.Should().BeTrue();
        result.PartPath.Should().NotBeNull();
        smartArt.DiagramRelIds.Should().ContainKey("qs");
        smartArt.Parts[result.PartPath!].ContentType.Should().Contain("diagramStyle");
    }

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
    public void ApplyTextPaneOutline_RebuildsSharedTreeAndLiveLayout()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("manager", "Manager"));
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/orgChart";

        var result = SmartArtEditingPlanner.ApplyTextPaneOutline(data,
        [
            new("Executive", 0, ModelId: "root"),
            new("Assistant", 1, IsAssistant: true, ModelId: "assistant"),
            new("Platform", 1, ModelId: "manager"),
            new("QA", 2),
            new("Operations", 0, ModelId: "operations")
        ]);

        result.Applied.Should().BeTrue();
        result.RowCount.Should().Be(5);
        result.Outline.Select(item => (item.ModelId, item.Text, item.Level, item.SiblingIndex, item.IsAssistant))
            .Should().Equal(
                ("root", "Executive", 0, 0, false),
                ("assistant", "Assistant", 1, 0, true),
                ("manager", "Platform", 1, 1, false),
                ("freep-smartart-node-4", "QA", 2, 0, false),
                ("operations", "Operations", 0, 1, false));

        data.Nodes.Should().HaveCount(2);
        data.Nodes[0].Children.Should().HaveCount(2);
        data.Nodes[0].Children[0].IsAssistant.Should().BeTrue();
        data.Nodes[0].Children[1].Children.Should().ContainSingle().Which.Text.Should().Be("QA");

        var liveTexts = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!
            .Where(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Select(shape => shape.TextBody!.Paragraphs[0].Runs[0].Text);

        liveTexts.Should().Contain(["Executive", "Assistant", "Platform", "QA", "Operations"]);
    }

    [Fact]
    public void ApplyTextPaneOutline_SkippedParentLevelIsRejectedWithoutMutation()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("manager", "Manager"));

        var result = SmartArtEditingPlanner.ApplyTextPaneOutline(data,
        [
            new("Executive", 0, ModelId: "root"),
            new("Too Deep", 2, ModelId: "deep")
        ]);

        result.Applied.Should().BeFalse();
        result.Message.Should().Be("SmartArt text-pane levels cannot skip a parent level.");
        data.Nodes.Select(node => node.Text).Should().Equal("Leader", "Manager");
    }

    [Fact]
    public void ApplyTextPaneOutline_PreservesPicturePayloadsByStableNodeId()
    {
        var picture = new ImagePart { Bytes = [0x89, 0x50, 0x4E, 0x47], ContentType = "image/png" };
        var data = MakeFlatData(SmartArtFamily.List, ("a", "Alpha"), ("b", "Beta"));
        data.Nodes[1].Picture = picture;

        var result = SmartArtEditingPlanner.ApplyTextPaneOutline(data,
        [
            new("Beta revised", 0, ModelId: "b"),
            new("Alpha", 0, ModelId: "a")
        ]);

        result.Applied.Should().BeTrue();
        data.Nodes.Select(node => node.ModelId).Should().Equal("b", "a");
        data.Nodes[0].Picture.Should().BeSameAs(picture);
        data.Nodes[1].Picture.Should().BeNull();
    }

    [Fact]
    public void TextPaneOutline_DataPartAndDrawingCacheRegenerationShareAppliedModel()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("manager", "Manager"));
        var smartArt = new SmartArtShape { Data = data, DrawingPartPath = "ppt/diagrams/drawing1.xml" };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };

        SmartArtEditingPlanner.ApplyTextPaneOutline(data,
        [
            new("Executive", 0, ModelId: "root"),
            new("Delivery Lead", 1, ModelId: "manager")
        ]).Applied.Should().BeTrue();

        var dataPart = SmartArtEditingPlanner.RewriteDataPart(smartArt);
        var cache = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            FrameX,
            FrameY,
            FrameCx,
            FrameCy,
            DefaultTheme());

        dataPart.Applied.Should().BeTrue();
        dataPart.ConnectionCount.Should().Be(1);
        cache.Applied.Should().BeTrue();
        cache.ShapeCount.Should().Be(3);

        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var dataDoc = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/data1.xml"].Bytes));
        dataDoc.Descendants(a + "t").Select(t => t.Value)
            .Should().Equal("Executive", "Delivery Lead");
        dataDoc.Descendants(dgm + "cxn")
            .Should().ContainSingle()
            .Which.Attribute("destId")!.Value.Should().Be("manager");

        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Should().Equal("Executive", "Delivery Lead");
    }

    [Theory]
    [InlineData(SmartArtTextPaneShortcutKey.Enter, SmartArtTextPaneShortcutModifiers.None, SmartArtNodeEditKind.AddSiblingAfter, "smartart.text-pane.enter.add-sibling-after")]
    [InlineData(SmartArtTextPaneShortcutKey.Enter, SmartArtTextPaneShortcutModifiers.Control, SmartArtNodeEditKind.AddChild, "smartart.text-pane.ctrl-enter.add-child")]
    [InlineData(SmartArtTextPaneShortcutKey.Tab, SmartArtTextPaneShortcutModifiers.None, SmartArtNodeEditKind.Demote, "smartart.text-pane.tab.demote")]
    [InlineData(SmartArtTextPaneShortcutKey.Tab, SmartArtTextPaneShortcutModifiers.Shift, SmartArtNodeEditKind.Promote, "smartart.text-pane.shift-tab.promote")]
    [InlineData(SmartArtTextPaneShortcutKey.Up, SmartArtTextPaneShortcutModifiers.Alt | SmartArtTextPaneShortcutModifiers.Shift, SmartArtNodeEditKind.MoveUp, "smartart.text-pane.alt-shift-up.move-up")]
    [InlineData(SmartArtTextPaneShortcutKey.Down, SmartArtTextPaneShortcutModifiers.Alt | SmartArtTextPaneShortcutModifiers.Shift, SmartArtNodeEditKind.MoveDown, "smartart.text-pane.alt-shift-down.move-down")]
    public void PlanTextPaneKeyboardRoute_MapsSharedChordsToEditIntents(
        SmartArtTextPaneShortcutKey key,
        SmartArtTextPaneShortcutModifiers modifiers,
        SmartArtNodeEditKind expectedKind,
        string expectedRouteId)
    {
        var route = SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(key, modifiers, " manager ");

        route.Should().NotBeNull();
        route!.RouteId.Should().Be(expectedRouteId);
        route.Key.Should().Be(key);
        route.Modifiers.Should().Be(modifiers);
        route.Intent.Kind.Should().Be(expectedKind);
        route.Intent.TargetModelId.Should().Be("manager");
    }

    [Fact]
    public void PlanTextPaneKeyboardRoute_RejectsUnownedChordsAndMissingSelection()
    {
        SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
                SmartArtTextPaneShortcutKey.Up,
                SmartArtTextPaneShortcutModifiers.None,
                "manager")
            .Should()
            .BeNull();

        SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
                SmartArtTextPaneShortcutKey.Tab,
                SmartArtTextPaneShortcutModifiers.None,
                "  ")
            .Should()
            .BeNull();
    }

    [Fact]
    public void PlanTextPaneKeyboardRoute_FeedsSharedModelEditsForHostAdapters()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("peer", "Peer"), ("manager", "Manager"));

        var addChild = SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
            SmartArtTextPaneShortcutKey.Enter,
            SmartArtTextPaneShortcutModifiers.Control,
            "manager");
        SmartArtEditingPlanner.Apply(data, addChild!.Intent).Applied.Should().BeTrue();

        var moveDown = SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
            SmartArtTextPaneShortcutKey.Down,
            SmartArtTextPaneShortcutModifiers.Alt | SmartArtTextPaneShortcutModifiers.Shift,
            "peer");
        SmartArtEditingPlanner.Apply(data, moveDown!.Intent).Applied.Should().BeTrue();

        var demote = SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
            SmartArtTextPaneShortcutKey.Tab,
            SmartArtTextPaneShortcutModifiers.None,
            "peer");
        SmartArtEditingPlanner.Apply(data, demote!.Intent).Applied.Should().BeTrue();

        data.Nodes.Select(node => node.ModelId).Should().Equal("root", "manager");
        data.Nodes[0].Children.Should().BeEmpty();
        data.Nodes[1].Children.Select(node => node.ModelId).Should().Equal("freep-smartart-node-4", "peer");
        data.Nodes[1].Children[0].Text.Should().Be(SmartArtEditingPlanner.DefaultNewNodeText);
        SmartArtEditingPlanner.BuildOutline(data)
            .Select(item => (item.ModelId, item.Level, item.SiblingIndex))
            .Should()
            .Equal(
                ("root", 0, 0),
                ("manager", 0, 1),
                ("freep-smartart-node-4", 1, 0),
                ("peer", 1, 1));
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
