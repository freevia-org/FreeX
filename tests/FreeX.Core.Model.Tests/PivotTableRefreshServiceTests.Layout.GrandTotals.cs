using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_HidesGrandTotalWhenDisabled()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8"),
            ShowGrandTotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "E4").Should().Be("West");
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "F5")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MatrixCanHideRowGrandTotalsWhileKeepingColumnGrandTotals()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I8"),
            ShowRowGrandTotals = false,
            ShowColumnGrandTotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "G2").Should().Be("Q2");
        sheet.GetCell(Addr(sheet, "H2")).Should().BeNull();
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(30);
        Number(sheet, "G5").Should().Be(40);
        sheet.GetCell(Addr(sheet, "H5")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MatrixCanHideColumnGrandTotalsWhileKeepingRowGrandTotals()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I8"),
            ShowRowGrandTotals = true,
            ShowColumnGrandTotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "H2").Should().Be("Grand Total");
        Number(sheet, "H3").Should().Be(25);
        Number(sheet, "H4").Should().Be(45);
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "F5")).Should().BeNull();
    }

}
