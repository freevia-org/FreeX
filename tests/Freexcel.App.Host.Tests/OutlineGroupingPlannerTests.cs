using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class OutlineGroupingPlannerTests
{
    [Fact]
    public void GetNextOutlineLevel_UsesOneWhenSelectionHasNoExistingLevels()
    {
        OutlineGroupingPlanner.GetNextOutlineLevel(2, 5, new Dictionary<uint, int>())
            .Should()
            .Be(1);
    }

    [Fact]
    public void GetNextOutlineLevel_UsesOneMoreThanMaximumLevelInsideSelection()
    {
        var levels = new Dictionary<uint, int>
        {
            [1] = 7,
            [3] = 2,
            [4] = 4,
            [9] = 8
        };

        OutlineGroupingPlanner.GetNextOutlineLevel(2, 5, levels)
            .Should()
            .Be(5);
    }

    [Fact]
    public void GetNextOutlineLevel_ClampsToExcelMaximumOutlineLevel()
    {
        var levels = new Dictionary<uint, int> { [2] = 8 };

        OutlineGroupingPlanner.GetNextOutlineLevel(1, 3, levels)
            .Should()
            .Be(8);
    }
}
