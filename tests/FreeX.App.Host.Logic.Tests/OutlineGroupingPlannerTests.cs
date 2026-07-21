using FluentAssertions;
using FreeX.App.Presentation;

namespace FreeX.App.Host.Tests;

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
        // Excel/OOXML's ST_OutlineLevel maxes out at 7 (R58-outline-6-2): even when an existing
        // level in the selection is already at (or beyond) the cap, the next requested level must
        // not advance past 7.
        var levels = new Dictionary<uint, int> { [2] = 8 };

        OutlineGroupingPlanner.GetNextOutlineLevel(1, 3, levels)
            .Should()
            .Be(7);
    }

    [Fact]
    public void GetUngroupedOutlineLevel_DecrementsDeepestLevelWithoutGoingBelowZero()
    {
        OutlineGroupingPlanner.GetUngroupedOutlineLevel(
            2, 5, new Dictionary<uint, int> { [2] = 1, [3] = 3, [9] = 8 })
            .Should()
            .Be(2);

        OutlineGroupingPlanner.GetUngroupedOutlineLevel(1, 1, new Dictionary<uint, int>())
            .Should()
            .Be(0);
    }
}
