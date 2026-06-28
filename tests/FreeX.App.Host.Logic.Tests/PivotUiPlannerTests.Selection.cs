using FluentAssertions;
using FreeX.Core.Model;
using PivotFieldListPanePlan = FreeX.App.Presentation.PivotUI.PivotFieldListPanePlan;
using PivotShowDetailsTarget = FreeX.App.Presentation.PivotUI.PivotShowDetailsTarget;

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
    public void FindPivotTableContainingCell_UsesRenderedPivotFootprint()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var pivot = CreatePivot("Pivot", 5, sheet.Id);
        pivot.LastRenderedRange = new GridRange(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 2));
        sheet.PivotTables.Add(pivot);

        PivotUiPlanner.FindPivotTableContainingCell(sheet, new CellAddress(sheet.Id, 6, 2))
            .Should()
            .BeSameAs(pivot);
        PivotUiPlanner.FindPivotTableContainingCell(sheet, new CellAddress(sheet.Id, 9, 4))
            .Should()
            .BeNull("the context menu should only expose PivotTable commands for rendered PivotTable cells");
    }

    [Fact]
    public void CreateFieldListPanePlan_ShowsOnlyWhenActiveCellIsInsidePivot()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var pivot = CreatePivot("Pivot", 5, sheet.Id);
        sheet.PivotTables.Add(pivot);

        PivotUiPlanner.CreateFieldListPanePlan(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1)))
            .Should()
            .Be(new PivotFieldListPanePlan(pivot));

        PivotUiPlanner.CreateFieldListPanePlan(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 6, 2)))
            .Should()
            .Be(new PivotFieldListPanePlan(null),
                "a range overlapping a PivotTable should not show the fields pane when the active cell starts outside the PivotTable");

        PivotUiPlanner.CreateFieldListPanePlan(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 20, 1), new CellAddress(sheet.Id, 20, 1)))
            .Should()
            .Be(new PivotFieldListPanePlan(null));
    }

    [Fact]
    public void CreateFieldListPanePlan_HidesForMissingSheetOrSelection()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.PivotTables.Add(CreatePivot("Pivot", 5, sheet.Id));

        PivotUiPlanner.CreateFieldListPanePlan(null, null).Should().Be(new PivotFieldListPanePlan(null));
        PivotUiPlanner.CreateFieldListPanePlan(sheet, null).Should().Be(new PivotFieldListPanePlan(null));
    }

    [Fact]
    public void CreateFieldListPanePlan_UsesRenderedPivotFootprintWhenAvailable()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var pivot = CreatePivot("Pivot", 5, sheet.Id);
        pivot.LastRenderedRange = new GridRange(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 2));
        sheet.PivotTables.Add(pivot);

        PivotUiPlanner.VisiblePivotRange(pivot).Should().Be(pivot.LastRenderedRange);

        PivotUiPlanner.CreateFieldListPanePlan(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 6, 2), new CellAddress(sheet.Id, 6, 2)))
            .Should()
            .Be(new PivotFieldListPanePlan(pivot));

        PivotUiPlanner.CreateFieldListPanePlan(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 9, 4), new CellAddress(sheet.Id, 9, 4)))
            .Should()
            .Be(new PivotFieldListPanePlan(null),
                "cells inside the static target but outside the rendered footprint are no longer active PivotTable cells");
    }

    [Fact]
    public void ReconcileSelectionAfterPivotResize_ClampsSelectionThatFallsOutsideNewFootprint()
    {
        var sheetId = SheetId.New();
        var previous = new GridRange(new CellAddress(sheetId, 5, 4), new CellAddress(sheetId, 10, 8));
        var updated = new GridRange(new CellAddress(sheetId, 5, 4), new CellAddress(sheetId, 7, 5));
        var selected = new GridRange(new CellAddress(sheetId, 10, 8), new CellAddress(sheetId, 10, 8));

        PivotUiPlanner.ReconcileSelectionAfterPivotResize(previous, updated, selected)
            .Should()
            .Be(new CellAddress(sheetId, 7, 5));
    }

    [Fact]
    public void ReconcileSelectionAfterPivotResize_DoesNotMoveIntentionalOutsideSelection()
    {
        var sheetId = SheetId.New();
        var previous = new GridRange(new CellAddress(sheetId, 5, 4), new CellAddress(sheetId, 10, 8));
        var updated = new GridRange(new CellAddress(sheetId, 5, 4), new CellAddress(sheetId, 7, 5));
        var selected = new GridRange(new CellAddress(sheetId, 20, 1), new CellAddress(sheetId, 20, 1));

        PivotUiPlanner.ReconcileSelectionAfterPivotResize(previous, updated, selected)
            .Should()
            .BeNull("outside user selections must continue to hide the pane instead of being pulled into the pivot");
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
