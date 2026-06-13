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
    [InlineData(7, 8, true, 8)]
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
}
