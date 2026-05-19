using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class PivotUiPlannerTests
{
    [Fact]
    public void FieldCaption_FallsBackToOneBasedColumnNameWhenHeaderMissing()
    {
        PivotUiPlanner.FieldCaption(["Region", "Amount"], 1).Should().Be("Amount");
        PivotUiPlanner.FieldCaption(["Region"], 2).Should().Be("Column 3");
    }

    [Fact]
    public void FindFieldIndexes_SearchesHeadersAndDataFieldsCaseInsensitively()
    {
        var pivot = CreatePivot();
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotUiPlanner.FindSourceFieldIndex(["Region", "Quarter", "Amount"], "quarter").Should().Be(1);
        PivotUiPlanner.FindDataFieldIndex(pivot, "sum OF amount").Should().Be(0);
        PivotUiPlanner.FindFieldSourceIndex(["Region"], pivot, "Sum of Amount").Should().Be(2);
    }

    [Fact]
    public void ResolvePivotChartFieldButtonCaption_UsesValuesAxisPageOrDataFallback()
    {
        var pivot = CreatePivot();
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.PageFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));
        var headers = new[] { "Region", "Quarter", "Channel", "Amount" };

        PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivot, headers, "Values").Should().Be("Sum of Amount");
        PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivot, headers, "Axis Fields").Should().Be("Region");
        PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivot, headers, "Legend").Should().Be("Channel");
    }

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
    public void DefaultTargetRange_PlacesPivotTwoColumnsAfterSourceAndClampsToSheetEdges()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var source = new GridRange(new CellAddress(sheet.Id, 10, 16382), new CellAddress(sheet.Id, 20, 16384));

        var target = PivotUiPlanner.DefaultTargetRange(sheet, source);

        target.Start.Should().Be(new CellAddress(sheet.Id, 10, 16384));
        target.End.Should().Be(new CellAddress(sheet.Id, 23, 16384));
    }

    [Fact]
    public void GenerateUniquePivotTableName_SkipsExistingNamesCaseInsensitively()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.PivotTables.Add(CreatePivot("PivotTable1", sheetId: sheet.Id));
        sheet.PivotTables.Add(CreatePivot("pivottable2", sheetId: sheet.Id));

        PivotUiPlanner.GenerateUniquePivotTableName(sheet).Should().Be("PivotTable3");
    }

    [Fact]
    public void SetFieldSelectedItems_UpdatesMatchingFieldOnly()
    {
        var fields = new[]
        {
            new PivotFieldModel(0, SelectedItems: ["East"]),
            new PivotFieldModel(1)
        };

        var updated = PivotUiPlanner.SetFieldSelectedItems(fields, 1, ["Q1"]);

        updated[0].SelectedItems.Should().Equal("East");
        updated[1].SelectedItem.Should().Be("Q1");
        updated[1].SelectedItems.Should().Equal("Q1");
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
