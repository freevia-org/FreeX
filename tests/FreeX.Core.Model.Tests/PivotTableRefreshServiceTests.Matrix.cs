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
            TargetRange = Range(sheet, "E2", "I7"),
            // R90-render-pivot-layout-5-3: pin the (former) Tabular default -- this test expects the
            // row field's own name as the header, not the new Compact "Row Labels" default.
            ReportLayout = PivotReportLayout.Tabular
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
    public void Refresh_ClearsPreviousPivotFootprintWhenMatrixShrinks()
    {
        var workbook = new Workbook("PivotShrinkClearingTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7"),
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        sheet.SetCell(Addr(sheet, "I7"), new TextValue("Outside pivot"));

        pivot.ColumnFields.Clear();
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Sum of Amount");
        sheet.GetCell(Addr(sheet, "G2")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "H2")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "G5")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "H5")).Should().BeNull();
        Text(sheet, "I7").Should().Be("Outside pivot");
        pivot.LastRenderedRange.Should().Be(Range(sheet, "E2", "F5"));
    }

    [Fact]
    public void Refresh_MatrixUsesCustomGrandTotalCaptionForHeadersRowsAndDetails()
    {
        var workbook = new Workbook("PivotMatrixCustomGrandTotalCaptionTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7"),
            GrandTotalCaption = "Overall Total"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "H2").Should().Be("Overall Total");
        Text(sheet, "E5").Should().Be("Overall Total");
        var rowGrandTotalDetail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "H3"));
        rowGrandTotalDetail.Rows.Select(row => string.Join("|", row.Select(PivotValueText)))
            .Should().BeEquivalentTo(["East|Q1|10", "East|Q2|15"]);
        var columnGrandTotalDetail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F5"));
        columnGrandTotalDetail.Rows.Select(row => string.Join("|", row.Select(PivotValueText)))
            .Should().BeEquivalentTo(["East|Q1|10", "West|Q1|20"]);
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
            ShowItemsWithNoDataOnRows = true,
            ReportLayout = PivotReportLayout.Tabular
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
            ShowItemsWithNoDataOnRows = true,
            ReportLayout = PivotReportLayout.Tabular
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
            ShowSubtotals = true,
            // R90-render-pivot-layout-5-1/5-3: pin the (former) Bottom/Tabular defaults this test's
            // column layout (value column at G) and "Total after group" assertions were written against.
            SubtotalPlacement = PivotSubtotalPlacement.Bottom,
            ReportLayout = PivotReportLayout.Tabular
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
            TargetRange = Range(sheet, "F2", "M10"),
            ReportLayout = PivotReportLayout.Tabular,
            // R90-render-pivot-layout-5-1: pin the (former) no-subtotal default -- this 2-column-field
            // test's column layout assumes no subtotal columns.
            ShowSubtotals = false
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
    public void Refresh_NestedColumnFieldMatrixWithSubtotals_EmitsSubtotalColumns()
    {
        // Ground-truth: Region(A)/Quarter(B)/Channel(C)/Amount(D), ShowSubtotals=true.
        // Column slots: Q1/Retail, Q1/Wholesale, [Q1 Total], Q2/Retail, Q2/Wholesale, [Q2 Total], Grand Total
        // Columns G..M (7 value columns + F row-label = columns F..M).
        var workbook = new Workbook("PivotNestedColumnSubtotalsTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "N10"),
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Header row 1 (outer quarter groupings)
        Text(sheet, "F2").Should().Be("Region");
        Text(sheet, "G2").Should().Be("Q1");   // Q1/Retail
        Text(sheet, "H2").Should().Be("Q1");   // Q1/Wholesale
        Text(sheet, "I2").Should().Be("Q1 Total"); // Q1 subtotal column
        Text(sheet, "J2").Should().Be("Q2");   // Q2/Retail
        Text(sheet, "K2").Should().Be("Q2");   // Q2/Wholesale
        Text(sheet, "L2").Should().Be("Q2 Total"); // Q2 subtotal column
        Text(sheet, "M2").Should().Be("Grand Total");

        // Header row 2 (inner channel labels; subtotal cols have blank channel header)
        Text(sheet, "G3").Should().Be("Retail");
        Text(sheet, "H3").Should().Be("Wholesale");
        Text(sheet, "I3").Should().Be("");      // subtotal column — no channel label
        Text(sheet, "J3").Should().Be("Retail");
        Text(sheet, "K3").Should().Be("Wholesale");
        Text(sheet, "L3").Should().Be("");      // subtotal column — no channel label

        // East data row
        Text(sheet, "F4").Should().Be("East");
        Number(sheet, "G4").Should().Be(10);   // Q1/Retail
        Number(sheet, "H4").Should().Be(15);   // Q1/Wholesale
        Number(sheet, "I4").Should().Be(25);   // Q1 Total for East
        Number(sheet, "J4").Should().Be(20);   // Q2/Retail
        Number(sheet, "K4").Should().Be(25);   // Q2/Wholesale
        Number(sheet, "L4").Should().Be(45);   // Q2 Total for East
        Number(sheet, "M4").Should().Be(70);   // Grand Total for East

        // West data row
        Text(sheet, "F5").Should().Be("West");
        Number(sheet, "G5").Should().Be(30);   // Q1/Retail
        Number(sheet, "H5").Should().Be(35);   // Q1/Wholesale
        Number(sheet, "I5").Should().Be(65);   // Q1 Total for West
        Number(sheet, "J5").Should().Be(40);   // Q2/Retail
        Number(sheet, "K5").Should().Be(45);   // Q2/Wholesale
        Number(sheet, "L5").Should().Be(85);   // Q2 Total for West
        Number(sheet, "M5").Should().Be(150);  // Grand Total for West

        // Grand Total row
        Text(sheet, "F6").Should().Be("Grand Total");
        Number(sheet, "G6").Should().Be(40);   // Q1/Retail grand
        Number(sheet, "H6").Should().Be(50);   // Q1/Wholesale grand
        Number(sheet, "I6").Should().Be(90);   // Q1 Total grand
        Number(sheet, "J6").Should().Be(60);   // Q2/Retail grand
        Number(sheet, "K6").Should().Be(70);   // Q2/Wholesale grand
        Number(sheet, "L6").Should().Be(130);  // Q2 Total grand
        Number(sheet, "M6").Should().Be(220);  // Grand Total grand
    }

    [Fact]
    public void Refresh_NestedColumnFieldMatrixShowSubtotalsFalse_NoSubtotalColumns()
    {
        // With ShowSubtotals=false the output must be identical to the no-subtotals layout:
        // leaf columns only (Q1/Retail, Q1/Wholesale, Q2/Retail, Q2/Wholesale) + Grand Total.
        var workbook = new Workbook("PivotNestedColumnNoSubtotalsTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "M10"),
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // No subtotal columns: G=Q1/Retail, H=Q1/Wholesale, I=Q2/Retail, J=Q2/Wholesale, K=Grand Total
        Text(sheet, "G2").Should().Be("Q1");
        Text(sheet, "H2").Should().Be("Q1");
        Text(sheet, "I2").Should().Be("Q2");
        Text(sheet, "J2").Should().Be("Q2");
        Text(sheet, "K2").Should().Be("Grand Total");
        Text(sheet, "G3").Should().Be("Retail");
        Text(sheet, "H3").Should().Be("Wholesale");
        Text(sheet, "I3").Should().Be("Retail");
        Text(sheet, "J3").Should().Be("Wholesale");
        // There must be no value at L2 (no subtotal column was emitted)
        sheet.GetCell(Addr(sheet, "L2")).Should().BeNull();

        Number(sheet, "G4").Should().Be(10);
        Number(sheet, "H4").Should().Be(15);
        Number(sheet, "I4").Should().Be(20);
        Number(sheet, "J4").Should().Be(25);
        Number(sheet, "K4").Should().Be(70);
        Number(sheet, "G5").Should().Be(30);
        Number(sheet, "H5").Should().Be(35);
        Number(sheet, "I5").Should().Be(40);
        Number(sheet, "J5").Should().Be(45);
        Number(sheet, "K5").Should().Be(150);
    }

    [Fact]
    public void Refresh_NestedColumnFieldMatrixWithSubtotals_PercentOfParentColumnTotalCorrect()
    {
        // With ShowSubtotals=true, the % of Parent Column Total for a leaf cell should use
        // the subtotal-column group as its parent (e.g. East/Q1/Retail uses Q1-East group = 25 as parent).
        var workbook = new Workbook("PivotNestedColumnSubtotalsParentColTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "N10"),
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "% Parent Col", "sum",
            ShowValuesAs: PivotShowValuesAs.PercentOfParentColumnTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // East/Q1/Retail=10, East/Q1/Wholesale=15. Parent column = Q1 restricted to East = 25.
        // So East/Q1/Retail % = 10/25; East/Q1/Wholesale % = 15/25.
        // G4 = East/Q1/Retail, H4 = East/Q1/Wholesale, I4 = East/Q1 subtotal
        Number(sheet, "G4").Should().BeApproximately(10d / 25d, 0.0000001);
        Number(sheet, "H4").Should().BeApproximately(15d / 25d, 0.0000001);
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
            TargetRange = Range(sheet, "E2", "I10"),
            // R90-render-pivot-layout-5-1/5-3: pin the (former) Tabular/no-subtotal defaults this
            // 2-row-field layout test was written against.
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = false
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
