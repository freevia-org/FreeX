using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class MultiRangeCopyPlannerTests
{
    private static readonly SheetId Sheet = SheetId.New();

    private static GridRange Range(uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(Sheet, r1, c1), new CellAddress(Sheet, r2, c2));

    [Fact]
    public void TryPlan_SameRows_CombinesSideBySideOrderedByColumn()
    {
        // C1:C2 then A1:A2 (out of column order) — same rows.
        var ranges = new[] { Range(1, 3, 2, 3), Range(1, 1, 2, 1) };

        MultiRangeCopyPlanner.TryPlan(ranges, out var layout).Should().BeTrue();
        layout!.Orientation.Should().Be(MultiRangeCopyOrientation.SideBySideColumns);
        layout.OrderedAreas.Select(a => a.Start.Col).Should().Equal(1u, 3u);
    }

    [Fact]
    public void TryPlan_SameColumns_CombinesStackedOrderedByRow()
    {
        // A3:B3 then A1:B1 (out of row order) — same columns.
        var ranges = new[] { Range(3, 1, 3, 2), Range(1, 1, 1, 2) };

        MultiRangeCopyPlanner.TryPlan(ranges, out var layout).Should().BeTrue();
        layout!.Orientation.Should().Be(MultiRangeCopyOrientation.StackedRows);
        layout.OrderedAreas.Select(a => a.Start.Row).Should().Equal(1u, 3u);
    }

    [Fact]
    public void TryPlan_DifferentRowsAndColumns_IsRejected()
    {
        var ranges = new[] { Range(1, 1, 2, 1), Range(3, 2, 4, 2) };

        MultiRangeCopyPlanner.TryPlan(ranges, out var layout).Should().BeFalse();
        layout.Should().BeNull();
    }

    [Fact]
    public void TryPlan_OverlappingColumnsWithSameRows_IsRejected()
    {
        var ranges = new[] { Range(1, 1, 2, 2), Range(1, 2, 2, 3) };

        MultiRangeCopyPlanner.TryPlan(ranges, out _).Should().BeFalse();
    }

    [Fact]
    public void TryPlan_IdenticalRanges_IsRejected()
    {
        var ranges = new[] { Range(1, 1, 2, 2), Range(1, 1, 2, 2) };

        MultiRangeCopyPlanner.TryPlan(ranges, out _).Should().BeFalse();
    }

    [Fact]
    public void TryPlan_SingleRange_IsRejected()
    {
        MultiRangeCopyPlanner.TryPlan(new[] { Range(1, 1, 2, 2) }, out _).Should().BeFalse();
    }

    [Fact]
    public void TryPlan_RangesOnDifferentSheets_IsRejected()
    {
        var other = SheetId.New();
        var ranges = new[]
        {
            Range(1, 1, 2, 1),
            new GridRange(new CellAddress(other, 1, 3), new CellAddress(other, 2, 3))
        };

        MultiRangeCopyPlanner.TryPlan(ranges, out _).Should().BeFalse();
    }
}
