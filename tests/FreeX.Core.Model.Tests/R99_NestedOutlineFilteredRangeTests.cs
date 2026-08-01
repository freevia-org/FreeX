using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R99 coverage for the shared outline/filter contract. Filter-owned visibility must survive
/// outline collapse/expand, while nested row and column levels remain independent of it.
/// </summary>
public sealed class R99_NestedOutlineFilteredRangeTests
{
    [Fact]
    public void ExpandOuterRowGroup_DoesNotResurrectRowsHiddenByActiveFilter()
    {
        var workbook = new Workbook("NestedOutlineFilteredRange");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 8; row++)
        {
            sheet.SetCell(
                new CellAddress(sheet.Id, row, 1),
                new TextValue(row is 3 or 5 ? "Drop" : "Keep"));
        }

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 8, 1));
        var context = new TestCommandContext(workbook);
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        new FilterCommand(sheet.Id, range, filterColOffset: 0, ["Keep"])
            .Apply(context)
            .Success
            .Should()
            .BeTrue();
        new GroupRowsCommand(sheet.Id, 2, 7, 1, preserveExistingHierarchy: true)
            .Apply(context)
            .Success
            .Should()
            .BeTrue();
        new GroupRowsCommand(sheet.Id, 3, 4, 2, preserveExistingHierarchy: true)
            .Apply(context)
            .Success
            .Should()
            .BeTrue();
        new GroupColumnsCommand(sheet.Id, 8, 12, 1, preserveExistingHierarchy: true)
            .Apply(context)
            .Success
            .Should()
            .BeTrue();
        new GroupColumnsCommand(sheet.Id, 9, 11, 2, preserveExistingHierarchy: true)
            .Apply(context)
            .Success
            .Should()
            .BeTrue();

        new CollapseRowGroupCommand(sheet.Id, 1).Apply(context).Success.Should().BeTrue();
        new ExpandRowGroupCommand(sheet.Id, 1).Apply(context).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
        sheet.GroupHiddenRows.Should().BeEmpty();
        sheet.RowOutlineLevels.Should().BeEquivalentTo(new Dictionary<uint, int>
        {
            [2] = 1,
            [3] = 2,
            [4] = 2,
            [5] = 1,
            [6] = 1,
            [7] = 1
        });
        sheet.ColOutlineLevels.Should().BeEquivalentTo(new Dictionary<uint, int>
        {
            [8] = 1,
            [9] = 2,
            [10] = 2,
            [11] = 2,
            [12] = 1
        });
    }
}
