using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotUiPlannerTests
{
    [Fact]
    public void FieldCaptionAndIndexes_UseSourceHeadersAndDataFields()
    {
        var pivot = CreatePivot();
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotUiPlanner.FieldCaption(["Region", "Amount"], 1).Should().Be("Amount");
        PivotUiPlanner.FieldCaption(["Region"], 2).Should().Be("Column 3");
        PivotUiPlanner.FindSourceFieldIndex(["Region", "Quarter", "Amount"], "quarter").Should().Be(1);
        PivotUiPlanner.FindDataFieldIndex(pivot, "sum OF amount").Should().Be(0);
        PivotUiPlanner.FindFieldSourceIndex(["Region"], pivot, "Sum of Amount").Should().Be(2);
    }

    [Fact]
    public void SelectionPlans_UseRenderedPivotFootprint()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var pivot = CreatePivot("Pivot", 5, sheet.Id);
        pivot.LastRenderedRange = new GridRange(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 2));
        sheet.PivotTables.Add(pivot);

        PivotUiPlanner.VisiblePivotRange(pivot).Should().Be(pivot.LastRenderedRange);
        PivotUiPlanner.FindPivotTableContainingCell(sheet, new CellAddress(sheet.Id, 6, 2))
            .Should()
            .BeSameAs(pivot);
        PivotUiPlanner.CreateFieldListPanePlan(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 6, 2), new CellAddress(sheet.Id, 6, 2)))
            .Should()
            .Be(new PivotFieldListPanePlan(pivot));
        PivotUiPlanner.CreateFieldListPanePlan(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 9, 4), new CellAddress(sheet.Id, 9, 4)))
            .Should()
            .Be(new PivotFieldListPanePlan(null));
    }

    [Fact]
    public void ResolveShowDetailsTarget_UsesSelectedRangeStartInsidePivot()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var pivot = CreatePivot("Pivot", 5, sheet.Id);
        sheet.PivotTables.Add(pivot);
        var start = new CellAddress(sheet.Id, 6, 2);

        PivotUiPlanner.ResolveShowDetailsTarget(
                sheet,
                new GridRange(start, new CellAddress(sheet.Id, 8, 4)))
            .Should()
            .Be(new PivotShowDetailsTarget("Pivot", start));
    }

    [Theory]
    [InlineData(PivotHeaderArea.Row, "Row")]
    [InlineData(PivotHeaderArea.Column, "Column")]
    [InlineData(PivotHeaderArea.Page, "Page")]
    [InlineData(PivotHeaderArea.Value, null)]
    public void FieldSelectionState_ReadsAndUpdatesOnlyRequestedArea(
        PivotHeaderArea area,
        string? expectedSelection)
    {
        var pivot = CreatePivot();
        pivot.RowFields.Add(new PivotFieldModel(1, SelectedItem: "Row"));
        pivot.ColumnFields.Add(new PivotFieldModel(1, SelectedItem: "Column"));
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Page"));

        var state = PivotUiPlanner.CreateFieldSelectionState(pivot, area, 1);
        var updated = state.WithSelectedItems(["Only"]);

        if (expectedSelection is null)
            state.SelectedItems.Should().BeEmpty();
        else
            state.SelectedItems.Should().Equal(expectedSelection);
        state.HasStoredSelection.Should().Be(area != PivotHeaderArea.Value);
        if (area == PivotHeaderArea.Value)
            updated.SelectedItems.Should().BeEmpty();
        else
            updated.SelectedItems.Should().Equal("Only");
        updated.HasStoredSelection.Should().Be(area != PivotHeaderArea.Value);
        AssertSelection(updated.RowFields.Single(), area == PivotHeaderArea.Row, "Row");
        AssertSelection(updated.ColumnFields.Single(), area == PivotHeaderArea.Column, "Column");
        AssertSelection(updated.PageFields.Single(), area == PivotHeaderArea.Page, "Page");
    }

    [Fact]
    public void FieldSelectionState_PreservesMultiSelectionAndClearsBothSelectionForms()
    {
        var pivot = CreatePivot();
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Old"));

        var selected = PivotUiPlanner
            .CreateFieldSelectionState(pivot, PivotHeaderArea.Page, 1)
            .WithSelectedItems(["Q1", "Q2"]);
        var cleared = selected.WithSelectedItems(null);

        selected.SelectedItems.Should().Equal("Q1", "Q2");
        selected.HasStoredSelection.Should().BeTrue();
        selected.PageFields.Single().SelectedItem.Should().BeNull();
        selected.PageFields.Single().SelectedItems.Should().Equal("Q1", "Q2");
        cleared.SelectedItems.Should().BeEmpty();
        cleared.HasStoredSelection.Should().BeFalse();
        cleared.PageFields.Single().SelectedItem.Should().BeNull();
        cleared.PageFields.Single().SelectedItems.Should().BeNull();
    }

    [Fact]
    public void ResolvePivotChartFieldArea_PrefersPageThenColumnForRepeatedSourceFields()
    {
        var pivot = CreatePivot();
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.PageFields.Add(new PivotFieldModel(1));

        PivotUiPlanner.ResolvePivotChartFieldArea(pivot, 1).Should().Be(PivotHeaderArea.Page);

        pivot.PageFields.Clear();
        PivotUiPlanner.ResolvePivotChartFieldArea(pivot, 1).Should().Be(PivotHeaderArea.Column);
    }

    [Fact]
    public void ResolvePivotChartFieldArea_UsesRowForRowOrUnassignedSourceFields()
    {
        var pivot = CreatePivot();
        pivot.RowFields.Add(new PivotFieldModel(1));

        PivotUiPlanner.ResolvePivotChartFieldArea(pivot, 1).Should().Be(PivotHeaderArea.Row);
        PivotUiPlanner.ResolvePivotChartFieldArea(pivot, 2).Should().Be(PivotHeaderArea.Row);
    }

    [Fact]
    public void CreateDefaultDataField_UsesSumForNumericSourceAndCountForTextSource()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var pivot = CreatePivot(sheetId: sheet.Id);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        PivotUiPlanner.CreateDefaultDataField(sheet, pivot, ["Region", "Amount"], 1)
            .Should()
            .Be(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        PivotUiPlanner.CreateDefaultDataField(sheet, pivot, ["Region", "Amount"], 0)
            .Should()
            .Be(new PivotDataFieldModel(0, "Count of Region", "count"));
    }

    private static PivotTableModel CreatePivot(string name = "Pivot", uint targetRow = 5, SheetId? sheetId = null)
    {
        sheetId ??= SheetId.New();
        return new PivotTableModel
        {
            Name = name,
            SourceRange = new GridRange(new CellAddress(sheetId.Value, 1, 1), new CellAddress(sheetId.Value, 4, 4)),
            TargetRange = new GridRange(new CellAddress(sheetId.Value, targetRow, 1), new CellAddress(sheetId.Value, targetRow + 4, 4))
        };
    }

    private static void AssertSelection(PivotFieldModel field, bool updated, string originalSelection)
    {
        field.SelectedItem.Should().Be(updated ? "Only" : originalSelection);
        if (updated)
            field.SelectedItems.Should().Equal("Only");
        else
            field.SelectedItems.Should().BeNull();
    }
}
