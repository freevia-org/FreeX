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
        // Excel semantics: calculated field evaluated once per group using SUM of each
        // constituent source field: SUM(Amount)*SUM(Units) = 25*5 = 125
        Number(sheet, "G3").Should().Be(125);
        Text(sheet, "F4").Should().Be("West");
        // SUM(Amount)*SUM(Units) = 45*6.2 = 279
        Number(sheet, "G4").Should().Be(279);
        Text(sheet, "F5").Should().Be("Grand Total");
        // SUM(Amount)*SUM(Units) = 70*11.2 = 784
        Number(sheet, "G5").Should().Be(784);
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

    [Fact]
    public void Refresh_EvaluatesCalculatedItemsForColumnField()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I4")
        };
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(1, "Q1 + Q2", "Q1+Q2"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Q1");
        Number(sheet, "E3").Should().Be(30);
        Text(sheet, "F2").Should().Be("Q2");
        Number(sheet, "F3").Should().Be(40);
        Text(sheet, "G2").Should().Be("Q1 + Q2");
        Number(sheet, "G3").Should().Be(70);
        Text(sheet, "H2").Should().Be("Grand Total");
        Number(sheet, "H3").Should().Be(140);
    }

    [Fact]
    public void Refresh_EvaluatesCalculatedItemsForInnerRowField()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H10")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(1, "Q1 + Q2", "Q1+Q2"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Number(sheet, "G3").Should().Be(10);
        Text(sheet, "E4").Should().Be("East");
        Text(sheet, "F4").Should().Be("Q2");
        Number(sheet, "G4").Should().Be(15);
        Text(sheet, "E5").Should().Be("East");
        Text(sheet, "F5").Should().Be("Q1 + Q2");
        Number(sheet, "G5").Should().Be(25);
        Text(sheet, "E6").Should().Be("West");
        Text(sheet, "F6").Should().Be("Q1");
        Number(sheet, "G6").Should().Be(20);
        Text(sheet, "E7").Should().Be("West");
        Text(sheet, "F7").Should().Be("Q2");
        Number(sheet, "G7").Should().Be(25);
        Text(sheet, "E8").Should().Be("West");
        Text(sheet, "F8").Should().Be("Q1 + Q2");
        Number(sheet, "G8").Should().Be(45);
        Text(sheet, "E9").Should().Be("Grand Total");
        Number(sheet, "G9").Should().Be(140);
    }

}
