using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class MergedSelectionRangePlannerTests
{
    [Fact]
    public void ExpandToFullyContainMerges_absorbs_transitively_intersecting_regions()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.AddMergedRegion(Range(sheet, 2, 2, 3, 3));
        sheet.AddMergedRegion(Range(sheet, 3, 3, 4, 4));

        var expanded = MergedSelectionRangePlanner.ExpandToFullyContainMerges(
            sheet,
            Range(sheet, 2, 1, 2, 2));

        expanded.Should().Be(Range(sheet, 2, 1, 4, 4));
    }

    [Fact]
    public void ExpandToFullyContainMerges_leaves_contained_and_disjoint_regions_unchanged()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.AddMergedRegion(Range(sheet, 2, 2, 3, 3));
        sheet.AddMergedRegion(Range(sheet, 8, 8, 9, 9));
        var selection = Range(sheet, 1, 1, 4, 4);

        MergedSelectionRangePlanner.ExpandToFullyContainMerges(sheet, selection)
            .Should().Be(selection);
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
