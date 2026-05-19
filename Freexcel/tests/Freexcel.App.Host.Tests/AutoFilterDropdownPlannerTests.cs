using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class AutoFilterDropdownPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void TryPlan_ReturnsCurrentRegionAndColumnOffsetForHeaderCell()
    {
        var region = new GridRange(
            new CellAddress(SheetId, 2, 3),
            new CellAddress(SheetId, 10, 6));
        var activeCell = new CellAddress(SheetId, 2, 5);

        var planned = AutoFilterDropdownPlanner.TryPlan(region, activeCell, out var plan);

        planned.Should().BeTrue();
        plan.Range.Should().Be(region);
        plan.FilterColumnOffset.Should().Be(2);
    }

    [Theory]
    [InlineData(3u, 5u)]
    [InlineData(2u, 7u)]
    [InlineData(1u, 5u)]
    public void TryPlan_RejectsCellsOutsideHeaderRowOrRegion(uint row, uint col)
    {
        var region = new GridRange(
            new CellAddress(SheetId, 2, 3),
            new CellAddress(SheetId, 10, 6));

        AutoFilterDropdownPlanner.TryPlan(region, new CellAddress(SheetId, row, col), out _)
            .Should()
            .BeFalse();
    }
}
