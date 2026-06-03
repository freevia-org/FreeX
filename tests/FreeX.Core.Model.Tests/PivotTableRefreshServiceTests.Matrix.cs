using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_MaterializesColumnFieldMatrix()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "G2").Should().Be("Q2");
        Text(sheet, "H2").Should().Be("Grand Total");
        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(10);
        Number(sheet, "G3").Should().Be(15);
        Number(sheet, "H3").Should().Be(25);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(30);
        Number(sheet, "G5").Should().Be(40);
        Number(sheet, "H5").Should().Be(70);
    }

    [Fact]
    public void Refresh_MatrixUsesEmptyValueTextForMissingIntersections()
    {
        var workbook = new Workbook("PivotEmptyValueDisplayTest");
        var sheet = workbook.AddSheet("Data");
        SeedSparseSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "I7"),
            EmptyValueText = "N/A"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(10);
        Text(sheet, "G3").Should().Be("N/A");
        Text(sheet, "E4").Should().Be("West");
        Text(sheet, "F4").Should().Be("N/A");
        Number(sheet, "G4").Should().Be(25);
        Number(sheet, "H3").Should().Be(10);
        Number(sheet, "H4").Should().Be(25);
    }

    [Fact]
    public void Refresh_ColumnOnlyPivotShowsCacheItemsWithNoData()
    {
        var workbook = new Workbook("PivotShowNoDataColumnItemsTest");
        var sheet = workbook.AddSheet("Data");
        SeedSparseSalesData(sheet);
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            Fields =
            {
                new PivotCacheFieldModel("Region", SharedItems: ["East", "West"]),
                new PivotCacheFieldModel("Quarter", SharedItems: ["Q1", "Q2", "Q3"]),
                new PivotCacheFieldModel("Amount")
            }
        });
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "I5"),
            EmptyValueText = "N/A",
            ShowItemsWithNoDataOnColumns = true
        };
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Q1");
        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Q3");
        Text(sheet, "H2").Should().Be("Grand Total");
        Number(sheet, "E3").Should().Be(10);
        Number(sheet, "F3").Should().Be(25);
        Text(sheet, "G3").Should().Be("N/A");
        Number(sheet, "H3").Should().Be(35);
    }

    [Fact]
    public void Refresh_RowPivotShowsCacheItemsWithNoData()
    {
        var workbook = new Workbook("PivotShowNoDataRowItemsTest");
        var sheet = workbook.AddSheet("Data");
        SeedSparseSalesData(sheet);
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            Fields =
            {
                new PivotCacheFieldModel("Region", SharedItems: ["East", "North", "West"]),
                new PivotCacheFieldModel("Quarter", SharedItems: ["Q1", "Q2"]),
                new PivotCacheFieldModel("Amount")
            }
        });
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "G7"),
            EmptyValueText = "N/A",
            ShowItemsWithNoDataOnRows = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Sum of Amount");
        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(10);
        Text(sheet, "E4").Should().Be("North");
        Text(sheet, "F4").Should().Be("N/A");
        Text(sheet, "E5").Should().Be("West");
        Number(sheet, "F5").Should().Be(25);
        Text(sheet, "E6").Should().Be("Grand Total");
        Number(sheet, "F6").Should().Be(35);
    }

    [Fact]
    public void Refresh_MatrixPivotShowsCacheRowItemsWithNoData()
    {
        var workbook = new Workbook("PivotShowNoDataMatrixRowItemsTest");
        var sheet = workbook.AddSheet("Data");
        SeedSparseSalesData(sheet);
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            Fields =
            {
                new PivotCacheFieldModel("Region", SharedItems: ["East", "North", "West"]),
                new PivotCacheFieldModel("Quarter", SharedItems: ["Q1", "Q2"]),
                new PivotCacheFieldModel("Amount")
            }
        });
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "I7"),
            EmptyValueText = "N/A",
            ShowItemsWithNoDataOnRows = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "G2").Should().Be("Q2");
        Text(sheet, "H2").Should().Be("Grand Total");
        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(10);
        Text(sheet, "G3").Should().Be("N/A");
        Number(sheet, "H3").Should().Be(10);
        Text(sheet, "E4").Should().Be("North");
        Text(sheet, "F4").Should().Be("N/A");
        Text(sheet, "G4").Should().Be("N/A");
        Text(sheet, "H4").Should().Be("N/A");
        Text(sheet, "E5").Should().Be("West");
        Text(sheet, "F5").Should().Be("N/A");
        Number(sheet, "G5").Should().Be(25);
        Number(sheet, "H5").Should().Be(25);
    }

    [Fact]
    public void Refresh_RowPivotShowsEmptyTextForNoDataSubtotalGroups()
    {
        var workbook = new Workbook("PivotShowNoDataSubtotalItemsTest");
        var sheet = workbook.AddSheet("Data");
        SeedSparseSalesData(sheet);
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            Fields =
            {
                new PivotCacheFieldModel("Region", SharedItems: ["East", "North", "West"]),
                new PivotCacheFieldModel("Quarter", SharedItems: ["Q1", "Q2"]),
                new PivotCacheFieldModel("Amount")
            }
        });
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "H12"),
            EmptyValueText = "N/A",
            ShowItemsWithNoDataOnRows = true,
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E8").Should().Be("North Total");
        Text(sheet, "G8").Should().Be("N/A");
        Text(sheet, "E11").Should().Be("West Total");
        Number(sheet, "G11").Should().Be(25);
        Text(sheet, "E12").Should().Be("Grand Total");
        Number(sheet, "G12").Should().Be(35);
    }

    [Fact]
    public void Refresh_MaterializesNestedColumnFieldMatrix()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "M10")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Region");
        Text(sheet, "G2").Should().Be("Q1");
        Text(sheet, "H2").Should().Be("Q1");
        Text(sheet, "I2").Should().Be("Q2");
        Text(sheet, "J2").Should().Be("Q2");
        Text(sheet, "K2").Should().Be("Grand Total");
        Text(sheet, "G3").Should().Be("Retail");
        Text(sheet, "H3").Should().Be("Wholesale");
        Text(sheet, "I3").Should().Be("Retail");
        Text(sheet, "J3").Should().Be("Wholesale");
        Text(sheet, "F4").Should().Be("East");
        Number(sheet, "G4").Should().Be(10);
        Number(sheet, "H4").Should().Be(15);
        Number(sheet, "I4").Should().Be(20);
        Number(sheet, "J4").Should().Be(25);
        Number(sheet, "K4").Should().Be(70);
        Text(sheet, "F5").Should().Be("West");
        Number(sheet, "G5").Should().Be(30);
        Number(sheet, "H5").Should().Be(35);
        Number(sheet, "I5").Should().Be(40);
        Number(sheet, "J5").Should().Be(45);
        Number(sheet, "K5").Should().Be(150);
        Text(sheet, "F6").Should().Be("Grand Total");
        Number(sheet, "G6").Should().Be(40);
        Number(sheet, "H6").Should().Be(50);
        Number(sheet, "I6").Should().Be(60);
        Number(sheet, "J6").Should().Be(70);
        Number(sheet, "K6").Should().Be(220);
    }

    [Fact]
    public void Refresh_MaterializesMultipleRowAndDataFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I10")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Count of Amount", "count"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Quarter");
        Text(sheet, "G2").Should().Be("Sum of Amount");
        Text(sheet, "H2").Should().Be("Count of Amount");
        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Number(sheet, "G3").Should().Be(10);
        Number(sheet, "H3").Should().Be(1);
        Text(sheet, "E6").Should().Be("West");
        Text(sheet, "F6").Should().Be("Q2");
        Number(sheet, "G6").Should().Be(25);
        Number(sheet, "H6").Should().Be(1);
        Text(sheet, "E7").Should().Be("Grand Total");
        Number(sheet, "G7").Should().Be(70);
        Number(sheet, "H7").Should().Be(4);
    }

    [Fact]
    public void Refresh_MaterializesValuesOnlyPivot()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H5")
        };
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Count of Amount", "count"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Sum of Amount");
        Text(sheet, "F2").Should().Be("Count of Amount");
        Number(sheet, "E3").Should().Be(70);
        Number(sheet, "F3").Should().Be(4);
        sheet.GetCell(Addr(sheet, "E4")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MaterializesColumnOnlyPivot()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I5")
        };
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Q1");
        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Grand Total");
        Number(sheet, "E3").Should().Be(30);
        Number(sheet, "F3").Should().Be(40);
        Number(sheet, "G3").Should().Be(70);
        sheet.GetCell(Addr(sheet, "E4")).Should().BeNull();
    }

}
