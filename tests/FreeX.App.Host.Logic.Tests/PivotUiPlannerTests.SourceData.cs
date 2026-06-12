using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotUiPlannerTests
{
    [Fact]
    public void ResolvePivotSourceSheet_UsesSourceSheetForCrossSheetPivot()
    {
        var workbook = new Workbook("CrossSheetPivotPlannerTest");
        var sourceSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("Pivot");
        var pivot = new PivotTableModel
        {
            Name = "Pivot",
            SourceRange = new GridRange(new CellAddress(sourceSheet.Id, 1, 1), new CellAddress(sourceSheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(pivotSheet.Id, 3, 1), new CellAddress(pivotSheet.Id, 8, 3))
        };
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 2), new NumberValue(42));

        var resolved = PivotUiPlanner.ResolvePivotSourceSheet(workbook, pivotSheet, pivot);

        resolved.Should().BeSameAs(sourceSheet);
        PivotUiPlanner.CreateDefaultDataField(resolved, pivot, ["Region", "Amount"], 1)
            .Should()
            .Be(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
    }

    [Fact]
    public void ChooseDefaultDataField_UsesFirstNumericOrDateColumnAfterHeader()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new DateTimeValue(46161));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(12));

        PivotUiPlanner.ChooseDefaultDataField(sheet, range).Should().Be(1);
    }

    [Fact]
    public void ChooseDefaultDataField_FallsBackToSecondColumnWhenNoNumericDataExists()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));

        PivotUiPlanner.ChooseDefaultDataField(sheet, range).Should().Be(1);
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
}
