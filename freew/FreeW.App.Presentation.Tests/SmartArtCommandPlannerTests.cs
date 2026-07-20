using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class SmartArtCommandPlannerTests
{
    [Fact]
    public void StatePlannerMatchesSharedStructuralCommandPreconditions()
    {
        var list = SmartArt.Create(SmartArtKind.List, ["A"]);
        SmartArtCommandPlanner.IsEnabled(list, SmartArtStructureOperation.AddShape).Should().BeTrue();
        SmartArtCommandPlanner.IsEnabled(list, SmartArtStructureOperation.RemoveShape).Should().BeFalse();
        SmartArtCommandPlanner.IsEnabled(list, SmartArtStructureOperation.Promote).Should().BeFalse();

        var hierarchy = new SmartArt { Kind = SmartArtKind.Hierarchy };
        hierarchy.Nodes.Add(new SmartArtNode("Root", [new SmartArtNode("Child")]));
        SmartArtCommandPlanner.IsEnabled(hierarchy, SmartArtStructureOperation.Promote).Should().BeTrue();
        SmartArtCommandPlanner.IsEnabled(hierarchy, SmartArtStructureOperation.Demote).Should().BeFalse();
    }

    [Fact]
    public void EditAndStylePlanningUseSharedCatalogValues()
    {
        var seed = SmartArt.Create(SmartArtKind.Process, ["A", "B"]);

        SmartArtCommandPlanner.BuildNodeText(seed).Should().Be($"A{Environment.NewLine}B");
        var result = SmartArtCommandPlanner.BuildEditedContent(SmartArtKind.List, " One\nTwo \n\n Three ");
        result.Should().NotBeNull();
        result!.Kind.Should().Be(SmartArtKind.List);
        result.Nodes.Select(node => node.Text).Should().Equal("One", "Two", "Three");
        SmartArtCommandPlanner.ResolveStyle(SmartArtStyle.Catalog[2].Name).Should().Be(SmartArtStyle.Catalog[2]);
        SmartArtCommandPlanner.StyleNames.Should().Equal(SmartArtStyle.Catalog.Select(style => style.Name));
    }
}
