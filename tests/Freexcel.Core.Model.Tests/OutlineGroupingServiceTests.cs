using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

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
}
