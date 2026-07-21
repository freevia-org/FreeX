using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class OutlineGroupingServiceTests
{
    [Fact]
    public void GetGroupingAxis_ReturnsColumnsForWholeColumnSelection()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 2),
            new CellAddress(sheetId, CellAddress.MaxRow, 4));

        OutlineGroupingService.GetGroupingAxis(range).Should().Be(OutlineGroupingAxis.Columns);
    }

    [Fact]
    public void GetGroupingAxis_ReturnsRowsForNormalSelection()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 3, 2),
            new CellAddress(sheetId, 7, 4));

        OutlineGroupingService.GetGroupingAxis(range).Should().Be(OutlineGroupingAxis.Rows);
    }

    [Theory]
    [InlineData(0, 2, true, 1)]
    [InlineData(1, 2, true, 2)]
    // Excel/OOXML's ST_OutlineLevel maxes out at 7 (8 nested groups, levels 0-7): an 8th nested
    // Group at the already-deepest previous level (7) clamps at 7 rather than advancing to 8
    // (R58-outline-6-2).
    [InlineData(7, 7, true, 7)]
    [InlineData(1, 2, false, 2)]
    public void GetGroupedOutlineLevel_PreservesExistingHierarchyWhenRequested(
        int previousLevel,
        int requestedLevel,
        bool preserveExistingHierarchy,
        int expectedLevel)
    {
        OutlineGroupingService.GetGroupedOutlineLevel(previousLevel, requestedLevel, preserveExistingHierarchy)
            .Should()
            .Be(expectedLevel);
    }

    [Fact]
    public void ValidateOutlineLevel_AllowsExcelMaximumOfSevenButRejectsEight()
    {
        var sevenAct = () => OutlineGroupingService.ValidateOutlineLevel(7);
        sevenAct.Should().NotThrow();

        var eightAct = () => OutlineGroupingService.ValidateOutlineLevel(8);
        eightAct.Should().Throw<ArgumentOutOfRangeException>();
    }
}
