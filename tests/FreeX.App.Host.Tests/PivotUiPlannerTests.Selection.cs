using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotUiPlannerTests
{
    [Fact]
    public void FindPivotTableForSelection_PrefersContainingPivotAndFallsBackToFirst()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var first = CreatePivot("First", 2, sheet.Id);
        var second = CreatePivot("Second", 10, sheet.Id);
        sheet.PivotTables.Add(first);
        sheet.PivotTables.Add(second);

        PivotUiPlanner.FindPivotTableForSelection(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 10, 2), new CellAddress(sheet.Id, 10, 2)))
            .Should()
            .BeSameAs(second);

        PivotUiPlanner.FindPivotTableForSelection(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 100, 2), new CellAddress(sheet.Id, 100, 2)))
            .Should()
            .BeSameAs(first);
    }

    [Fact]
    public void FindPivotTableContainingSelection_ReturnsOnlyIntersectingPivot()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var first = CreatePivot("First", 2, sheet.Id);
        var second = CreatePivot("Second", 10, sheet.Id);
        sheet.PivotTables.Add(first);
        sheet.PivotTables.Add(second);

        PivotUiPlanner.FindPivotTableContainingSelection(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 10, 2), new CellAddress(sheet.Id, 10, 2)))
            .Should()
            .BeSameAs(second);

        PivotUiPlanner.FindPivotTableContainingSelection(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 100, 2), new CellAddress(sheet.Id, 100, 2)))
            .Should()
            .BeNull("Excel hides contextual PivotTable tabs when selection leaves the PivotTable body");
    }

    [Fact]
    public void ResolveShowDetailsTarget_ReturnsNullForMissingSheetSelectionOrOutsideSelection()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.PivotTables.Add(CreatePivot("Pivot", 5, sheet.Id));

        PivotUiPlanner.ResolveShowDetailsTarget(null, null).Should().BeNull();
        PivotUiPlanner.ResolveShowDetailsTarget(sheet, null).Should().BeNull();
        PivotUiPlanner.ResolveShowDetailsTarget(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 50, 1), new CellAddress(sheet.Id, 50, 1)))
            .Should()
            .BeNull();
    }

    [Fact]
    public void ResolveShowDetailsTarget_UsesSelectedRangeStartInsidePivot()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var pivot = CreatePivot("Pivot", 5, sheet.Id);
        sheet.PivotTables.Add(pivot);
        var start = new CellAddress(sheet.Id, 6, 2);
        var selected = new GridRange(start, new CellAddress(sheet.Id, 8, 4));

        var target = PivotUiPlanner.ResolveShowDetailsTarget(sheet, selected);

        target.Should().Be(new PivotShowDetailsTarget("Pivot", start));
    }

    [Fact]
    public void ResolveShowDetailsTarget_DoesNotUseOverlapWhenSelectionStartIsOutsidePivot()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.PivotTables.Add(CreatePivot("Pivot", 5, sheet.Id));
        var selected = new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 6, 2));

        PivotUiPlanner.ResolveShowDetailsTarget(sheet, selected).Should().BeNull();
    }
}
