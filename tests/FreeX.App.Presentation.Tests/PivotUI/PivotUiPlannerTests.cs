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

    [Fact]
    public void FieldMutation_IsSharedPortableLogic()
    {
        var updated = PivotUiPlanner.SetFieldSelectedItems([new PivotFieldModel(1)], 1, ["Q1"]);
        updated.Single().SelectedItem.Should().Be("Q1");
        updated.Single().SelectedItems.Should().Equal("Q1");
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
}
