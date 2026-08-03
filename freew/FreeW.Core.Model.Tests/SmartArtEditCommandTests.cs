namespace FreeW.Core.Model.Tests;

public sealed class SmartArtEditCommandTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    [Theory]
    [InlineData(SmartArtStructureOperation.AddShape, "A,B,New Item")]
    [InlineData(SmartArtStructureOperation.RemoveShape, "A")]
    [InlineData(SmartArtStructureOperation.MoveUp, "B,A")]
    [InlineData(SmartArtStructureOperation.MoveDown, "B,A")]
    public void StructuralCommands_AreUndoableAndPreserveUnrelatedState(
        SmartArtStructureOperation operation,
        string expected)
    {
        var (bus, smartArt) = CreateDocument(SmartArt.Create(SmartArtKind.Process, ["A", "B"]));
        StampUnrelatedState(smartArt);

        bus.Execute(new MutateSmartArtStructureCommand(0, 0, operation));

        smartArt.Nodes.Select(node => node.Text).Should().Equal(expected.Split(','));
        AssertUnrelatedState(smartArt);
        bus.Undo().Should().BeTrue();
        smartArt.Nodes.Select(node => node.Text).Should().Equal("A", "B");
        AssertUnrelatedState(smartArt);
        bus.Redo().Should().BeTrue();
        smartArt.Nodes.Select(node => node.Text).Should().Equal(expected.Split(','));
    }

    [Fact]
    public void PromoteAndDemote_AreInverseUndoableHierarchyOperations()
    {
        var root = new SmartArtNode("Root", [new SmartArtNode("Child")]);
        var hierarchy = new SmartArt { Kind = SmartArtKind.Hierarchy };
        hierarchy.Nodes.Add(root);
        var (bus, smartArt) = CreateDocument(hierarchy);

        bus.Execute(new MutateSmartArtStructureCommand(0, 0, SmartArtStructureOperation.Promote));
        smartArt.Nodes.Select(node => node.Text).Should().Equal("Root", "Child");
        smartArt.Nodes[0].Children.Should().BeEmpty();
        bus.Undo().Should().BeTrue();
        smartArt.Nodes.Should().ContainSingle();
        smartArt.Nodes[0].Children.Should().ContainSingle().Which.Text.Should().Be("Child");

        smartArt.Nodes.Add(new SmartArtNode("Sibling"));
        bus.Execute(new MutateSmartArtStructureCommand(0, 0, SmartArtStructureOperation.Demote));
        smartArt.Nodes.Should().ContainSingle();
        smartArt.Nodes[0].Children.Select(node => node.Text).Should().Equal("Child", "Sibling");
        bus.Undo().Should().BeTrue();
        smartArt.Nodes.Select(node => node.Text).Should().Equal("Root", "Sibling");
    }

    [Fact]
    public void EditTextAndStyle_UseSharedCommandsAndPreserveUnrelatedState()
    {
        var (bus, smartArt) = CreateDocument(SmartArt.Create(SmartArtKind.Process, ["A", "B"]));
        StampUnrelatedState(smartArt);
        var replacement = SmartArt.Create(SmartArtKind.List, ["One", "Two", "Three"]);

        bus.Execute(new ReplaceSmartArtContentCommand(0, 0, replacement));
        smartArt.Kind.Should().Be(SmartArtKind.List);
        smartArt.Nodes.Select(node => node.Text).Should().Equal("One", "Two", "Three");
        AssertUnrelatedState(smartArt);
        bus.Undo().Should().BeTrue();
        smartArt.Kind.Should().Be(SmartArtKind.Process);
        smartArt.Nodes.Select(node => node.Text).Should().Equal("A", "B");

        bus.Execute(new SetSmartArtStyleCommand(0, 0, SmartArtStyle.Catalog[3].Id));
        smartArt.StyleId.Should().Be(SmartArtStyle.Catalog[3].Id);
        smartArt.Nodes.Select(node => node.Text).Should().Equal("A", "B");
        bus.Undo().Should().BeTrue();
        smartArt.StyleId.Should().Be("flat1");
        bus.Redo().Should().BeTrue();
        smartArt.StyleId.Should().Be(SmartArtStyle.Catalog[3].Id);
    }

    [Fact]
    public void LayoutAndColorCommands_AreUndoableAndPreserveOtherSmartArtState()
    {
        var (bus, smartArt) = CreateDocument(SmartArt.Create(SmartArtKind.List, ["A", "B"]));
        StampUnrelatedState(smartArt);
        var originalNodes = smartArt.Nodes.Select(node => node.Text).ToArray();

        bus.Execute(new SetSmartArtLayoutCommand(0, 0, SmartArtKind.Hierarchy, "hierarchy1"));
        smartArt.Kind.Should().Be(SmartArtKind.Hierarchy);
        smartArt.LayoutId.Should().Be("hierarchy1");
        smartArt.ColorSchemeId.Should().Be("colorful2");
        smartArt.StyleId.Should().Be("flat1");
        smartArt.Nodes.Select(node => node.Text).Should().Equal(originalNodes);
        bus.Undo().Should().BeTrue();
        smartArt.Kind.Should().Be(SmartArtKind.List);
        smartArt.LayoutId.Should().Be("process1");
        bus.Redo().Should().BeTrue();
        smartArt.LayoutId.Should().Be("hierarchy1");
        bus.Undo().Should().BeTrue();

        bus.Execute(new SetSmartArtColorCommand(0, 0, "accent1_2"));
        smartArt.ColorSchemeId.Should().Be("accent1_2");
        smartArt.LayoutId.Should().Be("process1");
        smartArt.StyleId.Should().Be("flat1");
        smartArt.Nodes.Select(node => node.Text).Should().Equal(originalNodes);
        bus.Undo().Should().BeTrue();
        smartArt.ColorSchemeId.Should().Be("colorful2");
        bus.Redo().Should().BeTrue();
        smartArt.ColorSchemeId.Should().Be("accent1_2");
    }

    private static (DocumentCommandBus Bus, SmartArt SmartArt) CreateDocument(SmartArt smartArt)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromSmartArt(smartArt));
        document.Blocks.Add(paragraph);
        return (new DocumentCommandBus(new Context(document)), smartArt);
    }

    private static void StampUnrelatedState(SmartArt smartArt)
    {
        smartArt.WidthPt = 420;
        smartArt.HeightPt = 240;
        smartArt.LayoutId = "process1";
        smartArt.ColorSchemeId = "colorful2";
        smartArt.StyleId = "flat1";
        smartArt.Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = 12,
            VerticalOffsetPt = 18,
            ZOrderIndex = 4,
        };
    }

    private static void AssertUnrelatedState(SmartArt smartArt)
    {
        smartArt.WidthPt.Should().Be(420);
        smartArt.HeightPt.Should().Be(240);
        smartArt.LayoutId.Should().Be("process1");
        smartArt.ColorSchemeId.Should().Be("colorful2");
        smartArt.StyleId.Should().Be("flat1");
        smartArt.Placement.Should().NotBeNull();
        smartArt.Placement!.HorizontalOffsetPt.Should().Be(12);
        smartArt.Placement.VerticalOffsetPt.Should().Be(18);
        smartArt.Placement.ZOrderIndex.Should().Be(4);
    }
}
