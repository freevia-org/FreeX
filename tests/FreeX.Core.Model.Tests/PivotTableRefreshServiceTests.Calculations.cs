using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_EvaluatesCalculatedFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesWithUnitsData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "F2", "I8")
        };
        pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Revenue", "Amount*Units"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(-1, "Sum of Revenue", "sum", CalculatedFieldName: "Revenue"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Region");
        Text(sheet, "G2").Should().Be("Sum of Revenue");
        Text(sheet, "F3").Should().Be("East");
        Number(sheet, "G3").Should().Be(65);
        Text(sheet, "F4").Should().Be("West");
        Number(sheet, "G4").Should().Be(135);
        Text(sheet, "F5").Should().Be("Grand Total");
        Number(sheet, "G5").Should().Be(200);
    }

    [Fact]
    public void Refresh_EvaluatesCalculatedItemsForRowField()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(0, "East + West", "East+West"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(25);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("East + West");
        Number(sheet, "F5").Should().Be(70);
        Text(sheet, "E6").Should().Be("Grand Total");
        Number(sheet, "F6").Should().Be(140);
    }

}
