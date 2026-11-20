using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

/// <summary>
/// Excel: double-clicking an edge of the selection's border jumps to the edge of the data that way,
/// like Ctrl+Arrow.
/// </summary>
public sealed class GridSelectionBorderJumpTests
{
    private static readonly GridSelectionLayout Layout =
        new(new GridRect(100, 50, 80, 20), HasTopEdge: true, HasLeftEdge: true, HasBottomEdge: true, HasRightEdge: true);

    [Theory]
    [InlineData(140, 50, GridSelectionBorderEdge.Top)]
    [InlineData(140, 70, GridSelectionBorderEdge.Bottom)]
    [InlineData(100, 60, GridSelectionBorderEdge.Left)]
    [InlineData(180, 60, GridSelectionBorderEdge.Right)]
    [InlineData(140, 60, GridSelectionBorderEdge.None)]
    [InlineData(300, 300, GridSelectionBorderEdge.None)]
    public void HitBorderEdge_NamesTheNearestEdge(double x, double y, GridSelectionBorderEdge expected) =>
        GridSelectionMovePlanner.HitBorderEdge(Layout, selectedRanges: null, new GridPoint(x, y)).Should().Be(expected);

    [Fact]
    public void HitBorderEdge_IgnoresTheFillHandle() =>
        GridSelectionMovePlanner.HitBorderEdge(Layout, selectedRanges: null, new GridPoint(180, 70))
            .Should().Be(GridSelectionBorderEdge.None, "the bottom-right corner is the fill handle");

    [Fact]
    public void FindBorderJumpTarget_MovesLikeCtrlArrow()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        var sheet = workbook.Sheets[0];
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        var a1 = new CellAddress(sheet.Id, 1, 1);

        GridSelectionMovePlanner.FindBorderJumpTarget(sheet, a1, GridSelectionBorderEdge.Bottom)
            .Should().Be(new CellAddress(sheet.Id, 5, 1), "the bottom border jumps to the last filled cell below");
        GridSelectionMovePlanner.FindBorderJumpTarget(sheet, new CellAddress(sheet.Id, 5, 1), GridSelectionBorderEdge.Top)
            .Should().Be(a1);
        GridSelectionMovePlanner.FindBorderJumpTarget(sheet, a1, GridSelectionBorderEdge.Left)
            .Should().Be(a1, "there is nowhere further left");
    }
}
