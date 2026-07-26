using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_EvaluatesCommonSummaryFunctions()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "L8"),
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Average", "average"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Min", "min"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Max", "max"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Product", "product"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Count Numbers", "countNums"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Average");
        Text(sheet, "J2").Should().Be("Count Numbers");
        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(12.5);
        Number(sheet, "G3").Should().Be(10);
        Number(sheet, "H3").Should().Be(15);
        Number(sheet, "I3").Should().Be(150);
        Number(sheet, "J3").Should().Be(2);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(17.5);
        Number(sheet, "J5").Should().Be(4);
    }

    [Fact]
    public void Refresh_EvaluatesStatisticalSummaryFunctions()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "L8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "StdDev", "stdDev"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "StdDevp", "stdDevP"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Var", "var"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Varp", "varP"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(Math.Sqrt(12.5d), 0.0000001);
        Number(sheet, "G3").Should().Be(2.5);
        Number(sheet, "H3").Should().Be(12.5);
        Number(sheet, "I3").Should().Be(6.25);
        Number(sheet, "F5").Should().BeApproximately(Math.Sqrt(125d / 3d), 0.0000001);
        Number(sheet, "G5").Should().BeApproximately(Math.Sqrt(31.25d), 0.0000001);
        Number(sheet, "H5").Should().BeApproximately(125d / 3d, 0.0000001);
        Number(sheet, "I5").Should().Be(31.25);
    }

}
