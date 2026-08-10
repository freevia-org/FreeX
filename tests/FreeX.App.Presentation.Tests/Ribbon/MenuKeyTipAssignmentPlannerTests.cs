using Free.Shared.Ribbon.KeyTips;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Ribbon;

public sealed class MenuKeyTipAssignmentPlannerTests
{
    [Fact]
    public void AssignUnique_PreservesValidAuthoredTipsAndRepairsConflicts()
    {
        var assignments = MenuKeyTipAssignmentPlanner.AssignUnique(
        [
            new("_Copy", " c "),
            new("_Clear", "C"),
            new("Paste", "!"),
        ]);

        assignments.Should().Equal("C", "L", "P");
    }

    [Fact]
    public void AssignUnique_EnforcesPrefixSafeCollectionScope()
    {
        var assignments = MenuKeyTipAssignmentPlanner.AssignUnique(
        [
            new("Alpha", "A"),
            new("Alpha Extended", "AB"),
            new("Another"),
        ]);

        assignments.Should().OnlyHaveUniqueItems();
        RibbonKeyTipText.IsAvailable(assignments[0], assignments.Skip(1)).Should().BeTrue();
        RibbonKeyTipText.IsAvailable(assignments[1], assignments.Where((_, index) => index != 1)).Should().BeTrue();
        RibbonKeyTipText.IsAvailable(assignments[2], assignments.Take(2)).Should().BeTrue();
    }
}
