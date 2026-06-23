using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageBreakDialogPlannerTests
{
    [Fact]
    public void CreateClearResult_RepresentsClearAll()
    {
        PageBreakDialogPlanner.CreateClearResult()
            .Should()
            .Be(new PageBreakDialogResult(PageBreakDialogAction.Clear, null, null));
    }

    [Fact]
    public void TryCreateResult_ParsesRowAndColumnBreaks()
    {
        PageBreakDialogPlanner.TryCreateResult(" row 12 ", out var rowResult).Should().BeTrue();
        PageBreakDialogPlanner.TryCreateResult(" column 5 ", out var columnResult).Should().BeTrue();
        PageBreakDialogPlanner.TryCreateResult(" column C ", out var letterColumnResult).Should().BeTrue();

        rowResult.Should().Be(new PageBreakDialogResult(PageBreakDialogAction.AddRow, 12, null));
        columnResult.Should().Be(new PageBreakDialogResult(PageBreakDialogAction.AddColumn, null, 5));
        letterColumnResult.Should().Be(new PageBreakDialogResult(PageBreakDialogAction.AddColumn, null, 3));
    }

    [Theory]
    [InlineData("row 0")]
    [InlineData("row 1")]
    [InlineData("row 1048577")]
    [InlineData("col 0")]
    [InlineData("col 1")]
    [InlineData("col A")]
    [InlineData("col 16385")]
    [InlineData("column 0")]
    [InlineData("column A")]
    [InlineData("column XFE")]
    public void TryCreateResult_RejectsInvalidBreakEntries(string input)
    {
        PageBreakDialogPlanner.TryCreateResult(input, out _).Should().BeFalse();
    }

    [Fact]
    public void TryCreateResult_WithDialogAction_UsesSelectedActionInput()
    {
        PageBreakDialogPlanner
            .TryCreateResult(PageBreakDialogAction.AddRow, rowInput: "9", columnInput: "C", out var rowResult)
            .Should()
            .BeTrue();
        PageBreakDialogPlanner
            .TryCreateResult(PageBreakDialogAction.AddColumn, rowInput: "9", columnInput: "C", out var columnResult)
            .Should()
            .BeTrue();

        rowResult.Should().Be(PageBreakDialogPlanner.CreateRowResult(9));
        columnResult.Should().Be(PageBreakDialogPlanner.CreateColumnResult(3));
    }

    [Fact]
    public void BuildDefaultInput_UsesSelectionShape()
    {
        var sheetId = default(SheetId);

        PageBreakDialogPlanner.BuildDefaultInput(null).Should().Be("row 2");
        PageBreakDialogPlanner.BuildDefaultInput(Range(sheetId, 12, 4, 12, 4)).Should().Be("row 12");
        PageBreakDialogPlanner.BuildDefaultInput(
                Range(sheetId, 1, 5, CellAddress.MaxRow, 5))
            .Should()
            .Be("column 5");
    }

    [Fact]
    public void PlanPageBreaks_ClearResetsAllBreaks()
    {
        var plan = PageBreakDialogPlanner.PlanPageBreaks(
            PageBreakDialogPlanner.CreateClearResult(),
            existingRowBreaks: [4u, 8u],
            existingColumnBreaks: [3u]);

        plan.RowBreaks.Should().BeEmpty();
        plan.ColumnBreaks.Should().BeEmpty();
    }

    [Fact]
    public void PlanPageBreaks_AddsDialogBreakAndPreservesSortedExistingBreaks()
    {
        var rowPlan = PageBreakDialogPlanner.PlanPageBreaks(
            PageBreakDialogPlanner.CreateRowResult(6),
            existingRowBreaks: [10u, 4u],
            existingColumnBreaks: [3u]);
        var columnPlan = PageBreakDialogPlanner.PlanPageBreaks(
            PageBreakDialogPlanner.CreateColumnResult(7),
            existingRowBreaks: [4u],
            existingColumnBreaks: [9u, 3u]);

        rowPlan.RowBreaks.Should().Equal(4u, 6u, 10u);
        rowPlan.ColumnBreaks.Should().Equal(3u);
        columnPlan.RowBreaks.Should().Equal(4u);
        columnPlan.ColumnBreaks.Should().Equal(3u, 7u, 9u);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
}
